using System.Globalization;
using System.Text.Json;
using PoWorks_Rework.Models;

namespace PoWorks_Rework.Services;

/// <summary>
/// Captures one current PCVue value per meter and polling pass. The timestamp
/// is when PoWorks read the value, not the variable's last-change timestamp.
/// </summary>
public sealed class AutoImportSnapshotReader
{
    public const int BatchSize = 250;
    private const int ValuesGetBatchSize = 40;

    private readonly PCVueWebService _webService;
    private readonly TimeProvider _clock;
    private readonly TrendsService? _trendsService;
    private readonly ILogger<AutoImportSnapshotReader>? _logger;

    public AutoImportSnapshotReader(
        PCVueWebService webService, TimeProvider? clock = null,
        TrendsService? trendsService = null,
        ILogger<AutoImportSnapshotReader>? logger = null)
    {
        _webService = webService;
        _clock = clock ?? TimeProvider.System;
        _trendsService = trendsService;
        _logger = logger;
    }

    public readonly record struct Snapshot(int MeterId, DateTime Timestamp, decimal Value, int Quality);

    public async Task<IReadOnlyList<Snapshot>> ReadAsync(
        PCVueWebServiceSettings settings,
        IReadOnlyList<MeterForTrendsAnalysis> meters,
        CancellationToken cancellationToken = default,
        int intervalMinutes = 1)
    {
        if (meters.Count == 0) return Array.Empty<Snapshot>();

        var live = new List<Snapshot>(meters.Count);
        var unresolved = new List<MeterForTrendsAnalysis>();
        foreach (var batch in meters.Chunk(BatchSize))
            await ReadWithUnknownVariableIsolationAsync(settings, batch, live, unresolved, cancellationToken);

        if (unresolved.Count == 0) return live;

        _logger?.LogWarning(
            "PCVue real-time read could not resolve {Count}/{Total} variables; checking recent historical values. Examples: {Names}",
            unresolved.Count, meters.Count,
            string.Join(", ", unresolved.Take(5).Select(m => m.OriginalVariableName)));
        var history = await ReadRecentHistoryAsync(settings, unresolved, intervalMinutes, cancellationToken);
        var byId = live.Concat(history).ToDictionary(snapshot => snapshot.MeterId);
        return meters.Where(m => byId.ContainsKey(m.MeterId))
            .Select(m => byId[m.MeterId]).ToArray();
    }

