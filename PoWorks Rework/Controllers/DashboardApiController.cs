using Microsoft.AspNetCore.Mvc;
using PoWorks_Rework.Models;
using PoWorks_Rework.Services;

namespace PoWorks_Rework.Controllers
{
    /// <summary>
    /// API controller providing dashboard data endpoints.
    /// Serves meter readings, consumption statistics, and chart data for frontend visualization.
    /// </summary>
    public class DashboardController : BaseController
    {
        private readonly ILogger<DashboardController> _logger;
        private readonly DashboardDataService _dashboardDataService;

        /// <summary>
        /// Initializes the dashboard controller with database, logging, and data service dependencies.
        /// </summary>
        public DashboardController(
            DatabaseService databaseService,
            ILogger<DashboardController> logger,
            DashboardDataService dashboardDataService)
            : base(databaseService)
        {
            _logger = logger;
            _dashboardDataService = dashboardDataService;
        }

        /// <summary>
        /// Returns a list of all available tenants for selection in dashboard filters.
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> GetTenants()
        {
            try
            {
                var tenants = await _dashboardDataService.GetTenantsAsync();
                return Json(tenants);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in GetTenants");
                return Json(new List<object>());
            }
        }
        /// <summary>
        /// Returns suggested date ranges for the dashboard based on available reading data.
        /// </summary>
        /// <returns>JSON with a default date range and alternative ranges.</returns>
        [HttpGet]
        public async Task<IActionResult> GetDateRangeSuggestions()
        {
            try
            {
                var suggestions = await _dashboardDataService.GetDateRangeSuggestionsAsync();

                return Json(new
                {
                    success = true,
                    defaultStartDate = suggestions.DefaultStartDate.ToString("yyyy-MM-dd"),
                    defaultEndDate = suggestions.DefaultEndDate.ToString("yyyy-MM-dd"),
                    message = suggestions.Message,
                    alternatives = suggestions.AlternativeRanges.Select(alt => new
                    {
                        name = alt.Name,
                        startDate = alt.StartDate.ToString("yyyy-MM-dd"),
                        endDate = alt.EndDate.ToString("yyyy-MM-dd"),
                        description = alt.Description
                    }).ToList()
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting date range suggestions");
                return Json(new
                {
                    success = false,
                    defaultStartDate = DateTime.Now.AddDays(-30).ToString("yyyy-MM-dd"),
                    defaultEndDate = DateTime.Now.ToString("yyyy-MM-dd"),
                    message = "Error determining optimal date range. Using defaults.",
                    alternatives = new List<object>()
                });
            }
        }
        /// <summary>
        /// Returns the overall available date range and data statistics from meter readings.
        /// </summary>
        /// <returns>JSON with the earliest/latest reading dates and data counts.</returns>
        [HttpGet]
        public async Task<IActionResult> GetAvailableDateRanges()
        {
            try
            {
                var dateInfo = await _dashboardDataService.GetAvailableDateRangesAsync();

                return Json(new
                {
                    success = true,
                    hasData = dateInfo.HasData,
                    earliestReading = dateInfo.EarliestReading?.ToString("yyyy-MM-dd"),
                    latestReading = dateInfo.LatestReading?.ToString("yyyy-MM-dd"),
                    totalReadings = dateInfo.TotalReadings,
                    metersWithData = dateInfo.MetersWithData,
                    daysWithData = dateInfo.DaysWithData
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting available date ranges");
                return Json(new { success = false, hasData = false });
            }
        }
        /// <summary>
        /// Returns active meters that have readings within the requested date range, with pagination.
        /// </summary>
        /// <param name="request">The filter request containing date range, tenant, limit, and offset.</param>
        /// <returns>JSON with the list of meters that have data in the range.</returns>
        [HttpPost]
        public async Task<IActionResult> GetMetersWithData([FromBody] GetMetersRequest request)
        {
            try
            {

                DateTime? adjustedEndDate = request.EndDate.HasValue ? request.EndDate.Value.Date.AddDays(1).AddTicks(-1) : null;

                var filters = new MeterReadingFilters
                {
                    StartDate = request.StartDate,
                    EndDate = adjustedEndDate,
                    TenantId = request.TenantId,
                    Limit = Math.Max(1, Math.Min(request.Limit ?? 5, 100)),
                    Offset = Math.Max(0, request.Offset ?? 0),
                    IncludeNullTenants = request.IncludeNullTenants ?? true,
                    ActiveOnly = true
                };


                var meters = await _dashboardDataService.GetActiveMetersWithDataAsync(filters);

                return Json(new
                {
                    success = true,
                    meters = meters.Select(m => new
                    {
                        id = m.MeterId,
                        name = m.Name,
                        unit = m.Unit,
                        type = m.Type,
                        active = m.Active,
                        tenantName = m.TenantName,
                        displayName = m.FullDisplayName
                    }).ToList(),
                    limit = filters.Limit,
                    offset = filters.Offset,
                    hasMore = meters.Count >= filters.Limit,
                    dateRange = new
                    {
                        startDate = filters.StartDate?.ToString("yyyy-MM-dd"),
                        endDate = filters.EndDate?.ToString("yyyy-MM-dd")
                    },
                    message = meters.Count >= filters.Limit
                        ? $"Found {meters.Count} meters with data in date range (limit: {filters.Limit}). Use 'Load More' for additional meters."
                        : $"Found {meters.Count} meters with data in the specified date range."
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching meters with data");
                return Json(new
                {
                    success = false,
                    meters = new List<object>(),
                    error = ex.Message
                });
            }
        }
        /// <summary>
        /// Returns meters for a specific tenant, optionally filtered by a date range.
        /// </summary>
        /// <param name="tenantId">The tenant ID to filter meters by.</param>
        /// <param name="limit">The maximum number of meters to return (1-100).</param>
        /// <param name="startDate">Optional start date filter for readings.</param>
        /// <param name="endDate">Optional end date filter for readings.</param>
        /// <returns>JSON with the list of meters and whether a date filter was applied.</returns>
        [HttpGet]
        public async Task<IActionResult> GetMetersByTenant(int tenantId, int limit = 25, DateTime? startDate = null, DateTime? endDate = null)
        {
            try
            {
                if (limit <= 0 || limit > 100)
                {
                    limit = 25;
                }
                if (startDate.HasValue && endDate.HasValue)
                {
                    var filters = new MeterReadingFilters
                    {
                        TenantId = tenantId,
                        StartDate = startDate,
                        EndDate = endDate,
                        Limit = limit,
                        IncludeNullTenants = false,
                        ActiveOnly = true
                    };

                    var metersWithData = await _dashboardDataService.GetActiveMetersWithDataAsync(filters);

                    return Json(new
                    {
                        success = true,
                        meters = metersWithData.Select(m => new
                        {
                            id = m.MeterId,
                            name = m.Name,
                            unit = m.Unit,
                            type = m.Type,
                            active = m.Active,
                            tenantName = m.TenantName
                        }).ToList(),
                        limit = limit,
                        message = $"Found {metersWithData.Count} meters for tenant with data in specified date range",
                        hasDateFilter = true
                    });
                }
                else
                {
                    var meters = await _dashboardDataService.GetMetersByTenantAsync(tenantId, limit);

                    return Json(new
                    {
                        success = true,
                        meters = meters,
                        limit = limit,
                        message = $"Found {meters.Count} meters for tenant",
                        hasDateFilter = false
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching meters for tenant {TenantId}", tenantId);
                return Json(new { success = false, meters = new List<object>(), error = ex.Message });
            }
        }

        /// <summary>
        /// Returns consumption chart data and summary statistics for the requested filters.
        /// Supports period-vs-period comparison when compare dates are provided.
        /// </summary>
        /// <param name="request">The dashboard filter request containing date range, meters, and comparison range.</param>
        /// <returns>JSON with chart data, summary statistics, and optional comparison data.</returns>
        [HttpPost]
        public async Task<IActionResult> GetConsumptionData([FromBody] DashboardFilterRequest request)
        {
            try
            {
                if (!_databaseService.IsInitialized)
                {
                    return Json(_dashboardDataService.GenerateDemoChartData("Database not configured. Showing demo data."));
                }

                DateTime? adjustedEndDate = request.EndDate.HasValue ? request.EndDate.Value.Date.AddDays(1).AddTicks(-1) : null;

                var filters = new MeterReadingFilters
                {
                    DateFilter = request.DateFilter ?? "monthly",
                    TenantId = request.TenantId,
                    MeterIds = request.MeterIds ?? new List<int>(),
                    StartDate = request.StartDate,
                    EndDate = adjustedEndDate,
                    Limit = Math.Max(1, Math.Min(request.Limit ?? 5, 100)),
                    ActiveOnly = true,
                    IncludeNullTenants = true,
                    // NOTE: the old weekday-grouping "IsComparisonMode" query path is no longer
                    // driven by the frontend - period-vs-period comparison (below) replaces it.
                    IsComparisonMode = false,
                    GroupBy = request.GroupBy
                };

                var availability = await _dashboardDataService.CheckDataAvailabilityAsync(filters);


                if (!filters.MeterIds.Any())
                {
                    var topMeters = await _dashboardDataService.GetActiveMetersWithDataAsync(filters);
                    filters.MeterIds = topMeters.Select(m => m.MeterId).ToList();
                }

                var consumptionData = await _dashboardDataService.GetMeterReadingsAsync(filters);

                if (!consumptionData.Any())
                {
                    return Json(new
                    {
                        chartData = new { labels = new List<string>(), datasets = new List<object>() },
                        summary = new { totalConsumption = 0, averageDaily = 0, peakUsage = 0, activeMeters = 0 },
                        message = "No consumption data found.",
                        noDataInRange = true
                    });
                }

                var chartData = _dashboardDataService.ProcessChartData(consumptionData);
                var summary = _dashboardDataService.CalculateSummary(consumptionData);
                summary.TotalMeters = availability.ActiveMeterCount;

                // NEW: period-vs-period comparison. Re-run the exact same query (same meters,
                // same granularity) on the comparison date range provided by the frontend, so
                // both periods are directly comparable meter-by-meter.
                object compareChartDataResponse = null;
                object compareSummaryResponse = null;

                if (request.CompareStartDate.HasValue && request.CompareEndDate.HasValue)
                {
                    DateTime compareAdjustedEndDate = request.CompareEndDate.Value.Date.AddDays(1).AddTicks(-1);

                    var compareFilters = new MeterReadingFilters
                    {
                        DateFilter = filters.DateFilter,
                        TenantId = filters.TenantId,
                        MeterIds = filters.MeterIds, // identical meter selection as the current period
                        StartDate = request.CompareStartDate,
                        EndDate = compareAdjustedEndDate,
                        Limit = filters.Limit,
                        ActiveOnly = true,
                        IncludeNullTenants = true,
                        IsComparisonMode = false,
                        GroupBy = filters.GroupBy
                    };

                    var compareData = await _dashboardDataService.GetMeterReadingsAsync(compareFilters);
                    if (compareData.Any())
                    {
                        var compareChartData = _dashboardDataService.ProcessChartData(compareData);
                        var compareSummary = _dashboardDataService.CalculateSummary(compareData);
                        compareChartDataResponse = compareChartData.ToApiResponse();
                        compareSummaryResponse = compareSummary.ToDisplayObject();
                    }
                }

                return Json(new
                {
                    chartData = chartData.ToApiResponse(),
                    compareChartData = compareChartDataResponse,
                    summary = summary.ToDisplayObject(),
                    compareSummary = compareSummaryResponse,
                    message = $"Showing data for {summary.ActiveMeters} meters.",
                    dataInfo = new
                    {
                        availableMeters = availability.ActiveMeterCount,
                        shownMeters = summary.ActiveMeters,
                        totalReadings = availability.TotalReadings
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ERROR in GetConsumptionData: {Message}", ex.Message);
                return Json(_dashboardDataService.GenerateDemoChartData($"Error loading data: {ex.Message}"));
            }
        }

        /// <summary>
        /// Returns dashboard statistics including meter counts, readings, and data availability.
        /// </summary>
        /// <param name="startDate">Optional start date to check data availability for.</param>
        /// <param name="endDate">Optional end date to check data availability for.</param>
        /// <returns>JSON with dashboard statistics and availability information.</returns>
        [HttpGet]
        public async Task<IActionResult> GetDashboardStats(DateTime? startDate = null, DateTime? endDate = null)
        {
            try
            {

                DateTime? adjustedEndDate = endDate.HasValue ? endDate.Value.Date.AddDays(1).AddTicks(-1) : null;

                var filters = new MeterReadingFilters
                {
                    Limit = 1,
                    IncludeNullTenants = true,
                    StartDate = startDate,
                    EndDate = adjustedEndDate
                };


                var availability = await _dashboardDataService.CheckDataAvailabilityAsync(filters);
                var dateInfo = await _dashboardDataService.GetAvailableDateRangesAsync();

                return Json(new
                {
                    totalMeters = availability.ActiveMeterCount,
                    metersWithTenants = availability.MetersWithTenants,
                    metersWithoutTenants = availability.MetersWithoutTenants,
                    totalReadings = availability.TotalReadings,
                    hasData = availability.IsDataAvailable,
                    message = availability.GetAvailabilityMessage(),
                    dateRange = startDate.HasValue && endDate.HasValue ? new
                    {
                        startDate = startDate?.ToString("yyyy-MM-dd"),
                        endDate = endDate?.ToString("yyyy-MM-dd"),
                        hasDataInRange = availability.HasReadings
                    } : null,
                    availableDateRange = dateInfo.HasData ? new
                    {
                        earliest = dateInfo.EarliestReading?.ToString("yyyy-MM-dd"),
                        latest = dateInfo.LatestReading?.ToString("yyyy-MM-dd"),
                        totalReadings = dateInfo.TotalReadings,
                        metersWithData = dateInfo.MetersWithData,
                        daysWithData = dateInfo.DaysWithData
                    } : null
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting dashboard stats");
                return Json(new { error = ex.Message });
            }
        }
    }
    /// <summary>
    /// Request model for the dashboard consumption data endpoint.
    /// </summary>
    public class DashboardFilterRequest
    {
        /// <summary>
        /// The date aggregation filter (e.g. monthly, daily).
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
        /// The start of the primary date range.
        /// </summary>
        public DateTime? StartDate { get; set; }

        /// <summary>
        /// The end of the primary date range.
        /// </summary>
        public DateTime? EndDate { get; set; }

        /// <summary>
        /// The maximum number of meters to return.
        /// </summary>
        public int? Limit { get; set; } = 5;

        /// <summary>
        /// Indicates whether the old weekday-grouping comparison mode is active.
        /// </summary>
        public bool IsComparisonMode { get; set; }

        /// <summary>
        /// The grouping mode for the chart data (e.g. meter).
        /// </summary>
        public string GroupBy { get; set; } = "meter";

        /// <summary>
        /// Start of the comparison period for period-vs-period comparison.
        /// </summary>
        public DateTime? CompareStartDate { get; set; }

        /// <summary>
        /// End of the comparison period for period-vs-period comparison.
        /// </summary>
        public DateTime? CompareEndDate { get; set; }
    }

    /// <summary>
    /// Request model for the meters-with-data endpoint.
    /// </summary>
    public class GetMetersRequest
    {
        /// <summary>
        /// The start date of the range to check for meter readings.
        /// </summary>
        public DateTime? StartDate { get; set; }

        /// <summary>
        /// The end date of the range to check for meter readings.
        /// </summary>
        public DateTime? EndDate { get; set; }

        /// <summary>
        /// The tenant ID to filter meters by, if any.
        /// </summary>
        public int? TenantId { get; set; }

        /// <summary>
        /// The maximum number of meters to return.
        /// </summary>
        public int? Limit { get; set; } = 5;

        /// <summary>
        /// The number of meters to skip for pagination.
        /// </summary>
        public int? Offset { get; set; } = 0;

        /// <summary>
        /// Whether meters without a tenant should be included.
        /// </summary>
        public bool? IncludeNullTenants { get; set; } = true;
    }
}