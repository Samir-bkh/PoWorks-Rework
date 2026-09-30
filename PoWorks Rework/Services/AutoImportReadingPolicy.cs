using PoWorks_Rework.Models;

namespace PoWorks_Rework.Services;

/// <summary>
/// Selects exclusively real PCVue historian readings for auto-import.
/// A failed or incomplete HTTP query must never be converted to a successful
/// synthetic reading (nor may an older point be accepted as a new point).
/// </summary>
public static class AutoImportReadingPolicy
{
    public readonly record struct RealReading(DateTime Timestamp, decimal Value);

    public static IReadOnlyList<RealReading> SelectNewRealReadings(
        VariableTrendResult? result,
        DateTime lastKnownTimestamp)
    {
        if (result is null || !result.Success || result.MaxNumberExceeded || result.TrendData is null)
            return Array.Empty<RealReading>();

        var readings = new SortedDictionary<DateTime, decimal>();
        foreach (var point in result.TrendData)
        {
            if (!string.Equals(point.Quality, "Good", StringComparison.OrdinalIgnoreCase) ||
                !point.TimestampParsed.HasValue || !double.IsFinite(point.Value))
                continue;

            // Maintain the timestamp convention currently used for the
            // PostgreSQL timestamp-without-time-zone column.
            var timestamp = point.TimestampParsed.Value.ToLocalTime();
            if (timestamp <= lastKnownTimestamp)
                continue;

            try
            {
                readings[timestamp] = Convert.ToDecimal(point.Value);
            }
            catch (OverflowException)
            {
                // An invalid numeric sample cannot become a database reading.
            }
        }

        return readings.Select(pair => new RealReading(pair.Key, pair.Value)).ToArray();
    }
}
