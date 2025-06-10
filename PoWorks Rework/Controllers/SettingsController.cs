// Controllers/SettingsController.cs
using Microsoft.AspNetCore.Mvc;
using PoWorks_Rework.Models;
using Microsoft.Extensions.Configuration;
using System;
using System.IO;
using System.Text.Json;
using Npgsql;
using PoWorks_Rework.Services;
using System.Collections.Generic;
using Microsoft.Data.SqlClient; // Add this for SQL Server connection

namespace PoWorks_Rework.Controllers
{
    public class SettingsController : Controller
    {
        private readonly IConfiguration _configuration;
        private readonly IWebHostEnvironment _webHostEnvironment;
        private readonly DatabaseService _databaseService;
        private readonly SqlServerService _sqlServerService;

        public SettingsController(
            IConfiguration configuration,
            IWebHostEnvironment webHostEnvironment,
            DatabaseService databaseService,
            SqlServerService sqlServerService)
        {
            _configuration = configuration;
            _webHostEnvironment = webHostEnvironment;
            _databaseService = databaseService;
            _sqlServerService = sqlServerService;
        }

        public IActionResult General()
        {
            // Use the current settings from the service if initialized,
            // otherwise load from configuration
            var pgSettings = _databaseService.IsInitialized
                ? _databaseService.CurrentSettings
                : new DatabaseSettings
                {
                    Host = _configuration["DatabaseSettings:Host"] ?? "localhost",
                    Port = _configuration["DatabaseSettings:Port"] ?? "5432",
                    Database = _configuration["DatabaseSettings:Database"] ?? "",
                    Username = _configuration["DatabaseSettings:Username"] ?? "postgres",
                    Password = _configuration["DatabaseSettings:Password"] ?? "",
                    SSLMode = _configuration["DatabaseSettings:SSLMode"] ?? "Prefer"
                };

            // Load SQL Server settings from the service
            var sqlConnections = LoadSqlServerConnections();

            var viewModel = new GeneralSettingsViewModel
            {
                PostgreSql = pgSettings,
                SqlServerConnections = sqlConnections
            };

            return View(viewModel);
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

                            // Save settings and initialize the service
                            UpdateAppSettings(settings);
                            _databaseService.Initialize(settings);

                            return Json(new { success = true, message = "Connected successfully and created tables." });
                        }

                        // Save settings and initialize the service
                        UpdateAppSettings(settings);
                        _databaseService.Initialize(settings);

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

                            // Save settings and initialize the service
                            UpdateAppSettings(settings);
                            _databaseService.Initialize(settings);

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
        public IActionResult SaveGeneralSettings(GeneralSettingsViewModel model)
        {
            if (ModelState.IsValid)
            {
                try
                {
                    // Test PostgreSQL connection before saving
                    using (var connection = new NpgsqlConnection(model.PostgreSql.ToConnectionString()))
                    {
                        connection.Open();
                    }

                    // Save PostgreSQL settings to appsettings.json 
                    UpdateAppSettings(model.PostgreSql);

                    // Initialize the database service with new settings
                    _databaseService.Initialize(model.PostgreSql);

                    // If SQL Server settings are provided, save them too
                    if (!string.IsNullOrEmpty(model.SqlServer.Host) && !string.IsNullOrEmpty(model.SqlServer.Database))
                    {
                        // Try to test connection
                        try
                        {
                            using (var connection = new SqlConnection(model.SqlServer.ToConnectionString()))
                            {
                                connection.Open();
                            }

                            // Connection successful, save SQL Server settings
                            UpdateSqlServerSettings(model.SqlServer);
                            TempData["SuccessMessage"] = "Database settings saved successfully.";
                        }
                        catch (Exception ex)
                        {
                            // Don't fail the entire operation if SQL Server connection fails
                            TempData["WarningMessage"] = $"PostgreSQL settings saved, but SQL Server connection failed: {ex.Message}";
                        }
                    }
                    else
                    {
                        TempData["SuccessMessage"] = "PostgreSQL database settings saved successfully.";
                    }

                    return RedirectToAction("General");
                }
                catch (Exception ex)
                {
                    ModelState.AddModelError("", $"Failed to connect to database: {ex.Message}");
                }
            }
            UpdateSqlServerSettings(model.SqlServer);
            _sqlServerService.Initialize(model.SqlServer);
            return View("General", model);
        }

