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
                return Json(new { success = true, records });
            }
            catch (VarexpParseException vex)
            {
                _logger.LogError(vex, "VAREXP parse error at line {LineNumber}", vex.LineNumber);
                // return 400 with the exact parse-error message
                return BadRequest($"Parsing error at line {vex.LineNumber}: {vex.Message}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error parsing VAREXP.DAT");
                return BadRequest($"Unexpected error: {ex.Message}");
            }
        }



        [HttpGet]
        public async Task<IActionResult> GetHdsTables()
        {
            try
            {
                if (!_sqlServerService.IsInitialized)
                {
                    return Json(new { success = false, error = "SQL Server connection not configured" });
                }

                var tables = await _sqlServerService.GetAvailableTables();
                return Json(new { success = true, tables = tables });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting tables from HDS");
                return Json(new { success = false, error = ex.Message });
            }
        }

        // Controllers/ImportController.cs - Updated GetMetersFromTable method
        [HttpGet]
        public async Task<IActionResult> GetMetersFromTable(string tableName, string startDate = null, string endDate = null, int limit = 1000)
        {
            try
            {
                _logger.LogInformation($"GetMetersFromTable called: tableName='{tableName}', limit={limit}");

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

                _logger.LogInformation($"Processing request for table '{tableName}' with limit {limit}");

                // Validate that the table exists before trying to query it
                var tableExists = await _sqlServerService.ValidateTableExists(tableName);
                if (!tableExists)
                {
                    _logger.LogWarning($"Table '{tableName}' does not exist or is not accessible");
                    return Json(new
                    {
                        success = false,
                        error = $"Table '{tableName}' does not exist or is not accessible. Please verify the table name and permissions."
                    });
                }

                // Get the HDS meters with the specified limit
                var hdsMeters = await _sqlServerService.GetDistinctMeterNames(tableName, limit);

                // Get parent meter options from PostgreSQL database
                var parentOptions = await GetParentMeterOptions();

                _logger.LogInformation($"Successfully retrieved {hdsMeters.Count} meters from table '{tableName}'");

                return Json(new
                {
                    success = true,
                    meters = hdsMeters,
                    parentOptions = parentOptions,
                    actualCount = hdsMeters.Count,
                    requestedLimit = limit,
                    tableName = tableName,
                    message = $"Retrieved {hdsMeters.Count} meters from table '{tableName}' (limit: {limit})"
                });
            }
            catch (Microsoft.Data.SqlClient.SqlException sqlEx)
            {
                // Handle specific SQL Server errors
                _logger.LogError(sqlEx, $"SQL Server error getting meters from table '{tableName}' with limit {limit}");

                string errorMessage = "Database error occurred";

                // Provide more specific error messages based on SQL error
                switch (sqlEx.Number)
                {
                    case 208: // Invalid object name
                        errorMessage = $"Table '{tableName}' does not exist or is not accessible";
                        break;
                    case 102: // Incorrect syntax
                        errorMessage = $"SQL syntax error - please check table name '{tableName}'";
                        break;
                    case 2: // Timeout
                        errorMessage = "Database query timeout - try reducing the limit or check table size";
                        break;
                    case 18456: // Login failed
                        errorMessage = "Database authentication failed - check connection settings";
                        break;
                    default:
                        errorMessage = $"Database error: {sqlEx.Message}";
                        break;
                }

                return Json(new
                {
                    success = false,
                    error = errorMessage,
                    sqlErrorNumber = sqlEx.Number,
                    details = $"SQL Error {sqlEx.Number}: {sqlEx.Message}"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Unexpected error getting meters from table '{tableName}' with limit {limit}");
                return Json(new
                {
                    success = false,
                    error = $"Unexpected error: {ex.Message}",
                    details = ex.ToString()
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
                                            else if (request.CreateMissingParents)
                                            {
                                                // Create a missing parent if requested
                                                _logger.LogWarning($"Parent meter with ID {parsedParentId} not found for {meter.HdsMeterName}");
                                                parentId = null;
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

        // Enhanced method to get parent meter options with better error handling
        private async Task<List<SelectListItem>> GetParentMeterOptions()
        {
            var options = new List<SelectListItem>
    {
        new SelectListItem { Value = "", Text = "None" }
    };

            try
            {
                if (!_databaseService.IsInitialized)
                {
                    _logger.LogWarning("PostgreSQL database not initialized - returning empty parent options");
                    return options;
                }

                using (var connection = new NpgsqlConnection(_databaseService.GetConnectionString()))
                {
                    await connection.OpenAsync();

                    string sql = @"
                SELECT ""MeterId"", ""Name"" 
                FROM ""Meters"" 
                WHERE ""Type"" = 'main' AND ""Active"" = true
                ORDER BY ""Name""";

                    using (var command = new NpgsqlCommand(sql, connection))
                    {
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

                _logger.LogInformation($"Retrieved {options.Count - 1} parent meter options");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting parent meter options from PostgreSQL");
                // Don't throw here, just return the basic options
            }

            return options;
        }
    }
}