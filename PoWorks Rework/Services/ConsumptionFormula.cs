namespace PoWorks_Rework.Services
{
    /// <summary>
    /// Canonical energy-consumption semantics shared by the dashboard and billing.
    /// Supported cumulative energy counters (Wh/kWh/MWh) are converted to kWh by
    /// counter delta. Supported power readings (W/kW/MW) are integrated over time
    /// and converted to kWh. Non-energy sensor units are deliberately unsupported.
    /// </summary>
    public static class ConsumptionFormula
    {
        public const string SqlDeltaExpression = @"
            CASE
                WHEN LOWER(REPLACE(COALESCE(""Unit"", ''), ' ', '')) IN ('wh', 'kwh', 'mwh') THEN
                    (
                        CASE
                            WHEN ""PreviousValue"" IS NULL THEN 0
                            WHEN ""Value"" >= ""PreviousValue"" THEN ""Value"" - ""PreviousValue""
                            WHEN ""Value"" >= 0 THEN ""Value""
                            ELSE 0
                        END
                    ) *
                    CASE LOWER(REPLACE(COALESCE(""Unit"", ''), ' ', ''))
                        WHEN 'wh' THEN 0.001
                        WHEN 'kwh' THEN 1.0
                        WHEN 'mwh' THEN 1000.0
                        ELSE 0
                    END
                WHEN LOWER(REPLACE(COALESCE(""Unit"", ''), ' ', '')) IN ('w', 'kw', 'mw') THEN
                    CASE
                        WHEN ""PreviousValue"" IS NULL OR ""PreviousTimestamp"" IS NULL THEN 0
                        WHEN ""Timestamp"" <= ""PreviousTimestamp"" THEN 0
                        ELSE
                            ""PreviousValue"" *
                            (EXTRACT(EPOCH FROM (""Timestamp"" - ""PreviousTimestamp"")) / 3600.0) *
                            CASE LOWER(REPLACE(COALESCE(""Unit"", ''), ' ', ''))
                                WHEN 'w' THEN 0.001
                                WHEN 'kw' THEN 1.0
                                WHEN 'mw' THEN 1000.0
                                ELSE 0
                            END
                    END
                ELSE 0
            END";

        public static string SqlSupportedEnergyUnitPredicate(string columnSql)
        {
            if (string.IsNullOrWhiteSpace(columnSql))
                throw new ArgumentException("Column SQL is required.", nameof(columnSql));

            return $@"LOWER(REPLACE(COALESCE({columnSql}, ''), ' ', ''))
                IN ('wh', 'kwh', 'mwh', 'w', 'kw', 'mw')";
        }

        public static bool IsSupportedEnergyUnit(string? unit)
        {
            var normalized = NormalizeUnit(unit);
            return normalized is "wh" or "kwh" or "mwh" or "w" or "kw" or "mw";
        }

        public static bool IsCumulativeUnit(string? unit)
        {
            var normalized = NormalizeUnit(unit);
            return normalized is "wh" or "kwh" or "mwh";
        }

        public static bool IsPowerUnit(string? unit)
        {
            var normalized = NormalizeUnit(unit);
            return normalized is "w" or "kw" or "mw";
        }

        public static string GetConsumptionUnit(string? unit) =>
            IsSupportedEnergyUnit(unit) ? "kWh" : string.Empty;

        public static decimal CalculateDelta(
            string? unit,
            decimal? previousValue,
            DateTime? previousTimestamp,
            decimal currentValue,
            DateTime currentTimestamp)
        {
            var normalized = NormalizeUnit(unit);
            if (!IsSupportedEnergyUnit(normalized) || !previousValue.HasValue)
                return 0m;

            if (IsCumulativeUnit(normalized))
            {
                var rawDelta = currentValue >= previousValue.Value
                    ? currentValue - previousValue.Value
                    : Math.Max(0m, currentValue);

                var factor = normalized switch
                {
                    "wh" => 0.001m,
                    "kwh" => 1m,
                    "mwh" => 1000m,
                    _ => 0m
                };

                return rawDelta * factor;
            }

            if (!previousTimestamp.HasValue || currentTimestamp <= previousTimestamp.Value)
                return 0m;

            var hours = (decimal)(currentTimestamp - previousTimestamp.Value).TotalHours;
            var powerFactor = normalized switch
            {
                "w" => 0.001m,
                "kw" => 1m,
                "mw" => 1000m,
                _ => 0m
            };

            return previousValue.Value * powerFactor * hours;
        }

        public static decimal CalculateConsumption(
            string? unit,
            IEnumerable<ConsumptionReadingPoint> readings)
        {
            if (!IsSupportedEnergyUnit(unit))
                return 0m;

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

        private static string NormalizeUnit(string? unit) =>
            (unit ?? string.Empty)
                .Replace(" ", string.Empty)
                .Replace("\u00A0", string.Empty)
                .Trim()
                .ToLowerInvariant();
    }

    public sealed record ConsumptionReadingPoint(DateTime Timestamp, decimal Value);
}
