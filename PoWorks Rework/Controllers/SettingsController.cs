using Microsoft.AspNetCore.Mvc;
using PoWorks_Rework.Models;
using System.Text.Json;
using Npgsql;
using PoWorks_Rework.Services;
using Microsoft.Data.SqlClient; 
using System.Text; 

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
            var sqlConnections = LoadSqlServerConnections();
            var webServiceConnections = LoadWebServiceConnections();

            var viewModel = new GeneralSettingsViewModel
            {
                PostgreSql = pgSettings,
                SqlServerConnections = sqlConnections,
                WebServiceConnections = webServiceConnections 
            };

            return View(viewModel);
        }
        [HttpPost]
        public async Task<IActionResult> GetWebServiceToken([FromBody] WebServiceConnectionTestRequest request)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(request.BaseUrl))
                    return Json(new { success = false, errorMessage = "Base URL is required" });

                if (string.IsNullOrWhiteSpace(request.Username))
                    return Json(new { success = false, errorMessage = "Username is required" });

                if (string.IsNullOrWhiteSpace(request.Password))
                    return Json(new { success = false, errorMessage = "Password is required" });

                if (string.IsNullOrWhiteSpace(request.ClientId))
                    return Json(new { success = false, errorMessage = "Client ID is required" });

                if (string.IsNullOrWhiteSpace(request.ClientSecret))
                    return Json(new { success = false, errorMessage = "Client Secret is required" });
                var settings = new PCVueWebServiceSettings
                {
                    ConnectionId = request.ConnectionId,
                    ConnectionName = request.ConnectionName,
                    BaseUrl = request.BaseUrl,
                    ClientId = request.ClientId,
                    ClientSecret = request.ClientSecret,
                    Username = request.Username,
                    Password = request.Password,
                    AuthType = AuthenticationType.OAuth,
                    TimeoutSeconds = request.TimeoutSeconds,
                    ProjectName = request.ProjectName
                };
                var handler = new HttpClientHandler();
                handler.ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true;

                using var httpClient = new HttpClient(handler);
                httpClient.Timeout = TimeSpan.FromSeconds(settings.TimeoutSeconds);

                var logger = HttpContext.RequestServices.GetRequiredService<ILogger<PCVueWebService>>();
                var webService = new PCVueWebService(httpClient, logger);
                var tokenResponse = await webService.GetAccessTokenAsync(settings);

                if (tokenResponse.Success)
                {
                    var controllerLogger = HttpContext.RequestServices.GetRequiredService<ILogger<SettingsController>>();
                    controllerLogger.LogInformation("=== OAUTH TOKEN ACQUIRED ===");
                    controllerLogger.LogInformation("Connection: {ConnectionName}", settings.ConnectionName);
                    controllerLogger.LogInformation("Access Token: {AccessToken}", tokenResponse.AccessToken);
                    controllerLogger.LogInformation("Refresh Token: {RefreshToken}", tokenResponse.RefreshToken ?? "Not provided");
                    controllerLogger.LogInformation("Token Type: {TokenType}", tokenResponse.TokenType);
                    controllerLogger.LogInformation("Expires In: {ExpiresIn} seconds", tokenResponse.ExpiresIn);
                    controllerLogger.LogInformation("=============================");

                    return Json(new
                    {
                        success = true,
                        message = "OAuth token acquired successfully! Check server terminal for token details.",
                        expiresIn = tokenResponse.ExpiresIn,
                        tokenType = tokenResponse.TokenType
                    });
                }
                else
                {
                    var controllerLogger = HttpContext.RequestServices.GetRequiredService<ILogger<SettingsController>>();
                    controllerLogger.LogError("Failed to get OAuth token for connection {ConnectionName}: {ErrorMessage}",
                        settings.ConnectionName, tokenResponse.ErrorMessage);

                    return Json(new
                    {
                        success = false,
                        errorMessage = tokenResponse.ErrorMessage
                    });
                }
            }
            catch (Exception ex)
            {
                var logger = HttpContext.RequestServices.GetRequiredService<ILogger<SettingsController>>();
                logger.LogError(ex, "Error getting OAuth token");
                return Json(new { success = false, errorMessage = ex.Message });
            }
        }
        [HttpPost]
        public async Task<IActionResult> RefreshWebServiceToken([FromBody] WebServiceConnectionTestRequest request)
        {
            try
            {
                var settings = new PCVueWebServiceSettings
                {
                    ConnectionId = request.ConnectionId,
                    ConnectionName = request.ConnectionName,
                    BaseUrl = request.BaseUrl,
                    ClientId = request.ClientId,
                    ClientSecret = request.ClientSecret,
                    Username = request.Username,
                    Password = request.Password,
                    AuthType = AuthenticationType.OAuth,
                    TimeoutSeconds = request.TimeoutSeconds,
                    ProjectName = request.ProjectName
                };
                var handler = new HttpClientHandler();
                handler.ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true;

                using var httpClient = new HttpClient(handler);
                httpClient.Timeout = TimeSpan.FromSeconds(settings.TimeoutSeconds);

                var logger = HttpContext.RequestServices.GetRequiredService<ILogger<PCVueWebService>>();
                var webService = new PCVueWebService(httpClient, logger);
                var token = await webService.GetValidAccessTokenAsync(settings);

                if (!string.IsNullOrEmpty(token))
                {
                    var controllerLogger = HttpContext.RequestServices.GetRequiredService<ILogger<SettingsController>>();
                    controllerLogger.LogInformation("=== TOKEN REFRESH ATTEMPT ===");
                    controllerLogger.LogInformation("Connection: {ConnectionName}", settings.ConnectionName);
                    controllerLogger.LogInformation("Valid Token Retrieved: {Token}", token);
                    controllerLogger.LogInformation("=============================");

                    return Json(new
                    {
                        success = true,
                        message = "Token retrieved successfully! Check server terminal for token details.",
                        expiresIn = 3600 
                    });
                }
                else
                {
                    var controllerLogger = HttpContext.RequestServices.GetRequiredService<ILogger<SettingsController>>();
                    controllerLogger.LogError("Failed to refresh/get token for connection {ConnectionName}", settings.ConnectionName);

                    return Json(new
                    {
                        success = false,
                        errorMessage = "Failed to get valid token. Check server logs for details."
                    });
                }
            }
            catch (Exception ex)
            {
                var logger = HttpContext.RequestServices.GetRequiredService<ILogger<SettingsController>>();
                logger.LogError(ex, "Error refreshing OAuth token");
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
                            UpdateAppSettings(settings);
                            _databaseService.Initialize(settings);

                            return Json(new { success = true, message = "Connected successfully and created tables." });
                        }
                        UpdateAppSettings(settings);
                        _databaseService.Initialize(settings);

                        return Json(new { success = true, message = "Connected successfully to existing database." });
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
                    using (var connection = new NpgsqlConnection(model.PostgreSql.ToConnectionString()))
                    {
                        connection.Open();
                    }
                    UpdateAppSettings(model.PostgreSql);
                    _databaseService.Initialize(model.PostgreSql);
                    if (!string.IsNullOrEmpty(model.SqlServer.Host) && !string.IsNullOrEmpty(model.SqlServer.Database))
                    {
                        try
                        {
                            using (var connection = new SqlConnection(model.SqlServer.ToConnectionString()))
                            {
                                connection.Open();
                            }
                            UpdateSqlServerSettings(model.SqlServer);
                            TempData["SuccessMessage"] = "Database settings saved successfully.";
                        }
                        catch (Exception ex)
                        {
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
        public IActionResult TestSqlServerConnection([FromBody] SqlServerConnectionTestRequest request)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(request.Host))
                {
                    return Json(new { success = false, errorMessage = "Host is required" });
                }

                if (string.IsNullOrWhiteSpace(request.Database))
                {
                    return Json(new { success = false, errorMessage = "Database name is required" });
                }

                if (string.IsNullOrWhiteSpace(request.Username))
                {
                    return Json(new { success = false, errorMessage = "Username is required" });
                }
                var settings = new SqlServerSettings
                {
                    ConnectionId = request.ConnectionId,
                    ConnectionName = request.ConnectionName,
                    Host = request.Host,
                    Port = !string.IsNullOrWhiteSpace(request.Port) ? request.Port : "1433",
                    Database = request.Database,
                    Username = request.Username,
                    Password = request.Password,
                    ProjectName = request.ProjectName
                };
                using (var connection = new SqlConnection(settings.ToConnectionString()))
                {
                    connection.Open();
                    using (var command = new SqlCommand("SELECT 1", connection))
                    {
                        var result = command.ExecuteScalar();
                    }

                    return Json(new
                    {
                        success = true,
                        message = "Connection successful",
                        connectionInfo = new
                        {
                            host = settings.Host,
                            port = settings.Port,
                            database = settings.Database,
                            username = settings.Username
                        }
                    });
                }
            }
            catch (SqlException sqlEx)
            {
                string friendlyMessage = sqlEx.Number switch
                {
                    2 => "Cannot connect to server. Please check the host and port.",
                    18456 => "Login failed. Please check your username and password.",
                    4060 => "Cannot open database. Please check the database name.",
                    53 => "Network path not found. Please check the server name.",
                    _ => $"SQL Server error: {sqlEx.Message}"
                };

                return Json(new
                {
                    success = false,
                    errorMessage = friendlyMessage,
                    sqlErrorNumber = sqlEx.Number
                });
            }
            catch (Exception ex)
            {
                return Json(new
                {
                    success = false,
                    errorMessage = $"Connection test failed: {ex.Message}"
                });
            }
        }

        #region SQL Server Connection Methods

        private List<SqlServerSettings> LoadSqlServerConnections()
        {
            var connections = new List<SqlServerSettings>();
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
                if (connections.Count <= 1)
                {
                    return Json(new { success = false, error = "Cannot delete the last connection. At least one connection is required." });
                }
                var connectionToDelete = connections.FirstOrDefault(c => c.ConnectionId == request.ConnectionId);
                if (connectionToDelete == null)
                {
                    return Json(new { success = false, error = "Connection not found." });
                }
                connections.Remove(connectionToDelete);
                if (connectionToDelete.IsDefault && connections.Any())
                {
                    connections.First().IsDefault = true;
                }
                UpdateSqlServerConnections(connections);
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
            if (updatedSettings.ContainsKey("SqlServerSettings"))
            {
                updatedSettings.Remove("SqlServerSettings");
            }
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

        #endregion

        #region Web Services Methods
        private List<PCVueWebServiceSettings> LoadWebServiceConnections()
        {
            var connections = new List<PCVueWebServiceSettings>();
            var webServiceConnections = _configuration.GetSection("WebServiceConnections").GetChildren();

            if (webServiceConnections.Any())
            {
                foreach (var connectionSection in webServiceConnections)
                {
                    var connection = new PCVueWebServiceSettings
                    {
                        ConnectionId = connectionSection["ConnectionId"] ?? Guid.NewGuid().ToString(),
                        ConnectionName = connectionSection["ConnectionName"] ?? "",
                        BaseUrl = connectionSection["BaseUrl"] ?? "",
                        ClientId = connectionSection["ClientId"] ?? "",
                        ClientSecret = connectionSection["ClientSecret"] ?? "",
                        ApiKey = connectionSection["ApiKey"] ?? "",
                        AuthType = Enum.Parse<AuthenticationType>(connectionSection["AuthType"] ?? "0"),
                        TimeoutSeconds = int.Parse(connectionSection["TimeoutSeconds"] ?? "30"),
                        ProjectName = connectionSection["ProjectName"] ?? "",
                        IsDefault = bool.Parse(connectionSection["IsDefault"] ?? "false")
                    };
                    connections.Add(connection);
                }
            }
            if (!connections.Any())
            {
                connections.Add(new PCVueWebServiceSettings
                {
                    ConnectionId = Guid.NewGuid().ToString(),
                    ConnectionName = "Default Web Service Connection",
                    BaseUrl = "",
                    TimeoutSeconds = 30,
                    IsDefault = true
                });
            }

            return connections;
        }
        [HttpPost]
        public async Task<IActionResult> TestWebServiceConnection([FromBody] WebServiceConnectionTestRequest request)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(request.BaseUrl))
                {
                    return Json(new { success = false, errorMessage = "Base URL is required" });
                }
                var settings = new PCVueWebServiceSettings
                {
                    ConnectionId = request.ConnectionId,
                    ConnectionName = request.ConnectionName,
                    BaseUrl = request.BaseUrl,
                    ClientId = request.ClientId,
                    ClientSecret = request.ClientSecret,
                    ApiKey = request.ApiKey,
                    Username = request.Username,  
                    Password = request.Password,  
                    AuthType = (AuthenticationType)request.AuthType,
                    TimeoutSeconds = request.TimeoutSeconds,
                    ProjectName = request.ProjectName
                };

                using var httpClient = new HttpClient();
                httpClient.Timeout = TimeSpan.FromSeconds(settings.TimeoutSeconds);
                switch (settings.AuthType)
                {
                    case AuthenticationType.OAuth:
                        if (!string.IsNullOrEmpty(settings.ClientId) && !string.IsNullOrEmpty(settings.ClientSecret))
                        {
                            var authString = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{settings.ClientId}:{settings.ClientSecret}"));
                            httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", authString);
                        }
                        break;
                    case AuthenticationType.ApiKey:
                        if (!string.IsNullOrEmpty(settings.ApiKey))
                        {
                            httpClient.DefaultRequestHeaders.Add("X-API-Key", settings.ApiKey);
                        }
                        break;
                    case AuthenticationType.Basic:  
                        if (!string.IsNullOrEmpty(settings.Username) && !string.IsNullOrEmpty(settings.Password))
                        {
                            var authString = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{settings.Username}:{settings.Password}"));
                            httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", authString);
                        }
                        break;
                }
                var testUrl = settings.BaseUrl.TrimEnd('/') + "/system/status";
                var response = await httpClient.GetAsync(testUrl);

                if (response.IsSuccessStatusCode)
                {
                    return Json(new { success = true, message = "Web Service connection successful!" });
                }
                else
                {
                    return Json(new { success = false, errorMessage = $"HTTP {response.StatusCode}: {response.ReasonPhrase}" });
                }
            }
            catch (HttpRequestException ex)
            {
                return Json(new { success = false, errorMessage = $"Connection failed: {ex.Message}" });
            }
            catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
            {
                return Json(new { success = false, errorMessage = "Connection timeout" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, errorMessage = ex.Message });
            }
        }
        [HttpPost]
        public IActionResult SaveWebServiceConnections([FromBody] SaveWebServiceConnectionsRequest request)
        {
            try
            {
                var connections = new List<PCVueWebServiceSettings>();

                foreach (var connData in request.Connections)
                {
                    var connection = new PCVueWebServiceSettings
                    {
                        ConnectionId = connData.ContainsKey("ConnectionId") ? connData["ConnectionId"] : Guid.NewGuid().ToString(),
                        ConnectionName = connData.ContainsKey("ConnectionName") ? connData["ConnectionName"] : "",
                        BaseUrl = connData.ContainsKey("BaseUrl") ? connData["BaseUrl"] : "",
                        ClientId = connData.ContainsKey("ClientId") ? connData["ClientId"] : "",
                        ClientSecret = connData.ContainsKey("ClientSecret") ? connData["ClientSecret"] : "",
                        ApiKey = connData.ContainsKey("ApiKey") ? connData["ApiKey"] : "",
                        Username = connData.ContainsKey("Username") ? connData["Username"] : "",  
                        Password = connData.ContainsKey("Password") ? connData["Password"] : "",  

                        AuthType = connData.ContainsKey("AuthType") ? Enum.Parse<AuthenticationType>(connData["AuthType"]) : AuthenticationType.OAuth,
                        TimeoutSeconds = connData.ContainsKey("TimeoutSeconds") ? int.Parse(connData["TimeoutSeconds"]) : 30,
                        ProjectName = connData.ContainsKey("ProjectName") ? connData["ProjectName"] : "",
                        IsDefault = connData.ContainsKey("IsDefault") ? bool.Parse(connData["IsDefault"]) : false
                    };
                    connections.Add(connection);
                }
                UpdateWebServiceSettings(connections);

                return Json(new { success = true, message = "Web Service connections saved successfully!" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, error = ex.Message });
            }
        }
        [HttpPost]
        public IActionResult DeleteWebServiceConnection([FromBody] DeleteConnectionRequest request)
        {
            try
            {
                var connections = LoadWebServiceConnections();
                if (connections.Count <= 1)
                {
                    return Json(new { success = false, error = "Cannot delete the last connection. At least one connection is required." });
                }
                var connectionToDelete = connections.FirstOrDefault(c => c.ConnectionId == request.ConnectionId);
                if (connectionToDelete == null)
                {
                    return Json(new { success = false, error = "Connection not found." });
                }
                connections.Remove(connectionToDelete);
                if (connectionToDelete.IsDefault && connections.Any())
                {
                    connections.First().IsDefault = true;
                }
                UpdateWebServiceConnections(connections);

                return Json(new
                {
                    success = true,
                    message = "Web Service connection deleted successfully!",
                    newDefaultConnectionId = connections.FirstOrDefault(c => c.IsDefault)?.ConnectionId
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, error = ex.Message });
            }
        }

        private void UpdateWebServiceSettings(List<PCVueWebServiceSettings> connections)
        {
            var appSettingsPath = Path.Combine(Directory.GetCurrentDirectory(), "appsettings.json");
            var json = System.IO.File.ReadAllText(appSettingsPath);
            var jsonSettings = JsonDocument.Parse(json);
            var updatedSettings = new Dictionary<string, object>();
            foreach (var element in jsonSettings.RootElement.EnumerateObject())
            {
                updatedSettings[element.Name] = JsonSerializer.Deserialize<object>(element.Value.GetRawText());
            }
            var webServiceSettings = connections.Select(conn => new Dictionary<string, object>
    {
        { "ConnectionId", conn.ConnectionId },
        { "ConnectionName", conn.ConnectionName },
        { "BaseUrl", conn.BaseUrl },
        { "ClientId", conn.ClientId },
        { "ClientSecret", conn.ClientSecret },
        { "ApiKey", conn.ApiKey },
        { "Username", conn.Username },  
        { "Password", conn.Password },  
        { "AuthType", (int)conn.AuthType },
        { "TimeoutSeconds", conn.TimeoutSeconds },
        { "ProjectName", conn.ProjectName },
        { "IsDefault", conn.IsDefault }
    }).ToList();
            updatedSettings["WebServiceConnections"] = webServiceSettings;
            var options = new JsonSerializerOptions { WriteIndented = true };
            var updatedJson = JsonSerializer.Serialize(updatedSettings, options);
            System.IO.File.WriteAllText(appSettingsPath, updatedJson);
        }

        private List<PCVueWebServiceSettings> GetWebServiceConnections()
        {
            var connections = new List<PCVueWebServiceSettings>();
            var webServiceSection = _configuration.GetSection("WebServiceConnections");

            if (webServiceSection.Exists())
            {
                foreach (var connectionSection in webServiceSection.GetChildren())
                {
                    var connection = new PCVueWebServiceSettings
                    {
                        ConnectionId = connectionSection["ConnectionId"] ?? Guid.NewGuid().ToString(),
                        ConnectionName = connectionSection["ConnectionName"] ?? "",
                        BaseUrl = connectionSection["BaseUrl"] ?? "",
                        ClientId = connectionSection["ClientId"] ?? "",
                        ClientSecret = connectionSection["ClientSecret"] ?? "",
                        ApiKey = connectionSection["ApiKey"] ?? "",
                        Username = connectionSection["Username"] ?? "",  
                        Password = connectionSection["Password"] ?? "",  
                        AuthType = Enum.Parse<AuthenticationType>(connectionSection["AuthType"] ?? "0"),
                        TimeoutSeconds = int.Parse(connectionSection["TimeoutSeconds"] ?? "30"),
                        ProjectName = connectionSection["ProjectName"] ?? "",
                        IsDefault = bool.Parse(connectionSection["IsDefault"] ?? "false")
                    };
                    connections.Add(connection);
                }
            }
            if (!connections.Any())
            {
                connections.Add(new PCVueWebServiceSettings
                {
                    ConnectionId = Guid.NewGuid().ToString(),
                    ConnectionName = "Default Web Service Connection",
                    BaseUrl = "",
                    TimeoutSeconds = 30,
                    IsDefault = true
                });
            }

            return connections;
        }
        private void UpdateWebServiceConnections(List<PCVueWebServiceSettings> connections)
        {
            var appSettingsPath = Path.Combine(Directory.GetCurrentDirectory(), "appsettings.json");
            var json = System.IO.File.ReadAllText(appSettingsPath);
            var jsonSettings = JsonDocument.Parse(json);
            var updatedSettings = new Dictionary<string, object>();
            foreach (var element in jsonSettings.RootElement.EnumerateObject())
            {
                if (element.Name != "WebServiceConnections")
                {
                    updatedSettings[element.Name] = JsonSerializer.Deserialize<object>(element.Value.GetRawText());
                }
            }
            var connectionsList = connections.Select(c => new Dictionary<string, object>
    {
        { "ConnectionId", c.ConnectionId },
        { "ConnectionName", c.ConnectionName },
        { "BaseUrl", c.BaseUrl },
        { "ClientId", c.ClientId },
        { "ClientSecret", c.ClientSecret },
        { "ApiKey", c.ApiKey },
        { "Username", c.Username },        
        { "Password", c.Password },        
        { "AuthType", (int)c.AuthType },
        { "TimeoutSeconds", c.TimeoutSeconds },
        { "ProjectName", c.ProjectName },
        { "IsDefault", c.IsDefault }
    }).ToList();

            updatedSettings["WebServiceConnections"] = connectionsList;
            var options = new JsonSerializerOptions { WriteIndented = true };
            var updatedJson = JsonSerializer.Serialize(updatedSettings, options);
            System.IO.File.WriteAllText(appSettingsPath, updatedJson);
        }

        #endregion

        #region Helper Methods

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
        private void UpdateAppSettings(DatabaseSettings settings)
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
                { "Password", settings.Password },
                { "SSLMode", settings.SSLMode }
            };
            updatedSettings["DatabaseSettings"] = dbSettings;
            var options = new JsonSerializerOptions { WriteIndented = true };
            var updatedJson = JsonSerializer.Serialize(updatedSettings, options);
            System.IO.File.WriteAllText(appSettingsPath, updatedJson);
        }
        private void UpdateSqlServerSettings(SqlServerSettings settings)
        {
            var appSettingsPath = Path.Combine(Directory.GetCurrentDirectory(), "appsettings.json");
            var json = System.IO.File.ReadAllText(appSettingsPath);
            var jsonSettings = JsonDocument.Parse(json);
            var updatedSettings = new Dictionary<string, object>();
            foreach (var element in jsonSettings.RootElement.EnumerateObject())
            {
                updatedSettings[element.Name] = JsonSerializer.Deserialize<object>(element.Value.GetRawText());
            }
            var sqlSettings = new Dictionary<string, string>
            {
                { "Host", settings.Host },
                { "Port", settings.Port },
                { "Database", settings.Database },
                { "Username", settings.Username },
                { "Password", settings.Password },
                { "ProjectName", settings.ProjectName }
            };
            updatedSettings["SqlServerSettings"] = sqlSettings;
            var options = new JsonSerializerOptions { WriteIndented = true };
            var updatedJson = JsonSerializer.Serialize(updatedSettings, options);
            System.IO.File.WriteAllText(appSettingsPath, updatedJson);
        }

        #endregion
    }

    #region Request Models
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
    public class WebServiceConnectionTestRequest
    {
        public string ConnectionId { get; set; } = "";
        public string ConnectionName { get; set; } = "";
        public string BaseUrl { get; set; } = "";
        public string ClientId { get; set; } = "";
        public string ClientSecret { get; set; } = "";
        public string ApiKey { get; set; } = "";
        public string Username { get; set; } = "";  
        public string Password { get; set; } = "";  
        public int AuthType { get; set; } = 0;
        public int TimeoutSeconds { get; set; } = 30;
        public string ProjectName { get; set; } = "";
    }

    public class SaveConnectionsRequest
    {
        public List<Dictionary<string, string>> Connections { get; set; } = new List<Dictionary<string, string>>();
    }
    public class SaveWebServiceConnectionsRequest
    {
        public List<Dictionary<string, string>> Connections { get; set; } = new List<Dictionary<string, string>>();
    }

    public class DeleteConnectionRequest
    {
        public string ConnectionId { get; set; } = "";
    }

    #endregion
}