using System.Text.Json;

namespace PoWorks_Rework.Services;

/// <summary>
/// The variable browse endpoint does not include Unit. BulkRead returns the
/// requested properties in order, even when the current value has bad quality.
/// </summary>
public sealed class PcVueVariableUnitReader
{
    private const int BatchSize = 40;
    private readonly Func<string[], CancellationToken, Task<string>> _bulkRead;

    public PcVueVariableUnitReader(Func<string[], CancellationToken, Task<string>> bulkRead)
        => _bulkRead = bulkRead;

    public sealed record Result(Dictionary<string, string> Units, bool Complete);

    public async Task<Result> ReadAsync(
        IEnumerable<string> variableNames, CancellationToken cancellationToken = default)
    {
        var units = new Dictionary<string, string>(StringComparer.Ordinal);
        var names = variableNames.Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.Ordinal).ToArray();
        var complete = true;

        foreach (var batch in names.Chunk(BatchSize))
        {
            try
            {
                if (!await ReadBatchAsync(batch, units, cancellationToken)) complete = false;
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException or
                                       HttpRequestException or OperationCanceledException)
            {
                // Keep units already retrieved from earlier batches. A broken
                // metadata endpoint should not prevent browsing the names.
                complete = false;
                break;
            }
        }

        return new Result(units, complete);
    }

    private async Task<bool> ReadBatchAsync(
        string[] names, Dictionary<string, string> units, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            using var document = JsonDocument.Parse(await _bulkRead(names, cancellationToken));
            var root = document.RootElement;
            if (root.ValueKind == JsonValueKind.Array && root.GetArrayLength() > 0 &&
                root[0].ValueKind == JsonValueKind.Object &&
                root[0].TryGetProperty("code", out var arrayCode))
                throw new JsonException("PCVue BulkRead returned " + SafeStatus(arrayCode));
            if (root.ValueKind != JsonValueKind.Object)
                throw new JsonException("PCVue BulkRead did not return a variable object.");

            if (root.TryGetProperty("code", out var code))
                throw new JsonException("PCVue BulkRead returned " + SafeStatus(code));

            foreach (var name in names)
            {
                if (!root.TryGetProperty(name, out var entry) ||
                    entry.ValueKind != JsonValueKind.Object ||
                    !entry.TryGetProperty("result", out var result) ||
                    !result.TryGetProperty("code", out var resultCode) ||
                    !resultCode.TryGetProperty("value", out var status) ||
                    !status.TryGetInt32(out var statusValue) || statusValue != 1 ||
                    !entry.TryGetProperty("properties", out var properties) ||
                    properties.ValueKind != JsonValueKind.Array ||
                    properties.GetArrayLength() < 1 ||
                    properties[0].ValueKind != JsonValueKind.String)
                    continue;

                // Do not inspect value or quality: an unavailable sensor still has
                // a configured unit in PcVue.
                var unit = properties[0].GetString()?.Trim();
                if (!string.IsNullOrEmpty(unit)) units[name] = unit;
            }
            return true;
        }
        catch (Exception ex) when ((ex is JsonException or InvalidOperationException) &&
                                   ex.Message.Contains("E_UnknownVariable", StringComparison.Ordinal))
        {
            if (names.Length == 1) return false;
            var half = names.Length / 2;
            var left = await ReadBatchAsync(names[..half], units, cancellationToken);
            var right = await ReadBatchAsync(names[half..], units, cancellationToken);
            return left && right;
        }
    }

    private static string SafeStatus(JsonElement code)
    {
        if (code.ValueKind == JsonValueKind.Object &&
            code.TryGetProperty("label", out var label) && label.ValueKind == JsonValueKind.String)
        {
            var text = label.GetString();
            if (text is { Length: > 0 and <= 64 } &&
                text.All(c => char.IsAsciiLetterOrDigit(c) || c == '_'))
                return text;
        }

        return "a status error";
    }
}