    private async Task ReadWithUnknownVariableIsolationAsync(
        PCVueWebServiceSettings settings,
        IReadOnlyList<MeterForTrendsAnalysis> meters,
        List<Snapshot> live,
        List<MeterForTrendsAnalysis> unresolved,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            live.AddRange(await ReadRealTimeAsync(settings, meters, cancellationToken));
        }
        catch (Exception ex) when (meters.Count > 1 && IsUnknownVariable(ex) &&
                                   (ex is JsonException or InvalidOperationException))
        {
            // WST rejects an entire BulkRead when just one name is unknown.
            // Narrow down the bad names without losing live values from the rest.
            var half = meters.Count / 2;
            await ReadWithUnknownVariableIsolationAsync(
                settings, meters.Take(half).ToArray(), live, unresolved, cancellationToken);
            await ReadWithUnknownVariableIsolationAsync(
                settings, meters.Skip(half).ToArray(), live, unresolved, cancellationToken);
        }
        catch (Exception ex) when (_trendsService != null &&
                                   (ex is JsonException or InvalidOperationException))
        {
            unresolved.AddRange(meters);
        }
    }

    private static bool IsUnknownVariable(Exception ex) =>
        ex.Message.Contains("E_UnknownVariable", StringComparison.Ordinal);

    private async Task<IReadOnlyList<Snapshot>> ReadRealTimeAsync(
        PCVueWebServiceSettings settings,
        IReadOnlyList<MeterForTrendsAnalysis> meters,
        CancellationToken cancellationToken)
    {
        var snapshots = new List<Snapshot>(meters.Count);
        foreach (var batch in meters.Chunk(BatchSize))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var variables = batch.Select(m => m.OriginalVariableName).Distinct(StringComparer.Ordinal).ToArray();
            Dictionary<string, (decimal Value, int Quality)> values;
            try
            {
                var response = await _webService.BulkReadVariablesAsync(
                    settings, variables, cancellationToken: cancellationToken);
                values = ParseGoodNumericValues(response);
            }
            catch (Exception ex) when (ex is JsonException jsonError && !IsUnknownVariable(jsonError) ||
                                       ex is InvalidOperationException &&
                                       (ex.Message.Contains("HTTP 404", StringComparison.Ordinal) ||
                                        ex.Message.Contains("HTTP 405", StringComparison.Ordinal)))
            {
                // Some WST installations do not expose BulkRead even though they
                // expose the documented multi-value GET endpoint.
                values = new Dictionary<string, (decimal, int)>(StringComparer.Ordinal);
                try
                {
                    foreach (var group in variables.Chunk(ValuesGetBatchSize))
                    {
                        var response = await _webService.ReadVariablesAsync(settings, group, cancellationToken);
                        foreach (var entry in ParseGoodNumericValues(response))
                            values[entry.Key] = entry.Value;
                    }
                }
                catch (Exception getError) when (getError is JsonException or InvalidOperationException)
                {
                    throw new InvalidOperationException(
                        $"BulkRead: {ex.Message}; Values: {getError.Message}", getError);
                }
            }
            var observedAt = _clock.GetLocalNow().DateTime;

            foreach (var meter in batch)
            {
                if (values.TryGetValue(meter.OriginalVariableName, out var value))
                    snapshots.Add(new Snapshot(meter.MeterId, observedAt, value.Value, value.Quality));
            }
        }

        return snapshots;
    }

    private async Task<IReadOnlyList<Snapshot>> ReadRecentHistoryAsync(
        PCVueWebServiceSettings settings,
        IReadOnlyList<MeterForTrendsAnalysis> meters,
        int intervalMinutes,
        CancellationToken cancellationToken)
    {
        var nowUtc = _clock.GetUtcNow().UtcDateTime;
        var startUtc = nowUtc.AddMinutes(-Math.Max(5, Math.Clamp(intervalMinutes, 1, 1440) * 2 + 1));
        var names = meters.Select(m => m.OriginalVariableName).Distinct(StringComparer.Ordinal).ToList();
        var results = await _trendsService!.ProcessVariablesTrendsAsync(
            names, startUtc, nowUtc, settings,
            "Auto-import recent snapshot", cancellationToken);
        var byName = results.ToDictionary(r => r.VariableName, StringComparer.Ordinal);
        var snapshots = new List<Snapshot>(meters.Count);
        foreach (var meter in meters)
        {
            if (!byName.TryGetValue(meter.OriginalVariableName, out var result) || !result.Success)
                continue;

            var point = result.TrendData
                .Select(p => (Point: p, Parsed: PcVueTimestamp.TryParseUtc(p.Timestamp, out var utc), Utc: utc))
                .Where(item => item.Point.IsGoodQuality && item.Parsed &&
                               item.Utc >= startUtc && item.Utc <= nowUtc.AddSeconds(10) &&
                               double.IsFinite(item.Point.Value))
                .OrderByDescending(item => item.Utc)
                .FirstOrDefault();
            if (point.Point == null) continue;
            try
            {
                snapshots.Add(new Snapshot(meter.MeterId, _clock.GetLocalNow().DateTime,
                    Convert.ToDecimal(point.Point.Value), point.Point.QualityValue));
            }
            catch (OverflowException) { /* PCVue value cannot fit in the database. */ }
        }

        _logger?.LogInformation("PCVue recent history returned current values for {Count}/{Total} meters.",
            snapshots.Count, meters.Count);
        return snapshots;
    }

    private static Dictionary<string, (decimal Value, int Quality)> ParseGoodNumericValues(string response)
    {
        using var document = JsonDocument.Parse(response);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
            throw new JsonException(DescribeUnexpectedResponse(document.RootElement));

        // A WST error can also be returned as an object with a top-level code.
        if (document.RootElement.TryGetProperty("code", out var errorCode))
            throw new JsonException("PCVue value read returned a status object" + SafeResultCode(errorCode));

        var values = new Dictionary<string, (decimal Value, int Quality)>(StringComparer.Ordinal);
        foreach (var variable in document.RootElement.EnumerateObject())
        {
            var entry = variable.Value;
            if (entry.ValueKind != JsonValueKind.Object ||
                !entry.TryGetProperty("result", out var result) ||
                result.ValueKind != JsonValueKind.Object ||
                !result.TryGetProperty("code", out var code) ||
                code.ValueKind != JsonValueKind.Object ||
                !code.TryGetProperty("value", out var status) ||
                status.ValueKind != JsonValueKind.Number ||
                !status.TryGetInt32(out var statusCode) || statusCode != 1 ||
                !entry.TryGetProperty("quality", out var quality) ||
                quality.ValueKind != JsonValueKind.String ||
                !string.Equals(quality.GetString(), "Good", StringComparison.OrdinalIgnoreCase) ||
                !entry.TryGetProperty("QualityValue", out var qualityValue) ||
                qualityValue.ValueKind != JsonValueKind.Number ||
                !qualityValue.TryGetInt32(out var numericQuality) ||
                !entry.TryGetProperty("value", out var rawValue))
                continue;

            decimal number;
            if (rawValue.ValueKind == JsonValueKind.Number)
            {
                if (!rawValue.TryGetDecimal(out number)) continue;
            }
            else if (rawValue.ValueKind == JsonValueKind.String)
            {
                if (!decimal.TryParse(rawValue.GetString(), NumberStyles.Float,
                        CultureInfo.InvariantCulture, out number)) continue;
            }
            else
            {
                continue;
            }

            values[variable.Name] = (number, numericQuality);
        }

        return values;
    }

    private static string DescribeUnexpectedResponse(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Array)
        {
            if (root.GetArrayLength() > 0 && root[0].ValueKind == JsonValueKind.Object &&
                root[0].TryGetProperty("code", out var code))
                return "PCVue value read returned a status array" + SafeResultCode(code);
            return "PCVue value read returned an array rather than values keyed by variable";
        }
        return $"PCVue value read returned {root.ValueKind} rather than values keyed by variable";
    }

    private static string SafeResultCode(JsonElement code)
    {
        if (code.ValueKind != JsonValueKind.Object ||
            !code.TryGetProperty("label", out var label) || label.ValueKind != JsonValueKind.String)
            return string.Empty;
        var value = label.GetString();
        return value is { Length: > 0 and <= 64 } &&
               value.All(c => char.IsAsciiLetterOrDigit(c) || c == '_')
            ? $": {value}"
            : string.Empty;
    }
}
