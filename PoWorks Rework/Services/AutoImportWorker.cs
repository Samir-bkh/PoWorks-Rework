using Npgsql;
using PoWorks_Rework.Models;

namespace PoWorks_Rework.Services
{
    public class AutoImportWorker : BackgroundService
    {
        private readonly ILogger<AutoImportWorker> _logger;
        private readonly IServiceProvider _serviceProvider;
        private readonly int _cycleDelayMinutes = 1;

        public AutoImportWorker(ILogger<AutoImportWorker> logger, IServiceProvider serviceProvider)
        {
            _logger = logger;
            _serviceProvider = serviceProvider;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("🚀 DÉMARRAGE DU PILOTE AUTOMATIQUE - Cycle: {Delay} minutes", _cycleDelayMinutes);

            while (!stoppingToken.IsCancellationRequested)
            {
                try { await RunImportCycleAsync(stoppingToken); }
                catch (Exception ex) { _logger.LogError(ex, "❌ ERREUR CRITIQUE !"); }
                await Task.Delay(TimeSpan.FromMinutes(_cycleDelayMinutes), stoppingToken);
            }
        }

        private async Task RunImportCycleAsync(CancellationToken stoppingToken)
        {
            using var scope = _serviceProvider.CreateScope();
            var dbService = scope.ServiceProvider.GetRequiredService<DatabaseService>();
            var pcVueWebService = scope.ServiceProvider.GetRequiredService<PCVueWebService>();
            var trendsService = scope.ServiceProvider.GetRequiredService<TrendsService>();

            var companyIds = await GetAllCompanyIdsAsync(dbService);

            foreach (var companyId in companyIds)
            {
                _logger.LogInformation("🏢 --- IMPORTATION COMPANY : {Id} ---", companyId);

                // On charge la config unique globale pour le moment (réutilisant GetApiSettingsAsync)
                var apiSettings = await GetApiSettingsAsync(dbService);
                if (apiSettings == null) continue;

                await dbService.ExecuteWithCompanyIsolationAsync(companyId, async (connection, transaction) =>
                {
                    var metersToImport = await GetMetersForCurrentCompanyAsync(connection, transaction);
                    foreach (var meter in metersToImport)
                    {
                        if (stoppingToken.IsCancellationRequested) break;

                        DateTime? lastPoint = await GetLastTimestampForMeterAsync(connection, transaction, meter.MeterId);
                        DateTime startTime = lastPoint ?? DateTime.Now.AddDays(-7);
                        DateTime endTime = DateTime.Now;

                        if (startTime >= endTime) continue;

                        try
                        {
                            var trendResults = await trendsService.ProcessVariablesTrendsAsync(
                                new List<string> { meter.OriginalVariableName }, startTime.ToUniversalTime(), endTime.ToUniversalTime(), apiSettings);

                            var resultForThisMeter = trendResults.FirstOrDefault();
                            if (resultForThisMeter?.TrendData == null) continue;

                            foreach (var point in resultForThisMeter.TrendData)
                            {
                                if (point.Quality?.ToLower() != "good" || !point.TimestampParsed.HasValue) continue;

                                DateTime localTime = point.TimestampParsed.Value.ToLocalTime();
                                if (localTime <= startTime) continue;

                                var insertCmd = new NpgsqlCommand(@"
                                    INSERT INTO ""MeterReadings"" (""MeterId"", ""Timestamp"", ""Value"", ""Quality"", ""CompanyId"")
                                    VALUES (@meterId, @timestamp, @value, @quality, @companyId)
                                    ON CONFLICT DO NOTHING", connection, transaction);

                                insertCmd.Parameters.AddWithValue("@meterId", meter.MeterId);
                                insertCmd.Parameters.AddWithValue("@timestamp", localTime);
                                insertCmd.Parameters.AddWithValue("@value", point.Value);
                                insertCmd.Parameters.AddWithValue("@quality", 192);
                                insertCmd.Parameters.AddWithValue("@companyId", companyId);
                                await insertCmd.ExecuteNonQueryAsync();
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Erreur sur {Name}", meter.Name);
                            pcVueWebService.ClearTokens();
                        }
                    }
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
                while (await reader.ReadAsync()) ids.Add(reader.GetInt32(0));
            }
            catch { }
            return ids;
        }

        private async Task<List<MeterForTrendsAnalysis>> GetMetersForCurrentCompanyAsync(NpgsqlConnection conn, NpgsqlTransaction tr)
        {
            var meters = new List<MeterForTrendsAnalysis>();
            using var cmd = new NpgsqlCommand("SELECT \"MeterId\", \"Name\", \"Active\" FROM \"Meters\" WHERE (\"Name\" LIKE '%.%' OR \"Name\" LIKE 'varsets.%') AND \"Active\" = true", conn, tr);
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync()) meters.Add(new MeterForTrendsAnalysis { MeterId = reader.GetInt32(0), Name = reader.GetString(1), OriginalVariableName = reader.GetString(1) });
            return meters;
        }

        private async Task<DateTime?> GetLastTimestampForMeterAsync(NpgsqlConnection conn, NpgsqlTransaction tr, int id)
        {
            using var cmd = new NpgsqlCommand("SELECT MAX(\"Timestamp\") FROM \"MeterReadings\" WHERE \"MeterId\" = @id", conn, tr);
            cmd.Parameters.AddWithValue("@id", id);
            var res = await cmd.ExecuteScalarAsync();
            return (res != DBNull.Value) ? (DateTime?)Convert.ToDateTime(res) : null;
        }

        private async Task<PCVueWebServiceSettings?> GetApiSettingsAsync(DatabaseService dbService)
        {
            try
            {
                using var conn = dbService.CreateNewConnection();
                await conn.OpenAsync();
                using var cmd = new NpgsqlCommand("SELECT \"SettingValue\" FROM \"Settings\" WHERE \"SettingKey\" = 'PCVueWebServiceSettings'", conn);
                var res = await cmd.ExecuteScalarAsync();
                return (res != null) ? System.Text.Json.JsonSerializer.Deserialize<PCVueWebServiceSettings>(res.ToString()!) : null;
            }
            catch { return null; }
        }
    }
}