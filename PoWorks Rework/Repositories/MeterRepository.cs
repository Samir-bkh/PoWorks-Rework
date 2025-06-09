// Repositories/MeterRepository.cs
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using Npgsql;
using PoWorks_Rework.Models;
using PoWorks_Rework.Services;
using Microsoft.Extensions.Logging;

namespace PoWorks_Rework.Repositories
{
    public class MeterRepository
    {
        private readonly DatabaseService _databaseService;
        private readonly ILogger<MeterRepository> _logger;

        public MeterRepository(DatabaseService databaseService, ILogger<MeterRepository> logger)
        {
            _databaseService = databaseService;
            _logger = logger;
        }

        public async Task<List<Meter>> GetMetersAsync(MeterSearchCriteria criteria, int page = 1, int pageSize = 10)
        {
            _logger.LogInformation("Getting meters with criteria: {SearchField}={SearchTerm}, Page={Page}, PageSize={PageSize}",
                criteria.SearchField, criteria.SearchTerm, page, pageSize);

            var meters = new List<Meter>();

            try
            {
                // Create a brand new connection for this operation
                using (var connection = new NpgsqlConnection(_databaseService.GetConnectionString()))
                {
                    await connection.OpenAsync();
                    _logger.LogInformation("Database connection opened for GetMetersAsync");

                    var whereClause = string.Empty;
                    if (!string.IsNullOrEmpty(criteria.SearchTerm))
                    {
                        switch (criteria.SearchField)
                        {
                            case "Name":
                                whereClause = @" WHERE (m.""Name"" ILIKE @SearchTerm OR m.""Label"" ILIKE @SearchTerm)";
                                break;
                            case "Type":
                                whereClause = @" WHERE m.""Type"" ILIKE @SearchTerm";
                                break;
                         
                            case "Tenant":
                                whereClause = @" WHERE m.""TenantID"" IN (SELECT ""TenantID"" FROM ""Tenants"" WHERE ""DisplayName"" ILIKE @SearchTerm)";
                                break;
                        }
                    }

                    string sql = $@"
                                SELECT m.""MeterId"", m.""Name"", m.""Label"", m.""Unit"", m.""ParentId"", p.""Name"" AS ""ParentName"",
                                m.""LastReading"", m.""Type"", m.""Active"", m.""TenantID"", t.""DisplayName"" AS ""TenantName""
                                FROM ""Meters"" m
                                LEFT JOIN ""Meters"" p ON m.""ParentId"" = p.""MeterId""
                                LEFT JOIN ""Tenants"" t ON m.""TenantID"" = t.""TenantID""
                                {whereClause}
                                ORDER BY m.""Name""
                                LIMIT @PageSize OFFSET @Offset";

                    _logger.LogInformation("Executing SQL: {SQL}", sql);

                    using var cmd = new NpgsqlCommand(sql, connection);

                    if (!string.IsNullOrEmpty(criteria.SearchTerm))
                    {
                        cmd.Parameters.AddWithValue("@SearchTerm", $"%{criteria.SearchTerm}%");
                    }

                    cmd.Parameters.AddWithValue("@PageSize", pageSize);
                    cmd.Parameters.AddWithValue("@Offset", (page - 1) * pageSize);

                    using var reader = await cmd.ExecuteReaderAsync();

                    while (await reader.ReadAsync())
                    {
                        meters.Add(new Meter
                        {
                            Id = reader.GetInt32(reader.GetOrdinal("MeterId")),
                            Name = reader.GetString(reader.GetOrdinal("Name")),
                            Label = reader.IsDBNull(reader.GetOrdinal("Label")) ? null : reader.GetString(reader.GetOrdinal("Label")),
                            Unit = reader.IsDBNull(reader.GetOrdinal("Unit")) ? "" : reader.GetString(reader.GetOrdinal("Unit")),
                            ParentMeterId = reader.IsDBNull(reader.GetOrdinal("ParentId")) ? null : reader.GetInt32(reader.GetOrdinal("ParentId")).ToString(),
                            ParentMeterName = reader.IsDBNull(reader.GetOrdinal("ParentName")) ? null : reader.GetString(reader.GetOrdinal("ParentName")),
                            LastReading = reader.GetInt32(reader.GetOrdinal("LastReading")).ToString(),
                            Type = reader.GetString(reader.GetOrdinal("Type")).First().ToString().ToUpper() + reader.GetString(reader.GetOrdinal("Type")).Substring(1),
                            TenantId = reader.IsDBNull(reader.GetOrdinal("TenantID")) ? null : reader.GetInt32(reader.GetOrdinal("TenantID")).ToString(),
                            TenantName = reader.IsDBNull(reader.GetOrdinal("TenantName")) ? null : reader.GetString(reader.GetOrdinal("TenantName")),
                            Active = reader.GetBoolean(reader.GetOrdinal("Active"))
                        });
                    }

                    _logger.LogInformation("Retrieved {Count} meters", meters.Count);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting meters");
                throw;
            }

            return meters;
        }

        public async Task<int> GetTotalMetersCountAsync(MeterSearchCriteria criteria)
        {
            _logger.LogInformation("Getting total meters count with criteria: {SearchField}={SearchTerm}",
                criteria.SearchField, criteria.SearchTerm);

            try
            {
                // Create a brand new connection for this operation
                using (var connection = new NpgsqlConnection(_databaseService.GetConnectionString()))
                {
                    await connection.OpenAsync();
                    _logger.LogInformation("Database connection opened for GetTotalMetersCountAsync");

                    var whereClause = string.Empty;
                    if (!string.IsNullOrEmpty(criteria.SearchTerm))
                    {
                        switch (criteria.SearchField)
                        {
                            case "Name":
                                whereClause = @" WHERE (m.""Name"" ILIKE @SearchTerm OR m.""Label"" ILIKE @SearchTerm)";
                                break;
                            case "Type":
                                whereClause = @" WHERE m.""Type"" ILIKE @SearchTerm";
                                break;
                          
                            case "Tenant":
                                whereClause = @" WHERE m.""TenantID"" IN (SELECT ""TenantID"" FROM ""Tenants"" WHERE ""DisplayName"" ILIKE @SearchTerm)";
                                break;
                        }
                    }

                    string sql = $@"SELECT COUNT(*) FROM ""Meters"" m {whereClause}";
                    _logger.LogInformation("Executing SQL: {SQL}", sql);

                    using var cmd = new NpgsqlCommand(sql, connection);

                    if (!string.IsNullOrEmpty(criteria.SearchTerm))
                    {
                        cmd.Parameters.AddWithValue("@SearchTerm", $"%{criteria.SearchTerm}%");
                    }

                    var result = await cmd.ExecuteScalarAsync();
                    int count = Convert.ToInt32(result);
                    _logger.LogInformation("Total meters count: {Count}", count);
                    return count;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting total meters count");
                throw;
            }
        }

        public async Task<Meter> GetMeterByIdAsync(int meterId)
        {
            _logger.LogInformation("Getting meter by ID: {MeterId}", meterId);

            try
            {
                using (var connection = new NpgsqlConnection(_databaseService.GetConnectionString()))
                {
                    await connection.OpenAsync();

                    string sql = @"
                SELECT m.""MeterId"", m.""Name"", m.""Label"", m.""Unit"", m.""ParentId"", p.""Name"" AS ""ParentName"",
                       m.""LastReading"", m.""Type"", m.""Active"", m.""TenantID"", t.""DisplayName"" AS ""TenantName""
                FROM ""Meters"" m
                LEFT JOIN ""Meters"" p ON m.""ParentId"" = p.""MeterId""
                LEFT JOIN ""Tenants"" t ON m.""TenantID"" = t.""TenantID""
                WHERE m.""MeterId"" = @MeterId";

                    using var cmd = new NpgsqlCommand(sql, connection);
                    cmd.Parameters.AddWithValue("@MeterId", meterId);

                    using var reader = await cmd.ExecuteReaderAsync();

                    if (await reader.ReadAsync())
                    {
                        var meter = new Meter
                        {
                            Id = reader.GetInt32(reader.GetOrdinal("MeterId")),
                            Name = reader.GetString(reader.GetOrdinal("Name")),
                            Label = reader.IsDBNull(reader.GetOrdinal("Label")) ? null : reader.GetString(reader.GetOrdinal("Label")), // Add Label
                            Unit = reader.IsDBNull(reader.GetOrdinal("Unit")) ? "" : reader.GetString(reader.GetOrdinal("Unit")),
                            ParentMeterId = reader.IsDBNull(reader.GetOrdinal("ParentId")) ? null : reader.GetInt32(reader.GetOrdinal("ParentId")).ToString(),
                            ParentMeterName = reader.IsDBNull(reader.GetOrdinal("ParentName")) ? null : reader.GetString(reader.GetOrdinal("ParentName")),
                            LastReading = reader.GetInt32(reader.GetOrdinal("LastReading")).ToString(),
                            Type = reader.GetString(reader.GetOrdinal("Type")).First().ToString().ToUpper() + reader.GetString(reader.GetOrdinal("Type")).Substring(1),
                            TenantId = reader.IsDBNull(reader.GetOrdinal("TenantID")) ? null : reader.GetInt32(reader.GetOrdinal("TenantID")).ToString(),
                            TenantName = reader.IsDBNull(reader.GetOrdinal("TenantName")) ? null : reader.GetString(reader.GetOrdinal("TenantName")),
                            Active = reader.GetBoolean(reader.GetOrdinal("Active"))
                        };

                        return meter;
                    }

                    return null;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting meter by ID {MeterId}", meterId);
                throw;
            }
        }

        public async Task<List<Meter>> GetSubMetersAsync(int parentMeterId)
        {
            var meters = new List<Meter>();

            try
            {
                using (var connection = new NpgsqlConnection(_databaseService.GetConnectionString()))
                {
                    await connection.OpenAsync();

                    // FIX: Use lowercase 'sub' to match database constraint
                    string sql = @"
                SELECT m.""MeterId"", m.""Name"", m.""Label"", m.""Unit"", m.""ParentId"", p.""Name"" AS ""ParentName"",
                       m.""LastReading"", m.""Type"", m.""Active"", m.""TenantID"", t.""DisplayName"" AS ""TenantName""
                FROM ""Meters"" m
                LEFT JOIN ""Meters"" p ON m.""ParentId"" = p.""MeterId""
                LEFT JOIN ""Tenants"" t ON m.""TenantID"" = t.""TenantID""
                WHERE m.""ParentId"" = @ParentId AND m.""Type"" = 'sub'";

                    using var cmd = new NpgsqlCommand(sql, connection);
                    cmd.Parameters.AddWithValue("@ParentId", parentMeterId);

                    using var reader = await cmd.ExecuteReaderAsync();

                    while (await reader.ReadAsync())
                    {
                        meters.Add(new Meter
                        {
                            Id = reader.GetInt32(reader.GetOrdinal("MeterId")),
                            Name = reader.GetString(reader.GetOrdinal("Name")),
                            Label = reader.IsDBNull(reader.GetOrdinal("Label")) ? null : reader.GetString(reader.GetOrdinal("Label")),
                            Unit = reader.IsDBNull(reader.GetOrdinal("Unit")) ? "" : reader.GetString(reader.GetOrdinal("Unit")),
                            ParentMeterId = reader.IsDBNull(reader.GetOrdinal("ParentId")) ? null : reader.GetInt32(reader.GetOrdinal("ParentId")).ToString(),
                            ParentMeterName = reader.IsDBNull(reader.GetOrdinal("ParentName")) ? null : reader.GetString(reader.GetOrdinal("ParentName")),
                            LastReading = reader.GetInt32(reader.GetOrdinal("LastReading")).ToString(),
                            Type = reader.GetString(reader.GetOrdinal("Type")).First().ToString().ToUpper() + reader.GetString(reader.GetOrdinal("Type")).Substring(1),
                            TenantId = reader.IsDBNull(reader.GetOrdinal("TenantID")) ? null : reader.GetInt32(reader.GetOrdinal("TenantID")).ToString(),
                            TenantName = reader.IsDBNull(reader.GetOrdinal("TenantName")) ? null : reader.GetString(reader.GetOrdinal("TenantName")),
                            Active = reader.GetBoolean(reader.GetOrdinal("Active"))
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting sub meters for parent {ParentMeterId}", parentMeterId);
            }

            _logger.LogInformation("Retrieved {Count} sub meters for parent meter ID {ParentMeterId}", meters.Count, parentMeterId);
            return meters;
        }
    }
}