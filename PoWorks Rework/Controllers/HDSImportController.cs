// Controllers/HdsImportController.cs
using Microsoft.AspNetCore.Mvc;
using PoWorks_Rework.Models;
using PoWorks_Rework.Services;


namespace PoWorks_Rework.Controllers
{
    public class HdsImportController : Controller
    {
        private readonly ILogger<HdsImportController> _logger;
        private readonly SqlServerService _sqlServerService;
        private readonly DatabaseService _databaseService;

        public HdsImportController(
            ILogger<HdsImportController> logger,
            SqlServerService sqlServerService,
            DatabaseService databaseService)
        {
            _logger = logger;
            _sqlServerService = sqlServerService;
            _databaseService = databaseService;
        }

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

                // Validate the connection exists
                if (!string.IsNullOrEmpty(connectionId))
                {
                    var connections = _sqlServerService.GetAllConnections();
                    if (!connections.Any(c => c.ConnectionId == connectionId))
                    {
                        return Json(new { success = false, error = $"Connection '{connectionId}' not found" });
                    }
                }

                // Get the HDS meters using the specified connection
                var hdsMeters = await _sqlServerService.GetDistinctMeterNames(tableName, limit, connectionId);

                // Get parent meter options
                var parentOptions = await GetParentMeterOptions();

                return Json(new
                {
                    success = true,
                    meters = hdsMeters,
                    parentOptions = parentOptions,
                    connectionId = connectionId,
                    tableName = tableName
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error getting meters from HDS table {tableName} on connection '{connectionId ?? "default"}'");
                return Json(new { success = false, error = ex.Message });
            }
        }

        public class ImportResult
        {
            public bool Success { get; set; }
            public int ImportedCount { get; set; }
            public int UpdatedCount { get; set; }
            public int ErrorCount { get; set; }
            public string Message { get; set; } = "";
        }

