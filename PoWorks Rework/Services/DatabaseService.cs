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

        // =========================================================================
        // NOUVELLES MÉTHODES POUR LE MULTI-TENANT (SaaS) ET LA SÉCURITÉ RLS
        // =========================================================================

        /// <summary>
        /// Exécute une opération base de données (avec retour de type T) isolée pour une Company spécifique.
        /// </summary>
        public async Task<T> ExecuteWithCompanyIsolationAsync<T>(int companyId, Func<NpgsqlConnection, NpgsqlTransaction, Task<T>> action)
        {
            await using var connection = CreateNewConnection();
            await connection.OpenAsync();

            // On ouvre obligatoirement une transaction pour que le "set_config" survive
            await using var transaction = await connection.BeginTransactionAsync();

            try
            {
                // 1. Définir le CompanyId pour PostgreSQL (Active la RLS)
                // Le paramètre "true" à la fin signifie que c'est une variable locale à la transaction
                await using (var cmd = new NpgsqlCommand("SELECT set_config('app.current_company_id', @id::text, true);", connection, transaction))
                {
                    cmd.Parameters.AddWithValue("id", companyId.ToString());
                    await cmd.ExecuteNonQueryAsync();
                }

                // 2. Exécuter la vraie requête C# (celle qui sera passée en paramètre)
                var result = await action(connection, transaction);

                // 3. Valider la transaction si tout s'est bien passé
                await transaction.CommitAsync();

                return result;
            }
            catch
            {
                // En cas de problème, on annule tout pour ne rien corrompre
                await transaction.RollbackAsync();
                throw;
            }
        }

        /// <summary>
        /// Exécute une opération base de données (sans retour) isolée pour une Company spécifique.
        /// </summary>
        public async Task ExecuteWithCompanyIsolationAsync(int companyId, Func<NpgsqlConnection, NpgsqlTransaction, Task> action)
        {
            await using var connection = CreateNewConnection();
            await connection.OpenAsync();

            await using var transaction = await connection.BeginTransactionAsync();

            try
            {
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