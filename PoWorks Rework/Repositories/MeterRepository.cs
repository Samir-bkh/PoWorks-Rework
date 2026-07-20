using Npgsql;
using PoWorks_Rework.Models;
using PoWorks_Rework.Services;

namespace PoWorks_Rework.Repositories
{
    public class MeterRepository
    {
        private readonly DatabaseService _databaseService;
        private readonly ICompanyContext _companyContext; // <--- AJOUT
        private readonly ILogger<MeterRepository> _logger;

        public MeterRepository(DatabaseService databaseService, ICompanyContext companyContext, ILogger<MeterRepository> logger)
        {
            _databaseService = databaseService;
            _companyContext = companyContext; // <--- AJOUT
            _logger = logger;
        }


        public async Task<List<Meter>> GetMetersAsync(MeterSearchCriteria criteria, int page = 1, int pageSize = 10)
        {
            int currentCompanyId = _companyContext.CurrentCompanyId;
            return await _databaseService.ExecuteWithCompanyIsolationAsync(currentCompanyId, async (connection, transaction) =>
            {
                var meters = new List<Meter>();
                var whereClause = string.Empty;
                if (!string.IsNullOrEmpty(criteria.SearchTerm))
                {
                    switch (criteria.SearchField)
                    {
                        case "Name":
                            whereClause = @" AND (m.""Name"" ILIKE @SearchTerm OR m.""Label"" ILIKE @SearchTerm)";
                            break;
                        case "Type":
                            whereClause = @" AND m.""Type"" ILIKE @SearchTerm";
                            break;
                        case "Tenant":
                            whereClause = @" AND m.""TenantID"" IN (SELECT ""TenantID"" FROM ""Tenants"" WHERE ""DisplayName"" ILIKE @SearchTerm)";
                            break;
                    }
                }

                string sql = $@"
                    SELECT m.""MeterId"", m.""Name"", m.""Label"", m.""Unit"", m.""ParentId"", p.""Name"" AS ""ParentName"",
                    m.""LastReading"", m.""Type"", m.""Active"", m.""TenantID"", t.""DisplayName"" AS ""TenantName""
                    FROM ""Meters"" m
                    LEFT JOIN ""Meters"" p ON m.""ParentId"" = p.""MeterId""
                    LEFT JOIN ""Tenants"" t ON m.""TenantID"" = t.""TenantID""
                    WHERE 1=1 {whereClause}
                    ORDER BY m.""Name""
                    LIMIT @PageSize OFFSET @Offset";

                using var cmd = new NpgsqlCommand(sql, connection, transaction);
                if (!string.IsNullOrEmpty(criteria.SearchTerm))
                    cmd.Parameters.AddWithValue("@SearchTerm", $"%{criteria.SearchTerm}%");

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
                return meters;
            });
        }

        public async Task<int> GetTotalMetersCountAsync(MeterSearchCriteria criteria)
        {
            int currentCompanyId = _companyContext.CurrentCompanyId;
            return await _databaseService.ExecuteWithCompanyIsolationAsync(currentCompanyId, async (connection, transaction) =>
            {
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
                using var cmd = new NpgsqlCommand(sql, connection, transaction);
                if (!string.IsNullOrEmpty(criteria.SearchTerm))
                    cmd.Parameters.AddWithValue("@SearchTerm", $"%{criteria.SearchTerm}%");

                var result = await cmd.ExecuteScalarAsync();
                return Convert.ToInt32(result);
            });
        }

