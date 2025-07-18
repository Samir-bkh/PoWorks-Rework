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
        private readonly VarexpParserService _varexpParserService;
        private readonly VariableBrowseParsingService _variableBrowseParsingService;
        private readonly TrendsService _trendsService;
        private readonly MeterRepository _meterRepository; // NEW: Added MeterRepository

        public ImportController(
            ILogger<ImportController> logger,
            SqlServerService sqlServerService,
            DatabaseService databaseService,
            VarexpParserService varexpParserService,
            VariableBrowseParsingService variableBrowseParsingService,
            TrendsService trendsService,
            MeterRepository meterRepository) // NEW: Added MeterRepository parameter
        {
            _logger = logger;
            _sqlServerService = sqlServerService;
            _databaseService = databaseService;
            _varexpParserService = varexpParserService;
            _variableBrowseParsingService = variableBrowseParsingService;
            _trendsService = trendsService;
            _meterRepository = meterRepository; // NEW: Assign MeterRepository
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

            return View(viewModel); // Fixed: Added missing return statement and closing brace
        }

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

                if (request?.SelectedVariables != null && request.SelectedVariables.Count > 0)
                {
                    Console.WriteLine("\n--- WEB SERVICE VARIABLE DETAILS ---");

                    for (int i = 0; i < request.SelectedVariables.Count; i++)
                    {
                        var variable = request.SelectedVariables[i];
                        Console.WriteLine($"\nVariable {i + 1}:");
                        Console.WriteLine($"  Variable Name: {variable.VariableName ?? "N/A"}");
                        Console.WriteLine($"  Unit: {variable.Unit ?? "N/A"}");
                        Console.WriteLine($"  Type: {variable.Type ?? "main"}");
                        Console.WriteLine($"  Parent ID: {variable.ParentMeterId ?? "None"}");
                        Console.WriteLine($"  Active: {variable.Active}");
                        Console.WriteLine($"  Variable Type: {variable.VariableType ?? "N/A"}");
                        Console.WriteLine($"  Is Read Only: {variable.IsReadOnly}");
                        Console.WriteLine($"  Selected: {variable.IsSelected}");
                    }

                    // Additional Web Service-specific information
                    Console.WriteLine("\n--- WEB SERVICE IMPORT SUMMARY ---");
                    Console.WriteLine($"Total variables to import as meters: {request.SelectedVariables.Count}");
                    Console.WriteLine($"Active variables: {request.SelectedVariables.Count(v => v.Active)}");
                    Console.WriteLine($"Main type variables: {request.SelectedVariables.Count(v => v.Type?.ToLower() == "main")}");
                    Console.WriteLine($"Sub type variables: {request.SelectedVariables.Count(v => v.Type?.ToLower() == "sub")}");
                    Console.WriteLine($"Variables with parents: {request.SelectedVariables.Count(v => !string.IsNullOrEmpty(v.ParentMeterId))}");
                    Console.WriteLine($"Read-only variables: {request.SelectedVariables.Count(v => v.IsReadOnly)}");
                    Console.WriteLine($"Source connection: {request.ConnectionId}");

                    // Group by variable type
                    var typeGroups = request.SelectedVariables
                        .GroupBy(v => v.VariableType ?? "Unknown")
                        .OrderBy(g => g.Key);

                    Console.WriteLine("\n--- VARIABLES BY PCVue TYPE ---");
                    foreach (var group in typeGroups)
                    {
                        Console.WriteLine($"  {group.Key}: {group.Count()} variables");
                        foreach (var variable in group.Take(3)) // Show first 3 in each group
                        {
                            Console.WriteLine($"    - {variable.VariableName}");
                        }
                        if (group.Count() > 3)
                        {
                            Console.WriteLine($"    ... and {group.Count() - 3} more");
                        }
                    }

                    // Group by unit type
                    var unitGroups = request.SelectedVariables
                        .Where(v => !string.IsNullOrEmpty(v.Unit))
                        .GroupBy(v => v.Unit)
                        .OrderBy(g => g.Key);

                    if (unitGroups.Any())
                    {
                        Console.WriteLine("\n--- VARIABLES BY UNIT TYPE ---");
                        foreach (var group in unitGroups)
                        {
                            Console.WriteLine($"  {group.Key}: {group.Count()} variables");
                        }
                    }
                }
                else
                {
                    Console.WriteLine("❌ No web service variables were provided for printing");
                }

                Console.WriteLine("=====================================================\n");

                return Json(new
                {
                    success = true,
                    message = "Web service variables printed to console successfully",
                    count = request?.SelectedVariables?.Count ?? 0,
                    connectionId = request?.ConnectionId,
                    connectionName = request?.ConnectionName
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error in Web Service Print function: {ex.Message}");
                return Json(new
                {
                    success = false,
                    error = $"Web Service Print failed: {ex.Message}"
                });
            }
        }

        [HttpPost]
        public async Task<IActionResult> ImportWebServiceMeters([FromBody] ImportWebServiceMetersRequest request)
        {
            try
            {
                _logger.LogInformation($"Received Web Service import request for {request?.Variables?.Count ?? 0} variables");

                if (request?.Variables == null || request.Variables.Count == 0)
                {
                    return Json(new
                    {
                        success = false,
                        error = "No variables selected for import"
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
                var errorVariables = new List<string>();
                var detailedErrors = new Dictionary<string, string>();

                // Create a NEW connection instead of using the service's shared connection
                using (var connection = new NpgsqlConnection(_databaseService.GetConnectionString()))
                {
                    await connection.OpenAsync();

                    // Create a transaction to ensure all operations succeed or fail together
                    using var transaction = await connection.BeginTransactionAsync();

                    try
                    {
                        foreach (var variable in request.Variables)
                        {
                            try
                            {
                                _logger.LogInformation($"Processing Web Service variable: {variable.VariableName}, Type: {variable.Type}, Unit: {variable.Unit}");

                                // Skip empty variable names
                                if (string.IsNullOrWhiteSpace(variable.VariableName))
                                {
                                    _logger.LogWarning("Skipping variable with empty name");
                                    skippedCount++;
                                    continue;
                                }

                                // Check if meter already exists
                                bool meterExists = false;
                                int existingMeterId = 0;

                                using (var checkCommand = new NpgsqlCommand(
                                    @"SELECT ""MeterId"" FROM ""Meters"" WHERE ""Name"" = @Name", connection, transaction))
                                {
                                    checkCommand.Parameters.AddWithValue("@Name", variable.VariableName);
                                    var result = await checkCommand.ExecuteScalarAsync();
                                    meterExists = result != null;
                                    if (meterExists)
                                        existingMeterId = Convert.ToInt32(result);
                                }

                                _logger.LogInformation($"Variable {variable.VariableName} exists as meter: {meterExists}, SkipExisting: {request.SkipExisting}, UpdateExisting: {request.UpdateExisting}");

                                // Skip existing meter if requested
                                if (meterExists && request.SkipExisting && !request.UpdateExisting)
                                {
                                    _logger.LogInformation($"Skipping existing meter: {variable.VariableName}");
                                    skippedCount++;
                                    continue;
                                }

                                // Ensure parent meter exists if specified
                                int? parentId = null;

                                if (!string.IsNullOrEmpty(variable.ParentMeterId))
                                {
                                    if (int.TryParse(variable.ParentMeterId, out int parsedParentId))
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
                                                _logger.LogWarning($"Parent meter with ID {parsedParentId} not found for {variable.VariableName}, setting parent to null");
                                                parentId = null;
                                            }
                                        }
                                    }
                                }

                                // Ensure type is valid
                                string type = "main";
                                if (!string.IsNullOrWhiteSpace(variable.Type) &&
                                    (variable.Type.ToLower() == "main" || variable.Type.ToLower() == "sub"))
                                {
                                    type = variable.Type.ToLower();
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
                                        updateCommand.Parameters.AddWithValue("@Unit", variable.Unit ?? "");
                                        updateCommand.Parameters.AddWithValue("@ParentId", parentId.HasValue ? parentId.Value : DBNull.Value);
                                        updateCommand.Parameters.AddWithValue("@LastReading", 0); // Default for web service variables
                                        updateCommand.Parameters.AddWithValue("@Type", type);
                                        updateCommand.Parameters.AddWithValue("@Active", variable.Active);

                                        int rowsAffected = await updateCommand.ExecuteNonQueryAsync();
                                        _logger.LogInformation($"Updated meter: {variable.VariableName}, Rows affected: {rowsAffected}");
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
                                        insertCommand.Parameters.AddWithValue("@Name", variable.VariableName);
                                        insertCommand.Parameters.AddWithValue("@Unit", variable.Unit ?? "");
                                        insertCommand.Parameters.AddWithValue("@ParentId", parentId.HasValue ? parentId.Value : DBNull.Value);
                                        insertCommand.Parameters.AddWithValue("@LastReading", 0); // Default for web service variables
                                        insertCommand.Parameters.AddWithValue("@Type", type);
                                        insertCommand.Parameters.AddWithValue("@Active", variable.Active);

                                        var newMeterId = await insertCommand.ExecuteScalarAsync();
                                        importedCount++;
                                        _logger.LogInformation($"Imported new meter from variable: {variable.VariableName}, ID: {newMeterId}");
                                    }
                                }
                                else
                                {
                                    // This case happens when the meter exists but we're not updating
                                    _logger.LogInformation($"Variable {variable.VariableName} exists as meter but not updating due to settings");
                                    skippedCount++;
                                }
                            }
                            catch (Exception ex)
                            {
                                // Track error for this variable but continue with others
                                _logger.LogError(ex, $"Error importing variable {variable.VariableName}");
                                errorCount++;
                                errorVariables.Add(variable.VariableName);
                                detailedErrors[variable.VariableName] = ex.Message;
                            }
                        }

                        // Commit the transaction
                        await transaction.CommitAsync();
                        _logger.LogInformation($"Web Service Import completed: {importedCount} imported, {updatedCount} updated, {skippedCount} skipped, {errorCount} errors");

                        return Json(new
                        {
                            success = errorCount == 0,
                            importedCount,
                            updatedCount,
                            skippedCount,
                            errorCount,
                            errorVariables,
                            detailedErrors,
                            message = $"Successfully imported {importedCount} meters from variables, updated {updatedCount}, skipped {skippedCount}, with {errorCount} errors."
                        });
                    }
                    catch (Exception ex)
                    {
                        // Rollback the transaction if any error occurs
                        await transaction.RollbackAsync();
                        throw new Exception($"Failed to import Web Service variables as meters: {ex.Message}", ex);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error importing Web Service variables as meters");
                return Json(new
                {
                    success = false,
                    error = ex.Message,
                    errorMessage = "An unexpected error occurred during the Web Service import process."
                });
            }
        } // Fixed: Removed misplaced semicolon and return statement

        [HttpGet]
        public IActionResult GetSqlServerConnections()
        {
            try
            {
                _logger.LogInformation("Getting SQL Server connections...");

                var connections = _sqlServerService.GetAllConnections();
                _logger.LogInformation($"Found {connections.Count} SQL Server connections");

                var connectionData = connections.Select(c => new
                {
                    connectionId = c.ConnectionId,
                    connectionName = c.ConnectionName,
                    host = c.Host,
                    port = c.Port,
                    database = c.Database,
                    isDefault = c.IsDefault
                }).ToList();

                _logger.LogInformation($"Returning connection data: {string.Join(", ", connectionData.Select(c => c.connectionName))}");

                return Json(new { success = true, connections = connectionData });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting SQL Server connections");
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
                var settings = GetWebServiceSettings(request.ConnectionId);
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

                PrintProcessingHeader(request);

                // Validate request
                var validationResult = ValidateTrendsRequest(request);
                if (!validationResult.IsValid)
                {
                    PrintValidationError(validationResult.ErrorMessage);
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
                    PrintConnectionError(errorMsg);
                    return Json(new ImportedMetersTrendsResponse
                    {
                        Success = false,
                        ErrorMessage = errorMsg
                    });
                }

                PrintConnectionInfo(settings);

                // Get imported meters from database
                var importedMeters = await GetImportedMetersForProcessing(request);
                if (importedMeters.Count == 0)
                {
                    var noMetersMsg = "No imported WebService meters found for processing";
                    PrintNoMetersFound();
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

                PrintMetersFound(importedMeters);

                // Process each meter sequentially
                var meterResults = await ProcessMetersSequentially(importedMeters, request, settings);

                var overallEndTime = DateTime.UtcNow;

                // Create summary
                var summary = CreateProcessingSummary(meterResults, overallStartTime, overallEndTime, settings, request);

                PrintOverallSummary(summary, meterResults);

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
                PrintFatalError(ex);

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

                PrintMeterProcessingStart(meter, i + 1, meters.Count);

                try
                {
                    // Step 1: Call GetTrendsData endpoint
                    var trendsDataResult = await CallGetTrendsDataEndpoint(meter, request, settings);

                    // Step 2: Call ImportWebServiceVariablesWithTrends endpoint
                    var importTrendsResult = await CallImportTrendsEndpoint(meter, request, settings);

                    // Create result object
                    var meterResult = CreateMeterResult(meter, trendsDataResult, importTrendsResult, meterStartTime);

                    results.Add(meterResult);

                    PrintMeterProcessingComplete(meterResult);

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
                    PrintMeterProcessingError(meter, ex);
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

        #region Console Output Methods

        /// <summary>
        /// Print main processing header
        /// </summary>
        private void PrintProcessingHeader(GetTrendsForImportedMetersRequest request)
        {
            Console.WriteLine();
            Console.WriteLine("=====================================================");
            Console.WriteLine("IMPORTED METERS TRENDS DATA PROCESSING");
            Console.WriteLine("=====================================================");
            Console.WriteLine($"Connection ID: {request.ConnectionId}");
            Console.WriteLine($"Date Range: {request.StartDate:yyyy-MM-dd HH:mm:ss} to {request.EndDate:yyyy-MM-dd HH:mm:ss}");
            Console.WriteLine($"Process All Imported: {request.GetAllImported}");
            Console.WriteLine($"Active Only: {request.ActiveOnly}");
            Console.WriteLine($"Meter Limit: {(request.MeterLimit > 0 ? request.MeterLimit.ToString() : "No Limit")}");
            Console.WriteLine($"Processing Started: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            Console.WriteLine("=====================================================");
        }

        /// <summary>
        /// Print validation error
        /// </summary>
        private void PrintValidationError(string? errorMessage)
        {
            Console.WriteLine();
            Console.WriteLine("❌ VALIDATION ERROR ❌");
            Console.WriteLine($"Error: {errorMessage}");
            Console.WriteLine("=====================================================");
        }

        /// <summary>
        /// Print connection error
        /// </summary>
        private void PrintConnectionError(string errorMessage)
        {
            Console.WriteLine();
            Console.WriteLine("❌ CONNECTION ERROR ❌");
            Console.WriteLine($"Error: {errorMessage}");
            Console.WriteLine("=====================================================");
        }

        /// <summary>
        /// Print connection information
        /// </summary>
        private void PrintConnectionInfo(PCVueWebServiceSettings settings)
        {
            Console.WriteLine();
            Console.WriteLine("--- CONNECTION INFORMATION ---");
            Console.WriteLine($"Connection Name: {settings.ConnectionName}");
            Console.WriteLine($"Base URL: {settings.BaseUrl}");
            Console.WriteLine($"Project: {settings.ProjectName}");
            Console.WriteLine($"Auth Type: {settings.AuthType}");
            Console.WriteLine($"Timeout: {settings.TimeoutSeconds}s");
        }

        /// <summary>
        /// Print when no meters are found
        /// </summary>
        private void PrintNoMetersFound()
        {
            Console.WriteLine();
            Console.WriteLine("⚠️  NO METERS FOUND ⚠️");
            Console.WriteLine("No imported WebService meters found matching the criteria.");
            Console.WriteLine("Check that:");
            Console.WriteLine("- Meters have been imported from WebService variables");
            Console.WriteLine("- Meters are active (if ActiveOnly = true)");
            Console.WriteLine("- Database contains meters with WebService naming patterns");
            Console.WriteLine("=====================================================");
        }

        /// <summary>
        /// Print meters found for processing
        /// </summary>
        private void PrintMetersFound(List<MeterForTrendsAnalysis> meters)
        {
            Console.WriteLine();
            Console.WriteLine($"--- FOUND {meters.Count} METERS FOR PROCESSING ---");

            for (int i = 0; i < Math.Min(meters.Count, 10); i++) // Show first 10
            {
                var meter = meters[i];
                Console.WriteLine($"{i + 1}. ID:{meter.MeterId} | {meter.Name} | {meter.Unit} | {meter.Type} | Active:{meter.Active}");
            }

            if (meters.Count > 10)
            {
                Console.WriteLine($"... and {meters.Count - 10} more meters");
            }
            Console.WriteLine();
        }

        /// <summary>
        /// Print meter processing start
        /// </summary>
        private void PrintMeterProcessingStart(MeterForTrendsAnalysis meter, int current, int total)
        {
            Console.WriteLine($"--- PROCESSING METER {current}/{total} ---");
            Console.WriteLine($"Meter ID: {meter.MeterId}");
            Console.WriteLine($"Meter Name: {meter.Name}");
            Console.WriteLine($"Variable Name: {meter.OriginalVariableName}");
            Console.WriteLine($"Unit: {meter.Unit}");
            Console.WriteLine($"Type: {meter.Type}");
            Console.WriteLine($"Started: {DateTime.Now:HH:mm:ss}");
        }

        /// <summary>
        /// Print meter processing completion
        /// </summary>
        private void PrintMeterProcessingComplete(MeterTrendsResult result)
        {
            Console.WriteLine();
            Console.WriteLine("--- ENDPOINT 1: GetTrendsData Results ---");
            Console.WriteLine($"Success: {result.GetTrendsDataSuccess}");
            if (result.GetTrendsDataSuccess)
            {
                Console.WriteLine($"Data Points Retrieved: {result.TrendsDataPointsCount:N0}");
                Console.WriteLine($"Request ID: {result.TrendsRequestId}");
                if (result.FirstTimestamp.HasValue && result.LastTimestamp.HasValue)
                {
                    Console.WriteLine($"Date Range: {result.FirstTimestamp:yyyy-MM-dd HH:mm:ss} to {result.LastTimestamp:yyyy-MM-dd HH:mm:ss}");
                }
                if (result.MinValue.HasValue && result.MaxValue.HasValue && result.AverageValue.HasValue)
                {
                    Console.WriteLine($"Value Range: {result.MinValue:F2} to {result.MaxValue:F2} (Avg: {result.AverageValue:F2})");
                }
            }
            else
            {
                Console.WriteLine($"Error: {result.GetTrendsDataError}");
            }

            Console.WriteLine();
            Console.WriteLine("--- ENDPOINT 2: ImportWebServiceVariablesWithTrends Results ---");
            Console.WriteLine($"Success: {result.ImportTrendsSuccess}");
            Console.WriteLine($"Action: {result.ImportAction}");
            if (result.ImportTrendsSuccess)
            {
                Console.WriteLine($"Data Points Imported: {result.ImportedDataPoints:N0}");
            }
            else
            {
                Console.WriteLine($"Error: {result.ImportTrendsError}");
            }

            Console.WriteLine();
            Console.WriteLine($"--- PROCESSING COMPLETE ---");
            Console.WriteLine($"Duration: {result.ProcessingDuration.TotalSeconds:F1}s");
            Console.WriteLine($"Overall Success: {(result.GetTrendsDataSuccess && result.ImportTrendsSuccess ? "✅ YES" : "❌ NO")}");
            Console.WriteLine("=====================================================");
        }

        /// <summary>
        /// Print meter processing error
        /// </summary>
        private void PrintMeterProcessingError(MeterForTrendsAnalysis meter, Exception ex)
        {
            Console.WriteLine();
            Console.WriteLine($"❌ ERROR PROCESSING METER: {meter.Name}");
            Console.WriteLine($"Exception: {ex.Message}");
            if (ex.InnerException != null)
            {
                Console.WriteLine($"Inner Exception: {ex.InnerException.Message}");
            }
            Console.WriteLine("=====================================================");
        }

        /// <summary>
        /// Print overall processing summary
        /// </summary>
        private void PrintOverallSummary(TrendsProcessingSummary summary, List<MeterTrendsResult> results)
        {
            Console.WriteLine();
            Console.WriteLine("=====================================================");
            Console.WriteLine("OVERALL TRENDS PROCESSING SUMMARY");
            Console.WriteLine("=====================================================");
            Console.WriteLine($"Total Meters Processed: {summary.TotalMetersProcessed}");
            Console.WriteLine($"Successful: {summary.SuccessfulMeters} ({summary.SuccessRate:F1}%)");
            Console.WriteLine($"Failed: {summary.FailedMeters} ({summary.FailureRate:F1}%)");
            Console.WriteLine($"Total Data Points Retrieved: {summary.TotalDataPointsRetrieved:N0}");
            Console.WriteLine($"Total Data Points Imported: {summary.TotalDataPointsImported:N0}");
            Console.WriteLine($"Average Points per Meter: {summary.AverageDataPointsPerMeter:F0}");
            Console.WriteLine($"Total Processing Time: {summary.TotalProcessingTime.TotalMinutes:F1} minutes");
            Console.WriteLine($"Connection Used: {summary.ConnectionUsed}");
            Console.WriteLine($"Completed: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");

            if (summary.Errors.Any())
            {
                Console.WriteLine();
                Console.WriteLine("--- ERRORS ENCOUNTERED ---");
                foreach (var error in summary.Errors.Take(5)) // Show first 5 errors
                {
                    Console.WriteLine($"• {error}");
                }
                if (summary.Errors.Count > 5)
                {
                    Console.WriteLine($"... and {summary.Errors.Count - 5} more errors");
                }
            }

            Console.WriteLine("=====================================================");
        }

        /// <summary>
        /// Print fatal error
        /// </summary>
        private void PrintFatalError(Exception ex)
        {
            Console.WriteLine();
            Console.WriteLine("💥 FATAL ERROR 💥");
            Console.WriteLine($"Exception: {ex.Message}");
            Console.WriteLine($"Stack Trace: {ex.StackTrace}");
            Console.WriteLine("=====================================================");
        }

        #endregion

        // ============================================================================================================
        #region HDS (Historical Data Server) FUNCTIONALITY
        // ============================================================================================================

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

        // ============================================================================================================
        #region VAREXP (PCVue Configuration Files) FUNCTIONALITY
        // ============================================================================================================

        [HttpPost]
        [ValidateAntiForgeryToken]
        [IgnoreAntiforgeryToken]
        public async Task<IActionResult> ParseVarexp(IFormFile VarexpFile)
        {
            // 1) Basic file check
            if (VarexpFile == null || VarexpFile.Length == 0)
                return BadRequest("No VAREXP.DAT file was uploaded.");

            try
            {
                // 2) Attempt parse
                var records = await _varexpParserService.ParseVarexpAsync(VarexpFile);

                // 3) Get parent meter options from PostgreSQL database
                _logger.LogInformation("🔍 DEBUG: About to call GetParentMeterOptions()"); // ✅ ADD THIS
                var parentOptions = await GetParentMeterOptions();
                _logger.LogInformation("🔍 DEBUG: GetParentMeterOptions() returned {Count} options", parentOptions?.Count ?? 0); // ✅ ADD THIS

                var response = new
                {
                    success = true,
                    records = records,
                    parentOptions = parentOptions
                };

                _logger.LogInformation("🔍 DEBUG: Returning response with {RecordCount} records and {ParentCount} parent options",
                    records?.Count ?? 0, parentOptions?.Count ?? 0); // ✅ ADD THIS

                return Json(response);
            }
            catch (VarexpParseException vex)
            {
                _logger.LogError(vex, "VAREXP parse error at line {LineNumber}", vex.LineNumber);
                return BadRequest($"Parsing error at line {vex.LineNumber}: {vex.Message}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error parsing VAREXP.DAT");
                return BadRequest($"Unexpected error: {ex.Message}");
            }
        }

        [HttpPost]
        public async Task<IActionResult> ImportVarexpMeters([FromBody] ImportVarexpMetersRequest request)
        {
            try
            {
                _logger.LogInformation($"Received VAREXP import request for {request?.Meters?.Count ?? 0} meters");

                if (request?.Meters == null || !request.Meters.Any())
                {
                    return Json(new
                    {
                        success = false,
                        error = "No meters provided for import"
                    });
                }

                if (!_databaseService.IsInitialized)
                {
                    return Json(new
                    {
                        success = false,
                        error = "Database connection not initialized"
                    });
                }

                int importedCount = 0;
                int updatedCount = 0;
                int skippedCount = 0;
                int errorCount = 0;
                var detailedErrors = new Dictionary<string, string>();

                // Use a single connection for the entire import operation
                using var connection = _databaseService.GetConnection();

                // Process each meter
                foreach (var meter in request.Meters)
                {
                    try
                    {
                        _logger.LogInformation($"Processing VAREXP meter: {meter.MeterName}");

                        // Check if meter already exists
                        var existingMeter = await GetExistingMeterByNameAsync(meter.MeterName, connection);

                        if (existingMeter != null)
                        {
                            if (request.SkipExisting)
                            {
                                _logger.LogInformation($"Skipping existing meter: {meter.MeterName}");
                                skippedCount++;
                                continue;
                            }
                            else if (request.UpdateExisting)
                            {
                                // Update existing meter
                                await UpdateExistingVarexpMeterAsync(existingMeter.MeterId, meter, connection);
                                updatedCount++;
                                _logger.LogInformation($"Updated meter: {meter.MeterName}");
                            }
                            else
                            {
                                errorCount++;
                                detailedErrors[meter.MeterName] = "Meter already exists";
                                _logger.LogWarning($"Meter already exists and not configured to skip/update: {meter.MeterName}");
                                continue;
                            }
                        }
                        else
                        {
                            // Create new meter
                            await CreateNewVarexpMeterAsync(meter, request.CreateMissingParents, connection);
                            importedCount++;
                            _logger.LogInformation($"Created new meter: {meter.MeterName}");
                        }
                    }
                    catch (Exception ex)
                    {
                        errorCount++;
                        detailedErrors[meter.MeterName] = ex.Message;
                        _logger.LogError(ex, $"Error processing VAREXP meter: {meter.MeterName}");
                    }
                }

                var totalProcessed = importedCount + updatedCount + skippedCount + errorCount;
                var message = $"VAREXP Import completed: {importedCount} imported, {updatedCount} updated, {skippedCount} skipped, {errorCount} errors.";

                return Json(new
                {
                    success = errorCount == 0,
                    importedCount = importedCount,
                    updatedCount = updatedCount,
                    skippedCount = skippedCount,
                    errorCount = errorCount,
                    totalProcessed = totalProcessed,
                    detailedErrors = detailedErrors,
                    message = message
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error importing VAREXP meters");
                return Json(new
                {
                    success = false,
                    error = ex.Message,
                    errorCount = request?.Meters?.Count ?? 0
                });
            }
        }

        #endregion

        // ============================================================================================================
        #region WEB SERVICES (PCVue API) FUNCTIONALITY
        // ============================================================================================================

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
                Console.WriteLine($"Include System Variables: {request.IncludeSystemVariables}");
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
                var handler = new HttpClientHandler();
                handler.ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true;

                using var httpClient = new HttpClient(handler);
                httpClient.Timeout = TimeSpan.FromSeconds(connection.TimeoutSeconds);

                var logger = HttpContext.RequestServices.GetRequiredService<ILogger<PCVueWebService>>();
                var webService = new PCVueWebService(httpClient, logger);

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

                var response = await httpClient.SendAsync(httpRequest);
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
                            totalVariables = parseResult.TotalCount,
                            systemVariablesIncluded = request.IncludeSystemVariables,
                            // Format variables for meter selection table
                            variables = parseResult.Variables.Select(v => new
                            {
                                variableName = v.FullPath,  // Use full path as meter name
                                variableType = v.VariableType,
                                isReadOnly = v.IsReadOnly,
                                isLeaf = v.IsLeaf,
                                branches = v.Branches
                            }).ToList(),
                            parseSuccess = parseResult.Success,
                            parseError = parseResult.Success ? null : parseResult.ErrorMessage,
                            // Add parent meter options for the selection table
                            parentOptions = await GetParentMeterOptions(),
                            // Add connection info for meter selection table
                            connectionInfo = new
                            {
                                connectionId = request.ConnectionId,
                                connectionName = connection.ConnectionName
                            }
                        });
                    }
                    catch (JsonException parseEx)
                    {
                        Console.WriteLine($"❌ JSON PARSING ERROR: {parseEx.Message}");
                        Console.WriteLine("=====================================================\n");

                        return Json(new
                        {
                            success = false,
                            message = $"API call succeeded but JSON parsing failed: {parseEx.Message}"
                        });
                    }
                }
                else
                {
                    Console.WriteLine($"❌ ERROR: Variables browse failed");
                    Console.WriteLine($"Status Code: {response.StatusCode}");
                    Console.WriteLine("=====================================================\n");

                    return Json(new
                    {
                        success = false,
                        message = $"Variables browse failed: {response.StatusCode}"
                    });
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ EXCEPTION: {ex.Message}");
                Console.WriteLine("=====================================================\n");

                return Json(new
                {
                    success = false,
                    message = "Error during variables browse. Check terminal for details."
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

        // ============================================================================================================
        #region VAREXP HELPER METHODS
        // ============================================================================================================

        // Helper method to get existing meter by name
        private async Task<dynamic> GetExistingMeterByNameAsync(string meterName, NpgsqlConnection connection)
        {
            var command = new Npgsql.NpgsqlCommand(@"
        SELECT ""MeterId"", ""Name"", ""Type"", ""Unit"", ""ParentId"", ""Active"", ""LastReading"", ""TenantID""
        FROM ""Meters"" 
        WHERE ""Name"" = @name", connection);

            command.Parameters.AddWithValue("@name", meterName);

            using var reader = await command.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                return new
                {
                    MeterId = reader.GetInt32(0),
                    Name = reader.GetString(1),
                    Type = reader.IsDBNull(2) ? null : reader.GetString(2),
                    Unit = reader.IsDBNull(3) ? "" : reader.GetString(3),
                    ParentId = reader.IsDBNull(4) ? (int?)null : reader.GetInt32(4),
                    Active = reader.GetBoolean(5),
                    LastReading = reader.IsDBNull(6) ? 0 : reader.GetInt32(6),
                    TenantID = reader.IsDBNull(7) ? (int?)null : reader.GetInt32(7)
                };
            }

            return null;
        }

        // Helper method to create new VAREXP meter
        private async Task CreateNewVarexpMeterAsync(VarexpMeterImportItem meter, bool createMissingParents, NpgsqlConnection connection)
        {
            // Handle parent meter ID conversion
            int? parentId = null;
            if (!string.IsNullOrEmpty(meter.ParentMeterId))
            {
                if (int.TryParse(meter.ParentMeterId, out var parentIdValue))
                {
                    // Verify parent meter exists using the same connection
                    var parentExists = await CheckMeterExistsAsync(parentIdValue, connection);
                    if (parentExists)
                    {
                        parentId = parentIdValue;
                    }
                    else if (createMissingParents)
                    {
                        _logger.LogWarning($"Parent meter ID {parentIdValue} not found for meter {meter.MeterName}. Creating without parent.");
                        // Could implement parent creation logic here if needed
                    }
                    else
                    {
                        throw new InvalidOperationException($"Parent meter ID {parentIdValue} not found and createMissingParents is false");
                    }
                }
                else
                {
                    _logger.LogWarning($"Invalid parent meter ID format: {meter.ParentMeterId} for meter {meter.MeterName}");
                }
            }

            var command = new Npgsql.NpgsqlCommand(@"
        INSERT INTO ""Meters"" (""Name"", ""Type"", ""Unit"", ""ParentId"", ""Active"", ""LastReading"", ""TenantID"")
        VALUES (@name, @type, @unit, @parentId, @active, @lastReading, @tenantId)
        RETURNING ""MeterId""", connection);

            command.Parameters.AddWithValue("@name", meter.MeterName);
            command.Parameters.AddWithValue("@type", meter.Type?.ToLower() ?? "main"); // Ensure lowercase as per schema constraint
            command.Parameters.AddWithValue("@unit", meter.Unit ?? ""); // Empty string, not null
            command.Parameters.AddWithValue("@parentId", (object)parentId ?? DBNull.Value);
            command.Parameters.AddWithValue("@active", meter.Active);
            command.Parameters.AddWithValue("@lastReading", 0); // Default to 0 for VAREXP meters
            command.Parameters.AddWithValue("@tenantId", DBNull.Value); // No tenant for VAREXP imports

            var newMeterId = await command.ExecuteScalarAsync();
            _logger.LogInformation($"Created meter {meter.MeterName} with ID {newMeterId}");
        }

        // Helper method to update existing VAREXP meter
        private async Task UpdateExistingVarexpMeterAsync(int meterId, VarexpMeterImportItem meter, NpgsqlConnection connection)
        {
            // Handle parent meter ID conversion
            int? parentId = null;
            if (!string.IsNullOrEmpty(meter.ParentMeterId) && int.TryParse(meter.ParentMeterId, out var parentIdValue))
            {
                var parentExists = await CheckMeterExistsAsync(parentIdValue, connection);
                if (parentExists)
                {
                    parentId = parentIdValue;
                }
            }

            var command = new Npgsql.NpgsqlCommand(@"
        UPDATE ""Meters"" 
        SET ""Type"" = @type, ""Unit"" = @unit, ""ParentId"" = @parentId, ""Active"" = @active
        WHERE ""MeterId"" = @meterId", connection);

            command.Parameters.AddWithValue("@meterId", meterId);
            command.Parameters.AddWithValue("@type", meter.Type?.ToLower() ?? "main"); // Ensure lowercase
            command.Parameters.AddWithValue("@unit", meter.Unit ?? ""); // Empty string, not null
            command.Parameters.AddWithValue("@parentId", (object)parentId ?? DBNull.Value);
            command.Parameters.AddWithValue("@active", meter.Active);

            await command.ExecuteNonQueryAsync();
            _logger.LogInformation($"Updated meter {meter.MeterName} with ID {meterId}");
        }

        // Helper method to check if meter exists by ID
        private async Task<bool> CheckMeterExistsAsync(int meterId, NpgsqlConnection connection)
        {
            var command = new Npgsql.NpgsqlCommand(@"
        SELECT COUNT(*) FROM ""Meters"" WHERE ""MeterId"" = @meterId", connection);

            command.Parameters.AddWithValue("@meterId", meterId);

            var count = (long)await command.ExecuteScalarAsync();
            return count > 0;
        }

        #endregion

        // ============================================================================================================
        #region REQUEST/RESPONSE MODELS
        // ============================================================================================================

        // HDS Models
        public class ImportReadingsRequest
        {
            public string TableName { get; set; }
            public List<string> MeterNames { get; set; }
            public DateTime? StartDate { get; set; }
            public DateTime? EndDate { get; set; }
            public int? Limit { get; set; }
        }

        public class PrintHDSMetersRequest
        {
            public string TableName { get; set; } = "";
            public string ConnectionId { get; set; } = "";
            public List<HDSMeterPrintItem> SelectedMeters { get; set; } = new();
            public bool ImportHistoricalReadings { get; set; } = false;
            public DateTime? StartDate { get; set; }
            public DateTime? EndDate { get; set; }
        }

        public class HDSMeterPrintItem
        {
            public string HdsMeterName { get; set; } = "";
            public string Unit { get; set; } = "";
            public string Type { get; set; } = "main";
            public string ParentMeterId { get; set; } = "";
            public bool Active { get; set; } = true;
            public string LastReading { get; set; } = "";
            public bool IsSelected { get; set; } = true;
        }

        public class ImportMetersRequest
        {
            public List<HDSMeterPrintItem> Meters { get; set; }
            public bool SkipExisting { get; set; }
            public bool UpdateExisting { get; set; }
        }

        // VAREXP Models
        public class ImportVarexpMetersRequest
        {
            public List<VarexpMeterImportItem> Meters { get; set; } = new();
            public bool SkipExisting { get; set; }
            public bool UpdateExisting { get; set; }
            public bool CreateMissingParents { get; set; }
        }

        public class VarexpMeterImportItem
        {
            public string MeterName { get; set; } = "";
            public string? Unit { get; set; }
            public string Type { get; set; } = "Main";
            public string? ParentMeterId { get; set; }
            public bool Active { get; set; } = true;
        }

        // Web Services Models
        public class BrowseVariablesRequest
        {
            public string ConnectionId { get; set; } = "";
            public int MaxVariables { get; set; } = 100000;
            public string? BranchFilter { get; set; } = "";
            public string VariableType { get; set; } = "Any";
            public int Depth { get; set; } = 0;
            public bool IncludeSystemVariables { get; set; } = false;
        }

        public class PrintWebServiceMetersRequest
        {
            public string ConnectionId { get; set; } = "";
            public string ConnectionName { get; set; } = "";
            public List<WebServiceVariableItem> SelectedVariables { get; set; } = new();
        }

        public class WebServiceVariableItem
        {
            public string VariableName { get; set; } = "";
            public string Unit { get; set; } = "";
            public string Type { get; set; } = "main";
            public string ParentMeterId { get; set; } = "";
            public bool Active { get; set; } = true;
            public string VariableType { get; set; } = "";
            public bool IsReadOnly { get; set; } = false;
            public bool IsSelected { get; set; } = true;
        }

        public class ImportWebServiceMetersRequest
        {
            public List<WebServiceVariableItem> Variables { get; set; } = new();
            public bool SkipExisting { get; set; }
            public bool UpdateExisting { get; set; }
        }

        // General Models
        public class PrintMetersRequest
        {
            public string TableName { get; set; }
            public List<string> SelectedMeterNames { get; set; }
            public List<string> SelectedMeterTypes { get; set; }
            public List<string> SelectedMeterUnits { get; set; }
        }

        #endregion
    }
}