using Microsoft.AspNetCore.Mvc;
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
        public async Task<IActionResult> GetReadings(int? meterId, string viewType = "raw", int page = 1, int pageSize = 50, DateTime? startDate = null, DateTime? endDate = null)
        {
            try
            {
                if (!_databaseService.IsInitialized)
                {
                    return Json(new { success = false, error = "Database not configured" });
                }

                var readings = await GetReadingsByTypeSingle(meterId, viewType, page, pageSize, startDate, endDate);
                var totalCount = await GetReadingsCountSingle(meterId, viewType, startDate, endDate);
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
                _logger.LogError(ex, $"Error getting readings: meterId={meterId}, viewType={viewType}");
                return Json(new { success = false, error = ex.Message });
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetMeterStats(int meterId, DateTime? startDate = null, DateTime? endDate = null)
        {
            try
            {
                if (!_databaseService.IsInitialized)
                {
                    return Json(new { success = false, error = "Database not configured" });
                }

                var stats = await CalculateMeterStats(meterId, startDate, endDate);
                return Json(new { success = true, data = stats });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error getting meter stats for meter {meterId}");
                return Json(new { success = false, error = ex.Message });
            }
        }

        #region Private Helper Methods

        private async Task<List<MeterReading>> GetReadingsByType(List<int> meterIds, string viewType,
            int page, int pageSize, DateTime? startDate = null, DateTime? endDate = null)
        {
            int currentCompanyId = _companyContext.CurrentCompanyId; 
            return await _databaseService.ExecuteWithCompanyIsolationAsync(currentCompanyId, async (connection, transaction) =>
            {
                var readings = new List<MeterReading>();
                string tableName = GetTableNameForViewType(viewType);
                string query = BuildReadingsQuery(tableName, meterIds, startDate, endDate, page, pageSize);

                using var command = new NpgsqlCommand(query, connection, transaction);
                AddMeterIdsParameters(command, meterIds);
                AddDateParameters(command, startDate, endDate);
                AddPaginationParameters(command, page, pageSize);

                using var reader = await command.ExecuteReaderAsync();
                readings = await ReadMeterReadingsFromDataReader(reader, viewType);
                return readings;
            });
        }

        private async Task<int> GetReadingsCount(List<int> meterIds, string viewType,
            DateTime? startDate = null, DateTime? endDate = null)
        {
            int currentCompanyId = _companyContext.CurrentCompanyId;
            return await _databaseService.ExecuteWithCompanyIsolationAsync(currentCompanyId, async (connection, transaction) =>
            {
                string tableName = GetTableNameForViewType(viewType);
                string query = BuildCountQuery(tableName, meterIds, startDate, endDate);

                using var command = new NpgsqlCommand(query, connection, transaction);
                AddMeterIdsParameters(command, meterIds);
                AddDateParameters(command, startDate, endDate);

                var result = await command.ExecuteScalarAsync();
                return Convert.ToInt32(result);
            });
        }

        private async Task<MeterStats> CalculateMultiMeterStats(List<int> meterIds,
            DateTime? startDate = null, DateTime? endDate = null)
        {
            var stats = new MeterStats();
            if (!meterIds.Any()) return stats;

            int currentCompanyId = _companyContext.CurrentCompanyId;
            return await _databaseService.ExecuteWithCompanyIsolationAsync(currentCompanyId, async (connection, transaction) =>
            {
                var whereClause = "WHERE mr.\"MeterId\" = ANY(@meterIds)";
                if (startDate.HasValue || endDate.HasValue)
                {
                    whereClause += " AND ";
                    if (startDate.HasValue && endDate.HasValue)
                        whereClause += "mr.\"Timestamp\" BETWEEN @startDate AND @endDate";
                    else if (startDate.HasValue)
                        whereClause += "mr.\"Timestamp\" >= @startDate";
                    else if (endDate.HasValue)
                        whereClause += "mr.\"Timestamp\" <= @endDate";
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
                    LEFT JOIN ""Meters"" m ON mr.""MeterId"" = m.""MeterId""
                    {whereClause}";

                using var command = new NpgsqlCommand(query, connection, transaction);
                command.Parameters.AddWithValue("@meterIds", meterIds.ToArray());
                AddDateParameters(command, startDate, endDate);

                using var reader = await command.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    stats.ReadingCount = reader.GetInt32("ReadingCount");
                    stats.MinValue = reader.GetDecimal("MinValue");
                    stats.MaxValue = reader.GetDecimal("MaxValue");
                    stats.AvgValue = reader.GetDecimal("AvgValue");
                    stats.FirstReading = reader.GetDateTime("FirstReading");
                    stats.LastReading = reader.GetDateTime("LastReading");
                    stats.MeterCount = reader.GetInt32("MeterCount");

                    if (!reader.IsDBNull("MeterNames"))
                    {
                        var meterNamesArray = reader.GetValue("MeterNames") as string[];
                        stats.MeterNames = meterNamesArray?.Where(name => !string.IsNullOrEmpty(name)).ToList() ?? new List<string>();
                    }
                }
                return stats;
            });
        }

        private async Task<List<MeterReading>> GetReadingsByTypeSingle(int? meterId, string viewType, int page, int pageSize, DateTime? startDate = null, DateTime? endDate = null)
        {
            int currentCompanyId = _companyContext.CurrentCompanyId;
            return await _databaseService.ExecuteWithCompanyIsolationAsync(currentCompanyId, async (connection, transaction) =>
            {
                var offset = (page - 1) * pageSize;
                string sql = viewType.ToLower() switch
                {
                    "daily" => BuildDailyReadingsQuery(meterId, startDate, endDate, offset, pageSize),
                    "monthly" => BuildMonthlyReadingsQuery(meterId, startDate, endDate, offset, pageSize),
                    "yearly" => BuildYearlyReadingsQuery(meterId, startDate, endDate, offset, pageSize),
                    _ => BuildRawReadingsQuery(meterId, startDate, endDate, offset, pageSize)
                };

                using var command = new NpgsqlCommand(sql, connection, transaction);
                AddParametersToCommand(command, meterId, startDate, endDate, offset, pageSize);
                using var reader = await command.ExecuteReaderAsync();
                return await ReadMeterReadingsFromDataReader(reader, viewType);
            });
        }

        private async Task<int> GetReadingsCountSingle(int? meterId, string viewType, DateTime? startDate = null, DateTime? endDate = null)
        {
            int currentCompanyId = _companyContext.CurrentCompanyId;
            return await _databaseService.ExecuteWithCompanyIsolationAsync(currentCompanyId, async (connection, transaction) =>
            {
                string sql = viewType.ToLower() switch
                {
                    "daily" => BuildCountQuerySingle("MeterReadingsDaily", "ReadingDate", meterId, startDate, endDate),
                    "monthly" => BuildCountQuerySingle("MeterReadingsMonthly", "Year, Month", meterId, startDate, endDate),
                    "yearly" => BuildCountQuerySingle("MeterReadingsYearly", "Year", meterId, startDate, endDate),
                    _ => BuildCountQuerySingle("MeterReadings", "Timestamp", meterId, startDate, endDate)
                };

                using var command = new NpgsqlCommand(sql, connection, transaction);
                AddParametersToCommand(command, meterId, startDate, endDate, 0, 0);
                var result = await command.ExecuteScalarAsync();
                return Convert.ToInt32(result);
            });
        }

        private async Task<MeterStats> CalculateMeterStats(int meterId, DateTime? startDate = null, DateTime? endDate = null)
        {
            int currentCompanyId = _companyContext.CurrentCompanyId;
            return await _databaseService.ExecuteWithCompanyIsolationAsync(currentCompanyId, async (connection, transaction) =>
            {
                var whereClause = "WHERE \"MeterId\" = @MeterId";
                if (startDate.HasValue) whereClause += " AND \"Timestamp\" >= @StartDate";
                if (endDate.HasValue) whereClause += " AND \"Timestamp\" <= @EndDate";

                string sql = $@"
                    SELECT 
                        COUNT(*) as ReadingCount,
                        MIN(""Value"") as MinValue,
                        MAX(""Value"") as MaxValue,
                        AVG(""Value"") as AvgValue,
                        MIN(""Timestamp"") as FirstReading,
                        MAX(""Timestamp"") as LastReading
                    FROM ""MeterReadings""
                    {whereClause}";

                using var command = new NpgsqlCommand(sql, connection, transaction);
                command.Parameters.AddWithValue("@MeterId", meterId);
                if (startDate.HasValue) command.Parameters.AddWithValue("@StartDate", startDate.Value);
                if (endDate.HasValue) command.Parameters.AddWithValue("@EndDate", endDate.Value);

                using var reader = await command.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    return new MeterStats
                    {
                        ReadingCount = reader.GetInt32("ReadingCount"),
                        MinValue = reader.IsDBNull(reader.GetOrdinal("MinValue")) ? 0 : reader.GetDecimal("MinValue"),
                        MaxValue = reader.IsDBNull(reader.GetOrdinal("MaxValue")) ? 0 : reader.GetDecimal("MaxValue"),
                        AvgValue = reader.IsDBNull(reader.GetOrdinal("AvgValue")) ? 0 : reader.GetDecimal("AvgValue"),
                        FirstReading = reader.IsDBNull(reader.GetOrdinal("FirstReading")) ? DateTime.MinValue : reader.GetDateTime("FirstReading"),
                        LastReading = reader.IsDBNull(reader.GetOrdinal("LastReading")) ? DateTime.MinValue : reader.GetDateTime("LastReading")
                    };
                }
                return new MeterStats();
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
                    WHERE m.""Active"" = true
                    ORDER BY CASE WHEN m.""ParentId"" IS NULL THEN 0 ELSE 1 END, m.""Name"" ASC";

                using var command = new NpgsqlCommand(query, connection, transaction);
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

        private string BuildRawReadingsQuery(int? meterId, DateTime? startDate, DateTime? endDate, int offset, int pageSize)
        {
            var whereClause = "WHERE 1=1";
            if (meterId.HasValue) whereClause += " AND mr.\"MeterId\" = @MeterId";
            if (startDate.HasValue) whereClause += " AND mr.\"Timestamp\" >= @StartDate";
            if (endDate.HasValue) whereClause += " AND mr.\"Timestamp\" <= @EndDate";

            return $@"
                SELECT mr.""ReadingId"", mr.""MeterId"", m.""Name"" as ""MeterName"", 
                       mr.""Timestamp"", mr.""Value"", COALESCE(mr.""Quality"", -1)::INTEGER as ""Quality""
                FROM ""MeterReadings"" mr
                JOIN ""Meters"" m ON mr.""MeterId"" = m.""MeterId""
                {whereClause}
                ORDER BY mr.""Timestamp"" DESC
                LIMIT @PageSize OFFSET @Offset";
        }

        private string BuildDailyReadingsQuery(int? meterId, DateTime? startDate, DateTime? endDate, int offset, int pageSize)
        {
            var whereClause = "WHERE 1=1";
            if (meterId.HasValue) whereClause += " AND dr.\"MeterId\" = @MeterId";
            if (startDate.HasValue) whereClause += " AND dr.\"ReadingDate\" >= @StartDate";
            if (endDate.HasValue) whereClause += " AND dr.\"ReadingDate\" <= @EndDate";

            return $@"
                SELECT dr.""DailyReadingId"" as ""ReadingId"", dr.""MeterId"", m.""Name"" as ""MeterName"",
                       dr.""ReadingDate""::timestamp as ""Timestamp"", dr.""AvgValue"" as ""Value"", 
                       dr.""MinValue"", dr.""MaxValue"", dr.""SumValue"", dr.""ReadingCount""
                FROM ""MeterReadingsDaily"" dr
                JOIN ""Meters"" m ON dr.""MeterId"" = m.""MeterId""
                {whereClause}
                ORDER BY dr.""ReadingDate"" DESC
                LIMIT @PageSize OFFSET @Offset";
        }

        private string BuildMonthlyReadingsQuery(int? meterId, DateTime? startDate, DateTime? endDate, int offset, int pageSize)
        {
            var whereClause = "WHERE 1=1";
            if (meterId.HasValue) whereClause += " AND mr.\"MeterId\" = @MeterId";
            if (startDate.HasValue) whereClause += " AND make_date(mr.\"Year\", mr.\"Month\", 1) >= @StartDate";
            if (endDate.HasValue) whereClause += " AND make_date(mr.\"Year\", mr.\"Month\", 1) <= @EndDate";

            return $@"
                SELECT mr.""MonthlyReadingId"" as ""ReadingId"", mr.""MeterId"", m.""Name"" as ""MeterName"",
                       make_date(mr.""Year"", mr.""Month"", 1) as ""Timestamp"", mr.""AvgValue"" as ""Value"",
                       mr.""MinValue"", mr.""MaxValue"", mr.""SumValue"", mr.""ReadingCount"", mr.""Year"", mr.""Month""
                FROM ""MeterReadingsMonthly"" mr
                JOIN ""Meters"" m ON mr.""MeterId"" = m.""MeterId""
                {whereClause}
                ORDER BY mr.""Year"" DESC, mr.""Month"" DESC
                LIMIT @PageSize OFFSET @Offset";
        }

        private string BuildYearlyReadingsQuery(int? meterId, DateTime? startDate, DateTime? endDate, int offset, int pageSize)
        {
            var whereClause = "WHERE 1=1";
            if (meterId.HasValue) whereClause += " AND yr.\"MeterId\" = @MeterId";
            if (startDate.HasValue) whereClause += " AND make_date(yr.\"Year\", 1, 1) >= @StartDate";
            if (endDate.HasValue) whereClause += " AND make_date(yr.\"Year\", 1, 1) <= @EndDate";

            return $@"
                SELECT yr.""YearlyReadingId"" as ""ReadingId"", yr.""MeterId"", m.""Name"" as ""MeterName"",
                       make_date(yr.""Year"", 1, 1) as ""Timestamp"", yr.""AvgValue"" as ""Value"",
                       yr.""MinValue"", yr.""MaxValue"", yr.""SumValue"", yr.""ReadingCount"", yr.""Year""
                FROM ""MeterReadingsYearly"" yr
                JOIN ""Meters"" m ON yr.""MeterId"" = m.""MeterId""
                {whereClause}
                ORDER BY yr.""Year"" DESC
                LIMIT @PageSize OFFSET @Offset";
        }

        private string BuildReadingsQuery(string tableName, List<int> meterIds, DateTime? startDate, DateTime? endDate, int page, int pageSize)
        {
            var whereClause = BuildWhereClause(meterIds, startDate, endDate);
            var offset = (page - 1) * pageSize;

            return $@"
                SELECT mr.""ReadingId"", mr.""MeterId"", m.""Name"" as MeterName, mr.""Timestamp"", mr.""Value"", mr.""Quality""
                FROM ""{tableName}"" mr
                LEFT JOIN ""Meters"" m ON mr.""MeterId"" = m.""MeterId""
                {whereClause}
                ORDER BY mr.""Timestamp"" DESC, mr.""MeterId""
                LIMIT @pageSize OFFSET @offset";
        }

        private string BuildCountQuery(string tableName, List<int> meterIds, DateTime? startDate, DateTime? endDate)
        {
            var whereClause = BuildWhereClause(meterIds, startDate, endDate);
            return $@"SELECT COUNT(*) FROM ""{tableName}"" mr {whereClause}";
        }

        private string BuildCountQuerySingle(string tableName, string dateColumn, int? meterId, DateTime? startDate, DateTime? endDate)
        {
            var whereClause = "WHERE 1=1";
            if (meterId.HasValue) whereClause += " AND \"MeterId\" = @MeterId";

            if (startDate.HasValue || endDate.HasValue)
            {
                if (tableName == "MeterReadingsMonthly")
                {
                    if (startDate.HasValue) whereClause += " AND make_date(\"Year\", \"Month\", 1) >= @StartDate";
                    if (endDate.HasValue) whereClause += " AND make_date(\"Year\", \"Month\", 1) <= @EndDate";
                }
                else if (tableName == "MeterReadingsYearly")
                {
                    if (startDate.HasValue) whereClause += " AND make_date(\"Year\", 1, 1) >= @StartDate";
                    if (endDate.HasValue) whereClause += " AND make_date(\"Year\", 1, 1) <= @EndDate";
                }
                else
                {
                    if (startDate.HasValue) whereClause += $" AND \"{dateColumn}\" >= @StartDate";
                    if (endDate.HasValue) whereClause += $" AND \"{dateColumn}\" <= @EndDate";
                }
            }

            return $@"SELECT COUNT(*) FROM ""{tableName}"" {whereClause}";
        }

        private string BuildWhereClause(List<int> meterIds, DateTime? startDate, DateTime? endDate)
        {
            var conditions = new List<string>();
            if (meterIds.Any()) conditions.Add("mr.\"MeterId\" = ANY(@meterIds)");

            if (startDate.HasValue && endDate.HasValue)
                conditions.Add("mr.\"Timestamp\" BETWEEN @startDate AND @endDate");
            else if (startDate.HasValue)
                conditions.Add("mr.\"Timestamp\" >= @startDate");
            else if (endDate.HasValue)
                conditions.Add("mr.\"Timestamp\" <= @endDate");

            return conditions.Any() ? "WHERE " + string.Join(" AND ", conditions) : "";
        }

        private void AddMeterIdsParameters(NpgsqlCommand command, List<int> meterIds)
        {
            if (meterIds.Any()) command.Parameters.AddWithValue("@meterIds", meterIds.ToArray());
        }

        private void AddDateParameters(NpgsqlCommand command, DateTime? startDate, DateTime? endDate)
        {
            if (startDate.HasValue) command.Parameters.AddWithValue("@startDate", startDate.Value.Date);
            if (endDate.HasValue)
            {
                DateTime endOfDay = endDate.Value.Date.AddDays(1).AddTicks(-1);
                command.Parameters.AddWithValue("@endDate", endOfDay);
                command.Parameters.AddWithValue("@EndDate", endOfDay); 
            }
        }

        private void AddParametersToCommand(NpgsqlCommand command, int? meterId, DateTime? startDate, DateTime? endDate, int offset, int pageSize)
        {
            if (meterId.HasValue) command.Parameters.AddWithValue("@MeterId", meterId.Value);
            if (startDate.HasValue) command.Parameters.AddWithValue("@StartDate", startDate.Value);
            if (endDate.HasValue) command.Parameters.AddWithValue("@EndDate", endDate.Value);
            if (pageSize > 0)
            {
                command.Parameters.AddWithValue("@PageSize", pageSize);
                command.Parameters.AddWithValue("@Offset", offset);
            }
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

        private void AddPaginationParameters(NpgsqlCommand command, int page, int pageSize)
        {
            command.Parameters.AddWithValue("@pageSize", pageSize);
            command.Parameters.AddWithValue("@offset", (page - 1) * pageSize);
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
                    Timestamp = reader.GetDateTime("Timestamp"),
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