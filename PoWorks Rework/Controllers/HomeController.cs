using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using PoWorks_Rework.Models;
using PoWorks_Rework.Services;

namespace PoWorks_Rework.Controllers
{
    public class HomeController : BaseController
    {
        private readonly ILogger<HomeController> _logger;

        public HomeController(DatabaseService databaseService, ILogger<HomeController> logger)
            : base(databaseService)
        {
            _logger = logger;
        }

        public IActionResult Index()
        {
            // Check if database is initialized
            if (!_databaseService.IsInitialized)
            {
                TempData["WarningMessage"] = "Database not configured. Dashboard functionality will be limited.";
            }

            return View();
        }

        public IActionResult Privacy()
        {
            return View();
        }
    }
}