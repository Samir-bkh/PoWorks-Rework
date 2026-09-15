using System.ComponentModel.DataAnnotations;

namespace PoWorks_Rework.Models
{
    /// <summary>
    /// Filters used for querying meter reading data on the dashboard.
    /// Controls date range, tenant, meter selection, pagination, and grouping options.
    /// </summary>
    public class MeterReadingFilters
    {
        /// <summary>
        /// The date aggregation level (e.g. monthly, daily, hourly, yearly).
        /// </summary>
        public string DateFilter { get; set; } = "monthly";

        /// <summary>
        /// The tenant ID to filter meters by, if any.
        /// </summary>
        public int? TenantId { get; set; }

        /// <summary>
        /// The list of meter IDs to include. Empty means all active meters.
        /// </summary>
        public List<int> MeterIds { get; set; } = new List<int>();

        /// <summary>
        /// The start of the date range.
        /// </summary>
        public DateTime? StartDate { get; set; }

        /// <summary>
        /// The end of the date range.
        /// </summary>
        public DateTime? EndDate { get; set; }

        /// <summary>
        /// The maximum number of meters to return.
        /// </summary>
        public int Limit { get; set; } = 5; 

        /// <summary>
        /// The number of meters to skip for pagination.
        /// </summary>
        public int Offset { get; set; } = 0;

        /// <summary>
        /// Whether to only include active meters.
        /// </summary>
        public bool ActiveOnly { get; set; } = true;

        /// <summary>
        /// Whether meters without a tenant should be included.
        /// </summary>
        public bool IncludeNullTenants { get; set; } = true; 

        /// <summary>
        /// Indicates whether comparison mode is active.
        /// </summary>
        public bool IsComparisonMode { get; set; }

        /// <summary>
        /// The grouping mode for the query (e.g. meter or tenant).
        /// </summary>
        public string GroupBy { get; set; } = "meter";

        /// <summary>
        /// Resolves the effective date range, defaulting to the last 30 days.
        /// </summary>
        /// <returns>A tuple with start and end dates.</returns>
        public (DateTime start, DateTime end) GetDateRange()
        {
            var endDate = EndDate ?? DateTime.Now;
            var startDate = StartDate ?? endDate.AddDays(-30);
            return (startDate, endDate);
        }
    }

    /// <summary>
    /// Represents a meter returned from a dashboard query.
    /// </summary>
    public class MeterQueryResult
    {
        /// <summary>
        /// The meter ID.
        /// </summary>
        public int MeterId { get; set; }

        /// <summary>
        /// The meter name.
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// The meter label, if any.
        /// </summary>
        public string Label { get; set; } = string.Empty;

        /// <summary>
        /// The meter's unit of measurement.
        /// </summary>
        public string Unit { get; set; } = string.Empty;

        /// <summary>
        /// The meter type (e.g. Energy).
        /// </summary>
        public string Type { get; set; } = "Energy";

        /// <summary>
        /// Whether the meter is active.
        /// </summary>
        public bool Active { get; set; }

        /// <summary>
        /// The tenant ID assigned to the meter, if any.
        /// </summary>
        public int? TenantId { get; set; }

        /// <summary>
        /// The display name of the assigned tenant.
        /// </summary>
        public string TenantName { get; set; } = string.Empty;

        /// <summary>
        /// The meter's last recorded reading.
        /// </summary>
        public int LastReading { get; set; }

        /// <summary>
        /// The display name combining the meter name and label.
        /// </summary>
        public string DisplayName => string.IsNullOrEmpty(Label) ? Name : $"{Name} ({Label})";

        /// <summary>
        /// The full display name including the tenant name when available.
        /// </summary>
        public string FullDisplayName
        {
            get
            {
                var display = DisplayName;
                if (!string.IsNullOrEmpty(TenantName))
                    display += $" - {TenantName}";
                return display;
            }
        }
    }

    /// <summary>
    /// Represents an aggregated consumption query result for charting.
    /// </summary>
    public class ConsumptionQueryResult
    {
        /// <summary>
        /// The meter or tenant ID.
        /// </summary>
        public int MeterId { get; set; }

        /// <summary>
        /// The meter or tenant name.
        /// </summary>
        public string MeterName { get; set; } = string.Empty;

