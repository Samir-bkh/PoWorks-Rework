using PoWorks_Rework.Models;

namespace PoWorks_Rework.Services
{
    public static class TenantLifecycleRules
    {
        private static readonly HashSet<string> AllowedPeriods = new(StringComparer.OrdinalIgnoreCase)
        {
            "Daily", "Weekly", "Monthly", "Quarterly", "Yearly"
        };

        private static readonly HashSet<string> AllowedTariffTypes = new(StringComparer.OrdinalIgnoreCase)
        {
            "Company", "Personal"
        };

        public static IReadOnlyList<string> Validate(Tenant tenant)
        {
            var errors = new List<string>();

            if (string.IsNullOrWhiteSpace(tenant.CompanyName))
                errors.Add("Company name is required.");

            if (!string.IsNullOrWhiteSpace(tenant.Email) &&
                !System.Net.Mail.MailAddress.TryCreate(tenant.Email, out _))
                errors.Add("Email address is invalid.");

            if (!AllowedPeriods.Contains(tenant.Period ?? ""))
                errors.Add("Billing period is invalid.");

            if (!AllowedTariffTypes.Contains(tenant.TariffType ?? ""))
                errors.Add("Tariff type is invalid.");

            if (tenant.BaseRate < 0 || tenant.Threshold1Rate < 0 || tenant.Threshold2Rate < 0)
                errors.Add("Tariff rates cannot be negative.");

            if (tenant.Threshold1 < 0 || tenant.Threshold2 < 0)
                errors.Add("Tariff thresholds cannot be negative.");

            if (tenant.Threshold2 < tenant.Threshold1)
                errors.Add("Threshold 2 must be greater than or equal to Threshold 1.");

            if (tenant.Deposit < 0)
                errors.Add("Deposit cannot be negative.");

            if (tenant.MonthlyFee < 0)
                errors.Add("Monthly fixed fee cannot be negative.");

            if (!DateTime.TryParse(tenant.StartDate, out _))
                errors.Add("Start date is invalid.");

            return errors;
        }

        public static bool CanPermanentlyDelete(TenantDependencySummary dependencies) =>
            dependencies.CanDelete;
    }
}
