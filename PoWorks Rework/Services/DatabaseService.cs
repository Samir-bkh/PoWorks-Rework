using Npgsql;
using PoWorks_Rework.Models;

namespace PoWorks_Rework.Services
{
    /// <summary>
    /// Manages PostgreSQL database connections and initialization.
    /// Handles connection pooling, configuration loading, and multi-tenant company isolation.
    /// </summary>
    public class DatabaseService
    {
        private readonly IConfiguration _configuration;
        private readonly EncryptionService _encryptionService; 
        private DatabaseSettings _currentSettings;
        private NpgsqlConnection _connection;
        private bool _isInitialized = false;

        /// <summary>
        /// Initializes the DatabaseService with configuration and encryption services.
        /// Automatically loads database settings from configuration file.
        /// </summary>
        public DatabaseService(IConfiguration configuration, EncryptionService encryptionService)
        {
            _configuration = configuration;
            _encryptionService = encryptionService;
            LoadSettingsFromConfig();
        }

        /// <summary>
        /// Gets the current database configuration settings
        /// </summary>
        public DatabaseSettings CurrentSettings => _currentSettings;

        /// <summary>
        /// Gets whether the database connection has been successfully initialized
        /// </summary>
        public bool IsInitialized => _isInitialized;

        /// <summary>
        /// Gets or creates a reusable database connection.
        /// Automatically opens closed connections and manages the connection state.
        /// </summary>
        public NpgsqlConnection GetConnection()
        {
            if (_connection == null || _connection.State == System.Data.ConnectionState.Closed)
            {
                _connection = new NpgsqlConnection(_currentSettings.ToConnectionString());
            }
            if (_connection.State == System.Data.ConnectionState.Closed)
            {
                _connection.Open();
            }

            return _connection;
        }

        /// <summary>
        /// Gets the connection string for the current database configuration.
        /// </summary>
        public string GetConnectionString()
        {
            return _currentSettings.ToConnectionString();
        }

        /// <summary>
        /// Reinitializes the database connection with new settings.
        /// Closes any existing connection before applying new settings.
        /// </summary>
        public void Initialize(DatabaseSettings settings)
        {
            _currentSettings = settings;
            _isInitialized = true;
            if (_connection != null && _connection.State != System.Data.ConnectionState.Closed)
            {
                _connection.Close();
                _connection = null;
            }
        }

        /// <summary>
        /// Loads database settings from the application configuration file.
        /// Decrypts the password using the encryption service.
        /// </summary>
        private void LoadSettingsFromConfig()
        {
            _currentSettings = new DatabaseSettings
            {
                Host = _configuration["DatabaseSettings:Host"] ?? "localhost",
                Port = _configuration["DatabaseSettings:Port"] ?? "5432",
                Database = _configuration["DatabaseSettings:Database"] ?? "",
                Username = _configuration["DatabaseSettings:Username"] ?? "postgres",
                Password = _encryptionService.Decrypt(_configuration["DatabaseSettings:Password"] ?? ""),
                SSLMode = _configuration["DatabaseSettings:SSLMode"] ?? "Prefer"
            };
            if (!string.IsNullOrEmpty(_currentSettings.Database))
            {
                _isInitialized = true;
            }
        }

        /// <summary>
        /// Creates a new database connection instance.
        /// Does not use the pooled connection - useful for parallel operations.
        /// </summary>
        public NpgsqlConnection CreateNewConnection()
        {
            return new NpgsqlConnection(_currentSettings.ToConnectionString());
        }

        /// <summary>
        /// Executes an action with company isolation and transaction support.
        /// Sets the PostgreSQL config variable for row-level security based on company ID.
        /// Returns a result from the executed action.
        /// </summary>
        public async Task<T> ExecuteWithCompanyIsolationAsync<T>(int companyId, Func<NpgsqlConnection, NpgsqlTransaction, Task<T>> action)
        {
            await using var connection = CreateNewConnection();
            await connection.OpenAsync();
            await using var transaction = await connection.BeginTransactionAsync();

            try
            {
                // Set company context for row-level security
                await using (var cmd = new NpgsqlCommand("SELECT set_config('app.current_company_id', @id::text, true);", connection, transaction))
                {
                    cmd.Parameters.AddWithValue("id", companyId.ToString());
                    await cmd.ExecuteNonQueryAsync();
                }
                var result = await action(connection, transaction);
                await transaction.CommitAsync();

                return result;
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

        /// <summary>
        /// Executes an action with company isolation and transaction support.
        /// Sets the PostgreSQL config variable for row-level security based on company ID.
        /// Does not return a result.
        /// </summary>
        public async Task ExecuteWithCompanyIsolationAsync(int companyId, Func<NpgsqlConnection, NpgsqlTransaction, Task> action)
        {
            await using var connection = CreateNewConnection();
            await connection.OpenAsync();

            await using var transaction = await connection.BeginTransactionAsync();

            try
            {
                // Set company context for row-level security
                await using (var cmd = new NpgsqlCommand("SELECT set_config('app.current_company_id', @id::text, true);", connection, transaction))
                {
                    cmd.Parameters.AddWithValue("id", companyId.ToString());
                    await cmd.ExecuteNonQueryAsync();
                }

                await action(connection, transaction);

                await transaction.CommitAsync();
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }
    }
}