        /// <summary>
        /// The unit of measurement.
        /// </summary>
        public string Unit { get; set; } = "kWh";

        /// <summary>
        /// The formatted reading date for the aggregation period.
        /// </summary>
        public string ReadingDate { get; set; } = string.Empty;

        /// <summary>
        /// The total consumption for the period.
        /// </summary>
        public double TotalConsumption { get; set; }

        /// <summary>
        /// The average consumption for the period.
        /// </summary>
        public double AvgConsumption { get; set; }

        /// <summary>
        /// The maximum consumption for the period.
        /// </summary>
        public double MaxConsumption { get; set; }

        /// <summary>
        /// The tenant ID, if grouped by tenant.
        /// </summary>
        public int? TenantId { get; set; }

        /// <summary>
        /// The tenant name.
        /// </summary>
        public string TenantName { get; set; } = string.Empty;
    }

    /// <summary>
    /// Summary statistics for the dashboard consumption display.
    /// </summary>
    public class DashboardSummary
    {
        public double TotalConsumption { get; set; }
        public double AverageDaily { get; set; }
        public double PeakUsage { get; set; }
        public int ActiveMeters { get; set; }
        public int TotalMeters { get; set; }

        /// <summary>
        /// Inclusive number of calendar days represented by the selected range.
        /// AverageDaily always uses this value rather than only days containing readings.
        /// </summary>
        public int PeriodDays { get; set; }

        /// <summary>
        /// Human readable unit when every displayed series shares the same unit.
        /// </summary>
        public string Unit { get; set; } = "kWh";

        /// <summary>
        /// True when incompatible units are shown together. In this case aggregate
        /// KPIs should not be presented as a physically meaningful single value.
        /// </summary>
        public bool HasMixedUnits { get; set; }

        /// <summary>
        /// Describes the bucket behind PeakUsage (daily, monthly or annual).
        /// </summary>
        public string PeakPeriodLabel { get; set; } = "Highest daily total";

        public int DataBuckets { get; set; }
        public DateTime? OldestReading { get; set; }
        public DateTime? NewestReading { get; set; }

        public object ToDisplayObject()
        {
            return new
            {
                totalConsumption = Math.Round(TotalConsumption, 2),
                averageDaily = Math.Round(AverageDaily, 2),
                peakUsage = Math.Round(PeakUsage, 2),
                activeMeters = ActiveMeters,
                totalMeters = TotalMeters,
                periodDays = PeriodDays,
                unit = Unit,
                hasMixedUnits = HasMixedUnits,
                peakPeriodLabel = PeakPeriodLabel,
                dataBuckets = DataBuckets
            };
        }
    }

    /// <summary>
    /// Represents chart-ready data with labels and datasets.
    /// </summary>
    public class ChartDataResult
    {
        /// <summary>
        /// The chart x-axis labels.
        /// </summary>
        public List<string> Labels { get; set; } = new List<string>();

        /// <summary>
        /// The chart datasets.
        /// </summary>
        public List<ChartDataset> Datasets { get; set; } = new List<ChartDataset>();

        /// <summary>
        /// Converts the chart data into an API response object.
        /// </summary>
        /// <returns>An anonymous object with labels and formatted datasets.</returns>
        public object ToApiResponse()
        {
            return new
            {
                labels = Labels,
                datasets = Datasets.Select(d => d.ToApiFormat()).ToList()
            };
        }
    }

    /// <summary>
    /// Represents a single chart dataset with its styling.
    /// </summary>
    public class ChartDataset
    {
        public int MeterId { get; set; }
        public int? TenantId { get; set; }
        public string SeriesKey { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
        public string MeterName { get; set; } = string.Empty;
        public string Unit { get; set; } = string.Empty;
        public string TenantName { get; set; } = string.Empty;
        public string MeasurementMetric { get; set; } = "energy";
        public bool IsAggregate { get; set; }
        public int SourceCount { get; set; } = 1;
        public List<double?> Data { get; set; } = new List<double?>();
        public string BackgroundColor { get; set; } = string.Empty;
        public string BorderColor { get; set; } = string.Empty;

        public object ToApiFormat()
        {
            return new
            {
                meterId = MeterId,
                tenantId = TenantId,
                seriesKey = SeriesKey,
                label = Label,
                meterName = MeterName,
                unit = Unit,
                tenantName = TenantName,
                measurementMetric = MeasurementMetric,
                isAggregate = IsAggregate,
                sourceCount = SourceCount,
                data = Data,
                backgroundColor = BackgroundColor,
                borderColor = BorderColor
            };
        }
    }

