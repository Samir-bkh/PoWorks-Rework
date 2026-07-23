using Npgsql;
using NpgsqlTypes;
using PoWorks_Rework.Models;

namespace PoWorks_Rework.Services
{
    public class AutoImportWorker : BackgroundService
    {
        private readonly ILogger<AutoImportWorker> _logger;
        private readonly IServiceProvider _serviceProvider;
        private readonly EncryptionService _encryptionService;
        private readonly int _cycleDelayMinutes = 1;

        public AutoImportWorker(ILogger<AutoImportWorker> logger, IServiceProvider serviceProvider, EncryptionService encryptionService)
        {
            _logger = logger;
            _serviceProvider = serviceProvider;
            _encryptionService = encryptionService;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("PILOT START - Cycle: {Delay} minutes", _cycleDelayMinutes);

            while (!stoppingToken.IsCancellationRequested)
            {
                try { await RunImportCycleAsync(stoppingToken); }
                catch (Exception ex) { _logger.LogError(ex, "CRITICAL ERROR"); }
                await Task.Delay(TimeSpan.FromMinutes(_cycleDelayMinutes), stoppingToken);
            }
        }

        private async Task RunImportCycleAsync(CancellationToken stoppingToken)
        {
            using var scope = _serviceProvider.CreateScope();
            var dbService = scope.ServiceProvider.GetRequiredService<DatabaseService>();
            var trendsService = scope.ServiceProvider.GetRequiredService<TrendsService>();

            var companyIds = await GetAllCompanyIdsAsync(dbService);

            foreach (var companyId in companyIds)
            {
                _logger.LogInformation("IMPORTING COMPANY: {Id}", companyId);
                var apiSettings = await GetApiSettingsAsync(dbService, companyId);

                if (apiSettings == null)
                {
                    continue;
                }

                await dbService.ExecuteWithCompanyIsolationAsync(companyId, async (connection, transaction) =>
                {
                    var metersToImport = await GetMetersForCurrentCompanyAsync(connection, transaction);

                    if (!metersToImport.Any())
                    {
                        return;
                    }

                    var lastTimestamps = await GetLastTimestampsAsync(connection, transaction);
                    DateTime endTime = DateTime.Now;

                    var meterGroups = metersToImport
                        .GroupBy(m => lastTimestamps.ContainsKey(m.MeterId) ? lastTimestamps[m.MeterId] : endTime.AddDays(-7))
                        .ToList();

                    var allTrendResults = new List<VariableTrendResult>();

                    foreach (var group in meterGroups)
                    {
                        var groupStartTime = group.Key;

                        if (groupStartTime >= endTime)
                        {
                            continue;
                        }

                        var variableNames = group.Select(m => m.OriginalVariableName).ToList();

                        var groupResults = await trendsService.ProcessVariablesTrendsAsync(
                            variableNames,
                            groupStartTime.ToUniversalTime(),
                            endTime.ToUniversalTime(),
                            apiSettings);

                        allTrendResults.AddRange(groupResults);
                    }

                    if (!allTrendResults.Any(r => r.Success && r.TrendData != null && r.TrendData.Any()))
                    {
                        return;
                    }

                    using var tempTableCmd = new NpgsqlCommand(@"
                        CREATE TEMP TABLE ""TempMeterReadings"" (LIKE ""MeterReadings"" EXCLUDING CONSTRAINTS) ON COMMIT DROP;
                        ALTER TABLE ""TempMeterReadings"" DROP COLUMN ""ReadingId"";
                    ", connection, transaction);

                    await tempTableCmd.ExecuteNonQueryAsync();

                    using (var writer = await connection.BeginBinaryImportAsync(@"COPY ""TempMeterReadings"" (""MeterId"", ""Timestamp"", ""Value"", ""Quality"", ""CompanyId"") FROM STDIN (FORMAT BINARY)", stoppingToken))
                    {
                        foreach (var result in allTrendResults)
                        {
                            if (!result.Success || result.TrendData == null)
                            {
                                continue;
                            }

                            var meter = metersToImport.FirstOrDefault(m => m.OriginalVariableName == result.VariableName);

                            if (meter == null)
                            {
                                continue;
                            }

                            var meterStartTime = lastTimestamps.ContainsKey(meter.MeterId) ? lastTimestamps[meter.MeterId] : endTime.AddDays(-7);

                            foreach (var point in result.TrendData)
                            {
                                if (point.Quality?.ToLower() != "good" || !point.TimestampParsed.HasValue)
                                {
                                    continue;
                                }

                                DateTime localTime = point.TimestampParsed.Value.ToLocalTime();

                                if (localTime <= meterStartTime)
                                {
                                    continue;
                                }

                                await writer.StartRowAsync(stoppingToken);
                                await writer.WriteAsync(meter.MeterId, NpgsqlDbType.Integer, stoppingToken);
                                await writer.WriteAsync(localTime, NpgsqlDbType.Timestamp, stoppingToken);
                                await writer.WriteAsync(Convert.ToDecimal(point.Value), NpgsqlDbType.Numeric, stoppingToken);
                                await writer.WriteAsync(192, NpgsqlDbType.Integer, stoppingToken);
                                await writer.WriteAsync(companyId, NpgsqlDbType.Integer, stoppingToken);
                            }
                        }
                        await writer.CompleteAsync(stoppingToken);
                    }

                    var insertCmd = new NpgsqlCommand(@"
                        INSERT INTO ""MeterReadings"" (""MeterId"", ""Timestamp"", ""Value"", ""Quality"", ""CompanyId"")
                        SELECT ""MeterId"", ""Timestamp"", ""Value"", ""Quality"", ""CompanyId""
                        FROM ""TempMeterReadings""
                        ON CONFLICT (""MeterId"", ""Timestamp"") DO NOTHING", connection, transaction);

                    await insertCmd.ExecuteNonQueryAsync();
                });
            }
        }

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
            catch
            {
            }
            return ids;
        }

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

        private async Task<Dictionary<int, DateTime>> GetLastTimestampsAsync(NpgsqlConnection conn, NpgsqlTransaction tr)
        {
            var dict = new Dictionary<int, DateTime>();
            using var cmd = new NpgsqlCommand("SELECT \"MeterId\", MAX(\"Timestamp\") FROM \"MeterReadings\" GROUP BY \"MeterId\"", conn, tr);
            using var reader = await cmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                dict[reader.GetInt32(0)] = reader.GetDateTime(1);
            }
            return dict;
        }

