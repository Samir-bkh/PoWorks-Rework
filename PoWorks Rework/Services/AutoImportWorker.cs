using Npgsql;
using NpgsqlTypes;
using PoWorks_Rework.Models;

namespace PoWorks_Rework.Services
{
    /// <summary>
    /// Background service that periodically imports meter readings from PCVue web services.
    /// Runs automatic import cycles for all companies with auto-import enabled.
    /// </summary>
    public class AutoImportWorker : BackgroundService
    {
        private readonly ILogger<AutoImportWorker> _logger;
        private readonly IServiceProvider _serviceProvider;
        private readonly EncryptionService _encryptionService;
        private const int DefaultCycleDelayMinutes = 1;

        /// <summary>
        /// Initializes the auto import worker with logging, service provider, and encryption dependencies.
        /// </summary>
        public AutoImportWorker(ILogger<AutoImportWorker> logger, IServiceProvider serviceProvider, EncryptionService encryptionService)
        {
            _logger = logger;
            _serviceProvider = serviceProvider;
            _encryptionService = encryptionService;
        }

        /// <summary>
        /// Runs the background import loop, executing import cycles at the configured interval.
        /// </summary>
        /// <param name="stoppingToken">The cancellation token to stop the background service.</param>
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("PILOT START - The import service has started.");