    /// <summary>
    /// Dashboard analytics query after controller-level authorization has been applied.
    /// </summary>
    public class DashboardAnalyticsQuery
    {
        public string Metric { get; set; } = "energy";
        public string ScopeMode { get; set; } = "aggregate";
        public string Aggregation { get; set; } = "auto";
        public string DateFilter { get; set; } = "daily";
        public int? TenantId { get; set; }
        public List<int> MeterIds { get; set; } = new();

        /// <summary>
        /// Optional internal stable series keys used to keep comparison charts
        /// aligned with the exact series visible in the primary period.
        /// </summary>
        public List<string> SeriesKeys { get; set; } = new();

        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public int MaxSeries { get; set; } = 10;
        public int RankingLimit { get; set; } = 5;
    }

    /// <summary>
    /// One normalized metric value for one meter and one time bucket.
    /// </summary>
    public class MeasurementBucketResult
    {
        public int MeterId { get; set; }
        public string MeterName { get; set; } = string.Empty;
        public int? TenantId { get; set; }
        public string TenantName { get; set; } = string.Empty;
        public string SourceUnit { get; set; } = string.Empty;
        public string CanonicalUnit { get; set; } = string.Empty;
        public string ReadingDate { get; set; } = string.Empty;
        public double Value { get; set; }
    }

    public class DashboardKpi
    {
        public string Key { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
        public double Value { get; set; }
        public string Unit { get; set; } = string.Empty;
        public string Detail { get; set; } = string.Empty;

        public object ToApiResponse() => new
        {
            key = Key,
            label = Label,
            value = Math.Round(Value, 4),
            unit = Unit,
            detail = Detail
        };
    }

    public class DashboardRankingItem
    {
        public string Key { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string TenantName { get; set; } = string.Empty;
        public double Value { get; set; }
        public string Unit { get; set; } = string.Empty;
        public int MeterCount { get; set; }

        public object ToApiResponse() => new
        {
            key = Key,
            name = Name,
            tenantName = TenantName,
            value = Math.Round(Value, 4),
            unit = Unit,
            meterCount = MeterCount
        };
    }

    public class DashboardAnalyticsSummary
    {
        public string Metric { get; set; } = "energy";
        public string MetricLabel { get; set; } = "Energy consumption";
        public string Unit { get; set; } = "kWh";
        public string ScopeMode { get; set; } = "aggregate";
        public string Aggregation { get; set; } = "sum";
        public int ActiveMeters { get; set; }
        public int SeriesCount { get; set; }
        public int PeriodDays { get; set; }
        public int DataBuckets { get; set; }
        public double CoveragePercent { get; set; }
        public List<DashboardKpi> Kpis { get; set; } = new();

        public object ToApiResponse() => new
        {
            metric = Metric,
            metricLabel = MetricLabel,
            unit = Unit,
            scopeMode = ScopeMode,
            aggregation = Aggregation,
            activeMeters = ActiveMeters,
            seriesCount = SeriesCount,
            periodDays = PeriodDays,
            dataBuckets = DataBuckets,
            coveragePercent = Math.Round(CoveragePercent, 1),
            kpis = Kpis.Select(k => k.ToApiResponse()).ToList()
        };
    }

    public class DashboardAnalyticsMetadata
    {
        public string Metric { get; set; } = "energy";
        public string MetricLabel { get; set; } = "Energy consumption";
        public string Description { get; set; } = string.Empty;
        public string ValueKind { get; set; } = "quantity";
        public string CanonicalUnit { get; set; } = "kWh";
        public string DefaultAggregation { get; set; } = "sum";
        public List<string> AllowedAggregations { get; set; } = new();
        public string ScopeMode { get; set; } = "aggregate";
        public string Aggregation { get; set; } = "sum";
        public int SourceMeterCount { get; set; }
        public int OmittedSeries { get; set; }

