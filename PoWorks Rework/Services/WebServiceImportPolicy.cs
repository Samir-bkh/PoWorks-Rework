namespace PoWorks_Rework.Services;

/// <summary>
/// Pure, database-independent normalization and filtering rules shared by every
/// PCVue Web Service import path.
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

    public static string NormalizeUnit(string? unit)
        => (unit ?? string.Empty).Trim();

    public static bool ShouldUpdateExistingUnit(
        string? existingUnit,
        string? importedUnit)
    {
        var normalizedImported = NormalizeUnit(importedUnit);
        if (string.IsNullOrWhiteSpace(normalizedImported)) return false;

        return !string.Equals(
            NormalizeUnit(existingUnit),
            normalizedImported,
            StringComparison.OrdinalIgnoreCase);
    }

    public static string NormalizeMeterType(string? type)
        => string.Equals(type?.Trim(), "sub", StringComparison.OrdinalIgnoreCase)
            ? "sub"
            : "main";
}
