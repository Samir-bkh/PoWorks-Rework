namespace PoWorks_Rework.Services
{
    /// <summary>
    /// Canonical consumption semantics shared by dashboard and billing.
    /// kWh readings are cumulative counters; other units preserve the legacy
    /// interpretation as instantaneous/rate values integrated over time.
    /// </summary>
    public static class ConsumptionFormula
    {
        public const string SqlDeltaExpression = @"
            CASE
                WHEN LOWER(REPLACE(COALESCE(""Unit"", ''), ' ', '')) = 'kwh' THEN
                    CASE
                        WHEN ""PreviousValue"" IS NULL THEN 0
                        WHEN ""Value"" >= ""PreviousValue"" THEN ""Value"" - ""PreviousValue""
                        WHEN ""Value"" >= 0 THEN ""Value""
                        ELSE 0
                    END
                ELSE
                    CASE
                        WHEN ""PreviousValue"" IS NULL OR ""PreviousTimestamp"" IS NULL THEN 0
                        WHEN ""Timestamp"" <= ""PreviousTimestamp"" THEN 0
                        ELSE ""PreviousValue"" *
                             (EXTRACT(EPOCH FROM (""Timestamp"" - ""PreviousTimestamp"")) / 3600.0)
                    END
            END";

        public static bool IsCumulativeUnit(string? unit) =>
            string.Equals(
                (unit ?? string.Empty).Replace(" ", string.Empty),
                "kWh",
                StringComparison.OrdinalIgnoreCase);

        public static decimal CalculateDelta(
            string? unit,
            decimal? previousValue,
            DateTime? previousTimestamp,
            decimal currentValue,
            DateTime currentTimestamp)
        {
            if (!previousValue.HasValue)
                return 0m;

            if (IsCumulativeUnit(unit))
            {
                if (currentValue >= previousValue.Value)
                    return currentValue - previousValue.Value;

                // Counter reset/rollover: count the new counter value from zero.
                return Math.Max(0m, currentValue);
            }

            if (!previousTimestamp.HasValue || currentTimestamp <= previousTimestamp.Value)
                return 0m;

            var hours = (decimal)(currentTimestamp - previousTimestamp.Value).TotalHours;
            return previousValue.Value * hours;
        }

        public static decimal CalculateConsumption(
            string? unit,
            IEnumerable<ConsumptionReadingPoint> readings)
        {
            var ordered = readings
                .OrderBy(r => r.Timestamp)
                .ToList();

            decimal total = 0m;
            decimal? previousValue = null;
            DateTime? previousTimestamp = null;

            foreach (var point in ordered)
            {
                total += CalculateDelta(
                    unit,
                    previousValue,
                    previousTimestamp,
                    point.Value,
                    point.Timestamp);

                previousValue = point.Value;
                previousTimestamp = point.Timestamp;
            }

            return Math.Round(total, 6);
        }
    }

    public sealed record ConsumptionReadingPoint(DateTime Timestamp, decimal Value);
}