        public object ToApiResponse() => new
        {
            metric = Metric,
            metricLabel = MetricLabel,
            description = Description,
            valueKind = ValueKind,
            canonicalUnit = CanonicalUnit,
            defaultAggregation = DefaultAggregation,
            allowedAggregations = AllowedAggregations,
            scopeMode = ScopeMode,
            aggregation = Aggregation,
            sourceMeterCount = SourceMeterCount,
            omittedSeries = OmittedSeries
        };
    }

    public class DashboardAnalyticsResult
    {
        public ChartDataResult ChartData { get; set; } = new();
        public DashboardAnalyticsSummary Summary { get; set; } = new();
        public DashboardAnalyticsMetadata Metadata { get; set; } = new();
        public List<DashboardRankingItem> Ranking { get; set; } = new();

        public object ToApiResponse() => new
        {
            chartData = ChartData.ToApiResponse(),
            summary = Summary.ToApiResponse(),
            metadata = Metadata.ToApiResponse(),
            ranking = Ranking.Select(r => r.ToApiResponse()).ToList()
        };
    }

    /// <summary>
    /// Represents the result of a data availability check.
    /// </summary>
    public class DataAvailabilityResult
    {
        /// <summary>
        /// Whether there are any active meters.
        /// </summary>
        public bool HasActiveMeters { get; set; }

        /// <summary>
        /// Whether there are any readings matching the filters.
        /// </summary>
        public bool HasReadings { get; set; }

        /// <summary>
        /// The number of active meters.
        /// </summary>
        public int ActiveMeterCount { get; set; }

        /// <summary>
        /// The total number of readings.
        /// </summary>
        public long TotalReadings { get; set; }

        /// <summary>
        /// The number of meters with a tenant assigned.
        /// </summary>
        public int MetersWithTenants { get; set; }

        /// <summary>
        /// The number of meters without a tenant assigned.
        /// </summary>
        public int MetersWithoutTenants { get; set; }

        /// <summary>
        /// Whether data is available (active meters and readings exist).
        /// </summary>
        public bool IsDataAvailable => HasActiveMeters && HasReadings;

        /// <summary>
        /// Returns a human-readable message describing the data availability.
        /// </summary>
        /// <returns>The availability message.</returns>
        public string GetAvailabilityMessage()
        {
            if (!HasActiveMeters)
                return "No active meters found";

            if (!HasReadings)
                return $"Found {ActiveMeterCount} active meters but no reading data";

            var tenantInfo = MetersWithTenants > 0 && MetersWithoutTenants > 0
                ? $" ({MetersWithTenants} with tenants, {MetersWithoutTenants} without)"
                : MetersWithTenants > 0 ? $" (all with tenants)" : $" (no tenant assignments)";

            return $"Found {ActiveMeterCount} active meters{tenantInfo} with {TotalReadings} readings";
        }
    }

    /// <summary>
    /// Fluent builder for constructing meter query SQL statements.
    /// </summary>
    public class MeterQueryBuilder
    {
        private readonly List<string> _whereConditions = new List<string>();
        private readonly List<object> _parameters = new List<object>();
        private string _orderBy = @"m.""Name""";
        private int? _limit;
        private int? _offset;

        /// <summary>
        /// Adds an active-only filter to the query.
        /// </summary>
        /// <param name="active">Whether to filter active meters only.</param>
        /// <returns>The query builder for chaining.</returns>
        public MeterQueryBuilder ActiveOnly(bool active = true)
        {
            if (active)
            {
                _whereConditions.Add(@"m.""Active"" = true");
            }
            return this;
        }

        /// <summary>
        /// Adds a tenant filter to the query.
        /// </summary>
        /// <param name="tenantId">The tenant ID to filter by.</param>
        /// <returns>The query builder for chaining.</returns>
        public MeterQueryBuilder WithTenant(int? tenantId)
        {
            if (tenantId.HasValue)
            {
                _whereConditions.Add(@"m.""TenantID"" = @TenantId");
                _parameters.Add(new { Name = "@TenantId", Value = tenantId.Value });
            }
            return this;
        }

