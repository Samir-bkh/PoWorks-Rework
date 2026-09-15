using Microsoft.AspNetCore.Mvc;
using PoWorks_Rework.Models;
using PoWorks_Rework.Services;
using Npgsql;

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
        private readonly DashboardAnalyticsService _dashboardAnalyticsService;
        private readonly ICompanyContext _companyContext;

        /// <summary>
        /// Initializes the dashboard controller with database, logging, and data service dependencies.
        /// </summary>
        public DashboardController(
            DatabaseService databaseService,
            ILogger<DashboardController> logger,
            DashboardDataService dashboardDataService,
            DashboardAnalyticsService dashboardAnalyticsService,
            ICompanyContext companyContext)
            : base(databaseService)
        {
            _logger = logger;
            _dashboardDataService = dashboardDataService;
            _dashboardAnalyticsService = dashboardAnalyticsService;
            _companyContext = companyContext;
        }

        /// <summary>
        /// Returns a list of all available tenants for selection in dashboard filters.
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> GetTenants()
        {
            try
            {
                if (IsTenantUser && CurrentTenantId.HasValue)
                {
                    using var connection = GetDatabaseConnection();
                    await connection.OpenAsync();

                    using var cmd = new NpgsqlCommand(@"
                        SELECT t.""TenantID"",
                               COALESCE(NULLIF(td.""CompanyName"", ''), t.""DisplayName"") AS ""TenantName""
                        FROM ""Tenants"" t
                        LEFT JOIN ""TenantDetails"" td
                          ON td.""TenantID"" = t.""TenantID""
                         AND td.""CompanyId"" = t.""CompanyId""
                        WHERE t.""TenantID"" = @TenantId
                          AND t.""CompanyId"" = @CompanyId", connection);
                    cmd.Parameters.AddWithValue("TenantId", CurrentTenantId.Value);
                    cmd.Parameters.AddWithValue("CompanyId", _companyContext.CurrentCompanyId);

                    using var reader = await cmd.ExecuteReaderAsync();
                    if (await reader.ReadAsync())
                    {
                        return Json(new[]
                        {
                            new
                            {
                                id = reader.GetInt32(0),
                                name = reader.GetString(1)
                            }
                        });
                    }

                    return Json(new List<object>());
                }

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
        /// Returns the measurement catalogue used to drive metric-specific dashboard controls.
        /// </summary>
        [HttpGet]
        public IActionResult GetAnalyticsCatalog()
        {
            var metrics = MeasurementSemantics.GetDefinitions()
                .Select(definition => new
                {
                    key = definition.Key,
                    label = definition.Label,
                    canonicalUnit = definition.CanonicalUnit,
                    valueKind = definition.ValueKind,
                    defaultAggregation = definition.DefaultAggregation,
                    allowedAggregations = definition.AllowedAggregations,
                    description = definition.Description
                })
                .ToList();

            return Json(new
            {
                success = true,
                metrics,
                tenantLocked = IsTenantUser,
                currentTenantId = IsTenantUser ? CurrentTenantId : null
            });
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
                var suggestions = await _dashboardDataService.GetDateRangeSuggestionsAsync(
                    IsTenantUser ? CurrentTenantId : null);

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
                var dateInfo = await _dashboardDataService.GetAvailableDateRangesAsync(
                    IsTenantUser ? CurrentTenantId : null);

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
                    TenantId = IsTenantUser ? CurrentTenantId : request.TenantId,
                    Limit = Math.Max(1, Math.Min(request.Limit ?? 250, 2000)),
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
                        displayName = m.FullDisplayName,
                        measurementFamily = MeasurementSemantics.GetFamilyForUnit(m.Unit),
                        compatibleMetrics = MeasurementSemantics.GetCompatibleMetrics(m.Unit)
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
                if (IsTenantUser)
                {
                    if (!CurrentTenantId.HasValue)
                    {
                        return Forbid();
                    }

                    tenantId = CurrentTenantId.Value;
                }
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
        /// Returns measurement-aware dashboard analytics. The default scope is one
        /// aggregate line, as requested for the building-owner view, while tenant
        /// and individual-meter breakdowns remain available to the user.
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> GetConsumptionData(
            [FromBody] DashboardFilterRequest request)
        {
            try
            {
                if (!_databaseService.IsInitialized)
                {
                    return Json(new
                    {
                        success = false,
                        message = "Database is not configured.",
                        noDataInRange = true
                    });
                }

                var startDate = (request.StartDate ?? DateTime.Now.AddDays(-30)).Date;
                var endDate = (request.EndDate ?? DateTime.Now).Date
                    .AddDays(1)
                    .AddTicks(-1);

                var query = new DashboardAnalyticsQuery
                {
                    Metric = request.Metric,
                    ScopeMode = request.ScopeMode,
                    Aggregation = request.Aggregation,
                    DateFilter = request.DateFilter ?? "daily",
                    TenantId = IsTenantUser
                        ? CurrentTenantId
                        : request.TenantId,
                    MeterIds = request.MeterIds ?? new List<int>(),
                    StartDate = startDate,
                    EndDate = endDate,
                    MaxSeries = Math.Clamp(request.MaxSeries ?? 10, 1, 50),
                    RankingLimit = Math.Clamp(request.Limit ?? 5, 1, 25)
                };

                var current = await _dashboardAnalyticsService
                    .GetAnalyticsAsync(query);

                DashboardAnalyticsResult? comparison = null;
                DateTime? compareStart = null;
                DateTime? compareEnd = null;

                if (request.CompareStartDate.HasValue)
                {
                    // The meeting explicitly requested equal-duration comparisons.
                    // Only the comparison start is user-selected; the end is server-derived.
                    var resolvedComparison =
                        DashboardComparisonPeriodResolver.Resolve(
                            startDate,
                            endDate,
                            request.CompareStartDate.Value);

                    compareStart = resolvedComparison.StartDate;
                    compareEnd = resolvedComparison.EndDate;

                    var compareQuery = new DashboardAnalyticsQuery
                    {
                        Metric = query.Metric,
                        ScopeMode = query.ScopeMode,
                        Aggregation = query.Aggregation,
                        DateFilter = query.DateFilter,
                        TenantId = query.TenantId,
                        MeterIds = query.MeterIds.ToList(),
                        StartDate = compareStart.Value,
                        EndDate = compareEnd.Value,
                        MaxSeries = query.MaxSeries,
                        RankingLimit = query.RankingLimit
                    };

                    comparison = await _dashboardAnalyticsService
                        .GetAnalyticsAsync(compareQuery);
                }

                var noData = current.ChartData.Datasets.Count == 0;

                return Json(new
                {
                    success = true,
                    chartData = current.ChartData.ToApiResponse(),
                    summary = current.Summary.ToApiResponse(),
                    metadata = current.Metadata.ToApiResponse(),
                    ranking = current.Ranking
                        .Select(item => item.ToApiResponse())
                        .ToList(),
                    compareChartData = comparison?.ChartData.ToApiResponse(),
                    compareSummary = comparison?.Summary.ToApiResponse(),
                    compareMetadata = comparison?.Metadata.ToApiResponse(),
                    comparison = compareStart.HasValue && compareEnd.HasValue
                        ? new
                        {
                            startDate = compareStart.Value.ToString("yyyy-MM-dd"),
                            endDate = compareEnd.Value.ToString("yyyy-MM-dd"),
                            durationDays = (compareEnd.Value.Date - compareStart.Value.Date).Days + 1
                        }
                        : null,
                    noDataInRange = noData,
                    message = noData
                        ? "No compatible readings were found for this analytical view."
                        : $"Analysing {current.Summary.ActiveMeters} meter(s) as {current.Metadata.MetricLabel}."
                });
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogWarning(
                    ex,
                    "Dashboard analytics validation rejected the selected scope.");

                return Json(new
                {
                    success = false,
                    validationError = true,
                    message = ex.Message,
                    chartData = new
                    {
                        labels = Array.Empty<string>(),
                        datasets = Array.Empty<object>()
                    },
                    noDataInRange = true
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "ERROR in GetConsumptionData: {Message}",
                    ex.Message);

                return Json(new
                {
                    success = false,
                    message = "Unable to calculate dashboard analytics.",
                    error = ex.Message,
                    noDataInRange = true
                });
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
                    TenantId = IsTenantUser ? CurrentTenantId : null,
                    Limit = 1,
                    IncludeNullTenants = !IsTenantUser,
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
        /// Analytical measurement: energy, power, volume, flow, temperature,
        /// pressure, percentage or raw.
        /// </summary>
        public string Metric { get; set; } = "energy";

        /// <summary>
        /// Series scope: aggregate, tenant or meter.
        /// </summary>
        public string ScopeMode { get; set; } = "aggregate";

        /// <summary>
        /// Cross-meter aggregation. "auto" resolves to the metric's safe default.
        /// </summary>
        public string Aggregation { get; set; } = "auto";

        /// <summary>
        /// Maximum visible series for tenant/meter breakdown modes.
        /// </summary>
        public int? MaxSeries { get; set; } = 10;

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