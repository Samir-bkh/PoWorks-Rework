using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using PoWorks_Rework.Models;
using Microsoft.Extensions.Logging;

namespace PoWorks_Rework.Services
{
    public class SqlServerService
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<SqlServerService> _logger;
        private SqlServerSettings _currentSettings;
        private bool _isInitialized = false;
        private SqlServerConnectionCollection _connectionCollection;

        public SqlServerService(IConfiguration configuration, ILogger<SqlServerService> logger)
        {
            _configuration = configuration;
            _logger = logger;
            _connectionCollection = new SqlServerConnectionCollection();
            // Load settings from configuration initially
            LoadSettingsFromConfig();
        }

        public SqlServerSettings CurrentSettings => _currentSettings;
        public bool IsInitialized => _isInitialized;

        public SqlConnection GetConnection()
        {
            if (!_isInitialized)
                throw new InvalidOperationException("SQL Server connection is not initialized.");

            return new SqlConnection(_currentSettings.ToConnectionString());
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

        // FIND the LoadSettingsFromConfig method and REPLACE with:
        private void LoadSettingsFromConfig()
        {
            try
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

                    if (!string.IsNullOrEmpty(legacyConnection.Database))
                    {
                        connections.Add(legacyConnection);
                    }
                }

                // Initialize the connection collection
                _connectionCollection = new SqlServerConnectionCollection();
                foreach (var connection in connections)
                {
                    _connectionCollection.AddConnection(connection);
                }

                _isInitialized = connections.Any(c => !string.IsNullOrEmpty(c.Database));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading SQL Server settings from configuration");
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

                    // SQL to get all tables in the database
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

            // If no tables found, add some sample tables for development/testing
            if (tables.Count == 0)
            {
                tables.Add("HDS_RAW_DATA");
                tables.Add("HDS_DAILY");
                tables.Add("HDS_MONTHLY");
                tables.Add("HDS_ARCHIVE");
            }

            return tables;
        }

        // Services/SqlServerService.cs - Fixed GetDistinctMeterNames method with corrected SQL syntax
        public async Task<List<HDSMeterItem>> GetDistinctMeterNames(string tableName, int? limit = null)
        {
            if (!IsInitialized)
                throw new InvalidOperationException("SQL Server connection is not initialized.");

            var meters = new List<HDSMeterItem>();

            try
            {
                // Validate the table name to prevent SQL injection
                if (!IsValidTableName(tableName))
                {
                    throw new ArgumentException("Invalid table name format");
                }

                using (var connection = new SqlConnection(_currentSettings.ToConnectionString()))
                {
                    await connection.OpenAsync();

                    // Build the SQL query with proper syntax for SQL Server
                    string sql;
                    if (limit.HasValue && limit.Value > 0)
                    {
                        // Use subquery approach for TOP with DISTINCT
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

                    _logger.LogInformation($"Executing SQL query with limit {limit}: {sql}");

                    using (var command = new SqlCommand(sql, connection))
                    {
                        // Set command timeout to handle large datasets
                        command.CommandTimeout = 60; // 60 seconds

                        using (var reader = await command.ExecuteReaderAsync())
                        {
                            while (await reader.ReadAsync())
                            {
                                var meterName = reader.GetString(0);

                                // Skip empty or whitespace-only meter names
                                if (!string.IsNullOrWhiteSpace(meterName))
                                {
                                    meters.Add(new HDSMeterItem
                                    {
                                        HdsMeterName = meterName.Trim(),
                                        Type = "Main", // Default to Main
                                        Active = true,
                                        IsSelected = true
                                    });
                                }
                            }
                        }
                    }
                }

                _logger.LogInformation($"Found {meters.Count} distinct meter names in table {tableName} (limit: {limit})");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error getting distinct meter names from table {tableName} with limit {limit}");

                // Log the specific SQL error for debugging
                if (ex.Message.Contains("Incorrect syntax"))
                {
                    _logger.LogError($"SQL Syntax Error - Table name: {tableName}, Limit: {limit}");
                }

                throw;
            }

            // If no meters found, create sample meters for development/testing
            if (meters.Count == 0)
            {
                _logger.LogWarning($"No meters found in table {tableName}, creating sample meters for development");

                // Apply limit to sample data as well
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

        // Enhanced table name validation
        private bool IsValidTableName(string tableName)
        {
            if (string.IsNullOrWhiteSpace(tableName))
                return false;

            // Remove brackets if present for validation
            var cleanTableName = tableName.Trim('[', ']');

            // Allow alphanumeric characters, underscores, and dots (for schema.table format)
            // Also allow spaces if the table name will be bracketed
            return System.Text.RegularExpressions.Regex.IsMatch(
                cleanTableName, @"^[a-zA-Z0-9_\s\.]+$");
        }

        public SqlConnection GetConnection(string connectionId = null)
        {
            if (!_isInitialized)
                throw new InvalidOperationException("SQL Server connection is not initialized.");

            var settings = string.IsNullOrEmpty(connectionId)
                ? _connectionCollection.GetDefaultConnection()
                : _connectionCollection.GetConnection(connectionId);

            if (settings == null)
                throw new InvalidOperationException($"SQL Server connection '{connectionId}' not found.");

            return new SqlConnection(settings.ToConnectionString());
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

        // Alternative method to get table schema and validate table exists
        public async Task<bool> ValidateTableExists(string tableName)
        {
            if (!IsInitialized)
                return false;

            try
            {
                using (var connection = new SqlConnection(_currentSettings.ToConnectionString()))
                {
                    await connection.OpenAsync();

                    // Check if table exists in the database
                    string sql = @"
                SELECT COUNT(*) 
                FROM INFORMATION_SCHEMA.TABLES 
                WHERE TABLE_NAME = @TableName 
                AND TABLE_TYPE = 'BASE TABLE'";

                    using (var command = new SqlCommand(sql, connection))
                    {
                        // Extract just the table name without schema or brackets
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
                _logger.LogError(ex, $"Error validating table existence for {tableName}");
                return false;
            }
        }
    }
}