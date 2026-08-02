using Microsoft.AspNetCore.Mvc;
using PoWorks_Rework.Models;
using System.Text.Json;
using Npgsql;
using PoWorks_Rework.Services;
using Microsoft.Data.SqlClient;
using System.Text;

namespace PoWorks_Rework.Controllers
{
    /// <summary>
    /// Controller for application settings and configuration management.
    /// Handles database connection settings, SQL Server connections, web service configuration, and application settings.
    /// </summary>
    public class SettingsController : Controller
    {
        private readonly IConfiguration _configuration;
        private readonly IWebHostEnvironment _webHostEnvironment;
        private readonly DatabaseService _databaseService;
        private readonly SqlServerService _sqlServerService;
        private readonly EncryptionService _encryptionService;

        /// <summary>
        /// Initializes the settings controller with configuration and service dependencies.
        /// </summary>
        public SettingsController(
            IConfiguration configuration,
            IWebHostEnvironment webHostEnvironment,
            DatabaseService databaseService,
            SqlServerService sqlServerService,
            EncryptionService encryptionService)
        {
            _configuration = configuration;
            _webHostEnvironment = webHostEnvironment;
            _databaseService = databaseService;
            _sqlServerService = sqlServerService;
            _encryptionService = encryptionService;
        }

        /// <summary>
        /// Displays the general settings page with database and connection configuration options.
        /// </summary>
        public async Task<IActionResult> General()
        {
            var pgSettings = _databaseService.IsInitialized
                ? _databaseService.CurrentSettings
                : new DatabaseSettings
                {
                    Host = _configuration["DatabaseSettings:Host"] ?? "localhost",
                    Port = _configuration["DatabaseSettings:Port"] ?? "5432",
                    Database = _configuration["DatabaseSettings:Database"] ?? "",
                    Username = _configuration["DatabaseSettings:Username"] ?? "postgres",
                    Password = _encryptionService.Decrypt(_configuration["DatabaseSettings:Password"] ?? ""),
                    SSLMode = _configuration["DatabaseSettings:SSLMode"] ?? "Prefer"
                };

            var sqlConnections = await LoadSqlServerConnectionsFromDb();
            var webServiceConnections = await LoadWebServiceConnectionsFromDb();

            var viewModel = new GeneralSettingsViewModel
            {
                PostgreSql = pgSettings,
                SqlServerConnections = sqlConnections,
                WebServiceConnections = webServiceConnections
            };

            return View(viewModel);
        }

        [HttpPost]
        public IActionResult SaveGeneralSettings(GeneralSettingsViewModel model)
        {
            if (ModelState.IsValid)
            {
                try
                {
                    using (var connection = new NpgsqlConnection(model.PostgreSql.ToConnectionString()))
                    {
                        connection.Open();
                    }

                    UpdatePostgresAppSettings(model.PostgreSql);
                    _databaseService.Initialize(model.PostgreSql);
                    TempData["SuccessMessage"] = "PostgreSQL database settings saved successfully.";

                    return RedirectToAction("General");
                }
                catch (Exception ex)
                {
                    ModelState.AddModelError("", $"Failed to connect to database: {ex.Message}");
                }
            }
            return View("General", model);
        }

