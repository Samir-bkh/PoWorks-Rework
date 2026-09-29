namespace PoWorks_Rework.Services;

/// <summary>
/// Small deterministic rules used by the PCVue Web Services import pipeline.
/// Keeping metadata merge rules here makes re-import behaviour explicit and testable.
/// </summary>
public static class WebServiceMeterImportRules
{
    /// <summary>
    /// A non-empty unit entered/imported by the operator replaces the stored unit.
    /// An empty incoming value never erases an existing engineering unit.
    /// </summary>
    public static string ResolveUnit(string? existingUnit, string? incomingUnit)
    {
        var incoming = incomingUnit?.Trim();
        if (!string.IsNullOrWhiteSpace(incoming))
        {
            return incoming;
        }

        return existingUnit?.Trim() ?? string.Empty;
    }

    /// <summary>
    /// Normalizes the meter hierarchy type while preserving a valid existing type
    /// if the incoming value is empty or unknown.
    /// </summary>
    public static string ResolveMeterType(string? existingType, string? incomingType)
    {
        var incoming = incomingType?.Trim().ToLowerInvariant();
        if (incoming is "main" or "sub")
        {
            return incoming;
        }

        var existing = existingType?.Trim().ToLowerInvariant();
        return existing is "main" or "sub" ? existing : "main";
    }

    /// <summary>
    /// Parses an optional parent meter id. Blank/invalid values mean
    /// "do not change parent" during an update and "no parent" on insert.
    /// </summary>
    public static int? ParseParentId(string? parentMeterId)
        => int.TryParse(parentMeterId?.Trim(), out var value) && value > 0
            ? value
            : null;
}
