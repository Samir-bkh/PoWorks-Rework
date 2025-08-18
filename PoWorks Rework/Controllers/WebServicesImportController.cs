using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Npgsql;
using PoWorks_Rework.Models;
using PoWorks_Rework.Services;
using PoWorks_Rework.Repositories;
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

        public WebServicesImportController(
            ILogger<WebServicesImportController> logger,
            DatabaseService databaseService,
            VariableBrowseParsingService variableBrowseParsingService,
            TrendsService trendsService,
            MeterRepository meterRepository)
        {
            _logger = logger;
            _databaseService = databaseService;
            _variableBrowseParsingService = variableBrowseParsingService;
            _trendsService = trendsService;
            _meterRepository = meterRepository;
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

        [HttpPost]
        public async Task<IActionResult> ImportWebServiceMeters([FromBody] ImportWebServiceMetersRequest request)
        {
            try
            {
                _logger.LogInformation($"Received Web Service import request for {request?.Variables?.Count ?? 0} variables");

                if (request?.Variables == null || !request.Variables.Any())
                {
                    return Json(new
                    {
                        success = false,
                        error = "No variables provided for import"
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
                var errorVariables = new List<string>();
                var detailedErrors = new Dictionary<string, string>();

                using var connection = _databaseService.GetConnection();
                using var transaction = await connection.BeginTransactionAsync();

                try
                {
                    foreach (var variable in request.Variables)
                    {
                        try
                        {
                            _logger.LogInformation($"Processing Web Service variable: {variable.VariableName}");

                            // Check if meter already exists by name (using correct column name "Name")
                            var checkCommand = new NpgsqlCommand(@"
                                SELECT ""MeterId"" FROM ""Meters"" 
                                WHERE ""Name"" = @meterName", connection, transaction);
                            checkCommand.Parameters.AddWithValue("@meterName", variable.VariableName);

                            var existingMeterId = await checkCommand.ExecuteScalarAsync();

                            if (existingMeterId != null)
                            {
                                if (request.SkipExisting)
                                {
                                    _logger.LogInformation($"Skipping existing meter: {variable.VariableName}");
                                    skippedCount++;
                                    continue;
                                }
                                else if (request.UpdateExisting)
                                {
                                    // Update existing meter
                                    int? parentId = null;
                                    if (!string.IsNullOrEmpty(variable.ParentMeterId) && int.TryParse(variable.ParentMeterId, out var parentIdValue))
                                    {
                                        parentId = parentIdValue;
                                    }

                                    var type = variable.Type?.ToLower() ?? "main";
                                    var updateCommand = new NpgsqlCommand(@"
                                        UPDATE ""Meters"" 
                                        SET ""Type"" = @type, ""Unit"" = @unit, ""ParentId"" = @parentId, ""Active"" = @active
                                        WHERE ""MeterId"" = @meterId", connection, transaction);

                                    updateCommand.Parameters.AddWithValue("@meterId", existingMeterId);
                                    updateCommand.Parameters.AddWithValue("@type", type);
                                    updateCommand.Parameters.AddWithValue("@unit", variable.Unit ?? "");
                                    updateCommand.Parameters.AddWithValue("@parentId", parentId.HasValue ? parentId.Value : DBNull.Value);
                                    updateCommand.Parameters.AddWithValue("@active", variable.Active);

                                    await updateCommand.ExecuteNonQueryAsync();
                                    updatedCount++;
                                    _logger.LogInformation($"Updated meter: {variable.VariableName}");
                                }
                                else
                                {
                                    // This case happens when the meter exists but we're not updating
                                    _logger.LogInformation($"Variable {variable.VariableName} exists as meter but not updating due to settings");
                                    skippedCount++;
                                }
                            }
                            else
                            {
                                // Create new meter (using correct column name "Name")
                                int? parentId = null;
                                if (!string.IsNullOrEmpty(variable.ParentMeterId) && int.TryParse(variable.ParentMeterId, out var parentIdValue))
                                {
                                    // Check if parent meter exists
                                    var parentCheckCommand = new NpgsqlCommand(@"
                                        SELECT COUNT(*) FROM ""Meters"" WHERE ""MeterId"" = @parentId", connection, transaction);
                                    parentCheckCommand.Parameters.AddWithValue("@parentId", parentIdValue);
                                    var parentExists = (long)await parentCheckCommand.ExecuteScalarAsync() > 0;

                                    if (parentExists)
                                    {
                                        parentId = parentIdValue;
                                    }
                                }

                                var type = variable.Type?.ToLower() ?? "main";
                                var insertCommand = new NpgsqlCommand(@"
                                    INSERT INTO ""Meters"" (""Name"", ""Unit"", ""ParentId"", ""LastReading"", ""Type"", ""Active"", ""TenantId"")
                                    VALUES (@meterName, @unit, @parentId, @lastReading, @type, @active, @tenantId)
                                    RETURNING ""MeterId""", connection, transaction);

                                insertCommand.Parameters.AddWithValue("@meterName", variable.VariableName);
                                insertCommand.Parameters.AddWithValue("@unit", variable.Unit ?? "");
                                insertCommand.Parameters.AddWithValue("@parentId", parentId.HasValue ? parentId.Value : DBNull.Value);
                                insertCommand.Parameters.AddWithValue("@lastReading", 0); // Default for web service variables
                                insertCommand.Parameters.AddWithValue("@type", type);
                                insertCommand.Parameters.AddWithValue("@active", variable.Active);
                                insertCommand.Parameters.AddWithValue("@tenantId", DBNull.Value);

                                var newMeterId = await insertCommand.ExecuteScalarAsync();
                                importedCount++;
                                _logger.LogInformation($"Imported new meter from variable: {variable.VariableName}, ID: {newMeterId}");
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
    }
}