using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PoWorks_Rework.Services;
using System.Security.Claims;

namespace PoWorks_Rework.Controllers
{
    [Authorize]
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
            var currentTenant = User.FindFirstValue("TenantId");
            ViewData["CurrentTenant"] = string.IsNullOrEmpty(currentTenant)
                ? "Global Admin View"
                : currentTenant;

            return View();
        }

        public IActionResult Privacy()
        {
            return View();
        }
    }
}