        private async Task<PCVueWebServiceSettings?> GetApiSettingsAsync(DatabaseService dbService, int companyId)
        {
            try
            {
                return await dbService.ExecuteWithCompanyIsolationAsync(companyId, async (conn, tr) =>
                {
                    string sql = @"SELECT ""ConnectionId"", ""ConnectionName"", ""BaseUrl"", ""ClientId"", ""ClientSecret"",
                                          ""ApiKey"", ""Username"", ""Password"", ""AuthType"", ""TimeoutSeconds"",
                                          ""ProjectName"", ""IsDefault""
                                   FROM ""WebServiceConnections""
                                   LIMIT 1";

                    using var cmd = new NpgsqlCommand(sql, conn, tr);
                    using var reader = await cmd.ExecuteReaderAsync();

                    if (await reader.ReadAsync())
                    {
                        return new PCVueWebServiceSettings
                        {
                            ConnectionId = reader.IsDBNull(0) ? "" : reader.GetString(0),
                            ConnectionName = reader.IsDBNull(1) ? "" : reader.GetString(1),
                            BaseUrl = reader.IsDBNull(2) ? "" : reader.GetString(2),
                            ClientId = reader.IsDBNull(3) ? "" : reader.GetString(3),
                            ClientSecret = reader.IsDBNull(4) ? "" : _encryptionService.Decrypt(reader.GetString(4)),
                            ApiKey = reader.IsDBNull(5) ? "" : reader.GetString(5),
                            Username = reader.IsDBNull(6) ? "" : reader.GetString(6),
                            Password = reader.IsDBNull(7) ? "" : _encryptionService.Decrypt(reader.GetString(7)),
                            AuthType = (AuthenticationType)reader.GetInt32(8),
                            TimeoutSeconds = reader.GetInt32(9),
                            ProjectName = reader.IsDBNull(10) ? "" : reader.GetString(10),
                            IsDefault = reader.GetBoolean(11)
                        };
                    }
                    return null;
                });
            }
            catch
            {
                return null;
            }
        }
    }
}