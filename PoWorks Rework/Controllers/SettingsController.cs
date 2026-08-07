using Microsoft.AspNetCore.Mvc;
using PoWorks_Rework.Models;
using System.Text.Json;
using Npgsql;
using PoWorks_Rework.Services;
using Microsoft.Data.SqlClient;
using System.Text;
using PoWorks_Rework.Repositories; 

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
        private readonly ICompanyContext _companyContext;
        private readonly PCVueWebService _pcVueWebService;

        /// <summary>
        /// Initializes the settings controller with configuration and service dependencies.
        /// </summary>
        public SettingsController(
            IConfiguration configuration,
            IWebHostEnvironment webHostEnvironment,
            DatabaseService databaseService,
            SqlServerService sqlServerService,
            EncryptionService encryptionService,
            ICompanyContext companyContext,
            PCVueWebService pcVueWebService)
        {
            _configuration = configuration;
            _webHostEnvironment = webHostEnvironment;
            _databaseService = databaseService;
            _sqlServerService = sqlServerService;
            _encryptionService = encryptionService;
            _companyContext = companyContext;
            _pcVueWebService = pcVueWebService;
        }

        /// <summary>
        /// Displays the general settings page containing PostgreSQL, SQL Server, and Web Service connection settings.
        /// </summary>
        /// <returns>The general settings view with current configurations.</returns>
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

        /// <summary>
        /// Saves the PostgreSQL database settings to the configuration file and initializes the connection.
        /// </summary>
        /// <param name="model">The general settings view model containing PostgreSQL settings.</param>
        /// <returns>A redirect to the general settings page, or the same view if validation fails.</returns>
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

        /// <summary>
        /// Loads all SQL Server connection configurations from the PostgreSQL database for the current company.
        /// </summary>
        /// <returns>A list of SQL Server settings.</returns>
        private async Task<List<SqlServerSettings>> LoadSqlServerConnectionsFromDb()
        {
            var connections = new List<SqlServerSettings>();

            if (!_databaseService.IsInitialized) return connections;

            using var conn = _databaseService.CreateNewConnection();
            await conn.OpenAsync();

            int currentCompanyId = _companyContext.CurrentCompanyId;

            string sql = "SELECT * FROM \"SqlServerConnections\" WHERE \"CompanyId\" = @companyId ORDER BY \"Id\"";
            using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@companyId", currentCompanyId);

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
                    IsDefault = Convert.ToBoolean(reader["IsDefault"]),
                    UseWindowsAuth = reader["UseWindowsAuth"] != DBNull.Value && Convert.ToBoolean(reader["UseWindowsAuth"])
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
                    Database = "",
                    Username = "",
                    Password = "",
                    ProjectName = "",
                    IsDefault = true,
                    UseWindowsAuth = false
                });
            }

            return connections;
        }

        /// <summary>
        /// Saves a list of SQL Server connection configurations to the database for the current company.
        /// Replaces any existing connections.
        /// </summary>
        /// <param name="request">The request containing the connections to save.</param>
        /// <returns>A JSON response indicating success or failure.</returns>
        [HttpPost]
        public async Task<IActionResult> SaveSqlServerConnections([FromBody] SaveConnectionsRequest request)
        {
            try
            {
                using var conn = _databaseService.CreateNewConnection();
                await conn.OpenAsync();

                int currentCompanyId = _companyContext.CurrentCompanyId;

                using (var deleteCmd = new NpgsqlCommand("DELETE FROM \"SqlServerConnections\" WHERE \"CompanyId\" = @companyId", conn))
                {
                    deleteCmd.Parameters.AddWithValue("@companyId", currentCompanyId);
                    await deleteCmd.ExecuteNonQueryAsync();
                }

                foreach (var connData in request.Connections)
                {
                    string insertSql = @"INSERT INTO ""SqlServerConnections"" 
                    (""ConnectionId"", ""ConnectionName"", ""Host"", ""Port"", ""Database"", ""Username"", ""Password"", ""ProjectName"", ""IsDefault"", ""UseWindowsAuth"", ""CompanyId"")
                    VALUES (@id, @name, @host, @port, @db, @user, @pass, @proj, @isDefault, @useWindowsAuth, @companyId)";

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
                    cmd.Parameters.AddWithValue("useWindowsAuth", connData.ContainsKey("UseWindowsAuth") && connData["UseWindowsAuth"].ToLower() == "true");
                    cmd.Parameters.AddWithValue("companyId", currentCompanyId);

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

        /// <summary>
        /// Deletes a specific SQL Server connection from the database based on its ID.
        /// </summary>
        /// <param name="request">The request containing the connection ID to delete.</param>
        /// <returns>A JSON response indicating success or failure.</returns>
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

        /// <summary>
        /// Tests a SQL Server connection using the provided configuration.
        /// </summary>
        /// <param name="request">The SQL Server connection configuration to test.</param>
        /// <returns>A JSON response indicating whether the connection test was successful.</returns>
        [HttpPost]
        public IActionResult TestSqlServerConnection([FromBody] SqlServerConnectionTestRequest request)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(request.Host) || string.IsNullOrWhiteSpace(request.Database))
                {
                    return Json(new { success = false, errorMessage = "Host and Database are required." });
                }

                if (!request.UseWindowsAuth && string.IsNullOrWhiteSpace(request.Username))
                {
                    return Json(new { success = false, errorMessage = "Username is required for SQL authentication." });
                }

                var settings = new SqlServerSettings
                {
                    Host = request.Host,
                    Port = !string.IsNullOrWhiteSpace(request.Port) ? request.Port : "1433",
                    Database = request.Database,
                    Username = request.Username,
                    Password = request.Password,
                    UseWindowsAuth = request.UseWindowsAuth
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

        /// <summary>
        /// Tests a PostgreSQL database connection.
        /// </summary>
        /// <param name="settings">The PostgreSQL database settings to test.</param>
        /// <returns>A JSON response indicating whether the connection was successful.</returns>
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

        /// <summary>
        /// Connects to a PostgreSQL database, creating it if it doesn't exist, and initializes the required tables.
        /// </summary>
        /// <param name="settings">The PostgreSQL database settings.</param>
        /// <returns>A JSON response indicating success or failure of the connection and setup process.</returns>
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
                catch (Npgsql.PostgresException ex) when (ex.SqlState == "3D000") // Database does not exist
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

        /// <summary>
        /// Checks if the core application tables (e.g., 'tenants') exist in the current database.
        /// </summary>
        /// <param name="connection">An open PostgreSQL connection.</param>
        /// <returns>True if tables exist, false otherwise.</returns>
        private bool TablesExist(NpgsqlConnection connection)
        {
            using (var cmd = new NpgsqlCommand(
                "SELECT EXISTS (SELECT FROM information_schema.tables WHERE table_name = 'tenants')", connection))
            {
                return (bool)cmd.ExecuteScalar();
            }
        }

        /// <summary>
        /// Executes the initial schema SQL script to set up the database structure.
        /// </summary>
        /// <param name="connection">An open PostgreSQL connection.</param>
        private void ExecuteSchemaScript(NpgsqlConnection connection)
        {
            string sqlFilePath = Path.Combine(_webHostEnvironment.WebRootPath, "sql", "initial_schema.sql");
            string sql = System.IO.File.ReadAllText(sqlFilePath);
            using (var cmd = new NpgsqlCommand(sql, connection))
            {
                cmd.ExecuteNonQuery();
            }
        }

        /// <summary>
        /// Loads all Web Service (PCVue) connection configurations from the database for the current company.
        /// </summary>
        /// <returns>A list of Web Service settings.</returns>
        private async Task<List<PCVueWebServiceSettings>> LoadWebServiceConnectionsFromDb()
        {
            var connections = new List<PCVueWebServiceSettings>();
            if (!_databaseService.IsInitialized) return connections;

            using var conn = _databaseService.CreateNewConnection();
            await conn.OpenAsync();

            int currentCompanyId = _companyContext.CurrentCompanyId;

            string sql = "SELECT * FROM \"WebServiceConnections\" WHERE \"CompanyId\" = @companyId ORDER BY \"Id\"";
            using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@companyId", currentCompanyId);

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
                    EnableAutomaticImport = reader["EnableAutomaticImport"] != DBNull.Value && Convert.ToBoolean(reader["EnableAutomaticImport"]),
                    AutoImportIntervalMinutes = reader["AutoImportIntervalMinutes"] != DBNull.Value ? Convert.ToInt32(reader["AutoImportIntervalMinutes"]) : 1
                });
            }
            return connections;
        }

        /// <summary>
        /// Updates the appsettings.json file with the newly configured PostgreSQL connection settings.
        /// The password is encrypted before saving.
        /// </summary>
        /// <param name="settings">The PostgreSQL database settings to save.</param>
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

        /// <summary>
        /// Saves a list of Web Service (PCVue) connection configurations to the database for the current company.
        /// Replaces any existing configurations.
        /// </summary>
        /// <param name="request">The request containing the connections to save.</param>
        /// <returns>A JSON response indicating success or failure.</returns>
        [HttpPost]
        public async Task<IActionResult> SaveWebServiceConnections([FromBody] SaveConnectionsRequest request)
        {
            try
            {
                using var conn = _databaseService.CreateNewConnection();
                await conn.OpenAsync();

                int currentCompanyId = _companyContext.CurrentCompanyId;

                using (var deleteCmd = new NpgsqlCommand("DELETE FROM \"WebServiceConnections\" WHERE \"CompanyId\" = @companyId", conn))
                {
                    deleteCmd.Parameters.AddWithValue("@companyId", currentCompanyId);
                    await deleteCmd.ExecuteNonQueryAsync();
                }

                foreach (var connData in request.Connections)
                {
                    int autoImportInterval = 1;
                    if (connData.ContainsKey("AutoImportIntervalMinutes") && int.TryParse(connData["AutoImportIntervalMinutes"], out int parsedInterval))
                    {
                        autoImportInterval = Math.Clamp(parsedInterval, 1, 1440);
                    }

                    string insertSql = @"INSERT INTO ""WebServiceConnections"" 
           (""ConnectionId"", ""ConnectionName"", ""BaseUrl"", ""ClientId"", ""ClientSecret"", ""Username"", ""Password"", ""ProjectName"", ""IsDefault"", ""IsActive"", ""EnableAutomaticImport"", ""AutoImportIntervalMinutes"", ""CompanyId"")
           VALUES (@id, @name, @baseUrl, @clientId, @clientSecret, @username, @password, @projectName, @isDefault, @isActive, @enableAutoImport, @autoImportInterval, @companyId)";

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
                    cmd.Parameters.AddWithValue("isActive", true);
                    cmd.Parameters.AddWithValue("enableAutoImport", connData.ContainsKey("EnableAutomaticImport") && connData["EnableAutomaticImport"].ToLower() == "true");
                    cmd.Parameters.AddWithValue("autoImportInterval", autoImportInterval);
                    cmd.Parameters.AddWithValue("companyId", currentCompanyId);

                    await cmd.ExecuteNonQueryAsync();
                }

                return Json(new { success = true, message = "All Web Service connections saved successfully!" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, error = ex.Message });
            }
        }

        /// <summary>
        /// Deletes a specific Web Service connection from the database based on its ID.
        /// </summary>
        /// <param name="request">The request containing the connection ID to delete.</param>
        /// <returns>A JSON response indicating success or failure.</returns>
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

        /// <summary>
        /// Attempts to obtain an OAuth token from the specified Web Service connection to verify its settings.
        /// </summary>
        /// <param name="request">The Web Service connection details.</param>
        /// <returns>A JSON response indicating whether the token retrieval was successful, along with its expiration.</returns>
        [HttpPost]
        public async Task<IActionResult> GetWebServiceToken([FromBody] WebServiceTokenRequest request)
        {
            try
            {
                var testSettings = new PCVueWebServiceSettings
                {
                    BaseUrl = request.BaseUrl,
                    ClientId = request.ClientId,
                    ClientSecret = request.ClientSecret,
                    Username = request.Username,
                    Password = request.Password
                };

                var tokenResponse = await _pcVueWebService.GetAccessTokenAsync(testSettings);

                if (tokenResponse.Success)
                {
                    return Json(new { success = true, expiresIn = tokenResponse.ExpiresIn });
                }
                else
                {
                    return Json(new { success = false, errorMessage = tokenResponse.ErrorMessage });
                }
            }
            catch (Exception ex)
            {
                return Json(new { success = false, errorMessage = ex.Message });
            }
        }

        /// <summary>
        /// Refreshes the Web Service OAuth token by initiating a new token request.
        /// </summary>
        /// <param name="request">The Web Service connection details.</param>
        /// <returns>A JSON response indicating success or failure, delegating to the token retrieval method.</returns>
        [HttpPost]
        public async Task<IActionResult> RefreshWebServiceToken([FromBody] WebServiceTokenRequest request)
        {
            return await GetWebServiceToken(request);
        }
    }

    /// <summary>
    /// Represents the incoming request data for testing a SQL Server connection.
    /// </summary>
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
        public bool UseWindowsAuth { get; set; } = false;
    }

    /// <summary>
    /// Represents the incoming request data for bulk saving connections.
    /// </summary>
    public class SaveConnectionsRequest
    {
        public List<Dictionary<string, string>> Connections { get; set; } = new List<Dictionary<string, string>>();
    }

    /// <summary>
    /// Represents the incoming request data to delete a specific connection by ID.
    /// </summary>
    public class DeleteConnectionRequest
    {
        public string ConnectionId { get; set; } = "";
    }

    /// <summary>
    /// Represents the incoming request data for retrieving or refreshing a Web Service token.
    /// </summary>
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