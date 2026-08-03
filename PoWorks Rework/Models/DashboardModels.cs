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
        public string Unit { get; set; } = "kWh";

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
        /// <summary>
        /// The total consumption across all meters.
        /// </summary>
        public double TotalConsumption { get; set; }

        /// <summary>
        /// The average daily consumption.
        /// </summary>
        public double AverageDaily { get; set; }

        /// <summary>
        /// The peak usage value.
        /// </summary>
        public double PeakUsage { get; set; }

        /// <summary>
        /// The number of active meters with data.
        /// </summary>
        public int ActiveMeters { get; set; }

        /// <summary>
        /// The total number of meters.
        /// </summary>
        public int TotalMeters { get; set; }

        /// <summary>
        /// The oldest reading timestamp.
        /// </summary>
        public DateTime? OldestReading { get; set; }

        /// <summary>
        /// The newest reading timestamp.
        /// </summary>
        public DateTime? NewestReading { get; set; }

        /// <summary>
        /// Converts the summary into a display-friendly anonymous object.
        /// </summary>
        /// <returns>An anonymous object with rounded summary values.</returns>
        public object ToDisplayObject()
        {
            return new
            {
                totalConsumption = Math.Round(TotalConsumption, 2),
                averageDaily = Math.Round(AverageDaily, 2),
                peakUsage = Math.Round(PeakUsage, 2),
                activeMeters = ActiveMeters,
                totalMeters = TotalMeters
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
        /// <summary>
        /// The dataset label.
        /// </summary>
        public string Label { get; set; } = string.Empty;

        /// <summary>
        /// The data values for this dataset.
        /// </summary>
        public List<double> Data { get; set; } = new List<double>();

        /// <summary>
        /// The background color for the dataset.
        /// </summary>
        public string BackgroundColor { get; set; } = string.Empty;

        /// <summary>
        /// The border color for the dataset.
        /// </summary>
        public string BorderColor { get; set; } = string.Empty;

        /// <summary>
        /// Converts the dataset into an API-compatible format.
        /// </summary>
        /// <returns>An anonymous object with dataset properties.</returns>
        public object ToApiFormat()
        {
            return new
            {
                label = Label,
                data = Data,
                backgroundColor = BackgroundColor,
                borderColor = BorderColor
            };
        }
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