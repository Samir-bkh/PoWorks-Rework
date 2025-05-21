using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
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

                using (var connection = _databaseService.GetConnection())
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

                                using (var checkCommand = new Npgsql.NpgsqlCommand(
                                    @"SELECT ""MeterId"" FROM ""Meters"" WHERE ""Name"" = @Name", connection, transaction))
                                {
                                    checkCommand.Parameters.AddWithValue("@Name", meter.HdsMeterName);
                                    var result = await checkCommand.ExecuteScalarAsync();
                                    meterExists = result != null;
                                    if (meterExists)
                                        existingMeterId = Convert.ToInt32(result);
                                }

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
                                        using (var parentCheckCommand = new Npgsql.NpgsqlCommand(
                                            @"SELECT COUNT(*) FROM ""Meters"" WHERE ""MeterId"" = @MeterId", connection, transaction))
                                        {
                                            parentCheckCommand.Parameters.AddWithValue("@MeterId", parsedParentId);
                                            int parentCount = Convert.ToInt32(await parentCheckCommand.ExecuteScalarAsync());

                                            if (parentCount > 0)
                                            {
                                                parentId = parsedParentId;
                                            }
                                            else if (request.CreateMissingParents)
                                            {
                                                // Create a missing parent if requested
                                                // This would be implemented in a real app, for now just log
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

                                // Insert or update the meter
                                if (meterExists && request.UpdateExisting)
                                {
                                    // Update existing meter
                                    using (var updateCommand = new Npgsql.NpgsqlCommand(
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

                                        await updateCommand.ExecuteNonQueryAsync();
                                        updatedCount++;
                                        _logger.LogInformation($"Updated meter: {meter.HdsMeterName}");
                                    }
                                }
                                else if (!meterExists)
                                {
                                    // Insert new meter
                                    using (var insertCommand = new Npgsql.NpgsqlCommand(
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