        /// <summary>
        /// Controls whether meters without tenants are included.
        /// </summary>
        /// <param name="include">Whether to include null-tenant meters.</param>
        /// <returns>The query builder for chaining.</returns>
        public MeterQueryBuilder IncludeNullTenants(bool include = true)
        {
            if (include)
            {
            }
            return this;
        }

        /// <summary>
        /// Sets the maximum number of meters to return.
        /// </summary>
        /// <param name="limit">The limit value.</param>
        /// <returns>The query builder for chaining.</returns>
        public MeterQueryBuilder WithLimit(int limit)
        {
            _limit = limit;
            return this;
        }

        /// <summary>
        /// Sets the number of meters to skip for pagination.
        /// </summary>
        /// <param name="offset">The offset value.</param>
        /// <returns>The query builder for chaining.</returns>
        public MeterQueryBuilder WithOffset(int offset)
        {
            _offset = offset;
            return this;
        }

        /// <summary>
        /// Sets the ORDER BY clause for the query.
        /// </summary>
        /// <param name="orderBy">The ordering expression.</param>
        /// <returns>The query builder for chaining.</returns>
        public MeterQueryBuilder OrderBy(string orderBy)
        {
            _orderBy = orderBy;
            return this;
        }

        /// <summary>
        /// Builds the final SQL query and parameter list.
        /// </summary>
        /// <returns>A tuple containing the SQL query and parameters.</returns>
        public (string query, List<object> parameters) Build()
        {
            var baseQuery = @"
                SELECT m.""MeterId"", m.""Name"", m.""Label"", m.""Unit"", 
                       m.""Type"", m.""Active"", m.""LastReading"", m.""TenantID"",
                       COALESCE(t.""DisplayName"", '') as ""TenantName""
                FROM ""Meters"" m
                LEFT JOIN ""Tenants"" t ON m.""TenantID"" = t.""TenantID""";

            if (_whereConditions.Any())
            {
                baseQuery += " WHERE " + string.Join(" AND ", _whereConditions);
            }

            baseQuery += $" ORDER BY {_orderBy}";

            if (_limit.HasValue)
            {
                baseQuery += $" LIMIT {_limit}";

                if (_offset.HasValue)
                {
                    baseQuery += $" OFFSET {_offset}";
                }
            }

            return (baseQuery, _parameters);
        }
    }

    /// <summary>
    /// Represents the overall available date range for reading data.
    /// </summary>
    public class DateRangeInfo
    {
        /// <summary>
        /// The earliest reading timestamp.
        /// </summary>
        public DateTime? EarliestReading { get; set; }

        /// <summary>
        /// The latest reading timestamp.
        /// </summary>
        public DateTime? LatestReading { get; set; }

        /// <summary>
        /// The total number of readings.
        /// </summary>
        public long TotalReadings { get; set; }

        /// <summary>
        /// The number of meters with data.
        /// </summary>
        public int MetersWithData { get; set; }

        /// <summary>
        /// The number of days with data.
        /// </summary>
        public int DaysWithData { get; set; }

        /// <summary>
        /// Whether any data is available.
        /// </summary>
        public bool HasData { get; set; }
    }

    /// <summary>
    /// Contains suggested date ranges for dashboard display.
    /// </summary>
    public class DateRangeSuggestions
    {
        /// <summary>
        /// The suggested default start date.
        /// </summary>
        public DateTime DefaultStartDate { get; set; }

        /// <summary>
        /// The suggested default end date.
        /// </summary>
        public DateTime DefaultEndDate { get; set; }

        /// <summary>
        /// A message describing the suggestions.
        /// </summary>
        public string Message { get; set; } = string.Empty;

        /// <summary>
        /// The list of alternative date range options.
        /// </summary>
        public List<DateRangeOption> AlternativeRanges { get; set; } = new List<DateRangeOption>();
    }

    /// <summary>
    /// Represents a selectable date range option.
    /// </summary>
    public class DateRangeOption
    {
        /// <summary>
        /// The display name of the range option.
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// The start date of the range.
        /// </summary>
        public DateTime StartDate { get; set; }

        /// <summary>
        /// The end date of the range.
        /// </summary>
        public DateTime EndDate { get; set; }

        /// <summary>
        /// A description of the range option.
        /// </summary>
        public string Description { get; set; } = string.Empty;
    }
}