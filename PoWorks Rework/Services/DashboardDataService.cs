using Microsoft.Extensions.Logging;
using Npgsql;
using PoWorks_Rework.Models;
using System.Data;

namespace PoWorks_Rework.Services
{
    /// <summary>
    /// Service providing aggregated data for dashboard displays.
    /// Retrieves consumption statistics, meter summaries, and availability checks for UI presentation.
    /// Implements multi-tenant isolation with company-level data filtering.
    /// </summary>
    public class DashboardDataService
    {
        private readonly DatabaseService _databaseService;
        private readonly ICompanyContext _companyContext;
        private readonly ConsumptionCalculationService _consumptionCalculationService;
        private readonly ILogger<DashboardDataService> _logger;

        /// <summary>
        /// Initializes the dashboard data service with database, company, and logging services.
        /// </summary>
        public DashboardDataService(
            DatabaseService databaseService,
            ICompanyContext companyContext,
            ConsumptionCalculationService consumptionCalculationService,
            ILogger<DashboardDataService> logger)
        {
            _databaseService = databaseService;
            _companyContext = companyContext;
            _consumptionCalculationService = consumptionCalculationService;
            _logger = logger;
        }

        /// <summary>
        /// Checks data availability for specified filters.
        /// Determines if meters and readings exist for the given criteria.
        /// </summary>
        public async Task<DataAvailabilityResult> CheckDataAvailabilityAsync(MeterReadingFilters filters)
        {
            var result = new DataAvailabilityResult();
            int currentCompanyId = _companyContext.CurrentCompanyId;

            try
            {
                if (!_databaseService.IsInitialized) return result;

                return await _databaseService.ExecuteWithCompanyIsolationAsync(currentCompanyId, async (connection, transaction) =>
                {
                    var (startDate, endDate) = filters.GetDateRange();

                    var query = @"
                        WITH meter_stats AS (
                            SELECT 
                                COUNT(*) as total_active,
                                COUNT(CASE WHEN m.""TenantID"" IS NOT NULL THEN 1 END) as with_tenants,
                                COUNT(CASE WHEN m.""TenantID"" IS NULL THEN 1 END) as without_tenants
                            FROM ""Meters"" m
                            WHERE m.""Active"" = true AND m.""CompanyId"" = @CompanyId
                            {0}
                        ),
                        reading_stats AS (
                            SELECT COUNT(*) as total_readings
                            FROM ""MeterReadings"" mr
                            INNER JOIN ""Meters"" m ON mr.""MeterId"" = m.""MeterId""
                            WHERE m.""Active"" = true AND m.""CompanyId"" = @CompanyId
                            AND mr.""Timestamp"" >= @StartDate 
                            AND mr.""Timestamp"" <= @EndDate
                            {0}
                        )
                        SELECT 
                            m.total_active,
                            m.with_tenants,
                            m.without_tenants,
                            r.total_readings
                        FROM meter_stats m
                        CROSS JOIN reading_stats r";

                    var tenantFilter = filters.TenantId.HasValue ? "AND m.\"TenantID\" = @TenantId" : "";
                    var finalQuery = string.Format(query, tenantFilter);

                    using var cmd = new NpgsqlCommand(finalQuery, connection, transaction);
                    cmd.Parameters.AddWithValue("@CompanyId", currentCompanyId);
                    cmd.Parameters.AddWithValue("@StartDate", startDate);
                    cmd.Parameters.AddWithValue("@EndDate", endDate);

                    if (filters.TenantId.HasValue)
                    {
                        cmd.Parameters.AddWithValue("@TenantId", filters.TenantId.Value);
                    }

                    using var reader = await cmd.ExecuteReaderAsync();
                    if (await reader.ReadAsync())
                    {
                        result.ActiveMeterCount = reader.GetInt32("total_active");
                        result.MetersWithTenants = reader.GetInt32("with_tenants");
                        result.MetersWithoutTenants = reader.GetInt32("without_tenants");
                        result.TotalReadings = reader.GetInt64("total_readings");
                        result.HasActiveMeters = result.ActiveMeterCount > 0;
                        result.HasReadings = result.TotalReadings > 0;
                    }
                    return result;
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking data availability");
            }

            return result;
        }

        /// <summary>
        /// Retrieves the overall available date range and data statistics from meter readings.
        /// </summary>
        /// <returns>A DateRangeInfo with the earliest/latest reading dates and data counts.</returns>
        public async Task<DateRangeInfo> GetAvailableDateRangesAsync()
        {
            var result = new DateRangeInfo();
            int currentCompanyId = _companyContext.CurrentCompanyId;

            try
            {
                if (!_databaseService.IsInitialized) return result;

                return await _databaseService.ExecuteWithCompanyIsolationAsync(currentCompanyId, async (connection, transaction) =>
                {
                    var query = @"
                        SELECT 
                            MIN(mr.""Timestamp"") as earliest_reading,
                            MAX(mr.""Timestamp"") as latest_reading,
                            COUNT(*) as total_readings,
                            COUNT(DISTINCT mr.""MeterId"") as meters_with_data,
                            COUNT(DISTINCT DATE(mr.""Timestamp"")) as days_with_data
                        FROM ""MeterReadings"" mr
                        INNER JOIN ""Meters"" m ON mr.""MeterId"" = m.""MeterId""
                        WHERE m.""Active"" = true AND m.""CompanyId"" = @CompanyId";

                    using var cmd = new NpgsqlCommand(query, connection, transaction);
                    cmd.Parameters.AddWithValue("@CompanyId", currentCompanyId);
                    using var reader = await cmd.ExecuteReaderAsync();

                    if (await reader.ReadAsync())
                    {
                        if (!reader.IsDBNull("earliest_reading"))
                        {
                            result.EarliestReading = reader.GetDateTime("earliest_reading");
                            result.LatestReading = reader.GetDateTime("latest_reading");
                            result.TotalReadings = reader.GetInt64("total_readings");
                            result.MetersWithData = reader.GetInt32("meters_with_data");
                            result.DaysWithData = reader.GetInt32("days_with_data");
                            result.HasData = true;
                        }
                    }
                    return result;
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting available date ranges");
            }

            return result;
        }

        /// <summary>
        /// Generates suggested date ranges for the dashboard based on available reading data.
        /// </summary>
        /// <returns>A DateRangeSuggestions with a default range and alternative options.</returns>
        public async Task<DateRangeSuggestions> GetDateRangeSuggestionsAsync()
        {
            var suggestions = new DateRangeSuggestions();
            try
            {
                var dateInfo = await GetAvailableDateRangesAsync();

                if (!dateInfo.HasData)
                {
                    suggestions.DefaultStartDate = DateTime.Now.AddDays(-30);
                    suggestions.DefaultEndDate = DateTime.Now;
                    suggestions.Message = "No meter reading data found. Using default date range.";
                    return suggestions;
                }

                var latest = dateInfo.LatestReading.Value;
                var earliest = dateInfo.EarliestReading.Value;

                if (latest > DateTime.Now.AddDays(-7))
                {
                    suggestions.DefaultStartDate = latest.AddDays(-30);
                    suggestions.DefaultEndDate = latest;
                    suggestions.Message = $"Recent data available. Showing last 30 days ending {latest:yyyy-MM-dd}.";
                }
                else if (latest > DateTime.Now.AddDays(-90))
                {
                    suggestions.DefaultStartDate = latest.AddDays(-30);
                    suggestions.DefaultEndDate = latest;
                    suggestions.Message = $"Latest data from {latest:yyyy-MM-dd}. Showing 30 days ending at latest data.";
                }
                else
                {
                    suggestions.DefaultStartDate = latest.AddDays(-60);
                    suggestions.DefaultEndDate = latest.AddDays(1);
                    suggestions.Message = $"Data available from {earliest:yyyy-MM-dd} to {latest:yyyy-MM-dd}. Showing 60 days around latest data.";
                }

                suggestions.AlternativeRanges = new List<DateRangeOption>
                {
                    new DateRangeOption { Name = "Last 7 days of data", StartDate = latest.AddDays(-6), EndDate = latest.AddDays(1), Description = "Recent week" },
                    new DateRangeOption { Name = "Last month of data", StartDate = latest.AddDays(-30), EndDate = latest.AddDays(1), Description = "Recent month" },
                    new DateRangeOption { Name = "All available data", StartDate = earliest, EndDate = latest.AddDays(1), Description = $"Full range ({(latest - earliest).Days} days)" }
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting date range suggestions");
                suggestions.DefaultStartDate = DateTime.Now.AddDays(-30);
                suggestions.DefaultEndDate = DateTime.Now;
                suggestions.Message = "Error determining optimal date range. Using defaults.";
            }

            return suggestions;
        }

        /// <summary>
        /// Retrieves active meters that have readings within the requested date range.
        /// </summary>
        /// <param name="filters">The filters to apply (date range, tenant, pagination).</param>
        /// <returns>A list of meters with their reading statistics.</returns>
        public async Task<List<MeterQueryResult>> GetActiveMetersWithDataAsync(MeterReadingFilters filters)
        {
            var meters = new List<MeterQueryResult>();
            int currentCompanyId = _companyContext.CurrentCompanyId;

            try
            {
                if (!_databaseService.IsInitialized) return meters;

                return await _databaseService.ExecuteWithCompanyIsolationAsync(currentCompanyId, async (connection, transaction) =>
                {
                    var (startDate, endDate) = filters.GetDateRange();

                    var query = @"
                        SELECT DISTINCT
                            m.""MeterId"", 
                            m.""Name"", 
                            m.""Label"", 
                            m.""Unit"", 
                            m.""Type"", 
                            m.""Active"", 
                            m.""LastReading"", 
                            m.""TenantID"",
                            COALESCE(t.""DisplayName"", '') as ""TenantName"",
                            COUNT(mr.""ReadingId"") as ""ReadingCount"",
                            MIN(mr.""Timestamp"") as ""FirstReading"",
                            MAX(mr.""Timestamp"") as ""LastReading""
                        FROM ""Meters"" m
                        LEFT JOIN ""Tenants"" t ON m.""TenantID"" = t.""TenantID""
                        INNER JOIN ""MeterReadings"" mr ON m.""MeterId"" = mr.""MeterId""
                        WHERE m.""Active"" = true AND m.""CompanyId"" = @CompanyId
                        AND mr.""Timestamp"" >= @StartDate 
                        AND mr.""Timestamp"" <= @EndDate";

                    var parameters = new List<NpgsqlParameter>
                    {
                        new NpgsqlParameter("@CompanyId", currentCompanyId),
                        new NpgsqlParameter("@StartDate", startDate),
                        new NpgsqlParameter("@EndDate", endDate)
                    };

                    if (filters.TenantId.HasValue)
                    {
                        query += " AND m.\"TenantID\" = @TenantId";
                        parameters.Add(new NpgsqlParameter("@TenantId", filters.TenantId.Value));
                    }
                    else if (!filters.IncludeNullTenants)
                    {
                        query += " AND m.\"TenantID\" IS NOT NULL";
                    }

                    query += @"
                        GROUP BY m.""MeterId"", m.""Name"", m.""Label"", m.""Unit"", m.""Type"", 
                                 m.""Active"", m.""LastReading"", m.""TenantID"", t.""DisplayName""
                        ORDER BY COUNT(mr.""ReadingId"") DESC, m.""Name""
                        LIMIT @Limit OFFSET @Offset";

                    parameters.Add(new NpgsqlParameter("@Limit", filters.Limit));
                    parameters.Add(new NpgsqlParameter("@Offset", filters.Offset));

                    using var cmd = new NpgsqlCommand(query, connection, transaction);
                    foreach (var param in parameters) cmd.Parameters.Add(param);

                    using var reader = await cmd.ExecuteReaderAsync();
                    while (await reader.ReadAsync())
                    {
                        meters.Add(new MeterQueryResult
                        {
                            MeterId = reader.GetInt32("MeterId"),
                            Name = reader.GetString("Name"),
                            Label = reader.IsDBNull("Label") ? string.Empty : reader.GetString("Label"),
                            Unit = reader.IsDBNull("Unit") ? "kWh" : reader.GetString("Unit"),
                            Type = reader.IsDBNull("Type") ? "Energy" : reader.GetString("Type"),
                            Active = reader.GetBoolean("Active"),
                            TenantId = reader.IsDBNull("TenantID") ? null : reader.GetInt32("TenantID"),
                            TenantName = reader.IsDBNull("TenantName") ? string.Empty : reader.GetString("TenantName"),
                            LastReading = reader.IsDBNull("LastReading") ? 0 : reader.GetInt32("LastReading")
                        });
                    }
                    return meters;
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting active meters with data");
            }

            return meters;
        }

        /// <summary>
        /// Retrieves active meters for the current company with optional filters.
        /// </summary>
        /// <param name="filters">The filters to apply (active only, tenant, pagination).</param>
        /// <returns>A list of meters.</returns>
        public async Task<List<MeterQueryResult>> GetActiveMetersAsync(MeterReadingFilters filters)
        {
            var meters = new List<MeterQueryResult>();
            int currentCompanyId = _companyContext.CurrentCompanyId;

            try
            {
                if (!_databaseService.IsInitialized) return meters;

                return await _databaseService.ExecuteWithCompanyIsolationAsync(currentCompanyId, async (connection, transaction) =>
                {
                    var query = @"
                        SELECT m.""MeterId"", m.""Name"", m.""Label"", m.""Unit"", 
                               m.""Type"", m.""Active"", m.""LastReading"", m.""TenantID"",
                               COALESCE(t.""DisplayName"", '') as ""TenantName""
                        FROM ""Meters"" m
                        LEFT JOIN ""Tenants"" t ON m.""TenantID"" = t.""TenantID""
                        WHERE m.""CompanyId"" = @CompanyId";

                    var whereConditions = new List<string>();
                    var parameters = new List<NpgsqlParameter>
                    {
                        new NpgsqlParameter("@CompanyId", currentCompanyId)
                    };

                    if (filters.ActiveOnly) whereConditions.Add(@"m.""Active"" = true");

                    if (filters.TenantId.HasValue)
                    {
                        whereConditions.Add(@"m.""TenantID"" = @TenantId");
                        parameters.Add(new NpgsqlParameter("@TenantId", filters.TenantId.Value));
                    }
                    else if (!filters.IncludeNullTenants)
                    {
                        whereConditions.Add(@"m.""TenantID"" IS NOT NULL");
                    }

                    if (whereConditions.Any()) query += " AND " + string.Join(" AND ", whereConditions);

                    query += " ORDER BY m.\"Name\" LIMIT @Limit OFFSET @Offset";
                    parameters.Add(new NpgsqlParameter("@Limit", filters.Limit));
                    parameters.Add(new NpgsqlParameter("@Offset", filters.Offset));

                    using var cmd = new NpgsqlCommand(query, connection, transaction);
                    foreach (var param in parameters) cmd.Parameters.Add(param);

                    using var reader = await cmd.ExecuteReaderAsync();
                    while (await reader.ReadAsync())
                    {
                        meters.Add(new MeterQueryResult
                        {
                            MeterId = reader.GetInt32("MeterId"),
                            Name = reader.GetString("Name"),
                            Label = reader.IsDBNull("Label") ? string.Empty : reader.GetString("Label"),
                            Unit = reader.IsDBNull("Unit") ? "kWh" : reader.GetString("Unit"),
                            Type = reader.IsDBNull("Type") ? "Energy" : reader.GetString("Type"),
                            Active = reader.GetBoolean("Active"),
                            TenantId = reader.IsDBNull("TenantID") ? null : reader.GetInt32("TenantID"),
                            TenantName = reader.IsDBNull("TenantName") ? string.Empty : reader.GetString("TenantName"),
                            LastReading = reader.IsDBNull("LastReading") ? 0 : reader.GetInt32("LastReading")
                        });
                    }
                    return meters;
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting active meters");
            }

            return meters;
        }

        /// <summary>
        /// Retrieves consumption data from meter readings with aggregation and filtering.
        /// Supports comparison mode and various date grouping levels.
        /// </summary>
        /// <param name="filters">The filters to apply (date range, meters, grouping).</param>
        /// <returns>A list of consumption query results.</returns>
        public async Task<List<ConsumptionQueryResult>> GetMeterReadingsAsync(MeterReadingFilters filters)
        {
            try
            {
                return await _consumptionCalculationService.GetConsumptionSeriesAsync(filters);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting normalized meter consumption");
                return new List<ConsumptionQueryResult>();
            }
        }

        /// <summary>
        /// Processes consumption data into chart-ready datasets grouped by meter.
        /// </summary>
        /// <param name="data">The consumption data to process.</param>
        /// <returns>A ChartDataResult with labels and datasets.</returns>
        public ChartDataResult ProcessChartData(List<ConsumptionQueryResult> data)
        {
            var result = new ChartDataResult();
            if (!data.Any()) return result;

            result.Labels = data.Select(d => d.ReadingDate).Distinct().OrderBy(x => x).ToList();
            var meterGroups = data.GroupBy(d => new { d.MeterId, d.MeterName, d.Unit, d.TenantName });

            var colors = new[]
            {
                "#2563EB", "#0EA5E9", "#10B981", "#8B5CF6",
                "#F59E0B", "#EF4444", "#14B8A6", "#6366F1"
            };
            int colorIndex = 0;

            foreach (var meterGroup in meterGroups)
            {
                var color = colors[colorIndex % colors.Length];
                var dataset = new ChartDataset
                {
                    MeterId = meterGroup.Key.MeterId,
                    Label = BuildMeterLabel(meterGroup.Key.MeterName, meterGroup.Key.Unit, meterGroup.Key.TenantName),
                    MeterName = meterGroup.Key.MeterName,
                    Unit = string.IsNullOrWhiteSpace(meterGroup.Key.Unit) ? "unit" : meterGroup.Key.Unit,
                    TenantName = meterGroup.Key.TenantName,
                    BackgroundColor = color,
                    BorderColor = color,
                    Data = result.Labels
                        .Select(label => meterGroup.FirstOrDefault(d => d.ReadingDate == label)?.TotalConsumption)
                        .ToList()
                };
                result.Datasets.Add(dataset);
                colorIndex++;
            }

            return result;
        }

        /// <summary>
        /// Calculates summary statistics from the consumption data.
        /// </summary>
        /// <param name="data">The consumption data to summarize.</param>
        /// <returns>A DashboardSummary with totals and averages.</returns>
        public DashboardSummary CalculateSummary(
            List<ConsumptionQueryResult> data,
            MeterReadingFilters filters)
        {
            var summary = new DashboardSummary();
            if (!data.Any()) return summary;

            var units = data
                .Select(d => string.IsNullOrWhiteSpace(d.Unit) ? "unit" : d.Unit.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            summary.HasMixedUnits = units.Count > 1;
            summary.Unit = summary.HasMixedUnits ? "mixed" : units[0];
            summary.TotalConsumption = data.Sum(d => d.TotalConsumption);
            summary.ActiveMeters = data.Select(d => d.MeterId).Distinct().Count();
            summary.DataBuckets = data.Select(d => d.ReadingDate).Distinct().Count();

            var (startDate, endDate) = filters.GetDateRange();
            summary.PeriodDays = Math.Max(1, (endDate.Date - startDate.Date).Days + 1);
            summary.AverageDaily = summary.TotalConsumption / summary.PeriodDays;

            // Peak means the highest total consumption bucket across all selected
            // meters, not the biggest individual meter value. This is what an
            // operator expects when looking for the peak of the displayed scope.
            summary.PeakUsage = data
                .GroupBy(d => d.ReadingDate)
                .Select(bucket => bucket.Sum(x => x.TotalConsumption))
                .DefaultIfEmpty(0)
                .Max();

            summary.PeakPeriodLabel = filters.DateFilter?.ToLowerInvariant() switch
            {
                "yearly" => "Highest annual total",
                "monthly" => "Highest monthly total",
                "hourly" => "Highest hourly total",
                _ => "Highest daily total"
            };

            return summary;
        }

        /// <summary>
        /// Generates sample chart data for demonstration purposes when no real data is available.
        /// </summary>
        /// <param name="message">The message to display with the demo data.</param>
        /// <returns>An object containing demo chart data and summary.</returns>
        public object GenerateDemoChartData(string message = "This is sample data to demonstrate the chart functionality.")
        {
            var labels = new List<string>();
            var sampleData1 = new List<double>();
            var sampleData2 = new List<double>();
            var random = new Random();

            for (int i = 6; i >= 0; i--)
            {
                var date = DateTime.Now.AddDays(-i);
                labels.Add(date.ToString("yyyy-MM-dd"));
                var baseValue1 = 150 + (i * 10);
                var baseValue2 = 200 + (i * 15);
                sampleData1.Add(baseValue1 + (random.NextDouble() * 50));
                sampleData2.Add(baseValue2 + (random.NextDouble() * 80));
            }

            var chartData = new
            {
                labels = labels,
                datasets = new object[]
                {
                    new { meterId = 1, label = "Sample Meter 1 (kWh)", meterName = "Sample Meter 1", tenantName = "Demo", unit = "kWh", data = sampleData1, backgroundColor = "#FF6384", borderColor = "#FF6384" },
                    new { meterId = 2, label = "Sample Meter 2 (kWh)", meterName = "Sample Meter 2", tenantName = "Demo", unit = "kWh", data = sampleData2, backgroundColor = "#36A2EB", borderColor = "#36A2EB" }
                }
            };

            var summary = new DashboardSummary
            {
                TotalConsumption = sampleData1.Sum() + sampleData2.Sum(),
                AverageDaily = (sampleData1.Sum() + sampleData2.Sum()) / 7,
                PeakUsage = Math.Max(sampleData1.Max(), sampleData2.Max()),
                ActiveMeters = 2,
                TotalMeters = 2
            };

            return new { chartData = chartData, summary = summary.ToDisplayObject(), message = message, isDemoData = true };
        }

        /// <summary>
        /// Retrieves the list of active tenants for the current company.
        /// </summary>
        /// <returns>A list of tenant objects with ID and name.</returns>
        public async Task<List<object>> GetTenantsAsync()
        {
            var tenants = new List<object>();
            int currentCompanyId = _companyContext.CurrentCompanyId;

            try
            {
                if (!_databaseService.IsInitialized) return tenants;

                return await _databaseService.ExecuteWithCompanyIsolationAsync(currentCompanyId, async (connection, transaction) =>
                {
                    var query = @"
                        SELECT t.""TenantID"" as Id, 
                               td.""CompanyName"" as Name,
                               td.""Active""
                        FROM ""Tenants"" t
                        INNER JOIN ""TenantDetails"" td ON t.""TenantID"" = td.""TenantID""
                        WHERE td.""Active"" = true AND t.""CompanyId"" = @CompanyId
                        ORDER BY td.""CompanyName""";

                    using var cmd = new NpgsqlCommand(query, connection, transaction);
                    cmd.Parameters.AddWithValue("@CompanyId", currentCompanyId);
                    using var reader = await cmd.ExecuteReaderAsync();

                    while (await reader.ReadAsync())
                    {
                        tenants.Add(new { id = reader.GetInt32("Id"), name = reader.GetString("Name") });
                    }
                    return tenants;
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching tenants");
            }

            return tenants;
        }

        /// <summary>
        /// Retrieves the active meters belonging to a specific tenant.
        /// </summary>
        /// <param name="tenantId">The tenant ID to filter meters by.</param>
        /// <param name="limit">The maximum number of meters to return.</param>
        /// <returns>A list of meter objects.</returns>
        public async Task<List<object>> GetMetersByTenantAsync(int tenantId, int limit = 100)
        {
            var meters = new List<object>();
            int currentCompanyId = _companyContext.CurrentCompanyId;

            try
            {
                if (!_databaseService.IsInitialized) return meters;

                return await _databaseService.ExecuteWithCompanyIsolationAsync(currentCompanyId, async (connection, transaction) =>
                {
                    var query = @"
                        SELECT ""MeterId"" as id,
                               ""Name"" as name,
                               ""Unit"" as unit,
                               ""Type"" as type,
                               ""Active"" as active
                        FROM ""Meters""
                        WHERE ""CompanyId"" = @CompanyId AND ""TenantID"" = @TenantId AND ""Active"" = true
                        ORDER BY ""Name""
                        LIMIT @Limit";

                    using var cmd = new NpgsqlCommand(query, connection, transaction);
                    cmd.Parameters.AddWithValue("@CompanyId", currentCompanyId);
                    cmd.Parameters.AddWithValue("@TenantId", tenantId);
                    cmd.Parameters.AddWithValue("@Limit", limit);
                    using var reader = await cmd.ExecuteReaderAsync();

                    while (await reader.ReadAsync())
                    {
                        meters.Add(new
                        {
                            id = reader.GetInt32("id"),
                            name = reader.GetString("name"),
                            unit = reader.IsDBNull("unit") ? "kWh" : reader.GetString("unit"),
                            type = reader.IsDBNull("type") ? "Energy" : reader.GetString("type"),
                            active = reader.GetBoolean("active")
                        });
                    }
                    return meters;
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching meters for tenant {TenantId}", tenantId);
            }

            return meters;
        }

        /// <summary>
        /// Builds a display label for a meter including unit and tenant information.
        /// </summary>
        /// <param name="meterName">The meter name.</param>
        /// <param name="unit">The meter unit.</param>
        /// <param name="tenantName">The tenant name.</param>
        /// <returns>The formatted meter label.</returns>
        private string BuildMeterLabel(string meterName, string unit, string tenantName)
        {
            var label = meterName;
            if (!string.IsNullOrWhiteSpace(unit))
            {
                label += $" ({unit})";
            }
            if (!string.IsNullOrWhiteSpace(tenantName) && tenantName != meterName)
            {
                label += $" - {tenantName}";
            }

            return label;
        }
    }
}