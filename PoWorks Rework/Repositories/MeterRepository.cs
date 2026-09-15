using Npgsql;
using PoWorks_Rework.Models;
using PoWorks_Rework.Services;

namespace PoWorks_Rework.Repositories
{
    /// <summary>
    /// Company-scoped data access for meter entities.
    /// </summary>
    public class MeterRepository
    {
        private readonly DatabaseService _databaseService;
        private readonly ICompanyContext _companyContext;
        private readonly ILogger<MeterRepository> _logger;

        public MeterRepository(
            DatabaseService databaseService,
            ICompanyContext companyContext,
            ILogger<MeterRepository> logger)
        {
            _databaseService = databaseService;
            _companyContext = companyContext;
            _logger = logger;
        }

        public async Task<List<Meter>> GetMetersAsync(
            MeterSearchCriteria criteria,
            int page = 1,
            int pageSize = 20)
        {
            var companyId = _companyContext.CurrentCompanyId;
            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 1, 5000);

            return await _databaseService.ExecuteWithCompanyIsolationAsync(
                companyId,
                async (connection, transaction) =>
                {
                    var meters = new List<Meter>();
                    var where = BuildWhereClause(criteria);

                    var sql = $@"
                        SELECT
                            m.""MeterId"", m.""Name"", m.""Label"", m.""Unit"",
                            m.""ParentId"", p.""Name"" AS ""ParentName"",
                            m.""LastReading"", m.""Type"", m.""Active"",
                            m.""TenantID"", t.""DisplayName"" AS ""TenantName""
                        FROM ""Meters"" m
                        LEFT JOIN ""Meters"" p
                          ON p.""MeterId"" = m.""ParentId""
                         AND p.""CompanyId"" = m.""CompanyId""
                        LEFT JOIN ""Tenants"" t
                          ON t.""TenantID"" = m.""TenantID""
                         AND t.""CompanyId"" = m.""CompanyId""
                        WHERE m.""CompanyId"" = @CompanyId
                        {where}
                        ORDER BY COALESCE(NULLIF(m.""Label"", ''), m.""Name""), m.""MeterId""
                        LIMIT @PageSize OFFSET @Offset";

                    using var cmd = new NpgsqlCommand(sql, connection, transaction);
                    AddSearchParameters(cmd, criteria, companyId);
                    cmd.Parameters.AddWithValue("@PageSize", pageSize);
                    cmd.Parameters.AddWithValue("@Offset", (page - 1) * pageSize);

                    using var reader = await cmd.ExecuteReaderAsync();
                    while (await reader.ReadAsync())
                        meters.Add(ReadMeter(reader));

                    return meters;
                });
        }

