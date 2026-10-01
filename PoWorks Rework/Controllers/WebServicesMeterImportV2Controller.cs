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

    /// <summary>
    /// Read-only check against the selected PcVue server. This distinguishes an
    /// empty archive/date range from an import or dashboard problem without
    /// creating meters or modifying stored readings.
    /// </summary>
    [HttpPost("/Import/ProbePcVueHistory")]
    public async Task<IActionResult> ProbePcVueHistory([FromBody] PcVueHistoryProbeRequest request)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.ConnectionId) ||
            string.IsNullOrWhiteSpace(request.VariableName) ||
            request.StartDate >= request.EndDate ||
            request.EndDate - request.StartDate > TimeSpan.FromDays(7))
            return BadRequest(new { success = false, error = "Select one variable and a date range of up to 7 days." });

        if (!_databaseService.IsInitialized)
            return BadRequest(new { success = false, error = "Database connection not initialized." });

        var settings = await GetWebServiceConnectionByIdAsync(request.ConnectionId, _companyContext.CurrentCompanyId);
        if (settings == null)
            return BadRequest(new { success = false, error = "The selected PcVue connection is unavailable." });

        using var scope = _scopeFactory.CreateScope();
        var trends = scope.ServiceProvider.GetRequiredService<TrendsService>();
        var result = (await trends.ProcessVariablesTrendsAsync(
            new List<string> { request.VariableName.Trim() },
            request.StartDate.ToUniversalTime(), request.EndDate.ToUniversalTime(),
            settings, "Historical availability check", HttpContext.RequestAborted)).Single();

        if (!result.Success)
            return Json(new { success = false, error = result.ErrorMessage, variableName = result.VariableName });

        var timestamps = (result.TrendData ?? new List<TrendDataPoint>())
            .Select(point => PcVueTimestamp.TryParseUtc(point.Timestamp, out var utc) ? (DateTime?)utc : null)
            .Where(timestamp => timestamp.HasValue)
            .Select(timestamp => timestamp!.Value)
            .ToList();
        return Json(new
        {
            success = true,
            variableName = result.VariableName,
            pointCount = result.TrendData?.Count ?? 0,
            firstUtc = timestamps.Count > 0 ? timestamps.Min() : (DateTime?)null,
            lastUtc = timestamps.Count > 0 ? timestamps.Max() : (DateTime?)null
        });
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
        var windows = TrendsService.PlanHistoricalWindows(
            startDate.ToUniversalTime(), endDate.ToUniversalTime());
        var jobId = Guid.NewGuid();
        var progress = new HistoricalImportProgress(companyId, variableNames.Count,
            checked(variableNames.Count * windows.Count));
        HistoricalJobs[jobId] = progress;

        _ = Task.Run(async () =>
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var trendsService = scope.ServiceProvider.GetRequiredService<TrendsService>();
                var databaseService = scope.ServiceProvider.GetRequiredService<DatabaseService>();
                var logger = scope.ServiceProvider.GetRequiredService<ILogger<WebServicesMeterImportV2Controller>>();

                logger.LogInformation(
                    "Web Service trends import started for {Count} meter(s) in {Windows} windows each.",
                    variableNames.Count, windows.Count);

                // At most five PcVue trend requests are active. Each short
                // window is saved before fetching the next one, so an archive
                // of many years does not have to fit in memory.
                foreach (var batch in variableNames.Chunk(5))
                {
                    await Task.WhenAll(batch.Select(variableName =>
                        ImportHistoricalVariableAsync(variableName, meterMap[variableName],
                            windows, settings, companyId, trendsService, databaseService,
                            progress, logger)));
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
            }
        });
        return jobId;
    }

    private static async Task ImportHistoricalVariableAsync(
        string variableName,
        int meterId,
        IReadOnlyList<HistoricalTrendWindow> windows,
        PCVueWebServiceSettings settings,
        int companyId,
        TrendsService trendsService,
        DatabaseService databaseService,
        HistoricalImportProgress progress,
        ILogger logger)
    {
        var receivedData = false;
        var failed = false;
        var attemptedWindows = 0;
        try
        {
            await trendsService.ProcessVariableTrendWindowsAsync(
                variableName, windows, settings, async (window, result) =>
                {
                    attemptedWindows++;
                    if (!result.Success)
                    {
                        failed = true;
                        var error = $"{variableName} {window.StartUtc:yyyy-MM-dd}–{window.EndUtc:yyyy-MM-dd}: " +
                                    (result.ErrorMessage ?? "PcVue historical request failed.");
                        progress.ReportWindow(window, variableName, 0, 0, error);
                        logger.LogWarning("PcVue history failed for {Variable} {Start:o}–{End:o}: {Error}",
                            variableName, window.StartUtc, window.EndUtc, result.ErrorMessage);
                        return;
                    }

                    var points = result.TrendData ?? new List<TrendDataPoint>();
                    receivedData |= points.Count > 0;
                    try
                    {
                        var inserted = points.Count > 0
                            ? await InsertHistoricalWindowAsync(databaseService, meterId, points, companyId)
                            : 0;
                        progress.ReportWindow(window, variableName, points.Count, inserted);
                        if (points.Count > 0)
                            logger.LogInformation(
                                "PcVue history {Variable} {Start:o}–{End:o}: {Received} points, {Inserted} new readings.",
                                variableName, window.StartUtc, window.EndUtc, points.Count, inserted);
                    }
                    catch (Exception ex)
                    {
                        failed = true;
                        progress.ReportWindow(window, variableName, 0, 0,
                            $"{variableName} {window.StartUtc:yyyy-MM-dd}: database write failed ({ex.GetType().Name}).");
                        throw;
                    }
                });
        }
        catch (Exception ex)
        {
            failed = true;
            progress.AddError($"{variableName}: {ex.GetType().Name}: {ex.Message}");
            logger.LogError(ex, "PcVue historical import failed for {Variable} and company {CompanyId}.",
                variableName, companyId);
        }
        finally
        {
            progress.CompleteVariable(receivedData, failed, windows.Count - attemptedWindows);
        }
    }

    private static async Task<int> InsertHistoricalWindowAsync(
        DatabaseService databaseService,
        int meterId,
        IReadOnlyList<TrendDataPoint> points,
        int companyId)
    {
        // Keep the gate only for the database write. PcVue requests and the
        // automatic import continue while a historical window is being read.
        await ImportLock.Gate.WaitAsync();
        try
        {
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
                    """, connection, transaction))
                    await temp.ExecuteNonQueryAsync();

                await using (var writer = await connection.BeginBinaryImportAsync(
                    """
                    COPY "TempMeterReadingsManualV2"
                        ("MeterId", "Timestamp", "Value", "Quality", "CompanyId")
                    FROM STDIN (FORMAT BINARY)
                    """))
                {
                    foreach (var point in points)
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
                    await writer.CompleteAsync();
                }

                await using var insert = new NpgsqlCommand(
                    """
                    INSERT INTO "MeterReadings"
                        ("MeterId", "Timestamp", "Value", "Quality", "CompanyId")
                    SELECT "MeterId", "Timestamp", "Value", "Quality", "CompanyId"
                    FROM "TempMeterReadingsManualV2"
                    ON CONFLICT ("MeterId", "Timestamp") DO NOTHING
                    """, connection, transaction);
                insert.CommandTimeout = 300;
                var inserted = await insert.ExecuteNonQueryAsync();
                await transaction.CommitAsync();
                return inserted;
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }
        finally
        {
            ImportLock.Gate.Release();
        }
    }

    private sealed class HistoricalImportProgress(int companyId, int totalVariables, int totalWindows)
    {
        private readonly object _gate = new();
        private int _processedVariables;
        private int _variablesWithData;
        private int _failedVariables;
        private int _processedWindows;
        private int _failedWindows;
        private long _pointsReturned;
        private long _insertedReadings;
        private readonly List<string> _errors = new();
        private string? _lastVariable;
        private DateTime? _lastWindowEndUtc;
        private bool _complete;
        private bool _failed;

        public int CompanyId { get; } = companyId;
        public DateTime CreatedUtc { get; } = DateTime.UtcNow;
        public int ProcessedVariables { get { lock (_gate) return _processedVariables; } }
        public long InsertedReadings { get { lock (_gate) return _insertedReadings; } }

        public void ReportWindow(HistoricalTrendWindow window, string variableName,
            int received, int inserted, string? error = null)
        {
            lock (_gate)
            {
                _processedWindows++;
                if (error != null) _failedWindows++;
                _pointsReturned += received;
                _insertedReadings += inserted;
                _lastVariable = variableName;
                _lastWindowEndUtc = window.EndUtc;
                if (error != null) AddErrorUnsafe(error);
            }
        }

        public void AddError(string error) { lock (_gate) AddErrorUnsafe(error); }
        private void AddErrorUnsafe(string error)
        {
            if (_errors.Count < 5 && !string.IsNullOrWhiteSpace(error))
                _errors.Add(error.Length > 240 ? error[..240] + "…" : error);
        }

        public void CompleteVariable(bool withData, bool failed, int skippedWindows)
        {
            lock (_gate)
            {
                _processedVariables++;
                if (withData) _variablesWithData++;
                if (failed) _failedVariables++;
                _processedWindows += skippedWindows;
                _failedWindows += skippedWindows;
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
                    totalWindows,
                    processedVariables = _processedVariables,
                    processedWindows = _processedWindows,
                    failedWindows = _failedWindows,
                    lastVariable = _lastVariable,
                    lastWindowEndUtc = _lastWindowEndUtc,
                    variablesWithData = _variablesWithData,
                    failedVariables = _failedVariables,
                    pointsReturned = _pointsReturned,
                    insertedReadings = _insertedReadings,
                    errors = _errors.ToArray(),
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

public sealed class PcVueHistoryProbeRequest
{
    public string ConnectionId { get; set; } = string.Empty;
    public string VariableName { get; set; } = string.Empty;
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
}
