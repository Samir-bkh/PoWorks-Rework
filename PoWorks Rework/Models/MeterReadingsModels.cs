// Models/MeterReadingsModels.cs - COMPLETE FIXED VERSION
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;

namespace PoWorks_Rework.Models
{
    /// <summary>
    /// Main view model for the meter readings page - COMPLETE multi-select support
    /// </summary>
    public class MeterReadingsViewModel
    {
        public MeterReadingsViewModel()
        {
            Readings = new List<MeterReading>();
            AvailableMeters = new List<MeterOption>();
            SelectedMeterIds = new List<int>(); // ADDED: Multi-select support
            MeterStats = new MeterStats();
            ViewType = "raw";
            PageSize = 50;
            CurrentPage = 1;

            // Set default date range to last 30 days
            EndDate = DateTime.Now.Date;
            StartDate = EndDate.Value.AddDays(-30);
        }

        // View Configuration
        public string ViewType { get; set; } = "raw"; // raw, daily, monthly, yearly

        // ADDED: Multiple meter selection support
        public List<int> SelectedMeterIds { get; set; } = new List<int>();

        // ADDED: Backward compatibility - returns first selected meter or null
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

        // Date Filtering
        public DateTime? StartDate { get; set; }
        public DateTime? EndDate { get; set; }

        // Pagination
        public int CurrentPage { get; set; } = 1;
        public int PageSize { get; set; } = 50;
        public int TotalPages { get; set; } = 1;
        public int TotalItems { get; set; } = 0;

        // Data Collections
        public List<MeterReading> Readings { get; set; }
        public List<MeterOption> AvailableMeters { get; set; }
        public MeterStats MeterStats { get; set; }

        // UI State
        public bool IsLoading { get; set; } = false;
        public string ErrorMessage { get; set; } = "";
        public string SuccessMessage { get; set; } = "";

        // Helper Properties
        public bool HasPreviousPage => CurrentPage > 1;
        public bool HasNextPage => CurrentPage < TotalPages;
        public int StartItemNumber => Math.Max(1, (CurrentPage - 1) * PageSize + 1);
        public int EndItemNumber => Math.Min(TotalItems, CurrentPage * PageSize);

        // Get display name for view type
        public string ViewTypeDisplayName => ViewType switch
        {
            "raw" => "Raw Readings",
            "daily" => "Daily Aggregated",
            "monthly" => "Monthly Aggregated",
            "yearly" => "Yearly Aggregated",
            _ => "Unknown View"
        };

        // ADDED: Multi-select helper properties
        public bool HasAnyMeterSelected => SelectedMeterIds.Any();
        public bool HasMultipleMetersSelected => SelectedMeterIds.Count > 1;

        // ADDED: Get selected meter names for multi-select display
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

        // ADDED: Get selected meter details
        public List<MeterOption> GetSelectedMeters()
        {
            return AvailableMeters.Where(m => SelectedMeterIds.Contains(m.MeterId)).ToList();
        }

        // ADDED: Backward compatibility
        public string SelectedMeterName => SelectedMeterNames;
    }

    /// <summary>
    /// UPDATED: Request model for filtering readings with multi-meter support
    /// </summary>
    public class MeterReadingsFilter
    {
        public List<int> MeterIds { get; set; } = new List<int>(); // CHANGED: Now supports multiple IDs
        public string ViewType { get; set; } = "raw";
        public DateTime? StartDate { get; set; }
        public DateTime? EndDate { get; set; }
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 50;

        // Backward compatibility
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

        // Validation
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

    /// <summary>
    /// Represents a meter reading (raw or aggregated)
    /// </summary>
    public class MeterReading
    {
        public int ReadingId { get; set; }
        public int MeterId { get; set; }
        public string MeterName { get; set; } = "";
        public DateTime Timestamp { get; set; }
        public decimal Value { get; set; }

        // Raw reading properties
        public int? Quality { get; set; }

        // Aggregated reading properties
        public decimal? MinValue { get; set; }
        public decimal? MaxValue { get; set; }
        public decimal? SumValue { get; set; }
        public int? ReadingCount { get; set; }

        // Additional properties for monthly/yearly views
        public int? Year { get; set; }
        public int? Month { get; set; }

        // Helper properties
        public bool IsAggregated => MinValue.HasValue || MaxValue.HasValue || ReadingCount.HasValue;
        public string FormattedTimestamp => Timestamp.ToString("yyyy-MM-dd HH:mm:ss");
        public string FormattedValue => Value.ToString("N2");

        // SIMPLIFIED: Just show the quality number
        public string QualityDescription
        {
            get
            {
                if (!Quality.HasValue)
                    return "No Quality";

                return Quality.Value.ToString();
            }
        }

        // Simple styling - just use a neutral badge
        public string QualityBadgeClass => "badge bg-info";

        // Date display helpers for different view types
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

    /// <summary>
    /// Meter option for dropdown/multi-select selection
    /// </summary>
    public class MeterOption
    {
        public int MeterId { get; set; }
        public string Name { get; set; } = "";
        public string Unit { get; set; } = "";
        public string Type { get; set; } = "";

        // Display name with unit
        public string DisplayName => string.IsNullOrEmpty(Unit) ? Name : $"{Name} ({Unit})";

        // Display name with type indicator
        public string FullDisplayName => $"{DisplayName} [{Type}]";
    }

    /// <summary>
    /// UPDATED: Statistics for meters (now handles multiple meters)
    /// </summary>
    public class MeterStats
    {
        public int ReadingCount { get; set; }
        public decimal MinValue { get; set; }
        public decimal MaxValue { get; set; }
        public decimal AvgValue { get; set; }
        public DateTime FirstReading { get; set; }
        public DateTime LastReading { get; set; }

        // ADDED: Multi-meter support
        public int MeterCount { get; set; } = 1;
        public List<string> MeterNames { get; set; } = new List<string>();

        // Calculated properties
        public decimal Range => MaxValue - MinValue;
        public TimeSpan DataSpan => LastReading - FirstReading;
        public double DaysWithData => DataSpan.TotalDays;
        public double AvgReadingsPerDay => DaysWithData > 0 ? ReadingCount / DaysWithData : 0;

        // Formatting helpers
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

        // Status indicators
        public bool HasData => ReadingCount > 0;
        public bool IsRecentData => LastReading > DateTime.Now.AddDays(-7);

        public string DataStatusClass => IsRecentData ? "text-success" : "text-warning";
        public string DataStatusText => IsRecentData ? "Recent data available" : "Data may be outdated";

        // ADDED: Multi-meter display helpers
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