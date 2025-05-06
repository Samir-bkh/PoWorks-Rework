using Microsoft.AspNetCore.Mvc;
using PoWorks_Rework.Models;
using Microsoft.Extensions.Configuration;
using System;
using System.IO;
using System.Text.Json;
using Npgsql;

namespace PoWorks_Rework.Controllers
{
    public class SettingsController : Controller
    {
        private readonly IConfiguration _configuration;
        private readonly IWebHostEnvironment _webHostEnvironment;

        public SettingsController(IConfiguration configuration, IWebHostEnvironment webHostEnvironment)
        {
            _configuration = configuration;
            _webHostEnvironment = webHostEnvironment;
        }

        public IActionResult General()
        {
            // Load existing settings from configuration or use defaults
            var settings = new DatabaseSettings
            {
                Host = _configuration["DatabaseSettings:Host"] ?? "localhost",
                Port = _configuration["DatabaseSettings:Port"] ?? "5432",
                Database = _configuration["DatabaseSettings:Database"] ?? "",
                Username = _configuration["DatabaseSettings:Username"] ?? "postgres",
                Password = _configuration["DatabaseSettings:Password"] ?? "",
                SSLMode = _configuration["DatabaseSettings:SSLMode"] ?? "Prefer"
            };

            return View(settings);
        }

        [HttpPost]
        public IActionResult Connect([FromBody] DatabaseSettings settings)
        {
            try
            {
                // First try connecting to the specified database
                try
                {
                    using (var connection = new NpgsqlConnection(settings.ToConnectionString()))
                    {
                        connection.Open();
                        // If we got here, the database exists, check if tables exist
                        if (!TablesExist(connection))
                        {
                            // Create tables in existing database
                            ExecuteSchemaScript(connection);
                            return Json(new { success = true, message = "Connected successfully and created tables." });
                        }
                        return Json(new { success = true, message = "Connected successfully to existing database." });
                    }
                }
                catch (Npgsql.PostgresException ex) when (ex.SqlState == "3D000") // Database does not exist
                {
                    // Connect to default postgres database instead
                    var connectionStringBuilder = new NpgsqlConnectionStringBuilder(settings.ToConnectionString())
                    {
                        Database = "postgres"
                    };

                    using (var connection = new NpgsqlConnection(connectionStringBuilder.ConnectionString))
                    {
                        connection.Open();

                        // Try to create the database
                        using (var cmd = new NpgsqlCommand($"CREATE DATABASE \"{settings.Database}\"", connection))
                        {
                            cmd.ExecuteNonQuery();
                        }

                        // Now connect to the new database
                        using (var newConnection = new NpgsqlConnection(settings.ToConnectionString()))
                        {
                            newConnection.Open();

                            // Create initial schema
                            ExecuteSchemaScript(newConnection);

                            return Json(new { success = true, message = "Database created successfully with the required tables!" });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                return Json(new { success = false, errorMessage = ex.Message });
            }
        }

        [HttpPost]
        public IActionResult TestConnection([FromBody] DatabaseSettings settings)
        {
            try
            {
                using (var connection = new NpgsqlConnection(settings.ToConnectionString()))
                {
                    connection.Open();
                    // If we get here, connection was successful
                    return Json(new { success = true });
                }
            }
            catch (Exception ex)
            {
                return Json(new { success = false, errorMessage = ex.Message });
            }
        }

        [HttpPost]
        public IActionResult SaveGeneralSettings(DatabaseSettings settings)
        {
            if (ModelState.IsValid)
            {
                try
                {
                    // Test connection before saving
                    using (var connection = new NpgsqlConnection(settings.ToConnectionString()))
                    {
                        connection.Open();
                    }

                    // Save settings to appsettings.json or your preferred configuration store
                    UpdateAppSettings(settings);

                    TempData["SuccessMessage"] = "Database settings saved successfully.";
                    return RedirectToAction("General");
                }
                catch (Exception ex)
                {
                    ModelState.AddModelError("", $"Failed to connect to database: {ex.Message}");
                }
            }
            return View("General", settings);
        }

        private bool TablesExist(NpgsqlConnection connection)
        {
            using (var cmd = new NpgsqlCommand(
                "SELECT EXISTS (SELECT FROM information_schema.tables WHERE table_name = 'tenants')", connection))
            {
                return (bool)cmd.ExecuteScalar();
            }
        }

        private void ExecuteSchemaScript(NpgsqlConnection connection)
        {
            // Load SQL from file
            string sqlFilePath = Path.Combine(_webHostEnvironment.WebRootPath, "sql", "initial_schema.sql");
            string sql = System.IO.File.ReadAllText(sqlFilePath);

            // Execute SQL script
            using (var cmd = new NpgsqlCommand(sql, connection))
            {
                cmd.ExecuteNonQuery();
            }
        }

        // Helper method to update appsettings.json
        private void UpdateAppSettings(DatabaseSettings settings)
        {
            var appSettingsPath = Path.Combine(Directory.GetCurrentDirectory(), "appsettings.json");
            var json = System.IO.File.ReadAllText(appSettingsPath);
            var jsonSettings = JsonDocument.Parse(json);

            // Create a new dictionary with updated values
            var updatedSettings = new Dictionary<string, object>();

            // Copy existing settings
            foreach (var element in jsonSettings.RootElement.EnumerateObject())
            {
                updatedSettings[element.Name] = JsonSerializer.Deserialize<object>(element.Value.GetRawText());
            }

            // Update database settings
            var dbSettings = new Dictionary<string, string>
            {
                { "Host", settings.Host },
                { "Port", settings.Port },
                { "Database", settings.Database },
                { "Username", settings.Username },
                { "Password", settings.Password },
                { "SSLMode", settings.SSLMode }
            };

            // Add or update the DatabaseSettings section
            updatedSettings["DatabaseSettings"] = dbSettings;

            // Save back to file
            var options = new JsonSerializerOptions { WriteIndented = true };
            var updatedJson = JsonSerializer.Serialize(updatedSettings, options);
            System.IO.File.WriteAllText(appSettingsPath, updatedJson);
        }
    }
}