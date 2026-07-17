using Microsoft.Extensions.Hosting;
using Npgsql;
using PoWorks_Rework.Models;
using PoWorks_Rework.Repositories;

namespace PoWorks_Rework.Services
{
    public class AutoImportWorker : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<AutoImportWorker> _logger;
        // On reste à 1 minute pour tes tests !
        private readonly int _pollingIntervalMinutes = 1;

        public AutoImportWorker(IServiceScopeFactory scopeFactory, ILogger<AutoImportWorker> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("🚀 DÉMARRAGE DU PILOTE AUTOMATIQUE (AutoImportWorker) - Cycle: {Minutes} minutes", _pollingIntervalMinutes);
            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogInformation("🔄 Lancement du cycle d'importation automatique à {time}", DateTimeOffset.Now);
                try
                {
                    await RunImportCycleAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "❌ Erreur critique dans le cycle d'importation automatique");
                }
                await Task.Delay(TimeSpan.FromMinutes(_pollingIntervalMinutes), stoppingToken);
            }
        }

        private async Task RunImportCycleAsync()
        {
            using var scope = _scopeFactory.CreateScope();
            var meterRepo = scope.ServiceProvider.GetRequiredService<MeterRepository>();
            var trendsService = scope.ServiceProvider.GetRequiredService<TrendsService>();
            var dbService = scope.ServiceProvider.GetRequiredService<DatabaseService>();
            var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();

            if (!dbService.IsInitialized) return;

            var meters = await meterRepo.GetWebServiceImportedMetersAsync(activeOnly: true);
            if (!meters.Any()) return;

            var connectionSection = config.GetSection("WebServiceConnections").GetChildren().FirstOrDefault(c => c["IsDefault"] == "true")
                                 ?? config.GetSection("WebServiceConnections").GetChildren().FirstOrDefault();

            if (connectionSection == null) return;

            var settings = new PCVueWebServiceSettings
            {
                ConnectionId = connectionSection["ConnectionId"] ?? "",
                ConnectionName = connectionSection["ConnectionName"] ?? "",
                BaseUrl = connectionSection["BaseUrl"] ?? "",
                ClientId = connectionSection["ClientId"] ?? "",
                ClientSecret = connectionSection["ClientSecret"] ?? "",
                Username = connectionSection["Username"] ?? "",
                Password = connectionSection["Password"] ?? ""
            };

            int pointsInsertsTotal = 0;

            // LA CORRECTION EST LÀ : ON DEMANDE L'HEURE UTC POUR PCVUE !
            var endDateUtc = DateTime.UtcNow;

            using var connection = new NpgsqlConnection(dbService.GetConnectionString());
            await connection.OpenAsync();

            foreach (var meter in meters)
            {
                _logger.LogWarning("🔍 --- ANALYSE DU COMPTEUR : {MeterName} ---", meter.Name);

                // On lit l'heure malaisienne dans ta base
                var lastTimestampLocal = await meterRepo.GetLastReadingTimestampAsync(meter.MeterId);

                DateTime startDateUtc;
                if (lastTimestampLocal.HasValue)
                {
                    // On convertit l'heure malaisienne en Heure Universelle (UTC) pour PcVue
                    var localTime = DateTime.SpecifyKind(lastTimestampLocal.Value, DateTimeKind.Local);
                    startDateUtc = localTime.ToUniversalTime();
                }
                else
                {
                    startDateUtc = DateTime.UtcNow.AddHours(-24);
                }

                _logger.LogInformation("🕰️ Dernier point en base (Local) : {Last}", lastTimestampLocal?.ToString("yyyy-MM-dd HH:mm:ss") ?? "Aucun");
                _logger.LogInformation("📡 Requête API (En UTC !!!) -> Start={Start} | End={End}", startDateUtc.ToString("yyyy-MM-dd HH:mm:ss"), endDateUtc.ToString("yyyy-MM-dd HH:mm:ss"));

                if (startDateUtc >= endDateUtc)
                {
                    _logger.LogInformation("⏩ Ignoré : on a déjà les données jusqu'à maintenant.");
                    continue;
                }

                var variableNames = new List<string> { meter.OriginalVariableName };
                var trendsResults = await trendsService.ProcessVariablesTrendsAsync(variableNames, startDateUtc, endDateUtc, settings);
                var result = trendsResults.FirstOrDefault();

                if (result != null && result.Success && result.TrendData != null)
                {
                    _logger.LogWarning("📥 PCVue a renvoyé {NbPoints} points.", result.TrendData.Count);

                    using var tx = await connection.BeginTransactionAsync();
                    try
                    {
                        int pointsInsertsPourCompteur = 0;
                        foreach (var point in result.TrendData)
                        {
                            _logger.LogInformation("   -> Point reçu brut : Heure={Time} | Valeur={Val} | Qualité={Qual}", point.Timestamp, point.Value, point.Quality);

                            if (point.TimestampParsed.HasValue)
                            {
                                // DOUBLE SÉCURITÉ : UNIQUEMENT LES BONS POINTS
                                if (point.IsGoodQuality)
                                {
                                    var insertCmd = new NpgsqlCommand(@"
                                        INSERT INTO ""MeterReadings"" (""MeterId"", ""Timestamp"", ""Value"", ""Quality"")
                                        VALUES (@meterId, @timestamp, @value, @quality)
                                        ON CONFLICT (""MeterId"", ""Timestamp"") DO NOTHING", connection, tx);

                                    // On prend l'heure UTC renvoyée par PcVue, et on la repasse en heure Malaisienne pour ta BDD !
                                    DateTime apiUtcTime = DateTime.SpecifyKind(point.TimestampParsed.Value, DateTimeKind.Utc);
                                    DateTime heureLocalePourDashboard = apiUtcTime.ToLocalTime();

                                    insertCmd.Parameters.AddWithValue("@meterId", meter.MeterId);
                                    insertCmd.Parameters.AddWithValue("@timestamp", heureLocalePourDashboard);
                                    insertCmd.Parameters.AddWithValue("@value", point.Value);
                                    insertCmd.Parameters.AddWithValue("@quality", 192);

                                    int inserted = await insertCmd.ExecuteNonQueryAsync();
                                    pointsInsertsPourCompteur += inserted;
                                    pointsInsertsTotal += inserted;

                                    if (inserted > 0)
                                        _logger.LogInformation("      ✅ Point inséré à l'heure locale : {LocalTime}", heureLocalePourDashboard);
                                    else
                                        _logger.LogInformation("      ⚠️ Point ignoré (Déjà existant en base).");
                                }
                                else
                                {
                                    _logger.LogWarning("      ❌ Point ignoré (Mauvaise qualité ou point de bordure artificiel)");
                                }
                            }
                        }
                        await tx.CommitAsync();
                        _logger.LogInformation("✔ Bilan pour {MeterName} : {Points} nouveaux points ajoutés.", meter.Name, pointsInsertsPourCompteur);
                    }
                    catch (Exception ex)
                    {
                        await tx.RollbackAsync();
                        _logger.LogError(ex, "Erreur lors de l'insertion en base.");
                    }
                }
                else
                {
                    _logger.LogWarning("❌ PCVue n'a retourné aucun point ou la requête a échoué.");
                }
            }

            _logger.LogInformation("✅ Cycle terminé. Total des nouveaux points : {Total}", pointsInsertsTotal);
        }
    }
}