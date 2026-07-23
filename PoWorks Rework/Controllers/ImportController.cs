using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Data.SqlClient;
using Npgsql;
using PoWorks_Rework.Models;
using PoWorks_Rework.Services;
using System.Text.Json;
using PoWorks_Rework.Repositories;

namespace PoWorks_Rework.Controllers
{
    public class ImportController : Controller
    {
        #region Constructor and Dependencies

        private readonly ILogger<ImportController> _logger;
        private readonly SqlServerService _sqlServerService;
        private readonly DatabaseService _databaseService;
        private readonly TrendsService _trendsService;
        private readonly MeterRepository _meterRepository;
        private readonly ICompanyContext _companyContext;

        public ImportController(
            ILogger<ImportController> logger,
            SqlServerService sqlServerService,
            DatabaseService databaseService,
            VarexpParserService varexpParserService,
            VariableBrowseParsingService variableBrowseParsingService,
            TrendsService trendsService,
            MeterRepository meterRepository,
            ICompanyContext companyContext)
        {
            _logger = logger;
            _sqlServerService = sqlServerService;
            _databaseService = databaseService;
            _trendsService = trendsService;
            _meterRepository = meterRepository;
            _companyContext = companyContext;
        }

        #endregion

        #region General Controller Actions

        public IActionResult Index()
        {
            var viewModel = new ImportExportViewModel
            {
                HdsTables = new List<string>()
            };
            return View(viewModel);
        }