        private async Task<List<SqlServerSettings>> LoadSqlServerConnectionsFromDb()
        {
            var connections = new List<SqlServerSettings>();

            if (!_databaseService.IsInitialized) return connections;

            using var conn = _databaseService.CreateNewConnection();
            await conn.OpenAsync();

            string sql = "SELECT * FROM \"SqlServerConnections\" ORDER BY \"Id\"";
            using var cmd = new NpgsqlCommand(sql, conn);
            using var reader = await cmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                connections.Add(new SqlServerSettings
                {
                    ConnectionId = reader["ConnectionId"].ToString(),
                    ConnectionName = reader["ConnectionName"].ToString(),
                    Host = reader["Host"].ToString(),
                    Port = reader["Port"].ToString(),
                    Database = reader["Database"].ToString(),
                    Username = reader["Username"].ToString(),
                    Password = _encryptionService.Decrypt(reader["Password"].ToString()),
                    ProjectName = reader["ProjectName"].ToString(),
                    IsDefault = Convert.ToBoolean(reader["IsDefault"])
                });
            }

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
        public async Task<IActionResult> SaveSqlServerConnections([FromBody] SaveConnectionsRequest request)
        {
            try
            {
                using var conn = _databaseService.CreateNewConnection();
                await conn.OpenAsync();

                using (var deleteCmd = new NpgsqlCommand("DELETE FROM \"SqlServerConnections\" WHERE \"CompanyId\" = 1", conn))
                {
                    await deleteCmd.ExecuteNonQueryAsync();
                }

                foreach (var connData in request.Connections)
                {
                    string insertSql = @"INSERT INTO ""SqlServerConnections"" 
                                       (""ConnectionId"", ""ConnectionName"", ""Host"", ""Port"", ""Database"", ""Username"", ""Password"", ""ProjectName"", ""IsDefault"", ""CompanyId"")
                                       VALUES (@id, @name, @host, @port, @db, @user, @pass, @proj, @isDefault, 1)";

                    using var cmd = new NpgsqlCommand(insertSql, conn);
                    cmd.Parameters.AddWithValue("id", connData.ContainsKey("ConnectionId") ? connData["ConnectionId"] : Guid.NewGuid().ToString());
                    cmd.Parameters.AddWithValue("name", connData.ContainsKey("ConnectionName") ? connData["ConnectionName"] : "");
                    cmd.Parameters.AddWithValue("host", connData.ContainsKey("Host") ? connData["Host"] : "localhost");
                    cmd.Parameters.AddWithValue("port", connData.ContainsKey("Port") ? connData["Port"] : "1433");
                    cmd.Parameters.AddWithValue("db", connData.ContainsKey("Database") ? connData["Database"] : "");
                    cmd.Parameters.AddWithValue("user", connData.ContainsKey("Username") ? connData["Username"] : "");
                    cmd.Parameters.AddWithValue("pass", _encryptionService.Encrypt(connData.ContainsKey("Password") ? connData["Password"] : ""));
                    cmd.Parameters.AddWithValue("proj", connData.ContainsKey("ProjectName") ? connData["ProjectName"] : "");
                    cmd.Parameters.AddWithValue("isDefault", connData.ContainsKey("IsDefault") && connData["IsDefault"].ToLower() == "true");

                    await cmd.ExecuteNonQueryAsync();
                }

                _sqlServerService.LoadSettingsFromDatabase();

                return Json(new { success = true, message = "All SQL Server connections saved successfully to database!" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, error = ex.Message });
            }
        }

        [HttpPost]
        public async Task<IActionResult> DeleteSqlServerConnection([FromBody] DeleteConnectionRequest request)
        {
            try
            {
                using var conn = _databaseService.CreateNewConnection();
                await conn.OpenAsync();

                string sql = "DELETE FROM \"SqlServerConnections\" WHERE \"ConnectionId\" = @id";
                using var cmd = new NpgsqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("id", request.ConnectionId);
                await cmd.ExecuteNonQueryAsync();

                _sqlServerService.LoadSettingsFromDatabase();

                return Json(new { success = true, message = "Connection deleted successfully!" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, error = ex.Message });
            }
        }

        [HttpPost]
        public IActionResult TestSqlServerConnection([FromBody] SqlServerConnectionTestRequest request)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(request.Host) || string.IsNullOrWhiteSpace(request.Database) || string.IsNullOrWhiteSpace(request.Username))
                {
                    return Json(new { success = false, errorMessage = "Host, Database, and Username are required." });
                }

                var settings = new SqlServerSettings
                {
                    Host = request.Host,
                    Port = !string.IsNullOrWhiteSpace(request.Port) ? request.Port : "1433",
                    Database = request.Database,
                    Username = request.Username,
                    Password = request.Password
                };

