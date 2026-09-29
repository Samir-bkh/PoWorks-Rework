using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using NpgsqlTypes;
using PoWorks_Rework.Models;
using PoWorks_Rework.Services;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace PoWorks_Rework.Controllers
{
    /// <summary>
    /// PCVue Web Services import pipeline: browse, inspect, enrich and upsert meters,
    /// then optionally import historical trends in the background.
    /// </summary>
    [Authorize(Policy = "ImportExportAccess")]
    public class WebServicesImportController : Controller
    {
        private readonly ILogger<WebServicesImportController> _logger;
        private readonly DatabaseService _databaseService;
        private readonly VariableBrowseParsingService _variableBrowseParsingService;
        private readonly PCVueWebService _pcvueWebService;
        private readonly ICompanyContext _companyContext;
        private readonly EncryptionService _encryptionService;
        private readonly IServiceScopeFactory _scopeFactory;

        public WebServicesImportController(
            ILogger<WebServicesImportController> logger,
            DatabaseService databaseService,
            VariableBrowseParsingService variableBrowseParsingService,
            PCVueWebService pcvueWebService,
            ICompanyContext companyContext,
            EncryptionService encryptionService,
            IServiceScopeFactory scopeFactory)
        {
            _logger = logger;
            _databaseService = databaseService;
            _variableBrowseParsingService = variableBrowseParsingService;
            _pcvueWebService = pcvueWebService;
            _companyContext = companyContext;
            _encryptionService = encryptionService;
            _scopeFactory = scopeFactory;
        }

        [HttpPost]
        public IActionResult PrintWebServiceMeters([FromBody] PrintWebServiceMetersRequest request)
            => Json(new { success = true, count = request?.SelectedVariables?.Count ?? 0 });

        [HttpPost]
        public async Task<IActionResult> BrowseVariablesWebService([FromBody] BrowseVariablesRequest request)
        {
            try
            {
                if (!_databaseService.IsInitialized)
                    return Json(new { success = false, message = "Database connection not initialized" });

                var connection = await GetWebServiceConnectionById(request.ConnectionId);
                if (connection == null)
                    return Json(new { success = false, message = "Web Service connection not found" });

                var maxVariables = Math.Clamp(request.MaxVariables, 1, 1_000_000);
                var endpoint = BuildVariablesBrowseUrl(
                    connection.BaseUrl,
                    request.BranchFilter,
                    request.Depth,
                    request.VariableType,
                    maxVariables);

                var token = await _pcvueWebService.GetValidAccessTokenAsync(connection);
                if (string.IsNullOrWhiteSpace(token))
                    return Json(new { success = false, message = "Failed to authenticate with PCVue" });

                string responseContent;
                using (var firstResponse = await SendBrowseRequestAsync(endpoint, token))
                {
                    if (firstResponse.StatusCode == HttpStatusCode.Unauthorized)
                    {
                        token = await _pcvueWebService.GetValidAccessTokenAsync(connection, forceRefresh: true);
                        if (string.IsNullOrWhiteSpace(token))
                            return Json(new { success = false, message = "PCVue session expired and could not be refreshed" });

                        using var retryResponse = await SendBrowseRequestAsync(endpoint, token);
                        responseContent = await retryResponse.Content.ReadAsStringAsync();
                        if (!retryResponse.IsSuccessStatusCode)
                            return Json(new { success = false, message = BuildApiError("Variables browse", retryResponse, responseContent) });
                    }
                    else
                    {
                        responseContent = await firstResponse.Content.ReadAsStringAsync();
                        if (!firstResponse.IsSuccessStatusCode)
                            return Json(new { success = false, message = BuildApiError("Variables browse", firstResponse, responseContent) });
                    }
                }

                var jsonData = JsonSerializer.Deserialize<JsonElement>(responseContent);
                var parsed = _variableBrowseParsingService.ParseBrowseVariablesResponse(
                    jsonData,
                    request.IncludeSystemVariables);
                if (!parsed.Success)
                    return Json(new { success = false, message = parsed.ErrorMessage });

                var companyId = _companyContext.CurrentCompanyId;
                var snapshot = await LoadMeterSnapshotAsync(companyId);

                var variables = parsed.Variables.Select(variable =>
                {
                    snapshot.ByName.TryGetValue(variable.FullPath, out var existing);
                    return new
                    {
                        variable.FullPath,
                        variable.Branches,
                        variable.VariableName,
                        variable.VariableType,
                        variable.IsReadOnly,
                        variable.IsLeaf,
                        variable.IsSystemVariable,
                        existingMeterId = existing?.MeterId,
                        existingUnit = existing?.Unit ?? string.Empty,
                        existingType = existing?.Type ?? string.Empty,
                        existingActive = existing?.Active,
                        existingParentId = existing?.ParentId
                    };
                }).ToList();

                return Json(new
                {
                    success = true,
                    message = $"Variables browse completed. Found {variables.Count} variable(s).",
                    variables,
                    totalVariables = variables.Count,
                    filteredSystemVariables = parsed.FilteredSystemVariables,
                    parentOptions = snapshot.ParentOptions,
                    connectionInfo = new
                    {
                        connectionId = connection.ConnectionId,
                        connectionName = connection.ConnectionName
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "PCVue variables browse failed.");
                return Json(new { success = false, error = ex.Message });
            }
        }

        /// <summary>
        /// Browse does not expose the engineering Unit property. This endpoint enriches
        /// selected variables with PCVue BulkRead in bounded batches.
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> ResolveVariableUnits([FromBody] ResolveVariableUnitsRequest request)
        {
            try
            {
                var names = request.VariableNames
                    .Where(name => !string.IsNullOrWhiteSpace(name))
                    .Select(name => name.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Take(5000)
                    .ToList();

                if (names.Count == 0)
                    return Json(new { success = true, units = new Dictionary<string, string>() });

                var settings = await GetWebServiceConnectionById(request.ConnectionId);
                if (settings == null)
                    return Json(new { success = false, error = "Web Service connection not found" });

                var units = await ResolvePcVueUnitsAsync(settings, names);
                return Json(new
                {
                    success = true,
                    units,
                    resolvedCount = units.Count(pair => !string.IsNullOrWhiteSpace(pair.Value)),
                    requestedCount = names.Count
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Unable to resolve PCVue units.");
                return Json(new { success = false, error = ex.Message });
            }
        }

        /// <summary>
        /// Imports new variables and truly updates existing meter metadata when requested.
        /// Existing labels, tenants, readings and last readings are deliberately preserved.
        /// </summary>
        [HttpPost("/Import/ImportWebServiceVariablesWithTrends")]
        public async Task<IActionResult> ImportWebServiceMeters([FromBody] WebServiceVariableImportRequest request)
        {
            try
            {
                if (request.Variables == null || request.Variables.Count == 0)
                    return Json(new { success = false, error = "No variables provided for import" });

                if (!_databaseService.IsInitialized)
                    return Json(new { success = false, error = "Database connection not initialized" });

                var variables = request.Variables
                    .Where(variable => !string.IsNullOrWhiteSpace(variable.VariableName))
                    .GroupBy(variable => variable.VariableName.Trim(), StringComparer.OrdinalIgnoreCase)
                    .Select(group => group.Last())
                    .ToList();

                var filteredSystemCount = 0;
                if (!request.IncludeSystemVariables)
                {
                    var filtered = new List<WebServiceVariableWithTrends>();
                    foreach (var variable in variables)
                    {
                        if (VariableBrowseParsingService.IsSystemVariablePath(variable.VariableName))
                        {
                            filteredSystemCount++;
                            continue;
                        }
                        filtered.Add(variable);
                    }
                    variables = filtered;
                }

                if (variables.Count == 0)
                {
                    return Json(new
                    {
                        success = true,
                        importedCount = 0,
                        updatedCount = 0,
                        skippedCount = 0,
                        errorCount = 0,
                        filteredSystemCount,
                        message = "No importable variables remained after filtering system variables."
                    });
                }

                var companyId = _companyContext.CurrentCompanyId;
                var meterIds = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                var errors = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                var importedCount = 0;
                var updatedCount = 0;
                var skippedCount = 0;

                await using (var connection = _databaseService.CreateNewConnection())
                {
                    await connection.OpenAsync();
                    await using var transaction = await connection.BeginTransactionAsync();

                    try
                    {
                        foreach (var variable in variables)
                        {
                            var variableName = variable.VariableName.Trim();
                            try
                            {
                                var existing = await FindExistingMeterAsync(
                                    connection,
                                    transaction,
                                    companyId,
                                    variableName);

                                if (existing != null)
                                {
                                    meterIds[variableName] = existing.MeterId;

                                    if (request.SkipExisting || !request.UpdateExisting)
                                    {
                                        skippedCount++;
                                        continue;
                                    }

                                    var resolvedUnit = WebServiceMeterImportRules.ResolveUnit(existing.Unit, variable.Unit);
                                    var resolvedType = WebServiceMeterImportRules.ResolveMeterType(existing.Type, variable.Type);
                                    var requestedParentId = WebServiceMeterImportRules.ParseParentId(variable.ParentMeterId);
                                    var resolvedParentId = await ResolveParentIdAsync(
                                        connection,
                                        transaction,
                                        companyId,
                                        existing.MeterId,
                                        requestedParentId,
                                        existing.ParentId);

                                    await using var update = new NpgsqlCommand(@"
                                        UPDATE ""Meters""
                                        SET ""Unit"" = @unit,
                                            ""Type"" = @type,
                                            ""Active"" = @active,
                                            ""ParentId"" = @parentId
                                        WHERE ""MeterId"" = @meterId
                                          AND ""CompanyId"" = @companyId", connection, transaction);
                                    update.Parameters.AddWithValue("unit", resolvedUnit);
                                    update.Parameters.AddWithValue("type", resolvedType);
                                    update.Parameters.AddWithValue("active", variable.Active);
                                    AddNullableInt(update, "parentId", resolvedParentId);
                                    update.Parameters.AddWithValue("meterId", existing.MeterId);
                                    update.Parameters.AddWithValue("companyId", companyId);
                                    await update.ExecuteNonQueryAsync();
                                    updatedCount++;
                                }
                                else
                                {
                                    var parentId = await ResolveParentIdAsync(
                                        connection,
                                        transaction,
                                        companyId,
                                        null,
                                        WebServiceMeterImportRules.ParseParentId(variable.ParentMeterId),
                                        null);

                                    await using var insert = new NpgsqlCommand(@"
                                        INSERT INTO ""Meters""
                                            (""Name"", ""Label"", ""Unit"", ""ParentId"", ""LastReading"", ""Type"", ""Active"", ""TenantID"", ""CompanyId"")
                                        VALUES
                                            (@name, @label, @unit, @parentId, 0, @type, @active, NULL, @companyId)
                                        RETURNING ""MeterId""", connection, transaction);
                                    insert.Parameters.AddWithValue("name", variableName);
                                    insert.Parameters.AddWithValue("label", variableName);
                                    insert.Parameters.AddWithValue("unit", WebServiceMeterImportRules.ResolveUnit(null, variable.Unit));
                                    AddNullableInt(insert, "parentId", parentId);
                                    insert.Parameters.AddWithValue("type", WebServiceMeterImportRules.ResolveMeterType(null, variable.Type));
                                    insert.Parameters.AddWithValue("active", variable.Active);
                                    insert.Parameters.AddWithValue("companyId", companyId);

                                    var newMeterId = Convert.ToInt32(await insert.ExecuteScalarAsync());
                                    meterIds[variableName] = newMeterId;
                                    importedCount++;
                                }
                            }
                            catch (Exception ex)
                            {
                                errors[variableName] = ex.Message;
                                _logger.LogWarning(ex, "Unable to import PCVue variable {VariableName}.", variableName);
                            }
                        }

                        await transaction.CommitAsync();
                    }
                    catch
                    {
                        await transaction.RollbackAsync();
                        throw;
                    }
                }

                var processTrends = request.ImportTrendsData &&
                                    meterIds.Count > 0 &&
                                    !string.IsNullOrWhiteSpace(request.ConnectionId) &&
                                    request.TrendsStartDate.HasValue &&
                                    request.TrendsEndDate.HasValue &&
                                    request.TrendsStartDate.Value < request.TrendsEndDate.Value;

                PCVueWebServiceSettings? trendsSettings = null;
                if (processTrends)
                {
                    trendsSettings = await GetWebServiceConnectionById(request.ConnectionId);
                    processTrends = trendsSettings != null;
                }

                if (processTrends && trendsSettings != null)
                {
                    StartBackgroundTrendsImport(
                        meterIds,
                        request.TrendsStartDate!.Value,
                        request.TrendsEndDate!.Value,
                        trendsSettings,
                        companyId);
                }

                return Json(new
                {
                    success = true,
                    importedCount,
                    updatedCount,
                    skippedCount,
                    errorCount = errors.Count,
                    filteredSystemCount,
                    detailedErrors = errors,
                    trendsStarted = processTrends,
                    message = processTrends
                        ? "Meter metadata was saved. Historical trends are importing in the background."
                        : "Meter metadata import completed."
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "PCVue meter import failed.");
                return Json(new { success = false, error = ex.Message });
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetWebServiceConnections()
        {
            try
            {
                var companyId = _companyContext.CurrentCompanyId;
                var connections = await _databaseService.ExecuteWithCompanyIsolationAsync(companyId, async (conn, tr) =>
                {
                    var list = new List<object>();
                    const string sql = @"
                        SELECT ""ConnectionId"", ""ConnectionName"", ""BaseUrl"", ""ProjectName"", ""IsDefault""
                        FROM ""WebServiceConnections""
                        WHERE ""CompanyId"" = @companyId
                        ORDER BY ""Id""";

                    await using var cmd = new NpgsqlCommand(sql, conn, tr);
                    cmd.Parameters.AddWithValue("companyId", companyId);
                    await using var reader = await cmd.ExecuteReaderAsync();
                    while (await reader.ReadAsync())
                    {
                        list.Add(new
                        {
                            connectionId = reader.IsDBNull(0) ? "" : reader.GetString(0),
                            connectionName = reader.IsDBNull(1) ? "" : reader.GetString(1),
                            baseUrl = reader.IsDBNull(2) ? "" : reader.GetString(2),
                            projectName = reader.IsDBNull(3) ? "" : reader.GetString(3),
                            isDefault = !reader.IsDBNull(4) && reader.GetBoolean(4)
                        });
                    }
                    return list;
                });

                return Json(new { success = true, connections });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, error = ex.Message });
            }
        }

        private async Task<HttpResponseMessage> SendBrowseRequestAsync(string endpoint, string token)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            return await _pcvueWebService.HttpClient.SendAsync(request);
        }

        private static string BuildVariablesBrowseUrl(
            string baseUrl,
            string? branchFilter,
            int depth,
            string? variableType,
            int size)
        {
            var endpoint = $"{baseUrl.TrimEnd('/')}/RealtimeData/v2/Variables";
            if (!string.IsNullOrWhiteSpace(branchFilter))
            {
                var segments = branchFilter
                    .Split(new[] { '.', '/', '\\' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(Uri.EscapeDataString);
                endpoint += "/" + string.Join("/", segments);
            }

            var query = new List<string>
            {
                $"Depth={Math.Max(0, depth)}",
                $"Type={Uri.EscapeDataString(string.IsNullOrWhiteSpace(variableType) ? "Any" : variableType)}",
                $"Size={size}"
            };
            return endpoint + "?" + string.Join("&", query);
        }

        private async Task<MeterSnapshot> LoadMeterSnapshotAsync(int companyId)
        {
            var byName = new Dictionary<string, ExistingMeterInfo>(StringComparer.OrdinalIgnoreCase);
            var parentOptions = new List<object>();

            await using var connection = _databaseService.CreateNewConnection();
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand(@"
                SELECT ""MeterId"", ""Name"", COALESCE(""Unit"", ''), COALESCE(""Type"", 'main'), ""Active"", ""ParentId""
                FROM ""Meters""
                WHERE ""CompanyId"" = @companyId
                ORDER BY ""Name""", connection);
            command.Parameters.AddWithValue("companyId", companyId);

            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var item = new ExistingMeterInfo(
                    reader.GetInt32(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetString(3),
                    reader.GetBoolean(4),
                    reader.IsDBNull(5) ? null : reader.GetInt32(5));

                byName[item.Name] = item;
                parentOptions.Add(new
                {
                    value = item.MeterId.ToString(),
                    text = string.IsNullOrWhiteSpace(item.Unit)
                        ? item.Name
                        : $"{item.Name} ({item.Unit})"
                });
            }

            return new MeterSnapshot(byName, parentOptions);
        }

        private static async Task<ExistingMeterInfo?> FindExistingMeterAsync(
            NpgsqlConnection connection,
            NpgsqlTransaction transaction,
            int companyId,
            string name)
        {
            await using var command = new NpgsqlCommand(@"
                SELECT ""MeterId"", ""Name"", COALESCE(""Unit"", ''), COALESCE(""Type"", 'main'), ""Active"", ""ParentId""
                FROM ""Meters""
                WHERE ""Name"" = @name
                  AND ""CompanyId"" = @companyId
                LIMIT 1", connection, transaction);
            command.Parameters.AddWithValue("name", name);
            command.Parameters.AddWithValue("companyId", companyId);

            await using var reader = await command.ExecuteReaderAsync();
            if (!await reader.ReadAsync()) return null;

            return new ExistingMeterInfo(
                reader.GetInt32(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetBoolean(4),
                reader.IsDBNull(5) ? null : reader.GetInt32(5));
        }

        private static async Task<int?> ResolveParentIdAsync(
            NpgsqlConnection connection,
            NpgsqlTransaction transaction,
            int companyId,
            int? meterId,
            int? requestedParentId,
            int? existingParentId)
        {
            if (!requestedParentId.HasValue) return existingParentId;
            if (meterId.HasValue && requestedParentId.Value == meterId.Value) return existingParentId;

            await using var command = new NpgsqlCommand(@"
                SELECT 1
                FROM ""Meters""
                WHERE ""MeterId"" = @parentId
                  AND ""CompanyId"" = @companyId
                LIMIT 1", connection, transaction);
            command.Parameters.AddWithValue("parentId", requestedParentId.Value);
            command.Parameters.AddWithValue("companyId", companyId);
            return await command.ExecuteScalarAsync() != null ? requestedParentId : existingParentId;
        }

        private static void AddNullableInt(NpgsqlCommand command, string name, int? value)
        {
            var parameter = command.Parameters.Add(name, NpgsqlDbType.Integer);
            parameter.Value = value.HasValue ? value.Value : DBNull.Value;
        }

        private async Task<Dictionary<string, string>> ResolvePcVueUnitsAsync(
            PCVueWebServiceSettings settings,
            List<string> names)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var token = await _pcvueWebService.GetValidAccessTokenAsync(settings);
            if (string.IsNullOrWhiteSpace(token))
                throw new InvalidOperationException("Unable to authenticate with PCVue.");

            foreach (var batch in names.Chunk(200))
            {
                var response = await SendBulkReadAsync(settings.BaseUrl, token, batch);
                if (response.StatusCode == HttpStatusCode.Unauthorized)
                {
                    response.Dispose();
                    token = await _pcvueWebService.GetValidAccessTokenAsync(settings, forceRefresh: true);
                    if (string.IsNullOrWhiteSpace(token))
                        throw new InvalidOperationException("PCVue session expired while reading variable units.");
                    response = await SendBulkReadAsync(settings.BaseUrl, token, batch);
                }

                using (response)
                {
                    var raw = await response.Content.ReadAsStringAsync();
                    if (!response.IsSuccessStatusCode)
                    {
                        _logger.LogWarning(
                            "PCVue BulkRead unit lookup failed: {Status} {Body}",
                            response.StatusCode,
                            Truncate(raw, 400));
                        continue;
                    }

                    using var document = JsonDocument.Parse(raw);
                    if (document.RootElement.ValueKind != JsonValueKind.Object) continue;

                    foreach (var property in document.RootElement.EnumerateObject())
                    {
                        var variableName = property.Name;
                        var unit = string.Empty;
                        if (TryGetPropertyInsensitive(property.Value, "properties", out var properties) &&
                            properties.ValueKind == JsonValueKind.Array)
                        {
                            var values = properties.EnumerateArray().ToArray();
                            if (values.Length > 0 &&
                                values[0].ValueKind == JsonValueKind.String &&
                                !string.IsNullOrWhiteSpace(values[0].GetString()))
                            {
                                variableName = values[0].GetString()!;
                            }
                            if (values.Length > 1 && values[1].ValueKind == JsonValueKind.String)
                            {
                                unit = values[1].GetString()?.Trim() ?? string.Empty;
                            }
                        }
                        result[variableName] = unit;
                    }
                }
            }

            return result;
        }

        private async Task<HttpResponseMessage> SendBulkReadAsync(
            string baseUrl,
            string token,
            IEnumerable<string> variables)
        {
            var endpoint = $"{baseUrl.TrimEnd('/')}/RealTimeData/v2/BulkRead";
            var payload = JsonSerializer.Serialize(new
            {
                Variables = variables.ToArray(),
                Properties = new[] { "VariableName", "Unit" }
            });

            var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            request.Content = new StringContent(payload, Encoding.UTF8, "application/json");
            return await _pcvueWebService.HttpClient.SendAsync(request);
        }

        private void StartBackgroundTrendsImport(
            IReadOnlyDictionary<string, int> meterIds,
            DateTime startDate,
            DateTime endDate,
            PCVueWebServiceSettings settings,
            int companyId)
        {
            var variableNames = meterIds.Keys.ToList();
            var meterMap = meterIds.ToDictionary(
                pair => pair.Key,
                pair => pair.Value,
                StringComparer.OrdinalIgnoreCase);

            _ = Task.Run(async () =>
            {
                using var scope = _scopeFactory.CreateScope();
                var trendsService = scope.ServiceProvider.GetRequiredService<TrendsService>();
                var databaseService = scope.ServiceProvider.GetRequiredService<DatabaseService>();
                var logger = scope.ServiceProvider.GetRequiredService<ILogger<WebServicesImportController>>();

                await ImportLock.Gate.WaitAsync();
                try
                {
                    logger.LogInformation(
                        "Background Trends Import Started for {Count} variables...",
                        variableNames.Count);
                    var trends = await trendsService.ProcessVariablesTrendsAsync(
                        variableNames,
                        startDate,
                        endDate,
                        settings);

                    await using var connection = new NpgsqlConnection(databaseService.GetConnectionString());
                    await connection.OpenAsync();
                    await using var transaction = await connection.BeginTransactionAsync();

                    try
                    {
                        await using (var createTemp = new NpgsqlCommand(@"
                            CREATE TEMP TABLE ""TempMeterReadingsManual"" (LIKE ""MeterReadings"" EXCLUDING CONSTRAINTS) ON COMMIT DROP;
                            ALTER TABLE ""TempMeterReadingsManual"" DROP COLUMN ""ReadingId"";", connection, transaction))
                        {
                            await createTemp.ExecuteNonQueryAsync();
                        }

                        await using (var writer = await connection.BeginBinaryImportAsync(@"
                            COPY ""TempMeterReadingsManual"" (""MeterId"", ""Timestamp"", ""Value"", ""Quality"", ""CompanyId"")
                            FROM STDIN (FORMAT BINARY)"))
                        {
                            foreach (var result in trends)
                            {
                                if (!result.Success ||
                                    result.TrendData == null ||
                                    !meterMap.TryGetValue(result.VariableName, out var meterId))
                                {
                                    continue;
                                }

                                foreach (var point in result.TrendData)
                                {
                                    if (!point.TimestampParsed.HasValue) continue;
                                    await writer.StartRowAsync();
                                    await writer.WriteAsync(meterId, NpgsqlDbType.Integer);
                                    await writer.WriteAsync(point.TimestampParsed.Value, NpgsqlDbType.Timestamp);
                                    await writer.WriteAsync(Convert.ToDecimal(point.Value), NpgsqlDbType.Numeric);
                                    await writer.WriteAsync(point.IsGoodQuality ? 192 : 0, NpgsqlDbType.Integer);
                                    await writer.WriteAsync(companyId, NpgsqlDbType.Integer);
                                }
                            }
                            await writer.CompleteAsync();
                        }

                        await using var insert = new NpgsqlCommand(@"
                            INSERT INTO ""MeterReadings"" (""MeterId"", ""Timestamp"", ""Value"", ""Quality"", ""CompanyId"")
                            SELECT ""MeterId"", ""Timestamp"", ""Value"", ""Quality"", ""CompanyId""
                            FROM ""TempMeterReadingsManual""
                            ON CONFLICT (""MeterId"", ""Timestamp"") DO NOTHING", connection, transaction);
                        insert.CommandTimeout = 300;
                        await insert.ExecuteNonQueryAsync();
                        await transaction.CommitAsync();

                        logger.LogInformation("SUCCESS: Background Trends bulk insert completed!");
                    }
                    catch (Exception ex)
                    {
                        await transaction.RollbackAsync();
                        logger.LogError(ex, "ERROR during background Trends bulk insert");
                    }
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Fatal error in background Trends import");
                }
                finally
                {
                    ImportLock.Gate.Release();
                }
            });
        }

        private async Task<PCVueWebServiceSettings?> GetWebServiceConnectionById(string connectionId)
        {
            try
            {
                var companyId = _companyContext.CurrentCompanyId;
                return await _databaseService.ExecuteWithCompanyIsolationAsync(companyId, async (conn, tr) =>
                {
                    const string sql = @"
                        SELECT ""ConnectionId"", ""ConnectionName"", ""BaseUrl"", ""ClientId"", ""ClientSecret"",
                               ""ApiKey"", ""Username"", ""Password"", ""AuthType"", ""TimeoutSeconds"",
                               ""ProjectName"", ""IsDefault""
                        FROM ""WebServiceConnections""
                        WHERE ""ConnectionId"" = @connId
                          AND ""CompanyId"" = @companyId
                        LIMIT 1";

                    await using var cmd = new NpgsqlCommand(sql, conn, tr);
                    cmd.Parameters.AddWithValue("connId", connectionId);
                    cmd.Parameters.AddWithValue("companyId", companyId);
                    await using var reader = await cmd.ExecuteReaderAsync();
                    if (!await reader.ReadAsync()) return null;

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
                        AuthType = reader.IsDBNull(8)
                            ? AuthenticationType.OAuth
                            : (AuthenticationType)Convert.ToInt32(reader.GetValue(8)),
                        TimeoutSeconds = reader.IsDBNull(9) ? 30 : Convert.ToInt32(reader.GetValue(9)),
                        ProjectName = reader.IsDBNull(10) ? "" : reader.GetString(10),
                        IsDefault = !reader.IsDBNull(11) && reader.GetBoolean(11)
                    };
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Unable to load Web Service connection {ConnectionId}.", connectionId);
                return null;
            }
        }

        private static bool TryGetPropertyInsensitive(JsonElement element, string name, out JsonElement value)
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                if (element.TryGetProperty(name, out value)) return true;
                foreach (var property in element.EnumerateObject())
                {
                    if (property.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                    {
                        value = property.Value;
                        return true;
                    }
                }
            }
            value = default;
            return false;
        }

        private static string BuildApiError(
            string operation,
            HttpResponseMessage response,
            string content)
            => $"{operation} failed: HTTP {(int)response.StatusCode} ({response.StatusCode})" +
               (string.IsNullOrWhiteSpace(content) ? string.Empty : $" - {Truncate(content, 400)}");

        private static string Truncate(string value, int maxLength)
            => value.Length <= maxLength ? value : value[..maxLength] + "…";

        private sealed record ExistingMeterInfo(
            int MeterId,
            string Name,
            string Unit,
            string Type,
            bool Active,
            int? ParentId);

        private sealed record MeterSnapshot(
            Dictionary<string, ExistingMeterInfo> ByName,
            List<object> ParentOptions);
    }

    public sealed class WebServiceVariableImportRequest
    {
        public List<WebServiceVariableWithTrends> Variables { get; set; } = new();
        public bool SkipExisting { get; set; }
        public bool UpdateExisting { get; set; } = true;
        public bool IncludeSystemVariables { get; set; }
        public bool ImportTrendsData { get; set; } = true;
        public DateTime? TrendsStartDate { get; set; }
        public DateTime? TrendsEndDate { get; set; }
        public string ConnectionId { get; set; } = "";
    }

    public sealed class ResolveVariableUnitsRequest
    {
        public string ConnectionId { get; set; } = "";
        public List<string> VariableNames { get; set; } = new();
    }
}
