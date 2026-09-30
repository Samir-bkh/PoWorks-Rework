using Npgsql;
using NpgsqlTypes;
using PoWorks_Rework.Models;

namespace PoWorks_Rework.Services
{
    /// <summary>
    /// Background service that periodically imports real meter readings from PCVue.
    /// Failed or empty historian queries are not converted into synthetic readings.
    /// </summary>
    public class AutoImportWorker : BackgroundService
    {
        private readonly ILogger<AutoImportWorker> _logger;
        private readonly IServiceProvider _serviceProvider;
        private readonly EncryptionService _encryptionService;
        private readonly AutoImportSchedule _schedule = new();
        private const int DefaultCycleDelayMinutes = 1;

        public AutoImportWorker(ILogger<AutoImportWorker> logger, IServiceProvider serviceProvider, EncryptionService encryptionService)
        {
            _logger = logger;
            _serviceProvider = serviceProvider;
            _encryptionService = encryptionService;
        }

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
                    _logger.LogError(ex, "IMPORT CYCLE FAILED");
                }

                _logger.LogInformation("--- END OF CYCLE, GOING TO SLEEP ({Delay} min) ---", cycleDelayMinutes);
                await Task.Delay(TimeSpan.FromMinutes(cycleDelayMinutes), stoppingToken);
            }
        }

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
                    stoppingToken.ThrowIfCancellationRequested();
                    _logger.LogInformation(">> Processing company ID: {CompanyId}", companyId);

                    var apiSettings = await GetApiSettingsAsync(dbService, companyId);
                    if (apiSettings == null)
                    {
                        _schedule.Forget(companyId);
                        _logger.LogWarning(">> No WebService settings found for company {Id}.", companyId);
                        continue;
                    }

                    if (!apiSettings.EnableAutomaticImport)
                    {
                        _schedule.Forget(companyId);
                        _logger.LogInformation(">> Auto-import disabled for company {Id}.", companyId);
                        continue;
                    }

                    if (!_schedule.TryStart(companyId, apiSettings.ConnectionId,
                            apiSettings.AutoImportIntervalMinutes, DateTimeOffset.UtcNow))
                    {
                        _logger.LogDebug(">> Auto-import not due yet for company {Id}, connection {ConnectionId} ({Interval} min).",
                            companyId, apiSettings.ConnectionId, apiSettings.AutoImportIntervalMinutes);
                        continue;
                    }

                    _logger.LogInformation(">> Auto-import starting for company {Id}, connection {ConnectionId} ({Interval} min).",
                        companyId, apiSettings.ConnectionId, apiSettings.AutoImportIntervalMinutes);

                    var testToken = await webService.GetValidAccessTokenAsync(apiSettings);
                    if (string.IsNullOrEmpty(testToken))
                    {
                        _logger.LogWarning(">> Unable to retrieve the PCVue token for company {Id}.", companyId);
                        continue;
                    }

                    await dbService.ExecuteWithCompanyIsolationAsync(companyId, async (connection, transaction) =>
                    {
                        var metersToImport = await GetMetersForCurrentCompanyAsync(connection, transaction, companyId);
                        _logger.LogInformation(">> Found {Count} active meter(s) to import for company {Id}.", metersToImport.Count, companyId);
                        if (metersToImport.Count == 0) return;

                        var meterIds = metersToImport.Select(m => m.MeterId).ToArray();
                        var lastReadings = await GetLastKnownReadingsAsync(
                            connection,
                            transaction,
                            companyId,
                            meterIds);
                        DateTime endTime = DateTime.Now;

                        var meterGroups = metersToImport
                            .GroupBy(m => lastReadings.TryGetValue(m.MeterId, out var last)
                                ? last.Timestamp : endTime.AddHours(-1))
                            .ToList();

                        var allTrendResults = new List<VariableTrendResult>();
                        foreach (var group in meterGroups)
                        {
                            stoppingToken.ThrowIfCancellationRequested();
                            var groupStartTime = group.Key;
                            if (groupStartTime >= endTime) continue;

                            var variableNames = group.Select(m => m.OriginalVariableName).ToList();
                            _logger.LogInformation(">> Calling PCVue for {Count} variable(s) since {Time}",
                                variableNames.Count, groupStartTime);
                            var groupResults = await trendsService.ProcessVariablesTrendsAsync(
                                variableNames,
                                groupStartTime.ToUniversalTime(),
                                endTime.ToUniversalTime(),
                                apiSettings,
                                $"Company {companyId}");
                            allTrendResults.AddRange(groupResults);
                        }

                        // A lookup prevents a per-meter linear scan of up to tens of
                        // thousands of result objects, while keeping the prior first-match behavior.
                        var resultsByVariable = allTrendResults.ToLookup(
                            result => result.VariableName,
                            StringComparer.Ordinal);

                        using var tempTableCmd = new NpgsqlCommand(@"
                            CREATE TEMP TABLE ""TempMeterReadings"" (LIKE ""MeterReadings"" EXCLUDING CONSTRAINTS) ON COMMIT DROP;
                            ALTER TABLE ""TempMeterReadings"" DROP COLUMN ""ReadingId"";
                        ", connection, transaction);
                        await tempTableCmd.ExecuteNonQueryAsync(stoppingToken);

                        int realPointsRetrieved = 0;
                        int failedMeters = 0;
                        int noNewDataMeters = 0;

                        using (var writer = await connection.BeginBinaryImportAsync(@"
                            COPY ""TempMeterReadings""
                            (""MeterId"", ""Timestamp"", ""Value"", ""Quality"", ""CompanyId"")
                            FROM STDIN (FORMAT BINARY)", stoppingToken))
                        {
                            foreach (var meter in metersToImport)
                            {
                                stoppingToken.ThrowIfCancellationRequested();
                                var result = resultsByVariable[meter.OriginalVariableName].FirstOrDefault();
                                if (result is null || !result.Success || result.MaxNumberExceeded)
                                {
                                    failedMeters++;
                                    continue;
                                }

                                var meterStartTime = lastReadings.TryGetValue(meter.MeterId, out var last)
                                    ? last.Timestamp : endTime.AddHours(-1);
                                var readings = AutoImportReadingPolicy.SelectNewRealReadings(
                                    result,
                                    meterStartTime);
                                if (readings.Count == 0)
                                {
                                    noNewDataMeters++;
                                    continue;
                                }

                                foreach (var reading in readings)
                                {
                                    await writer.StartRowAsync(stoppingToken);
                                    await writer.WriteAsync(meter.MeterId, NpgsqlDbType.Integer, stoppingToken);
                                    await writer.WriteAsync(reading.Timestamp, NpgsqlDbType.Timestamp, stoppingToken);
                                    await writer.WriteAsync(reading.Value, NpgsqlDbType.Numeric, stoppingToken);
                                    await writer.WriteAsync(192, NpgsqlDbType.Integer, stoppingToken);
                                    await writer.WriteAsync(companyId, NpgsqlDbType.Integer, stoppingToken);
                                    realPointsRetrieved++;
                                }
                            }
                            await writer.CompleteAsync(stoppingToken);
                        }

                        if (realPointsRetrieved == 0)
                        {
                            _logger.LogInformation(
                                ">> Import completed: 0 real PCVue points; {Empty} unchanged meter(s), {Failed} failed meter(s). No synthetic readings written.",
                                noNewDataMeters,
                                failedMeters);
                            return;
                        }

                        using var insertCmd = new NpgsqlCommand(@"
                            INSERT INTO ""MeterReadings""
                            (""MeterId"", ""Timestamp"", ""Value"", ""Quality"", ""CompanyId"")
                            SELECT ""MeterId"", ""Timestamp"", ""Value"", ""Quality"", @companyId
                            FROM ""TempMeterReadings""
                            ON CONFLICT (""MeterId"", ""Timestamp"") DO NOTHING", connection, transaction);
                        insertCmd.Parameters.AddWithValue("companyId", companyId);
                        insertCmd.CommandTimeout = 300;
                        var inserted = await insertCmd.ExecuteNonQueryAsync(stoppingToken);
                        _logger.LogInformation(
                            ">> Import completed: {Retrieved} real PCVue points received, {Inserted} new database rows, {Empty} unchanged meter(s), {Failed} failed meter(s); no synthetic readings generated.",
                            realPointsRetrieved, inserted, noNewDataMeters, failedMeters);
                    });
                }
            }
            finally
            {
                ImportLock.Gate.Release();
            }
        }

        /// <summary>
        /// Retrieves only the last reading for the currently selected workspace and meter IDs.
        /// </summary>
        private async Task<Dictionary<int, (DateTime Timestamp, decimal Value)>> GetLastKnownReadingsAsync(
            NpgsqlConnection conn,
            NpgsqlTransaction tr,
            int companyId,
            IReadOnlyCollection<int> meterIds)
        {
            var dict = new Dictionary<int, (DateTime Timestamp, decimal Value)>();
            if (meterIds.Count == 0) return dict;

            using var cmd = new NpgsqlCommand(AutoImportQueries.LastReadings, conn, tr);
            cmd.Parameters.AddWithValue("companyId", companyId);
            cmd.Parameters.AddWithValue("meterIds", meterIds.Distinct().ToArray());
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                dict[reader.GetInt32(0)] = (reader.GetDateTime(1), reader.GetDecimal(2));

            return dict;
        }

        private async Task<int> GetMinimumAutoImportIntervalMinutesAsync(CancellationToken stoppingToken)
        {
            using var scope = _serviceProvider.CreateScope();
            var dbService = scope.ServiceProvider.GetRequiredService<DatabaseService>();
            if (!dbService.IsInitialized) return DefaultCycleDelayMinutes;

            using var conn = dbService.CreateNewConnection();
            await conn.OpenAsync(stoppingToken);
            using var cmd = new NpgsqlCommand(@"
                SELECT MIN(""AutoImportIntervalMinutes"")
                FROM ""WebServiceConnections""
                WHERE ""EnableAutomaticImport"" = TRUE
                  AND ""IsActive"" = TRUE", conn);
            var result = await cmd.ExecuteScalarAsync(stoppingToken);
            return result is null or DBNull
                ? DefaultCycleDelayMinutes
                : Math.Clamp(Convert.ToInt32(result), 1, 1440);
        }

        private async Task<List<int>> GetAllCompanyIdsAsync(DatabaseService dbService)
        {
            var ids = new List<int>();
            try
            {
                using var conn = dbService.CreateNewConnection();
                await conn.OpenAsync();
                using var cmd = new NpgsqlCommand(
                    "SELECT \"CompanyId\" FROM \"Companies\" WHERE \"Active\" = TRUE", conn);
                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync()) ids.Add(reader.GetInt32(0));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unable to retrieve the companies available for auto-import.");
            }
            return ids;
        }

        private async Task<List<MeterForTrendsAnalysis>> GetMetersForCurrentCompanyAsync(
            NpgsqlConnection conn,
            NpgsqlTransaction tr,
            int companyId)
        {
            var meters = new List<MeterForTrendsAnalysis>();
            using var cmd = new NpgsqlCommand(AutoImportQueries.ActiveMeters, conn, tr);
            cmd.Parameters.AddWithValue("companyId", companyId);
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

        private async Task<PCVueWebServiceSettings?> GetApiSettingsAsync(DatabaseService dbService, int companyId)
        {
            try
            {
                return await dbService.ExecuteWithCompanyIsolationAsync(companyId, async (conn, tr) =>
                {
                    using var cmd = new NpgsqlCommand(AutoImportQueries.ApiSettings, conn, tr);
                    cmd.Parameters.AddWithValue("companyId", companyId);
                    using var reader = await cmd.ExecuteReaderAsync();
                    if (await reader.ReadAsync())
                    {
                        return new PCVueWebServiceSettings
                        {
                            ConnectionId = reader.IsDBNull(0) ? "" : reader.GetValue(0).ToString() ?? "",
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
                            EnableAutomaticImport = !reader.IsDBNull(13) && reader.GetBoolean(13),
                            AutoImportIntervalMinutes = reader.IsDBNull(14)
                                ? 1
                                : Math.Clamp(reader.GetInt32(14), 1, 1440)
                        };
                    }

                    _logger.LogWarning(">> No active WebService connection found for company {Id}.", companyId);
                    return null;
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, ">> Error reading the API settings for company {CompanyId}", companyId);
                return null;
            }
        }
    }
}
