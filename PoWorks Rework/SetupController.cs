using Microsoft.AspNetCore.Mvc;
using Npgsql;
using PoWorks_Rework.Services;

namespace PoWorks_Rework.Controllers
{
    public class SetupController : Controller
    {
        private readonly DatabaseService _databaseService;

        public SetupController(DatabaseService databaseService)
        {
            _databaseService = databaseService;
        }

        [HttpGet]
        public IActionResult Index()
        {
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> CompleteSetup(string adminUsername, string adminPassword)
        {
            if (string.IsNullOrEmpty(adminUsername) || string.IsNullOrEmpty(adminPassword))
            {
                ModelState.AddModelError("", "Tous les champs sont obligatoires.");
                return View("Index");
            }

            using var connection = _databaseService.CreateNewConnection();
            await connection.OpenAsync();

            using (var cmd = new NpgsqlCommand(
                @"INSERT INTO ""Users"" (""Username"", ""PasswordHash"", ""Role"", ""CompanyId"", ""IsActive"", ""CreatedAt"") 
                  VALUES (@username, @password, 'Admin', 1, TRUE, NOW())
                  ON CONFLICT (""Username"") DO UPDATE SET ""PasswordHash"" = EXCLUDED.""PasswordHash""", connection))
            {
                cmd.Parameters.AddWithValue("username", adminUsername);
                cmd.Parameters.AddWithValue("password", adminPassword);
                await cmd.ExecuteNonQueryAsync();
            }

            return RedirectToAction("Index", "Home");
        }
    }
}