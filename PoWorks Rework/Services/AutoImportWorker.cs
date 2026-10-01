using System.Diagnostics;
using Npgsql;
using PoWorks_Rework.Models;

namespace PoWorks_Rework.Services
{
    /// <summary>
    /// Background service that periodically samples the current value of each meter
    /// from PCVue. Each polling pass writes at most one reading per meter.
    /// </summary>
    public class AutoImportWorker : BackgroundService
    {
        private readonly ILogger<AutoImportWorker> _logger;
        private readonly IServiceProvider _serviceProvider;
        private readonly EncryptionService _encryptionService;
        private readonly AutoImportSchedule _schedule = new();
        private const int DefaultCycleDelayMinutes = 1;
        private const int WriteBatchSize = 25;

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
                var cycleTimer = Stopwatch.StartNew();
                int cycleDelayMinutes = DefaultCycleDelayMinutes;
                try
                {
                    cycleDelayMinutes = await GetMinimumAutoImportIntervalMinutesAsync(stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Unable to read auto-import interval from database, using default ({Default} min).", DefaultCycleDelayMinutes);
                }

                _logger.LogInformation("--- AUTO-IMPORT CHECK (minimum selected connection interval {Delay} min) ---", cycleDelayMinutes);
                try
                {
                    await RunImportCycleAsync(stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "IMPORT CYCLE FAILED");
                }

                var remaining = TimeSpan.FromMinutes(cycleDelayMinutes) - cycleTimer.Elapsed;
                var delay = _schedule.UntilNextDue(DateTimeOffset.UtcNow,
                    remaining > TimeSpan.Zero ? remaining : TimeSpan.FromSeconds(1));
                _logger.LogInformation("--- END OF CYCLE, NEXT CHECK IN {Delay} ---", delay);
                await Task.Delay(delay, stoppingToken);
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
                var webService = scope.ServiceProvider.GetRequiredService<PCVueWebService>();
                var snapshotReader = new AutoImportSnapshotReader(
                    webService,
                    trendsService: scope.ServiceProvider.GetRequiredService<TrendsService>(),
                    logger: scope.ServiceProvider.GetRequiredService<ILogger<AutoImportSnapshotReader>>());

                var companyIds = await GetAllCompanyIdsAsync(dbService);
                _logger.LogInformation(">> Found {Count} compan(y/ies) in the database.", companyIds.Count);

                foreach (var companyId in companyIds)
                {
                    stoppingToken.ThrowIfCancellationRequested();
                    _logger.LogInformation(">> Processing company ID: {CompanyId}", companyId);
                    try
                    {

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

                    // A slow/unavailable PcVue must not hold the shared import lock
                    // for several minutes or prevent the next configured poll.
                    using var deadline = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                    deadline.CancelAfter(TimeSpan.FromSeconds(
                        Math.Clamp(Math.Clamp(apiSettings.AutoImportIntervalMinutes, 1, 1440) * 60 - 15, 30, 90)));
                    var testToken = await webService.GetValidAccessTokenAsync(
                        apiSettings, cancellationToken: deadline.Token);
                    if (string.IsNullOrEmpty(testToken))
                    {
                        _logger.LogWarning(">> Unable to retrieve the PCVue token for company {Id}.", companyId);
                        continue;
                    }

                    var metersToImport = await dbService.ExecuteWithCompanyIsolationAsync(
                        companyId, (connection, transaction) =>
                            GetMetersForCurrentCompanyAsync(connection, transaction, companyId));
                    _logger.LogInformation(">> Found {Count} active meter(s) to import for company {Id}.", metersToImport.Count, companyId);
                    if (metersToImport.Count == 0) continue;

                    var received = 0;
                    var insertedTotal = 0;
                    var processed = 0;
                    foreach (var batch in metersToImport.Chunk(WriteBatchSize))
                    {
                        deadline.Token.ThrowIfCancellationRequested();
                        var snapshots = await snapshotReader.ReadAsync(
                            apiSettings, batch, deadline.Token,
                            apiSettings.AutoImportIntervalMinutes);
                        processed += batch.Length;
                        received += snapshots.Count;
                        if (snapshots.Count == 0) continue;

                        var inserted = await dbService.ExecuteWithCompanyIsolationAsync(
                            companyId, (connection, transaction) =>
                                AutoImportSnapshotWriter.InsertAsync(
                                    connection, transaction, companyId, snapshots, stoppingToken));
                        insertedTotal += inserted;
                        _logger.LogInformation(">> Imported {Inserted} reading(s), {Processed}/{Total} meters processed.",
                            inserted, processed, metersToImport.Count);
                    }
                    if (received == 0)
                        _logger.LogWarning(">> Import completed: no recent, valid PcVue values; no readings written.");
                    else
                        _logger.LogInformation(
                            ">> Import completed: {Received} current PcVue values received, {Inserted} new database rows, {Skipped} meter(s) with no valid value.",
                            received, insertedTotal, metersToImport.Count - received);
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (OperationCanceledException)
                    {
                        _logger.LogWarning("Auto-import timed out for company {CompanyId}; next poll will retry without blocking manual imports.", companyId);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Auto-import failed for company {CompanyId}; other companies will continue.", companyId);
                    }
                }
            }
            finally
            {
                ImportLock.Gate.Release();
            }
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
                FROM (
                    SELECT DISTINCT ON (ws.""CompanyId"")
                        ws.""AutoImportIntervalMinutes"", ws.""EnableAutomaticImport""
                    FROM ""WebServiceConnections"" ws
                    JOIN ""Companies"" c ON c.""CompanyId"" = ws.""CompanyId""
                    WHERE ws.""IsActive"" = TRUE AND c.""Active"" = TRUE
                    ORDER BY ws.""CompanyId"", ws.""IsDefault"" DESC, ws.""ConnectionId""
                ) selected
                WHERE ""EnableAutomaticImport"" = TRUE", conn);
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
