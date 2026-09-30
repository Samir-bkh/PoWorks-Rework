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

        try
        {
            return await ReadRealTimeAsync(settings, meters, cancellationToken);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException &&
                                   _trendsService != null)
        {
            _logger?.LogWarning("PCVue real-time read unavailable ({Reason}); checking recent historical values.", ex.Message);
            return await ReadRecentHistoryAsync(settings, meters, intervalMinutes, cancellationToken);
        }
    }

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
            catch (Exception ex) when (ex is JsonException ||
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
        var now = _clock.GetLocalNow().DateTime;
        var start = now.AddMinutes(-Math.Max(5, Math.Clamp(intervalMinutes, 1, 1440) * 2 + 1));
        var names = meters.Select(m => m.OriginalVariableName).Distinct(StringComparer.Ordinal).ToList();
        var results = await _trendsService!.ProcessVariablesTrendsAsync(
            names, start.ToUniversalTime(), now.ToUniversalTime(), settings,
            "Auto-import recent snapshot", cancellationToken);
        var byName = results.ToDictionary(r => r.VariableName, StringComparer.Ordinal);
        var snapshots = new List<Snapshot>(meters.Count);
        foreach (var meter in meters)
        {
            if (!byName.TryGetValue(meter.OriginalVariableName, out var result) || !result.Success)
                continue;

            var point = result.TrendData
                .Where(p => p.IsGoodQuality && p.TimestampParsed is { } timestamp &&
                            timestamp >= start && timestamp <= now.AddSeconds(10) &&
                            double.IsFinite(p.Value))
                .OrderByDescending(p => p.TimestampParsed)
                .FirstOrDefault();
            if (point == null) continue;
            try
            {
                snapshots.Add(new Snapshot(meter.MeterId, _clock.GetLocalNow().DateTime,
                    Convert.ToDecimal(point.Value), point.QualityValue));
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
            throw new JsonException("PCVue BulkRead must return an object keyed by variable name.");

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
}
