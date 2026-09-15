using PoWorks_Rework.Models;

namespace PoWorks_Rework.Services
{
    public static class MeterLifecycleRules
    {
        private static readonly HashSet<string> AllowedTypes =
            new(StringComparer.OrdinalIgnoreCase) { "main", "sub" };

        private static readonly HashSet<string> AllowedStatuses =
            new(StringComparer.OrdinalIgnoreCase) { "All", "Active", "Disabled" };

        private static readonly HashSet<string> AllowedAssignments =
            new(StringComparer.OrdinalIgnoreCase) { "All", "Assigned", "Unassigned" };

        public static IReadOnlyList<string> Validate(Meter meter)
        {
            var errors = new List<string>();

            if (string.IsNullOrWhiteSpace(meter.Name))
                errors.Add("Meter name is required.");
            else if (meter.Name.Trim().Length > 100)
                errors.Add("Meter name cannot exceed 100 characters.");

            if (!string.IsNullOrWhiteSpace(meter.Label) && meter.Label.Length > 150)
                errors.Add("Meter label cannot exceed 150 characters.");

            if (!string.IsNullOrWhiteSpace(meter.Unit) && meter.Unit.Length > 20)
                errors.Add("Meter unit cannot exceed 20 characters.");

            if (!AllowedTypes.Contains(meter.Type ?? ""))
                errors.Add("Meter type must be Main or Sub.");

            return errors;
        }

        public static string NormalizeType(string? type) =>
            string.Equals(type, "sub", StringComparison.OrdinalIgnoreCase) ? "sub" : "main";

        public static string NormalizeStatus(string? status) =>
            AllowedStatuses.Contains(status ?? "") ? status! : "All";

        public static string NormalizeAssignment(string? assignment) =>
            AllowedAssignments.Contains(assignment ?? "") ? assignment! : "All";

        public static bool CanPermanentlyDelete(MeterDependencySummary dependencies) =>
            dependencies.CanDelete;
    }
}