        [HttpPost]
        public IActionResult TestSqlServerConnection([FromBody] SqlServerSettings settings)
        {
            try
            {
                // If database name is not specified but project name is, generate the database name
                if (string.IsNullOrEmpty(settings.Database) && !string.IsNullOrEmpty(settings.ProjectName))
                {
                    settings.Database = $"{settings.ProjectName}_DB";
                }

                using (var connection = new SqlConnection(settings.ToConnectionString()))
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

        // Other methods remain the same...
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

        // Helper method to update appsettings.json for PostgreSQL
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

        private List<SqlServerSettings> LoadSqlServerConnections()
        {
            var connections = new List<SqlServerSettings>();

            // Try to load from the new multiple connections format first
            var connectionsSection = _configuration.GetSection("SqlServerConnections");
            if (connectionsSection.Exists() && connectionsSection.GetChildren().Any())
            {
                foreach (var connectionSection in connectionsSection.GetChildren())
                {
                    var connection = new SqlServerSettings
                    {
                        ConnectionId = connectionSection["ConnectionId"] ?? Guid.NewGuid().ToString(),
                        ConnectionName = connectionSection["ConnectionName"] ?? "",
                        Host = connectionSection["Host"] ?? "localhost",
                        Port = connectionSection["Port"] ?? "1433",
                        Database = connectionSection["Database"] ?? "",
                        Username = connectionSection["Username"] ?? "",
                        Password = connectionSection["Password"] ?? "",
                        ProjectName = connectionSection["ProjectName"] ?? "",
                        IsDefault = bool.Parse(connectionSection["IsDefault"] ?? "false")
                    };
                    connections.Add(connection);
                }
            }
            else
            {
                // Fallback to old single connection format for backward compatibility
                var legacyConnection = new SqlServerSettings
                {
                    ConnectionId = "legacy",
                    ConnectionName = "Legacy Connection",
                    Host = _configuration["SqlServerSettings:Host"] ?? "localhost",
                    Port = _configuration["SqlServerSettings:Port"] ?? "1433",
                    Database = _configuration["SqlServerSettings:Database"] ?? "",
                    Username = _configuration["SqlServerSettings:Username"] ?? "",
                    Password = _configuration["SqlServerSettings:Password"] ?? "",
                    ProjectName = _configuration["SqlServerSettings:ProjectName"] ?? "",
                    IsDefault = true
                };

                if (!string.IsNullOrEmpty(legacyConnection.Host) && !string.IsNullOrEmpty(legacyConnection.Database))
                {
                    connections.Add(legacyConnection);
                }
            }

            // If no connections exist, create a default empty one
            if (!connections.Any())
            {
                connections.Add(new SqlServerSettings
                {
                    ConnectionId = Guid.NewGuid().ToString(),
                    ConnectionName = "Default Connection",
                    Host = "localhost",
                    Port = "1433",
                    IsDefault = true
                });
            }

            return connections;
        }

        [HttpPost]
        public IActionResult DeleteSqlServerConnection([FromBody] DeleteConnectionRequest request)
        {
            try
            {
                var connections = LoadSqlServerConnections();

                // Check if we're trying to delete the last connection
                if (connections.Count <= 1)
                {
                    return Json(new { success = false, error = "Cannot delete the last connection. At least one connection is required." });
                }

                // Find the connection to delete
                var connectionToDelete = connections.FirstOrDefault(c => c.ConnectionId == request.ConnectionId);
                if (connectionToDelete == null)
                {
                    return Json(new { success = false, error = "Connection not found." });
                }

                // Remove the connection
                connections.Remove(connectionToDelete);

                // If we deleted the default connection, set a new default
                if (connectionToDelete.IsDefault && connections.Any())
                {
                    connections.First().IsDefault = true;
                }

                // Save updated connections to appsettings.json
                UpdateSqlServerConnections(connections);

                // Update the service with new connections
                _sqlServerService.InitializeMultiple(connections);

                return Json(new
                {
                    success = true,
                    message = "Connection deleted successfully!",
                    newDefaultConnectionId = connections.FirstOrDefault(c => c.IsDefault)?.ConnectionId
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, error = ex.Message });
            }
        }

        [HttpPost]
        public IActionResult SaveSqlServerConnections([FromBody] SaveConnectionsRequest request)
        {
            try
            {
                var connections = new List<SqlServerSettings>();

                foreach (var connData in request.Connections)
                {
                    var connection = new SqlServerSettings
                    {
                        ConnectionId = connData.ContainsKey("ConnectionId") ? connData["ConnectionId"] : Guid.NewGuid().ToString(),
                        ConnectionName = connData.ContainsKey("ConnectionName") ? connData["ConnectionName"] : "",
                        Host = connData.ContainsKey("Host") ? connData["Host"] : "localhost",
                        Port = connData.ContainsKey("Port") ? connData["Port"] : "1433",
                        Database = connData.ContainsKey("Database") ? connData["Database"] : "",
                        Username = connData.ContainsKey("Username") ? connData["Username"] : "",
                        Password = connData.ContainsKey("Password") ? connData["Password"] : "",
                        ProjectName = connData.ContainsKey("ProjectName") ? connData["ProjectName"] : "",
                        IsDefault = connData.ContainsKey("IsDefault") && connData["IsDefault"].ToLower() == "true"
                    };

                    connections.Add(connection);
                }

                // Save to configuration
                UpdateSqlServerConnections(connections);

                return Json(new { success = true, message = "All SQL Server connections saved successfully!" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, error = ex.Message });
            }
        }

        private void UpdateSqlServerConnections(List<SqlServerSettings> connections)
        {
            var appSettingsPath = Path.Combine(Directory.GetCurrentDirectory(), "appsettings.json");
            var json = System.IO.File.ReadAllText(appSettingsPath);
            var jsonSettings = JsonDocument.Parse(json);

            var updatedSettings = new Dictionary<string, object>();

            foreach (var element in jsonSettings.RootElement.EnumerateObject())
            {
                updatedSettings[element.Name] = JsonSerializer.Deserialize<object>(element.Value.GetRawText());
            }

            // Remove old single connection format
            if (updatedSettings.ContainsKey("SqlServerSettings"))
            {
                updatedSettings.Remove("SqlServerSettings");
            }

            // Add new multiple connections format
            var connectionsList = connections.Select(c => new Dictionary<string, object>
    {
        { "ConnectionId", c.ConnectionId },
        { "ConnectionName", c.ConnectionName },
        { "Host", c.Host },
        { "Port", c.Port },
        { "Database", c.Database },
        { "Username", c.Username },
        { "Password", c.Password },
        { "ProjectName", c.ProjectName },
        { "IsDefault", c.IsDefault }
    }).ToList();

            updatedSettings["SqlServerConnections"] = connectionsList;

            var options = new JsonSerializerOptions { WriteIndented = true };
            var updatedJson = JsonSerializer.Serialize(updatedSettings, options);
            System.IO.File.WriteAllText(appSettingsPath, updatedJson);
        }

        // Helper method to update appsettings.json for SQL Server
        private void UpdateSqlServerSettings(SqlServerSettings settings)
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

            // Update SQL Server settings
            var sqlSettings = new Dictionary<string, string>
            {
                { "Host", settings.Host },
                { "Port", settings.Port },
                { "Database", settings.Database },
                { "Username", settings.Username },
                { "Password", settings.Password },
                { "ProjectName", settings.ProjectName }
            };

            // Add or update the SqlServerSettings section
            updatedSettings["SqlServerSettings"] = sqlSettings;

            // Save back to file
            var options = new JsonSerializerOptions { WriteIndented = true };
            var updatedJson = JsonSerializer.Serialize(updatedSettings, options);
            System.IO.File.WriteAllText(appSettingsPath, updatedJson);
        }
    }


}

public class SaveConnectionsRequest
{
    public List<Dictionary<string, string>> Connections { get; set; } = new List<Dictionary<string, string>>();
}

public class DeleteConnectionRequest
{
    public string ConnectionId { get; set; } = "";
}