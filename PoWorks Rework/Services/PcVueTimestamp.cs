using System.Globalization;

namespace PoWorks_Rework.Services;

/// <summary>
/// WST REST dates are UTC, including timestamps without a trailing Z. MeterReadings
/// currently stores local wall-clock timestamps in a PostgreSQL timestamp column.
/// Convert at the boundary and keep the database DateTime kind unspecified.
/// </summary>
public static class PcVueTimestamp
{
    public static bool TryParseUtc(string? value, out DateTime utc)
    {
        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var parsed))
        {
            utc = parsed.UtcDateTime;
            return true;
        }

        utc = default;
        return false;
    }

    public static bool TryToLocalDatabaseTime(
        string? value, TimeZoneInfo localZone, out DateTime local)
    {
        if (TryParseUtc(value, out var utc))
        {
            local = DateTime.SpecifyKind(
                TimeZoneInfo.ConvertTimeFromUtc(utc, localZone), DateTimeKind.Unspecified);
            return true;
        }

        local = default;
        return false;
    }
}
