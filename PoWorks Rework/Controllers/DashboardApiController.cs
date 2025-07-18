using Microsoft.AspNetCore.Mvc;
using Npgsql;
using PoWorks_Rework.Services;


namespace PoWorks_Rework.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class DashboardApiController : BaseController
    {
        private readonly ILogger<DashboardApiController> _logger;

        public DashboardApiController(DatabaseService databaseService, ILogger<DashboardApiController> logger)
            : base(databaseService)
        {
            _logger = logger;
        }

        [HttpGet("GetTenants")]
        public async Task<IActionResult> GetTenants()
        {
            try
            {
                if (!_databaseService.IsInitialized)
                {
                    return Ok(new List<object>());
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
                        id = reader.GetInt32(0),
                        name = reader.GetString(1),
                        active = reader.GetBoolean(2)
                    });
                }

                return Ok(tenants);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching tenants");
                return StatusCode(500, new { error = "Error fetching tenants", details = ex.Message });
            }
        }

        [HttpGet("GetMetersByTenant/{tenantId}")]
        public async Task<IActionResult> GetMetersByTenant(int tenantId)
        {
            try
            {
                if (!_databaseService.IsInitialized)
                {
                    return Ok(new List<object>());
                }

                using var connection = GetDatabaseConnection();

                var query = @"
                    SELECT ""MeterId"" as Id, 
                           ""Name"", 
                           ""Unit"", 
                           ""Type"",
                           ""Active""
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
                        id = reader.GetInt32(0),
                        name = reader.GetString(1),
                        unit = reader.IsDBNull(2) ? "" : reader.GetString(2),
                        type = reader.GetString(3),
                        active = reader.GetBoolean(4)
                    });
                }

                return Ok(meters);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching meters for tenant {TenantId}", tenantId);
                return StatusCode(500, new { error = "Error fetching meters", details = ex.Message });
            }
        }

        [HttpPost("GetConsumptionData")]
        public async Task<IActionResult> GetConsumptionData([FromBody] DashboardFilterRequest filters)
        {
            try
            {
                if (!_databaseService.IsInitialized)
                {
                    return Ok(new
                    {
                        chartData = new { labels = new List<string>(), datasets = new List<object>() },
                        summary = new { totalConsumption = 0, averageDaily = 0, peakUsage = 0, activeMeters = 0 }
                    });
                }

                using var connection = GetDatabaseConnection();

                // Get consumption data based on filters
                var consumptionData = await GetFilteredConsumptionData(connection, filters);

                // Process data for charts
                var chartData = ProcessChartData(consumptionData, filters);

                // Calculate summary statistics
                var summary = CalculateSummary(consumptionData);

                return Ok(new
                {
                    chartData = chartData,
                    summary = summary
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching consumption data");
                return StatusCode(500, new { error = "Error fetching consumption data", details = ex.Message });
            }
        }

        private async Task<List<ConsumptionData>> GetFilteredConsumptionData(NpgsqlConnection connection, DashboardFilterRequest filters)
        {
            var data = new List<ConsumptionData>();
            string tableName = "";
            string dateColumn = "";
            string groupBy = "";
            string dateFormat = "";

            // Determine which table and grouping to use based on date filter
            switch (filters.DateFilter?.ToLower())
            {
                case "daily":
                    tableName = "MeterReadingsDaily";
                    dateColumn = "ReadingDate";
                    groupBy = "\"ReadingDate\"";
                    dateFormat = "YYYY-MM-DD";
                    break;
                case "monthly":
                    tableName = "MeterReadingsMonthly";
                    dateColumn = "CONCAT(\"Year\", '-', LPAD(\"Month\"::text, 2, '0'))";
                    groupBy = "\"Year\", \"Month\"";
                    dateFormat = "YYYY-MM";
                    break;
                case "yearly":
                    tableName = "MeterReadingsYearly";
                    dateColumn = "\"Year\"::text";
                    groupBy = "\"Year\"";
                    dateFormat = "YYYY";
                    break;
                default:
                    // Default to daily
                    tableName = "MeterReadingsDaily";
                    dateColumn = "ReadingDate";
                    groupBy = "\"ReadingDate\"";
                    dateFormat = "YYYY-MM-DD";
                    break;
            }

            // Build the query
            var query = $@"
                SELECT 
                    m.""MeterId"",
                    m.""Name"" as MeterName,
                    m.""Unit"",
                    {dateColumn} as ReadingDate,
                    SUM(r.""SumValue"") as TotalConsumption,
                    AVG(r.""AvgValue"") as AvgConsumption,
                    MAX(r.""MaxValue"") as MaxConsumption
                FROM ""{tableName}"" r
                INNER JOIN ""Meters"" m ON r.""MeterId"" = m.""MeterId""
                WHERE 1=1";

            var parameters = new List<NpgsqlParameter>();

            // Add tenant filter
            if (filters.TenantId.HasValue && filters.TenantId.Value > 0)
            {
                query += " AND m.\"TenantID\" = @TenantId";
                parameters.Add(new NpgsqlParameter("@TenantId", filters.TenantId.Value));
            }

            // Add meter filter
            if (filters.MeterId.HasValue && filters.MeterId.Value > 0)
            {
                query += " AND m.\"MeterId\" = @MeterId";
                parameters.Add(new NpgsqlParameter("@MeterId", filters.MeterId.Value));
            }

            // Add date range filter
            if (filters.StartDate.HasValue && filters.EndDate.HasValue)
            {
                if (tableName == "MeterReadingsDaily")
                {
                    query += " AND r.\"ReadingDate\" BETWEEN @StartDate AND @EndDate";
                    parameters.Add(new NpgsqlParameter("@StartDate", filters.StartDate.Value.Date));
                    parameters.Add(new NpgsqlParameter("@EndDate", filters.EndDate.Value.Date));
                }
                else if (tableName == "MeterReadingsMonthly")
                {
                    query += " AND (r.\"Year\" * 100 + r.\"Month\") BETWEEN @StartMonth AND @EndMonth";
                    parameters.Add(new NpgsqlParameter("@StartMonth", filters.StartDate.Value.Year * 100 + filters.StartDate.Value.Month));
                    parameters.Add(new NpgsqlParameter("@EndMonth", filters.EndDate.Value.Year * 100 + filters.EndDate.Value.Month));
                }
                else if (tableName == "MeterReadingsYearly")
                {
                    query += " AND r.\"Year\" BETWEEN @StartYear AND @EndYear";
                    parameters.Add(new NpgsqlParameter("@StartYear", filters.StartDate.Value.Year));
                    parameters.Add(new NpgsqlParameter("@EndYear", filters.EndDate.Value.Year));
                }
            }

            query += $@"
                GROUP BY m.""MeterId"", m.""Name"", m.""Unit"", {groupBy}
                ORDER BY {groupBy}, m.""Name""";

            using var cmd = new NpgsqlCommand(query, connection);
            foreach (var param in parameters)
            {
                cmd.Parameters.Add(param);
            }

            using var reader = await cmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                data.Add(new ConsumptionData
                {
                    MeterId = reader.GetInt32(0),
                    MeterName = reader.GetString(1),
                    Unit = reader.IsDBNull(2) ? "kWh" : reader.GetString(2),
                    ReadingDate = reader.GetString(3),
                    TotalConsumption = reader.IsDBNull(4) ? 0 : Convert.ToDouble(reader.GetDecimal(4)),
                    AvgConsumption = reader.IsDBNull(5) ? 0 : Convert.ToDouble(reader.GetDecimal(5)),
                    MaxConsumption = reader.IsDBNull(6) ? 0 : Convert.ToDouble(reader.GetDecimal(6))
                });
            }

            return data;
        }

        private object ProcessChartData(List<ConsumptionData> data, DashboardFilterRequest filters)
        {
            var labels = data.Select(d => d.ReadingDate).Distinct().OrderBy(x => x).ToList();
            var meterGroups = data.GroupBy(d => new { d.MeterId, d.MeterName, d.Unit });

            var datasets = new List<object>();

            foreach (var meterGroup in meterGroups)
            {
                var dataset = new
                {
                    label = $"{meterGroup.Key.MeterName} ({meterGroup.Key.Unit})",
                    data = new List<double>()
                };

                foreach (var label in labels)
                {
                    var value = meterGroup.FirstOrDefault(d => d.ReadingDate == label)?.TotalConsumption ?? 0;
                    dataset.data.Add(value);
                }

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

            // Calculate average daily consumption
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

        // Helper classes for API
        public class DashboardFilterRequest
        {
            public string DateFilter { get; set; }
            public int? TenantId { get; set; }
            public int? MeterId { get; set; }
            public DateTime? StartDate { get; set; }
            public DateTime? EndDate { get; set; }
        }

        private class ConsumptionData
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
}