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
        #region Constructor and Dependencies - UPDATED

        private readonly ILogger<ImportController> _logger;
        private readonly SqlServerService _sqlServerService;
        private readonly DatabaseService _databaseService;
        private readonly TrendsService _trendsService;
        private readonly MeterRepository _meterRepository;

        public ImportController(
            ILogger<ImportController> logger,
            SqlServerService sqlServerService,
            DatabaseService databaseService,
            VarexpParserService varexpParserService,
            VariableBrowseParsingService variableBrowseParsingService,
            TrendsService trendsService,
            MeterRepository meterRepository)
        {
            _logger = logger;
            _sqlServerService = sqlServerService;
            _databaseService = databaseService;
            _trendsService = trendsService;
            _meterRepository = meterRepository;
        }

        #endregion

        #region General Controller Actions

        public IActionResult Index()
        {
            var viewModel = new ImportExportViewModel
            {
                // Initialize with default values if needed
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


        #region Trends Endpoints (NEW)

        /// <summary>
        /// Get trends data for selected variables
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> GetTrendsData([FromBody] ProcessTrendsRequest request)
        {
            try
            {
                _logger.LogInformation("Processing trends data for {Count} variables", request.VariableNames?.Count ?? 0);

                // Validate request
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

                // Get connection settings
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

                // Process trends for all variables
                var results = await _trendsService.ProcessVariablesTrendsAsync(
                    request.VariableNames,
                    request.StartDate,
                    request.EndDate,
                    settings
                );

                var endTime = DateTime.UtcNow;

                // Convert service results to controller response format
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

                // Create summary
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

                _logger.LogInformation("Trends processing completed. Success: {Success}/{Total}, Total Data Points: {DataPoints}",
                    summary.SuccessfulVariables, summary.TotalVariables, summary.TotalDataPoints);


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
        /// Import Web Service variables with trends data
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> ImportWebServiceVariablesWithTrends([FromBody] ImportWebServiceVariablesWithTrendsRequest request)
        {
            try
            {
                _logger.LogInformation("Importing {Count} variables with trends data", request.Variables?.Count ?? 0);

                // Validate request
                if (request == null || request.Variables == null || request.Variables.Count == 0)
                {
                    return Json(new ImportVariablesWithTrendsResponse
                    {
                        Success = false,
                        ErrorMessage = "No variables specified for import"
                    });
                }

                if (string.IsNullOrEmpty(request.ConnectionId))
                {
                    return Json(new ImportVariablesWithTrendsResponse
                    {
                        Success = false,
                        ErrorMessage = "Connection ID is required"
                    });
                }

                // Get connection settings
                var settings = GetWebServiceSettings(request.ConnectionId);
                if (settings == null)
                {
                    return Json(new ImportVariablesWithTrendsResponse
                    {
                        Success = false,
                        ErrorMessage = $"Web service connection '{request.ConnectionId}' not found"
                    });
                }

                var response = new ImportVariablesWithTrendsResponse { Success = true };
                var importSummary = new ImportSummary();
                var trendsSummary = new TrendsSummary();

                // Step 1: Get trends data if requested
                if (request.ImportTrendsData && request.TrendsStartDate.HasValue && request.TrendsEndDate.HasValue)
                {
                    _logger.LogInformation("Fetching trends data for {Count} variables", request.Variables.Count);

                    var variableNames = request.Variables.Select(v => v.VariableName).ToList();
                    var trendsResults = await _trendsService.ProcessVariablesTrendsAsync(
                        variableNames,
                        request.TrendsStartDate.Value,
                        request.TrendsEndDate.Value,
                        settings
                    );

                    // Merge trends data with variables
                    foreach (var variable in request.Variables)
                    {
                        var trendsResult = trendsResults.FirstOrDefault(r => r.VariableName == variable.VariableName);
                        if (trendsResult != null)
                        {
                            variable.TrendsData = trendsResult.TrendData;
                            variable.TrendsDataAvailable = trendsResult.Success;
                            variable.TrendsErrorMessage = trendsResult.ErrorMessage;
                            variable.TrendsDataPointsCount = trendsResult.TrendData?.Count ?? 0;
                            variable.TrendsStartDate = request.TrendsStartDate;
                            variable.TrendsEndDate = request.TrendsEndDate;
                        }
                    }

                    // Update trends summary
                    trendsSummary.TotalVariables = trendsResults.Count;
                    trendsSummary.SuccessfulVariables = trendsResults.Count(r => r.Success);
                    trendsSummary.FailedVariables = trendsResults.Count(r => !r.Success);
                    trendsSummary.TotalDataPoints = trendsResults.Sum(r => r.TrendData?.Count ?? 0);
                }

                // Step 2: Import variables as meters (using existing logic)
                var results = new List<VariableImportResult>();

                foreach (var variable in request.Variables)
                {
                    try
                    {
                        var result = new VariableImportResult
                        {
                            VariableName = variable.VariableName,
                            TrendsDataPointsImported = variable.TrendsDataPointsCount
                        };

                        // TODO: Implement actual meter import logic here
                        // This would integrate with your existing meter creation system
                        // For now, just simulate success
                        result.ImportSuccess = true;
                        result.TrendsSuccess = variable.TrendsDataAvailable;
                        result.Action = "Created"; // or "Updated" or "Skipped"
                        result.MeterId = new Random().Next(1000, 9999); // Simulate meter ID

                        if (!result.ImportSuccess)
                        {
                            result.ImportErrorMessage = "Import failed (simulated)";
                            importSummary.FailedVariables++;
                            importSummary.Errors.Add($"{variable.VariableName}: {result.ImportErrorMessage}");
                        }
                        else if (result.Action == "Created")
                        {
                            importSummary.ImportedVariables++;
                        }
                        else if (result.Action == "Updated")
                        {
                            importSummary.UpdatedVariables++;
                        }
                        else if (result.Action == "Skipped")
                        {
                            importSummary.SkippedVariables++;
                        }

                        results.Add(result);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error importing variable: {VariableName}", variable.VariableName);
                        results.Add(new VariableImportResult
                        {
                            VariableName = variable.VariableName,
                            ImportSuccess = false,
                            ImportErrorMessage = ex.Message
                        });
                        importSummary.FailedVariables++;
                        importSummary.Errors.Add($"{variable.VariableName}: {ex.Message}");
                    }
                }

                // Finalize summaries
                importSummary.TotalVariables = request.Variables.Count;

                response.ImportSummary = importSummary;
                response.TrendsSummary = trendsSummary;
                response.Results = results;

                _logger.LogInformation("Import completed. Imported: {Imported}, Failed: {Failed}, Trends Points: {TrendsPoints}",
                    importSummary.ImportedVariables, importSummary.FailedVariables, trendsSummary.TotalDataPoints);

                return Json(response);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error importing variables with trends");
                return Json(new ImportVariablesWithTrendsResponse
                {
                    Success = false,
                    ErrorMessage = $"Server error: {ex.Message}"
                });
            }
        }

        /// <summary>
        /// Get available Web Service connections for trends processing
        /// </summary>
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
                        status = "Available" // Could add real status checking
                    })
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting web service connections for trends");
                return Json(new
                {
                    success = false,
                    error = ex.Message
                });
            }
        }

        // Add this new endpoint to ImportController.cs in the #region Trends Endpoints (NEW) section

        /// <summary>
        /// Get trends data for all imported WebService meters - Main Testing Endpoint
        /// Calls both trends endpoints sequentially and prints detailed results to console
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> GetTrendsDataForImportedMeters([FromBody] GetTrendsForImportedMetersRequest request)
        {
            var overallStartTime = DateTime.UtcNow;

            try
            {
                _logger.LogInformation("Starting trends processing for imported meters - Connection: {ConnectionId}, DateRange: {StartDate} to {EndDate}",
                    request.ConnectionId, request.StartDate, request.EndDate);

                // Validate request
                var validationResult = ValidateTrendsRequest(request);
                if (!validationResult.IsValid)
                {                 
                    return Json(new ImportedMetersTrendsResponse
                    {
                        Success = false,
                        ErrorMessage = validationResult.ErrorMessage
                    });
                }

                // Get connection settings
                var settings = GetWebServiceSettings(request.ConnectionId);
                if (settings == null)
                {
                    var errorMsg = $"WebService connection '{request.ConnectionId}' not found";                 
                    return Json(new ImportedMetersTrendsResponse
                    {
                        Success = false,
                        ErrorMessage = errorMsg
                    });
                }

                // Get imported meters from database
                var importedMeters = await GetImportedMetersForProcessing(request);
                if (importedMeters.Count == 0)
                {
                    var noMetersMsg = "No imported WebService meters found for processing";
                    return Json(new ImportedMetersTrendsResponse
                    {
                        Success = true,
                        ErrorMessage = noMetersMsg,
                        Summary = new TrendsProcessingSummary
                        {
                            TotalMetersProcessed = 0,
                            ConnectionUsed = settings.ConnectionName,
                            StartTime = overallStartTime,
                            EndTime = DateTime.UtcNow
                        }
                    });
                }

                // Process each meter sequentially
                var meterResults = await ProcessMetersSequentially(importedMeters, request, settings);

                var overallEndTime = DateTime.UtcNow;

                // Create summary
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
        /// Process all meters sequentially, calling both trends endpoints for each
        /// </summary>
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
                    // Step 1: Call GetTrendsData endpoint
                    var trendsDataResult = await CallGetTrendsDataEndpoint(meter, request, settings);

                    // Step 2: Call ImportWebServiceVariablesWithTrends endpoint
                    var importTrendsResult = await CallImportTrendsEndpoint(meter, request, settings);

                    // Create result object
                    var meterResult = CreateMeterResult(meter, trendsDataResult, importTrendsResult, meterStartTime);

                    results.Add(meterResult);

                    // Small delay between meters to be API-friendly
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
        /// Call the GetTrendsData endpoint for a single meter
        /// </summary>
        private async Task<(bool Success, string? Error, List<TrendDataPoint>? Data, string? RequestId)> CallGetTrendsDataEndpoint(
            MeterForTrendsAnalysis meter,
            GetTrendsForImportedMetersRequest request,
            PCVueWebServiceSettings settings)
        {
            try
            {
                _logger.LogInformation("Calling GetTrendsData for meter: {MeterName}", meter.Name);

                var trendsRequest = new ProcessTrendsRequest
                {
                    ConnectionId = request.ConnectionId,
                    VariableNames = new List<string> { meter.OriginalVariableName },
                    StartDate = request.StartDate,
                    EndDate = request.EndDate
                };

                // Call the existing TrendsService directly
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
                else
                {
                    return (false, "No result returned from trends service", null, null);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception calling GetTrendsData for meter: {MeterName}", meter.Name);
                return (false, $"Exception: {ex.Message}", null, null);
            }
        }

        /// <summary>
        /// Call the ImportWebServiceVariablesWithTrends endpoint for a single meter
        /// </summary>
        private async Task<(bool Success, string? Error, string Action, int ImportedPoints)> CallImportTrendsEndpoint(
            MeterForTrendsAnalysis meter,
            GetTrendsForImportedMetersRequest request,
            PCVueWebServiceSettings settings)
        {
            try
            {
                _logger.LogInformation("Calling ImportWebServiceVariablesWithTrends for meter: {MeterName}", meter.Name);

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
                    TrendsDataAvailable = false // Will be set by the endpoint
                }
            },
                    ConnectionId = request.ConnectionId,
                    ImportTrendsData = true,
                    TrendsStartDate = request.StartDate,
                    TrendsEndDate = request.EndDate,
                    SkipExisting = true, // Don't re-import the meter itself
                    UpdateExisting = false
                };

                // NOTE: This would typically call the actual endpoint, but for now we'll simulate the response
                // In a real implementation, you might need to make an internal HTTP call or restructure the method

                // For testing purposes, we'll return a simulated successful response
                return (true, null, "Skipped (already exists)", 0);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception calling ImportTrends for meter: {MeterName}", meter.Name);
                return (false, $"Exception: {ex.Message}", "Failed", 0);
            }
        }

        #endregion

        #region Helper Methods for Trends Processing

        /// <summary>
        /// Validate the trends processing request
        /// </summary>
        private (bool IsValid, string? ErrorMessage) ValidateTrendsRequest(GetTrendsForImportedMetersRequest request)
        {
            if (request == null)
                return (false, "Request cannot be null");

            if (string.IsNullOrEmpty(request.ConnectionId))
                return (false, "Connection ID is required");

            if (request.StartDate >= request.EndDate)
                return (false, "Start date must be before end date");

            if (request.EndDate > DateTime.UtcNow)
                return (false, "End date cannot be in the future");

            var timeSpan = request.EndDate - request.StartDate;
            if (timeSpan.TotalDays > 365)
                return (false, "Date range cannot exceed 365 days");

            return (true, null);
        }

        /// <summary>
        /// Get imported meters for processing based on request criteria
        /// </summary>
        private async Task<List<MeterForTrendsAnalysis>> GetImportedMetersForProcessing(GetTrendsForImportedMetersRequest request)
        {
            List<MeterForTrendsAnalysis> meters;

            if (request.GetAllImported)
            {
                // Get all imported meters
                meters = await _meterRepository.GetWebServiceImportedMetersAsync(request.ActiveOnly, request.MeterLimit);
            }
            else if (request.SpecificMeterIds.Any())
            {
                // Get specific meters (would need to implement this method)
                meters = new List<MeterForTrendsAnalysis>(); // Placeholder
                                                             // TODO: Implement GetSpecificMetersForTrendsAsync if needed
            }
            else
            {
                meters = new List<MeterForTrendsAnalysis>();
            }

            // Set connection ID for each meter
            foreach (var meter in meters)
            {
                meter.AssignedConnectionId = request.ConnectionId;
            }

            return meters;
        }

        /// <summary>
        /// Create meter processing result object
        /// </summary>
        private MeterTrendsResult CreateMeterResult(
            MeterForTrendsAnalysis meter,
            (bool Success, string? Error, List<TrendDataPoint>? Data, string? RequestId) trendsResult,
            (bool Success, string? Error, string Action, int ImportedPoints) importResult,
            DateTime startTime)
        {
            var result = new MeterTrendsResult
            {
                MeterId = meter.MeterId,
                MeterName = meter.Name,
                OriginalVariableName = meter.OriginalVariableName,

                // GetTrendsData results
                GetTrendsDataSuccess = trendsResult.Success,
                GetTrendsDataError = trendsResult.Error,
                TrendsData = trendsResult.Data,
                TrendsDataPointsCount = trendsResult.Data?.Count ?? 0,
                TrendsRequestId = trendsResult.RequestId,

                // ImportTrends results
                ImportTrendsSuccess = importResult.Success,
                ImportTrendsError = importResult.Error,
                ImportAction = importResult.Action,
                ImportedDataPoints = importResult.ImportedPoints,

                ProcessingDuration = DateTime.UtcNow - startTime
            };


            return result;
        }

        /// <summary>
        /// Create overall processing summary
        /// </summary>
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

        // ============================================================================================================
        #region HDS (Historical Data Server) FUNCTIONALITY
        // ============================================================================================================

        [HttpGet]
        public async Task<IActionResult> GetTables(string connectionId = null)
        {
            try
            {
                _logger.LogInformation($"GetTables called with connectionId: '{connectionId}'");

                if (!_sqlServerService.IsInitialized)
                {
                    _logger.LogError("SQL Server service not initialized");
                    return Json(new { success = false, error = "SQL Server connection not configured" });
                }

                // Get tables using the SQL Server service
                var tables = await _sqlServerService.GetAvailableTables(connectionId);

                _logger.LogInformation($"Retrieved {tables.Count} tables for connection '{connectionId ?? "default"}'");

                return Json(new
                {
                    success = true,
                    tables = tables,
                    connectionId = connectionId
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting tables from HDS on connection '{ConnectionId}'", connectionId ?? "default");
                return Json(new
                {
                    success = false,
                    error = $"Error retrieving tables: {ex.Message}",
                    connectionId = connectionId
                });
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
                _logger.LogError(ex, "Error getting tables from HDS for connection {ConnectionId}", connectionId);
                return Json(new { success = false, error = ex.Message });
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetMetersFromTable(string tableName, string connectionId = null, string startDate = null, string endDate = null, int limit = 1000)
        {
            try
            {
                _logger.LogInformation($"GetMetersFromTable called: tableName='{tableName}', connectionId='{connectionId}', limit={limit}");

                if (!_sqlServerService.IsInitialized)
                {
                    _logger.LogError("SQL Server service not initialized");
                    return Json(new { success = false, error = "SQL Server connection not configured" });
                }

                if (string.IsNullOrWhiteSpace(tableName))
                {
                    _logger.LogError("Table name is null or empty");
                    return Json(new { success = false, error = "Table name is required" });
                }

                // Validate limit parameter
                if (limit <= 0)
                {
                    limit = 1000; // Default limit
                    _logger.LogWarning($"Invalid limit provided, using default: {limit}");
                }

                // Maximum limit to prevent performance issues
                if (limit > 10000)
                {
                    limit = 10000;
                    _logger.LogWarning($"Limit reduced to maximum allowed value: {limit}");
                }

                _logger.LogInformation($"Processing request for table '{tableName}' on connection '{connectionId}' with limit {limit}");

                // Validate that the table exists before trying to query it
                var tableExists = await _sqlServerService.ValidateTableExists(tableName, connectionId);
                if (!tableExists)
                {
                    _logger.LogWarning($"Table '{tableName}' does not exist or is not accessible on connection '{connectionId}'");
                    return Json(new
                    {
                        success = false,
                        error = $"Table '{tableName}' does not exist or is not accessible on the selected connection. Please verify the table name and permissions."
                    });
                }

                // Get the HDS meters with the specified limit and connection
                var hdsMeters = await _sqlServerService.GetDistinctMeterNames(tableName, limit, connectionId);

                // Get parent meter options from PostgreSQL database
                var parentOptions = await GetParentMeterOptions();

                _logger.LogInformation($"Successfully retrieved {hdsMeters.Count} meters from table '{tableName}' on connection '{connectionId}'");

                // RETURN JSON RESPONSE - NOT PARTIAL VIEW
                return Json(new
                {
                    success = true,
                    meters = hdsMeters,
                    parentOptions = parentOptions,
                    actualCount = hdsMeters.Count,
                    requestedLimit = limit,
                    tableName = tableName,
                    connectionId = connectionId,
                    message = $"Retrieved {hdsMeters.Count} meters from table '{tableName}' (limit: {limit})"
                });
            }
            catch (Microsoft.Data.SqlClient.SqlException sqlEx)
            {
                // Handle specific SQL Server errors
                _logger.LogError(sqlEx, $"SQL Server error getting meters from table '{tableName}' on connection '{connectionId}' with limit {limit}");

                string errorMessage = "Database error occurred";

                // Provide more specific error messages based on SQL error
                switch (sqlEx.Number)
                {
                    case 208: // Invalid object name
                        errorMessage = $"Table '{tableName}' does not exist or is not accessible on the selected connection";
                        break;
                    case 102: // Incorrect syntax
                        errorMessage = "Invalid SQL syntax - please check table name format";
                        break;
                    case 2: // Connection timeout
                        errorMessage = "Connection timeout - please check connection settings";
                        break;
                    case 18456: // Login failed
                        errorMessage = "Authentication failed - please check connection credentials";
                        break;
                    default:
                        errorMessage = $"SQL Server error: {sqlEx.Message}";
                        break;
                }

                return Json(new
                {
                    success = false,
                    error = errorMessage,
                    sqlErrorNumber = sqlEx.Number,
                    tableName = tableName,
                    connectionId = connectionId
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Unexpected error getting meters from table '{tableName}' on connection '{connectionId}' with limit {limit}");
                return Json(new
                {
                    success = false,
                    error = $"Unexpected error: {ex.Message}",
                    tableName = tableName,
                    connectionId = connectionId
                });
            }
        }

        [HttpPost]
        public async Task<IActionResult> ImportMeterReadings([FromBody] ImportReadingsRequest request)
        {
            _logger.LogInformation("================================================");
            _logger.LogInformation("IMPORT METER READINGS - REAL IMPORT");
            _logger.LogInformation("================================================");

            try
            {
                // Basic validation
                if (request == null || string.IsNullOrEmpty(request.TableName))
                {
                    return Json(new { success = false, error = "Missing table name" });
                }

                if (request.MeterNames == null || request.MeterNames.Count == 0)
                {
                    return Json(new { success = false, error = "No meter names provided" });
                }

                _logger.LogInformation($"Importing readings for {request.MeterNames.Count} meters from table {request.TableName}");

                // Check database connections
                if (!_databaseService.IsInitialized || !_sqlServerService.IsInitialized)
                {
                    return Json(new { success = false, error = "Database connections not initialized" });
                }

                // Statistics
                int totalReadingsImported = 0;
                int totalMetersProcessed = 0;
                var errorMeters = new List<string>();
                var detailedErrors = new Dictionary<string, string>();

                // Process each meter
                foreach (var meterName in request.MeterNames)
                {
                    try
                    {
                        _logger.LogInformation($"Processing readings for meter: {meterName}");

                        // 1. Find meter ID in PostgreSQL
                        int? meterId = null;
                        using (var pgConnection = new NpgsqlConnection(_databaseService.GetConnectionString()))
                        {
                            await pgConnection.OpenAsync();
                            using var cmd = new NpgsqlCommand(@"SELECT ""MeterId"" FROM ""Meters"" WHERE ""Name"" = @Name", pgConnection);
                            cmd.Parameters.AddWithValue("@Name", meterName);
                            var result = await cmd.ExecuteScalarAsync();
                            if (result != null)
                            {
                                meterId = Convert.ToInt32(result);
                            }
                        }

                        if (!meterId.HasValue)
                        {
                            _logger.LogWarning($"Meter {meterName} not found in PostgreSQL, skipping");
                            errorMeters.Add(meterName);
                            detailedErrors[meterName] = "Meter not found in database";
                            continue;
                        }

                        _logger.LogInformation($"Found meter {meterName} with ID: {meterId}");

                        // 2. Get readings from SQL Server
                        var readings = new List<(DateTime timestamp, double value, int quality)>();

                        using (var sqlConnection = _sqlServerService.GetConnection())
                        {
                            await sqlConnection.OpenAsync();

                            // Build query with optional date filters
                            string sql = $"SELECT Chrono, Value, Quality FROM {request.TableName} WHERE NAME = @Name";

                            if (request.StartDate.HasValue)
                            {
                                // Convert DateTime to Windows filetime for comparison
                                long startFiletime = request.StartDate.Value.ToFileTimeUtc();
                                sql += " AND Chrono >= @StartDate";
                            }

                            if (request.EndDate.HasValue)
                            {
                                long endFiletime = request.EndDate.Value.ToFileTimeUtc();
                                sql += " AND Chrono <= @EndDate";
                            }

                            sql += " ORDER BY Chrono";

                            if (request.Limit.HasValue)
                            {
                                sql = $"SELECT TOP {request.Limit} * FROM ({sql}) AS ordered_readings";
                            }

                            using var cmd = new SqlCommand(sql, sqlConnection);
                            cmd.Parameters.AddWithValue("@Name", meterName);

                            if (request.StartDate.HasValue)
                            {
                                cmd.Parameters.AddWithValue("@StartDate", request.StartDate.Value.ToFileTimeUtc());
                            }
                            if (request.EndDate.HasValue)
                            {
                                cmd.Parameters.AddWithValue("@EndDate", request.EndDate.Value.ToFileTimeUtc());
                            }

                            using var reader = await cmd.ExecuteReaderAsync();
                            while (await reader.ReadAsync())
                            {
                                try
                                {
                                    long chrono = reader.GetInt64(0);
                                    double value = reader.GetDouble(1);
                                    int quality = reader.GetInt16(2);

                                    // Convert Windows filetime to DateTime
                                    DateTime timestamp = DateTime.FromFileTimeUtc(chrono);

                                    readings.Add((timestamp, value, quality));
                                }
                                catch (Exception ex)
                                {
                                    _logger.LogWarning($"Error parsing reading for {meterName}: {ex.Message}");
                                }
                            }
                        }

                        _logger.LogInformation($"Retrieved {readings.Count} readings for meter {meterName}");

                        // 3. Insert readings into PostgreSQL
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

                                    _logger.LogInformation($"Successfully imported {readings.Count} readings for meter {meterName}");
                                }
                                catch (Exception ex)
                                {
                                    await transaction.RollbackAsync();
                                    _logger.LogError(ex, $"Error inserting readings for meter {meterName}");
                                    errorMeters.Add(meterName);
                                    detailedErrors[meterName] = ex.Message;
                                }
                            }
                        }
                        else
                        {
                            _logger.LogInformation($"No readings found for meter {meterName}");
                            totalMetersProcessed++;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, $"Error processing meter {meterName}");
                        errorMeters.Add(meterName);
                        detailedErrors[meterName] = ex.Message;
                    }
                }

                _logger.LogInformation($"Import completed: {totalReadingsImported} readings imported from {totalMetersProcessed} meters");

                return Json(new
                {
                    success = errorMeters.Count == 0,
                    totalReadingsImported = totalReadingsImported,
                    totalMetersProcessed = totalMetersProcessed,
                    errorMeters = errorMeters,
                    detailedErrors = detailedErrors,
                    message = $"Successfully imported {totalReadingsImported} readings from {totalMetersProcessed} meters."
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled exception in ImportMeterReadings");
                return Json(new
                {
                    success = false,
                    error = ex.Message,
                    details = ex.StackTrace
                });
            }
        }

        [HttpPost]
        public IActionResult PrintHDSMeters([FromBody] PrintHDSMetersRequest request)
        {
            try
            {
                Console.WriteLine("\n=====================================================");
                Console.WriteLine("HDS METERS PRINT FUNCTION");
                Console.WriteLine("=====================================================");
                Console.WriteLine($"HDS Table Name: {request?.TableName ?? "Not provided"}");
                Console.WriteLine($"HDS Connection ID: {request?.ConnectionId ?? "Not provided"}");
                Console.WriteLine($"Selected HDS meters count: {request?.SelectedMeters?.Count ?? 0}");
                Console.WriteLine($"Print timestamp: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");

                if (request?.SelectedMeters != null && request.SelectedMeters.Count > 0)
                {
                    Console.WriteLine("\n--- HDS METER DETAILS ---");

                    for (int i = 0; i < request.SelectedMeters.Count; i++)
                    {
                        var meter = request.SelectedMeters[i];
                        Console.WriteLine($"\nHDS Meter {i + 1}:");
                        Console.WriteLine($"  Name: {meter.HdsMeterName ?? "N/A"}");
                        Console.WriteLine($"  Unit: {meter.Unit ?? "N/A"}");
                        Console.WriteLine($"  Type: {meter.Type ?? "main"}");
                        Console.WriteLine($"  Parent ID: {meter.ParentMeterId ?? "None"}");
                        Console.WriteLine($"  Active: {meter.Active}");
                        Console.WriteLine($"  Last Reading: {meter.LastReading ?? "N/A"}");
                        Console.WriteLine($"  Selected: {meter.IsSelected}");
                    }

                    // Additional HDS-specific information
                    Console.WriteLine("\n--- HDS IMPORT SUMMARY ---");
                    Console.WriteLine($"Total meters to import: {request.SelectedMeters.Count}");
                    Console.WriteLine($"Active meters: {request.SelectedMeters.Count(m => m.Active)}");
                    Console.WriteLine($"Main meters: {request.SelectedMeters.Count(m => m.Type?.ToLower() == "main")}");
                    Console.WriteLine($"Sub meters: {request.SelectedMeters.Count(m => m.Type?.ToLower() == "sub")}");
                    Console.WriteLine($"Meters with parents: {request.SelectedMeters.Count(m => !string.IsNullOrEmpty(m.ParentMeterId))}");
                    Console.WriteLine($"Source table: {request.TableName}");
                    Console.WriteLine($"Connection: {request.ConnectionId}");

                    // Group by unit type
                    var unitGroups = request.SelectedMeters
                        .GroupBy(m => m.Unit ?? "Unknown")
                        .OrderBy(g => g.Key);

                    Console.WriteLine("\n--- METERS BY UNIT TYPE ---");
                    foreach (var group in unitGroups)
                    {
                        Console.WriteLine($"  {group.Key}: {group.Count()} meters");
                        foreach (var meter in group.Take(3)) // Show first 3 in each group
                        {
                            Console.WriteLine($"    - {meter.HdsMeterName}");
                        }
                        if (group.Count() > 3)
                        {
                            Console.WriteLine($"    ... and {group.Count() - 3} more");
                        }
                    }
                }
                else
                {
                    Console.WriteLine("❌ No HDS meters were provided for printing");
                }

                Console.WriteLine("=====================================================\n");

                return Json(new
                {
                    success = true,
                    message = "HDS meters printed to console successfully",
                    count = request?.SelectedMeters?.Count ?? 0,
                    tableName = request?.TableName,
                    connectionId = request?.ConnectionId
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error in HDS Print function: {ex.Message}");
                return Json(new
                {
                    success = false,
                    error = $"HDS Print failed: {ex.Message}"
                });
            }
        }

        [HttpPost]
        public async Task<IActionResult> ImportMeters([FromBody] ImportMetersRequest request)
        {
            try
            {
                _logger.LogInformation($"Received import request for {request?.Meters?.Count ?? 0} meters");

                if (request?.Meters == null || request.Meters.Count == 0)
                {
                    return Json(new
                    {
                        success = false,
                        error = "No meters selected for import"
                    });
                }

                // Check if database is initialized
                if (!_databaseService.IsInitialized)
                {
                    return Json(new
                    {
                        success = false,
                        error = "PostgreSQL database not configured"
                    });
                }

                // Statistics for import result
                int importedCount = 0;
                int skippedCount = 0;
                int updatedCount = 0;
                int errorCount = 0;
                var errorMeters = new List<string>();
                var detailedErrors = new Dictionary<string, string>();

                // Create a NEW connection instead of using the service's shared connection
                using (var connection = new NpgsqlConnection(_databaseService.GetConnectionString()))
                {
                    await connection.OpenAsync();

                    // Create a transaction to ensure all operations succeed or fail together
                    using var transaction = await connection.BeginTransactionAsync();

                    try
                    {
                        foreach (var meter in request.Meters)
                        {
                            try
                            {
                                _logger.LogInformation($"Processing meter: {meter.HdsMeterName}, Type: {meter.Type}, Unit: {meter.Unit}");

                                // Skip empty meter names
                                if (string.IsNullOrWhiteSpace(meter.HdsMeterName))
                                {
                                    _logger.LogWarning("Skipping meter with empty name");
                                    skippedCount++;
                                    continue;
                                }

                                // Check if meter already exists
                                bool meterExists = false;
                                int existingMeterId = 0;

                                using (var checkCommand = new NpgsqlCommand(
                                    @"SELECT ""MeterId"" FROM ""Meters"" WHERE ""Name"" = @Name", connection, transaction))
                                {
                                    checkCommand.Parameters.AddWithValue("@Name", meter.HdsMeterName);
                                    var result = await checkCommand.ExecuteScalarAsync();
                                    meterExists = result != null;
                                    if (meterExists)
                                        existingMeterId = Convert.ToInt32(result);
                                }

                                _logger.LogInformation($"Meter {meter.HdsMeterName} exists: {meterExists}, SkipExisting: {request.SkipExisting}, UpdateExisting: {request.UpdateExisting}");

                                // Skip existing meter if requested
                                if (meterExists && request.SkipExisting && !request.UpdateExisting)
                                {
                                    _logger.LogInformation($"Skipping existing meter: {meter.HdsMeterName}");
                                    skippedCount++;
                                    continue;
                                }

                                // Ensure parent meter exists if specified
                                int? parentId = null;

                                if (!string.IsNullOrEmpty(meter.ParentMeterId))
                                {
                                    if (int.TryParse(meter.ParentMeterId, out int parsedParentId))
                                    {
                                        // Check if parent exists
                                        using (var parentCheckCommand = new NpgsqlCommand(
                                            @"SELECT COUNT(*) FROM ""Meters"" WHERE ""MeterId"" = @MeterId", connection, transaction))
                                        {
                                            parentCheckCommand.Parameters.AddWithValue("@MeterId", parsedParentId);
                                            int parentCount = Convert.ToInt32(await parentCheckCommand.ExecuteScalarAsync());

                                            if (parentCount > 0)
                                            {
                                                parentId = parsedParentId;
                                                _logger.LogInformation($"Parent meter found with ID: {parentId}");
                                            }
                                            else
                                            {
                                                _logger.LogWarning($"Parent meter with ID {parsedParentId} not found for {meter.HdsMeterName}, setting parent to null");
                                                parentId = null;
                                            }
                                        }
                                    }
                                }

                                // Parse last reading if provided
                                int lastReading = 0;
                                if (!string.IsNullOrEmpty(meter.LastReading) && int.TryParse(meter.LastReading, out int parsedReading))
                                {
                                    lastReading = parsedReading;
                                }

                                // Ensure type is valid
                                string type = "main";
                                if (!string.IsNullOrWhiteSpace(meter.Type) &&
                                    (meter.Type.ToLower() == "main" || meter.Type.ToLower() == "sub"))
                                {
                                    type = meter.Type.ToLower();
                                }

                                _logger.LogInformation($"Will insert: {!meterExists}, Will update: {meterExists && request.UpdateExisting}");

                                // Insert or update the meter
                                if (meterExists && request.UpdateExisting)
                                {
                                    // Update existing meter
                                    using (var updateCommand = new NpgsqlCommand(
                                        @"UPDATE ""Meters"" SET 
                                  ""Unit"" = @Unit,
                                  ""ParentId"" = @ParentId,
                                  ""LastReading"" = @LastReading,
                                  ""Type"" = @Type,
                                  ""Active"" = @Active
                                  WHERE ""MeterId"" = @MeterId", connection, transaction))
                                    {
                                        updateCommand.Parameters.AddWithValue("@MeterId", existingMeterId);
                                        updateCommand.Parameters.AddWithValue("@Unit", meter.Unit ?? "");
                                        updateCommand.Parameters.AddWithValue("@ParentId", parentId.HasValue ? parentId.Value : DBNull.Value);
                                        updateCommand.Parameters.AddWithValue("@LastReading", lastReading);
                                        updateCommand.Parameters.AddWithValue("@Type", type);
                                        updateCommand.Parameters.AddWithValue("@Active", meter.Active);

                                        int rowsAffected = await updateCommand.ExecuteNonQueryAsync();
                                        _logger.LogInformation($"Updated meter: {meter.HdsMeterName}, Rows affected: {rowsAffected}");
                                        if (rowsAffected > 0)
                                        {
                                            updatedCount++;
                                        }
                                    }
                                }
                                else if (!meterExists)
                                {
                                    // Insert new meter
                                    using (var insertCommand = new NpgsqlCommand(
                                        @"INSERT INTO ""Meters"" (""Name"", ""Unit"", ""ParentId"", ""LastReading"", ""Type"", ""Active"")
                                  VALUES (@Name, @Unit, @ParentId, @LastReading, @Type, @Active)
                                  RETURNING ""MeterId""", connection, transaction))
                                    {
                                        insertCommand.Parameters.AddWithValue("@Name", meter.HdsMeterName);
                                        insertCommand.Parameters.AddWithValue("@Unit", meter.Unit ?? "");
                                        insertCommand.Parameters.AddWithValue("@ParentId", parentId.HasValue ? parentId.Value : DBNull.Value);
                                        insertCommand.Parameters.AddWithValue("@LastReading", lastReading);
                                        insertCommand.Parameters.AddWithValue("@Type", type);
                                        insertCommand.Parameters.AddWithValue("@Active", meter.Active);

                                        var newMeterId = await insertCommand.ExecuteScalarAsync();
                                        importedCount++;
                                        _logger.LogInformation($"Imported new meter: {meter.HdsMeterName}, ID: {newMeterId}");
                                    }
                                }
                                else
                                {
                                    // This case happens when the meter exists but we're not updating
                                    _logger.LogInformation($"Meter {meter.HdsMeterName} exists but not updating due to settings");
                                    skippedCount++;
                                }
                            }
                            catch (Exception ex)
                            {
                                // Track error for this meter but continue with others
                                _logger.LogError(ex, $"Error importing meter {meter.HdsMeterName}");
                                errorCount++;
                                errorMeters.Add(meter.HdsMeterName);
                                detailedErrors[meter.HdsMeterName] = ex.Message;
                            }
                        }

                        // Commit the transaction
                        await transaction.CommitAsync();
                        _logger.LogInformation($"Import completed: {importedCount} imported, {updatedCount} updated, {skippedCount} skipped, {errorCount} errors");

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
                        // Rollback the transaction if any error occurs
                        await transaction.RollbackAsync();
                        throw new Exception($"Failed to import meters: {ex.Message}", ex);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error importing meters");
                return Json(new
                {
                    success = false,
                    error = ex.Message,
                    errorMessage = "An unexpected error occurred during the import process."
                });
            }
        }

        #endregion

        #region Helper Methods (NEW)


        /// <summary>
        /// Parse timestamp string to DateTime
        /// </summary>
        private DateTime? GetParsedTimestamp(string? timestamp)
        {
            if (string.IsNullOrEmpty(timestamp))
                return null;

            if (DateTime.TryParse(timestamp, out var result))
                return result;

            return null;
        }

        /// <summary>
        /// Get Web Service settings by connection ID
        /// </summary>
        private PCVueWebServiceSettings? GetWebServiceSettings(string connectionId)
        {
            try
            {
                // Read from appsettings.json
                var appSettingsPath = Path.Combine(Directory.GetCurrentDirectory(), "appsettings.json");
                if (!System.IO.File.Exists(appSettingsPath))
                {
                    _logger.LogError("appsettings.json not found at: {Path}", appSettingsPath);
                    return null;
                }

                var json = System.IO.File.ReadAllText(appSettingsPath);
                using var document = JsonDocument.Parse(json);

                if (!document.RootElement.TryGetProperty("PCVueWebServiceSettings", out var settingsElement))
                {
                    _logger.LogError("PCVueWebServiceSettings section not found in appsettings.json");
                    return null;
                }

                // Find the connection by ID
                if (settingsElement.TryGetProperty("Connections", out var connectionsElement))
                {
                    foreach (var connectionElement in connectionsElement.EnumerateArray())
                    {
                        if (connectionElement.TryGetProperty("ConnectionId", out var idElement) &&
                            idElement.GetString() == connectionId)
                        {
                            return new PCVueWebServiceSettings
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
                                ProjectName = connectionElement.GetProperty("ProjectName").GetString() ?? ""
                            };
                        }
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

        /// <summary>
        /// Get available Web Service connections
        /// </summary>
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
                            IsDefault = connectionElement.TryGetProperty("IsDefault", out var isDefaultElement) &&
                                       isDefaultElement.GetBoolean()
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

        // ============================================================================================================
        #region UTILITY METHODS & HELPERS
        // ============================================================================================================

        [HttpPost]
        public IActionResult PrintSelectedMeters([FromBody] PrintMetersRequest request)
        {
            Console.WriteLine("\n=====================================================");
            Console.WriteLine("PRINT SELECTED METERS DATA");
            Console.WriteLine("=====================================================");
            Console.WriteLine($"Table Name: {request?.TableName ?? "Not provided"}");
            Console.WriteLine($"Request type: {request?.GetType().Name ?? "null"}");

            var selectedMeterNames = request?.SelectedMeterNames;
            Console.WriteLine($"Selected meters count: {selectedMeterNames?.Count ?? 0}");

            if (selectedMeterNames != null && selectedMeterNames.Count > 0)
            {
                Console.WriteLine("\nSelected meters:");
                for (int i = 0; i < selectedMeterNames.Count; i++)
                {
                    string meterName = selectedMeterNames[i];
                    string meterType = (request.SelectedMeterTypes != null && i < request.SelectedMeterTypes.Count) ? request.SelectedMeterTypes[i] : "Unknown";
                    string meterUnit = (request.SelectedMeterUnits != null && i < request.SelectedMeterUnits.Count) ? request.SelectedMeterUnits[i] : "";

                    Console.WriteLine($"  Meter {i + 1}: {meterName}");
                    Console.WriteLine($"    Type: {meterType}");
                    Console.WriteLine($"    Unit: {meterUnit}");
                }
            }
            else
            {
                Console.WriteLine("No meter names received");
            }

            Console.WriteLine("=====================================================\n");

            return Json(new { success = true, message = "Printed meter data to console", count = selectedMeterNames?.Count ?? 0 });
        }

        // Helper method to get web service connection by ID
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

        // Enhanced method to get parent meter options with better error handling
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
                    using (var connection = _databaseService.GetConnection())
                    {
                        var command = new Npgsql.NpgsqlCommand(@"
                    SELECT ""MeterId"", ""Name"" 
                    FROM ""Meters"" 
                    WHERE ""Type"" = 'main' AND ""Active"" = true
                    ORDER BY ""Name""", connection);

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
                // Don't throw here, just return what we have
            }

            return options;
        }

        #endregion
    }
}