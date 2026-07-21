namespace PoWorks_Rework.Models
{
    public class MeterReadingsViewModel
    {
        public MeterReadingsViewModel()
        {
            Readings = new List<MeterReading>();
            AvailableMeters = new List<MeterOption>();
            SelectedMeterIds = new List<int>(); 
            MeterStats = new MeterStats();
            ViewType = "raw";
            PageSize = 50;
            CurrentPage = 1;
            EndDate = DateTime.Now.Date;
            StartDate = EndDate.Value.AddDays(-30);
        }
        public string ViewType { get; set; } = "raw"; 
        public List<int> SelectedMeterIds { get; set; } = new List<int>();
        public int? SelectedMeterId
        {
            get => SelectedMeterIds.FirstOrDefault() == 0 ? null : SelectedMeterIds.FirstOrDefault();
            set
            {
                if (value.HasValue && value.Value > 0)
                {
                    SelectedMeterIds = new List<int> { value.Value };
                }
                else
                {
                    SelectedMeterIds = new List<int>();
                }
            }
        }
        public DateTime? StartDate { get; set; }
        public DateTime? EndDate { get; set; }
        public int CurrentPage { get; set; } = 1;
        public int PageSize { get; set; } = 50;
        public int TotalPages { get; set; } = 1;
        public int TotalItems { get; set; } = 0;
        public List<MeterReading> Readings { get; set; }
        public List<MeterOption> AvailableMeters { get; set; }
        public MeterStats MeterStats { get; set; }
        public bool IsLoading { get; set; } = false;
        public string ErrorMessage { get; set; } = "";
        public string SuccessMessage { get; set; } = "";
        public bool HasPreviousPage => CurrentPage > 1;
        public bool HasNextPage => CurrentPage < TotalPages;
        public int StartItemNumber => Math.Max(1, (CurrentPage - 1) * PageSize + 1);
        public int EndItemNumber => Math.Min(TotalItems, CurrentPage * PageSize);
        public string ViewTypeDisplayName => ViewType switch
        {
            "raw" => "Raw Readings",
            "daily" => "Daily Aggregated",
            "monthly" => "Monthly Aggregated",
            "yearly" => "Yearly Aggregated",
            _ => "Unknown View"
        };
        public bool HasAnyMeterSelected => SelectedMeterIds.Any();
        public bool HasMultipleMetersSelected => SelectedMeterIds.Count > 1;
        public string SelectedMeterNames
        {
            get
            {
                if (!SelectedMeterIds.Any()) return "All Meters";

                if (SelectedMeterIds.Count == 1)
                {
                    var meter = AvailableMeters.FirstOrDefault(m => m.MeterId == SelectedMeterIds.First());
                    return meter?.Name ?? "Unknown Meter";
                }

                var selectedMeters = AvailableMeters.Where(m => SelectedMeterIds.Contains(m.MeterId)).ToList();
                if (selectedMeters.Count <= 3)
                {
                    return string.Join(", ", selectedMeters.Select(m => m.Name));
                }
                else
                {
                    return $"{selectedMeters.First().Name} and {selectedMeters.Count - 1} others";
                }
            }
        }
        public List<MeterOption> GetSelectedMeters()
        {
            return AvailableMeters.Where(m => SelectedMeterIds.Contains(m.MeterId)).ToList();
        }
        public string SelectedMeterName => SelectedMeterNames;
    }
    public class MeterReadingsFilter
    {
        public List<int> MeterIds { get; set; } = new List<int>(); 
        public string ViewType { get; set; } = "raw";
        public DateTime? StartDate { get; set; }
        public DateTime? EndDate { get; set; }
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 50;
        public int? MeterId
        {
            get => MeterIds.FirstOrDefault() == 0 ? null : MeterIds.FirstOrDefault();
            set
            {
                if (value.HasValue && value.Value > 0)
                {
                    MeterIds = new List<int> { value.Value };
                }
                else
                {
                    MeterIds = new List<int>();
                }
            }
        }
        public bool IsValid()
        {
            if (Page < 1) return false;
            if (PageSize < 1 || PageSize > 1000) return false;
            if (StartDate.HasValue && EndDate.HasValue && StartDate > EndDate) return false;

            var validViewTypes = new[] { "raw", "daily", "monthly", "yearly" };
            if (!validViewTypes.Contains(ViewType.ToLower())) return false;

            return true;
        }

        public string GetValidationError()
        {
            if (Page < 1) return "Page number must be greater than 0";
            if (PageSize < 1) return "Page size must be greater than 0";
            if (PageSize > 1000) return "Page size cannot exceed 1000";
            if (StartDate.HasValue && EndDate.HasValue && StartDate > EndDate)
                return "Start date cannot be after end date";

            var validViewTypes = new[] { "raw", "daily", "monthly", "yearly" };
            if (!validViewTypes.Contains(ViewType.ToLower()))
                return "Invalid view type. Valid types are: " + string.Join(", ", validViewTypes);

            return null;
        }
    }
    public class MeterReading
    {
        public int ReadingId { get; set; }
        public int MeterId { get; set; }
        public string MeterName { get; set; } = "";
        public DateTime Timestamp { get; set; }
        public decimal Value { get; set; }
        public int? Quality { get; set; }
        public decimal? MinValue { get; set; }
        public decimal? MaxValue { get; set; }
        public decimal? SumValue { get; set; }
        public int? ReadingCount { get; set; }
        public int? Year { get; set; }
        public int? Month { get; set; }
        public bool IsAggregated => MinValue.HasValue || MaxValue.HasValue || ReadingCount.HasValue;
        public string FormattedTimestamp => Timestamp.ToString("yyyy-MM-dd HH:mm:ss");
        public string FormattedValue => Value.ToString("N2");
        public string QualityDescription
        {
            get
            {
                if (!Quality.HasValue)
                    return "No Quality";

                return Quality.Value.ToString();
            }
        }
        public string QualityBadgeClass => "badge bg-info";
        public string GetDateDisplay(string viewType)
        {
            return viewType switch
            {
                "daily" => Timestamp.ToString("yyyy-MM-dd"),
                "monthly" => Timestamp.ToString("yyyy-MM"),
                "yearly" => Timestamp.ToString("yyyy"),
                _ => Timestamp.ToString("yyyy-MM-dd HH:mm:ss")
            };
        }

        public string GetAggregationSummary()
        {
            if (!IsAggregated) return "";

            var parts = new List<string>();
            if (ReadingCount.HasValue) parts.Add($"{ReadingCount} readings");
            if (MinValue.HasValue) parts.Add($"Min: {MinValue:N2}");
            if (MaxValue.HasValue) parts.Add($"Max: {MaxValue:N2}");
            if (SumValue.HasValue) parts.Add($"Sum: {SumValue:N2}");

            return string.Join(" | ", parts);
        }
    }
    public class MeterOption
    {
        public int MeterId { get; set; }
        public string Name { get; set; } = "";
        public string Unit { get; set; } = "";
        public string Type { get; set; } = "";
        public string DisplayName => string.IsNullOrEmpty(Unit) ? Name : $"{Name} ({Unit})";
        public string FullDisplayName => $"{DisplayName} [{Type}]";
    }
    public class MeterStats
    {
        public int ReadingCount { get; set; }
        public decimal MinValue { get; set; }
        public decimal MaxValue { get; set; }
        public decimal AvgValue { get; set; }
        public DateTime FirstReading { get; set; }
        public DateTime LastReading { get; set; }
        public int MeterCount { get; set; } = 1;
        public List<string> MeterNames { get; set; } = new List<string>();
        public decimal Range => MaxValue - MinValue;
        public TimeSpan DataSpan => LastReading - FirstReading;
        public double DaysWithData => DataSpan.TotalDays;
        public double AvgReadingsPerDay => DaysWithData > 0 ? ReadingCount / DaysWithData : 0;
        public string FormattedMinValue => MinValue.ToString("N2");
        public string FormattedMaxValue => MaxValue.ToString("N2");
        public string FormattedAvgValue => AvgValue.ToString("N2");
        public string FormattedRange => Range.ToString("N2");

        public string FormattedFirstReading => FirstReading == DateTime.MinValue ? "No data" : FirstReading.ToString("yyyy-MM-dd HH:mm");
        public string FormattedLastReading => LastReading == DateTime.MinValue ? "No data" : LastReading.ToString("yyyy-MM-dd HH:mm");

        public string FormattedDataSpan
        {
            get
            {
                if (DataSpan.TotalDays <= 0) return "No data";
                if (DataSpan.TotalDays < 1) return $"{DataSpan.TotalHours:N1} hours";
                if (DataSpan.TotalDays < 30) return $"{DataSpan.TotalDays:N1} days";
                if (DataSpan.TotalDays < 365) return $"{DataSpan.TotalDays / 30:N1} months";
                return $"{DataSpan.TotalDays / 365:N1} years";
            }
        }

        public string FormattedAvgReadingsPerDay => AvgReadingsPerDay.ToString("N1");
        public bool HasData => ReadingCount > 0;
        public bool IsRecentData => LastReading > DateTime.Now.AddDays(-7);

        public string DataStatusClass => IsRecentData ? "text-success" : "text-warning";
        public string DataStatusText => IsRecentData ? "Recent data available" : "Data may be outdated";
        public string MeterSummary
        {
            get
            {
                if (MeterCount <= 1) return MeterNames.FirstOrDefault() ?? "Single Meter";
                if (MeterCount <= 3) return string.Join(", ", MeterNames);
                return $"{MeterNames.FirstOrDefault()} and {MeterCount - 1} others";
            }
        }
    }
}