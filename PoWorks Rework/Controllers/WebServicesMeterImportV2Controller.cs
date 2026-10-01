using System.Collections.Concurrent;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using NpgsqlTypes;
using PoWorks_Rework.Models;
using PoWorks_Rework.Services;

namespace PoWorks_Rework.Controllers;

/// <summary>
/// Reliable Web Services meter import workflow.
/// Existing meters are reused and their non-empty imported unit can be updated without
/// deleting readings, tenant assignments, billing links, or any other meter metadata.
/// </summary>
[Authorize(Policy = "ImportExportAccess")]
public class WebServicesMeterImportV2Controller : Controller
{
    private static readonly ConcurrentDictionary<Guid, HistoricalImportProgress> HistoricalJobs = new();
    private readonly DatabaseService _databaseService;
    private readonly ICompanyContext _companyContext;
    private readonly EncryptionService _encryptionService;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<WebServicesMeterImportV2Controller> _logger;

    public WebServicesMeterImportV2Controller(
        DatabaseService databaseService,
        ICompanyContext companyContext,
        EncryptionService encryptionService,
        IServiceScopeFactory scopeFactory,
        ILogger<WebServicesMeterImportV2Controller> logger)
    {
        _databaseService = databaseService;
        _companyContext = companyContext;
        _encryptionService = encryptionService;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    [HttpPost("/Import/UpsertWebServiceVariablesWithTrends")]
    public async Task<IActionResult> UpsertWebServiceVariablesWithTrends(
        [FromBody] WebServiceUpsertRequest request)
    {
        if (request?.Variables == null || request.Variables.Count == 0)
            return Json(new { success = false, error = "No variables provided for import." });

        if (!_databaseService.IsInitialized)
            return Json(new { success = false, error = "Database connection not initialized." });

        var companyId = _companyContext.CurrentCompanyId;
        PCVueWebServiceSettings? trendsSettings = null;
        if (request.ImportTrendsData)
        {
            if (string.IsNullOrWhiteSpace(request.ConnectionId) ||
                !request.TrendsStartDate.HasValue || !request.TrendsEndDate.HasValue ||
                request.TrendsStartDate.Value >= request.TrendsEndDate.Value)
            {
                return Json(new { success = false, error = "Select a PcVue Web Service connection and a valid historical date range." });
            }

            trendsSettings = await GetWebServiceConnectionByIdAsync(request.ConnectionId, companyId);
            if (trendsSettings == null)
                return Json(new { success = false, error = "The selected PcVue Web Service connection is unavailable." });
        }

        var candidates = request.Variables
            .Where(v => v != null && !string.IsNullOrWhiteSpace(v.VariableName))
            .GroupBy(v => v.VariableName.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();

        var filteredSystemCount = 0;
        if (!request.IncludeSystemVariables)
        {
            filteredSystemCount = candidates.Count(v =>
                WebServiceImportPolicy.IsSystemVariable(v.VariableName));
            candidates = candidates
                .Where(v => !WebServiceImportPolicy.IsSystemVariable(v.VariableName))
                .ToList();
        }

        if (candidates.Count == 0)
        {
            return Json(new
            {
                success = false,
                error = filteredSystemCount > 0
                    ? "All selected variables were System variables while 'Include System Variables' was disabled."
                    : "No valid variables provided for import."
            });
        }

        WebServiceMeterBulkUpsertResult upsert;
        await using (var connection = new NpgsqlConnection(_databaseService.GetConnectionString()))
        {
            await connection.OpenAsync(HttpContext.RequestAborted);
            await using var transaction = await connection.BeginTransactionAsync(HttpContext.RequestAborted);

            try
            {
                upsert = await WebServiceMeterBulkUpsertService.UpsertAsync(
                    connection,
                    transaction,
                    companyId,
                    candidates,
                    HttpContext.RequestAborted);
                await transaction.CommitAsync(HttpContext.RequestAborted);
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync(CancellationToken.None);
                _logger.LogError(
                    ex,
                    "Bulk Web Service meter import failed for company {CompanyId}.",
                    companyId);
                return Json(new
                {
                    success = false,
                    error = "Web Service meter import failed. No partial metadata changes were committed."
                });
            }
        }

        var meterIds = upsert.MeterIds;
        var errorCount = upsert.Errors.Count;

        var processTrends = trendsSettings != null && meterIds.Count > 0;
        Guid? historyJobId = null;

        if (processTrends)
        {
            historyJobId = QueueTrendImport(
                meterIds,
                request.TrendsStartDate!.Value,
                request.TrendsEndDate!.Value,
                trendsSettings!,
                companyId);
        }

        return Json(new
        {
            success = true,
            importedCount = upsert.CreatedCount,
            updatedCount = upsert.UpdatedCount,
            unchangedCount = upsert.UnchangedCount,
            skippedCount = filteredSystemCount,
            filteredSystemCount,
            errorCount,
            detailedErrors = upsert.Errors,
            trendsQueued = processTrends,
            historyJobId,
            message = processTrends
                ? "Meters saved. Historical trends are being imported in the background."
                : "Meters saved."
        });
    }

    [HttpGet("/Import/HistoricalTrendJobStatus/{jobId:guid}")]
    public IActionResult HistoricalTrendJobStatus(Guid jobId)
    {
        if (!HistoricalJobs.TryGetValue(jobId, out var job) || job.CompanyId != _companyContext.CurrentCompanyId)
            return NotFound();

        return Json(job.Snapshot());
    }

    private async Task<PCVueWebServiceSettings?> GetWebServiceConnectionByIdAsync(
        string connectionId,
        int companyId)
    {
        return await _databaseService.ExecuteWithCompanyIsolationAsync(
            companyId,
            async (connection, transaction) =>
            {
                await using var command = new NpgsqlCommand(
                    """
                    SELECT "ConnectionId", "ConnectionName", "BaseUrl", "ClientId", "ClientSecret",
                           "ApiKey", "Username", "Password", "AuthType", "TimeoutSeconds",
                           "ProjectName", "IsDefault"
                    FROM "WebServiceConnections"
                    WHERE "ConnectionId" = @connectionId
                      AND "CompanyId" = @companyId
                      AND "IsActive" = TRUE
                    LIMIT 1
                    """,
                    connection,
                    transaction);
                command.Parameters.AddWithValue("connectionId", connectionId);
                command.Parameters.AddWithValue("companyId", companyId);

                await using var reader = await command.ExecuteReaderAsync();
                if (!await reader.ReadAsync()) return null;

                return new PCVueWebServiceSettings
                {
                    ConnectionId = reader.IsDBNull(0) ? string.Empty : reader.GetString(0),
                    ConnectionName = reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
                    BaseUrl = reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                    ClientId = reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
                    ClientSecret = reader.IsDBNull(4)
                        ? string.Empty
                        : _encryptionService.Decrypt(reader.GetString(4)),
                    ApiKey = reader.IsDBNull(5) ? string.Empty : reader.GetString(5),
                    Username = reader.IsDBNull(6) ? string.Empty : reader.GetString(6),
                    Password = reader.IsDBNull(7)
                        ? string.Empty
                        : _encryptionService.Decrypt(reader.GetString(7)),
                    AuthType = reader.IsDBNull(8)
                        ? AuthenticationType.OAuth
                        : (AuthenticationType)Convert.ToInt32(reader.GetValue(8)),
                    TimeoutSeconds = reader.IsDBNull(9) ? 30 : Convert.ToInt32(reader.GetValue(9)),
                    ProjectName = reader.IsDBNull(10) ? string.Empty : reader.GetString(10),
                    IsDefault = !reader.IsDBNull(11) && reader.GetBoolean(11)
                };
            });
    }

    private Guid QueueTrendImport(
        IReadOnlyDictionary<string, int> meterIds,
        DateTime startDate,
        DateTime endDate,
        PCVueWebServiceSettings settings,
        int companyId)
    {
        foreach (var oldJob in HistoricalJobs.Where(pair => pair.Value.CreatedUtc < DateTime.UtcNow.AddDays(-1)))
            HistoricalJobs.TryRemove(oldJob.Key, out _);

        var variableNames = meterIds.Keys.ToList();
        var meterMap = meterIds.ToDictionary(
            pair => pair.Key,
            pair => pair.Value,
            StringComparer.OrdinalIgnoreCase);
        var jobId = Guid.NewGuid();
        var progress = new HistoricalImportProgress(companyId, variableNames.Count);
        HistoricalJobs[jobId] = progress;

        _ = Task.Run(async () =>
        {
            await ImportLock.Gate.WaitAsync();
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var trendsService = scope.ServiceProvider.GetRequiredService<TrendsService>();
                var databaseService = scope.ServiceProvider.GetRequiredService<DatabaseService>();
                var logger = scope.ServiceProvider.GetRequiredService<ILogger<WebServicesMeterImportV2Controller>>();

                logger.LogInformation(
                    "Web Service trends import started for {Count} meter(s).",
                    variableNames.Count);

                foreach (var batch in variableNames.Chunk(25))
                {
                    try
                    {
                        var results = await trendsService.ProcessVariablesTrendsAsync(
                            batch.ToList(),
                            startDate.ToUniversalTime(),
                            endDate.ToUniversalTime(),
                            settings,
                            $"Manual company {companyId}");
                        var failed = results.Count(result => !result.Success);
                        var withData = results.Count(result => result.Success && result.TrendData?.Count > 0);
                        var received = results.Where(result => result.Success).Sum(result => result.TrendData?.Count ?? 0);

                        await using var connection = new NpgsqlConnection(databaseService.GetConnectionString());
                        await connection.OpenAsync();
                        await using var transaction = await connection.BeginTransactionAsync();
                        try
                        {
                            await using (var temp = new NpgsqlCommand(
                                """
                                CREATE TEMP TABLE "TempMeterReadingsManualV2"
                                (LIKE "MeterReadings" EXCLUDING CONSTRAINTS) ON COMMIT DROP;
                                ALTER TABLE "TempMeterReadingsManualV2" DROP COLUMN "ReadingId";
                                """,
                                connection,
                                transaction))
                            {
                                await temp.ExecuteNonQueryAsync();
                            }

                            await using (var writer = await connection.BeginBinaryImportAsync(
                                """
                                COPY "TempMeterReadingsManualV2"
                                    ("MeterId", "Timestamp", "Value", "Quality", "CompanyId")
                                FROM STDIN (FORMAT BINARY)
                                """))
                            {
                                foreach (var result in results)
                                {
                                    if (!result.Success ||
                                        result.TrendData == null ||
                                        !meterMap.TryGetValue(result.VariableName, out var meterId))
                                        continue;

                                    foreach (var point in result.TrendData)
                                    {
                                        if (!PcVueTimestamp.TryToLocalDatabaseTime(
                                                point.Timestamp, TimeZoneInfo.Local, out var localTimestamp) ||
                                            !double.IsFinite(point.Value) ||
                                            point.Value >= (double)decimal.MaxValue ||
                                            point.Value <= (double)decimal.MinValue)
                                            continue;

                                        await writer.StartRowAsync();
                                        await writer.WriteAsync(meterId, NpgsqlDbType.Integer);
                                        await writer.WriteAsync(localTimestamp, NpgsqlDbType.Timestamp);
                                        await writer.WriteAsync(Convert.ToDecimal(point.Value), NpgsqlDbType.Numeric);
                                        await writer.WriteAsync(point.IsGoodQuality ? 192 : 0, NpgsqlDbType.Integer);
                                        await writer.WriteAsync(companyId, NpgsqlDbType.Integer);
                                    }
                                }
                                await writer.CompleteAsync();
                            }

                            await using var insert = new NpgsqlCommand(
                                """
                                INSERT INTO "MeterReadings"
                                    ("MeterId", "Timestamp", "Value", "Quality", "CompanyId")
                                SELECT "MeterId", "Timestamp", "Value", "Quality", "CompanyId"
                                FROM "TempMeterReadingsManualV2"
                                ON CONFLICT ("MeterId", "Timestamp") DO NOTHING
                                """,
                                connection,
                                transaction);
                            insert.CommandTimeout = 300;
                            var inserted = await insert.ExecuteNonQueryAsync();
                            await transaction.CommitAsync();
                            progress.Report(batch.Length, withData, failed, received, inserted);
                            logger.LogInformation(
                                "Web Service trends import: {Inserted} new readings; {Processed}/{Total} meters processed for company {CompanyId}.",
                                inserted, progress.ProcessedVariables, variableNames.Count, companyId);
                        }
                        catch
                        {
                            await transaction.RollbackAsync();
                            throw;
                        }
                    }
                    catch (Exception ex)
                    {
                        progress.Report(batch.Length, 0, batch.Length, 0, 0);
                        logger.LogError(ex, "Web Service trends batch failed for company {CompanyId}.", companyId);
                    }
                }
                logger.LogInformation("Web Service trends import completed for company {CompanyId}: {Inserted} new readings.",
                    companyId, progress.InsertedReadings);
            }
            catch (Exception ex)
            {
                progress.Fail();
                _logger.LogError(ex, "Web Service trends background import failed for company {CompanyId}.", companyId);
            }
            finally
            {
                progress.Complete();
                ImportLock.Gate.Release();
            }
        });
        return jobId;
    }

    private sealed class HistoricalImportProgress(int companyId, int totalVariables)
    {
        private readonly object _gate = new();
        private int _processedVariables;
        private int _variablesWithData;
        private int _failedVariables;
        private int _pointsReturned;
        private int _insertedReadings;
        private bool _complete;
        private bool _failed;

        public int CompanyId { get; } = companyId;
        public DateTime CreatedUtc { get; } = DateTime.UtcNow;
        public int ProcessedVariables { get { lock (_gate) return _processedVariables; } }
        public int InsertedReadings { get { lock (_gate) return _insertedReadings; } }

        public void Report(int processed, int withData, int failed, int received, int inserted)
        {
            lock (_gate)
            {
                _processedVariables += processed;
                _variablesWithData += withData;
                _failedVariables += failed;
                _pointsReturned += received;
                _insertedReadings += inserted;
            }
        }

        public void Fail() { lock (_gate) _failed = true; }
        public void Complete() { lock (_gate) _complete = true; }

        public object Snapshot()
        {
            lock (_gate)
                return new
                {
                    totalVariables,
                    processedVariables = _processedVariables,
                    variablesWithData = _variablesWithData,
                    failedVariables = _failedVariables,
                    pointsReturned = _pointsReturned,
                    insertedReadings = _insertedReadings,
                    complete = _complete,
                    failed = _failed
                };
        }
    }
}

public sealed class WebServiceUpsertRequest
{
    public List<WebServiceVariableWithTrends> Variables { get; set; } = new();
    public bool IncludeSystemVariables { get; set; }
    public bool ImportTrendsData { get; set; } = true;
    public DateTime? TrendsStartDate { get; set; }
    public DateTime? TrendsEndDate { get; set; }
    public string ConnectionId { get; set; } = string.Empty;
}
