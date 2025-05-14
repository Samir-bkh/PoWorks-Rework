// Services/SqlServerService.cs
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using PoWorks_Rework.Models;
using System;

namespace PoWorks_Rework.Services
{
    public class SqlServerService
    {
        private readonly IConfiguration _configuration;
        private SqlServerSettings _currentSettings;
        private bool _isInitialized = false;

        public SqlServerService(IConfiguration configuration)
        {
            _configuration = configuration;
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


        // Services/SqlServerService.cs - Add this method
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

                throw;
            }

            return tables;
        }
    }
}