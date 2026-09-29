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
        var candidates = request.Variables
            .Where(v => v != null && !string.IsNullOrWhiteSpace(v.VariableName))
            .GroupBy(v => v.VariableName.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();

        var filteredSystemCount = 0;
        if (!request.IncludeSystemVariables)
        {
            filteredSystemCount = candidates.Count(v => WebServiceImportPolicy.IsSystemVariable(v.VariableName));
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

        var meterIds = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var createdCount = 0;
        var updatedCount = 0;
        var unchangedCount = 0;
        var errorCount = 0;
        var errors = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        await using (var connection = new NpgsqlConnection(_databaseService.GetConnectionString()))
        {
            await connection.OpenAsync();
            await using var transaction = await connection.BeginTransactionAsync();

            try
            {
                foreach (var variable in candidates)
                {
                    var variableName = variable.VariableName.Trim();
                    try
                    {
                        await using var find = new NpgsqlCommand(@"
                            SELECT ""MeterId"", COALESCE(""Unit"", '')
                            FROM ""Meters""
                            WHERE ""Name"" = @name AND ""CompanyId"" = @companyId
                            LIMIT 1", connection, transaction);
                        find.Parameters.AddWithValue("name", variableName);
                        find.Parameters.AddWithValue("companyId", companyId);

                        int? existingId = null;
                        string existingUnit = string.Empty;
                        await using (var reader = await find.ExecuteReaderAsync())
                        {
                            if (await reader.ReadAsync())
                            {
                                existingId = reader.GetInt32(0);
                                existingUnit = reader.IsDBNull(1) ? string.Empty : reader.GetString(1);
                            }
                        }

                        if (existingId.HasValue)
                        {
                            meterIds[variableName] = existingId.Value;
                            var normalizedUnit = WebServiceImportPolicy.NormalizeUnit(variable.Unit);

                            // Never erase a valid PoWorks unit with an empty value from PCVue.
                            // An explicitly supplied non-empty unit is considered an operator update.
                            if (WebServiceImportPolicy.ShouldUpdateExistingUnit(existingUnit, normalizedUnit))
                            {
                                await using var update = new NpgsqlCommand(@"
                                    UPDATE ""Meters"
                                    SET ""Unit"" = @unit
                                    WHERE ""MeterId"" = @meterId AND ""CompanyId"" = @companyId", connection, transaction);
                                update.Parameters.AddWithValue("unit", normalizedUnit);
                                update.Parameters.AddWithValue("meterId", existingId.Value);
                                update.Parameters.AddWithValue("companyId", companyId);
                                await update.ExecuteNonQueryAsync();
                                updatedCount++;
                            }
                            else
                            {
                                unchangedCount++;
                            }

                            continue;
                        }

                        var parentId = await ResolveParentIdAsync(
                            connection,
                            transaction,
                            companyId,
                            variable.ParentMeterId);

                        await using var insert = new NpgsqlCommand(@"
                            INSERT INTO ""Meters"
                                (""Name"", ""Label"", ""Unit"", ""ParentId"", ""LastReading"", ""Type"", ""Active"", ""TenantID"", ""CompanyId"")
                            VALUES
                                (@name, @label, @unit, @parentId, 0, @type, @active, NULL, @companyId)
                            RETURNING ""MeterId""", connection, transaction);
                        insert.Parameters.AddWithValue("name", variableName);
                        insert.Parameters.AddWithValue("label", variableName);
                        insert.Parameters.AddWithValue("unit", WebServiceImportPolicy.NormalizeUnit(variable.Unit));
                        insert.Parameters.AddWithValue("parentId", parentId.HasValue ? parentId.Value : DBNull.Value);
                        insert.Parameters.AddWithValue("type", WebServiceImportPolicy.NormalizeMeterType(variable.Type));
                        insert.Parameters.AddWithValue("active", variable.Active);
                        insert.Parameters.AddWithValue("companyId", companyId);

                        var meterId = Convert.ToInt32(await insert.ExecuteScalarAsync());
                        meterIds[variableName] = meterId;
                        createdCount++;
                    }
                    catch (Exception ex)
                    {
                        errorCount++;
                        errors[variableName] = ex.Message;
                        _logger.LogError(ex, "Failed to import Web Service variable {VariableName}", variableName);
                    }
                }

                await transaction.CommitAsync();
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

        var processTrends = request.ImportTrendsData
                            && meterIds.Count > 0
                            && !string.IsNullOrWhiteSpace(request.ConnectionId)
                            && request.TrendsStartDate.HasValue
                            && request.TrendsEndDate.HasValue;

        if (processTrends)
        {
            var settings = await GetWebServiceConnectionByIdAsync(request.ConnectionId, companyId);
            if (settings != null)
            {
                QueueTrendImport(
                    meterIds,
                    request.TrendsStartDate!.Value,
                    request.TrendsEndDate!.Value,
                    settings,
                    companyId);
            }
            else
            {
                processTrends = false;
            }
        }

        return Json(new
        {
            success = true,
            importedCount = createdCount,
            updatedCount,
            unchangedCount,
            skippedCount = filteredSystemCount,
            filteredSystemCount,
            errorCount,
            detailedErrors = errors,
            trendsQueued = processTrends,
            message = processTrends
                ? "Meters saved. Historical trends are being imported in the background."
                : "Meters saved."
        });
    }

    private static async Task<int?> ResolveParentIdAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        int companyId,
        string? parentMeterId)
    {
        if (!int.TryParse(parentMeterId, out var parsedParentId) || parsedParentId <= 0)
            return null;

        await using var command = new NpgsqlCommand(@"
            SELECT ""MeterId""
            FROM ""Meters"
            WHERE ""MeterId"" = @meterId AND ""CompanyId"" = @companyId
            LIMIT 1", connection, transaction);
        command.Parameters.AddWithValue("meterId", parsedParentId);
        command.Parameters.AddWithValue("companyId", companyId);
        var result = await command.ExecuteScalarAsync();
        return result == null || result == DBNull.Value ? null : Convert.ToInt32(result);
    }

    private async Task<PCVueWebServiceSettings?> GetWebServiceConnectionByIdAsync(string connectionId, int companyId)
    {
        return await _databaseService.ExecuteWithCompanyIsolationAsync(companyId, async (connection, transaction) =>
        {
            await using var command = new NpgsqlCommand(@"
                SELECT ""ConnectionId"", ""ConnectionName"", ""BaseUrl"", ""ClientId"", ""ClientSecret"",
                       ""ApiKey"", ""Username"", ""Password"", ""AuthType"", ""TimeoutSeconds"",
                       ""ProjectName"", ""IsDefault""
                FROM ""WebServiceConnections""
                WHERE ""ConnectionId"" = @connectionId
                  AND ""CompanyId"" = @companyId
                  AND ""IsActive"" = TRUE
                LIMIT 1", connection, transaction);
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
                ClientSecret = reader.IsDBNull(4) ? string.Empty : _encryptionService.Decrypt(reader.GetString(4)),
                ApiKey = reader.IsDBNull(5) ? string.Empty : reader.GetString(5),
                Username = reader.IsDBNull(6) ? string.Empty : reader.GetString(6),
                Password = reader.IsDBNull(7) ? string.Empty : _encryptionService.Decrypt(reader.GetString(7)),
                AuthType = reader.IsDBNull(8) ? AuthenticationType.OAuth : (AuthenticationType)Convert.ToInt32(reader.GetValue(8)),
                TimeoutSeconds = reader.IsDBNull(9) ? 30 : Convert.ToInt32(reader.GetValue(9)),
                ProjectName = reader.IsDBNull(10) ? string.Empty : reader.GetString(10),
                IsDefault = !reader.IsDBNull(11) && reader.GetBoolean(11)
            };
        });
    }

    private void QueueTrendImport(
        IReadOnlyDictionary<string, int> meterIds,
        DateTime startDate,
        DateTime endDate,
        PCVueWebServiceSettings settings,
        int companyId)
    {
        var variableNames = meterIds.Keys.ToList();
        var meterMap = meterIds.ToDictionary(
            pair => pair.Key,
            pair => pair.Value,
            StringComparer.OrdinalIgnoreCase);

        _ = Task.Run(async () =>
        {
            using var scope = _scopeFactory.CreateScope();
            var trendsService = scope.ServiceProvider.GetRequiredService<TrendsService>();
            var databaseService = scope.ServiceProvider.GetRequiredService<DatabaseService>();
            var logger = scope.ServiceProvider.GetRequiredService<ILogger<WebServicesMeterImportV2Controller>>();

            await ImportLock.Gate.WaitAsync();
            try
            {
                logger.LogInformation("Web Service trends import started for {Count} meter(s).", variableNames.Count);
                var results = await trendsService.ProcessVariablesTrendsAsync(
                    variableNames,
                    startDate.ToUniversalTime(),
                    endDate.ToUniversalTime(),
                    settings,
                    $"Manual company {companyId}");

                await using var connection = new NpgsqlConnection(databaseService.GetConnectionString());
                await connection.OpenAsync();
                await using var transaction = await connection.BeginTransactionAsync();

                try
                {
                    await using (var temp = new NpgsqlCommand(@"
                        CREATE TEMP TABLE ""TempMeterReadingsManualV2""
                        (LIKE ""MeterReadings"" EXCLUDING CONSTRAINTS) ON COMMIT DROP;
                        ALTER TABLE ""TempMeterReadingsManualV2"" DROP COLUMN ""ReadingId"";",
                        connection,
                        transaction))
                    {
                        await temp.ExecuteNonQueryAsync();
                    }

                    await using (var writer = await connection.BeginBinaryImportAsync(@"
                        COPY ""TempMeterReadingsManualV2"
                            (""MeterId"", ""Timestamp"", ""Value"", ""Quality"", ""CompanyId"")
                        FROM STDIN (FORMAT BINARY)"))
                    {
                        foreach (var result in results)
                        {
                            if (!result.Success || result.TrendData == null || !meterMap.TryGetValue(result.VariableName, out var meterId))
                                continue;

                            foreach (var point in result.TrendData)
                            {
                                if (!point.TimestampParsed.HasValue) continue;

                                await writer.StartRowAsync();
                                await writer.WriteAsync(meterId, NpgsqlDbType.Integer);
                                await writer.WriteAsync(point.TimestampParsed.Value, NpgsqlDbType.Timestamp);
                                await writer.WriteAsync(Convert.ToDecimal(point.Value), NpgsqlDbType.Numeric);
                                await writer.WriteAsync(point.IsGoodQuality ? 192 : 0, NpgsqlDbType.Integer);
                                await writer.WriteAsync(companyId, NpgsqlDbType.Integer);
                            }
                        }

                        await writer.CompleteAsync();
                    }

                    await using var insert = new NpgsqlCommand(@"
                        INSERT INTO ""MeterReadings""
                            (""MeterId"", ""Timestamp"", ""Value"", ""Quality"", ""CompanyId"")
                        SELECT ""MeterId"", ""Timestamp"", ""Value"", ""Quality"", ""CompanyId""
                        FROM ""TempMeterReadingsManualV2"
                        ON CONFLICT (""MeterId"", ""Timestamp"") DO NOTHING",
                        connection,
                        transaction);
                    insert.CommandTimeout = 300;
                    await insert.ExecuteNonQueryAsync();
                    await transaction.CommitAsync();

                    logger.LogInformation("Web Service trends import completed for company {CompanyId}.", companyId);
                }
                catch
                {
                    await transaction.RollbackAsync();
                    throw;
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Web Service trends background import failed for company {CompanyId}.", companyId);
            }
            finally
            {
                ImportLock.Gate.Release();
            }
        });
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

/// <summary>
/// Pure policy helpers are kept separate so filtering and update rules can be unit-tested
/// without a database or a live PCVue server.
/// </summary>
public static class WebServiceImportPolicy
{
    public static bool IsSystemVariable(string? variableName)
    {
        if (string.IsNullOrWhiteSpace(variableName)) return false;

        var normalized = variableName.Trim();
        if (normalized.Equals("System", StringComparison.OrdinalIgnoreCase)) return true;

        return normalized.StartsWith("System.", StringComparison.OrdinalIgnoreCase)
               || normalized.StartsWith("System/", StringComparison.OrdinalIgnoreCase)
               || normalized.StartsWith("System\\", StringComparison.OrdinalIgnoreCase);
    }

    public static string NormalizeUnit(string? unit) => (unit ?? string.Empty).Trim();

    public static bool ShouldUpdateExistingUnit(string? existingUnit, string? importedUnit)
    {
        var normalizedImported = NormalizeUnit(importedUnit);
        if (string.IsNullOrWhiteSpace(normalizedImported)) return false;

        return !string.Equals(
            NormalizeUnit(existingUnit),
            normalizedImported,
            StringComparison.OrdinalIgnoreCase);
    }

    public static string NormalizeMeterType(string? type)
        => string.Equals(type?.Trim(), "sub", StringComparison.OrdinalIgnoreCase) ? "sub" : "main";
}
