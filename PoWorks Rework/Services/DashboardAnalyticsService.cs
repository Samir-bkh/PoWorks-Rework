using Npgsql;
using PoWorks_Rework.Models;

namespace PoWorks_Rework.Services
{
    /// <summary>
    /// Queries meter readings for one analytical metric, normalizes all compatible
    /// PcVue units, and delegates presentation-level aggregation to the pure
    /// DashboardAnalyticsEngine.
    /// </summary>
    public class DashboardAnalyticsService
    {
        private readonly DatabaseService _databaseService;
        private readonly ICompanyContext _companyContext;
        private readonly ConsumptionCalculationService _consumptionCalculationService;
        private readonly ILogger<DashboardAnalyticsService> _logger;

        public DashboardAnalyticsService(
            DatabaseService databaseService,
            ICompanyContext companyContext,
            ConsumptionCalculationService consumptionCalculationService,
            ILogger<DashboardAnalyticsService> logger)
        {
            _databaseService = databaseService;
            _companyContext = companyContext;
            _consumptionCalculationService = consumptionCalculationService;
            _logger = logger;
        }

        public async Task<DashboardAnalyticsResult> GetAnalyticsAsync(
            DashboardAnalyticsQuery query)
        {
            if (query.EndDate < query.StartDate)
                throw new ArgumentException("End date must be on or after start date.");

            query.Metric = MeasurementSemantics.NormalizeMetric(query.Metric);
            query.Aggregation = MeasurementSemantics.ResolveAggregation(
                query.Metric,
                query.Aggregation);
            query.MaxSeries = Math.Clamp(query.MaxSeries, 1, 50);
            query.RankingLimit = Math.Clamp(query.RankingLimit, 1, 25);

            var rows = await GetMetricBucketsAsync(query);
            return DashboardAnalyticsEngine.Build(query, rows);
        }

