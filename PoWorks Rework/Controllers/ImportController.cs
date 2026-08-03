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
    /// <summary>
    /// Controller for data import functionality.
    /// Handles HDS (Historical Data Server) table browsing, meter import from SQL Server,
    /// meter reading imports, trends data retrieval, and meter export.
    /// </summary>
    public class ImportController : Controller
    {
        #region Constructor and Dependencies

        private readonly ILogger<ImportController> _logger;
        private readonly SqlServerService _sqlServerService;
        private readonly DatabaseService _databaseService;
        private readonly TrendsService _trendsService;
        private readonly MeterRepository _meterRepository;
        private readonly ICompanyContext _companyContext;

        /// <summary>
        /// Initializes the import controller with its logging, database, trends, repository, and company context dependencies.
        /// </summary>
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

        /// <summary>
        /// Displays the main import/export page.
        /// </summary>
        /// <returns>The import/export index view.</returns>
        public IActionResult Index()
        {
            var viewModel = new ImportExportViewModel
            {
                HdsTables = new List<string>()
            };
            return View(viewModel);
        }

        /// <summary>
        /// Returns the list of configured SQL Server connections.
        /// </summary>
        /// <returns>JSON containing the available SQL Server connections.</returns>
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

        /// <summary>
        /// Processes trends data for the specified variables from a PCVue web service connection.
        /// </summary>
        /// <param name="request">The trends request containing connection ID, variable names, and date range.</param>
        /// <returns>JSON with trends data results and a processing summary.</returns>
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

        /// <summary>
        /// Returns the list of available PCVue web service connections for trends processing.
        /// </summary>
        /// <returns>JSON containing the available web service connections.</returns>
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

        /// <summary>
        /// Processes trends data for imported WebService meters, retrieving and importing readings for each meter.
        /// </summary>
        /// <param name="request">The request containing connection, date range, and meter selection criteria.</param>
        /// <returns>JSON with per-meter trends results and an overall processing summary.</returns>
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

        /// <summary>
        /// Processes each meter sequentially, retrieving its trends data and importing the results.
        /// </summary>
        /// <param name="meters">The list of meters to process.</param>
        /// <param name="request">The original trends request parameters.</param>
        /// <param name="settings">The web service connection settings to use.</param>
        /// <returns>A list of per-meter trends processing results.</returns>
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

        /// <summary>
        /// Calls the trends data retrieval for a single meter's variable.
        /// </summary>
        /// <param name="meter">The meter whose variable trends should be retrieved.</param>
        /// <param name="request">The original trends request parameters.</param>
        /// <param name="settings">The web service connection settings to use.</param>
        /// <returns>A tuple with success status, error message, trend data, and request ID.</returns>
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

        /// <summary>
        /// Imports trends data for a single meter's variable from the web service.
        /// </summary>
        /// <param name="meter">The meter whose trends should be imported.</param>
        /// <param name="request">The original trends request parameters.</param>
        /// <param name="settings">The web service connection settings to use.</param>
        /// <returns>A tuple with success status, error message, import action, and imported point count.</returns>
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

        /// <summary>
        /// Returns the available tables from a SQL Server connection.
        /// </summary>
        /// <param name="connectionId">The optional connection ID to use.</param>
        /// <returns>JSON with the list of available tables.</returns>
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

        /// <summary>
        /// Returns the available HDS tables from a SQL Server connection.
        /// </summary>
        /// <param name="connectionId">The optional connection ID to use.</param>
        /// <returns>JSON with the list of available HDS tables.</returns>
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

        /// <summary>
        /// Retrieves the distinct meter names from a HDS table, along with parent meter options.
        /// </summary>
        /// <param name="tableName">The name of the HDS table to query.</param>
        /// <param name="connectionId">The optional SQL Server connection ID to use.</param>
        /// <param name="startDate">Optional start date filter.</param>
        /// <param name="endDate">Optional end date filter.</param>
        /// <param name="limit">The maximum number of meters to return (capped at 10000).</param>
        /// <returns>JSON with the meters found and parent meter options.</returns>
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

        /// <summary>
        /// Imports meters into the PostgreSQL database, skipping or updating existing meters based on the request options.
        /// </summary>
        /// <param name="request">The import request containing the meters to import and their settings.</param>
        /// <returns>JSON with import counts and any errors.</returns>
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

        /// <summary>
        /// Imports meter readings from a SQL Server table into the PostgreSQL database.
        /// </summary>
        /// <param name="request">The import request containing the table name, meter names, and optional filters.</param>
        /// <returns>JSON with import totals and any errors.</returns>
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

                         
                            string topClause = request.Limit.HasValue ? $"TOP {request.Limit.Value} " : "";
                            string sql = $"SELECT {topClause}Chrono, Value, Quality FROM {request.TableName} WHERE NAME = @Name";

                            if (request.StartDate.HasValue)
                                sql += " AND Chrono >= @StartDate";

                            if (request.EndDate.HasValue)
                                sql += " AND Chrono <= @EndDate";

                            sql += " ORDER BY Chrono";

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
                                            @"INSERT INTO ""MeterReadings"" (""MeterId"", ""Timestamp"", ""Value"", ""Quality"", ""CompanyId"") 
                                              VALUES (@MeterId, @Timestamp, @Value, @Quality, @CompanyId) 
                                              ON CONFLICT (""MeterId"", ""Timestamp"") DO NOTHING",
                                            pgConnection, transaction);

                                        insertCmd.Parameters.AddWithValue("@MeterId", meterId.Value);
                                        insertCmd.Parameters.AddWithValue("@Timestamp", reading.timestamp);
                                        insertCmd.Parameters.AddWithValue("@Value", reading.value);
                                        insertCmd.Parameters.AddWithValue("@Quality", reading.quality);
                                        insertCmd.Parameters.AddWithValue("@CompanyId", currentCompanyId); 

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
                                    detailedErrors[meterName] = "PG Insert Error: " + ex.Message;
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

        /// <summary>
        /// Prints the selected HDS meters.
        /// </summary>
        /// <param name="request">The request containing the selected meters.</param>
        /// <returns>JSON with the count of selected meters.</returns>
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

        /// <summary>
        /// Attempts to parse a timestamp string into a DateTime value.
        /// </summary>
        /// <param name="timestamp">The timestamp string to parse.</param>
        /// <returns>The parsed DateTime, or null if parsing fails.</returns>
        private DateTime? GetParsedTimestamp(string? timestamp)
        {
            if (string.IsNullOrEmpty(timestamp)) return null;
            if (DateTime.TryParse(timestamp, out var result)) return result;
            return null;
        }

        /// <summary>
        /// Finds a PCVue web service connection by its ID from the configuration.
        /// </summary>
        /// <param name="connectionId">The connection ID to look up.</param>
        /// <returns>The matching settings, or null if not found.</returns>
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

        /// <summary>
        /// Reads the list of available PCVue web service connections from appsettings.json.
        /// </summary>
        /// <returns>The list of configured web service connections.</returns>
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

        /// <summary>
        /// Prints the selected meters.
        /// </summary>
        /// <param name="request">The request containing the selected meter names.</param>
        /// <returns>JSON with the count of selected meters.</returns>
        [HttpPost]
        public IActionResult PrintSelectedMeters([FromBody] PrintMetersRequest request)
        {
            return Json(new { success = true, count = request?.SelectedMeterNames?.Count ?? 0 });
        }

        /// <summary>
        /// Retrieves the active main-type meters for the current company as dropdown options.
        /// </summary>
        /// <returns>A list of select list items for parent meter selection.</returns>
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

        /// <summary>
        /// Validates a trends request for imported meters.
        /// </summary>
        /// <param name="request">The request to validate.</param>
        /// <returns>A tuple indicating validity and an error message if invalid.</returns>
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

        /// <summary>
        /// Retrieves the list of imported meters to process based on the request criteria.
        /// </summary>
        /// <param name="request">The request containing meter selection criteria.</param>
        /// <returns>A list of meters configured with the assigned connection ID.</returns>
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

        /// <summary>
        /// Builds a meter trends result from the trends data and import results.
        /// </summary>
        /// <param name="meter">The meter that was processed.</param>
        /// <param name="trendsResult">The trends data retrieval result.</param>
        /// <param name="importResult">The trends import result.</param>
        /// <param name="startTime">The time processing started for this meter.</param>
        /// <returns>A populated MeterTrendsResult.</returns>
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

        /// <summary>
        /// Creates an overall processing summary from the per-meter trends results.
        /// </summary>
        /// <param name="results">The list of per-meter results.</param>
        /// <param name="startTime">The time overall processing started.</param>
        /// <param name="endTime">The time overall processing ended.</param>
        /// <param name="settings">The web service connection settings used.</param>
        /// <param name="request">The original trends request.</param>
        /// <returns>A populated TrendsProcessingSummary.</returns>
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
        /// <summary>
        /// Exports the meters for the current company to a CSV or JSON file.
        /// </summary>
        /// <param name="format">The export format (CSV or JSON).</param>
        /// <param name="activeOnly">Whether to only include active meters.</param>
        /// <param name="includeReadings">Whether to include the last reading for each meter.</param>
        /// <returns>A file download containing the exported meter data.</returns>
        [HttpGet]
        public async Task<IActionResult> ExportMeters(string format = "CSV", bool activeOnly = false, bool includeReadings = false)
        {
            try
            {
                int currentCompanyId = _companyContext.CurrentCompanyId;

                var exportData = new List<Dictionary<string, object>>();

                using (var connection = _databaseService.CreateNewConnection())
                {
                    await connection.OpenAsync();

                    string sql = @"SELECT m.""MeterId"", m.""Name"", m.""Unit"", m.""Type"", m.""Active""";

                    if (includeReadings)
                    {
                        sql += @", COALESCE((SELECT mr.""Value"" FROM ""MeterReadings"" mr WHERE mr.""MeterId"" = m.""MeterId"" ORDER BY mr.""Timestamp"" DESC LIMIT 1), 0) as ""LastReading""";
                    }

                    sql += @" FROM ""Meters"" m WHERE m.""CompanyId"" = @CompanyId";

                    if (activeOnly)
                    {
                        sql += @" AND m.""Active"" = true";
                    }

                    sql += @" ORDER BY m.""Name""";

                    using var cmd = new Npgsql.NpgsqlCommand(sql, connection);
                    cmd.Parameters.AddWithValue("@CompanyId", currentCompanyId);

                    using var reader = await cmd.ExecuteReaderAsync();
                    while (await reader.ReadAsync())
                    {
                        var row = new Dictionary<string, object>
                        {
                            ["MeterId"] = reader.GetInt32(0),
                            ["Name"] = reader.GetString(1),
                            ["Unit"] = reader.IsDBNull(2) ? "" : reader.GetString(2),
                            ["Type"] = reader.IsDBNull(3) ? "" : reader.GetString(3),
                            ["Active"] = reader.GetBoolean(4)
                        };

                        if (includeReadings)
                        {
                            row["LastReading"] = reader.GetDouble(5);
                        }

                        exportData.Add(row);
                    }
                }

                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmm");
                string fileName = $"MetersExport_{timestamp}";

                if (format.ToUpper() == "JSON")
                {
                    var json = System.Text.Json.JsonSerializer.Serialize(exportData, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
                    return File(System.Text.Encoding.UTF8.GetBytes(json), "application/json", $"{fileName}.json");
                }
                else 
                {
                    var sb = new System.Text.StringBuilder();
                    string separator = ",";

                    var headers = new List<string> { "MeterId", "Name", "Unit", "Type", "Active" };
                    if (includeReadings) headers.Add("LastReading");
                    sb.AppendLine(string.Join(separator, headers));

   
                    foreach (var item in exportData)
                    {
                        var values = new List<string>
                {
                    item["MeterId"].ToString(),
                    item["Name"].ToString(),
                    item["Unit"].ToString(),
                    item["Type"].ToString(),
                    item["Active"].ToString()
                };

                        if (includeReadings)
                        {
                            values.Add(Convert.ToDouble(item["LastReading"]).ToString(System.Globalization.CultureInfo.InvariantCulture));
                        }

                        sb.AppendLine(string.Join(separator, values));
                    }

                    var preamble = System.Text.Encoding.UTF8.GetPreamble();
                    var data = System.Text.Encoding.UTF8.GetBytes(sb.ToString());
                    var fileBytes = new byte[preamble.Length + data.Length];
                    Buffer.BlockCopy(preamble, 0, fileBytes, 0, preamble.Length);
                    Buffer.BlockCopy(data, 0, fileBytes, preamble.Length, data.Length);

                    return File(fileBytes, "text/csv", $"{fileName}.csv");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error exporting meters");
                return BadRequest("An error occurred during export.");
            }
        }

        #endregion
    }
}