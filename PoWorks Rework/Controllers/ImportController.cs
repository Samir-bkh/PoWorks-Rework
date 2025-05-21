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

        public ImportController(
            ILogger<ImportController> logger,
            SqlServerService sqlServerService,
            DatabaseService databaseService)
        {
            _logger = logger;
            _sqlServerService = sqlServerService;
            _databaseService = databaseService;
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

        [HttpGet]
        public async Task<IActionResult> GetMetersFromTable(string tableName, string startDate = null, string endDate = null, int limit = 1000)
        {
            try
            {
                if (!_sqlServerService.IsInitialized)
                {
                    return Json(new { success = false, error = "SQL Server connection not configured" });
                }

                // Adjust this call to match the method signature in your SqlServerService
                // If your service doesn't accept all these parameters, modify accordingly
                var hdsMeters = await _sqlServerService.GetDistinctMeterNames(tableName);

                // Get parent meter options from PostgreSQL database
                var parentOptions = await GetParentMeterOptions();

                return Json(new
                {
                    success = true,
                    meters = hdsMeters,
                    parentOptions = parentOptions
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error getting meters from HDS table {tableName}");
                return Json(new { success = false, error = ex.Message });
            }
        }

        [HttpPost]
        public async Task<IActionResult> ImportMeterReadings([FromBody] ImportReadingsRequest request)
        {
            _logger.LogInformation("================================================");
            _logger.LogInformation("IMPORT METER READINGS - DEBUGGING");
            _logger.LogInformation("================================================");

            try
            {
                // 1. Log the request data
                _logger.LogInformation($"Table name: {request?.TableName ?? "NULL"}");
                _logger.LogInformation($"Meter names count: {request?.MeterNames?.Count.ToString() ?? "NULL"}");

                if (request?.MeterNames != null)
                {
                    foreach (var name in request.MeterNames)
                    {
                        _logger.LogInformation($"Meter name: {name}");
                    }
                }

                // 2. Basic validation
                if (request == null || string.IsNullOrEmpty(request.TableName))
                {
                    _logger.LogError("Invalid request: Missing table name");
                    return Json(new { success = false, error = "Missing table name" });
                }

                if (request.MeterNames == null || request.MeterNames.Count == 0)
                {
                    _logger.LogError("Invalid request: No meter names provided");
                    return Json(new { success = false, error = "No meter names provided" });
                }

                // 3. Check database connections
                if (!_databaseService.IsInitialized)
                {
                    _logger.LogError("PostgreSQL database is not initialized");
                    return Json(new { success = false, error = "PostgreSQL database is not initialized" });
                }

                if (!_sqlServerService.IsInitialized)
                {
                    _logger.LogError("SQL Server is not initialized");
                    return Json(new { success = false, error = "SQL Server is not initialized" });
                }

                // 4. Test SQL Server connection
                try
                {
                    using (var connection = _sqlServerService.GetConnection())
                    {
                        await connection.OpenAsync();
                        _logger.LogInformation("SQL Server connection test: SUCCESS");

                        // Test a simple query to check if the table exists
                        string testSql = $"SELECT TOP 1 * FROM {request.TableName}";
                        try
                        {
                            using var cmd = new Microsoft.Data.SqlClient.SqlCommand(testSql, connection);
                            using var reader = await cmd.ExecuteReaderAsync();

                            // Log column names
                            var columns = new List<string>();
                            for (int i = 0; i < reader.FieldCount; i++)
                            {
                                columns.Add($"{reader.GetName(i)} ({reader.GetFieldType(i).Name})");
                            }

                            _logger.LogInformation($"Table columns: {string.Join(", ", columns)}");

                            if (await reader.ReadAsync())
                            {
                                _logger.LogInformation("First row values:");
                                for (int i = 0; i < reader.FieldCount; i++)
                                {
                                    _logger.LogInformation($"  {reader.GetName(i)}: {(reader.IsDBNull(i) ? "NULL" : reader.GetValue(i).ToString())}");
                                }
                            }
                            else
                            {
                                _logger.LogWarning("Table exists but has no data");
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, $"Error testing table {request.TableName}");
                            return Json(new { success = false, error = $"Error accessing table: {ex.Message}" });
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "SQL Server connection test failed");
                    return Json(new { success = false, error = $"SQL Server connection error: {ex.Message}" });
                }

                // 5. Test PostgreSQL connection and MeterReadings table
                try
                {
                    using (var connection = new NpgsqlConnection(_databaseService.GetConnectionString()))
                    {
                        await connection.OpenAsync();
                        _logger.LogInformation("PostgreSQL connection test: SUCCESS");

                        // Check if MeterReadings table exists
                        try
                        {
                            using var cmd = new NpgsqlCommand(
                                @"SELECT EXISTS (
                            SELECT FROM information_schema.tables 
                            WHERE table_name = 'MeterReadings'
                          )", connection);

                            bool tableExists = (bool)await cmd.ExecuteScalarAsync();
                            _logger.LogInformation($"MeterReadings table exists: {tableExists}");

                            if (!tableExists)
                            {
                                _logger.LogError("MeterReadings table does not exist in PostgreSQL");
                                return Json(new { success = false, error = "MeterReadings table does not exist" });
                            }

                            // Check table structure
                            using var structureCmd = new NpgsqlCommand(
                                @"SELECT column_name, data_type 
                          FROM information_schema.columns 
                          WHERE table_name = 'MeterReadings'", connection);

                            using var reader = await structureCmd.ExecuteReaderAsync();
                            _logger.LogInformation("MeterReadings table structure:");

                            while (await reader.ReadAsync())
                            {
                                _logger.LogInformation($"  {reader.GetString(0)}: {reader.GetString(1)}");
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Error checking MeterReadings table");
                            return Json(new { success = false, error = $"Error checking MeterReadings table: {ex.Message}" });
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "PostgreSQL connection test failed");
                    return Json(new { success = false, error = $"PostgreSQL connection error: {ex.Message}" });
                }

                // 6. Now try to handle a single meter as a test
                if (request.MeterNames.Count > 0)
                {
                    string testMeterName = request.MeterNames[0];
                    _logger.LogInformation($"Testing import for meter: {testMeterName}");

                    // Find meter ID
                    int? meterId = null;

                    try
                    {
                        using (var connection = new NpgsqlConnection(_databaseService.GetConnectionString()))
                        {
                            await connection.OpenAsync();

                            using var cmd = new NpgsqlCommand(
                                @"SELECT ""MeterId"" FROM ""Meters"" WHERE ""Name"" = @Name", connection);
                            cmd.Parameters.AddWithValue("@Name", testMeterName);

                            var result = await cmd.ExecuteScalarAsync();
                            if (result != null)
                            {
                                meterId = Convert.ToInt32(result);
                                _logger.LogInformation($"Found meter {testMeterName} with ID: {meterId}");
                            }
                            else
                            {
                                _logger.LogWarning($"Meter {testMeterName} not found in PostgreSQL");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, $"Error finding meter ID for {testMeterName}");
                        return Json(new { success = false, error = $"Error finding meter ID: {ex.Message}" });
                    }

                    if (!meterId.HasValue)
                    {
                        _logger.LogError($"Cannot proceed with readings import - meter {testMeterName} not found");
                        return Json(new { success = false, error = $"Meter {testMeterName} not found in database" });
                    }

                    // Get a sample of readings from SQL Server
                    try
                    {
                        using (var connection = _sqlServerService.GetConnection())
                        {
                            await connection.OpenAsync();

                            string sql = $"SELECT TOP 10 Chrono, Value, Quality FROM {request.TableName} WHERE NAME = @Name";
                            _logger.LogInformation($"Sample query: {sql}");

                            using var cmd = new Microsoft.Data.SqlClient.SqlCommand(sql, connection);
                            cmd.Parameters.AddWithValue("@Name", testMeterName);

                            using var reader = await cmd.ExecuteReaderAsync();
                            _logger.LogInformation($"Sample readings for {testMeterName}:");

                            int count = 0;
                            while (await reader.ReadAsync())
                            {
                                count++;

                                try
                                {
                                    // Get column values with detailed type info for debugging
                                    string chronoType = reader.GetFieldType(0).Name;
                                    string valueType = reader.GetFieldType(1).Name;
                                    string qualityType = reader.GetFieldType(2).Name;

                                    object chronoValue = reader.IsDBNull(0) ? "NULL" : reader.GetValue(0);
                                    object valueValue = reader.IsDBNull(1) ? "NULL" : reader.GetValue(1);
                                    object qualityValue = reader.IsDBNull(2) ? "NULL" : reader.GetValue(2);

                                    _logger.LogInformation($"Reading {count}:");
                                    _logger.LogInformation($"  Chrono: {chronoValue} (Type: {chronoType})");
                                    _logger.LogInformation($"  Value: {valueValue} (Type: {valueType})");
                                    _logger.LogInformation($"  Quality: {qualityValue} (Type: {qualityType})");

                                    // Try parsing the timestamp
                                    if (chronoValue != "NULL")
                                    {
                                        try
                                        {
                                            // Get raw value
                                            long chrono;
                                            if (chronoValue is long longValue)
                                            {
                                                chrono = longValue;
                                            }
                                            else
                                            {
                                                chrono = Convert.ToInt64(chronoValue);
                                            }

                                            // Try to convert from Windows filetime
                                            DateTime timestamp = DateTime.FromFileTimeUtc(chrono);
                                            _logger.LogInformation($"  Converted timestamp: {timestamp}");
                                        }
                                        catch (Exception ex)
                                        {
                                            _logger.LogError(ex, "Error converting timestamp");
                                        }
                                    }
                                }
                                catch (Exception ex)
                                {
                                    _logger.LogError(ex, $"Error examining reading {count}");
                                }
                            }

                            _logger.LogInformation($"Found {count} sample readings");
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error getting sample readings from SQL Server");
                        return Json(new { success = false, error = $"Error retrieving sample readings: {ex.Message}" });
                    }

                    // Try to insert a single sample reading
                    try
                    {
                        using (var connection = new NpgsqlConnection(_databaseService.GetConnectionString()))
                        {
                            await connection.OpenAsync();

                            // Use hard-coded test values for a direct test
                            DateTime now = DateTime.UtcNow;

                            using var cmd = new NpgsqlCommand(
                                @"INSERT INTO ""MeterReadings"" (""MeterId"", ""Timestamp"", ""Value"", ""Quality"")
                          VALUES (@MeterId, @Timestamp, @Value, @Quality)", connection);

                            cmd.Parameters.AddWithValue("@MeterId", meterId.Value);
                            cmd.Parameters.AddWithValue("@Timestamp", now);
                            cmd.Parameters.AddWithValue("@Value", 42.0);
                            cmd.Parameters.AddWithValue("@Quality", 100);

                            int rowsAffected = await cmd.ExecuteNonQueryAsync();
                            _logger.LogInformation($"Test insert result: {rowsAffected} rows affected");
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error inserting test reading to PostgreSQL");
                        return Json(new
                        {
                            success = false,
                            error = $"Error inserting test reading: {ex.Message}",
                            details = $"Stack trace: {ex.StackTrace}"
                        });
                    }
                }

                // Return information about what we found, not a real import yet
                return Json(new
                {
                    success = true,
                    message = "Debugging complete - check server logs for details",
                    meterCount = request.MeterNames.Count,
                    tableName = request.TableName
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

        private async Task<List<SelectListItem>> GetParentMeterOptions()
        {
            var options = new List<SelectListItem>
            {
                new SelectListItem { Value = "", Text = "None" }
            };

            try
            {
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