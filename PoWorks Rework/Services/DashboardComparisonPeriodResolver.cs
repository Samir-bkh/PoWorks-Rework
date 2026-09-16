namespace PoWorks_Rework.Services
{
    public sealed record DashboardComparisonPeriod(
        DateTime StartDate,
        DateTime EndDate,
        int DurationDays);

    /// <summary>
    /// Resolves equal-duration comparison periods. The user chooses only the
    /// comparison start; the server owns the duration rule so every client
    /// receives mathematically comparable periods.
    /// </summary>
    public static class DashboardComparisonPeriodResolver
    {
        public static DashboardComparisonPeriod Resolve(
            DateTime primaryStart,
            DateTime primaryEnd,
            DateTime comparisonStart)
        {
            var normalizedStart = primaryStart.Date;
            var normalizedEnd = primaryEnd.Date;

            if (normalizedEnd < normalizedStart)
                throw new ArgumentException(
                    "Primary end date must be on or after primary start date.");

            var durationDays =
                (normalizedEnd - normalizedStart).Days + 1;
            var compareStart = comparisonStart.Date;
            var compareEnd = compareStart
                .AddDays(durationDays)
                .AddTicks(-1);

            return new DashboardComparisonPeriod(
                compareStart,
                compareEnd,
                durationDays);
        }
    }
}
