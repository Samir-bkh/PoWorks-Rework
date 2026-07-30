using Microsoft.AspNetCore.Mvc;
using Npgsql;
using NpgsqlTypes;
using PoWorks_Rework.Models;
using PoWorks_Rework.Repositories;
using PoWorks_Rework.Services;
using System.Text.Json;

namespace PoWorks_Rework.Controllers
{
    public class WebServicesImportController : Controller
    {
        #region Dependencies

        private readonly ILogger<WebServicesImportController> _logger;
        private readonly DatabaseService _databaseService;
        private readonly VariableBrowseParsingService _variableBrowseParsingService;
        private readonly TrendsService _trendsService;
        private readonly MeterRepository _meterRepository;
        private readonly PCVueWebService _pcvueWebService;
        private readonly ICompanyContext _companyContext;
        private readonly EncryptionService _encryptionService;

        // 🟢 NOUVEAU : L'usine pour créer des services en arrière-plan sans qu'ils soient détruits
        private readonly IServiceScopeFactory _scopeFactory;

        public WebServicesImportController(
            ILogger<WebServicesImportController> logger,
            DatabaseService databaseService,
            VariableBrowseParsingService variableBrowseParsingService,
            TrendsService trendsService,
            MeterRepository meterRepository,
            PCVueWebService pcvueWebService,
            ICompanyContext companyContext,
            EncryptionService encryptionService,
            IServiceScopeFactory scopeFactory) // 🟢 Injection ici
        {
            _logger = logger;
            _databaseService = databaseService;
            _variableBrowseParsingService = variableBrowseParsingService;
            _trendsService = trendsService;
            _meterRepository = meterRepository;
            _pcvueWebService = pcvueWebService;
            _companyContext = companyContext;
            _encryptionService = encryptionService;
            _scopeFactory = scopeFactory;
        }

        #endregion

        #region WebServices Functions

