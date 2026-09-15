using Npgsql;
using PoWorks_Rework.Models;

namespace PoWorks_Rework.Services
{
    /// <summary>
    /// Single source of truth for consumption queries used by the dashboard and billing.
    /// Keeping both modules on this service prevents "dashboard says X, invoice says Y"
    /// inconsistencies for the same meter/date range.
    /// </summary>
    public class ConsumptionCalculationService
    {
        private readonly DatabaseService _databaseService;
        private readonly ICompanyContext _companyContext;
        private readonly ILogger<ConsumptionCalculationService> _logger;

        public ConsumptionCalculationService(
            DatabaseService databaseService,
            ICompanyContext companyContext,
            ILogger<ConsumptionCalculationService> logger)
        {
            _databaseService = databaseService;
            _companyContext = companyContext;
            _logger = logger;
        }

        public async Task<Dictionary<int, decimal>> GetMeterConsumptionTotalsAsync(
            IReadOnlyCollection<int> meterIds,
            DateTime startDate,
            DateTime endDate)
        {
            var result = meterIds
                .Distinct()
                .ToDictionary(id => id, _ => 0m);

            if (result.Count == 0 || !_databaseService.IsInitialized)
                return result;

            var companyId = _companyContext.CurrentCompanyId;

            try
            {
                return await _databaseService.ExecuteWithCompanyIsolationAsync(
                    companyId,
                    async (connection, transaction) =>
                    {
                        var sql = BuildNormalizedReadingsCte(
                            includeTenantFilter: false,
                            includeMeterFilter: true) + @"
                            SELECT
                                ""MeterId"",
                                COALESCE(SUM(""ConsumptionDelta""), 0) AS ""TotalConsumption""
                            FROM normalized
                            GROUP BY ""MeterId""";

                        using var cmd = new NpgsqlCommand(sql, connection, transaction);
                        cmd.Parameters.AddWithValue("@CompanyId", companyId);
                        cmd.Parameters.AddWithValue("@StartDate", startDate);
                        cmd.Parameters.AddWithValue("@EndDate", endDate);
                        cmd.Parameters.AddWithValue("@MeterIds", result.Keys.ToArray());

                        using var reader = await cmd.ExecuteReaderAsync();
                        while (await reader.ReadAsync())
                        {
                            result[reader.GetInt32(0)] =
                                reader.IsDBNull(1) ? 0m : reader.GetDecimal(1);
                        }

                        return result;
                    });
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Failed to calculate consumption totals for {MeterCount} meter(s) in workspace {CompanyId}.",
                    result.Count,
                    companyId);
                throw;
            }
        }

        public async Task<List<ConsumptionQueryResult>> GetConsumptionSeriesAsync(
            MeterReadingFilters filters)
        {
            var data = new List<ConsumptionQueryResult>();
            if (!_databaseService.IsInitialized)
                return data;

            var companyId = _companyContext.CurrentCompanyId;

            try
            {
                return await _databaseService.ExecuteWithCompanyIsolationAsync(
                    companyId,
                    async (connection, transaction) =>
                    {
                        var (startDate, endDate) = filters.GetDateRange();
                        var includeTenant = filters.TenantId.HasValue;
                        var includeMeters = filters.MeterIds?.Any() == true;

                        var sql = BuildNormalizedReadingsCte(includeTenant, includeMeters);
                        var bucket = GetBucketExpression(filters.DateFilter);

                        if (string.Equals(filters.GroupBy, "tenant", StringComparison.OrdinalIgnoreCase))
                        {
                            sql += $@"
                                SELECT
                                    COALESCE(""TenantID"", 0) AS ""MeterId"",
                                    COALESCE(NULLIF(""TenantName"", ''), 'Unassigned') AS ""MeterName"",
                                    'kWh' AS ""Unit"",
                                    {bucket} AS ""ReadingDate"",
                                    COALESCE(SUM(""ConsumptionDelta""), 0) AS ""TotalConsumption"",
                                    COALESCE(AVG(""ConsumptionDelta""), 0) AS ""AvgConsumption"",
                                    COALESCE(SUM(""ConsumptionDelta""), 0) AS ""MaxConsumption"",
                                    ""TenantID"",
                                    COALESCE(""TenantName"", '') AS ""TenantName""
                                FROM normalized
                                GROUP BY ""TenantID"", ""TenantName"", {bucket}
                                ORDER BY {bucket} ASC, ""MeterName""";
                        }
                        else
                        {
                            sql += $@"
                                SELECT
                                    ""MeterId"",
                                    ""MeterName"",
                                    'kWh' AS ""Unit"",
                                    {bucket} AS ""ReadingDate"",
                                    COALESCE(SUM(""ConsumptionDelta""), 0) AS ""TotalConsumption"",
                                    COALESCE(AVG(""ConsumptionDelta""), 0) AS ""AvgConsumption"",
                                    COALESCE(SUM(""ConsumptionDelta""), 0) AS ""MaxConsumption"",
                                    ""TenantID"",
                                    COALESCE(""TenantName"", '') AS ""TenantName""
                                FROM normalized
                                GROUP BY
                                    ""MeterId"", ""MeterName"",
                                    ""TenantID"", ""TenantName"", {bucket}
                                ORDER BY {bucket} ASC, ""MeterName""";
                        }

                        using var cmd = new NpgsqlCommand(sql, connection, transaction);
                        cmd.Parameters.AddWithValue("@CompanyId", companyId);
                        cmd.Parameters.AddWithValue("@StartDate", startDate);
                        cmd.Parameters.AddWithValue("@EndDate", endDate);

                        if (includeTenant)
                            cmd.Parameters.AddWithValue("@TenantId", filters.TenantId!.Value);

                        if (includeMeters)
                            cmd.Parameters.AddWithValue("@MeterIds", filters.MeterIds!.Distinct().ToArray());

                        using var reader = await cmd.ExecuteReaderAsync();
                        while (await reader.ReadAsync())
                        {
                            data.Add(new ConsumptionQueryResult
                            {
                                MeterId = reader.GetInt32(reader.GetOrdinal("MeterId")),
                                MeterName = reader.GetString(reader.GetOrdinal("MeterName")),
                                Unit = reader.GetString(reader.GetOrdinal("Unit")),
                                ReadingDate = reader.GetString(reader.GetOrdinal("ReadingDate")),
                                TotalConsumption = Convert.ToDouble(reader.GetDecimal(reader.GetOrdinal("TotalConsumption"))),
                                AvgConsumption = Convert.ToDouble(reader.GetDecimal(reader.GetOrdinal("AvgConsumption"))),
                                MaxConsumption = Convert.ToDouble(reader.GetDecimal(reader.GetOrdinal("MaxConsumption"))),
                                TenantId = reader.IsDBNull(reader.GetOrdinal("TenantID"))
                                    ? null
                                    : reader.GetInt32(reader.GetOrdinal("TenantID")),
                                TenantName = reader.GetString(reader.GetOrdinal("TenantName"))
                            });
                        }

                        return data;
                    });
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Failed to calculate dashboard consumption in workspace {CompanyId}.",
                    companyId);
                throw;
            }
        }

        private static string BuildNormalizedReadingsCte(
            bool includeTenantFilter,
            bool includeMeterFilter)
        {
            var tenantFilter = includeTenantFilter
                ? @" AND m.""TenantID"" = @TenantId"
                : string.Empty;

            var meterFilter = includeMeterFilter
                ? @" AND m.""MeterId"" = ANY(@MeterIds)"
                : string.Empty;

            var supportedEnergyUnit =
                ConsumptionFormula.SqlSupportedEnergyUnitPredicate(@"m.""Unit""");

            return $@"
                WITH source AS (
                    SELECT
                        m.""MeterId"",
                        m.""Name"" AS ""MeterName"",
                        COALESCE(m.""Unit"", '') AS ""Unit"",
                        m.""TenantID"",
                        COALESCE(t.""DisplayName"", '') AS ""TenantName"",
                        mr.""Timestamp"",
                        mr.""Value"",
                        LAG(mr.""Value"") OVER (
                            PARTITION BY m.""MeterId""
                            ORDER BY mr.""Timestamp""
                        ) AS ""PreviousValue"",
                        LAG(mr.""Timestamp"") OVER (
                            PARTITION BY m.""MeterId""
                            ORDER BY mr.""Timestamp""
                        ) AS ""PreviousTimestamp""
                    FROM ""MeterReadings"" mr
                    INNER JOIN ""Meters"" m
                      ON m.""MeterId"" = mr.""MeterId""
                     AND m.""CompanyId"" = mr.""CompanyId""
                    LEFT JOIN ""Tenants"" t
                      ON t.""TenantID"" = m.""TenantID""
                     AND t.""CompanyId"" = m.""CompanyId""
                    WHERE m.""CompanyId"" = @CompanyId
                      AND mr.""CompanyId"" = @CompanyId
                      AND m.""Active"" = TRUE
                      AND {supportedEnergyUnit}
                      AND mr.""Timestamp"" >= @StartDate
                      AND mr.""Timestamp"" <= @EndDate
                      {tenantFilter}
                      {meterFilter}
                ),
                normalized AS (
                    SELECT
                        source.*,
                        {ConsumptionFormula.SqlDeltaExpression} AS ""ConsumptionDelta""
                    FROM source
                )
                ";
        }

        private static string GetBucketExpression(string? dateFilter)
        {
            return dateFilter?.ToLowerInvariant() switch
            {
                "yearly" => @"to_char(DATE_TRUNC('year', ""Timestamp""), 'YYYY')",
                "monthly" => @"to_char(DATE_TRUNC('month', ""Timestamp""), 'YYYY-MM')",
                "hourly" => @"to_char(DATE_TRUNC('hour', ""Timestamp""), 'YYYY-MM-DD HH24:00')",
                _ => @"to_char(DATE_TRUNC('day', ""Timestamp""), 'YYYY-MM-DD')"
            };
        }
    }
}
