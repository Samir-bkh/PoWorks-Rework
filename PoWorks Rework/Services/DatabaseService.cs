using Npgsql;
using PoWorks_Rework.Models;

namespace PoWorks_Rework.Services
{
    public class DatabaseService
    {
        private readonly IConfiguration _configuration;
        private DatabaseSettings _currentSettings;
        private NpgsqlConnection _connection;
        private bool _isInitialized = false;

        public DatabaseService(IConfiguration configuration)
        {
            _configuration = configuration;
            // Load settings from configuration initially
            LoadSettingsFromConfig();
        }

        public DatabaseSettings CurrentSettings => _currentSettings;
        public bool IsInitialized => _isInitialized;

        public NpgsqlConnection GetConnection()
        {
            if (_connection == null || _connection.State == System.Data.ConnectionState.Closed)
            {
                _connection = new NpgsqlConnection(_currentSettings.ToConnectionString());
            }

            // Only open the connection if it's closed
            if (_connection.State == System.Data.ConnectionState.Closed)
            {
                _connection.Open();
            }

            return _connection;
        }

        public string GetConnectionString()
        {
            return _currentSettings.ToConnectionString();
        }

        public void Initialize(DatabaseSettings settings)
        {
            _currentSettings = settings;
            _isInitialized = true;
            // Close existing connection if any
            if (_connection != null && _connection.State != System.Data.ConnectionState.Closed)
            {
                _connection.Close();
                _connection = null;
            }
        }

        private void LoadSettingsFromConfig()
        {
            _currentSettings = new DatabaseSettings
            {
                Host = _configuration["DatabaseSettings:Host"] ?? "localhost",
                Port = _configuration["DatabaseSettings:Port"] ?? "5432",
                Database = _configuration["DatabaseSettings:Database"] ?? "",
                Username = _configuration["DatabaseSettings:Username"] ?? "postgres",
                Password = _configuration["DatabaseSettings:Password"] ?? "",
                SSLMode = _configuration["DatabaseSettings:SSLMode"] ?? "Prefer"
            };
            // Check if we have a valid database configuration
            if (!string.IsNullOrEmpty(_currentSettings.Database))
            {
                _isInitialized = true;
            }
        }

        public NpgsqlConnection CreateNewConnection()
        {
            return new NpgsqlConnection(_currentSettings.ToConnectionString());
        }
    }


}