using Microsoft.AspNetCore.Mvc;
using PoWorks_Rework.Models;
using PoWorks_Rework.Services;

namespace PoWorks_Rework.Controllers
{
    public class DashboardController : BaseController
    {
        private readonly ILogger<DashboardController> _logger;
        private readonly DashboardDataService _dashboardDataService;

        public DashboardController(
            DatabaseService databaseService,
            ILogger<DashboardController> logger,
            DashboardDataService dashboardDataService)
            : base(databaseService)
        {
            _logger = logger;
            _dashboardDataService = dashboardDataService;
        }
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
                    MeterId = request.MeterId,
                    StartDate = request.StartDate,
                    EndDate = adjustedEndDate, 
                    Limit = Math.Max(1, Math.Min(request.Limit ?? 5, 25)),
                    ActiveOnly = true,
                    IncludeNullTenants = true,
                    IsComparisonMode = request.IsComparisonMode,
                    GroupBy = request.GroupBy
                };
                

                var availability = await _dashboardDataService.CheckDataAvailabilityAsync(filters);
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

                return Json(new
                {
                    chartData = chartData.ToApiResponse(),
                    summary = summary.ToDisplayObject(),
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
    public class DashboardFilterRequest
    {
        public string DateFilter { get; set; } = "monthly";
        public int? TenantId { get; set; }
        public int? MeterId { get; set; }
        public DateTime? StartDate { get; set; }
        public DateTime? EndDate { get; set; }
        public int? Limit { get; set; } = 5;
        public bool IsComparisonMode { get; set; }
        public string GroupBy { get; set; } = "meter";
    }
    public class GetMetersRequest
    {
        public DateTime? StartDate { get; set; }
        public DateTime? EndDate { get; set; }
        public int? TenantId { get; set; }
        public int? Limit { get; set; } = 5;
        public int? Offset { get; set; } = 0;
        public bool? IncludeNullTenants { get; set; } = true;
    }
}