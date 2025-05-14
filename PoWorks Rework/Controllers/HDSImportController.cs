// Controllers/HdsImportController.cs
using Microsoft.AspNetCore.Mvc;
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

        public HdsImportController(
            ILogger<HdsImportController> logger,
            SqlServerService sqlServerService)
        {
            _logger = logger;
            _sqlServerService = sqlServerService;
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

        // Additional methods will be added for importing data from PCVue HDS
    }
}