        public async Task<List<int>> GetMeterIdsAsync(MeterSearchCriteria criteria)
        {
            var companyId = _companyContext.CurrentCompanyId;

            return await _databaseService.ExecuteWithCompanyIsolationAsync(
                companyId,
                async (connection, transaction) =>
                {
                    var ids = new List<int>();
                    var where = BuildWhereClause(criteria);
                    var sql = $@"
                        SELECT m.""MeterId""
                        FROM ""Meters"" m
                        WHERE m.""CompanyId"" = @CompanyId
                        {where}
                        ORDER BY m.""MeterId""";

                    using var cmd = new NpgsqlCommand(sql, connection, transaction);
                    AddSearchParameters(cmd, criteria, companyId);
                    using var reader = await cmd.ExecuteReaderAsync();

                    while (await reader.ReadAsync())
                        ids.Add(reader.GetInt32(0));

                    return ids;
                });
        }

        public async Task<int> GetTotalMetersCountAsync(MeterSearchCriteria criteria)
        {
            var companyId = _companyContext.CurrentCompanyId;

            return await _databaseService.ExecuteWithCompanyIsolationAsync(
                companyId,
                async (connection, transaction) =>
                {
                    var where = BuildWhereClause(criteria);
                    var sql = $@"
                        SELECT COUNT(*)
                        FROM ""Meters"" m
                        WHERE m.""CompanyId"" = @CompanyId
                        {where}";

                    using var cmd = new NpgsqlCommand(sql, connection, transaction);
                    AddSearchParameters(cmd, criteria, companyId);
                    return Convert.ToInt32(await cmd.ExecuteScalarAsync());
                });
        }

        public async Task<MeterSummary> GetSummaryAsync()
        {
            var companyId = _companyContext.CurrentCompanyId;

            return await _databaseService.ExecuteWithCompanyIsolationAsync(
                companyId,
                async (connection, transaction) =>
                {
                    const string sql = @"
                        SELECT
                            COUNT(*) AS Total,
                            COUNT(*) FILTER (WHERE COALESCE(""Active"", TRUE)) AS ActiveCount,
                            COUNT(*) FILTER (WHERE NOT COALESCE(""Active"", TRUE)) AS DisabledCount,
                            COUNT(*) FILTER (WHERE ""TenantID"" IS NOT NULL) AS AssignedCount,
                            COUNT(*) FILTER (WHERE ""TenantID"" IS NULL) AS UnassignedCount
                        FROM ""Meters""
                        WHERE ""CompanyId"" = @CompanyId";

                    using var cmd = new NpgsqlCommand(sql, connection, transaction);
                    cmd.Parameters.AddWithValue("@CompanyId", companyId);
                    using var reader = await cmd.ExecuteReaderAsync();
                    await reader.ReadAsync();

                    return new MeterSummary
                    {
                        Total = Convert.ToInt32(reader.GetInt64(0)),
                        Active = Convert.ToInt32(reader.GetInt64(1)),
                        Disabled = Convert.ToInt32(reader.GetInt64(2)),
                        Assigned = Convert.ToInt32(reader.GetInt64(3)),
                        Unassigned = Convert.ToInt32(reader.GetInt64(4))
                    };
                });
        }

        public async Task<Meter?> GetMeterByIdAsync(int meterId)
        {
            var companyId = _companyContext.CurrentCompanyId;

            return await _databaseService.ExecuteWithCompanyIsolationAsync<Meter?>(
                companyId,
                async (connection, transaction) =>
                {
                    const string sql = @"
                        SELECT
                            m.""MeterId"", m.""Name"", m.""Label"", m.""Unit"",
                            m.""ParentId"", p.""Name"" AS ""ParentName"",
                            m.""LastReading"", m.""Type"", m.""Active"",
                            m.""TenantID"", t.""DisplayName"" AS ""TenantName""
                        FROM ""Meters"" m
                        LEFT JOIN ""Meters"" p
                          ON p.""MeterId"" = m.""ParentId""
                         AND p.""CompanyId"" = m.""CompanyId""
                        LEFT JOIN ""Tenants"" t
                          ON t.""TenantID"" = m.""TenantID""
                         AND t.""CompanyId"" = m.""CompanyId""
                        WHERE m.""MeterId"" = @MeterId
                          AND m.""CompanyId"" = @CompanyId";

                    using var cmd = new NpgsqlCommand(sql, connection, transaction);
                    cmd.Parameters.AddWithValue("@MeterId", meterId);
                    cmd.Parameters.AddWithValue("@CompanyId", companyId);
                    using var reader = await cmd.ExecuteReaderAsync();

                    return await reader.ReadAsync() ? ReadMeter(reader) : null;
                });
        }

        public async Task<List<Meter>> GetSubMetersAsync(int parentMeterId)
        {
            var companyId = _companyContext.CurrentCompanyId;

            return await _databaseService.ExecuteWithCompanyIsolationAsync(
                companyId,
                async (connection, transaction) =>
                {
                    var meters = new List<Meter>();
                    const string sql = @"
                        SELECT
                            m.""MeterId"", m.""Name"", m.""Label"", m.""Unit"",
                            m.""ParentId"", p.""Name"" AS ""ParentName"",
                            m.""LastReading"", m.""Type"", m.""Active"",
                            m.""TenantID"", t.""DisplayName"" AS ""TenantName""
                        FROM ""Meters"" m
                        LEFT JOIN ""Meters"" p
                          ON p.""MeterId"" = m.""ParentId""
                         AND p.""CompanyId"" = m.""CompanyId""
                        LEFT JOIN ""Tenants"" t
                          ON t.""TenantID"" = m.""TenantID""
                         AND t.""CompanyId"" = m.""CompanyId""
                        WHERE m.""ParentId"" = @ParentId
                          AND m.""CompanyId"" = @CompanyId
                        ORDER BY COALESCE(NULLIF(m.""Label"", ''), m.""Name"")";

                    using var cmd = new NpgsqlCommand(sql, connection, transaction);
                    cmd.Parameters.AddWithValue("@ParentId", parentMeterId);
                    cmd.Parameters.AddWithValue("@CompanyId", companyId);

                    using var reader = await cmd.ExecuteReaderAsync();
                    while (await reader.ReadAsync())
                        meters.Add(ReadMeter(reader));

                    return meters;
                });
        }

        public async Task<List<Meter>> GetParentMetersAsync(int? excludeMeterId = null)
        {
            var companyId = _companyContext.CurrentCompanyId;

            return await _databaseService.ExecuteWithCompanyIsolationAsync(
                companyId,
                async (connection, transaction) =>
                {
                    var meters = new List<Meter>();
                    var excludeClause = excludeMeterId.HasValue
                        ? @" AND m.""MeterId"" <> @ExcludeMeterId"
                        : "";

                    var sql = $@"
                        SELECT
                            m.""MeterId"", m.""Name"", m.""Label"", m.""Unit"",
                            m.""ParentId"", NULL::varchar AS ""ParentName"",
                            m.""LastReading"", m.""Type"", m.""Active"",
                            m.""TenantID"", NULL::varchar AS ""TenantName""
                        FROM ""Meters"" m
                        WHERE m.""CompanyId"" = @CompanyId
                          AND LOWER(m.""Type"") = 'main'
                          {excludeClause}
                        ORDER BY COALESCE(NULLIF(m.""Label"", ''), m.""Name"")";

                    using var cmd = new NpgsqlCommand(sql, connection, transaction);
                    cmd.Parameters.AddWithValue("@CompanyId", companyId);

                    if (excludeMeterId.HasValue)
                        cmd.Parameters.AddWithValue("@ExcludeMeterId", excludeMeterId.Value);

                    using var reader = await cmd.ExecuteReaderAsync();
                    while (await reader.ReadAsync())
                        meters.Add(ReadMeter(reader));

                    return meters;
                });
        }

        public async Task<List<MeterForTrendsAnalysis>> GetWebServiceImportedMetersAsync(
            bool activeOnly = true,
            int limit = 0)
        {
            var companyId = _companyContext.CurrentCompanyId;

            return await _databaseService.ExecuteWithCompanyIsolationAsync(
                companyId,
                async (connection, transaction) =>
                {
                    var meters = new List<MeterForTrendsAnalysis>();
                    var conditions = new List<string>
                    {
                        @"(m.""Name"" LIKE '%.%' OR m.""Name"" LIKE 'varsets.%')",
                        @"m.""CompanyId"" = @CompanyId"
                    };

                    if (activeOnly)
                        conditions.Add(@"m.""Active"" = TRUE");

                    var where = "WHERE " + string.Join(" AND ", conditions);
                    var limitClause = limit > 0 ? "LIMIT @Limit" : "";

                    var sql = $@"
                        SELECT
                            m.""MeterId"", m.""Name"", m.""Label"", m.""Unit"",
                            m.""Type"", m.""Active"", m.""TenantID"",
                            t.""DisplayName"" AS ""TenantName""
                        FROM ""Meters"" m
                        LEFT JOIN ""Tenants"" t
                          ON t.""TenantID"" = m.""TenantID""
                         AND t.""CompanyId"" = m.""CompanyId""
                        {where}
                        ORDER BY m.""Name""
                        {limitClause}";

                    using var cmd = new NpgsqlCommand(sql, connection, transaction);
                    cmd.Parameters.AddWithValue("@CompanyId", companyId);
                    if (limit > 0)
                        cmd.Parameters.AddWithValue("@Limit", limit);

                    using var reader = await cmd.ExecuteReaderAsync();
                    while (await reader.ReadAsync())
                    {
                        meters.Add(new MeterForTrendsAnalysis
                        {
                            MeterId = reader.GetInt32(reader.GetOrdinal("MeterId")),
                            Name = reader.GetString(reader.GetOrdinal("Name")),
                            Label = reader.IsDBNull(reader.GetOrdinal("Label"))
                                ? null
                                : reader.GetString(reader.GetOrdinal("Label")),
                            Unit = reader.IsDBNull(reader.GetOrdinal("Unit"))
                                ? ""
                                : reader.GetString(reader.GetOrdinal("Unit")),
                            Type = reader.GetString(reader.GetOrdinal("Type")),
                            Active = reader.GetBoolean(reader.GetOrdinal("Active")),
                            TenantId = reader.IsDBNull(reader.GetOrdinal("TenantID"))
                                ? null
                                : reader.GetInt32(reader.GetOrdinal("TenantID")),
                            TenantName = reader.IsDBNull(reader.GetOrdinal("TenantName"))
                                ? null
                                : reader.GetString(reader.GetOrdinal("TenantName")),
                            OriginalVariableName = reader.GetString(reader.GetOrdinal("Name"))
                        });
                    }

                    return meters;
                });
        }

        public async Task<int> GetWebServiceImportedMetersCountAsync(bool activeOnly = true)
        {
            var companyId = _companyContext.CurrentCompanyId;

            return await _databaseService.ExecuteWithCompanyIsolationAsync(
                companyId,
                async (connection, transaction) =>
                {
                    var conditions = new List<string>
                    {
                        @"(""Name"" LIKE '%.%' OR ""Name"" LIKE 'varsets.%')",
                        @"""CompanyId"" = @CompanyId"
                    };

                    if (activeOnly)
                        conditions.Add(@"""Active"" = TRUE");

                    var sql = $@"SELECT COUNT(*) FROM ""Meters"" WHERE {string.Join(" AND ", conditions)}";
                    using var cmd = new NpgsqlCommand(sql, connection, transaction);
                    cmd.Parameters.AddWithValue("@CompanyId", companyId);
                    return Convert.ToInt32(await cmd.ExecuteScalarAsync());
                });
        }

        public async Task<DateTime?> GetLastReadingTimestampAsync(int meterId)
        {
            var companyId = _companyContext.CurrentCompanyId;

            return await _databaseService.ExecuteWithCompanyIsolationAsync<DateTime?>(
                companyId,
                async (connection, transaction) =>
                {
                    const string sql = @"
                        SELECT MAX(mr.""Timestamp"")
                        FROM ""MeterReadings"" mr
                        INNER JOIN ""Meters"" m
                          ON m.""MeterId"" = mr.""MeterId""
                         AND m.""CompanyId"" = mr.""CompanyId""
                        WHERE mr.""MeterId"" = @MeterId
                          AND mr.""CompanyId"" = @CompanyId
                          AND m.""CompanyId"" = @CompanyId";

                    using var cmd = new NpgsqlCommand(sql, connection, transaction);
                    cmd.Parameters.AddWithValue("@MeterId", meterId);
                    cmd.Parameters.AddWithValue("@CompanyId", companyId);

                    var result = await cmd.ExecuteScalarAsync();
                    return result is null or DBNull ? null : Convert.ToDateTime(result);
                });
        }

        private static string BuildWhereClause(MeterSearchCriteria criteria)
        {
            var parts = new List<string>();

            if (!string.IsNullOrWhiteSpace(criteria.SearchTerm))
            {
                parts.Add(criteria.SearchField switch
                {
                    "Type" => @"m.""Type"" ILIKE @SearchTerm",
                    "Tenant" => @"
                        m.""TenantID"" IN (
                            SELECT t2.""TenantID""
                            FROM ""Tenants"" t2
                            WHERE t2.""CompanyId"" = @CompanyId
                              AND t2.""DisplayName"" ILIKE @SearchTerm
                        )",
                    _ => @"(m.""Name"" ILIKE @SearchTerm OR COALESCE(m.""Label"", '') ILIKE @SearchTerm)"
                });
            }

            var status = MeterLifecycleRules.NormalizeStatus(criteria.StatusFilter);
            if (status == "Active")
                parts.Add(@"COALESCE(m.""Active"", TRUE) = TRUE");
            else if (status == "Disabled")
                parts.Add(@"COALESCE(m.""Active"", TRUE) = FALSE");

            var assignment = MeterLifecycleRules.NormalizeAssignment(criteria.AssignmentFilter);
            if (assignment == "Assigned")
                parts.Add(@"m.""TenantID"" IS NOT NULL");
            else if (assignment == "Unassigned")
                parts.Add(@"m.""TenantID"" IS NULL");

            return parts.Count == 0 ? "" : " AND " + string.Join(" AND ", parts);
        }

        private static void AddSearchParameters(
            NpgsqlCommand cmd,
            MeterSearchCriteria criteria,
            int companyId)
        {
            cmd.Parameters.AddWithValue("@CompanyId", companyId);

            if (!string.IsNullOrWhiteSpace(criteria.SearchTerm))
                cmd.Parameters.AddWithValue("@SearchTerm", $"%{criteria.SearchTerm.Trim()}%");
        }

        private static Meter ReadMeter(NpgsqlDataReader reader)
        {
            var type = reader.GetString(reader.GetOrdinal("Type"));

            return new Meter
            {
                Id = reader.GetInt32(reader.GetOrdinal("MeterId")),
                Name = reader.GetString(reader.GetOrdinal("Name")),
                Label = reader.IsDBNull(reader.GetOrdinal("Label"))
                    ? null
                    : reader.GetString(reader.GetOrdinal("Label")),
                Unit = reader.IsDBNull(reader.GetOrdinal("Unit"))
                    ? ""
                    : reader.GetString(reader.GetOrdinal("Unit")),
                ParentMeterId = reader.IsDBNull(reader.GetOrdinal("ParentId"))
                    ? null
                    : reader.GetInt32(reader.GetOrdinal("ParentId")).ToString(),
                ParentMeterName = reader.IsDBNull(reader.GetOrdinal("ParentName"))
                    ? null
                    : reader.GetString(reader.GetOrdinal("ParentName")),
                LastReading = reader.IsDBNull(reader.GetOrdinal("LastReading"))
                    ? "0"
                    : reader.GetInt32(reader.GetOrdinal("LastReading")).ToString(),
                Type = char.ToUpperInvariant(type[0]) + type[1..].ToLowerInvariant(),
                Active = reader.IsDBNull(reader.GetOrdinal("Active")) || reader.GetBoolean(reader.GetOrdinal("Active")),
                TenantId = reader.IsDBNull(reader.GetOrdinal("TenantID"))
                    ? null
                    : reader.GetInt32(reader.GetOrdinal("TenantID")).ToString(),
                TenantName = reader.IsDBNull(reader.GetOrdinal("TenantName"))
                    ? null
                    : reader.GetString(reader.GetOrdinal("TenantName"))
            };
        }
    }
}