        private ImportResult ProcessMeterImport(List<HDSMeterItem> meters, string connectionId)
        {
            var result = new ImportResult
            {
                Success = false,
                ImportedCount = 0,
                UpdatedCount = 0,
                ErrorCount = 0,
                Message = ""
            };

            try
            {
                if (!_databaseService.IsInitialized)
                {
                    result.Message = "PostgreSQL database not configured";
                    return result;
                }

                int importedCount = 0;
                int updatedCount = 0;
                int errorCount = 0;
                var errorMessages = new List<string>();

                using (var connection = new Npgsql.NpgsqlConnection(_databaseService.GetConnectionString()))
                {
                    connection.Open();

                    foreach (var meter in meters)
                    {
                        try
                        {
                            // Check if meter already exists
                            var checkCommand = new Npgsql.NpgsqlCommand(@"
                        SELECT ""MeterId"" FROM ""Meters"" WHERE ""Name"" = @Name", connection);
                            checkCommand.Parameters.AddWithValue("@Name", meter.HdsMeterName);

                            var existingId = checkCommand.ExecuteScalar();

                            if (existingId == null)
                            {
                                // Insert new meter
                                var insertCommand = new Npgsql.NpgsqlCommand(@"
                            INSERT INTO ""Meters"" (""Name"", ""Unit"", ""Type"", ""Active"", ""ParentId"", ""LastReading"") 
                            VALUES (@Name, @Unit, @Type, @Active, @ParentId, @LastReading) 
                            RETURNING ""MeterId""", connection);

                                insertCommand.Parameters.AddWithValue("@Name", meter.HdsMeterName);
                                insertCommand.Parameters.AddWithValue("@Unit", meter.Unit ?? "");
                                insertCommand.Parameters.AddWithValue("@Type", meter.Type?.ToLower() ?? "main");
                                insertCommand.Parameters.AddWithValue("@Active", meter.Active);

                                // Handle ParentId
                                if (!string.IsNullOrEmpty(meter.ParentMeterId) && int.TryParse(meter.ParentMeterId, out int parentId))
                                {
                                    insertCommand.Parameters.AddWithValue("@ParentId", parentId);
                                }
                                else
                                {
                                    insertCommand.Parameters.AddWithValue("@ParentId", DBNull.Value);
                                }

                                insertCommand.Parameters.AddWithValue("@LastReading", decimal.TryParse(meter.LastReading, out decimal reading) ? reading : 0);

                                var newId = insertCommand.ExecuteScalar();
                                importedCount++;
                                _logger.LogInformation($"Imported meter: {meter.HdsMeterName}, ID: {newId}");
                            }
                            else
                            {
                                // Meter exists - could update here if needed
                                _logger.LogInformation($"Meter {meter.HdsMeterName} already exists, skipping");
                            }
                        }
                        catch (Exception ex)
                        {
                            errorCount++;
                            errorMessages.Add($"{meter.HdsMeterName}: {ex.Message}");
                            _logger.LogError(ex, $"Error processing meter {meter.HdsMeterName}");
                        }
                    }

                    result.Success = errorCount == 0;
                    result.ImportedCount = importedCount;
                    result.UpdatedCount = updatedCount;
                    result.ErrorCount = errorCount;
                    result.Message = $"Processed {meters.Count} meters: {importedCount} imported, {updatedCount} updated, {errorCount} errors";

                    if (errorMessages.Any())
                    {
                        result.Message += $". Errors: {string.Join("; ", errorMessages)}";
                    }
                }
            }
            catch (Exception ex)
            {
                result.Message = $"Import failed: {ex.Message}";
                result.ErrorCount = meters.Count;
                _logger.LogError(ex, "Failed to process meter import");
            }

            return result;
        }


        [HttpPost]
        public async Task<IActionResult> ImportMeters([FromBody] ImportMetersRequest request)
        {
            try
            {
                _logger.LogInformation($"Received import request for {request?.Meters?.Count ?? 0} meters");

                if (request?.Meters == null || !request.Meters.Any())
                {
                    return Json(new { success = false, error = "No meters provided for import" });
                }

                // Validate connection if specified
                if (!string.IsNullOrEmpty(request.ConnectionId))
                {
                    var connections = _sqlServerService.GetAllConnections();
                    if (!connections.Any(c => c.ConnectionId == request.ConnectionId))
                    {
                        return Json(new { success = false, error = $"Connection '{request.ConnectionId}' not found" });
                    }
                }

                // ✅ STEP 1: Import meters first (existing logic)
                _logger.LogInformation("Step 1: Importing meters...");
                var meterImportResult = ProcessMeterImport(request.Meters, request.ConnectionId);

                // ✅ STEP 2: Import readings if requested (for ALL requested meters, not just imported ones)
                int totalReadingsImported = 0;
                int readingsErrorCount = 0;
                string readingsMessage = "";

                if (request.ImportReadings)
                {
                    // Get ALL meter names from the request (imported + skipped + updated)
                    var allRequestedMeterNames = request.Meters.Select(m => m.HdsMeterName).ToList();

                    _logger.LogInformation($"Step 2: Importing readings for {allRequestedMeterNames.Count} meters (including existing ones)...");

                    try
                    {
                        var readingsResult = await ImportMeterReadingsForMeters(
                            request.TableName,
                            allRequestedMeterNames,
                            request.ConnectionId);

                        totalReadingsImported = readingsResult.totalReadingsImported;
                        readingsErrorCount = readingsResult.errorCount;
                        readingsMessage = readingsResult.message;

                        _logger.LogInformation($"Readings import completed: {totalReadingsImported} readings imported");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error during readings import");
                        readingsMessage = $"Readings import failed: {ex.Message}";
                        readingsErrorCount = allRequestedMeterNames.Count;
                    }
                }

                // ✅ STEP 3: Return combined results
                var combinedMessage = $"Meters: {meterImportResult.ImportedCount} imported, {meterImportResult.UpdatedCount} updated, {meterImportResult.ErrorCount} errors";

                // Calculate skipped meters
                var totalMetersProcessed = request.Meters.Count;
                var skippedMeters = totalMetersProcessed - meterImportResult.ImportedCount - meterImportResult.UpdatedCount - meterImportResult.ErrorCount;
                if (skippedMeters > 0)
                {
                    combinedMessage += $", {skippedMeters} skipped (already exist)";
                }

                if (request.ImportReadings)
                {
                    combinedMessage += $". Readings: {totalReadingsImported} imported";
                    if (readingsErrorCount > 0)
                    {
                        combinedMessage += $", {readingsErrorCount} meters had reading errors";
                    }
                    combinedMessage += ".";
                }
                else
                {
                    combinedMessage += ".";
                }

                return Json(new
                {
                    success = meterImportResult.Success && readingsErrorCount == 0,
                    importedCount = meterImportResult.ImportedCount,
                    updatedCount = meterImportResult.UpdatedCount,
                    errorCount = meterImportResult.ErrorCount + readingsErrorCount,

                    // ✅ NEW: Add readings-specific results
                    readingsImported = totalReadingsImported,
                    readingsEnabled = request.ImportReadings,

                    message = combinedMessage,
                    connectionId = request.ConnectionId,

                    // Detailed breakdown
                    details = new
                    {
                        metersMessage = meterImportResult.Message,
                        readingsMessage = readingsMessage
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error importing meters");
                return Json(new { success = false, error = $"Import failed: {ex.Message}" });
            }
        }

        // ✅ NEW: Helper method to import readings for specific meters (simplified)
        private async Task<(int totalReadingsImported, int errorCount, string message)> ImportMeterReadingsForMeters(
            string tableName,
            List<string> meterNames,
            string connectionId)
        {
            if (string.IsNullOrEmpty(tableName) || !meterNames.Any())
            {
                return (0, 0, "No table name or meter names provided");
            }

            if (!_databaseService.IsInitialized || !_sqlServerService.IsInitialized)
            {
                return (0, meterNames.Count, "Database connections not initialized");
            }

            int totalReadingsImported = 0;
            int errorCount = 0;
            var errorMeters = new List<string>();

            foreach (var meterName in meterNames)
            {
                try
                {
                    _logger.LogInformation($"Importing readings for meter: {meterName}");

                    // 1. Find meter ID in PostgreSQL
                    int? meterId = null;
                    using (var pgConnection = new Npgsql.NpgsqlConnection(_databaseService.GetConnectionString()))
                    {
                        await pgConnection.OpenAsync();
                        using var cmd = new Npgsql.NpgsqlCommand(@"SELECT ""MeterId"" FROM ""Meters"" WHERE ""Name"" = @Name", pgConnection);
                        cmd.Parameters.AddWithValue("@Name", meterName);
                        var result = await cmd.ExecuteScalarAsync();
                        if (result != null)
                        {
                            meterId = Convert.ToInt32(result);
                        }
                    }

                    if (!meterId.HasValue)
                    {
                        _logger.LogWarning($"Meter {meterName} not found in PostgreSQL for readings import");
                        errorCount++;
                        errorMeters.Add(meterName);
                        continue;
                    }

                    // 2. Get ALL readings from SQL Server (simplified - no date filtering)
                    var readings = new List<(DateTime timestamp, double value, int quality)>();

                    using (var sqlConnection = _sqlServerService.GetConnection(connectionId))
                    {
                        await sqlConnection.OpenAsync();

                        // Simple query to get all readings for this meter
                        string sql = $"SELECT Chrono, Value, Quality FROM {tableName} WHERE NAME = @Name ORDER BY Chrono";

                        using var cmd = new Microsoft.Data.SqlClient.SqlCommand(sql, sqlConnection);
                        cmd.Parameters.AddWithValue("@Name", meterName);

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
                        using (var pgConnection = new Npgsql.NpgsqlConnection(_databaseService.GetConnectionString()))
                        {
                            await pgConnection.OpenAsync();
                            using var transaction = await pgConnection.BeginTransactionAsync();

                            try
                            {
                                foreach (var reading in readings)
                                {
                                    using var insertCmd = new Npgsql.NpgsqlCommand(
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

                                _logger.LogInformation($"Successfully imported {readings.Count} readings for meter {meterName}");
                            }
                            catch (Exception ex)
                            {
                                await transaction.RollbackAsync();
                                _logger.LogError(ex, $"Error inserting readings for meter {meterName}");
                                errorCount++;
                                errorMeters.Add(meterName);
                            }
                        }
                    }
                    else
                    {
                        _logger.LogInformation($"No readings found for meter {meterName}");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Error processing readings for meter {meterName}");
                    errorCount++;
                    errorMeters.Add(meterName);
                }
            }

            string message = $"Imported {totalReadingsImported} readings from {meterNames.Count - errorCount} meters";
            if (errorCount > 0)
            {
                message += $", {errorCount} meters had errors: {string.Join(", ", errorMeters)}";
            }

            return (totalReadingsImported, errorCount, message);
        }
        // Helper classes
        public class ImportMetersRequest
        {
            public string? TableName { get; set; }
            public List<HDSMeterItem> Meters { get; set; } = new List<HDSMeterItem>();
            public string? ConnectionId { get; set; }
            public bool ImportReadings { get; set; } = false;
            public bool SkipExisting { get; set; } = true;
            public bool UpdateExisting { get; set; } = false;
            public bool CreateMissingParents { get; set; } = false;
        }

        // Helper methods
        private async Task<List<object>> GetParentMeterOptions()
        {
            var options = new List<object>
    {
        new { value = "", text = "None" }
    };

            try
            {
                if (_databaseService.IsInitialized)
                {
                    using (var connection = new Npgsql.NpgsqlConnection(_databaseService.GetConnectionString()))
                    {
                        await connection.OpenAsync();

                        var command = new Npgsql.NpgsqlCommand(@"
                    SELECT ""MeterId"", ""Name"" 
                    FROM ""Meters"" 
                    WHERE ""Type"" = 'main' AND ""Active"" = true
                    ORDER BY ""Name""", connection);

                        using (var reader = await command.ExecuteReaderAsync())
                        {
                            while (await reader.ReadAsync())
                            {
                                options.Add(new
                                {
                                    value = reader.GetInt32(0).ToString(),
                                    text = reader.GetString(1)
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