        public async Task<Meter> GetMeterByIdAsync(int meterId)
        {
            int currentCompanyId = _companyContext.CurrentCompanyId;
            return await _databaseService.ExecuteWithCompanyIsolationAsync(currentCompanyId, async (connection, transaction) =>
            {
                string sql = @"
                SELECT m.""MeterId"", m.""Name"", m.""Label"", m.""Unit"", m.""ParentId"", p.""Name"" AS ""ParentName"",
                       m.""LastReading"", m.""Type"", m.""Active"", m.""TenantID"", t.""DisplayName"" AS ""TenantName""
                FROM ""Meters"" m
                LEFT JOIN ""Meters"" p ON m.""ParentId"" = p.""MeterId""
                LEFT JOIN ""Tenants"" t ON m.""TenantID"" = t.""TenantID""
                WHERE m.""MeterId"" = @MeterId";

                using var cmd = new NpgsqlCommand(sql, connection, transaction);
                cmd.Parameters.AddWithValue("@MeterId", meterId);

                using var reader = await cmd.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    return new Meter
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
                    };
                }
                return null;
            });
        }

        public async Task<List<Meter>> GetSubMetersAsync(int parentMeterId)
        {
            int currentCompanyId = _companyContext.CurrentCompanyId;
            return await _databaseService.ExecuteWithCompanyIsolationAsync(currentCompanyId, async (connection, transaction) =>
            {
                var meters = new List<Meter>();
                string sql = @"
                SELECT m.""MeterId"", m.""Name"", m.""Label"", m.""Unit"", m.""ParentId"", p.""Name"" AS ""ParentName"",
                       m.""LastReading"", m.""Type"", m.""Active"", m.""TenantID"", t.""DisplayName"" AS ""TenantName""
                FROM ""Meters"" m
                LEFT JOIN ""Meters"" p ON m.""ParentId"" = p.""MeterId""
                LEFT JOIN ""Tenants"" t ON m.""TenantID"" = t.""TenantID""
                WHERE m.""ParentId"" = @ParentId AND m.""Type"" = 'sub'";

                using var cmd = new NpgsqlCommand(sql, connection, transaction);
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
                return meters;
            });
        }

        public async Task<List<MeterForTrendsAnalysis>> GetWebServiceImportedMetersAsync(bool activeOnly = true, int limit = 0)
        {
            int currentCompanyId = _companyContext.CurrentCompanyId;
            return await _databaseService.ExecuteWithCompanyIsolationAsync(currentCompanyId, async (connection, transaction) =>
            {
                var meters = new List<MeterForTrendsAnalysis>();
                var whereConditions = new List<string> { @"(m.""Name"" LIKE '%.%' OR m.""Name"" LIKE 'varsets.%')" };
                if (activeOnly) whereConditions.Add(@"m.""Active"" = true");

                var whereClause = "WHERE " + string.Join(" AND ", whereConditions);
                var limitClause = limit > 0 ? $"LIMIT {limit}" : "";

                string sql = $@"
                SELECT m.""MeterId"", m.""Name"", m.""Label"", m.""Unit"", 
                       m.""Type"", m.""Active"", m.""TenantID"", 
                       t.""DisplayName"" AS ""TenantName""
                FROM ""Meters"" m
                LEFT JOIN ""Tenants"" t ON m.""TenantID"" = t.""TenantID""
                {whereClause}
                ORDER BY m.""Name""
                {limitClause}";

                using var cmd = new NpgsqlCommand(sql, connection, transaction);
                using var reader = await cmd.ExecuteReaderAsync();

                while (await reader.ReadAsync())
                {
                    meters.Add(new MeterForTrendsAnalysis
                    {
                        MeterId = reader.GetInt32(reader.GetOrdinal("MeterId")),
                        Name = reader.GetString(reader.GetOrdinal("Name")),
                        Label = reader.IsDBNull(reader.GetOrdinal("Label")) ? null : reader.GetString(reader.GetOrdinal("Label")),
                        Unit = reader.IsDBNull(reader.GetOrdinal("Unit")) ? "" : reader.GetString(reader.GetOrdinal("Unit")),
                        Type = reader.GetString(reader.GetOrdinal("Type")),
                        Active = reader.GetBoolean(reader.GetOrdinal("Active")),
                        TenantId = reader.IsDBNull(reader.GetOrdinal("TenantID")) ? null : reader.GetInt32(reader.GetOrdinal("TenantID")),
                        TenantName = reader.IsDBNull(reader.GetOrdinal("TenantName")) ? null : reader.GetString(reader.GetOrdinal("TenantName")),
                        OriginalVariableName = reader.GetString(reader.GetOrdinal("Name"))
                    });
                }
                return meters;
            });
        }

        public async Task<int> GetWebServiceImportedMetersCountAsync(bool activeOnly = true)
        {
            int currentCompanyId = _companyContext.CurrentCompanyId;
            return await _databaseService.ExecuteWithCompanyIsolationAsync(currentCompanyId, async (connection, transaction) =>
            {
                var whereConditions = new List<string> { @"(""Name"" LIKE '%.%' OR ""Name"" LIKE 'varsets.%')" };
                if (activeOnly) whereConditions.Add(@"""Active"" = true");

                var whereClause = "WHERE " + string.Join(" AND ", whereConditions);
                string sql = $@"SELECT COUNT(*) FROM ""Meters"" {whereClause}";

                using var cmd = new NpgsqlCommand(sql, connection, transaction);
                var result = await cmd.ExecuteScalarAsync();
                return Convert.ToInt32(result);
            });
        }

        public async Task<DateTime?> GetLastReadingTimestampAsync(int meterId)
        {
            int currentCompanyId = _companyContext.CurrentCompanyId;

            // On précise explicitement <DateTime?> ici pour le compilateur
            return await _databaseService.ExecuteWithCompanyIsolationAsync<DateTime?>(currentCompanyId, async (connection, transaction) =>
            {
                string sql = @"SELECT MAX(""Timestamp"") FROM ""MeterReadings"" WHERE ""MeterId"" = @MeterId";
                using var cmd = new NpgsqlCommand(sql, connection, transaction);
                cmd.Parameters.AddWithValue("@MeterId", meterId);

                var result = await cmd.ExecuteScalarAsync();
                if (result != DBNull.Value && result != null)
                {
                    return (DateTime?)Convert.ToDateTime(result);
                }
                return (DateTime?)null;
            });
        }

    
    }
}