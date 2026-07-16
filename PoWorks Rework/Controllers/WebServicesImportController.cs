using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Npgsql;
using PoWorks_Rework.Models;
using PoWorks_Rework.Services;
using PoWorks_Rework.Repositories;
using System.Text.Json;
using System.Security.Authentication;

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

        public WebServicesImportController(
            ILogger<WebServicesImportController> logger,
            DatabaseService databaseService,
            VariableBrowseParsingService variableBrowseParsingService,
            TrendsService trendsService,
            MeterRepository meterRepository,
            PCVueWebService pcvueWebService)   
        {
            _logger = logger;
            _databaseService = databaseService;
            _variableBrowseParsingService = variableBrowseParsingService;
            _trendsService = trendsService;
            _meterRepository = meterRepository;
            _pcvueWebService = pcvueWebService;  // ← AJOUTE ÇA
        }

        #endregion

        #region WebServices Functions (Moved from ImportController)

        [HttpPost]
        public IActionResult PrintWebServiceMeters([FromBody] PrintWebServiceMetersRequest request)
        {
            try
            {
                Console.WriteLine("\n=====================================================");
                Console.WriteLine("WEB SERVICE VARIABLES PRINT FUNCTION");
                Console.WriteLine("=====================================================");
                Console.WriteLine($"Connection ID: {request?.ConnectionId ?? "Not provided"}");
                Console.WriteLine($"Connection Name: {request?.ConnectionName ?? "Not provided"}");
                Console.WriteLine($"Selected variables count: {request?.SelectedVariables?.Count ?? 0}");
                Console.WriteLine($"Print timestamp: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");

                // ADD date range output
                if (!string.IsNullOrEmpty(request?.StartDate))
                {
                    Console.WriteLine($"Trends Start Date: {request.StartDate}");
                }
                if (!string.IsNullOrEmpty(request?.EndDate))
                {
                    Console.WriteLine($"Trends End Date: {request.EndDate}");
                }
                if (!string.IsNullOrEmpty(request?.StartDate) && !string.IsNullOrEmpty(request?.EndDate))
                {
                    if (DateTime.TryParse(request.StartDate, out var start) && DateTime.TryParse(request.EndDate, out var end))
                    {
                        var duration = end - start;
                        Console.WriteLine($"Trends Duration: {duration.TotalDays:F1} days ({duration.TotalHours:F1} hours)");
                    }
                }

                if (request?.SelectedVariables != null && request.SelectedVariables.Count > 0)
                {
                    // existing variable details code...
                }

                return Json(new { success = true, count = request?.SelectedVariables?.Count ?? 0 });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error in PrintWebServiceMeters: {ex.Message}");
                return Json(new { success = false, error = ex.Message });
            }
        }

        [HttpPost("/Import/ImportWebServiceVariablesWithTrends")]
        public async Task<IActionResult> ImportWebServiceMeters([FromBody] ImportWebServiceVariablesWithTrendsRequest request)
        {
            try
            {
                Console.WriteLine("\n=====================================================");
                Console.WriteLine("🚀 WEB SERVICE VARIABLES IMPORT WITH TRENDS");
                Console.WriteLine("=====================================================");

                if (request?.Variables == null || request.Variables.Count == 0)
                    return Json(new { success = false, error = "No variables provided for import" });

                if (!_databaseService.IsInitialized)
                    return Json(new { success = false, error = "Database connection not initialized" });

                PCVueWebServiceSettings trendsSettings = null;
                bool processTrends = request.ImportTrendsData &&
                                   !string.IsNullOrEmpty(request.ConnectionId) &&
                                   request.TrendsStartDate.HasValue &&
                                   request.TrendsEndDate.HasValue;

                if (processTrends)
                {
                    trendsSettings = GetWebServiceConnectionById(request.ConnectionId);
                    if (trendsSettings == null) processTrends = false;
                }

                int importedCount = 0, updatedCount = 0, skippedCount = 0, errorCount = 0;
                int trendsSuccessCount = 0, trendsFailedCount = 0;
                var errorVariables = new List<string>();
                var detailedErrors = new Dictionary<string, string>();
                var meterIdsMap = new Dictionary<string, int>();

                // --- PHASE 1 : CRÉATION DES COMPTEURS ---
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
                                var checkCommand = new NpgsqlCommand(@"SELECT ""MeterId"" FROM ""Meters"" WHERE ""Name"" = @meterName", connection, transaction);
                                checkCommand.Parameters.AddWithValue("@meterName", variable.VariableName);
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
                                INSERT INTO ""Meters"" (""Name"", ""Label"", ""Unit"", ""ParentId"", ""LastReading"", ""Type"", ""Active"", ""TenantID"")
                                VALUES (@name, @label, @unit, @parentId, @lastReading, @type, @active, @tenantId)
                                RETURNING ""MeterId""", connection, transaction);

                                    insertCommand.Parameters.AddWithValue("@name", variable.VariableName);
                                    insertCommand.Parameters.AddWithValue("@label", variable.VariableName);
                                    insertCommand.Parameters.AddWithValue("@unit", variable.Unit ?? "");
                                    insertCommand.Parameters.AddWithValue("@parentId", DBNull.Value);
                                    insertCommand.Parameters.AddWithValue("@lastReading", 0);
                                    insertCommand.Parameters.AddWithValue("@type", string.IsNullOrEmpty(variable.Type) ? "main" : variable.Type.ToLower());
                                    insertCommand.Parameters.AddWithValue("@active", variable.Active);
                                    insertCommand.Parameters.AddWithValue("@tenantId", DBNull.Value);

                                    var newMeterId = await insertCommand.ExecuteScalarAsync();
                                    meterIdsMap[variable.VariableName] = Convert.ToInt32(newMeterId);
                                    importedCount++;
                                }
                            }
                            catch (Exception ex)
                            {
                                errorCount++;
                                errorVariables.Add(variable.VariableName);
                                detailedErrors[variable.VariableName] = ex.Message;
                            }
                        }
                        await transaction.CommitAsync();
                    }
                    catch (Exception) { await transaction.RollbackAsync(); throw; }
                }

                // --- PHASE 2 : TRAITEMENT PAR BATCH ET INSERTION EN BDD ---
                if (processTrends && meterIdsMap.Any())
                {
                    Console.WriteLine($"\n⚡ Lancement du téléchargement BATCH pour {meterIdsMap.Count} compteurs...");

                    var variableNamesList = meterIdsMap.Keys.ToList();
                    var trendsResults = await _trendsService.ProcessVariablesTrendsAsync(variableNamesList, request.TrendsStartDate.Value, request.TrendsEndDate.Value, trendsSettings);

                    // On ouvre une connexion pour insérer les points
                    using (var conn = new NpgsqlConnection(_databaseService.GetConnectionString()))
                    {
                        await conn.OpenAsync();

                        foreach (var res in trendsResults)
                        {
                            if (res.Success && res.TrendData != null && res.TrendData.Any())
                            {
                                trendsSuccessCount++;
                                int currentMeterId = meterIdsMap[res.VariableName];

                                using var tx = await conn.BeginTransactionAsync();
                                try
                                {
                                    int pointsInserted = 0;
                                    foreach (var point in res.TrendData)
                                    {
                                        if (point.TimestampParsed.HasValue)
                                        {
                                            var insertCmd = new NpgsqlCommand(@"
                                INSERT INTO ""MeterReadings"" (""MeterId"", ""Timestamp"", ""Value"", ""Quality"")
                                VALUES (@meterId, @timestamp, @value, @quality)
                                ON CONFLICT (""MeterId"", ""Timestamp"") DO NOTHING", conn, tx);

                                            insertCmd.Parameters.AddWithValue("@meterId", currentMeterId);
                                            insertCmd.Parameters.AddWithValue("@timestamp", point.TimestampParsed.Value);
                                            insertCmd.Parameters.AddWithValue("@value", point.Value);

                                            // Si la qualité est "Good", on met 192 (standard OPC), sinon 0
                                            int qualityValue = point.IsGoodQuality ? 192 : 0;
                                            insertCmd.Parameters.AddWithValue("@quality", qualityValue);

                                            await insertCmd.ExecuteNonQueryAsync();
                                            pointsInserted++;
                                        }
                                    }
                                    await tx.CommitAsync();
                                    Console.WriteLine($"💾 SAUVEGARDE RÉUSSIE : {pointsInserted} points insérés pour {res.VariableName} !");
                                }
                                catch (Exception ex)
                                {
                                    await tx.RollbackAsync();
                                    Console.WriteLine($"❌ Erreur d'insertion pour {res.VariableName}: {ex.Message}");
                                    _logger.LogError(ex, "Erreur lors de la sauvegarde des trends en BDD");
                                }
                            }
                            else
                            {
                                trendsFailedCount++;
                            }
                        }
                    }
                }

                // --- PHASE 3 : RÉPONSE JSON TOUJOURS VALIDE ---
                return Json(new
                {
                    success = true,
                    importedCount,
                    updatedCount,
                    skippedCount,
                    errorCount,
                    trendsSuccessCount,
                    trendsFailedCount,
                    errorVariables,
                    detailedErrors,
                    message = $"Import terminé. Succès: {importedCount}. Échecs: {errorCount}."
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur fatale");
                return Json(new { success = false, error = ex.Message });
            }
        }

        [HttpGet]
        public IActionResult GetWebServiceConnections()
        {
            try
            {
                _logger.LogInformation("Getting Web Service connections...");

                // Get webservice connections from configuration
                var connections = new List<dynamic>();
                var webServiceSection = HttpContext.RequestServices.GetRequiredService<IConfiguration>().GetSection("WebServiceConnections");

                if (webServiceSection.Exists())
                {
                    foreach (var connectionSection in webServiceSection.GetChildren())
                    {
                        connections.Add(new
                        {
                            connectionId = connectionSection["ConnectionId"] ?? Guid.NewGuid().ToString(),
                            connectionName = connectionSection["ConnectionName"] ?? "",
                            baseUrl = connectionSection["BaseUrl"] ?? "",
                            projectName = connectionSection["ProjectName"] ?? "",
                            isDefault = bool.Parse(connectionSection["IsDefault"] ?? "false")
                        });
                    }
                }

                _logger.LogInformation($"Found {connections.Count} Web Service connections");
                return Json(new { success = true, connections = connections });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting Web Service connections");
                return Json(new { success = false, error = ex.Message });
            }
        }

        [HttpPost]
        public async Task<IActionResult> BrowseVariablesWebService([FromBody] BrowseVariablesRequest request)
        {
            try
            {
                Console.WriteLine("\n=====================================================");
                Console.WriteLine("PCVue VARIABLES BROWSE");
                Console.WriteLine("=====================================================");
                Console.WriteLine($"Connection ID: {request.ConnectionId}");
                Console.WriteLine($"Max Variables: {request.MaxVariables}");
                Console.WriteLine($"Branch Filter: {request.BranchFilter ?? "None"}");
                Console.WriteLine($"Variable Type: {request.VariableType}");
                Console.WriteLine($"Depth: {request.Depth}");
                Console.WriteLine($"Include System Variables: {request.IncludeSystemVariables}");

                if (!string.IsNullOrEmpty(request.StartDate))
                {
                    Console.WriteLine($"Trends Start Date: {request.StartDate}");
                }
                if (!string.IsNullOrEmpty(request.EndDate))
                {
                    Console.WriteLine($"Trends End Date: {request.EndDate}");
                }
                if (!string.IsNullOrEmpty(request.StartDate) && !string.IsNullOrEmpty(request.EndDate))
                {
                    if (DateTime.TryParse(request.StartDate, out var start) && DateTime.TryParse(request.EndDate, out var end))
                    {
                        var duration = end - start;
                        Console.WriteLine($"Trends Duration: {duration.TotalDays:F1} days ({duration.TotalHours:F1} hours)");
                    }
                }

                Console.WriteLine($"Start Time: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");

                // Get the connection settings
                var connection = GetWebServiceConnectionById(request.ConnectionId);
                if (connection == null)
                {
                    Console.WriteLine("❌ ERROR: Web Service connection not found");
                    Console.WriteLine("=====================================================\n");
                    return Json(new { success = false, message = "Web Service connection not found" });
                }

                Console.WriteLine($"Connection Name: {connection.ConnectionName}");

                // Create HttpClient with SSL bypass
                var webService = _pcvueWebService;

                // Get authentication token
                Console.WriteLine("\n--- AUTHENTICATION ---");
                var token = await webService.GetValidAccessTokenAsync(connection);
                if (string.IsNullOrEmpty(token))
                {
                    Console.WriteLine("❌ ERROR: Failed to get authentication token");
                    Console.WriteLine("=====================================================\n");
                    return Json(new { success = false, message = "Failed to authenticate" });
                }

                Console.WriteLine("✅ Authentication successful");

                // Build the Variables endpoint URL
                var variablesEndpoint = $"{connection.BaseUrl.TrimEnd('/')}/RealtimeData/v2/Variables";
                var queryParams = new List<string>
                {
                    "Depth=0",
                    "Type=Any",
                    $"Size={request.MaxVariables}"
                };

                if (!string.IsNullOrEmpty(request.BranchFilter))
                {
                    queryParams.Add($"Id={Uri.EscapeDataString(request.BranchFilter)}");
                }

                var fullUrl = $"{variablesEndpoint}?{string.Join("&", queryParams)}";
                Console.WriteLine($"Endpoint: {fullUrl}");

                // Create and send request
                var httpRequest = new HttpRequestMessage(HttpMethod.Get, fullUrl);
                httpRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
                httpRequest.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));

                var response = await _pcvueWebService.HttpClient.SendAsync(httpRequest);
                var responseContent = await response.Content.ReadAsStringAsync();

                Console.WriteLine($"Response Status: {response.StatusCode}");
                Console.WriteLine($"Response Length: {responseContent?.Length ?? 0} characters");

                if (response.IsSuccessStatusCode)
                {
                    Console.WriteLine("✅ API call successful");

                    try
                    {
                        // Parse JSON response
                        var jsonData = JsonSerializer.Deserialize<JsonElement>(responseContent);

                        // Parse the response using our parsing service with System variable filtering
                        var parseResult = _variableBrowseParsingService.ParseBrowseVariablesResponse(
                            jsonData,
                            request.IncludeSystemVariables);

                        // Print ONLY the parsed results to console
                        var connectionName = connection.ConnectionName ?? request.ConnectionId;
                        _variableBrowseParsingService.PrintParsedVariablesToConsole(
                            parseResult,
                            connectionName,
                            request.IncludeSystemVariables);

                        Console.WriteLine($"✅ Parsing completed successfully");
                        Console.WriteLine($"End Time: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                        Console.WriteLine("=====================================================\n");

                        // Return response with parsed data for meter selection table
                        return Json(new
                        {
                            success = true,
                            message = $"Variables browse completed! Found {parseResult.TotalCount} variables (System variables {(request.IncludeSystemVariables ? "included" : "filtered out")}). Check terminal for detailed results.",
                            variables = parseResult.Variables,
                            totalVariables = parseResult.TotalCount,
                            connectionInfo = new
                            {
                                connectionId = request.ConnectionId,
                                connectionName = connection.ConnectionName
                            }
                        });
                    }
                    catch (Exception parseEx)
                    {
                        Console.WriteLine($"❌ ERROR parsing response: {parseEx.Message}");
                        Console.WriteLine("=====================================================\n");
                        return Json(new { success = false, message = $"Error parsing variables: {parseEx.Message}" });
                    }
                }
                else
                {
                    Console.WriteLine($"❌ ERROR: API call failed");
                    Console.WriteLine($"Response: {responseContent}");
                    Console.WriteLine("=====================================================\n");
                    return Json(new { success = false, message = $"API call failed: {response.StatusCode}" });
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error during browse: {ex.Message}");
                Console.WriteLine("=====================================================\n");
                _logger.LogError(ex, "Error browsing PCVue variables");
                return Json(new { success = false, error = ex.Message });
            }
        }

        #endregion

        #region Helper Methods

        /// <summary>
        /// Get Web Service connection settings by connection ID
        /// </summary>
        private PCVueWebServiceSettings? GetWebServiceConnectionById(string connectionId)
        {
            try
            {
                var config = HttpContext.RequestServices.GetRequiredService<IConfiguration>();
                var webServiceSection = config.GetSection("WebServiceConnections");

                foreach (var connectionSection in webServiceSection.GetChildren())
                {
                    if (connectionSection["ConnectionId"] == connectionId)
                    {
                        return new PCVueWebServiceSettings
                        {
                            ConnectionId = connectionSection["ConnectionId"] ?? "",
                            ConnectionName = connectionSection["ConnectionName"] ?? "",
                            BaseUrl = connectionSection["BaseUrl"] ?? "",
                            ClientId = connectionSection["ClientId"] ?? "",
                            ClientSecret = connectionSection["ClientSecret"] ?? "",
                            Username = connectionSection["Username"] ?? "",
                            Password = connectionSection["Password"] ?? "",
                            AuthType = (AuthenticationType)(int.TryParse(connectionSection["AuthType"], out var authType) ? authType : 0),
                            TimeoutSeconds = int.TryParse(connectionSection["TimeoutSeconds"], out var timeout) ? timeout : 30,
                            ProjectName = connectionSection["ProjectName"] ?? "",
                            IsDefault = bool.Parse(connectionSection["IsDefault"] ?? "false")
                        };
                    }
                }

                _logger.LogWarning("Web service connection not found: {ConnectionId}", connectionId);
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reading web service settings for connection: {ConnectionId}", connectionId);
                return null;
            }
        }

        #endregion


        // Ajoute ça juste ici :
        private async Task<bool> ProcessTrendsForVariable(WebServiceVariableItem variable, PCVueWebServiceSettings settings, string startDate, string endDate)
        {
            try
            {
                if (!DateTime.TryParse(startDate, out var start) || !DateTime.TryParse(endDate, out var end))
                    return false;

                var variableNames = new List<string> { variable.VariableName };
                var trendsResults = await _trendsService.ProcessVariablesTrendsAsync(variableNames, start, end, settings);

                var result = trendsResults.FirstOrDefault();
                return result != null && result.Success && result.TrendData != null && result.TrendData.Any();
            }
            catch
            {
                return false;



            }
        }
    } 
} 
