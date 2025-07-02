using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Data.SqlClient;
using Npgsql;
using PoWorks_Rework.Models;
using PoWorks_Rework.Services;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using static PoWorks_Rework.Controllers.HdsImportController;
using System.Net.Http;
using System.Text.Json;

namespace PoWorks_Rework.Controllers
{
    public class ImportController : Controller
    {
        private readonly ILogger<ImportController> _logger;
        private readonly SqlServerService _sqlServerService;
        private readonly DatabaseService _databaseService;
        private readonly VarexpParserService _varexpParserService;

        public ImportController(
            ILogger<ImportController> logger,
            SqlServerService sqlServerService,
            DatabaseService databaseService,
            VarexpParserService varexpParserService)
        {
            _logger = logger;
            _sqlServerService = sqlServerService;
            _databaseService = databaseService;
            _varexpParserService = varexpParserService;
        }

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

        // REPLACE the GetMetersFromTable method in your ImportController.cs with this version

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

        public class ImportReadingsRequest
        {
            public string TableName { get; set; }
            public List<string> MeterNames { get; set; }
            public DateTime? StartDate { get; set; }
            public DateTime? EndDate { get; set; }
            public int? Limit { get; set; }
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

        // HDS-specific request model
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

        // Replace the BrowseVariablesWebService method in ImportController.cs with this debug version

        [HttpPost]
        public async Task<IActionResult> BrowseVariablesWebService([FromBody] BrowseVariablesRequest request)
        {
            try
            {
                Console.WriteLine("\n=====================================================");
                Console.WriteLine("PCVue VARIABLES BROWSE - RAW RESPONSE DEBUG");
                Console.WriteLine("=====================================================");
                Console.WriteLine($"Connection ID: {request.ConnectionId}");
                Console.WriteLine($"Max Variables: {request.MaxVariables}");
                Console.WriteLine($"Branch Filter: {request.BranchFilter ?? "None"}");
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
                Console.WriteLine($"Base URL: {connection.BaseUrl}");

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

                Console.WriteLine("\n--- VARIABLES REQUEST ---");
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
                    Console.WriteLine("\n🎉 VARIABLES BROWSE SUCCESS!");

                    // ✅ PRINT THE COMPLETE RAW RESPONSE
                    Console.WriteLine("\n=== RAW RESPONSE START ===");
                    Console.WriteLine(responseContent);
                    Console.WriteLine("=== RAW RESPONSE END ===\n");

                    // Also try to pretty-print the JSON structure
                    try
                    {
                        var jsonData = JsonSerializer.Deserialize<JsonElement>(responseContent);
                        Console.WriteLine("\n=== JSON STRUCTURE ANALYSIS ===");
                        Console.WriteLine($"Root element type: {jsonData.ValueKind}");

                        if (jsonData.ValueKind == JsonValueKind.Object)
                        {
                            Console.WriteLine("Root properties found:");
                            foreach (var property in jsonData.EnumerateObject())
                            {
                                Console.WriteLine($"  - {property.Name}: {property.Value.ValueKind}");

                                // If it's an array, show how many items
                                if (property.Value.ValueKind == JsonValueKind.Array)
                                {
                                    Console.WriteLine($"    Array length: {property.Value.GetArrayLength()}");

                                    // Show first array item structure if it exists
                                    if (property.Value.GetArrayLength() > 0)
                                    {
                                        var firstItem = property.Value[0];
                                        Console.WriteLine($"    First item type: {firstItem.ValueKind}");
                                        if (firstItem.ValueKind == JsonValueKind.Object)
                                        {
                                            Console.WriteLine("    First item properties:");
                                            foreach (var subProp in firstItem.EnumerateObject())
                                            {
                                                Console.WriteLine($"      - {subProp.Name}: {subProp.Value.ValueKind}");
                                            }
                                        }
                                    }
                                }
                            }
                        }

                        Console.WriteLine("==========================\n");
                    }
                    catch (JsonException parseEx)
                    {
                        Console.WriteLine($"❌ JSON PARSING ERROR: {parseEx.Message}");
                        Console.WriteLine("This might not be valid JSON!\n");
                    }

                    Console.WriteLine($"✅ Raw response printed above");
                    Console.WriteLine($"End Time: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                    Console.WriteLine("=====================================================\n");

                    return Json(new
                    {
                        success = true,
                        message = $"Variables browse completed! Response length: {responseContent?.Length ?? 0} characters. Check terminal for RAW RESPONSE.",
                        responseLength = responseContent?.Length ?? 0
                    });
                }
                else
                {
                    Console.WriteLine($"❌ ERROR: Variables browse failed");
                    Console.WriteLine($"Status Code: {response.StatusCode}");
                    Console.WriteLine($"Response: {responseContent}");
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
                Console.WriteLine($"Stack Trace: {ex.StackTrace}");
                Console.WriteLine("=====================================================\n");

                return Json(new
                {
                    success = false,
                    message = "Error during variables browse. Check terminal for details."
                });
            }
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

        // Request models for VAREXP import
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

        // Create a class to receive the data from the request
        public class PrintMetersRequest
        {
            public string TableName { get; set; }
            public List<string> SelectedMeterNames { get; set; }
            public List<string> SelectedMeterTypes { get; set; }
            public List<string> SelectedMeterUnits { get; set; }
        }

        // Updated request model for Variables browsing
        public class BrowseVariablesRequest
        {
            public string ConnectionId { get; set; } = "";
            public int MaxVariables { get; set; } = 100000;
            public string? BranchFilter { get; set; }
            public string VariableType { get; set; } = "Any";
            public int Depth { get; set; } = 0;
        }

        // Enhanced method to get parent meter options with better error handling
        // Helper methods (CORRECTED - update this existing method)
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
    }
}