        [HttpGet]
        public IActionResult GetSqlServerConnections()
        {
            try
            {
                var connections = _sqlServerService.GetAllConnections();
                var connectionData = connections.Select(c => new
                {
                    connectionId = c.ConnectionId,
                    connectionName = c.ConnectionName,
                    host = c.Host,
                    port = c.Port,
                    database = c.Database,
                    isDefault = c.IsDefault
                }).ToList();

                return Json(new { success = true, connections = connectionData });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, error = ex.Message });
            }
        }

        #endregion

        #region Trends Endpoints

        [HttpPost]
        public async Task<IActionResult> GetTrendsData([FromBody] ProcessTrendsRequest request)
        {
            try
            {
                _logger.LogInformation("Processing trends data for {Count} variables", request.VariableNames?.Count ?? 0);
                if (request == null || string.IsNullOrEmpty(request.ConnectionId))
                {
                    return Json(new ProcessTrendsResponse
                    {
                        Success = false,
                        ErrorMessage = "Invalid request: Connection ID is required"
                    });
                }

                if (request.VariableNames == null || request.VariableNames.Count == 0)
                {
                    return Json(new ProcessTrendsResponse
                    {
                        Success = false,
                        ErrorMessage = "No variables specified for trends processing"
                    });
                }

                if (request.StartDate >= request.EndDate)
                {
                    return Json(new ProcessTrendsResponse
                    {
                        Success = false,
                        ErrorMessage = "Invalid date range: Start date must be before end date"
                    });
                }
                var settings = GetWebServiceConnectionById(request.ConnectionId);
                if (settings == null)
                {
                    return Json(new ProcessTrendsResponse
                    {
                        Success = false,
                        ErrorMessage = $"Web service connection '{request.ConnectionId}' not found"
                    });
                }

                var startTime = DateTime.UtcNow;
                var results = await _trendsService.ProcessVariablesTrendsAsync(
                    request.VariableNames,
                    request.StartDate,
                    request.EndDate,
                    settings
                );

                var endTime = DateTime.UtcNow;
                var responseResults = results.Select(r => new VariableTrendsResult
                {
                    VariableName = r.VariableName,
                    Success = r.Success,
                    ErrorMessage = r.ErrorMessage,
                    RequestId = r.RequestId,
                    TrendData = r.TrendData,
                    MaxNumberExceeded = r.MaxNumberExceeded,
                    DataPointsCount = r.TrendData?.Count ?? 0,
                    FirstTimestamp = GetParsedTimestamp(r.TrendData?.FirstOrDefault()?.Timestamp),
                    LastTimestamp = GetParsedTimestamp(r.TrendData?.LastOrDefault()?.Timestamp)
                }).ToList();

                var summary = new TrendsSummary
                {
                    TotalVariables = results.Count,
                    SuccessfulVariables = results.Count(r => r.Success),
                    FailedVariables = results.Count(r => !r.Success),
                    TotalDataPoints = results.Sum(r => r.TrendData?.Count ?? 0),
                    OverallStartTime = request.StartDate,
                    OverallEndTime = request.EndDate,
                    ProcessingDuration = endTime - startTime
                };

                return Json(new ProcessTrendsResponse
                {
                    Success = true,
                    Results = responseResults,
                    Summary = summary,
                    ProcessedAt = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing trends data");
                return Json(new ProcessTrendsResponse
                {
                    Success = false,
                    ErrorMessage = $"Server error: {ex.Message}"
                });
            }
        }

        [HttpGet]
        public IActionResult GetWebServiceConnectionsForTrends()
        {
            try
            {
                var connections = GetAvailableWebServiceConnections();
                return Json(new
                {
                    success = true,
                    connections = connections.Select(c => new
                    {
                        connectionId = c.ConnectionId,
                        connectionName = c.ConnectionName,
                        baseUrl = c.BaseUrl,
                        isDefault = c.IsDefault,
                        status = "Available"
                    })
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, error = ex.Message });
            }
        }

        [HttpPost]
        public async Task<IActionResult> GetTrendsDataForImportedMeters([FromBody] GetTrendsForImportedMetersRequest request)
        {
            var overallStartTime = DateTime.UtcNow;

            try
            {
                var validationResult = ValidateTrendsRequest(request);
                if (!validationResult.IsValid)
                {
                    return Json(new ImportedMetersTrendsResponse
                    {
                        Success = false,
                        ErrorMessage = validationResult.ErrorMessage
                    });
                }
                var settings = GetWebServiceConnectionById(request.ConnectionId);
                if (settings == null)
                {
                    return Json(new ImportedMetersTrendsResponse
                    {
                        Success = false,
                        ErrorMessage = $"WebService connection '{request.ConnectionId}' not found"
                    });
                }
                var importedMeters = await GetImportedMetersForProcessing(request);
                if (importedMeters.Count == 0)
                {
                    return Json(new ImportedMetersTrendsResponse
                    {
                        Success = true,
                        ErrorMessage = "No imported WebService meters found for processing",
                        Summary = new TrendsProcessingSummary
                        {
                            TotalMetersProcessed = 0,
                            ConnectionUsed = settings.ConnectionName,
                            StartTime = overallStartTime,
                            EndTime = DateTime.UtcNow
                        }
                    });
                }
                var meterResults = await ProcessMetersSequentially(importedMeters, request, settings);

                var overallEndTime = DateTime.UtcNow;
                var summary = CreateProcessingSummary(meterResults, overallStartTime, overallEndTime, settings, request);

                return Json(new ImportedMetersTrendsResponse
                {
                    Success = true,
                    MeterResults = meterResults,
                    Summary = summary,
                    ProcessedAt = overallEndTime
                });
            }
            catch (Exception ex)
            {
                var errorMsg = $"Server error during trends processing: {ex.Message}";
                _logger.LogError(ex, "Error processing trends for imported meters");

                return Json(new ImportedMetersTrendsResponse
                {
                    Success = false,
                    ErrorMessage = errorMsg,
                    Summary = new TrendsProcessingSummary
                    {
                        StartTime = overallStartTime,
                        EndTime = DateTime.UtcNow,
                        Errors = new List<string> { errorMsg }
                    }
                });
            }
        }

        private async Task<List<MeterTrendsResult>> ProcessMetersSequentially(
            List<MeterForTrendsAnalysis> meters,
            GetTrendsForImportedMetersRequest request,
            PCVueWebServiceSettings settings)
        {
            var results = new List<MeterTrendsResult>();

            for (int i = 0; i < meters.Count; i++)
            {
                var meter = meters[i];
                var meterStartTime = DateTime.UtcNow;

                try
                {
                    var trendsDataResult = await CallGetTrendsDataEndpoint(meter, request, settings);
                    var importTrendsResult = await CallImportTrendsEndpoint(meter, request, settings);
                    var meterResult = CreateMeterResult(meter, trendsDataResult, importTrendsResult, meterStartTime);

                    results.Add(meterResult);
                    await Task.Delay(200);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing meter {MeterId}: {MeterName}", meter.MeterId, meter.Name);

                    var errorResult = new MeterTrendsResult
                    {
                        MeterId = meter.MeterId,
                        MeterName = meter.Name,
                        OriginalVariableName = meter.OriginalVariableName,
                        GetTrendsDataSuccess = false,
                        GetTrendsDataError = $"Exception: {ex.Message}",
                        ImportTrendsSuccess = false,
                        ImportTrendsError = $"Exception: {ex.Message}",
                        ProcessingDuration = DateTime.UtcNow - meterStartTime
                    };
                    results.Add(errorResult);
                }
            }

            return results;
        }

        private async Task<(bool Success, string? Error, List<TrendDataPoint>? Data, string? RequestId)> CallGetTrendsDataEndpoint(
            MeterForTrendsAnalysis meter,
            GetTrendsForImportedMetersRequest request,
            PCVueWebServiceSettings settings)
        {
            try
            {
                var trendsRequest = new ProcessTrendsRequest
                {
                    ConnectionId = request.ConnectionId,
                    VariableNames = new List<string> { meter.OriginalVariableName },
                    StartDate = request.StartDate,
                    EndDate = request.EndDate
                };
                var serviceResults = await _trendsService.ProcessVariablesTrendsAsync(
                    trendsRequest.VariableNames,
                    trendsRequest.StartDate,
                    trendsRequest.EndDate,
                    settings
                );

                var result = serviceResults.FirstOrDefault();
                if (result != null)
                {
                    return (result.Success, result.ErrorMessage, result.TrendData, result.RequestId);
                }
                return (false, "No result returned from trends service", null, null);
            }
            catch (Exception ex)
            {
                return (false, $"Exception: {ex.Message}", null, null);
            }
        }

        private async Task<(bool Success, string? Error, string Action, int ImportedPoints)> CallImportTrendsEndpoint(
            MeterForTrendsAnalysis meter,
            GetTrendsForImportedMetersRequest request,
            PCVueWebServiceSettings settings)
        {
            try
            {
                var importRequest = new ImportWebServiceVariablesWithTrendsRequest
                {
                    Variables = new List<WebServiceVariableWithTrends>
                    {
                        new WebServiceVariableWithTrends
                        {
                            VariableName = meter.OriginalVariableName,
                            Unit = meter.Unit,
                            Type = meter.Type.ToLower(),
                            Active = meter.Active,
                            TrendsDataAvailable = false
                        }
                    },
                    ConnectionId = request.ConnectionId,
                    ImportTrendsData = true,
                    TrendsStartDate = request.StartDate,
                    TrendsEndDate = request.EndDate,
                    SkipExisting = true,
                    UpdateExisting = false
                };
                return (true, null, "Skipped (already exists)", 0);
            }
            catch (Exception ex)
            {
                return (false, $"Exception: {ex.Message}", "Failed", 0);
            }
        }

        #endregion

        #region HDS (Historical Data Server) FUNCTIONALITY

        [HttpGet]
        public async Task<IActionResult> GetTables(string connectionId = null)
        {
            try
            {
                if (!_sqlServerService.IsInitialized)
                {
                    return Json(new { success = false, error = "SQL Server connection not configured" });
                }
                var tables = await _sqlServerService.GetAvailableTables(connectionId);

                return Json(new
                {
                    success = true,
                    tables = tables,
                    connectionId = connectionId
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, error = $"Error retrieving tables: {ex.Message}" });
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetHdsTables(string connectionId = null)
        {
            try
            {
                if (!_sqlServerService.IsInitialized)
                {
                    return Json(new { success = false, error = "SQL Server connection not configured" });
                }

                var tables = await _sqlServerService.GetAvailableTables(connectionId);
                return Json(new { success = true, tables = tables });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, error = ex.Message });
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetMetersFromTable(string tableName, string connectionId = null, string startDate = null, string endDate = null, int limit = 1000)
        {
            try
            {
                if (!_sqlServerService.IsInitialized)
                {
                    return Json(new { success = false, error = "SQL Server connection not configured" });
                }

                if (string.IsNullOrWhiteSpace(tableName))
                {
                    return Json(new { success = false, error = "Table name is required" });
                }
                if (limit <= 0) limit = 1000;
                if (limit > 10000) limit = 10000;

                var tableExists = await _sqlServerService.ValidateTableExists(tableName, connectionId);
                if (!tableExists)
                {
                    return Json(new
                    {
                        success = false,
                        error = $"Table '{tableName}' does not exist or is not accessible on the selected connection."
                    });
                }

                var hdsMeters = await _sqlServerService.GetDistinctMeterNames(tableName, limit, connectionId);
                var parentOptions = await GetParentMeterOptions();

                return Json(new
                {
                    success = true,
                    meters = hdsMeters,
                    parentOptions = parentOptions,
                    actualCount = hdsMeters.Count,
                    requestedLimit = limit,
                    tableName = tableName,
                    connectionId = connectionId
                });
            }
            catch (Microsoft.Data.SqlClient.SqlException sqlEx)
            {
                string errorMessage = "Database error occurred";
                switch (sqlEx.Number)
                {
                    case 208: errorMessage = $"Table '{tableName}' does not exist or is not accessible"; break;
                    case 102: errorMessage = "Invalid SQL syntax - please check table name format"; break;
                    case 2: errorMessage = "Connection timeout - please check connection settings"; break;
                    case 18456: errorMessage = "Authentication failed - please check connection credentials"; break;
                    default: errorMessage = $"SQL Server error: {sqlEx.Message}"; break;
                }

                return Json(new { success = false, error = errorMessage });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, error = $"Unexpected error: {ex.Message}" });
            }
        }

        [HttpPost]
        public async Task<IActionResult> ImportMeters([FromBody] ImportMetersRequest request)
        {
            try
            {
                if (request?.Meters == null || request.Meters.Count == 0)
                {
                    return Json(new { success = false, error = "No meters selected for import" });
                }

                if (!_databaseService.IsInitialized)
                {
                    return Json(new { success = false, error = "PostgreSQL database not configured" });
                }

                int importedCount = 0;
                int skippedCount = 0;
                int updatedCount = 0;
                int errorCount = 0;
                var errorMeters = new List<string>();
                var detailedErrors = new Dictionary<string, string>();

                int currentCompanyId = _companyContext.CurrentCompanyId;

                using (var connection = new NpgsqlConnection(_databaseService.GetConnectionString()))
                {
                    await connection.OpenAsync();
                    using var transaction = await connection.BeginTransactionAsync();

                    try
                    {
                        foreach (var meter in request.Meters)
                        {
                            try
                            {
                                if (string.IsNullOrWhiteSpace(meter.HdsMeterName))
                                {
                                    skippedCount++;
                                    continue;
                                }

                                bool meterExists = false;
                                int existingMeterId = 0;

                                using (var checkCommand = new NpgsqlCommand(
                                    @"SELECT ""MeterId"" FROM ""Meters"" WHERE ""Name"" = @Name AND ""CompanyId"" = @CompanyId", connection, transaction))
                                {
                                    checkCommand.Parameters.AddWithValue("@Name", meter.HdsMeterName);
                                    checkCommand.Parameters.AddWithValue("@CompanyId", currentCompanyId);
                                    var result = await checkCommand.ExecuteScalarAsync();

                                    meterExists = result != null;
                                    if (meterExists)
                                        existingMeterId = Convert.ToInt32(result);
                                }

                                if (meterExists && request.SkipExisting && !request.UpdateExisting)
                                {
                                    skippedCount++;
                                    continue;
                                }

                                int? parentId = null;

                                if (!string.IsNullOrEmpty(meter.ParentMeterId))
                                {
                                    if (int.TryParse(meter.ParentMeterId, out int parsedParentId))
                                    {
                                        using (var parentCheckCommand = new NpgsqlCommand(
                                            @"SELECT COUNT(*) FROM ""Meters"" WHERE ""MeterId"" = @MeterId AND ""CompanyId"" = @CompanyId", connection, transaction))
                                        {
                                            parentCheckCommand.Parameters.AddWithValue("@MeterId", parsedParentId);
                                            parentCheckCommand.Parameters.AddWithValue("@CompanyId", currentCompanyId);
                                            int parentCount = Convert.ToInt32(await parentCheckCommand.ExecuteScalarAsync());

                                            if (parentCount > 0)
                                            {
                                                parentId = parsedParentId;
                                            }
                                            else
                                            {
                                                parentId = null;
                                            }
                                        }
                                    }
                                }

                                int lastReading = 0;
                                if (!string.IsNullOrEmpty(meter.LastReading) && int.TryParse(meter.LastReading, out int parsedReading))
                                {
                                    lastReading = parsedReading;
                                }

                                string type = "main";
                                if (!string.IsNullOrWhiteSpace(meter.Type) &&
                                    (meter.Type.ToLower() == "main" || meter.Type.ToLower() == "sub"))
                                {
                                    type = meter.Type.ToLower();
                                }

                                if (meterExists && request.UpdateExisting)
                                {
                                    using (var updateCommand = new NpgsqlCommand(
                                        @"UPDATE ""Meters"" SET 
                                          ""Unit"" = @Unit,
                                          ""ParentId"" = @ParentId,
                                          ""LastReading"" = @LastReading,
                                          ""Type"" = @Type,
                                          ""Active"" = @Active
                                          WHERE ""MeterId"" = @MeterId AND ""CompanyId"" = @CompanyId", connection, transaction))
                                    {
                                        updateCommand.Parameters.AddWithValue("@MeterId", existingMeterId);
                                        updateCommand.Parameters.AddWithValue("@CompanyId", currentCompanyId);
                                        updateCommand.Parameters.AddWithValue("@Unit", meter.Unit ?? "");
                                        updateCommand.Parameters.AddWithValue("@ParentId", parentId.HasValue ? parentId.Value : DBNull.Value);
                                        updateCommand.Parameters.AddWithValue("@LastReading", lastReading);
                                        updateCommand.Parameters.AddWithValue("@Type", type);
                                        updateCommand.Parameters.AddWithValue("@Active", meter.Active);

                                        int rowsAffected = await updateCommand.ExecuteNonQueryAsync();
                                        if (rowsAffected > 0) updatedCount++;
                                    }
                                }
                                else if (!meterExists)
                                {
                                    using (var insertCommand = new NpgsqlCommand(
                                        @"INSERT INTO ""Meters"" (""Name"", ""Unit"", ""ParentId"", ""LastReading"", ""Type"", ""Active"", ""CompanyId"")
                                          VALUES (@Name, @Unit, @ParentId, @LastReading, @Type, @Active, @CompanyId)
                                          RETURNING ""MeterId""", connection, transaction))
                                    {
                                        insertCommand.Parameters.AddWithValue("@Name", meter.HdsMeterName);
                                        insertCommand.Parameters.AddWithValue("@Unit", meter.Unit ?? "");
                                        insertCommand.Parameters.AddWithValue("@ParentId", parentId.HasValue ? parentId.Value : DBNull.Value);
                                        insertCommand.Parameters.AddWithValue("@LastReading", lastReading);
                                        insertCommand.Parameters.AddWithValue("@Type", type);
                                        insertCommand.Parameters.AddWithValue("@Active", meter.Active);
                                        insertCommand.Parameters.AddWithValue("@CompanyId", currentCompanyId);

                                        await insertCommand.ExecuteScalarAsync();
                                        importedCount++;
                                    }
                                }
                                else
                                {
                                    skippedCount++;
                                }
                            }
                            catch (Exception ex)
                            {
                                errorCount++;
                                errorMeters.Add(meter.HdsMeterName);
                                detailedErrors[meter.HdsMeterName] = ex.Message;
                            }
                        }
                        await transaction.CommitAsync();

                        return Json(new
                        {
                            success = errorCount == 0,
                            importedCount,
                            updatedCount,
                            skippedCount,
                            errorCount,
                            errorMeters,
                            detailedErrors,
                            message = $"Successfully imported {importedCount} meters, updated {updatedCount}, skipped {skippedCount}, with {errorCount} errors."
                        });
                    }
                    catch (Exception ex)
                    {
                        await transaction.RollbackAsync();
                        throw new Exception($"Failed to import meters: {ex.Message}", ex);
                    }
                }
            }
            catch (Exception ex)
            {
                return Json(new { success = false, error = ex.Message });
            }
        }

        [HttpPost]
        public async Task<IActionResult> ImportMeterReadings([FromBody] ImportReadingsRequest request)
        {
            try
            {
                if (request == null || string.IsNullOrEmpty(request.TableName))
                {
                    return Json(new { success = false, error = "Missing table name" });
                }

                if (request.MeterNames == null || request.MeterNames.Count == 0)
                {
                    return Json(new { success = false, error = "No meter names provided" });
                }

                if (!_databaseService.IsInitialized || !_sqlServerService.IsInitialized)
                {
                    return Json(new { success = false, error = "Database connections not initialized" });
                }

                int totalReadingsImported = 0;
                int totalMetersProcessed = 0;
                var errorMeters = new List<string>();
                var detailedErrors = new Dictionary<string, string>();

                int currentCompanyId = _companyContext.CurrentCompanyId;

                foreach (var meterName in request.MeterNames)
                {
                    try
                    {
                        int? meterId = null;
                        using (var pgConnection = new NpgsqlConnection(_databaseService.GetConnectionString()))
                        {
                            await pgConnection.OpenAsync();
                      
                            using var cmd = new NpgsqlCommand(@"SELECT ""MeterId"" FROM ""Meters"" WHERE ""Name"" = @Name AND ""CompanyId"" = @CompanyId", pgConnection);
                            cmd.Parameters.AddWithValue("@Name", meterName);
                            cmd.Parameters.AddWithValue("@CompanyId", currentCompanyId);
                            var result = await cmd.ExecuteScalarAsync();
                            if (result != null)
                            {
                                meterId = Convert.ToInt32(result);
                            }
                        }

                        if (!meterId.HasValue)
                        {
                            errorMeters.Add(meterName);
                            detailedErrors[meterName] = "Meter not found in database for your company";
                            continue;
                        }

                        var readings = new List<(DateTime timestamp, double value, int quality)>();

                        using (var sqlConnection = _sqlServerService.GetConnection())
                        {
                            await sqlConnection.OpenAsync();
                            string sql = $"SELECT Chrono, Value, Quality FROM {request.TableName} WHERE NAME = @Name";

                            if (request.StartDate.HasValue)
                                sql += " AND Chrono >= @StartDate";

                            if (request.EndDate.HasValue)
                                sql += " AND Chrono <= @EndDate";

                            sql += " ORDER BY Chrono";

                            if (request.Limit.HasValue)
                                sql = $"SELECT TOP {request.Limit} * FROM ({sql}) AS ordered_readings";

                            using var cmd = new SqlCommand(sql, sqlConnection);
                            cmd.Parameters.AddWithValue("@Name", meterName);

                            if (request.StartDate.HasValue)
                                cmd.Parameters.AddWithValue("@StartDate", request.StartDate.Value.ToFileTimeUtc());

                            if (request.EndDate.HasValue)
                                cmd.Parameters.AddWithValue("@EndDate", request.EndDate.Value.ToFileTimeUtc());

                            using var reader = await cmd.ExecuteReaderAsync();
                            while (await reader.ReadAsync())
                            {
                                try
                                {
                                    long chrono = reader.GetInt64(0);
                                    double value = reader.GetDouble(1);
                                    int quality = reader.GetInt16(2);
                                    DateTime timestamp = DateTime.FromFileTimeUtc(chrono);

                                    readings.Add((timestamp, value, quality));
                                }
                                catch (Exception) { /* Ignorer les erreurs de parsing mineures */ }
                            }
                        }

                        if (readings.Count > 0)
                        {
                            using (var pgConnection = new NpgsqlConnection(_databaseService.GetConnectionString()))
                            {
                                await pgConnection.OpenAsync();
                                using var transaction = await pgConnection.BeginTransactionAsync();

                                try
                                {
                                    foreach (var reading in readings)
                                    {
                                        using var insertCmd = new NpgsqlCommand(
                                            @"INSERT INTO ""MeterReadings"" (""MeterId"", ""Timestamp"", ""Value"", ""Quality"") 
                                              VALUES (@MeterId, @Timestamp, @Value, @Quality) 
                                              ON CONFLICT (""MeterId"", ""Timestamp"") DO NOTHING",
                                            pgConnection, transaction);

                                        insertCmd.Parameters.AddWithValue("@MeterId", meterId.Value);
                                        insertCmd.Parameters.AddWithValue("@Timestamp", reading.timestamp);
                                        insertCmd.Parameters.AddWithValue("@Value", reading.value);
                                        insertCmd.Parameters.AddWithValue("@Quality", reading.quality);

                                        await insertCmd.ExecuteNonQueryAsync();
                                    }

                                    await transaction.CommitAsync();
                                    totalReadingsImported += readings.Count;
                                    totalMetersProcessed++;
                                }
                                catch (Exception ex)
                                {
                                    await transaction.RollbackAsync();
                                    errorMeters.Add(meterName);
                                    detailedErrors[meterName] = ex.Message;
                                }
                            }
                        }
                        else
                        {
                            totalMetersProcessed++;
                        }
                    }
                    catch (Exception ex)
                    {
                        errorMeters.Add(meterName);
                        detailedErrors[meterName] = ex.Message;
                    }
                }

                return Json(new
                {
                    success = errorMeters.Count == 0,
                    totalReadingsImported,
                    totalMetersProcessed,
                    errorMeters,
                    detailedErrors
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, error = ex.Message });
            }
        }

        [HttpPost]
        public IActionResult PrintHDSMeters([FromBody] PrintHDSMetersRequest request)
        {
            try
            {
                return Json(new { success = true, count = request?.SelectedMeters?.Count ?? 0 });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, error = ex.Message });
            }
        }

        #endregion

        #region Helper Methods

        private DateTime? GetParsedTimestamp(string? timestamp)
        {
            if (string.IsNullOrEmpty(timestamp)) return null;
            if (DateTime.TryParse(timestamp, out var result)) return result;
            return null;
        }

        private PCVueWebServiceSettings? GetWebServiceConnectionById(string connectionId)
        {
            var webServiceSection = HttpContext.RequestServices.GetRequiredService<IConfiguration>().GetSection("WebServiceConnections");

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
                        AuthType = Enum.Parse<AuthenticationType>(connectionSection["AuthType"] ?? "0"),
                        TimeoutSeconds = int.Parse(connectionSection["TimeoutSeconds"] ?? "30"),
                        ProjectName = connectionSection["ProjectName"] ?? "",
                        IsDefault = bool.Parse(connectionSection["IsDefault"] ?? "false")
                    };
                }
            }
            return null;
        }

        private List<PCVueWebServiceSettings> GetAvailableWebServiceConnections()
        {
            var connections = new List<PCVueWebServiceSettings>();
            try
            {
                var appSettingsPath = Path.Combine(Directory.GetCurrentDirectory(), "appsettings.json");
                if (!System.IO.File.Exists(appSettingsPath)) return connections;

                var json = System.IO.File.ReadAllText(appSettingsPath);
                using var document = JsonDocument.Parse(json);

                if (document.RootElement.TryGetProperty("PCVueWebServiceSettings", out var settingsElement) &&
                    settingsElement.TryGetProperty("Connections", out var connectionsElement))
                {
                    foreach (var connectionElement in connectionsElement.EnumerateArray())
                    {
                        connections.Add(new PCVueWebServiceSettings
                        {
                            ConnectionId = connectionElement.GetProperty("ConnectionId").GetString() ?? "",
                            ConnectionName = connectionElement.GetProperty("ConnectionName").GetString() ?? "",
                            BaseUrl = connectionElement.GetProperty("BaseUrl").GetString() ?? "",
                            ClientId = connectionElement.GetProperty("ClientId").GetString() ?? "",
                            ClientSecret = connectionElement.GetProperty("ClientSecret").GetString() ?? "",
                            Username = connectionElement.GetProperty("Username").GetString() ?? "",
                            Password = connectionElement.GetProperty("Password").GetString() ?? "",
                            AuthType = (AuthenticationType)connectionElement.GetProperty("AuthType").GetInt32(),
                            TimeoutSeconds = connectionElement.GetProperty("TimeoutSeconds").GetInt32(),
                            ProjectName = connectionElement.GetProperty("ProjectName").GetString() ?? "",
                            IsDefault = connectionElement.TryGetProperty("IsDefault", out var isDefaultElement) && isDefaultElement.GetBoolean()
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reading web service connections");
            }
            return connections;
        }

        #endregion

        #region UTILITY METHODS & HELPERS

        [HttpPost]
        public IActionResult PrintSelectedMeters([FromBody] PrintMetersRequest request)
        {
            return Json(new { success = true, count = request?.SelectedMeterNames?.Count ?? 0 });
        }

        private async Task<List<SelectListItem>> GetParentMeterOptions()
        {
            var options = new List<SelectListItem>
            {
                new SelectListItem { Value = "", Text = "None" }
            };

            try
            {
                if (_databaseService.IsInitialized)
                {
                    int currentCompanyId = _companyContext.CurrentCompanyId;

                    using (var connection = _databaseService.GetConnection())
                    {
                        // 🟢 FIX : Isolation CompanyId pour les compteurs parents !
                        var command = new Npgsql.NpgsqlCommand(@"
                    SELECT ""MeterId"", ""Name"" 
                    FROM ""Meters"" 
                    WHERE ""Type"" = 'main' AND ""Active"" = true AND ""CompanyId"" = @CompanyId
                    ORDER BY ""Name""", connection);

                        command.Parameters.AddWithValue("@CompanyId", currentCompanyId);

                        using (var reader = await command.ExecuteReaderAsync())
                        {
                            while (await reader.ReadAsync())
                            {
                                options.Add(new SelectListItem
                                {
                                    Value = reader.GetInt32(0).ToString(),
                                    Text = reader.GetString(1)
                                });
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting parent meter options");
            }

            return options;
        }

        private (bool IsValid, string? ErrorMessage) ValidateTrendsRequest(GetTrendsForImportedMetersRequest request)
        {
            if (request == null) return (false, "Request cannot be null");
            if (string.IsNullOrEmpty(request.ConnectionId)) return (false, "Connection ID is required");
            if (request.StartDate >= request.EndDate) return (false, "Start date must be before end date");
            if (request.EndDate > DateTime.UtcNow) return (false, "End date cannot be in the future");
            var timeSpan = request.EndDate - request.StartDate;
            if (timeSpan.TotalDays > 365) return (false, "Date range cannot exceed 365 days");
            return (true, null);
        }

        private async Task<List<MeterForTrendsAnalysis>> GetImportedMetersForProcessing(GetTrendsForImportedMetersRequest request)
        {
            List<MeterForTrendsAnalysis> meters;
            if (request.GetAllImported)
            {
                meters = await _meterRepository.GetWebServiceImportedMetersAsync(request.ActiveOnly, request.MeterLimit);
            }
            else if (request.SpecificMeterIds.Any())
            {
                meters = new List<MeterForTrendsAnalysis>();
            }
            else
            {
                meters = new List<MeterForTrendsAnalysis>();
            }

            foreach (var meter in meters)
            {
                meter.AssignedConnectionId = request.ConnectionId;
            }
            return meters;
        }

        private MeterTrendsResult CreateMeterResult(
            MeterForTrendsAnalysis meter,
            (bool Success, string? Error, List<TrendDataPoint>? Data, string? RequestId) trendsResult,
            (bool Success, string? Error, string Action, int ImportedPoints) importResult,
            DateTime startTime)
        {
            return new MeterTrendsResult
            {
                MeterId = meter.MeterId,
                MeterName = meter.Name,
                OriginalVariableName = meter.OriginalVariableName,
                GetTrendsDataSuccess = trendsResult.Success,
                GetTrendsDataError = trendsResult.Error,
                TrendsData = trendsResult.Data,
                TrendsDataPointsCount = trendsResult.Data?.Count ?? 0,
                TrendsRequestId = trendsResult.RequestId,
                ImportTrendsSuccess = importResult.Success,
                ImportTrendsError = importResult.Error,
                ImportAction = importResult.Action,
                ImportedDataPoints = importResult.ImportedPoints,
                ProcessingDuration = DateTime.UtcNow - startTime
            };
        }

        private TrendsProcessingSummary CreateProcessingSummary(
            List<MeterTrendsResult> results,
            DateTime startTime,
            DateTime endTime,
            PCVueWebServiceSettings settings,
            GetTrendsForImportedMetersRequest request)
        {
            return new TrendsProcessingSummary
            {
                TotalMetersProcessed = results.Count,
                SuccessfulMeters = results.Count(r => r.GetTrendsDataSuccess),
                FailedMeters = results.Count(r => !r.GetTrendsDataSuccess),
                TotalDataPointsRetrieved = results.Sum(r => r.TrendsDataPointsCount),
                TotalDataPointsImported = results.Sum(r => r.ImportedDataPoints),
                TotalProcessingTime = endTime - startTime,
                ConnectionUsed = settings.ConnectionName,
                StartTime = startTime,
                EndTime = endTime,
                Errors = results.Where(r => !string.IsNullOrEmpty(r.GetTrendsDataError))
                                .Select(r => $"{r.MeterName}: {r.GetTrendsDataError}")
                                .ToList()
            };
        }

        #endregion
    }
}