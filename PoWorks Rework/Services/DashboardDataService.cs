// Services/DashboardDataService.cs - COMPLETE: All Methods Including Missing Ones
using Npgsql;
using PoWorks_Rework.Models;
using System.Data;

namespace PoWorks_Rework.Services
{
    /// <summary>
    /// COMPLETE: Dashboard service with all methods including the missing ones
    /// </summary>
    public class DashboardDataService
    {
        private readonly DatabaseService _databaseService;
        private readonly ILogger<DashboardDataService> _logger;

        public DashboardDataService(DatabaseService databaseService, ILogger<DashboardDataService> logger)
        {
            _databaseService = databaseService;
            _logger = logger;
        }

        /// <summary>
        /// FIXED: Smart data availability check that considers date range
        /// </summary>
        public async Task<DataAvailabilityResult> CheckDataAvailabilityAsync(MeterReadingFilters filters)
        {
            var result = new DataAvailabilityResult();

            try
            {
                if (!_databaseService.IsInitialized)
                {
                    _logger.LogWarning("Database service not initialized");
                    return result;
                }

                using var connection = _databaseService.GetConnection();

                // FIXED: Single query that considers date range for readings
                var (startDate, endDate) = filters.GetDateRange();

                var query = @"
                    WITH meter_stats AS (
                        SELECT 
                            COUNT(*) as total_active,
                            COUNT(CASE WHEN ""TenantID"" IS NOT NULL THEN 1 END) as with_tenants,
                            COUNT(CASE WHEN ""TenantID"" IS NULL THEN 1 END) as without_tenants
                        FROM ""Meters"" 
                        WHERE ""Active"" = true
                        {0}
                    ),
                    reading_stats AS (
                        SELECT COUNT(*) as total_readings
                        FROM ""MeterReadings"" mr
                        INNER JOIN ""Meters"" m ON mr.""MeterId"" = m.""MeterId""
                        WHERE m.""Active"" = true
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

                var tenantFilter = filters.TenantId.HasValue ? "AND \"TenantID\" = @TenantId" : "";
                var finalQuery = string.Format(query, tenantFilter);

                using var cmd = new NpgsqlCommand(finalQuery, connection);
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

                _logger.LogInformation("Data availability for date range {StartDate} to {EndDate}: {Message}",
                    startDate.ToString("yyyy-MM-dd"), endDate.ToString("yyyy-MM-dd"), result.GetAvailabilityMessage());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking data availability");
            }

            return result;
        }

        /// <summary>
        /// NEW: Discover available date ranges in the database
        /// </summary>
        public async Task<DateRangeInfo> GetAvailableDateRangesAsync()
        {
            var result = new DateRangeInfo();

            try
            {
                if (!_databaseService.IsInitialized)
                {
                    return result;
                }

                using var connection = _databaseService.GetConnection();

                var query = @"
                    SELECT 
                        MIN(mr.""Timestamp"") as earliest_reading,
                        MAX(mr.""Timestamp"") as latest_reading,
                        COUNT(*) as total_readings,
                        COUNT(DISTINCT mr.""MeterId"") as meters_with_data,
                        COUNT(DISTINCT DATE(mr.""Timestamp"")) as days_with_data
                    FROM ""MeterReadings"" mr
                    INNER JOIN ""Meters"" m ON mr.""MeterId"" = m.""MeterId""
                    WHERE m.""Active"" = true";

                using var cmd = new NpgsqlCommand(query, connection);
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

                _logger.LogInformation("Available date range: {Earliest} to {Latest} ({TotalReadings} readings, {MetersWithData} meters)",
                    result.EarliestReading?.ToString("yyyy-MM-dd"),
                    result.LatestReading?.ToString("yyyy-MM-dd"),
                    result.TotalReadings,
                    result.MetersWithData);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting available date ranges");
            }

            return result;
        }

        /// <summary>
        /// NEW: Get intelligent date range suggestions based on available data
        /// </summary>
        public async Task<DateRangeSuggestions> GetDateRangeSuggestionsAsync()
        {
            var suggestions = new DateRangeSuggestions();

            try
            {
                var dateInfo = await GetAvailableDateRangesAsync();

                if (!dateInfo.HasData)
                {
                    // No data available - return default suggestions
                    suggestions.DefaultStartDate = DateTime.Now.AddDays(-30);
                    suggestions.DefaultEndDate = DateTime.Now;
                    suggestions.Message = "No meter reading data found. Using default date range.";
                    return suggestions;
                }

                var latest = dateInfo.LatestReading.Value;
                var earliest = dateInfo.EarliestReading.Value;

                // Suggest based on data availability
                if (latest > DateTime.Now.AddDays(-7))
                {
                    // Recent data available - suggest last 30 days
                    suggestions.DefaultStartDate = latest.AddDays(-30);
                    suggestions.DefaultEndDate = latest;
                    suggestions.Message = $"Recent data available. Showing last 30 days ending {latest:yyyy-MM-dd}.";
                }
                else if (latest > DateTime.Now.AddDays(-90))
                {
                    // Somewhat recent data - suggest around latest data
                    suggestions.DefaultStartDate = latest.AddDays(-30);
                    suggestions.DefaultEndDate = latest;
                    suggestions.Message = $"Latest data from {latest:yyyy-MM-dd}. Showing 30 days ending at latest data.";
                }
                else
                {
                    // Old data - suggest a reasonable range around latest data
                    suggestions.DefaultStartDate = latest.AddDays(-60);
                    suggestions.DefaultEndDate = latest.AddDays(1);
                    suggestions.Message = $"Data available from {earliest:yyyy-MM-dd} to {latest:yyyy-MM-dd}. Showing 60 days around latest data.";
                }

                // Add alternative suggestions
                suggestions.AlternativeRanges = new List<DateRangeOption>
                {
                    new DateRangeOption
                    {
                        Name = "Last 7 days of data",
                        StartDate = latest.AddDays(-6),
                        EndDate = latest.AddDays(1),
                        Description = "Recent week"
                    },
                    new DateRangeOption
                    {
                        Name = "Last month of data",
                        StartDate = latest.AddDays(-30),
                        EndDate = latest.AddDays(1),
                        Description = "Recent month"
                    },
                    new DateRangeOption
                    {
                        Name = "All available data",
                        StartDate = earliest,
                        EndDate = latest.AddDays(1),
                        Description = $"Full range ({(latest - earliest).Days} days)"
                    }
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
        /// FIXED: Get meters that have data in the specified date range
        /// </summary>
        public async Task<List<MeterQueryResult>> GetActiveMetersWithDataAsync(MeterReadingFilters filters)
        {
            var meters = new List<MeterQueryResult>();

            try
            {
                if (!_databaseService.IsInitialized)
                {
                    _logger.LogWarning("Database not initialized");
                    return meters;
                }

                using var connection = _databaseService.GetConnection();

                var (startDate, endDate) = filters.GetDateRange();

                // FIXED: Query only meters that have data in the date range
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
                    WHERE m.""Active"" = true
                    AND mr.""Timestamp"" >= @StartDate 
                    AND mr.""Timestamp"" <= @EndDate";

                var parameters = new List<NpgsqlParameter>
                {
                    new NpgsqlParameter("@StartDate", startDate),
                    new NpgsqlParameter("@EndDate", endDate)
                };

                // Add tenant filter if specified
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

                _logger.LogInformation("Getting meters with data for date range {StartDate} to {EndDate} (limit: {Limit})",
                    startDate.ToString("yyyy-MM-dd"), endDate.ToString("yyyy-MM-dd"), filters.Limit);

                using var cmd = new NpgsqlCommand(query, connection);
                foreach (var param in parameters)
                {
                    cmd.Parameters.Add(param);
                }

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

                _logger.LogInformation("Retrieved {Count} meters with data in date range (requested limit: {Limit})",
                    meters.Count, filters.Limit);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting active meters with data");
            }

            return meters;
        }

        /// <summary>
        /// REUSABLE: Get active meters (SIMPLIFIED from Phase 1)
        /// </summary>
        public async Task<List<MeterQueryResult>> GetActiveMetersAsync(MeterReadingFilters filters)
        {
            var meters = new List<MeterQueryResult>();

            try
            {
                if (!_databaseService.IsInitialized)
                {
                    _logger.LogWarning("Database not initialized");
                    return meters;
                }

                using var connection = _databaseService.GetConnection();

                // Simplified query without complex parameter builder
                var query = @"
                    SELECT m.""MeterId"", m.""Name"", m.""Label"", m.""Unit"", 
                           m.""Type"", m.""Active"", m.""LastReading"", m.""TenantID"",
                           COALESCE(t.""DisplayName"", '') as ""TenantName""
                    FROM ""Meters"" m
                    LEFT JOIN ""Tenants"" t ON m.""TenantID"" = t.""TenantID""";

                var whereConditions = new List<string>();
                var parameters = new List<NpgsqlParameter>();

                // Add active filter if needed
                if (filters.ActiveOnly)
                {
                    whereConditions.Add(@"m.""Active"" = true");
                }

                // Add tenant filter if specified
                if (filters.TenantId.HasValue)
                {
                    whereConditions.Add(@"m.""TenantID"" = @TenantId");
                    parameters.Add(new NpgsqlParameter("@TenantId", filters.TenantId.Value));
                }
                else if (!filters.IncludeNullTenants)
                {
                    whereConditions.Add(@"m.""TenantID"" IS NOT NULL");
                }

                // Build final query
                if (whereConditions.Any())
                {
                    query += " WHERE " + string.Join(" AND ", whereConditions);
                }

                query += " ORDER BY m.\"Name\" LIMIT @Limit OFFSET @Offset";
                parameters.Add(new NpgsqlParameter("@Limit", filters.Limit));
                parameters.Add(new NpgsqlParameter("@Offset", filters.Offset));

                _logger.LogInformation("Executing meter query with limit {Limit}", filters.Limit);

                using var cmd = new NpgsqlCommand(query, connection);
                foreach (var param in parameters)
                {
                    cmd.Parameters.Add(param);
                }

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

                _logger.LogInformation("Retrieved {Count} meters (requested limit: {Limit})", meters.Count, filters.Limit);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting active meters");
            }

            return meters;
        }

        /// <summary>
        /// FIXED: Get meter readings with dynamic superposition (Intraday, MoM, YoY) & Dynamic Grouping
        /// </summary>
        public async Task<List<ConsumptionQueryResult>> GetMeterReadingsAsync(MeterReadingFilters filters)
        {
            var data = new List<ConsumptionQueryResult>();

            try
            {
                if (!_databaseService.IsInitialized) return data;

                using var connection = _databaseService.GetConnection();
                var (startDate, endDate) = filters.GetDateRange();

                string query;
                var parameters = new List<NpgsqlParameter>();

                // =========================================================================
                // 🧠 L'ASTUCE : Variables SQL dynamiques pour le bouton Locataire / Compteur
                // =========================================================================
                string idColumn = filters.GroupBy == "tenant" ? "COALESCE(m.\"TenantID\", 0)" : "m.\"MeterId\"";
                string nameColumn = filters.GroupBy == "tenant" ? "COALESCE(t.\"DisplayName\", 'Zones Communes')" : "m.\"Name\"";
                // On force le type ::text pour que Npgsql ne plante pas
                string unitColumn = filters.GroupBy == "tenant" ? "'kWh'::text" : "COALESCE(m.\"Unit\", 'kWh')";
                string groupColumns = filters.GroupBy == "tenant" ? "m.\"TenantID\", t.\"DisplayName\"" : "m.\"MeterId\", m.\"Name\", m.\"Unit\"";

                // =========================================================================
                // 🚀 MODE COMPARAISON : Superposition (Heures, Jours du mois, ou Mois de l'année)
                // =========================================================================
                if (filters.IsComparisonMode)
                {
                    string curveNameSql = "";
                    string xAxisSql = "";

                    if (filters.DateFilter == "daily")
                    {
                        curveNameSql = @"CASE EXTRACT(ISODOW FROM mr.""Timestamp"")
                            WHEN 1 THEN 'Lundi' WHEN 2 THEN 'Mardi' WHEN 3 THEN 'Mercredi'
                            WHEN 4 THEN 'Jeudi' WHEN 5 THEN 'Vendredi' WHEN 6 THEN 'Samedi'
                            WHEN 7 THEN 'Dimanche' END";
                        xAxisSql = @"to_char(DATE_TRUNC('hour', mr.""Timestamp""), 'HH24:00')";

                        // Sécurité : Forcer la semaine actuelle
                        startDate = DateTime.Now.Date.AddDays(-7);
                        endDate = DateTime.Now.Date.AddDays(1);
                    }
                    else if (filters.DateFilter == "monthly")
                    {
                        curveNameSql = @"to_char(DATE_TRUNC('month', mr.""Timestamp""), 'MM-YYYY')";
                        xAxisSql = @"to_char(DATE_TRUNC('day', mr.""Timestamp""), 'DD')";
                    }
                    else // yearly
                    {
                        curveNameSql = @"to_char(DATE_TRUNC('year', mr.""Timestamp""), 'YYYY')";
                        xAxisSql = @"to_char(DATE_TRUNC('month', mr.""Timestamp""), 'MM')";
                    }

                    query = $@"
                        SELECT 
                            {idColumn} as ""MeterId"",
                            {curveNameSql} as MeterName,
                            {unitColumn} as Unit,
                            {xAxisSql} as ReadingDate,
                            SUM(mr.""Value"") as TotalConsumption,
                            AVG(mr.""Value"") as AvgConsumption,
                            MAX(mr.""Value"") as MaxConsumption,
                            m.""TenantID"",
                            COALESCE(t.""DisplayName"", '') as TenantName
                        FROM ""MeterReadings"" mr
                        INNER JOIN ""Meters"" m ON mr.""MeterId"" = m.""MeterId""
                        LEFT JOIN ""Tenants"" t ON m.""TenantID"" = t.""TenantID""
                        WHERE m.""Active"" = true
                        AND mr.""Timestamp"" >= @StartDate::timestamp
                        AND mr.""Timestamp"" <= @EndDate::timestamp";

                    parameters.Add(new NpgsqlParameter("@StartDate", startDate));
                    parameters.Add(new NpgsqlParameter("@EndDate", endDate));

                    if (filters.TenantId.HasValue)
                    {
                        query += " AND m.\"TenantID\" = @TenantId";
                        parameters.Add(new NpgsqlParameter("@TenantId", filters.TenantId.Value));
                    }
                    if (filters.MeterId.HasValue)
                    {
                        query += " AND m.\"MeterId\" = @MeterId";
                        parameters.Add(new NpgsqlParameter("@MeterId", filters.MeterId.Value));
                    }

                    query += $" GROUP BY {groupColumns}, m.\"TenantID\", t.\"DisplayName\", {curveNameSql}, {xAxisSql} ORDER BY {xAxisSql} ASC";
                }
                // =========================================================================
                // 📊 MODE STANDARD (L'axe X est une date normale et continue)
                // =========================================================================
                else
                {
                    string timeGrouping = "to_char(DATE_TRUNC('day', mr.\"Timestamp\"), 'YYYY-MM-DD')";
                    if (filters.DateFilter?.ToLower() == "yearly")
                        timeGrouping = "to_char(DATE_TRUNC('year', mr.\"Timestamp\"), 'YYYY')";
                    else if (filters.DateFilter?.ToLower() == "monthly")
                        timeGrouping = "to_char(DATE_TRUNC('month', mr.\"Timestamp\"), 'YYYY-MM')";

                    query = $@"
                        SELECT 
                            {idColumn} as ""MeterId"",
                            {nameColumn} as MeterName,
                            {unitColumn} as Unit,
                            {timeGrouping} as ReadingDate,
                            SUM(mr.""Value"") as TotalConsumption,
                            AVG(mr.""Value"") as AvgConsumption,
                            MAX(mr.""Value"") as MaxConsumption,
                            m.""TenantID"",
                            COALESCE(t.""DisplayName"", '') as TenantName
                        FROM ""MeterReadings"" mr
                        INNER JOIN ""Meters"" m ON mr.""MeterId"" = m.""MeterId""
                        LEFT JOIN ""Tenants"" t ON m.""TenantID"" = t.""TenantID""
                        WHERE m.""Active"" = true
                        AND mr.""Timestamp"" >= @StartDate::timestamp
                        AND mr.""Timestamp"" <= @EndDate::timestamp";

                    parameters.Add(new NpgsqlParameter("@StartDate", startDate));
                    parameters.Add(new NpgsqlParameter("@EndDate", endDate));

                    if (filters.TenantId.HasValue)
                    {
                        query += " AND m.\"TenantID\" = @TenantId";
                        parameters.Add(new NpgsqlParameter("@TenantId", filters.TenantId.Value));
                    }
                    if (filters.MeterId.HasValue)
                    {
                        query += " AND m.\"MeterId\" = @MeterId";
                        parameters.Add(new NpgsqlParameter("@MeterId", filters.MeterId.Value));
                    }

                    query += $" GROUP BY {groupColumns}, m.\"TenantID\", t.\"DisplayName\", {timeGrouping} ORDER BY {timeGrouping} ASC, {nameColumn}";
                }

                // =========================================================================
                // EXÉCUTION DE LA REQUÊTE
                // =========================================================================
                using var cmd = new NpgsqlCommand(query, connection);
                foreach (var param in parameters) cmd.Parameters.Add(param);
                using var reader = await cmd.ExecuteReaderAsync();

                while (await reader.ReadAsync())
                {
                    data.Add(new ConsumptionQueryResult
                    {
                        MeterId = reader.GetInt32(reader.GetOrdinal("MeterId")),
                        MeterName = reader.GetString(reader.GetOrdinal("MeterName")),
                        Unit = reader.GetString(reader.GetOrdinal("Unit")),
                        ReadingDate = reader.GetString(reader.GetOrdinal("ReadingDate")),
                        TotalConsumption = Convert.ToDouble(reader.GetDecimal(reader.GetOrdinal("TotalConsumption"))),
                        AvgConsumption = Convert.ToDouble(reader.GetDecimal(reader.GetOrdinal("AvgConsumption"))),
                        MaxConsumption = Convert.ToDouble(reader.GetDecimal(reader.GetOrdinal("MaxConsumption"))),
                        TenantId = reader.IsDBNull(reader.GetOrdinal("TenantID")) ? null : reader.GetInt32(reader.GetOrdinal("TenantID")),
                        TenantName = reader.IsDBNull(reader.GetOrdinal("TenantName")) ? string.Empty : reader.GetString(reader.GetOrdinal("TenantName"))
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting meter readings");
            }

            return data;
        }

        /// <summary>
        /// REUSABLE: Process chart data (from Phase 1)
        /// </summary>
        public ChartDataResult ProcessChartData(List<ConsumptionQueryResult> data)
        {
            var result = new ChartDataResult();

            if (!data.Any())
            {
                return result;
            }

            result.Labels = data.Select(d => d.ReadingDate)
                               .Distinct()
                               .OrderBy(x => x)
                               .ToList();

            var meterGroups = data.GroupBy(d => new { d.MeterId, d.MeterName, d.Unit, d.TenantName });

            var colors = new[]
            {
                "#FF6384", "#36A2EB", "#FFCE56", "#4BC0C0",
                "#9966FF", "#FF9F40", "#FF6384", "#C9CBCF"
            };

            int colorIndex = 0;

            foreach (var meterGroup in meterGroups)
            {
                var color = colors[colorIndex % colors.Length];

                var dataset = new ChartDataset
                {
                    Label = BuildMeterLabel(meterGroup.Key.MeterName, meterGroup.Key.Unit, meterGroup.Key.TenantName),
                    BackgroundColor = color,
                    BorderColor = color,
                    Data = result.Labels.Select(label =>
                        meterGroup.FirstOrDefault(d => d.ReadingDate == label)?.TotalConsumption ?? 0)
                        .ToList()
                };

                result.Datasets.Add(dataset);
                colorIndex++;
            }

            return result;
        }

        /// <summary>
        /// REUSABLE: Calculate summary (from Phase 1)
        /// </summary>
        public DashboardSummary CalculateSummary(List<ConsumptionQueryResult> data)
        {
            var summary = new DashboardSummary();

            if (!data.Any())
            {
                return summary;
            }

            summary.TotalConsumption = data.Sum(d => d.TotalConsumption);
            summary.PeakUsage = data.Max(d => d.MaxConsumption);
            summary.ActiveMeters = data.Select(d => d.MeterId).Distinct().Count();

            var uniqueDates = data.Select(d => d.ReadingDate).Distinct().Count();
            summary.AverageDaily = uniqueDates > 0 ? summary.TotalConsumption / uniqueDates : 0;

            return summary;
        }

        /// <summary>
        /// REUSABLE: Generate demo data (from Phase 1)
        /// </summary>
        public object GenerateDemoChartData(string message = "This is sample data to demonstrate the chart functionality.")
        {
            var labels = new List<string>();
            var sampleData1 = new List<double>();
            var sampleData2 = new List<double>();

            var random = new Random();

            // Generate 7 days of sample data
            for (int i = 6; i >= 0; i--)
            {
                var date = DateTime.Now.AddDays(-i);
                labels.Add(date.ToString("yyyy-MM-dd"));

                // Generate realistic sample consumption data
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
                    new
                    {
                        label = "Sample Meter 1 (kWh)",
                        data = sampleData1,
                        backgroundColor = "#FF6384",
                        borderColor = "#FF6384"
                    },
                    new
                    {
                        label = "Sample Meter 2 (kWh)",
                        data = sampleData2,
                        backgroundColor = "#36A2EB",
                        borderColor = "#36A2EB"
                    }
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

            return new
            {
                chartData = chartData,
                summary = summary.ToDisplayObject(),
                message = message,
                isDemoData = true
            };
        }

        /// <summary>
        /// REUSABLE: Get tenants for dropdown (from Phase 1)
        /// </summary>
        public async Task<List<object>> GetTenantsAsync()
        {
            var tenants = new List<object>();

            try
            {
                if (!_databaseService.IsInitialized)
                {
                    return tenants;
                }

                using var connection = _databaseService.GetConnection();

                var query = @"
                    SELECT t.""TenantID"" as Id, 
                           td.""CompanyName"" as Name,
                           td.""Active""
                    FROM ""Tenants"" t
                    INNER JOIN ""TenantDetails"" td ON t.""TenantID"" = td.""TenantID""
                    WHERE td.""Active"" = true
                    ORDER BY td.""CompanyName""";

                using var cmd = new NpgsqlCommand(query, connection);
                using var reader = await cmd.ExecuteReaderAsync();

                while (await reader.ReadAsync())
                {
                    tenants.Add(new
                    {
                        id = reader.GetInt32("Id"),
                        name = reader.GetString("Name")
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching tenants");
            }

            return tenants;
        }

        /// <summary>
        /// REUSABLE: Get meters by tenant for dropdown (from Phase 1)
        /// </summary>
        public async Task<List<object>> GetMetersByTenantAsync(int tenantId, int limit = 100)
        {
            var meters = new List<object>();

            try
            {
                if (!_databaseService.IsInitialized)
                {
                    return meters;
                }

                using var connection = _databaseService.GetConnection();

                var query = @"
                    SELECT ""MeterId"" as id,
                           ""Name"" as name,
                           ""Unit"" as unit,
                           ""Type"" as type,
                           ""Active"" as active
                    FROM ""Meters""
                    WHERE ""TenantID"" = @TenantId AND ""Active"" = true
                    ORDER BY ""Name""
                    LIMIT @Limit";

                using var cmd = new NpgsqlCommand(query, connection);
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
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching meters for tenant {TenantId}", tenantId);
            }

            return meters;
        }

        /// <summary>
        /// Helper: Build meter label for charts
        /// </summary>
        private string BuildMeterLabel(string meterName, string unit, string tenantName)
        {
            var label = $"{meterName} ({unit})";

            if (!string.IsNullOrEmpty(tenantName))
            {
                label += $" - {tenantName}";
            }

            return label;
        }
    }
}