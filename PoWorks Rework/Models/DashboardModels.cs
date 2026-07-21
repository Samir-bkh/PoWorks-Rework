using System.ComponentModel.DataAnnotations;

namespace PoWorks_Rework.Models
{
    public class MeterReadingFilters
    {
        public string DateFilter { get; set; } = "monthly";
        public int? TenantId { get; set; }
        public int? MeterId { get; set; }
        public DateTime? StartDate { get; set; }
        public DateTime? EndDate { get; set; }
        public int Limit { get; set; } = 5; 
        public int Offset { get; set; } = 0;
        public bool ActiveOnly { get; set; } = true;
        public bool IncludeNullTenants { get; set; } = true; 
        public bool IsComparisonMode { get; set; }
        public string GroupBy { get; set; } = "meter";
        public (DateTime start, DateTime end) GetDateRange()
        {
            var endDate = EndDate ?? DateTime.Now;
            var startDate = StartDate ?? endDate.AddDays(-30);
            return (startDate, endDate);
        }
    }
    public class MeterQueryResult
    {
        public int MeterId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
        public string Unit { get; set; } = "kWh";
        public string Type { get; set; } = "Energy";
        public bool Active { get; set; }
        public int? TenantId { get; set; }
        public string TenantName { get; set; } = string.Empty;
        public int LastReading { get; set; }
        public string DisplayName => string.IsNullOrEmpty(Label) ? Name : $"{Name} ({Label})";
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
    public class ConsumptionQueryResult
    {
        public int MeterId { get; set; }
        public string MeterName { get; set; } = string.Empty;
        public string Unit { get; set; } = "kWh";
        public string ReadingDate { get; set; } = string.Empty;
        public double TotalConsumption { get; set; }
        public double AvgConsumption { get; set; }
        public double MaxConsumption { get; set; }
        public int? TenantId { get; set; }
        public string TenantName { get; set; } = string.Empty;
    }
    public class DashboardSummary
    {
        public double TotalConsumption { get; set; }
        public double AverageDaily { get; set; }
        public double PeakUsage { get; set; }
        public int ActiveMeters { get; set; }
        public int TotalMeters { get; set; }
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
                totalMeters = TotalMeters
            };
        }
    }
    public class ChartDataResult
    {
        public List<string> Labels { get; set; } = new List<string>();
        public List<ChartDataset> Datasets { get; set; } = new List<ChartDataset>();
        public object ToApiResponse()
        {
            return new
            {
                labels = Labels,
                datasets = Datasets.Select(d => d.ToApiFormat()).ToList()
            };
        }
    }
    public class ChartDataset
    {
        public string Label { get; set; } = string.Empty;
        public List<double> Data { get; set; } = new List<double>();
        public string BackgroundColor { get; set; } = string.Empty;
        public string BorderColor { get; set; } = string.Empty;
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
    public class DataAvailabilityResult
    {
        public bool HasActiveMeters { get; set; }
        public bool HasReadings { get; set; }
        public int ActiveMeterCount { get; set; }
        public long TotalReadings { get; set; }
        public int MetersWithTenants { get; set; }
        public int MetersWithoutTenants { get; set; }
        public bool IsDataAvailable => HasActiveMeters && HasReadings;
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
    public class MeterQueryBuilder
    {
        private readonly List<string> _whereConditions = new List<string>();
        private readonly List<object> _parameters = new List<object>();
        private string _orderBy = @"m.""Name""";
        private int? _limit;
        private int? _offset;

        public MeterQueryBuilder ActiveOnly(bool active = true)
        {
            if (active)
            {
                _whereConditions.Add(@"m.""Active"" = true");
            }
            return this;
        }

        public MeterQueryBuilder WithTenant(int? tenantId)
        {
            if (tenantId.HasValue)
            {
                _whereConditions.Add(@"m.""TenantID"" = @TenantId");
                _parameters.Add(new { Name = "@TenantId", Value = tenantId.Value });
            }
            return this;
        }

        public MeterQueryBuilder IncludeNullTenants(bool include = true)
        {
            if (include)
            {
            }
            return this;
        }

        public MeterQueryBuilder WithLimit(int limit)
        {
            _limit = limit;
            return this;
        }

        public MeterQueryBuilder WithOffset(int offset)
        {
            _offset = offset;
            return this;
        }

        public MeterQueryBuilder OrderBy(string orderBy)
        {
            _orderBy = orderBy;
            return this;
        }

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
    public class DateRangeInfo
    {
        public DateTime? EarliestReading { get; set; }
        public DateTime? LatestReading { get; set; }
        public long TotalReadings { get; set; }
        public int MetersWithData { get; set; }
        public int DaysWithData { get; set; }
        public bool HasData { get; set; }
    }
    public class DateRangeSuggestions
    {
        public DateTime DefaultStartDate { get; set; }
        public DateTime DefaultEndDate { get; set; }
        public string Message { get; set; } = string.Empty;
        public List<DateRangeOption> AlternativeRanges { get; set; } = new List<DateRangeOption>();
    }
    public class DateRangeOption
    {
        public string Name { get; set; } = string.Empty;
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public string Description { get; set; } = string.Empty;
    }
}