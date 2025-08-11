using System.Data;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using PoWorks_Rework.Services;

namespace PoWorks_Rework.Controllers
{
    public class DashboardController : BaseController
    {
        private readonly ILogger<DashboardController> _logger;

        public DashboardController(DatabaseService databaseService, ILogger<DashboardController> logger)
            : base(databaseService)
        {
            _logger = logger;
        }

        [HttpGet]
        public async Task<IActionResult> GetTenants()
        {
            try
            {
                if (!_databaseService.IsInitialized)
                {
                    return Json(new List<object>());
                }

                using var connection = GetDatabaseConnection();

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

                var tenants = new List<object>();
                while (await reader.ReadAsync())
                {
                    tenants.Add(new
                    {
                        id = reader.GetInt32("Id"),
                        name = reader.GetString("Name")
                    });
                }

                return Json(tenants);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching tenants");
                return Json(new List<object>());
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetMetersByTenant(int tenantId)
        {
            try
            {
                if (!_databaseService.IsInitialized)
                {
                    return Json(new List<object>());
                }

                using var connection = GetDatabaseConnection();

                var query = @"
                    SELECT ""MeterId"" as id,
                           ""Name"" as name,
                           ""Unit"" as unit,
                           ""Type"" as type,
                           ""Active"" as active
                    FROM ""Meters""
                    WHERE ""TenantID"" = @TenantId AND ""Active"" = true
                    ORDER BY ""Name""";

                using var cmd = new NpgsqlCommand(query, connection);
                cmd.Parameters.AddWithValue("@TenantId", tenantId);
                using var reader = await cmd.ExecuteReaderAsync();

                var meters = new List<object>();
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

                return Json(meters);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching meters for tenant {TenantId}", tenantId);
                return Json(new List<object>());
            }
        }

        [HttpPost]
        public async Task<IActionResult> GetConsumptionData([FromBody] DashboardFilterRequest filters)
        {
            try
            {
                _logger.LogInformation("=== DASHBOARD API DEBUG START ===");
                _logger.LogInformation($"Database IsInitialized: {_databaseService.IsInitialized}");
                _logger.LogInformation($"Received filters: DateFilter={filters?.DateFilter}, TenantId={filters?.TenantId}, MeterId={filters?.MeterId}, StartDate={filters?.StartDate}, EndDate={filters?.EndDate}");

                if (!_databaseService.IsInitialized)
                {
                    _logger.LogWarning("Database service not initialized - returning demo data");
                    return Json(GenerateDemoChartData());
                }

                using var connection = GetDatabaseConnection();
                _logger.LogInformation($"Database connection established: {connection.State}");

                // ✅ OPTIMIZATION 1: Quick data availability check
                var hasData = await CheckDataAvailability(connection, filters);
                _logger.LogInformation($"Data availability check: {hasData}");

                if (!hasData)
                {
                    _logger.LogInformation("No data found - returning demo data with message");
                    return Json(GenerateDemoChartData("No meter reading data found. This is sample data to demonstrate the chart functionality."));
                }

                // ✅ OPTIMIZATION 2: Use simplified, faster queries
                var consumptionData = await GetOptimizedConsumptionData(connection, filters);
                _logger.LogInformation($"Raw consumption data records retrieved: {consumptionData.Count}");

                // Process data for charts
                var chartData = ProcessChartData(consumptionData);
                var summary = CalculateSummary(consumptionData);

                var result = new
                {
                    chartData = chartData,
                    summary = summary,
                    message = consumptionData.Any() ? "" : "No data found for the selected criteria"
                };

                _logger.LogInformation("=== DASHBOARD API DEBUG END ===");
                return Json(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ERROR in GetConsumptionData: {Message}", ex.Message);
                _logger.LogError("Stack trace: {StackTrace}", ex.StackTrace);

                // ✅ OPTIMIZATION 3: Return demo data on error instead of failing
                return Json(GenerateDemoChartData($"Error loading data: {ex.Message}. Showing demo data."));
            }
        }

        // ✅ OPTIMIZATION 4: Fast data availability check
        private async Task<bool> CheckDataAvailability(NpgsqlConnection connection, DashboardFilterRequest filters)
        {
            try
            {
                // Quick check: Do we have any active meters?
                var meterCountQuery = "SELECT COUNT(*) FROM \"Meters\" WHERE \"Active\" = true";
                if (filters.TenantId.HasValue)
                {
                    meterCountQuery += " AND \"TenantID\" = @TenantId";
                }

                using var meterCmd = new NpgsqlCommand(meterCountQuery, connection);
                if (filters.TenantId.HasValue)
                {
                    meterCmd.Parameters.AddWithValue("@TenantId", filters.TenantId.Value);
                }

                var meterCount = (long)await meterCmd.ExecuteScalarAsync();
                if (meterCount == 0)
                {
                    _logger.LogInformation("No active meters found");
                    return false;
                }

                // Quick check: Do we have any readings data?
                var readingsCountQuery = "SELECT COUNT(*) FROM \"MeterReadings\" LIMIT 1";
                using var readingsCmd = new NpgsqlCommand(readingsCountQuery, connection);
                var readingsCount = (long)await readingsCmd.ExecuteScalarAsync();

                _logger.LogInformation($"Found {meterCount} active meters and {readingsCount} readings");
                return readingsCount > 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking data availability");
                return false;
            }
        }

        // ✅ OPTIMIZATION 5: Simplified, faster queries
        private async Task<List<ConsumptionData>> GetOptimizedConsumptionData(NpgsqlConnection connection, DashboardFilterRequest filters)
        {
            var data = new List<ConsumptionData>();

            try
            {
                // Use direct query on MeterReadings if aggregated tables are empty
                var query = @"
                    SELECT 
                        m.""MeterId"",
                        m.""Name"" as MeterName,
                        COALESCE(m.""Unit"", 'kWh') as Unit,
                        DATE(mr.""Timestamp"") as ReadingDate,
                        SUM(mr.""Value"") as TotalConsumption,
                        AVG(mr.""Value"") as AvgConsumption,
                        MAX(mr.""Value"") as MaxConsumption
                    FROM ""MeterReadings"" mr
                    INNER JOIN ""Meters"" m ON mr.""MeterId"" = m.""MeterId""
                    WHERE m.""Active"" = true";

                var parameters = new List<NpgsqlParameter>();

                // Add filters
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

                // Add date filters (default to last 30 days if none specified)
                var endDate = filters.EndDate ?? DateTime.Now;
                var startDate = filters.StartDate ?? endDate.AddDays(-30);

                query += " AND mr.\"Timestamp\" >= @StartDate AND mr.\"Timestamp\" <= @EndDate";
                parameters.Add(new NpgsqlParameter("@StartDate", startDate));
                parameters.Add(new NpgsqlParameter("@EndDate", endDate));

                query += @"
                    GROUP BY m.""MeterId"", m.""Name"", m.""Unit"", DATE(mr.""Timestamp"")
                    ORDER BY DATE(mr.""Timestamp""), m.""Name""
                    LIMIT 100"; // Limit results for performance

                _logger.LogInformation($"OPTIMIZED SQL QUERY: {query}");
                _logger.LogInformation($"Parameters: StartDate={startDate}, EndDate={endDate}");

                using var cmd = new NpgsqlCommand(query, connection);
                foreach (var param in parameters)
                {
                    cmd.Parameters.Add(param);
                }

                // ✅ OPTIMIZATION 6: Add command timeout
                cmd.CommandTimeout = 30; // 30 second timeout

                using var reader = await cmd.ExecuteReaderAsync();

                int rowCount = 0;
                while (await reader.ReadAsync() && rowCount < 100) // Limit processing
                {
                    rowCount++;
                    var item = new ConsumptionData
                    {
                        MeterId = reader.GetInt32(0),
                        MeterName = reader.GetString(1),
                        Unit = reader.GetString(2),
                        ReadingDate = reader.GetDateTime(3).ToString("yyyy-MM-dd"),
                        TotalConsumption = Convert.ToDouble(reader.GetDecimal(4)),
                        AvgConsumption = Convert.ToDouble(reader.GetDecimal(5)),
                        MaxConsumption = Convert.ToDouble(reader.GetDecimal(6))
                    };

                    data.Add(item);
                }

                _logger.LogInformation($"Retrieved {rowCount} optimized data rows");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in optimized consumption data query");
                throw;
            }

            return data;
        }

        // ✅ OPTIMIZATION 7: Generate demo data for better UX
        private object GenerateDemoChartData(string message = "This is sample data to demonstrate the chart functionality.")
        {
            var labels = new List<string>();
            var sampleData1 = new List<double>();
            var sampleData2 = new List<double>();

            // Generate 7 days of sample data
            for (int i = 6; i >= 0; i--)
            {
                var date = DateTime.Now.AddDays(-i);
                labels.Add(date.ToString("yyyy-MM-dd"));

                // Generate realistic sample consumption data
                var baseValue1 = 150 + (i * 10);
                var baseValue2 = 200 + (i * 15);
                sampleData1.Add(baseValue1 + (new Random().NextDouble() * 50));
                sampleData2.Add(baseValue2 + (new Random().NextDouble() * 80));
            }

            var chartData = new
            {
                labels = labels,
                datasets = new object[]
                {
                    new
                    {
                        label = "Sample Meter 1 (kWh)",
                        data = sampleData1
                    },
                    new
                    {
                        label = "Sample Meter 2 (kWh)",
                        data = sampleData2
                    }
                }
            };

            var summary = new
            {
                totalConsumption = sampleData1.Sum() + sampleData2.Sum(),
                averageDaily = (sampleData1.Sum() + sampleData2.Sum()) / 7,
                peakUsage = Math.Max(sampleData1.Max(), sampleData2.Max()),
                activeMeters = 2
            };

            return new
            {
                chartData = chartData,
                summary = summary,
                message = message,
                isDemoData = true
            };
        }

        private object ProcessChartData(List<ConsumptionData> data)
        {
            if (!data.Any())
            {
                return new
                {
                    labels = new List<string>(),
                    datasets = new List<object>()
                };
            }

            var labels = data.Select(d => d.ReadingDate).Distinct().OrderBy(x => x).ToList();
            var meterGroups = data.GroupBy(d => new { d.MeterId, d.MeterName, d.Unit });

            var datasets = new List<object>();

            foreach (var meterGroup in meterGroups)
            {
                var dataset = new
                {
                    label = $"{meterGroup.Key.MeterName} ({meterGroup.Key.Unit})",
                    data = labels.Select(label =>
                        meterGroup.FirstOrDefault(d => d.ReadingDate == label)?.TotalConsumption ?? 0).ToList()
                };

                datasets.Add(dataset);
            }

            return new
            {
                labels = labels,
                datasets = datasets
            };
        }

        private object CalculateSummary(List<ConsumptionData> data)
        {
            if (!data.Any())
            {
                return new
                {
                    totalConsumption = 0,
                    averageDaily = 0,
                    peakUsage = 0,
                    activeMeters = 0
                };
            }

            var totalConsumption = data.Sum(d => d.TotalConsumption);
            var peakUsage = data.Max(d => d.MaxConsumption);
            var activeMeters = data.Select(d => d.MeterId).Distinct().Count();

            var uniqueDates = data.Select(d => d.ReadingDate).Distinct().Count();
            var averageDaily = uniqueDates > 0 ? totalConsumption / uniqueDates : 0;

            return new
            {
                totalConsumption = Math.Round(totalConsumption, 2),
                averageDaily = Math.Round(averageDaily, 2),
                peakUsage = Math.Round(peakUsage, 2),
                activeMeters = activeMeters
            };
        }
    }

    public class DashboardFilterRequest
    {
        public string DateFilter { get; set; }
        public int? TenantId { get; set; }
        public int? MeterId { get; set; }
        public DateTime? StartDate { get; set; }
        public DateTime? EndDate { get; set; }
    }

    public class ConsumptionData
    {
        public int MeterId { get; set; }
        public string MeterName { get; set; }
        public string Unit { get; set; }
        public string ReadingDate { get; set; }
        public double TotalConsumption { get; set; }
        public double AvgConsumption { get; set; }
        public double MaxConsumption { get; set; }
    }
}