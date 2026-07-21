using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Npgsql;
using PoWorks_Rework.Models;
using PoWorks_Rework.Services;

namespace PoWorks_Rework.Controllers
{
    public class VarexpImportController : Controller
    {
        #region Dependencies

        private readonly ILogger<VarexpImportController> _logger;
        private readonly DatabaseService _databaseService;
        private readonly VarexpParserService _varexpParserService;

        public VarexpImportController(
            ILogger<VarexpImportController> logger,
            DatabaseService databaseService,
            VarexpParserService varexpParserService)
        {
            _logger = logger;
            _databaseService = databaseService;
            _varexpParserService = varexpParserService;
        }

        #endregion

        #region VAREXP Parse & Import Methods
        [HttpPost]
        [ValidateAntiForgeryToken]
        [IgnoreAntiforgeryToken]
        public async Task<IActionResult> ParseVarexp(IFormFile VarexpFile)
        {
            if (VarexpFile == null || VarexpFile.Length == 0)
                return BadRequest("No VAREXP.DAT file was uploaded.");

            try
            {
                var records = await _varexpParserService.ParseVarexpAsync(VarexpFile);
                _logger.LogInformation("🔍 DEBUG: About to call GetParentMeterOptions()");
                var parentOptions = await GetParentMeterOptions();
                _logger.LogInformation("🔍 DEBUG: GetParentMeterOptions() returned {Count} options", parentOptions?.Count ?? 0);

                var response = new
                {
                    success = true,
                    records,
                    parentOptions
                };

                _logger.LogInformation("🔍 DEBUG: Returning response with {RecordCount} records and {ParentCount} parent options",
                    records?.Count ?? 0, parentOptions?.Count ?? 0);

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
                using var connection = _databaseService.GetConnection();
                foreach (var meter in request.Meters)
                {
                    try
                    {
                        _logger.LogInformation($"Processing VAREXP meter: {meter.MeterName}");
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
                    importedCount,
                    updatedCount,
                    skippedCount,
                    errorCount,
                    totalProcessed,
                    detailedErrors,
                    message
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

        #region Helper Methods
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
                        var command = new NpgsqlCommand(@"
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
            }

            return options;
        }
        private async Task<dynamic> GetExistingMeterByNameAsync(string meterName, NpgsqlConnection connection)
        {
            var command = new NpgsqlCommand(@"
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
        private async Task CreateNewVarexpMeterAsync(VarexpMeterImportItem meter, bool createMissingParents, NpgsqlConnection connection)
        {
            int? parentId = null;
            if (!string.IsNullOrEmpty(meter.ParentMeterId))
            {
                if (int.TryParse(meter.ParentMeterId, out var parentIdValue))
                {
                    var parentExists = await CheckMeterExistsAsync(parentIdValue, connection);
                    if (parentExists)
                    {
                        parentId = parentIdValue;
                    }
                    else if (createMissingParents)
                    {
                        _logger.LogWarning($"Parent meter ID {parentIdValue} not found for meter {meter.MeterName}. Creating without parent.");
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

            var command = new NpgsqlCommand(@"
        INSERT INTO ""Meters"" (""Name"", ""Type"", ""Unit"", ""ParentId"", ""Active"", ""LastReading"", ""TenantID"")
        VALUES (@name, @type, @unit, @parentId, @active, @lastReading, @tenantId)
        RETURNING ""MeterId""", connection);

            command.Parameters.AddWithValue("@name", meter.MeterName);
            command.Parameters.AddWithValue("@type", meter.Type?.ToLower() ?? "main"); 
            command.Parameters.AddWithValue("@unit", meter.Unit ?? ""); 
            command.Parameters.AddWithValue("@parentId", (object)parentId ?? DBNull.Value);
            command.Parameters.AddWithValue("@active", meter.Active);
            command.Parameters.AddWithValue("@lastReading", 0); 
            command.Parameters.AddWithValue("@tenantId", DBNull.Value); 

            var newMeterId = await command.ExecuteScalarAsync();
            _logger.LogInformation($"Created meter {meter.MeterName} with ID {newMeterId}");
        }
        private async Task UpdateExistingVarexpMeterAsync(int meterId, VarexpMeterImportItem meter, NpgsqlConnection connection)
        {
            int? parentId = null;
            if (!string.IsNullOrEmpty(meter.ParentMeterId) && int.TryParse(meter.ParentMeterId, out var parentIdValue))
            {
                var parentExists = await CheckMeterExistsAsync(parentIdValue, connection);
                if (parentExists)
                {
                    parentId = parentIdValue;
                }
            }

            var command = new NpgsqlCommand(@"
        UPDATE ""Meters"" 
        SET ""Type"" = @type, ""Unit"" = @unit, ""ParentId"" = @parentId, ""Active"" = @active
        WHERE ""MeterId"" = @meterId", connection);

            command.Parameters.AddWithValue("@meterId", meterId);
            command.Parameters.AddWithValue("@type", meter.Type?.ToLower() ?? "main"); 
            command.Parameters.AddWithValue("@unit", meter.Unit ?? ""); 
            command.Parameters.AddWithValue("@parentId", (object)parentId ?? DBNull.Value);
            command.Parameters.AddWithValue("@active", meter.Active);

            await command.ExecuteNonQueryAsync();
            _logger.LogInformation($"Updated meter {meter.MeterName} with ID {meterId}");
        }
        private async Task<bool> CheckMeterExistsAsync(int meterId, NpgsqlConnection connection)
        {
            var command = new NpgsqlCommand(@"
        SELECT COUNT(*) FROM ""Meters"" WHERE ""MeterId"" = @meterId", connection);

            command.Parameters.AddWithValue("@meterId", meterId);

            var count = (long)await command.ExecuteScalarAsync();
            return count > 0;
        }

        #endregion
    }
}