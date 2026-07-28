using Microsoft.Data.SqlClient;
using PoWorks_Rework.Models;
using Npgsql; 

namespace PoWorks_Rework.Services
{
    public class SqlServerService
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<SqlServerService> _logger;
        private readonly EncryptionService _encryptionService;
        private SqlServerSettings _currentSettings;
        private bool _isInitialized = false;
        private SqlServerConnectionCollection _connectionCollection;

        public SqlServerService(IConfiguration configuration, ILogger<SqlServerService> logger, EncryptionService encryptionService)
        {
            _configuration = configuration;
            _logger = logger;
            _encryptionService = encryptionService;
            _connectionCollection = new SqlServerConnectionCollection();

       
            LoadSettingsFromDatabase();
        }

        public SqlServerSettings CurrentSettings => _currentSettings;
        public bool IsInitialized => _isInitialized;

        public SqlConnection GetConnection(string connectionId = null)
        {
            if (!_isInitialized)
                throw new InvalidOperationException("SQL Server connection is not initialized.");

            SqlServerSettings settings;

            if (_connectionCollection != null && _connectionCollection.Connections.Any())
            {
                settings = string.IsNullOrEmpty(connectionId)
                    ? _connectionCollection.GetDefaultConnection()
                    : _connectionCollection.GetConnection(connectionId);

                if (settings == null)
                    throw new InvalidOperationException($"SQL Server connection '{connectionId ?? "default"}' not found.");
            }
            else
            {
                settings = _currentSettings;
                if (settings == null)
                    throw new InvalidOperationException("SQL Server connection is not initialized.");
            }

            return new SqlConnection(settings.ToConnectionString());
        }

        public void Initialize(SqlServerSettings settings)
        {
            _currentSettings = settings;
            _isInitialized = true;
        }

        public bool RemoveConnection(string connectionId)
        {
            try
            {
                if (_connectionCollection.Connections.Count <= 1)
                {
                    _logger.LogWarning("Cannot remove the last SQL Server connection");
                    return false;
                }

                var connectionToRemove = _connectionCollection.GetConnection(connectionId);
                if (connectionToRemove != null)
                {
                    _connectionCollection.RemoveConnection(connectionId);
                    _isInitialized = _connectionCollection.Connections.Any();

                    _logger.LogInformation($"Removed SQL Server connection '{connectionToRemove.ConnectionName}' (ID: {connectionId})");
                    return true;
                }

                _logger.LogWarning($"SQL Server connection with ID '{connectionId}' not found");
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error removing SQL Server connection '{connectionId}'");
                return false;
            }
        }

