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

        public SqlServerService(IConfiguration configuration, ILogger<SqlServerService> logger)
        {
            _configuration = configuration;
            _logger = logger;
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

        private void LoadSettingsFromConfig()
        {
            _currentSettings = new SqlServerSettings
            {
                Host = _configuration["SqlServerSettings:Host"] ?? "localhost",
                Port = _configuration["SqlServerSettings:Port"] ?? "1433",
                Database = _configuration["SqlServerSettings:Database"] ?? "",
                Username = _configuration["SqlServerSettings:Username"] ?? "",
                Password = _configuration["SqlServerSettings:Password"] ?? "",
                ProjectName = _configuration["SqlServerSettings:ProjectName"] ?? ""
            };

            // Check if we have a valid database configuration
            if (!string.IsNullOrEmpty(_currentSettings.Database))
            {
                _isInitialized = true;
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