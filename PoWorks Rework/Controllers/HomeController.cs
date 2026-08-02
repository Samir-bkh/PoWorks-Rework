using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PoWorks_Rework.Services;
using System.Security.Claims;

namespace PoWorks_Rework.Controllers
{
    /// <summary>
    /// Home controller providing the main dashboard and application entry points.
    /// Displays different views based on user authorization level.
    /// </summary>
    [Authorize]
    public class HomeController : BaseController
    {
        private readonly ILogger<HomeController> _logger;

        /// <summary>
        /// Initializes the home controller with database and logging services.
        /// </summary>
        public HomeController(DatabaseService databaseService, ILogger<HomeController> logger)
            : base(databaseService)
        {
            _logger = logger;
        }

        /// <summary>
        /// Displays the home/dashboard page.
        /// Shows the current tenant context for the user or "Global Admin View" if none assigned.
        /// </summary>
        public IActionResult Index()
        {
            var currentTenant = User.FindFirstValue("TenantId");
            ViewData["CurrentTenant"] = string.IsNullOrEmpty(currentTenant)
                ? "Global Admin View"
                : currentTenant;

            return View();
        }

        /// <summary>
        /// Displays the application privacy policy page.
        /// </summary>
        public IActionResult Privacy()
        {
            return View();
        }
    }
}