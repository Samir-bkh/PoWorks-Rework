using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Npgsql;
using PoWorks_Rework.Models;
using PoWorks_Rework.Services;
using System.Data;

namespace PoWorks_Rework.Controllers
{
    public class MeterReadingsController : BaseController
    {
        private readonly ILogger<MeterReadingsController> _logger;
        private readonly ICompanyContext _companyContext;
        public MeterReadingsController(DatabaseService databaseService, ICompanyContext companyContext, ILogger<MeterReadingsController> logger)
            : base(databaseService)
        {
            _companyContext = companyContext;
            _logger = logger;
        }

        public async Task<IActionResult> Index(string meterIds, string viewType = "raw", int page = 1, int pageSize = 50)
        {
            if (!_databaseService.IsInitialized)
            {
                TempData["ErrorMessage"] = "Database not configured. Please set up database first.";
                return RedirectToAction("General", "Settings");
            }

            try
            {
                var selectedMeterIds = ParseMeterIds(meterIds);

                var viewModel = new MeterReadingsViewModel
                {
                    ViewType = viewType,
                    CurrentPage = page,
                    PageSize = pageSize,
                    SelectedMeterIds = selectedMeterIds
                };

                viewModel.AvailableMeters = await GetAvailableMeters();
                await LoadReadingsData(viewModel);

                return View(viewModel);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading meter readings page");
                TempData["ErrorMessage"] = $"Error loading meter readings: {ex.Message}";
                return View(new MeterReadingsViewModel());
            }
        }

        private List<int> ParseMeterIds(string meterIds)
        {
            if (string.IsNullOrWhiteSpace(meterIds))
                return new List<int>();

            return meterIds.Split(',', StringSplitOptions.RemoveEmptyEntries)
                          .Select(id => int.TryParse(id.Trim(), out int parsed) ? parsed : 0)
                          .Where(id => id > 0)
                          .Distinct()
                          .ToList();
        }

