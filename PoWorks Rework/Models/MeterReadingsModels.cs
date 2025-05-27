// Models/MeterReadingsModels.cs - FIXED VERSION
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;

namespace PoWorks_Rework.Models
{
    /// <summary>
    /// Main view model for the meter readings page
    /// </summary>
    public class MeterReadingsViewModel
    {
        public MeterReadingsViewModel()
        {
            Readings = new List<MeterReading>();
            AvailableMeters = new List<MeterOption>();
            MeterStats = new MeterStats();
            ViewType = "raw";
            PageSize = 50;
            CurrentPage = 1;

            // Set default date range to last 30 days
            EndDate = DateTime.Now.Date;
            StartDate = EndDate.Value.AddDays(-30); // Fixed: Added System.Linq for extension methods
        }

        // View Configuration
        public string ViewType { get; set; } = "raw"; // raw, daily, monthly, yearly
        public int? SelectedMeterId { get; set; }

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

        // Get selected meter name
        public string SelectedMeterName
        {
            get
            {
                if (!SelectedMeterId.HasValue) return "All Meters";
                var meter = AvailableMeters.FirstOrDefault(m => m.MeterId == SelectedMeterId.Value);
                return meter?.Name ?? "Unknown Meter";
            }
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

        public string QualityDescription => Quality switch
        {
            0 => "Good",
            1 => "Uncertain",
            2 => "Bad",
            _ => "Unknown"
        };

        public string QualityBadgeClass => Quality switch
        {
            0 => "badge bg-success",
            1 => "badge bg-warning",
            2 => "badge bg-danger",
            _ => "badge bg-secondary"
        };

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
    /// Meter option for dropdown selection
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
    /// Statistics for a meter
    /// </summary>
    public class MeterStats
    {
        public int ReadingCount { get; set; }
        public decimal MinValue { get; set; }
        public decimal MaxValue { get; set; }
        public decimal AvgValue { get; set; }
        public DateTime FirstReading { get; set; }
        public DateTime LastReading { get; set; }

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
    }

    /// <summary>
    /// Request model for filtering readings
    /// </summary>
    public class MeterReadingsFilter
    {
        public int? MeterId { get; set; }
        public string ViewType { get; set; } = "raw";
        public DateTime? StartDate { get; set; }
        public DateTime? EndDate { get; set; }
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 50;

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
                return "Invalid view type. Must be: raw, daily, monthly, or yearly";

            return "";
        }
    }

    /// <summary>
    /// Response model for AJAX requests
    /// </summary>
    public class MeterReadingsResponse
    {
        public bool Success { get; set; }
        public string Error { get; set; } = "";
        public List<MeterReading> Data { get; set; } = new List<MeterReading>();
        public MeterReadingsPagination Pagination { get; set; } = new MeterReadingsPagination();
        public MeterStats Stats { get; set; } = new MeterStats();
    }

    /// <summary>
    /// Pagination information for AJAX responses
    /// </summary>
    public class MeterReadingsPagination
    {
        public int CurrentPage { get; set; }
        public int TotalPages { get; set; }
        public int TotalCount { get; set; }
        public int PageSize { get; set; }
        public bool HasPreviousPage => CurrentPage > 1;
        public bool HasNextPage => CurrentPage < TotalPages;
    }

    /// <summary>
    /// Chart data for visualizations
    /// </summary>
    public class MeterReadingsChartData
    {
        public List<ChartDataPoint> DataPoints { get; set; } = new List<ChartDataPoint>();
        public string Label { get; set; } = "";
        public string Unit { get; set; } = "";
        public string ViewType { get; set; } = "";
    }

    /// <summary>
    /// Individual chart data point
    /// </summary>
    public class ChartDataPoint
    {
        public DateTime Timestamp { get; set; }
        public decimal Value { get; set; }
        public string Label => Timestamp.ToString("yyyy-MM-dd HH:mm");
        public string FormattedValue => Value.ToString("N2");
    }

    /// <summary>
    /// Export options for meter readings
    /// </summary>
    public class MeterReadingsExportOptions
    {
        public string Format { get; set; } = "csv"; // csv, excel, json
        public int? MeterId { get; set; }
        public string ViewType { get; set; } = "raw";
        public DateTime? StartDate { get; set; }
        public DateTime? EndDate { get; set; }
        public bool IncludeStats { get; set; } = true;
        public bool IncludeChartData { get; set; } = false;

        public string GetFileName()
        {
            var meterName = MeterId.HasValue ? $"meter_{MeterId}" : "all_meters";
            var dateRange = StartDate.HasValue && EndDate.HasValue
                ? $"_{StartDate:yyyyMMdd}_{EndDate:yyyyMMdd}"
                : "";
            var timestamp = DateTime.Now.ToString("yyyyMMddHHmmss");

            return $"meter_readings_{ViewType}_{meterName}{dateRange}_{timestamp}.{Format}";
        }
    }
}