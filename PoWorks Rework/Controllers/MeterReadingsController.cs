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

        public MeterReadingsController(DatabaseService databaseService, ILogger<MeterReadingsController> logger)
            : base(databaseService)
        {
            _logger = logger;
        }

        /// <summary>
        /// Main meter readings page - shows raw readings by default
        /// </summary>
        // UPDATE this method in your MeterReadingsController.cs

        /// <summary>
        /// Main meter readings page - UPDATED to support multiple meter IDs
        /// </summary>
        public async Task<IActionResult> Index(string meterIds, string viewType = "raw", int page = 1, int pageSize = 50)
        {
            // Check if database is initialized
            if (!_databaseService.IsInitialized)
            {
                TempData["ErrorMessage"] = "Database not configured. Please set up database first.";
                return RedirectToAction("General", "Settings");
            }

            try
            {
                // Parse meter IDs from comma-separated string
                var selectedMeterIds = ParseMeterIds(meterIds);

                // Create view model
                var viewModel = new MeterReadingsViewModel
                {
                    ViewType = viewType,
                    CurrentPage = page,
                    PageSize = pageSize,
                    SelectedMeterIds = selectedMeterIds
                };

                // Load available meters for the multi-select
                viewModel.AvailableMeters = await GetAvailableMeters();

                // Load readings based on view type and selected meters
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
        /// NEW: Parse meter IDs from comma-separated string
        /// </summary>
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

        /// <summary>
        /// UPDATED: Load readings data with multi-meter support
        /// </summary>
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

            // Load meter statistics if meters are selected
            if (viewModel.SelectedMeterIds.Any())
            {
                viewModel.MeterStats = await CalculateMultiMeterStats(
                    viewModel.SelectedMeterIds,
                    viewModel.StartDate,
                    viewModel.EndDate
                );
            }
        }

        /// <summary>
        /// UPDATED: Get readings from different tables with multi-meter support
        /// </summary>
        private async Task<List<MeterReading>> GetReadingsByType(List<int> meterIds, string viewType,
            int page, int pageSize, DateTime? startDate = null, DateTime? endDate = null)
        {
            var readings = new List<MeterReading>();

            try
            {
                using var connection = GetDatabaseConnection();
                await connection.OpenAsync();

                string tableName = GetTableNameForViewType(viewType);
                string query = BuildReadingsQuery(tableName, meterIds, startDate, endDate, page, pageSize);

                using var command = new NpgsqlCommand(query, connection);
                AddMeterIdsParameters(command, meterIds);
                AddDateParameters(command, startDate, endDate);
                AddPaginationParameters(command, page, pageSize);

                using var reader = await command.ExecuteReaderAsync();
                readings = await ReadMeterReadingsFromDataReader(reader, viewType);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error getting {viewType} readings for meters {string.Join(",", meterIds)}");
                throw;
            }

            return readings;
        }

        /// <summary>
        /// UPDATED: Get total count with multi-meter support
        /// </summary>
        private async Task<int> GetReadingsCount(List<int> meterIds, string viewType,
            DateTime? startDate = null, DateTime? endDate = null)
        {
            try
            {
                using var connection = GetDatabaseConnection();
                await connection.OpenAsync();

                string tableName = GetTableNameForViewType(viewType);
                string query = BuildCountQuery(tableName, meterIds, startDate, endDate);

                using var command = new NpgsqlCommand(query, connection);
                AddMeterIdsParameters(command, meterIds);
                AddDateParameters(command, startDate, endDate);

                var result = await command.ExecuteScalarAsync();
                return Convert.ToInt32(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error getting readings count for meters {string.Join(",", meterIds)}");
                return 0;
            }
        }

        /// <summary>
        /// NEW: Calculate statistics for multiple meters
        /// </summary>
        private async Task<MeterStats> CalculateMultiMeterStats(List<int> meterIds,
            DateTime? startDate = null, DateTime? endDate = null)
        {
            var stats = new MeterStats();

            if (!meterIds.Any())
                return stats;

            try
            {
                using var connection = GetDatabaseConnection();
                await connection.OpenAsync();

                // Build query for multi-meter stats
                var whereClause = meterIds.Any() ?
                    $"WHERE mr.\"MeterId\" = ANY(@meterIds)" : "";

                if (startDate.HasValue || endDate.HasValue)
                {
                    whereClause += meterIds.Any() ? " AND " : "WHERE ";
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

                using var command = new NpgsqlCommand(query, connection);

                if (meterIds.Any())
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

                    // Handle meter names array
                    if (!reader.IsDBNull("MeterNames"))
                    {
                        var meterNamesArray = reader.GetValue("MeterNames") as string[];
                        stats.MeterNames = meterNamesArray?.Where(name => !string.IsNullOrEmpty(name)).ToList()
                                          ?? new List<string>();
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error calculating stats for meters {string.Join(",", meterIds)}");
            }

            return stats;
        }

        /// <summary>
        /// NEW: Build readings query with multi-meter support
        /// </summary>
        private string BuildReadingsQuery(string tableName, List<int> meterIds,
            DateTime? startDate, DateTime? endDate, int page, int pageSize)
        {
            var whereClause = BuildWhereClause(meterIds, startDate, endDate);
            var offset = (page - 1) * pageSize;

            return $@"
        SELECT 
            mr.""ReadingId"",
            mr.""MeterId"",
            m.""Name"" as MeterName,
            mr.""Timestamp"",
            mr.""Value"",
            mr.""Quality""
        FROM ""{tableName}"" mr
        LEFT JOIN ""Meters"" m ON mr.""MeterId"" = m.""MeterId""
        {whereClause}
        ORDER BY mr.""Timestamp"" DESC, mr.""MeterId""
        LIMIT @pageSize OFFSET @offset";
        }

        /// <summary>
        /// NEW: Build count query with multi-meter support
        /// </summary>
        private string BuildCountQuery(string tableName, List<int> meterIds,
            DateTime? startDate, DateTime? endDate)
        {
            var whereClause = BuildWhereClause(meterIds, startDate, endDate);

            return $@"
        SELECT COUNT(*) 
        FROM ""{tableName}"" mr
        {whereClause}";
        }

        /// <summary>
        /// NEW: Build WHERE clause for multi-meter queries
        /// </summary>
        private string BuildWhereClause(List<int> meterIds, DateTime? startDate, DateTime? endDate)
        {
            var conditions = new List<string>();

            if (meterIds.Any())
            {
                conditions.Add("mr.\"MeterId\" = ANY(@meterIds)");
            }

            if (startDate.HasValue && endDate.HasValue)
            {
                conditions.Add("mr.\"Timestamp\" BETWEEN @startDate AND @endDate");
            }
            else if (startDate.HasValue)
            {
                conditions.Add("mr.\"Timestamp\" >= @startDate");
            }
            else if (endDate.HasValue)
            {
                conditions.Add("mr.\"Timestamp\" <= @endDate");
            }

            return conditions.Any() ? "WHERE " + string.Join(" AND ", conditions) : "";
        }

        /// <summary>
        /// NEW: Add meter IDs parameters to command
        /// </summary>
        private void AddMeterIdsParameters(NpgsqlCommand command, List<int> meterIds)
        {
            if (meterIds.Any())
            {
                command.Parameters.AddWithValue("@meterIds", meterIds.ToArray());
            }
        }

        /// <summary>
        /// Add date parameters to command
        /// </summary>
        private void AddDateParameters(NpgsqlCommand command, DateTime? startDate, DateTime? endDate)
        {
            if (startDate.HasValue)
                command.Parameters.AddWithValue("@startDate", startDate.Value);

            if (endDate.HasValue)
                command.Parameters.AddWithValue("@endDate", endDate.Value);
        }

        /// <summary>
        /// Add pagination parameters to command
        /// </summary>
        private void AddPaginationParameters(NpgsqlCommand command, int page, int pageSize)
        {
            command.Parameters.AddWithValue("@pageSize", pageSize);
            command.Parameters.AddWithValue("@offset", (page - 1) * pageSize);
        }

        /// <summary>
        /// Get table name based on view type
        /// </summary>
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

        /// <summary>
        /// FIXED: Get ALL available meters for multi-select (no limit)
        /// </summary>
        private async Task<List<MeterOption>> GetAvailableMeters()
        {
            var meters = new List<MeterOption>();

            try
            {
                using var connection = GetDatabaseConnection();
                await connection.OpenAsync();

                // FIXED: Remove any LIMIT clause to get ALL meters
                string query = @"
            SELECT 
                m.""MeterId"", 
                m.""Name"", 
                COALESCE(m.""Unit"", '') as ""Unit"", 
                COALESCE(m.""Type"", 'Unknown') as ""Type"",
                m.""Active"",
                CASE 
                    WHEN m.""ParentId"" IS NULL THEN 'Main'
                    ELSE 'Sub'
                END as ""MeterType""
            FROM ""Meters"" m
            WHERE m.""Active"" = true
            ORDER BY 
                CASE WHEN m.""ParentId"" IS NULL THEN 0 ELSE 1 END,  -- Main meters first
                m.""Name"" ASC"; 
        
        using var command = new NpgsqlCommand(query, connection);
                using var reader = await command.ExecuteReaderAsync();

                _logger.LogInformation("Loading ALL available meters for multi-select...");

                while (await reader.ReadAsync())
                {
                    var meter = new MeterOption
                    {
                        MeterId = reader.GetInt32("MeterId"),
                        Name = reader.GetString("Name"),
                        Unit = reader.GetString("Unit"),
                        Type = reader.GetString("MeterType")  // Use calculated type (Main/Sub)
                    };

                    meters.Add(meter);
                }

                _logger.LogInformation($"Loaded {meters.Count} meters for multi-select dropdown");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting ALL available meters");
                throw;
            }

            return meters;
        }

        /// <summary>
        /// Read meter readings from data reader
        /// </summary>
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

                // Only raw readings have Quality column
                if (viewType == "raw" && !reader.IsDBNull("Quality"))
                {
                    reading.Quality = reader.GetInt32("Quality");
                }

                readings.Add(reading);
            }

            return readings;
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

                _logger.LogInformation($"Executing SQL for {viewType} readings: {sql}");

                using (var command = new NpgsqlCommand(sql, connection))
                {
                    AddParametersToCommand(command, meterId, startDate, endDate, offset, pageSize);

                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        int recordCount = 0;
                        while (await reader.ReadAsync())
                        {
                            var reading = MapReaderToMeterReading(reader, viewType);
                            readings.Add(reading);
                            recordCount++;

                            // Log quality values for debugging (first 5 records only)
                            if (recordCount <= 5 && viewType == "raw")
                            {
                                _logger.LogInformation($"Record {recordCount}: Quality = {reading.Quality}, Quality Description = {reading.QualityDescription}");
                            }
                        }
                        _logger.LogInformation($"Loaded {recordCount} {viewType} readings");
                    }
                }
            }

            return readings;
        }

        /// <summary>
        /// Build SQL query for raw readings - FIXED to ensure Quality is selected correctly
        /// </summary>
        private string BuildRawReadingsQuery(int? meterId, DateTime? startDate, DateTime? endDate, int offset, int pageSize)
        {
            var whereClause = "WHERE 1=1";
            if (meterId.HasValue) whereClause += " AND mr.\"MeterId\" = @MeterId";
            if (startDate.HasValue) whereClause += " AND mr.\"Timestamp\" >= @StartDate";
            if (endDate.HasValue) whereClause += " AND mr.\"Timestamp\" <= @EndDate";

            // FIXED: Explicitly cast Quality to ensure proper data type handling
            return $@"
        SELECT mr.""ReadingId"", mr.""MeterId"", m.""Name"" as ""MeterName"", 
               mr.""Timestamp"", mr.""Value"", 
               COALESCE(mr.""Quality"", -1)::INTEGER as ""Quality""
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
        /// Map database reader to MeterReading object - FIXED Quality handling
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
                // FIXED: Raw readings have quality - handle different quality value scenarios
                if (reader.HasColumn("Quality"))
                {
                    var qualityOrdinal = reader.GetOrdinal("Quality");
                    if (!reader.IsDBNull(qualityOrdinal))
                    {
                        var qualityValue = reader.GetInt32(qualityOrdinal);

                        // Handle the special case where we used -1 for null values in SQL
                        if (qualityValue == -1)
                        {
                            reading.Quality = null;
                        }
                        else
                        {
                            reading.Quality = qualityValue;
                        }

                        // Log for debugging
                        _logger.LogDebug($"Quality value read from DB: {qualityValue}, Assigned to reading: {reading.Quality}");
                    }
                    else
                    {
                        reading.Quality = null;
                        _logger.LogDebug("Quality value is DBNull");
                    }
                }
                else
                {
                    _logger.LogWarning("Quality column not found in result set");
                    reading.Quality = null;
                }
            }

            return reading;
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

// Extension method to check if a column exists in the reader
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