        public async Task<List<string>> GetAvailableTables(string connectionId = null)
        {
            if (!IsInitialized)
                throw new InvalidOperationException("SQL Server connection is not initialized.");

            var tables = new List<string>();

            try
            {
                using (var connection = GetConnection(connectionId))
                {
                    await connection.OpenAsync();
                    string sql = @"
                SELECT TABLE_NAME 
                FROM INFORMATION_SCHEMA.TABLES 
                WHERE TABLE_TYPE = 'BASE TABLE'
                AND TABLE_SCHEMA = 'dbo'
                ORDER BY TABLE_NAME";

                    using (var command = new SqlCommand(sql, connection))
                    {
                        using (var reader = await command.ExecuteReaderAsync())
                        {
                            while (await reader.ReadAsync())
                            {
                                tables.Add(reader.GetString(0));
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting available tables from SQL Server connection '{ConnectionId}'", connectionId ?? "default");
                throw;
            }

            if (tables.Count == 0)
            {
                tables.Add("HDS_RAW_DATA");
                tables.Add("HDS_DAILY");
                tables.Add("HDS_MONTHLY");
                tables.Add("HDS_ARCHIVE");
            }

            return tables;
        }

        public void LoadSettingsFromDatabase()
        {
            try
            {
                var connections = new List<SqlServerSettings>();

                var host = _configuration["DatabaseSettings:Host"];
                var db = _configuration["DatabaseSettings:Database"];

                if (string.IsNullOrEmpty(host) || string.IsNullOrEmpty(db))
                {
                    _isInitialized = false;
                    return;
                }

                var port = _configuration["DatabaseSettings:Port"] ?? "5432";
                var user = _configuration["DatabaseSettings:Username"] ?? "postgres";
                var pass = _encryptionService.Decrypt(_configuration["DatabaseSettings:Password"] ?? "");

                var pgConnectionString = $"Host={host};Port={port};Database={db};Username={user};Password={pass};";

                using (var conn = new NpgsqlConnection(pgConnectionString))
                {
                    conn.Open();

                    using (var checkCmd = new NpgsqlCommand("SELECT EXISTS (SELECT FROM information_schema.tables WHERE table_name = 'SqlServerConnections')", conn))
                    {
                        bool exists = (bool)checkCmd.ExecuteScalar();
                        if (exists)
                        {
                            using (var cmd = new NpgsqlCommand("SELECT * FROM \"SqlServerConnections\" ORDER BY \"Id\"", conn))
                            using (var reader = cmd.ExecuteReader())
                            {
                                while (reader.Read())
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
                            }
                        }
                    }
                }

                _connectionCollection = new SqlServerConnectionCollection();
                foreach (var connection in connections)
                {
                    _connectionCollection.AddConnection(connection);
                }

                _isInitialized = connections.Any();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "PostgreSQL database might not be initialized yet. SQL Server connections skipped.");
                _connectionCollection = new SqlServerConnectionCollection();
                _isInitialized = false;
            }
        }

        public async Task<List<string>> GetAvailableTables()
        {
            if (!IsInitialized)
                throw new InvalidOperationException("SQL Server connection is not initialized.");

            var tables = new List<string>();

            try
            {
                using (var connection = new SqlConnection(_currentSettings.ToConnectionString()))
                {
                    await connection.OpenAsync();
                    string sql = @"
                SELECT TABLE_NAME 
                FROM INFORMATION_SCHEMA.TABLES 
                WHERE TABLE_TYPE = 'BASE TABLE'
                AND TABLE_SCHEMA = 'dbo'
                ORDER BY TABLE_NAME";

                    using (var command = new SqlCommand(sql, connection))
                    {
                        using (var reader = await command.ExecuteReaderAsync())
                        {
                            while (await reader.ReadAsync())
                            {
                                tables.Add(reader.GetString(0));
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting available tables from SQL Server");
                throw;
            }

            if (tables.Count == 0)
            {
                tables.Add("HDS_RAW_DATA");
                tables.Add("HDS_DAILY");
                tables.Add("HDS_MONTHLY");
                tables.Add("HDS_ARCHIVE");
            }

            return tables;
        }

        public async Task<List<HDSMeterItem>> GetDistinctMeterNames(string tableName, int? limit = null, string connectionId = null)
        {
            if (!IsInitialized)
                throw new InvalidOperationException("SQL Server connection is not initialized.");

            var meters = new List<HDSMeterItem>();

            try
            {
                if (!IsValidTableName(tableName))
                {
                    throw new ArgumentException("Invalid table name format");
                }

                using (var connection = GetConnection(connectionId))
                {
                    await connection.OpenAsync();
                    string sql;
                    if (limit.HasValue && limit.Value > 0)
                    {
                        sql = $@"
                    SELECT TOP ({limit.Value}) NAME 
                    FROM (
                        SELECT DISTINCT NAME 
                        FROM [{tableName}]
                        WHERE NAME IS NOT NULL
                    ) AS DistinctNames
                    ORDER BY NAME";
                    }
                    else
                    {
                        sql = $@"
                    SELECT DISTINCT NAME 
                    FROM [{tableName}]
                    WHERE NAME IS NOT NULL
                    ORDER BY NAME";
                    }

                    _logger.LogInformation($"Executing SQL query on connection '{connectionId ?? "default"}' with limit {limit}: {sql}");

                    using (var command = new SqlCommand(sql, connection))
                    {
                        command.CommandTimeout = 60;

                        using (var reader = await command.ExecuteReaderAsync())
                        {
                            while (await reader.ReadAsync())
                            {
                                var meterName = reader.GetString(0);
                                if (!string.IsNullOrWhiteSpace(meterName))
                                {
                                    meters.Add(new HDSMeterItem
                                    {
                                        HdsMeterName = meterName.Trim(),
                                        Type = "Main",
                                        Active = true,
                                        IsSelected = true
                                    });
                                }
                            }
                        }
                    }
                }

                _logger.LogInformation($"Found {meters.Count} distinct meter names in table {tableName} on connection '{connectionId ?? "default"}' (limit: {limit})");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error getting distinct meter names from table {tableName} on connection '{connectionId ?? "default"}' with limit {limit}");
                if (ex.Message.Contains("Incorrect syntax"))
                {
                    _logger.LogError($"SQL Syntax Error - Table name: {tableName}, Connection: {connectionId ?? "default"}, Limit: {limit}");
                }

                throw;
            }
            if (meters.Count == 0)
            {
                _logger.LogWarning($"No meters found in table {tableName} on connection '{connectionId ?? "default"}', creating sample meters for development");
                int sampleCount = limit.HasValue && limit.Value > 0 ? Math.Min(limit.Value, 15) : 15;

                for (int i = 1; i <= sampleCount; i++)
                {
                    var prefix = i % 3 == 0 ? "FLOW_" : (i % 3 == 1 ? "PRESSURE_" : "TEMP_");
                    meters.Add(new HDSMeterItem
                    {
                        HdsMeterName = $"{prefix}{i:D2}",
                        Unit = i % 3 == 0 ? "m³/h" : (i % 3 == 1 ? "bar" : "°C"),
                        Type = "Main",
                        Active = true,
                        IsSelected = true,
                        LastReading = (1000 + i * 50).ToString()
                    });
                }
            }

            return meters;
        }

        private bool IsValidTableName(string tableName)
        {
            if (string.IsNullOrWhiteSpace(tableName))
                return false;
            var cleanTableName = tableName.Trim('[', ']');
            return System.Text.RegularExpressions.Regex.IsMatch(
                cleanTableName, @"^[a-zA-Z0-9_\s\.]+$");
        }


        public List<SqlServerSettings> GetAllConnections()
        {
            return _connectionCollection.Connections.ToList();
        }

        public void InitializeMultiple(List<SqlServerSettings> connections)
        {
            _connectionCollection = new SqlServerConnectionCollection();

            foreach (var connection in connections)
            {
                _connectionCollection.AddConnection(connection);
            }

            _isInitialized = connections.Any();
        }

        public async Task<bool> ValidateTableExists(string tableName, string connectionId = null)
        {
            if (!IsInitialized)
                return false;

            try
            {
                using (var connection = GetConnection(connectionId))
                {
                    await connection.OpenAsync();
                    string sql = @"
                SELECT COUNT(*) 
                FROM INFORMATION_SCHEMA.TABLES 
                WHERE TABLE_NAME = @TableName 
                AND TABLE_TYPE = 'BASE TABLE'";

                    using (var command = new SqlCommand(sql, connection))
                    {
                        var cleanTableName = tableName.Trim('[', ']');
                        if (cleanTableName.Contains("."))
                        {
                            cleanTableName = cleanTableName.Split('.').Last();
                        }

                        command.Parameters.AddWithValue("@TableName", cleanTableName);

                        var result = await command.ExecuteScalarAsync();
                        return Convert.ToInt32(result) > 0;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error validating table existence for {tableName} on connection '{connectionId ?? "default"}'");
                return false;
            }
        }
    }
}