            while (!stoppingToken.IsCancellationRequested)
            {
                int cycleDelayMinutes = DefaultCycleDelayMinutes;
                try
                {
                    cycleDelayMinutes = await GetMinimumAutoImportIntervalMinutesAsync(stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Unable to read auto-import interval from database, using default ({Default} min).", DefaultCycleDelayMinutes);
                }

                _logger.LogInformation("--- START OF AN IMPORT CYCLE ({Delay} min) ---", cycleDelayMinutes);
                try
                {
                    await RunImportCycleAsync(stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError("IMPORT CYCLE FAILED | Reason: {Message} | StackTrace: {Stack}", ex.Message, ex.StackTrace);
                }

                _logger.LogInformation("--- END OF CYCLE, GOING TO SLEEP ({Delay} min) ---", cycleDelayMinutes);
                await Task.Delay(TimeSpan.FromMinutes(cycleDelayMinutes), stoppingToken);
            }
        }

        /// <summary>
        /// Executes a single import cycle for all companies with auto-import enabled.
        /// </summary>
        /// <param name="stoppingToken">The cancellation token.</param>
        private async Task RunImportCycleAsync(CancellationToken stoppingToken)
        {
            if (!await ImportLock.Gate.WaitAsync(0, stoppingToken))
            {
                _logger.LogWarning("Manual import or another process is running, skipping this auto-import cycle.");
                return;
            }

            try
            {
                using var scope = _serviceProvider.CreateScope();
                var dbService = scope.ServiceProvider.GetRequiredService<DatabaseService>();
                var trendsService = scope.ServiceProvider.GetRequiredService<TrendsService>();
                var webService = scope.ServiceProvider.GetRequiredService<PCVueWebService>();

                var companyIds = await GetAllCompanyIdsAsync(dbService);
                _logger.LogInformation(">> Found {Count} compan(y/ies) in the database.", companyIds.Count);

                foreach (var companyId in companyIds)
                {
                    _logger.LogInformation(">> Processing company ID: {CompanyId}", companyId);

                    var apiSettings = await GetApiSettingsAsync(dbService, companyId);
                    if (apiSettings == null)
                    {
                        _logger.LogWarning(">> ERROR: No WebService settings found for company {Id}. Moving to the next one.", companyId);
                        continue;
                    }

                    if (!apiSettings.EnableAutomaticImport)
                    {
                        _logger.LogInformation(">> Auto-import disabled for company {Id}. Moving to the next one.", companyId);
                        continue;
                    }

                    var testToken = await webService.GetValidAccessTokenAsync(apiSettings);
                    if (string.IsNullOrEmpty(testToken))
                    {
                        _logger.LogWarning(">> ERROR: Unable to retrieve the PCVue token for company {Id}.", companyId);
                        continue;
                    }

                    await dbService.ExecuteWithCompanyIsolationAsync(companyId, async (connection, transaction) =>
                    {
                        var metersToImport = await GetMetersForCurrentCompanyAsync(connection, transaction);
                        _logger.LogInformation(">> Found {Count} active meter(s) to import for company {Id}.", metersToImport.Count, companyId);

                        if (!metersToImport.Any()) return;

                        var lastReadings = await GetLastKnownReadingsAsync(connection, transaction);
                        DateTime endTime = DateTime.Now;

                        var meterGroups = metersToImport
                            .GroupBy(m => lastReadings.ContainsKey(m.MeterId) ? lastReadings[m.MeterId].Timestamp : endTime.AddHours(-1))
                            .ToList();

                        var allTrendResults = new List<VariableTrendResult>();

                        foreach (var group in meterGroups)
                        {
                            var groupStartTime = group.Key;
                            if (groupStartTime >= endTime) continue;

                            var variableNames = group.Select(m => m.OriginalVariableName).ToList();
                            _logger.LogInformation(">> Calling PCVue for {Count} variable(s) since {Time}", variableNames.Count, groupStartTime);

                            var groupResults = await trendsService.ProcessVariablesTrendsAsync(
                                variableNames,
                                groupStartTime.ToUniversalTime(),
                                endTime.ToUniversalTime(),
                                apiSettings);

                            allTrendResults.AddRange(groupResults);
                        }

                        using var tempTableCmd = new NpgsqlCommand(@"
                            CREATE TEMP TABLE ""TempMeterReadings"" (LIKE ""MeterReadings"" EXCLUDING CONSTRAINTS) ON COMMIT DROP;
                            ALTER TABLE ""TempMeterReadings"" DROP COLUMN ""ReadingId"";
                        ", connection, transaction);

                        await tempTableCmd.ExecuteNonQueryAsync();

                        int pointsAdded = 0;
                        int paddingAdded = 0;

                        using (var writer = await connection.BeginBinaryImportAsync(@"COPY ""TempMeterReadings"" (""MeterId"", ""Timestamp"", ""Value"", ""Quality"", ""CompanyId"") FROM STDIN (FORMAT BINARY)", stoppingToken))
                        {
                            foreach (var meter in metersToImport)
                            {
                                var result = allTrendResults.FirstOrDefault(r => r.VariableName == meter.OriginalVariableName);

                                bool hasNewData = false;
                                decimal latestValue = 0;
                                DateTime meterStartTime = lastReadings.ContainsKey(meter.MeterId) ? lastReadings[meter.MeterId].Timestamp : endTime.AddHours(-1);

                                if (result != null && result.Success && result.TrendData != null)
                                {
                                    foreach (var point in result.TrendData)
                                    {
                                        if (point.Quality?.ToLower() != "good" || !point.TimestampParsed.HasValue) continue;

                                        DateTime localTime = point.TimestampParsed.Value.ToLocalTime();
                                        if (localTime <= meterStartTime) continue;

                                        decimal pointValue = Convert.ToDecimal(point.Value);

                                        await writer.StartRowAsync(stoppingToken);
                                        await writer.WriteAsync(meter.MeterId, NpgsqlDbType.Integer, stoppingToken);
                                        await writer.WriteAsync(localTime, NpgsqlDbType.Timestamp, stoppingToken);
                                        await writer.WriteAsync(pointValue, NpgsqlDbType.Numeric, stoppingToken);
                                        await writer.WriteAsync(192, NpgsqlDbType.Integer, stoppingToken);
                                        await writer.WriteAsync(companyId, NpgsqlDbType.Integer, stoppingToken);

                                        hasNewData = true;
                                        latestValue = pointValue;
                                        pointsAdded++;
                                    }
                                }

                                if (!hasNewData && lastReadings.ContainsKey(meter.MeterId))
                                {
                                    latestValue = lastReadings[meter.MeterId].Value;
                                    hasNewData = true;
                                }

                                if (hasNewData)
                                {
                                    await writer.StartRowAsync(stoppingToken);
                                    await writer.WriteAsync(meter.MeterId, NpgsqlDbType.Integer, stoppingToken);
                                    await writer.WriteAsync(endTime, NpgsqlDbType.Timestamp, stoppingToken);
                                    await writer.WriteAsync(latestValue, NpgsqlDbType.Numeric, stoppingToken);
                                    await writer.WriteAsync(192, NpgsqlDbType.Integer, stoppingToken);
                                    await writer.WriteAsync(companyId, NpgsqlDbType.Integer, stoppingToken);
                                    paddingAdded++;
                                }
                            }
                            await writer.CompleteAsync(stoppingToken);
                        }

                        _logger.LogInformation(">> Import completed: {Points} new PCVue points, {Padding} padding points generated.", pointsAdded, paddingAdded);

                        var insertCmd = new NpgsqlCommand(@"
                            INSERT INTO ""MeterReadings"" (""MeterId"", ""Timestamp"", ""Value"", ""Quality"", ""CompanyId"")
                            SELECT ""MeterId"", ""Timestamp"", ""Value"", ""Quality"", @companyId
                            FROM ""TempMeterReadings""
                            ON CONFLICT (""MeterId"", ""Timestamp"") DO NOTHING", connection, transaction);

                        insertCmd.Parameters.AddWithValue("companyId", companyId);
                        insertCmd.CommandTimeout = 300;
                        await insertCmd.ExecuteNonQueryAsync();
                    });
                }
            }
            finally
            {
                ImportLock.Gate.Release();
            }
        }

        /// <summary>
        /// Retrieves the most recent reading timestamp and value for each meter.
        /// </summary>
        /// <param name="conn">The database connection to use.</param>
        /// <param name="tr">The transaction to use.</param>
        /// <returns>A dictionary mapping meter IDs to their last known reading.</returns>
        private async Task<Dictionary<int, (DateTime Timestamp, decimal Value)>> GetLastKnownReadingsAsync(NpgsqlConnection conn, NpgsqlTransaction tr)
        {
            var dict = new Dictionary<int, (DateTime Timestamp, decimal Value)>();
            using var cmd = new NpgsqlCommand(@"
                SELECT DISTINCT ON (""MeterId"") ""MeterId"", ""Timestamp"", ""Value""
                FROM ""MeterReadings""
                ORDER BY ""MeterId"", ""Timestamp"" DESC", conn, tr);
            using var reader = await cmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                dict[reader.GetInt32(0)] = (reader.GetDateTime(1), reader.GetDecimal(2));
            }
            return dict;
        }

        /// <summary>
        /// Reads the minimum auto-import interval from the database across all active connections.
        /// </summary>
        /// <param name="stoppingToken">The cancellation token.</param>
        /// <returns>The minimum interval in minutes, clamped between 1 and 1440.</returns>
        private async Task<int> GetMinimumAutoImportIntervalMinutesAsync(CancellationToken stoppingToken)
        {
            using var scope = _serviceProvider.CreateScope();
            var dbService = scope.ServiceProvider.GetRequiredService<DatabaseService>();

            if (!dbService.IsInitialized)
            {
                return DefaultCycleDelayMinutes;
            }

            using var conn = dbService.CreateNewConnection();
            await conn.OpenAsync(stoppingToken);

            using var cmd = new NpgsqlCommand(@"
                SELECT MIN(""AutoImportIntervalMinutes"")
                FROM ""WebServiceConnections""
                WHERE ""EnableAutomaticImport"" = TRUE
                  AND ""IsActive"" = TRUE", conn);

            var result = await cmd.ExecuteScalarAsync(stoppingToken);
            if (result == null || result == DBNull.Value)
            {
                return DefaultCycleDelayMinutes;
            }

            return Math.Clamp(Convert.ToInt32(result), 1, 1440);
        }

        /// <summary>
        /// Retrieves the list of all company IDs from the database.
        /// </summary>
        /// <param name="dbService">The database service to use.</param>
        /// <returns>A list of company IDs.</returns>
        private async Task<List<int>> GetAllCompanyIdsAsync(DatabaseService dbService)
        {
            var ids = new List<int>();
            try
            {
                using var conn = dbService.CreateNewConnection();
                await conn.OpenAsync();
                using var cmd = new NpgsqlCommand("SELECT \"CompanyId\" FROM \"Companies\"", conn);
                using var reader = await cmd.ExecuteReaderAsync();

                while (await reader.ReadAsync())
                {
                    ids.Add(reader.GetInt32(0));
                }
            }
            catch { }
            return ids;
        }

        /// <summary>
        /// Retrieves the active meters to import for the current company.
        /// </summary>
        /// <param name="conn">The database connection to use.</param>
        /// <param name="tr">The transaction to use.</param>
        /// <returns>A list of meters for trends analysis.</returns>
        private async Task<List<MeterForTrendsAnalysis>> GetMetersForCurrentCompanyAsync(NpgsqlConnection conn, NpgsqlTransaction tr)
        {
            var meters = new List<MeterForTrendsAnalysis>();
            using var cmd = new NpgsqlCommand("SELECT \"MeterId\", \"Name\", \"Active\" FROM \"Meters\" WHERE (\"Name\" LIKE '%.%' OR \"Name\" LIKE 'varsets.%') AND \"Active\" = true", conn, tr);
            using var reader = await cmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                meters.Add(new MeterForTrendsAnalysis
                {
                    MeterId = reader.GetInt32(0),
                    Name = reader.GetString(1),
                    OriginalVariableName = reader.GetString(1)
                });
            }
            return meters;
        }

        /// <summary>
        /// Retrieves the web service API settings for a specific company.
        /// </summary>
        /// <param name="dbService">The database service to use.</param>
        /// <param name="companyId">The company ID to retrieve settings for.</param>
        /// <returns>The web service settings, or null if not found.</returns>
        private async Task<PCVueWebServiceSettings?> GetApiSettingsAsync(DatabaseService dbService, int companyId)
        {
            try
            {
                return await dbService.ExecuteWithCompanyIsolationAsync(companyId, async (conn, tr) =>
                {
                    string sql = @"SELECT ""ConnectionId"", ""ConnectionName"", ""BaseUrl"", ""ClientId"", ""ClientSecret"",
                                  ""ApiKey"", ""Username"", ""Password"", ""AuthType"", ""TimeoutSeconds"",
                                  ""ProjectName"", ""IsDefault"", ""IsActive"", ""EnableAutomaticImport""
                           FROM ""WebServiceConnections""
                           LIMIT 1";

                    using var cmd = new NpgsqlCommand(sql, conn, tr);
                    using var reader = await cmd.ExecuteReaderAsync();

                    if (await reader.ReadAsync())
                    {
                        return new PCVueWebServiceSettings
                        {
                            // Use GetValue().ToString() instead of GetString() in case the ID is a number/GUID
                            ConnectionId = reader.IsDBNull(0) ? "" : reader.GetValue(0).ToString(),
                            ConnectionName = reader.IsDBNull(1) ? "" : reader.GetString(1),
                            BaseUrl = reader.IsDBNull(2) ? "" : reader.GetString(2),
                            ClientId = reader.IsDBNull(3) ? "" : reader.GetString(3),
                            ClientSecret = reader.IsDBNull(4) ? "" : _encryptionService.Decrypt(reader.GetString(4)),
                            ApiKey = reader.IsDBNull(5) ? "" : reader.GetString(5),
                            Username = reader.IsDBNull(6) ? "" : reader.GetString(6),
                            Password = reader.IsDBNull(7) ? "" : _encryptionService.Decrypt(reader.GetString(7)),
                            AuthType = reader.IsDBNull(8) ? AuthenticationType.OAuth : (AuthenticationType)Convert.ToInt32(reader.GetValue(8)),
                            TimeoutSeconds = reader.IsDBNull(9) ? 30 : Convert.ToInt32(reader.GetValue(9)),
                            ProjectName = reader.IsDBNull(10) ? "" : reader.GetString(10),
                            IsDefault = !reader.IsDBNull(11) && reader.GetBoolean(11),

                            // Column 13 = EnableAutomaticImport
                            EnableAutomaticImport = !reader.IsDBNull(13) && reader.GetBoolean(13)
                        };
                    }

                    _logger.LogWarning(">> DB: The SQL query returned no rows for company {Id}.", companyId);
                    return null;
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, ">> CRITICAL ERROR while reading the API settings for company {CompanyId}", companyId);
                return null;
            }
        }
    }
}