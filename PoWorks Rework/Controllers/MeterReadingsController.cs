// Controllers/MeterReadingsController.cs - FIXED VERSION
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Npgsql;
using PoWorks_Rework.Models;
using PoWorks_Rework.Services;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;

namespace PoWorks_Rework.Controllers
{
    public class MeterReadingsController : BaseController
    {
        private readonly ILogger<MeterReadingsController> _logger;

        public MeterReadingsController(DatabaseService databaseService, ILogger<MeterReadingsController> logger)
            : base(databaseService)
        {
            _logger = logger;
        }

        /// <summary>
        /// Main meter readings page - shows raw readings by default
        /// </summary>
        public async Task<IActionResult> Index(int? meterId = null, string viewType = "raw", int page = 1, int pageSize = 50)
        {
            // Check if database is initialized
            if (!_databaseService.IsInitialized)
            {
                TempData["ErrorMessage"] = "Database not configured. Please set up database first.";
                return RedirectToAction("General", "Settings");
            }

            try
            {
                // Create view model - use fully qualified name to avoid ambiguity
                var viewModel = new MeterReadingsViewModel
                {
                    ViewType = viewType,
                    CurrentPage = page,
                    PageSize = pageSize,
                    SelectedMeterId = meterId
                };

                // Load available meters for the dropdown
                viewModel.AvailableMeters = await GetAvailableMeters();

                // Load readings based on view type and selected meter
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

        /// <summary>
        /// AJAX endpoint to get readings data for different view types
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> GetReadings(int? meterId, string viewType = "raw", int page = 1, int pageSize = 50, DateTime? startDate = null, DateTime? endDate = null)
        {
            try
            {
                if (!_databaseService.IsInitialized)
                {
                    return Json(new { success = false, error = "Database not configured" });
                }

                var readings = await GetReadingsByType(meterId, viewType, page, pageSize, startDate, endDate);
                var totalCount = await GetReadingsCount(meterId, viewType, startDate, endDate);
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

        /// <summary>
        /// Get meter statistics for the dashboard
        /// </summary>
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

        /// <summary>
        /// Load readings data based on view model settings
        /// </summary>
        private async Task LoadReadingsData(MeterReadingsViewModel viewModel)
        {
            viewModel.Readings = await GetReadingsByType(
                viewModel.SelectedMeterId,
                viewModel.ViewType,
                viewModel.CurrentPage,
                viewModel.PageSize,
                viewModel.StartDate,
                viewModel.EndDate
            );

            viewModel.TotalItems = await GetReadingsCount(
                viewModel.SelectedMeterId,
                viewModel.ViewType,
                viewModel.StartDate,
                viewModel.EndDate
            );

            viewModel.TotalPages = (int)Math.Ceiling(viewModel.TotalItems / (double)viewModel.PageSize);

            // Load meter statistics if a meter is selected
            if (viewModel.SelectedMeterId.HasValue)
            {
                viewModel.MeterStats = await CalculateMeterStats(
                    viewModel.SelectedMeterId.Value,
                    viewModel.StartDate,
                    viewModel.EndDate
                );
            }
        }

        /// <summary>
        /// Get readings from different tables based on view type
        /// </summary>
        private async Task<List<MeterReading>> GetReadingsByType(int? meterId, string viewType, int page, int pageSize, DateTime? startDate = null, DateTime? endDate = null)
        {
            var readings = new List<MeterReading>();

            using (var connection = new NpgsqlConnection(_databaseService.GetConnectionString()))
            {
                await connection.OpenAsync();

                string sql;
                var offset = (page - 1) * pageSize;

                switch (viewType.ToLower())
                {
                    case "daily":
                        sql = BuildDailyReadingsQuery(meterId, startDate, endDate, offset, pageSize);
                        break;

                    case "monthly":
                        sql = BuildMonthlyReadingsQuery(meterId, startDate, endDate, offset, pageSize);
                        break;

                    case "yearly":
                        sql = BuildYearlyReadingsQuery(meterId, startDate, endDate, offset, pageSize);
                        break;

                    default: // raw
                        sql = BuildRawReadingsQuery(meterId, startDate, endDate, offset, pageSize);
                        break;
                }

                using (var command = new NpgsqlCommand(sql, connection))
                {
                    AddParametersToCommand(command, meterId, startDate, endDate, offset, pageSize);

                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            readings.Add(MapReaderToMeterReading(reader, viewType));
                        }
                    }
                }
            }

            return readings;
        }

        /// <summary>
        /// Build SQL query for raw readings
        /// </summary>
        private string BuildRawReadingsQuery(int? meterId, DateTime? startDate, DateTime? endDate, int offset, int pageSize)
        {
            var whereClause = "WHERE 1=1";
            if (meterId.HasValue) whereClause += " AND mr.\"MeterId\" = @MeterId";
            if (startDate.HasValue) whereClause += " AND mr.\"Timestamp\" >= @StartDate";
            if (endDate.HasValue) whereClause += " AND mr.\"Timestamp\" <= @EndDate";

            return $@"
        SELECT mr.""ReadingId"", mr.""MeterId"", m.""Name"" as ""MeterName"", 
               mr.""Timestamp"", mr.""Value"", mr.""Quality""
        FROM ""MeterReadings"" mr
        JOIN ""Meters"" m ON mr.""MeterId"" = m.""MeterId""
        {whereClause}
        ORDER BY mr.""Timestamp"" DESC
        LIMIT @PageSize OFFSET @Offset";
        }

        /// <summary>
        /// Build SQL query for daily aggregated readings
        /// </summary>
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

        /// <summary>
        /// Build SQL query for monthly aggregated readings
        /// </summary>
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

        /// <summary>
        /// Build SQL query for yearly aggregated readings
        /// </summary>
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

        /// <summary>
        /// Get total count of readings for pagination
        /// </summary>
        private async Task<int> GetReadingsCount(int? meterId, string viewType, DateTime? startDate = null, DateTime? endDate = null)
        {
            using (var connection = new NpgsqlConnection(_databaseService.GetConnectionString()))
            {
                await connection.OpenAsync();

                string sql = viewType.ToLower() switch
                {
                    "daily" => BuildCountQuery("MeterReadingsDaily", "ReadingDate", meterId, startDate, endDate),
                    "monthly" => BuildCountQuery("MeterReadingsMonthly", "Year, Month", meterId, startDate, endDate),
                    "yearly" => BuildCountQuery("MeterReadingsYearly", "Year", meterId, startDate, endDate),
                    _ => BuildCountQuery("MeterReadings", "Timestamp", meterId, startDate, endDate)
                };

                using (var command = new NpgsqlCommand(sql, connection))
                {
                    AddParametersToCommand(command, meterId, startDate, endDate, 0, 0);
                    var result = await command.ExecuteScalarAsync();
                    return Convert.ToInt32(result);
                }
            }
        }

        /// <summary>
        /// Build count query for pagination
        /// </summary>
        private string BuildCountQuery(string tableName, string dateColumn, int? meterId, DateTime? startDate, DateTime? endDate)
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

        /// <summary>
        /// Add common parameters to SQL commands
        /// </summary>
        private void AddParametersToCommand(NpgsqlCommand command, int? meterId, DateTime? startDate, DateTime? endDate, int offset, int pageSize)
        {
            if (meterId.HasValue)
                command.Parameters.AddWithValue("@MeterId", meterId.Value);
            if (startDate.HasValue)
                command.Parameters.AddWithValue("@StartDate", startDate.Value);
            if (endDate.HasValue)
                command.Parameters.AddWithValue("@EndDate", endDate.Value);
            if (pageSize > 0)
            {
                command.Parameters.AddWithValue("@PageSize", pageSize);
                command.Parameters.AddWithValue("@Offset", offset);
            }
        }

        /// <summary>
        /// Map database reader to MeterReading object
        /// </summary>
        private MeterReading MapReaderToMeterReading(NpgsqlDataReader reader, string viewType)
        {
            var reading = new MeterReading
            {
                ReadingId = reader.GetInt32("ReadingId"),
                MeterId = reader.GetInt32("MeterId"),
                MeterName = reader.GetString("MeterName"),
                Timestamp = reader.GetDateTime("Timestamp"),
                Value = reader.GetDecimal("Value")
            };

            // Add aggregated data for non-raw views
            if (viewType != "raw")
            {
                if (!reader.IsDBNull(reader.GetOrdinal("MinValue")))
                    reading.MinValue = reader.GetDecimal("MinValue");
                if (!reader.IsDBNull(reader.GetOrdinal("MaxValue")))
                    reading.MaxValue = reader.GetDecimal("MaxValue");
                if (!reader.IsDBNull(reader.GetOrdinal("SumValue")))
                    reading.SumValue = reader.GetDecimal("SumValue");
                if (!reader.IsDBNull(reader.GetOrdinal("ReadingCount")))
                    reading.ReadingCount = reader.GetInt32("ReadingCount");

                // Monthly readings have Year and Month
                if (viewType == "monthly")
                {
                    if (!reader.IsDBNull(reader.GetOrdinal("Year")))
                        reading.Year = reader.GetInt32("Year");
                    if (!reader.IsDBNull(reader.GetOrdinal("Month")))
                        reading.Month = reader.GetInt32("Month");
                }
                // Yearly readings have Year
                else if (viewType == "yearly")
                {
                    if (!reader.IsDBNull(reader.GetOrdinal("Year")))
                        reading.Year = reader.GetInt32("Year");
                }
            }
            else
            {
                // Raw readings have quality
                if (!reader.IsDBNull(reader.GetOrdinal("Quality")))
                    reading.Quality = reader.GetInt32("Quality");
            }

            return reading;
        }

        /// <summary>
        /// Get available meters for dropdown
        /// </summary>
        private async Task<List<MeterOption>> GetAvailableMeters()
        {
            var meters = new List<MeterOption>();

            using (var connection = new NpgsqlConnection(_databaseService.GetConnectionString()))
            {
                await connection.OpenAsync();

                string sql = @"
                    SELECT ""MeterId"", ""Name"", ""Unit"", ""Type""
                    FROM ""Meters""
                    WHERE ""Active"" = true
                    ORDER BY ""Name""";

                using (var command = new NpgsqlCommand(sql, connection))
                using (var reader = await command.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        meters.Add(new MeterOption
                        {
                            MeterId = reader.GetInt32("MeterId"),
                            Name = reader.GetString("Name"),
                            Unit = reader.IsDBNull(reader.GetOrdinal("Unit")) ? "" : reader.GetString("Unit"),
                            Type = reader.GetString("Type")
                        });
                    }
                }
            }

            return meters;
        }

        /// <summary>
        /// Calculate statistics for a meter
        /// </summary>
        private async Task<MeterStats> CalculateMeterStats(int meterId, DateTime? startDate = null, DateTime? endDate = null)
        {
            using (var connection = new NpgsqlConnection(_databaseService.GetConnectionString()))
            {
                await connection.OpenAsync();

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

                using (var command = new NpgsqlCommand(sql, connection))
                {
                    command.Parameters.AddWithValue("@MeterId", meterId);
                    if (startDate.HasValue) command.Parameters.AddWithValue("@StartDate", startDate.Value);
                    if (endDate.HasValue) command.Parameters.AddWithValue("@EndDate", endDate.Value);

                    using (var reader = await command.ExecuteReaderAsync())
                    {
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
                    }
                }
            }

            return new MeterStats();
        }

        #endregion
    }
}