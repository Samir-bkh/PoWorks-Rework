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

    private readonly PCVueWebService _webService;
    private readonly TimeProvider _clock;

    public AutoImportSnapshotReader(PCVueWebService webService, TimeProvider? clock = null)
    {
        _webService = webService;
        _clock = clock ?? TimeProvider.System;
    }

    public readonly record struct Snapshot(int MeterId, DateTime Timestamp, decimal Value, int Quality);

    public async Task<IReadOnlyList<Snapshot>> ReadAsync(
        PCVueWebServiceSettings settings,
        IReadOnlyList<MeterForTrendsAnalysis> meters,
        CancellationToken cancellationToken = default)
    {
        if (meters.Count == 0) return Array.Empty<Snapshot>();

        var snapshots = new List<Snapshot>(meters.Count);
        foreach (var batch in meters.Chunk(BatchSize))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var variables = batch.Select(m => m.OriginalVariableName).Distinct(StringComparer.Ordinal).ToArray();
            var response = await _webService.BulkReadVariablesAsync(
                settings, variables, cancellationToken: cancellationToken);
            var values = ParseGoodNumericValues(response);
            var observedAt = _clock.GetLocalNow().DateTime;

            foreach (var meter in batch)
            {
                if (values.TryGetValue(meter.OriginalVariableName, out var value))
                    snapshots.Add(new Snapshot(meter.MeterId, observedAt, value.Value, value.Quality));
            }
        }

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