        private async Task LoadReadingsData(MeterReadingsViewModel viewModel)
        {
            viewModel.Readings = await GetReadingsByType(
                viewModel.SelectedMeterIds,
                viewModel.ViewType,
                viewModel.CurrentPage,
                viewModel.PageSize,
                viewModel.StartDate,
                viewModel.EndDate
            );

            viewModel.TotalItems = await GetReadingsCount(
                viewModel.SelectedMeterIds,
                viewModel.ViewType,
                viewModel.StartDate,
                viewModel.EndDate
            );

            viewModel.TotalPages = (int)Math.Ceiling(viewModel.TotalItems / (double)viewModel.PageSize);

            if (viewModel.SelectedMeterIds.Any())
            {
                viewModel.MeterStats = await CalculateMultiMeterStats(
                    viewModel.SelectedMeterIds,
                    viewModel.StartDate,
                    viewModel.EndDate
                );
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetReadings(string meterIds, string viewType = "raw", int page = 1, int pageSize = 50, DateTime? startDate = null, DateTime? endDate = null)
        {
            try
            {
                if (!_databaseService.IsInitialized)
                    return Json(new { success = false, error = "Database not configured" });

                var selectedIds = ParseMeterIds(meterIds);

                var readings = await GetReadingsByType(selectedIds, viewType, page, pageSize, startDate, endDate);
                var totalCount = await GetReadingsCount(selectedIds, viewType, startDate, endDate);
                var totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);

                return Json(new
                {
                    success = true,
                    data = readings,
                    pagination = new
                    {
                        currentPage = page,
                        totalPages = totalPages,
                        totalCount = totalCount,
                        pageSize = pageSize
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error getting readings: meterIds={meterIds}, viewType={viewType}");
                return Json(new { success = false, error = ex.Message });
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetMeterStats(string meterIds, DateTime? startDate = null, DateTime? endDate = null)
        {
            try
            {
                if (!_databaseService.IsInitialized) return Json(new { success = false, error = "Database not configured" });

                var selectedIds = ParseMeterIds(meterIds);
                var stats = await CalculateMultiMeterStats(selectedIds, startDate, endDate);

                return Json(new { success = true, data = stats });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting meter stats");
                return Json(new { success = false, error = ex.Message });
            }
        }



        [HttpGet]
        public async Task<IActionResult> Export(string meterIds, string viewType = "raw", DateTime? startDate = null, DateTime? endDate = null, string format = "csv")
        {
            try
            {
                if (!_databaseService.IsInitialized)
                    return Json(new { success = false, error = "Database not configured" });

                var selectedIds = ParseMeterIds(meterIds);

                // On récupère TOUT (pas de pagination) en demandant une pageSize énorme
                var readings = await GetReadingsByType(selectedIds, viewType, page: 1, pageSize: int.MaxValue, startDate, endDate);

                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmm");
                string fileName = $"MeterReadings_{viewType}_{timestamp}";

                if (format.ToLower() == "json")
                {
                    var json = System.Text.Json.JsonSerializer.Serialize(readings, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
                    return File(System.Text.Encoding.UTF8.GetBytes(json), "application/json", $"{fileName}.json");
                }
                else
                {
                    var sb = new System.Text.StringBuilder();
                    bool isRaw = viewType.ToLower() == "raw";
                    bool isYearly = viewType.ToLower() == "yearly";

                    var headers = new List<string> { "MeterId", "MeterName", "Timestamp", "Value" };
                    if (isRaw)
                    {
                        headers.Add("Quality");
                    }
                    else
                    {
                        headers.AddRange(new[] { "MinValue", "MaxValue", "ReadingCount" });
                        if (!isYearly) headers.Add("SumValue");
                    }
                    sb.AppendLine(string.Join(",", headers));

                    foreach (var r in readings)
                    {
                        var values = new List<string>
                {
                    r.MeterId.ToString(),
                    EscapeCsv(r.MeterName),
                    r.Timestamp.ToString("yyyy-MM-dd HH:mm:ss"),
                    r.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)
                };

                        if (isRaw)
                        {
                            values.Add(r.Quality?.ToString() ?? "");
                        }
                        else
                        {
                            values.Add(r.MinValue?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "");
                            values.Add(r.MaxValue?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "");
                            values.Add(r.ReadingCount?.ToString() ?? "");
                            if (!isYearly) values.Add(r.SumValue?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "");
                        }

                        sb.AppendLine(string.Join(",", values));
                    }

                    var preamble = System.Text.Encoding.UTF8.GetPreamble();
                    var data = System.Text.Encoding.UTF8.GetBytes(sb.ToString());
                    var fileBytes = new byte[preamble.Length + data.Length];
                    Buffer.BlockCopy(preamble, 0, fileBytes, 0, preamble.Length);
                    Buffer.BlockCopy(data, 0, fileBytes, preamble.Length, data.Length);

                    return File(fileBytes, "text/csv", $"{fileName}.csv");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error exporting readings");
                return Json(new { success = false, error = ex.Message });
            }
        }

        private string EscapeCsv(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            if (value.Contains(",") || value.Contains("\""))
            {
                return "\"" + value.Replace("\"", "\"\"") + "\"";
            }
            return value;
        }

        #region Private Helper Methods

        private async Task<List<MeterReading>> GetReadingsByType(List<int> meterIds, string viewType, int page, int pageSize, DateTime? startDate = null, DateTime? endDate = null)
        {
            int currentCompanyId = _companyContext.CurrentCompanyId;
            return await _databaseService.ExecuteWithCompanyIsolationAsync(currentCompanyId, async (connection, transaction) =>
            {
                string tableName = GetTableNameForViewType(viewType);
                string query = BuildReadingsQuery(tableName, meterIds, startDate, endDate, page, pageSize);

                using var command = new NpgsqlCommand(query, connection, transaction);
                command.Parameters.AddWithValue("@CompanyId", currentCompanyId); 
                AddDateParameters(command, startDate, endDate);
                AddPaginationParameters(command, page, pageSize);

                using var reader = await command.ExecuteReaderAsync();
                return await ReadMeterReadingsFromDataReader(reader, viewType);
            });
        }

        private async Task<int> GetReadingsCount(List<int> meterIds, string viewType, DateTime? startDate = null, DateTime? endDate = null)
        {
            int currentCompanyId = _companyContext.CurrentCompanyId;
            return await _databaseService.ExecuteWithCompanyIsolationAsync(currentCompanyId, async (connection, transaction) =>
            {
                string tableName = GetTableNameForViewType(viewType);
                string query = BuildCountQuery(tableName, meterIds, startDate, endDate);

                using var command = new NpgsqlCommand(query, connection, transaction);
                command.Parameters.AddWithValue("@CompanyId", currentCompanyId); 
                AddDateParameters(command, startDate, endDate);

                var result = await command.ExecuteScalarAsync();
                return result != null && result != DBNull.Value ? Convert.ToInt32(result) : 0;
            });
        }

        private async Task<MeterStats> CalculateMultiMeterStats(List<int> meterIds, DateTime? startDate = null, DateTime? endDate = null)
        {
            var stats = new MeterStats();
            if (!meterIds.Any()) return stats;

            int currentCompanyId = _companyContext.CurrentCompanyId;
            return await _databaseService.ExecuteWithCompanyIsolationAsync(currentCompanyId, async (connection, transaction) =>
            {
                var ids = string.Join(",", meterIds);
                var whereClause = $"WHERE m.\"CompanyId\" = @CompanyId AND mr.\"MeterId\" IN ({ids})";

                if (startDate.HasValue || endDate.HasValue)
                {
                    if (startDate.HasValue && endDate.HasValue)
                        whereClause += " AND mr.\"Timestamp\" BETWEEN @startDate AND @endDate";
                    else if (startDate.HasValue)
                        whereClause += " AND mr.\"Timestamp\" >= @startDate";
                    else if (endDate.HasValue)
                        whereClause += " AND mr.\"Timestamp\" <= @endDate";
                }

                string query = $@"
                    SELECT 
                        COUNT(*) as ReadingCount,
                        COALESCE(MIN(mr.""Value""), 0) as MinValue,
                        COALESCE(MAX(mr.""Value""), 0) as MaxValue,
                        COALESCE(AVG(mr.""Value""), 0) as AvgValue,
                        COALESCE(MIN(mr.""Timestamp""), '1900-01-01') as FirstReading,
                        COALESCE(MAX(mr.""Timestamp""), '1900-01-01') as LastReading,
                        COUNT(DISTINCT mr.""MeterId"") as MeterCount,
                        array_agg(DISTINCT m.""Name"") as MeterNames
                    FROM ""MeterReadings"" mr
                    JOIN ""Meters"" m ON mr.""MeterId"" = m.""MeterId""
                    {whereClause}";

                using var command = new NpgsqlCommand(query, connection, transaction);
                command.Parameters.AddWithValue("@CompanyId", currentCompanyId); 
                AddDateParameters(command, startDate, endDate);

                using var reader = await command.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    stats.ReadingCount = reader.GetInt32(reader.GetOrdinal("ReadingCount"));
                    stats.MinValue = reader.GetDecimal(reader.GetOrdinal("MinValue"));
                    stats.MaxValue = reader.GetDecimal(reader.GetOrdinal("MaxValue"));
                    stats.AvgValue = reader.GetDecimal(reader.GetOrdinal("AvgValue"));
                    stats.FirstReading = reader.GetDateTime(reader.GetOrdinal("FirstReading"));
                    stats.LastReading = reader.GetDateTime(reader.GetOrdinal("LastReading"));
                    stats.MeterCount = reader.GetInt32(reader.GetOrdinal("MeterCount"));

                    if (!reader.IsDBNull(reader.GetOrdinal("MeterNames")))
                    {
                        var meterNamesArray = reader.GetValue(reader.GetOrdinal("MeterNames")) as string[];
                        stats.MeterNames = meterNamesArray?.Where(name => !string.IsNullOrEmpty(name)).ToList() ?? new List<string>();
                    }
                }
                return stats;
            });
        }

        private async Task<List<MeterOption>> GetAvailableMeters()
        {
            int currentCompanyId = _companyContext.CurrentCompanyId;
            return await _databaseService.ExecuteWithCompanyIsolationAsync(currentCompanyId, async (connection, transaction) =>
            {
                var meters = new List<MeterOption>();
                string query = @"
                    SELECT 
                        m.""MeterId"", m.""Name"", COALESCE(m.""Unit"", '') as ""Unit"", 
                        COALESCE(m.""Type"", 'Unknown') as ""Type"", m.""Active"",
                        CASE WHEN m.""ParentId"" IS NULL THEN 'Main' ELSE 'Sub' END as ""MeterType""
                    FROM ""Meters"" m
                    WHERE m.""CompanyId"" = @CompanyId AND m.""Active"" = true
                    ORDER BY CASE WHEN m.""ParentId"" IS NULL THEN 0 ELSE 1 END, m.""Name"" ASC";

                using var command = new NpgsqlCommand(query, connection, transaction);
                command.Parameters.AddWithValue("@CompanyId", currentCompanyId); 
                using var reader = await command.ExecuteReaderAsync();

                while (await reader.ReadAsync())
                {
                    meters.Add(new MeterOption
                    {
                        MeterId = reader.GetInt32("MeterId"),
                        Name = reader.GetString("Name"),
                        Unit = reader.GetString("Unit"),
                        Type = reader.GetString("MeterType")
                    });
                }
                return meters;
            });
        }

        private string BuildReadingsQuery(string tableName, List<int> meterIds, DateTime? startDate, DateTime? endDate, int page, int pageSize)
        {
            var conditions = new List<string> { "m.\"CompanyId\" = @CompanyId" };

            if (meterIds.Any())
            {
                var ids = string.Join(",", meterIds);
                conditions.Add($"mr.\"MeterId\" IN ({ids})");
            }

            string selectColumns = "";
            string orderBy = "";

            if (tableName == "MeterReadingsDaily")
            {
                selectColumns = @"mr.""DailyReadingId"" as ""ReadingId"", mr.""MeterId"", m.""Name"" as ""MeterName"", 
                                  mr.""ReadingDate""::timestamp as ""Timestamp"", mr.""AvgValue"" as ""Value"", 192 as ""Quality"",
                                  mr.""MinValue"", mr.""MaxValue"", mr.""SumValue"", mr.""ReadingCount""";

                if (startDate.HasValue && endDate.HasValue) conditions.Add("mr.\"ReadingDate\" BETWEEN @startDate AND @endDate");
                else if (startDate.HasValue) conditions.Add("mr.\"ReadingDate\" >= @startDate");
                else if (endDate.HasValue) conditions.Add("mr.\"ReadingDate\" <= @endDate");
                orderBy = "ORDER BY mr.\"ReadingDate\" DESC, mr.\"MeterId\"";
            }
            else if (tableName == "MeterReadingsMonthly")
            {
                selectColumns = @"mr.""MonthlyReadingId"" as ""ReadingId"", mr.""MeterId"", m.""Name"" as ""MeterName"", 
                                  make_date(mr.""Year"", mr.""Month"", 1)::timestamp as ""Timestamp"", mr.""AvgValue"" as ""Value"", 192 as ""Quality"",
                                  mr.""MinValue"", mr.""MaxValue"", mr.""SumValue"", mr.""ReadingCount"", mr.""Year"", mr.""Month""";

                if (startDate.HasValue && endDate.HasValue) conditions.Add("make_date(mr.\"Year\", mr.\"Month\", 1) BETWEEN @startDate AND @endDate");
                else if (startDate.HasValue) conditions.Add("make_date(mr.\"Year\", mr.\"Month\", 1) >= @startDate");
                else if (endDate.HasValue) conditions.Add("make_date(mr.\"Year\", mr.\"Month\", 1) <= @endDate");
                orderBy = "ORDER BY mr.\"Year\" DESC, mr.\"Month\" DESC, mr.\"MeterId\"";
            }
            else if (tableName == "MeterReadingsYearly")
            {
                selectColumns = @"mr.""YearlyReadingId"" as ""ReadingId"", mr.""MeterId"", m.""Name"" as ""MeterName"", 
                                  make_date(mr.""Year"", 1, 1)::timestamp as ""Timestamp"", mr.""AvgValue"" as ""Value"", 192 as ""Quality"",
                                  mr.""MinValue"", mr.""MaxValue"", mr.""SumValue"", mr.""ReadingCount"", mr.""Year""";

                if (startDate.HasValue && endDate.HasValue) conditions.Add("make_date(mr.\"Year\", 1, 1) BETWEEN @startDate AND @endDate");
                else if (startDate.HasValue) conditions.Add("make_date(mr.\"Year\", 1, 1) >= @startDate");
                else if (endDate.HasValue) conditions.Add("make_date(mr.\"Year\", 1, 1) <= @endDate");
                orderBy = "ORDER BY mr.\"Year\" DESC, mr.\"MeterId\"";
            }
            else
            {
                selectColumns = @"mr.""ReadingId"", mr.""MeterId"", m.""Name"" as ""MeterName"", mr.""Timestamp"", mr.""Value"", COALESCE(mr.""Quality"", 192) as ""Quality"",
                                  NULL::numeric as ""MinValue"", NULL::numeric as ""MaxValue"", NULL::numeric as ""SumValue"", NULL::integer as ""ReadingCount""";

                if (startDate.HasValue && endDate.HasValue) conditions.Add("mr.\"Timestamp\" BETWEEN @startDate AND @endDate");
                else if (startDate.HasValue) conditions.Add("mr.\"Timestamp\" >= @startDate");
                else if (endDate.HasValue) conditions.Add("mr.\"Timestamp\" <= @endDate");
                orderBy = "ORDER BY mr.\"Timestamp\" DESC, mr.\"MeterId\"";
            }

            var whereClause = "WHERE " + string.Join(" AND ", conditions);

            return $@"
        SELECT {selectColumns}
        FROM ""{tableName}"" mr
        JOIN ""Meters"" m ON mr.""MeterId"" = m.""MeterId""
        {whereClause}
        {orderBy}
        LIMIT @pageSize OFFSET @offset";
        }

        private string BuildCountQuery(string tableName, List<int> meterIds, DateTime? startDate, DateTime? endDate)
        {
            var conditions = new List<string> { "m.\"CompanyId\" = @CompanyId" };

            if (meterIds.Any())
            {
                var ids = string.Join(",", meterIds);
                conditions.Add($"mr.\"MeterId\" IN ({ids})");
            }

          
            if (tableName == "MeterReadingsDaily")
            {
                if (startDate.HasValue && endDate.HasValue) conditions.Add("mr.\"ReadingDate\" BETWEEN @startDate AND @endDate");
                else if (startDate.HasValue) conditions.Add("mr.\"ReadingDate\" >= @startDate");
                else if (endDate.HasValue) conditions.Add("mr.\"ReadingDate\" <= @endDate");
            }
            else if (tableName == "MeterReadingsMonthly")
            {
                if (startDate.HasValue && endDate.HasValue) conditions.Add("make_date(mr.\"Year\", mr.\"Month\", 1) BETWEEN @startDate AND @endDate");
                else if (startDate.HasValue) conditions.Add("make_date(mr.\"Year\", mr.\"Month\", 1) >= @startDate");
                else if (endDate.HasValue) conditions.Add("make_date(mr.\"Year\", mr.\"Month\", 1) <= @endDate");
            }
            else if (tableName == "MeterReadingsYearly")
            {
                if (startDate.HasValue && endDate.HasValue) conditions.Add("make_date(mr.\"Year\", 1, 1) BETWEEN @startDate AND @endDate");
                else if (startDate.HasValue) conditions.Add("make_date(mr.\"Year\", 1, 1) >= @startDate");
                else if (endDate.HasValue) conditions.Add("make_date(mr.\"Year\", 1, 1) <= @endDate");
            }
            else
            {
                if (startDate.HasValue && endDate.HasValue) conditions.Add("mr.\"Timestamp\" BETWEEN @startDate AND @endDate");
                else if (startDate.HasValue) conditions.Add("mr.\"Timestamp\" >= @startDate");
                else if (endDate.HasValue) conditions.Add("mr.\"Timestamp\" <= @endDate");
            }

            var whereClause = "WHERE " + string.Join(" AND ", conditions);

            return $@"
        SELECT COUNT(*)
        FROM ""{tableName}"" mr
        JOIN ""Meters"" m ON mr.""MeterId"" = m.""MeterId""
        {whereClause}";
        }

        private void AddDateParameters(NpgsqlCommand command, DateTime? startDate, DateTime? endDate)
        {
            if (startDate.HasValue) command.Parameters.AddWithValue("@startDate", startDate.Value);
            if (endDate.HasValue)
            {
                DateTime endVal = endDate.Value;
                if (endVal.TimeOfDay == TimeSpan.Zero)
                {
                    endVal = endVal.Date.AddDays(1).AddTicks(-1);
                }

                command.Parameters.AddWithValue("@endDate", endVal);
                command.Parameters.AddWithValue("@EndDate", endVal);
            }
        }

        private void AddPaginationParameters(NpgsqlCommand command, int page, int pageSize)
        {
            command.Parameters.AddWithValue("@pageSize", pageSize);
            command.Parameters.AddWithValue("@offset", (page - 1) * pageSize);
        }

        private string GetTableNameForViewType(string viewType)
        {
            return viewType.ToLower() switch
            {
                "daily" => "MeterReadingsDaily",
                "monthly" => "MeterReadingsMonthly",
                "yearly" => "MeterReadingsYearly",
                _ => "MeterReadings"
            };
        }

        private async Task<List<MeterReading>> ReadMeterReadingsFromDataReader(NpgsqlDataReader reader, string viewType)
        {
            var readings = new List<MeterReading>();
            while (await reader.ReadAsync())
            {
                var reading = new MeterReading
                {
                    ReadingId = reader.GetInt32("ReadingId"),
                    MeterId = reader.GetInt32("MeterId"),
                    MeterName = reader.IsDBNull("MeterName") ? "Unknown" : reader.GetString("MeterName"),
                    Timestamp = reader.GetDateTime(reader.GetOrdinal("Timestamp")),
                    Value = reader.GetDecimal("Value")
                };

                if (viewType == "daily" || viewType == "monthly" || viewType == "yearly")
                {
                    reading.MinValue = reader.IsDBNull("MinValue") ? 0 : reader.GetDecimal("MinValue");
                    reading.MaxValue = reader.IsDBNull("MaxValue") ? 0 : reader.GetDecimal("MaxValue");
                    reading.SumValue = reader.IsDBNull("SumValue") ? 0 : reader.GetDecimal("SumValue");
                    reading.ReadingCount = reader.IsDBNull("ReadingCount") ? 0 : reader.GetInt32("ReadingCount");

                    if (viewType == "monthly")
                    {
                        reading.Year = reader.IsDBNull("Year") ? 0 : reader.GetInt32("Year");
                        reading.Month = reader.IsDBNull("Month") ? 0 : reader.GetInt32("Month");
                    }
                    else if (viewType == "yearly")
                    {
                        reading.Year = reader.IsDBNull("Year") ? 0 : reader.GetInt32("Year");
                    }
                }
                else
                {
                    if (reader.HasColumn("Quality"))
                    {
                        var qualityOrdinal = reader.GetOrdinal("Quality");
                        if (!reader.IsDBNull(qualityOrdinal))
                        {
                            var qualityValue = reader.GetInt32(qualityOrdinal);
                            reading.Quality = qualityValue == -1 ? null : qualityValue;
                        }
                    }
                }
                readings.Add(reading);
            }
            return readings;
        }

        #endregion
    }




    public static class NpgsqlDataReaderExtensions
    {
        public static bool HasColumn(this NpgsqlDataReader reader, string columnName)
        {
            try
            {
                return reader.GetOrdinal(columnName) >= 0;
            }
            catch (IndexOutOfRangeException)
            {
                return false;
            }
        }
    }
}