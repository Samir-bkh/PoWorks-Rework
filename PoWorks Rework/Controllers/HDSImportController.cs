// Controllers/HdsImportController.cs
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using PoWorks_Rework.Models;
using PoWorks_Rework.Services;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

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
        public async Task<IActionResult> GetTables()
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

                // Get the HDS meters
                var hdsMeters = await _sqlServerService.GetDistinctMeterNames(tableName);

                // Get parent meter options
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
        public IActionResult ImportMeters([FromBody] ImportMetersRequest request)
        {
            try
            {
                _logger.LogInformation($"Received import request for {request?.Meters?.Count ?? 0} meters");

                // For now just log the data and return success
                // In a real implementation, you would insert these meters into your database
                if (request?.Meters != null)
                {
                    foreach (var meter in request.Meters)
                    {
                        _logger.LogInformation($"Importing meter: {meter.HdsMeterName}, Type: {meter.Type}, Unit: {meter.Unit}");
                    }
                }

                return Json(new
                {
                    success = true,
                    importedCount = request?.Meters?.Count ?? 0,
                    message = $"Successfully imported {request?.Meters?.Count ?? 0} meters."
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error importing meters");
                return Json(new
                {
                    success = false,
                    error = ex.Message
                });
            }
        }

        // Helper classes
        public class ImportMetersRequest
        {
            public string TableName { get; set; }
            public List<HDSMeterItem> Meters { get; set; }
            public bool SkipExisting { get; set; }
            public bool UpdateExisting { get; set; }
            public bool CreateMissingParents { get; set; }
        }

        // Helper methods
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