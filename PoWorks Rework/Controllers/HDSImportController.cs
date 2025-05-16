// Controllers/HdsImportController.cs
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Data.SqlClient;
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
            if (!_sqlServerService.IsInitialized)
            {
                return BadRequest("SQL Server connection is not configured. Please set up the SQL Server connection in General Settings.");
            }

            try
            {
                // This method would be implemented in SqlServerService to get all available tables
                var tables = await _sqlServerService.GetAvailableTables();
                return Json(tables);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting tables from PCVue HDS");
                return StatusCode(500, $"Error: {ex.Message}");
            }
        }

        // In HdsImportController.cs, update the GetMetersFromTable method:

        [HttpGet]
        public async Task<IActionResult> GetMetersFromTable(string tableName)
        {
            if (!_sqlServerService.IsInitialized)
            {
                return BadRequest("SQL Server connection is not configured.");
            }

            try
            {
                // Validate the table name to prevent SQL injection
                if (string.IsNullOrWhiteSpace(tableName) || !IsValidTableName(tableName))
                {
                    return BadRequest("Invalid table name provided.");
                }

                // Get distinct meter names from the SQL Server table
                var hdsMeters = await GetDistinctMeterNames(tableName);

                // Get parent meter options from PostgreSQL database
                var parentOptions = await GetParentMeterOptions();

                // Create view model
                var viewModel = new HDSMeterSelectionViewModel
                {
                    HdsMeters = hdsMeters,
                    ParentMeterOptions = parentOptions,
                    TableName = tableName
                };

                // Change this line to return the view from the Shared folder
                return PartialView("~/Views/Shared/_HDSMeterSelectionModal.cshtml", viewModel);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error getting meters from HDS table {tableName}");
                return StatusCode(500, $"Error: {ex.Message}");
            }
        }

        [HttpPost]
        public async Task<IActionResult> ImportMeters([FromBody] ImportMetersRequest request)
        {
            if (!_databaseService.IsInitialized)
            {
                return BadRequest("PostgreSQL database is not configured.");
            }

            if (!_sqlServerService.IsInitialized)
            {
                return BadRequest("SQL Server connection is not configured.");
            }

            try
            {
                var response = new ImportMetersResponse
                {
                    Success = true,
                    ImportedCount = 0,
                    ErrorCount = 0
                };

                // For demonstration, we'll create a simulated response
                // In a real implementation, you would:
                // 1. Connect to SQL Server to get meter data
                // 2. Connect to PostgreSQL to import meters
                // 3. Track progress and handle errors

                foreach (var meter in request.Meters)
                {
                    try
                    {
                        // In a real implementation, you would insert the meter into PostgreSQL
                        // For now, we'll just simulate success
                        response.ImportedCount++;
                        response.ImportedMeters.Add(meter.HdsMeterName);

                        // Simulate occasional errors for demo purposes
                        if (new Random().Next(10) == 0)
                        {
                            throw new Exception("Random error for demonstration");
                        }
                    }
                    catch (Exception ex)
                    {
                        response.ErrorCount++;
                        response.ErrorMeters.Add(meter.HdsMeterName);
                        _logger.LogError(ex, $"Error importing meter {meter.HdsMeterName}");
                    }
                }

                return Json(response);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during meter import");
                return StatusCode(500, new ImportMetersResponse
                {
                    Success = false,
                    ErrorMessage = $"Import failed: {ex.Message}"
                });
            }
        }

        #region Helper Methods

        private bool IsValidTableName(string tableName)
        {
            // Simple validation to prevent SQL injection
            // Only allow alphanumeric characters, underscores, and optional square brackets
            return System.Text.RegularExpressions.Regex.IsMatch(
                tableName, @"^(\[?[a-zA-Z0-9_]+\]?)|(\[?[a-zA-Z0-9_]+\]?\.\[?[a-zA-Z0-9_]+\]?)$");
        }

        private async Task<List<HDSMeterItem>> GetDistinctMeterNames(string tableName)
        {
            var meters = new List<HDSMeterItem>();

            try
            {
                using (var connection = _sqlServerService.GetConnection())
                {
                    await connection.OpenAsync();

                    // Assume HDS tables have a "NAME" column and a "VAL" column
                    // This query should be adjusted based on the actual structure of your HDS tables
                    string sql = $@"
                        SELECT DISTINCT NAME 
                        FROM {tableName}
                        WHERE NAME IS NOT NULL
                        ORDER BY NAME";

                    using (var command = new SqlCommand(sql, connection))
                    {
                        using (var reader = await command.ExecuteReaderAsync())
                        {
                            while (await reader.ReadAsync())
                            {
                                meters.Add(new HDSMeterItem
                                {
                                    HdsMeterName = reader.GetString(0),
                                    Type = "Main", // Default to Main
                                    Active = true,
                                    IsSelected = true
                                });
                            }
                        }
                    }
                }

                // For demonstration purposes, if we have no actual data, create some sample meters
                if (meters.Count == 0)
                {
                    // Generate 10 sample meters
                    for (int i = 1; i <= 10; i++)
                    {
                        meters.Add(new HDSMeterItem
                        {
                            HdsMeterName = $"Sample_Meter_{i}",
                            Unit = i % 2 == 0 ? "kW" : "kWh",
                            Type = "Main",
                            Active = true,
                            IsSelected = true
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error getting distinct meter names from {tableName}");
                throw;
            }

            return meters;
        }

        private async Task<List<SelectListItem>> GetParentMeterOptions()
        {
            var options = new List<SelectListItem>
            {
                new SelectListItem { Value = "", Text = "None" }
            };

            try
            {
                using (var connection = new Npgsql.NpgsqlConnection(_databaseService.GetConnectionString()))
                {
                    await connection.OpenAsync();

                    string sql = @"
                        SELECT ""MeterId"", ""Name"" 
                        FROM ""Meters"" 
                        WHERE ""Type"" = 'main' AND ""Active"" = true
                        ORDER BY ""Name""";

                    using (var command = new Npgsql.NpgsqlCommand(sql, connection))
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

                // For demonstration purposes, if we have no parent meters, create some sample options
                if (options.Count <= 1)
                {
                    options.Add(new SelectListItem { Value = "1", Text = "Main Meter 1" });
                    options.Add(new SelectListItem { Value = "2", Text = "Main Meter 2" });
                    options.Add(new SelectListItem { Value = "3", Text = "Main Meter 3" });
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