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
            _logger.LogInformation("PILOT START - Le service d'importation est lancé.");

            while (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogInformation("--- DEBUT D'UN CYCLE D'IMPORT ({Delay} min) ---", _cycleDelayMinutes);
                try
                {
                    await RunImportCycleAsync(stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError("IMPORT CYCLE FAILED | Reason: {Message} | StackTrace: {Stack}", ex.Message, ex.StackTrace);
                }

                _logger.LogInformation("--- FIN DU CYCLE, MISE EN VEILLE ---");
                await Task.Delay(TimeSpan.FromMinutes(_cycleDelayMinutes), stoppingToken);
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
                _logger.LogInformation(">> Trouvé {Count} compagnie(s) dans la base.", companyIds.Count);

                foreach (var companyId in companyIds)
                {
                    _logger.LogInformation(">> Traitement de la compagnie ID: {CompanyId}", companyId);

                    var apiSettings = await GetApiSettingsAsync(dbService, companyId);
                    if (apiSettings == null)
                    {
                        _logger.LogWarning(">> ERREUR: Aucun paramètre WebService trouvé pour la compagnie {Id}. On passe à la suivante.", companyId);
                        continue;
                    }

                    if (!apiSettings.EnableAutomaticImport)
                    {
                        _logger.LogInformation(">> Import automatique désactivé pour la compagnie {Id}. On passe à la suivante.", companyId);
                        continue;
                    }

                    var testToken = await webService.GetValidAccessTokenAsync(apiSettings);
                    if (string.IsNullOrEmpty(testToken))
                    {
                        _logger.LogWarning(">> ERREUR: Impossible de récupérer le token PCVue pour la compagnie {Id}.", companyId);
                        continue;
                    }

                    await dbService.ExecuteWithCompanyIsolationAsync(companyId, async (connection, transaction) =>
                    {
                        var metersToImport = await GetMetersForCurrentCompanyAsync(connection, transaction);
                        _logger.LogInformation(">> Trouvé {Count} compteur(s) actif(s) à importer pour la compagnie {Id}.", metersToImport.Count, companyId);

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
                            _logger.LogInformation(">> Appel PCVue pour {Count} variable(s) depuis {Time}", variableNames.Count, groupStartTime);

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

                        _logger.LogInformation(">> Import terminé : {Points} nouveaux points PCVue, {Padding} points de padding générés.", pointsAdded, paddingAdded);

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

        // 🟢 FIX 4 : Nouvelle méthode pour récupérer la Date ET la Valeur
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
                            // On utilise GetValue().ToString() au lieu de GetString() au cas où l'ID serait un chiffre/GUID
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

                            // Colonne 13 = EnableAutomaticImport
                            EnableAutomaticImport = !reader.IsDBNull(13) && reader.GetBoolean(13)
                        };
                    }

                    _logger.LogWarning(">> BDD : La requête SQL n'a retourné aucune ligne pour la compagnie {Id}.", companyId);
                    return null;
                });
            }
            catch (Exception ex)
            {
                // On logue la VRAIE erreur pour savoir ce qui coince !
                _logger.LogError(ex, ">> ERREUR CRITIQUE lors de la lecture des paramètres API pour la compagnie {CompanyId}", companyId);
                return null;
            }
        }
    }
}