                using (var connection = new SqlConnection(settings.ToConnectionString()))
                {
                    connection.Open();
                    return Json(new { success = true, message = "Connection successful" });
                }
            }
            catch (Exception ex)
            {
                return Json(new { success = false, errorMessage = $"Connection test failed: {ex.Message}" });
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
                    return Json(new { success = true, message = "Connection successful" });
                }
            }
            catch (Exception ex)
            {
                return Json(new { success = false, errorMessage = ex.Message });
            }
        }

        [HttpPost]
        public IActionResult Connect([FromBody] DatabaseSettings settings)
        {
            try
            {
                try
                {
                    using (var connection = new NpgsqlConnection(settings.ToConnectionString()))
                    {
                        connection.Open();
                        if (!TablesExist(connection))
                        {
                            ExecuteSchemaScript(connection);
                        }
                        UpdatePostgresAppSettings(settings);
                        _databaseService.Initialize(settings);

                        return Json(new { success = true, message = "Connected successfully." });
                    }
                }
                catch (Npgsql.PostgresException ex) when (ex.SqlState == "3D000")
                {
                    var connectionStringBuilder = new NpgsqlConnectionStringBuilder(settings.ToConnectionString())
                    {
                        Database = "postgres"
                    };

                    using (var connection = new NpgsqlConnection(connectionStringBuilder.ConnectionString))
                    {
                        connection.Open();
                        using (var cmd = new NpgsqlCommand($"CREATE DATABASE \"{settings.Database}\"", connection))
                        {
                            cmd.ExecuteNonQuery();
                        }
                        using (var newConnection = new NpgsqlConnection(settings.ToConnectionString()))
                        {
                            newConnection.Open();
                            ExecuteSchemaScript(newConnection);
                            UpdatePostgresAppSettings(settings);
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
            string sqlFilePath = Path.Combine(_webHostEnvironment.WebRootPath, "sql", "initial_schema.sql");
            string sql = System.IO.File.ReadAllText(sqlFilePath);
            using (var cmd = new NpgsqlCommand(sql, connection))
            {
                cmd.ExecuteNonQuery();
            }
        }

        private async Task<List<PCVueWebServiceSettings>> LoadWebServiceConnectionsFromDb()
        {
            var connections = new List<PCVueWebServiceSettings>();
            if (!_databaseService.IsInitialized) return connections;

            using var conn = _databaseService.CreateNewConnection();
            await conn.OpenAsync();

            string sql = "SELECT * FROM \"WebServiceConnections\" ORDER BY \"Id\"";
            using var cmd = new NpgsqlCommand(sql, conn);
            using var reader = await cmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                connections.Add(new PCVueWebServiceSettings
                {
                    ConnectionId = reader["ConnectionId"].ToString(),
                    ConnectionName = reader["ConnectionName"].ToString(),
                    BaseUrl = reader["BaseUrl"].ToString(),
                    ClientId = reader["ClientId"] != DBNull.Value ? reader["ClientId"].ToString() : "",
                    ClientSecret = reader["ClientSecret"] != DBNull.Value ? _encryptionService.Decrypt(reader["ClientSecret"].ToString()) : "",
                    Username = reader["Username"] != DBNull.Value ? reader["Username"].ToString() : "",
                    Password = reader["Password"] != DBNull.Value ? _encryptionService.Decrypt(reader["Password"].ToString()) : "",
                    ProjectName = reader["ProjectName"] != DBNull.Value ? reader["ProjectName"].ToString() : "",
                    TimeoutSeconds = reader["TimeoutSeconds"] != DBNull.Value ? Convert.ToInt32(reader["TimeoutSeconds"]) : 30,
                    IsDefault = reader["IsDefault"] != DBNull.Value && Convert.ToBoolean(reader["IsDefault"]),

                    EnableAutomaticImport = reader["EnableAutomaticImport"] != DBNull.Value && Convert.ToBoolean(reader["EnableAutomaticImport"])
                });
            }
            return connections;
        }
        private void UpdatePostgresAppSettings(DatabaseSettings settings)
        {
            var appSettingsPath = Path.Combine(Directory.GetCurrentDirectory(), "appsettings.json");
            var json = System.IO.File.ReadAllText(appSettingsPath);
            var jsonSettings = JsonDocument.Parse(json);

            var updatedSettings = new Dictionary<string, object>();
            foreach (var element in jsonSettings.RootElement.EnumerateObject())
            {
                updatedSettings[element.Name] = JsonSerializer.Deserialize<object>(element.Value.GetRawText());
            }

            var dbSettings = new Dictionary<string, string>
            {
                { "Host", settings.Host },
                { "Port", settings.Port },
                { "Database", settings.Database },
                { "Username", settings.Username },
                { "Password", _encryptionService.Encrypt(settings.Password) },
                { "SSLMode", settings.SSLMode }
            };

            updatedSettings["DatabaseSettings"] = dbSettings;
            updatedSettings.Remove("SqlServerConnections");
            updatedSettings.Remove("WebServiceConnections");
            updatedSettings.Remove("SqlServerSettings");

            var options = new JsonSerializerOptions { WriteIndented = true };
            System.IO.File.WriteAllText(appSettingsPath, JsonSerializer.Serialize(updatedSettings, options));
        }

        // ==========================================
        // WEB SERVICES CONNECTIONS METHODS
        // ==========================================

        [HttpPost]
        public async Task<IActionResult> SaveWebServiceConnections([FromBody] SaveConnectionsRequest request)
        {
            try
            {
                using var conn = _databaseService.CreateNewConnection();
                await conn.OpenAsync();

         
                using (var deleteCmd = new NpgsqlCommand("DELETE FROM \"WebServiceConnections\" WHERE \"CompanyId\" = 1", conn))
                {
                    await deleteCmd.ExecuteNonQueryAsync();
                }

            
                foreach (var connData in request.Connections)
                {
                    string insertSql = @"INSERT INTO ""WebServiceConnections"" 
           (""ConnectionId"", ""ConnectionName"", ""BaseUrl"", ""ClientId"", ""ClientSecret"", ""Username"", ""Password"", ""ProjectName"", ""IsDefault"", ""IsActive"", ""EnableAutomaticImport"", ""CompanyId"")
           VALUES (@id, @name, @baseUrl, @clientId, @clientSecret, @username, @password, @projectName, @isDefault, @isActive, @enableAutoImport, 1)";

                    using var cmd = new NpgsqlCommand(insertSql, conn);
                    cmd.Parameters.AddWithValue("id", connData.ContainsKey("ConnectionId") ? connData["ConnectionId"] : Guid.NewGuid().ToString());
                    cmd.Parameters.AddWithValue("name", connData.ContainsKey("ConnectionName") ? connData["ConnectionName"] : "");
                    cmd.Parameters.AddWithValue("baseUrl", connData.ContainsKey("BaseUrl") ? connData["BaseUrl"] : "");
                    cmd.Parameters.AddWithValue("clientId", connData.ContainsKey("ClientId") ? connData["ClientId"] : "");

                    string encryptedSecret = connData.ContainsKey("ClientSecret") && !string.IsNullOrEmpty(connData["ClientSecret"]) ? _encryptionService.Encrypt(connData["ClientSecret"]) : "";
                    string encryptedPassword = connData.ContainsKey("Password") && !string.IsNullOrEmpty(connData["Password"]) ? _encryptionService.Encrypt(connData["Password"]) : "";

                    cmd.Parameters.AddWithValue("clientSecret", encryptedSecret);
                    cmd.Parameters.AddWithValue("username", connData.ContainsKey("Username") ? connData["Username"] : "");
                    cmd.Parameters.AddWithValue("password", encryptedPassword);
                    cmd.Parameters.AddWithValue("projectName", connData.ContainsKey("ProjectName") ? connData["ProjectName"] : "");

                    cmd.Parameters.AddWithValue("isDefault", connData.ContainsKey("IsDefault") && connData["IsDefault"].ToLower() == "true");
                    cmd.Parameters.AddWithValue("isActive", true); // la connexion elle-même reste active par défaut
                    cmd.Parameters.AddWithValue("enableAutoImport", connData.ContainsKey("EnableAutomaticImport") && connData["EnableAutomaticImport"].ToLower() == "true");

                    await cmd.ExecuteNonQueryAsync();
                }

                return Json(new { success = true, message = "All Web Service connections saved successfully!" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, error = ex.Message });
            }
        }

        [HttpPost]
        public async Task<IActionResult> DeleteWebServiceConnection([FromBody] DeleteConnectionRequest request)
        {
            try
            {
                using var conn = _databaseService.CreateNewConnection();
                await conn.OpenAsync();

                string sql = "DELETE FROM \"WebServiceConnections\" WHERE \"ConnectionId\" = @id";
                using var cmd = new NpgsqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("id", request.ConnectionId);
                await cmd.ExecuteNonQueryAsync();

                return Json(new { success = true, message = "Web Service Connection deleted successfully!" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, error = ex.Message });
            }
        }

        [HttpPost]
        public IActionResult GetWebServiceToken([FromBody] WebServiceTokenRequest request)
        {
            try
            {
                // Ici, tu pourras ajouter la vraie logique HTTP pour appeler l'API PCVue plus tard.
                // Pour l'instant, on simule une réponse réussie pour débloquer l'interface.
                Console.WriteLine($"Mocking Token Request for {request.BaseUrl}");

                return Json(new { success = true, expiresIn = 3600 });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, errorMessage = ex.Message });
            }
        }

        [HttpPost]
        public IActionResult RefreshWebServiceToken([FromBody] WebServiceTokenRequest request)
        {
            try
            {
                Console.WriteLine($"Mocking Token Refresh for {request.BaseUrl}");
                return Json(new { success = true, expiresIn = 3600 });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, errorMessage = ex.Message });
            }
        }
    }

    // ==========================================
    // CLASSES DE REQUÊTES (DTOs)
    // ==========================================

    public class SqlServerConnectionTestRequest
    {
        public string ConnectionId { get; set; } = "";
        public string ConnectionName { get; set; } = "";
        public string Host { get; set; } = "";
        public string Port { get; set; } = "1433";
        public string Database { get; set; } = "";
        public string Username { get; set; } = "";
        public string Password { get; set; } = "";
        public string ProjectName { get; set; } = "";
    }

    public class SaveConnectionsRequest
    {
        public List<Dictionary<string, string>> Connections { get; set; } = new List<Dictionary<string, string>>();
    }

    public class DeleteConnectionRequest
    {
        public string ConnectionId { get; set; } = "";
    }

    public class WebServiceTokenRequest
    {
        public string ConnectionId { get; set; } = "";
        public string BaseUrl { get; set; } = "";
        public string ClientId { get; set; } = "";
        public string ClientSecret { get; set; } = "";
        public string Username { get; set; } = "";
        public string Password { get; set; } = "";
    }
}