        public async Task<List<MeasurementBucketResult>> GetMetricBucketsAsync(
            DashboardAnalyticsQuery query)
        {
            var rows = new List<MeasurementBucketResult>();
            if (!_databaseService.IsInitialized)
                return rows;

            var companyId = _companyContext.CurrentCompanyId;
            var definition = MeasurementSemantics.GetDefinition(query.Metric);
            var metric = definition.Key;

            // Energy is billable business data. Reuse the same calculation
            // service as invoicing so the dashboard can never show a different
            // kWh total for the same meters and date range.
            if (metric == "energy")
                return await GetEnergyBucketsAsync(query);

            var quantityMetric = definition.ValueKind == "quantity";
            var includeMeters = query.MeterIds?.Count > 0;
            var unitPredicate = MeasurementSemantics.SqlMetricPredicate(
                metric,
                @"m.""Unit""");
            var bucketExpression = GetBucketExpression(
                query.DateFilter,
                @"n.""Timestamp""");

            var tenantFilter = query.TenantId.HasValue
                ? @" AND m.""TenantID"" = @TenantId"
                : string.Empty;
            var meterFilter = includeMeters
                ? @" AND m.""MeterId"" = ANY(@MeterIds)"
                : string.Empty;

            var metricValueExpression = quantityMetric
                ? MeasurementSemantics.SqlIntervalQuantityExpression(
                    metric,
                    @"o.""SourceUnit""",
                    @"o.""PreviousValue""",
                    @"o.""PreviousTimestamp""",
                    @"o.""Value""",
                    @"o.""Timestamp""")
                : MeasurementSemantics.SqlInstantValueExpression(
                    metric,
                    @"o.""SourceUnit""",
                    @"o.""Value""");

            var canonicalUnitExpression = metric == "raw"
                ? @"COALESCE(NULLIF(TRIM(n.""SourceUnit""), ''), '(unspecified)')"
                : $"'{definition.CanonicalUnit.Replace("'", "''")}'";

            var bucketAggregate = quantityMetric
                ? @"SUM(n.""MetricValue"")"
                : @"AVG(n.""MetricValue"")";

            var sql = $@"
                WITH selected_meters AS (
                    SELECT
                        m.""MeterId"",
                        m.""Name"" AS ""MeterName"",
                        COALESCE(m.""Unit"", '') AS ""SourceUnit"",
                        m.""TenantID"",
                        COALESCE(t.""DisplayName"", '') AS ""TenantName""
                    FROM ""Meters"" m
                    LEFT JOIN ""Tenants"" t
                      ON t.""TenantID"" = m.""TenantID""
                     AND t.""CompanyId"" = m.""CompanyId""
                    WHERE m.""CompanyId"" = @CompanyId
                      AND m.""Active"" = TRUE
                      AND {unitPredicate}
                      {tenantFilter}
                      {meterFilter}
                ),
                period_readings AS (
                    SELECT
                        sm.""MeterId"",
                        sm.""MeterName"",
                        sm.""SourceUnit"",
                        sm.""TenantID"",
                        sm.""TenantName"",
                        mr.""Timestamp"",
                        mr.""Value""
                    FROM selected_meters sm
                    INNER JOIN ""MeterReadings"" mr
                      ON mr.""MeterId"" = sm.""MeterId""
                     AND mr.""CompanyId"" = @CompanyId
                    WHERE mr.""Timestamp"" >= @StartDate
                      AND mr.""Timestamp"" <= @EndDate
                ),
                ordered AS (
                    SELECT
                        pr.*,
                        LAG(pr.""Value"") OVER (
                            PARTITION BY pr.""MeterId""
                            ORDER BY pr.""Timestamp""
                        ) AS ""PreviousValue"",
                        LAG(pr.""Timestamp"") OVER (
                            PARTITION BY pr.""MeterId""
                            ORDER BY pr.""Timestamp""
                        ) AS ""PreviousTimestamp""
                    FROM period_readings pr
                ),
                normalized AS (
                    SELECT
                        o.*,
                        {metricValueExpression} AS ""MetricValue""
                    FROM ordered o
                )
                SELECT
                    n.""MeterId"",
                    n.""MeterName"",
                    n.""TenantID"",
                    n.""TenantName"",
                    n.""SourceUnit"",
                    {canonicalUnitExpression} AS ""CanonicalUnit"",
                    {bucketExpression} AS ""ReadingDate"",
                    {bucketAggregate}::double precision AS ""Value""
                FROM normalized n
                WHERE n.""Timestamp"" >= @StartDate
                  AND n.""Timestamp"" <= @EndDate
                  AND n.""MetricValue"" IS NOT NULL
                GROUP BY
                    n.""MeterId"",
                    n.""MeterName"",
                    n.""TenantID"",
                    n.""TenantName"",
                    n.""SourceUnit"",
                    {bucketExpression}
                ORDER BY
                    {bucketExpression} ASC,
                    n.""MeterName"" ASC";

            try
            {
                return await _databaseService.ExecuteWithCompanyIsolationAsync(
                    companyId,
                    async (connection, transaction) =>
                    {
                        using var command = new NpgsqlCommand(
                            sql,
                            connection,
                            transaction);
                        command.Parameters.AddWithValue(
                            "@CompanyId",
                            companyId);
                        command.Parameters.AddWithValue(
                            "@StartDate",
                            query.StartDate);
                        command.Parameters.AddWithValue(
                            "@EndDate",
                            query.EndDate);

                        if (query.TenantId.HasValue)
                        {
                            command.Parameters.AddWithValue(
                                "@TenantId",
                                query.TenantId.Value);
                        }

                        if (includeMeters)
                        {
                            command.Parameters.AddWithValue(
                                "@MeterIds",
                                query.MeterIds.Distinct().ToArray());
                        }

                        using var reader = await command.ExecuteReaderAsync();
                        while (await reader.ReadAsync())
                        {
                            rows.Add(new MeasurementBucketResult
                            {
                                MeterId = reader.GetInt32(
                                    reader.GetOrdinal("MeterId")),
                                MeterName = reader.GetString(
                                    reader.GetOrdinal("MeterName")),
                                TenantId = reader.IsDBNull(
                                    reader.GetOrdinal("TenantID"))
                                    ? null
                                    : reader.GetInt32(
                                        reader.GetOrdinal("TenantID")),
                                TenantName = reader.GetString(
                                    reader.GetOrdinal("TenantName")),
                                SourceUnit = reader.GetString(
                                    reader.GetOrdinal("SourceUnit")),
                                CanonicalUnit = reader.GetString(
                                    reader.GetOrdinal("CanonicalUnit")),
                                ReadingDate = reader.GetString(
                                    reader.GetOrdinal("ReadingDate")),
                                Value = reader.GetDouble(
                                    reader.GetOrdinal("Value"))
                            });
                        }

                        return rows;
                    });
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Failed dashboard analytics query for metric {Metric} in workspace {CompanyId}.",
                    metric,
                    companyId);
                throw;
            }
        }

        private async Task<List<MeasurementBucketResult>> GetEnergyBucketsAsync(
            DashboardAnalyticsQuery query)
        {
            var rows = await _consumptionCalculationService.GetConsumptionSeriesAsync(
                new MeterReadingFilters
                {
                    DateFilter = query.DateFilter,
                    TenantId = query.TenantId,
                    MeterIds = query.MeterIds?.Distinct().ToList() ?? new List<int>(),
                    StartDate = query.StartDate,
                    EndDate = query.EndDate,
                    GroupBy = "meter",
                    ActiveOnly = true,
                    IncludeNullTenants = true,
                    Limit = int.MaxValue
                });

            return rows.Select(row => new MeasurementBucketResult
            {
                MeterId = row.MeterId,
                MeterName = row.MeterName,
                TenantId = row.TenantId,
                TenantName = row.TenantName,
                SourceUnit = row.Unit,
                CanonicalUnit = "kWh",
                ReadingDate = row.ReadingDate,
                Value = row.TotalConsumption
            }).ToList();
        }

        private static string GetBucketExpression(
            string? dateFilter,
            string timestampSql)
        {
            return dateFilter?.Trim().ToLowerInvariant() switch
            {
                "yearly" =>
                    $"to_char(DATE_TRUNC('year', {timestampSql}), 'YYYY')",
                "monthly" =>
                    $"to_char(DATE_TRUNC('month', {timestampSql}), 'YYYY-MM')",
                "hourly" =>
                    $"to_char(DATE_TRUNC('hour', {timestampSql}), 'YYYY-MM-DD HH24:00')",
                _ =>
                    $"to_char(DATE_TRUNC('day', {timestampSql}), 'YYYY-MM-DD')"
            };
        }
    }
}