        [HttpPost]
        public IActionResult PrintWebServiceMeters([FromBody] PrintWebServiceMetersRequest request)
        {
            try
            {
                return Json(new { success = true, count = request?.SelectedVariables?.Count ?? 0 });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, error = ex.Message });
            }
        }

        [HttpPost("/Import/ImportWebServiceVariablesWithTrends")]
        public async Task<IActionResult> ImportWebServiceMeters([FromBody] ImportWebServiceVariablesWithTrendsRequest request)
        {
            try
            {
                if (request?.Variables == null || request.Variables.Count == 0)
                    return Json(new { success = false, error = "No variables provided for import" });

                if (!_databaseService.IsInitialized)
                    return Json(new { success = false, error = "Database connection not initialized" });

                int companyId = _companyContext.CurrentCompanyId;

                PCVueWebServiceSettings trendsSettings = null;
                bool processTrends = request.ImportTrendsData &&
                                     !string.IsNullOrEmpty(request.ConnectionId) &&
                                     request.TrendsStartDate.HasValue &&
                                     request.TrendsEndDate.HasValue;

                if (processTrends)
                {
                    trendsSettings = await GetWebServiceConnectionById(request.ConnectionId);
                    if (trendsSettings == null) processTrends = false;
                }

                int importedCount = 0, updatedCount = 0, skippedCount = 0, errorCount = 0;
                var meterIdsMap = new Dictionary<string, int>();

                // 1. IMPORTATION DES NOMS DES COMPTEURS (Rapide, bloque l'UI max 1 seconde)
                using (var connection = new NpgsqlConnection(_databaseService.GetConnectionString()))
                {
                    await connection.OpenAsync();
                    using var transaction = await connection.BeginTransactionAsync();
                    try
                    {
                        foreach (var variable in request.Variables)
                        {
                            try
                            {
                                var checkCommand = new NpgsqlCommand(@"SELECT ""MeterId"" FROM ""Meters"" WHERE ""Name"" = @meterName AND ""CompanyId"" = @companyId", connection, transaction);
                                checkCommand.Parameters.AddWithValue("@meterName", variable.VariableName);
                                checkCommand.Parameters.AddWithValue("@companyId", companyId);
                                var existingMeterId = await checkCommand.ExecuteScalarAsync();

                                if (existingMeterId != null)
                                {
                                    meterIdsMap[variable.VariableName] = Convert.ToInt32(existingMeterId);
                                    if (request.SkipExisting) skippedCount++;
                                    else if (request.UpdateExisting) updatedCount++;
                                    else skippedCount++;
                                }
                                else
                                {
                                    var insertCommand = new NpgsqlCommand(@"
                                INSERT INTO ""Meters"" (""Name"", ""Label"", ""Unit"", ""ParentId"", ""LastReading"", ""Type"", ""Active"", ""TenantID"", ""CompanyId"")
                                VALUES (@name, @label, @unit, @parentId, @lastReading, @type, @active, @tenantId, @companyId)
                                RETURNING ""MeterId""", connection, transaction);

                                    insertCommand.Parameters.AddWithValue("@name", variable.VariableName);
                                    insertCommand.Parameters.AddWithValue("@label", variable.VariableName);
                                    insertCommand.Parameters.AddWithValue("@unit", variable.Unit ?? "");
                                    insertCommand.Parameters.AddWithValue("@parentId", DBNull.Value);
                                    insertCommand.Parameters.AddWithValue("@lastReading", 0);
                                    insertCommand.Parameters.AddWithValue("@type", string.IsNullOrEmpty(variable.Type) ? "main" : variable.Type.ToLower());
                                    insertCommand.Parameters.AddWithValue("@active", variable.Active);
                                    insertCommand.Parameters.AddWithValue("@tenantId", DBNull.Value);
                                    insertCommand.Parameters.AddWithValue("@companyId", companyId);

                                    var newMeterId = await insertCommand.ExecuteScalarAsync();
                                    meterIdsMap[variable.VariableName] = Convert.ToInt32(newMeterId);
                                    importedCount++;
                                }
                            }
                            catch (Exception)
                            {
                                errorCount++;
                            }
                        }
                        await transaction.CommitAsync();
                    }
                    catch (Exception) { await transaction.RollbackAsync(); throw; }
                }

                // 2. LANCEMENT DE L'HISTORIQUE EN ARRIÈRE-PLAN (Fire-and-Forget)
                if (processTrends && meterIdsMap.Any())
                {
                    // On copie les variables pour le Thread en arrière-plan
                    var bgVariableNamesList = meterIdsMap.Keys.ToList();
                    var bgStartDate = request.TrendsStartDate.Value;
                    var bgEndDate = request.TrendsEndDate.Value;
                    var bgSettings = trendsSettings;
                    var bgMeterIdsMap = new Dictionary<string, int>(meterIdsMap);
                    var bgCompanyId = companyId;

                    // 🚀 Lancement de la tâche magique qui tourne toute seule
                    _ = Task.Run(async () =>
                    {
                        // On crée un "Scope" protégé qui ne sera pas détruit par la page Web
                        using var scope = _scopeFactory.CreateScope();
                        var bgTrendsService = scope.ServiceProvider.GetRequiredService<TrendsService>();
                        var bgDbService = scope.ServiceProvider.GetRequiredService<DatabaseService>();
                        var bgLogger = scope.ServiceProvider.GetRequiredService<ILogger<WebServicesImportController>>();

                        await ImportLock.Gate.WaitAsync(); // Protège la base de données
                        try
                        {
                            bgLogger.LogInformation("Background Trends Import Started for {Count} variables...", bgVariableNamesList.Count);

                            // L'appel qui prend 5 minutes se fait ici, en silence !
                            var trendsResults = await bgTrendsService.ProcessVariablesTrendsAsync(bgVariableNamesList, bgStartDate, bgEndDate, bgSettings);

                            using var conn = new NpgsqlConnection(bgDbService.GetConnectionString());
                            await conn.OpenAsync();
                            using var tx = await conn.BeginTransactionAsync();

                            try
                            {
                                using (var tempTableCmd = new NpgsqlCommand(@"
                                    CREATE TEMP TABLE ""TempMeterReadingsManual"" (LIKE ""MeterReadings"" EXCLUDING CONSTRAINTS) ON COMMIT DROP;
                                    ALTER TABLE ""TempMeterReadingsManual"" DROP COLUMN ""ReadingId"";
                                ", conn, tx))
                                {
                                    await tempTableCmd.ExecuteNonQueryAsync();
                                }

                                using (var writer = await conn.BeginBinaryImportAsync(@"COPY ""TempMeterReadingsManual"" (""MeterId"", ""Timestamp"", ""Value"", ""Quality"", ""CompanyId"") FROM STDIN (FORMAT BINARY)"))
                                {
                                    foreach (var res in trendsResults)
                                    {
                                        if (!res.Success || res.TrendData == null || !res.TrendData.Any()) continue;
                                        int currentMeterId = bgMeterIdsMap[res.VariableName];

                                        foreach (var point in res.TrendData)
                                        {
                                            if (!point.TimestampParsed.HasValue) continue;
                                            await writer.StartRowAsync();
                                            await writer.WriteAsync(currentMeterId, NpgsqlDbType.Integer);
                                            await writer.WriteAsync(point.TimestampParsed.Value, NpgsqlDbType.Timestamp);
                                            await writer.WriteAsync(Convert.ToDecimal(point.Value), NpgsqlDbType.Numeric);
                                            await writer.WriteAsync(point.IsGoodQuality ? 192 : 0, NpgsqlDbType.Integer);
                                            await writer.WriteAsync(bgCompanyId, NpgsqlDbType.Integer);
                                        }
                                    }
                                    await writer.CompleteAsync();
                                }

                                using (var insertCmd = new NpgsqlCommand(@"
                                    INSERT INTO ""MeterReadings"" (""MeterId"", ""Timestamp"", ""Value"", ""Quality"", ""CompanyId"")
                                    SELECT ""MeterId"", ""Timestamp"", ""Value"", ""Quality"", ""CompanyId""
                                    FROM ""TempMeterReadingsManual""
                                    ON CONFLICT (""MeterId"", ""Timestamp"") DO NOTHING", conn, tx))
                                {
                                    insertCmd.CommandTimeout = 300;
                                    await insertCmd.ExecuteNonQueryAsync();
                                }

                                await tx.CommitAsync();
                                bgLogger.LogInformation("SUCCESS: Background Trends bulk insert completed!");
                            }
                            catch (Exception ex)
                            {
                                await tx.RollbackAsync();
                                bgLogger.LogError(ex, "ERROR during background bulk insert");
                            }
                        }
                        catch (Exception ex)
                        {
                            bgLogger.LogError(ex, "Fatal error in background task");
                        }
                        finally
                        {
                            ImportLock.Gate.Release();
                        }
                    });
                }

                // 3. RÉPONSE INSTANTANÉE À L'INTERFACE WEB
                return Json(new
                {
                    success = true,
                    importedCount,
                    updatedCount,
                    skippedCount,
                    errorCount,
                    message = processTrends ? "Les compteurs ont été importés. L'historique (Trends) est en cours de téléchargement en arrière-plan !" : "Importation terminée sans historique."
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, error = ex.Message });
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetWebServiceConnections()
        {
            try
            {
                int companyId = _companyContext.CurrentCompanyId;
                var connections = await _databaseService.ExecuteWithCompanyIsolationAsync(companyId, async (conn, tr) =>
                {
                    var list = new List<dynamic>();
                    string sql = @"SELECT ""ConnectionId"", ""ConnectionName"", ""BaseUrl"", ""ProjectName"", ""IsDefault"" FROM ""WebServiceConnections""";
                    using var cmd = new NpgsqlCommand(sql, conn, tr);
                    using var reader = await cmd.ExecuteReaderAsync();
                    while (await reader.ReadAsync())
                    {
                        list.Add(new
                        {
                            connectionId = reader.GetString(0),
                            connectionName = reader.GetString(1),
                            baseUrl = reader.GetString(2),
                            projectName = reader.IsDBNull(3) ? "" : reader.GetString(3),
                            isDefault = reader.GetBoolean(4)
                        });
                    }
                    return list;
                });
                return Json(new { success = true, connections = connections });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, error = ex.Message });
            }
        }

        [HttpPost]
        public async Task<IActionResult> BrowseVariablesWebService([FromBody] BrowseVariablesRequest request)
        {
            try
            {
                var connection = await GetWebServiceConnectionById(request.ConnectionId);
                if (connection == null) return Json(new { success = false, message = "Web Service connection not found" });

                var token = await _pcvueWebService.GetValidAccessTokenAsync(connection);
                if (string.IsNullOrEmpty(token)) return Json(new { success = false, message = "Failed to authenticate" });

                var variablesEndpoint = $"{connection.BaseUrl.TrimEnd('/')}/RealtimeData/v2/Variables";
                var queryParams = new List<string> { "Depth=0", "Type=Any", $"Size={request.MaxVariables}" };
                if (!string.IsNullOrEmpty(request.BranchFilter)) queryParams.Add($"Id={Uri.EscapeDataString(request.BranchFilter)}");

                var fullUrl = $"{variablesEndpoint}?{string.Join("&", queryParams)}";
                var httpRequest = new HttpRequestMessage(HttpMethod.Get, fullUrl);
                httpRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
                httpRequest.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));

                var response = await _pcvueWebService.HttpClient.SendAsync(httpRequest);
                var responseContent = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    var jsonData = JsonSerializer.Deserialize<JsonElement>(responseContent);
                    var parseResult = _variableBrowseParsingService.ParseBrowseVariablesResponse(jsonData, request.IncludeSystemVariables);

                    return Json(new
                    {
                        success = true,
                        message = $"Variables browse completed! Found {parseResult.TotalCount} variables.",
                        variables = parseResult.Variables,
                        totalVariables = parseResult.TotalCount
                    });
                }
                return Json(new { success = false, message = $"API call failed: {response.StatusCode}" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, error = ex.Message });
            }
        }

        #endregion

        #region Helper Methods
        private async Task<PCVueWebServiceSettings?> GetWebServiceConnectionById(string connectionId)
        {
            try
            {
                int companyId = _companyContext.CurrentCompanyId;
                return await _databaseService.ExecuteWithCompanyIsolationAsync(companyId, async (conn, tr) =>
                {
                    string sql = @"SELECT ""ConnectionId"", ""ConnectionName"", ""BaseUrl"", ""ClientId"", ""ClientSecret"",
                                          ""ApiKey"", ""Username"", ""Password"", ""AuthType"", ""TimeoutSeconds"",
                                          ""ProjectName"", ""IsDefault""
                                   FROM ""WebServiceConnections""
                                   WHERE ""ConnectionId"" = @connId LIMIT 1";

                    using var cmd = new NpgsqlCommand(sql, conn, tr);
                    cmd.Parameters.AddWithValue("connId", connectionId);
                    using var reader = await cmd.ExecuteReaderAsync();

                    if (await reader.ReadAsync())
                    {
                        return new PCVueWebServiceSettings
                        {
                            ConnectionId = reader.IsDBNull(0) ? "" : reader.GetString(0),
                            ConnectionName = reader.IsDBNull(1) ? "" : reader.GetString(1),
                            BaseUrl = reader.IsDBNull(2) ? "" : reader.GetString(2),
                            ClientId = reader.IsDBNull(3) ? "" : reader.GetString(3),
                            ClientSecret = reader.IsDBNull(4) ? "" : _encryptionService.Decrypt(reader.GetString(4)),
                            ApiKey = reader.IsDBNull(5) ? "" : reader.GetString(5),
                            Username = reader.IsDBNull(6) ? "" : reader.GetString(6),
                            Password = reader.IsDBNull(7) ? "" : _encryptionService.Decrypt(reader.GetString(7)),
                            AuthType = (AuthenticationType)reader.GetInt32(8),
                            TimeoutSeconds = reader.GetInt32(9),
                            ProjectName = reader.IsDBNull(10) ? "" : reader.GetString(10),
                            IsDefault = reader.GetBoolean(11)
                        };
                    }
                    return null;
                });
            }
            catch
            {
                return null;
            }
        }
        #endregion
    }
}