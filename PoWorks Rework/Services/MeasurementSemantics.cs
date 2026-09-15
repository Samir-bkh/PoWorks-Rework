namespace PoWorks_Rework.Services
{
    /// <summary>
    /// Describes one analytical measurement exposed by the building dashboard.
    /// A metric may accept several PcVue source units and always exposes one
    /// canonical unit so physically incompatible values are never mixed.
    /// </summary>
    public sealed record MeasurementMetricDefinition(
        string Key,
        string Label,
        string CanonicalUnit,
        string ValueKind,
        string DefaultAggregation,
        IReadOnlyList<string> AllowedAggregations,
        string Description);

    /// <summary>
    /// Central unit/measurement semantics used by dashboard discovery, SQL
    /// normalization, aggregation and tests.
    /// </summary>
    public static class MeasurementSemantics
    {
        private static readonly string[] EnergyCounterUnits = { "wh", "kwh", "mwh" };
        private static readonly string[] PowerUnits = { "w", "kw", "mw" };
        private static readonly string[] VolumeCounterUnits = { "ml", "l", "liter", "litre", "m3" };
        private static readonly string[] FlowUnits = { "l/h", "l/min", "l/s", "m3/h", "m3/min", "m3/s" };
        private static readonly string[] TemperatureUnits = { "c", "degc", "celsius", "f", "degf", "fahrenheit" };
        private static readonly string[] PressureUnits = { "pa", "kpa", "mpa", "bar" };
        private static readonly string[] PercentageUnits = { "%", "pct", "percent" };

        private static readonly IReadOnlyList<MeasurementMetricDefinition> Definitions =
            new List<MeasurementMetricDefinition>
            {
                new(
                    "energy",
                    "Energy consumption",
                    "kWh",
                    "quantity",
                    "sum",
                    new[] { "sum", "average", "min", "max" },
                    "Energy consumed. Wh/kWh/MWh counters are differenced; W/kW/MW sources are integrated over time."),
                new(
                    "power",
                    "Power demand",
                    "kW",
                    "rate",
                    "sum",
                    new[] { "sum", "average", "min", "max" },
                    "Instantaneous electrical demand normalized to kW."),
                new(
                    "volume",
                    "Volume consumption",
                    "m³",
                    "quantity",
                    "sum",
                    new[] { "sum", "average", "min", "max" },
                    "Consumed volume. Cumulative volume counters are differenced and flow meters can be integrated."),
                new(
                    "flow",
                    "Flow",
                    "m³/h",
                    "rate",
                    "sum",
                    new[] { "sum", "average", "min", "max" },
                    "Instantaneous volumetric flow normalized to m³/h."),
                new(
                    "temperature",
                    "Temperature",
                    "°C",
                    "state",
                    "average",
                    new[] { "average", "min", "max" },
                    "Temperature normalized to °C. Summing temperatures is intentionally not offered."),
                new(
                    "pressure",
                    "Pressure",
                    "bar",
                    "state",
                    "average",
                    new[] { "average", "min", "max" },
                    "Pressure normalized to bar."),
                new(
                    "percentage",
                    "Percentage / humidity",
                    "%",
                    "state",
                    "average",
                    new[] { "average", "min", "max" },
                    "Percentage-like measurements such as humidity or utilization."),
                new(
                    "raw",
                    "Other / raw measurement",
                    "",
                    "raw",
                    "average",
                    new[] { "average", "min", "max" },
                    "Unclassified PcVue measurements. Values are never converted; selected meters must share the same unit.")
            };

        public static IReadOnlyList<MeasurementMetricDefinition> GetDefinitions() => Definitions;

        public static MeasurementMetricDefinition GetDefinition(string? metric)
        {
            var key = NormalizeMetric(metric);
            return Definitions.FirstOrDefault(d => d.Key == key)
                   ?? Definitions.First(d => d.Key == "energy");
        }

        public static string NormalizeMetric(string? metric)
        {
            var key = (metric ?? string.Empty).Trim().ToLowerInvariant();
            return Definitions.Any(d => d.Key == key) ? key : "energy";
        }

        public static string NormalizeUnit(string? unit)
        {
            return (unit ?? string.Empty)
                .Trim()
                .ToLowerInvariant()
                .Replace(" ", string.Empty)
                .Replace("\u00A0", string.Empty)
                .Replace("³", "3")
                .Replace("°", string.Empty);
        }

        public static string GetFamilyForUnit(string? unit)
        {
            var normalized = NormalizeUnit(unit);

            if (EnergyCounterUnits.Contains(normalized)) return "energy";
            if (PowerUnits.Contains(normalized)) return "power";
            if (VolumeCounterUnits.Contains(normalized)) return "volume";
            if (FlowUnits.Contains(normalized)) return "flow";
            if (TemperatureUnits.Contains(normalized)) return "temperature";
            if (PressureUnits.Contains(normalized)) return "pressure";
            if (PercentageUnits.Contains(normalized)) return "percentage";
            return "raw";
        }

        public static IReadOnlyList<string> GetCompatibleMetrics(string? unit)
        {
            var family = GetFamilyForUnit(unit);
            return family switch
            {
                "energy" => new[] { "energy" },
                "power" => new[] { "power", "energy" },
                "volume" => new[] { "volume" },
                "flow" => new[] { "flow", "volume" },
                "temperature" => new[] { "temperature" },
                "pressure" => new[] { "pressure" },
                "percentage" => new[] { "percentage" },
                _ => new[] { "raw" }
            };
        }

        public static bool IsCompatible(string? metric, string? unit)
        {
            var key = NormalizeMetric(metric);
            return GetCompatibleMetrics(unit)
                .Contains(key, StringComparer.OrdinalIgnoreCase);
        }

        public static string ResolveAggregation(string? metric, string? requested)
        {
            var definition = GetDefinition(metric);
            var normalized = (requested ?? string.Empty).Trim().ToLowerInvariant();

            if (string.IsNullOrWhiteSpace(normalized) || normalized == "auto")
                return definition.DefaultAggregation;

            return definition.AllowedAggregations
                .Contains(normalized, StringComparer.OrdinalIgnoreCase)
                ? normalized
                : definition.DefaultAggregation;
        }

        public static string GetCanonicalUnit(string? metric, string? rawUnit = null)
        {
            var definition = GetDefinition(metric);
            return definition.Key == "raw"
                ? (rawUnit ?? string.Empty).Trim()
                : definition.CanonicalUnit;
        }

        public static double ConvertInstantValue(string? metric, string? unit, double value)
        {
            var key = NormalizeMetric(metric);
            var normalized = NormalizeUnit(unit);

            return key switch
            {
                "power" => value * PowerFactorToKw(normalized),
                "flow" => value * FlowFactorToM3PerHour(normalized),
                "temperature" => normalized is "f" or "degf" or "fahrenheit"
                    ? (value - 32d) * 5d / 9d
                    : value,
                "pressure" => normalized switch
                {
                    "pa" => value / 100000d,
                    "kpa" => value / 100d,
                    "mpa" => value * 10d,
                    _ => value
                },
                "percentage" => value,
                "raw" => value,
                _ => value
            };
        }

        public static decimal CalculateIntervalQuantity(
            string? metric,
            string? unit,
            decimal? previousValue,
            DateTime? previousTimestamp,
            decimal currentValue,
            DateTime currentTimestamp)
        {
            if (!previousValue.HasValue)
                return 0m;

            var key = NormalizeMetric(metric);
            var normalized = NormalizeUnit(unit);

            if (key == "energy" && EnergyCounterUnits.Contains(normalized))
            {
                var delta = currentValue >= previousValue.Value
                    ? currentValue - previousValue.Value
                    : Math.Max(0m, currentValue);

                var factor = normalized switch
                {
                    "wh" => 0.001m,
                    "kwh" => 1m,
                    "mwh" => 1000m,
                    _ => 0m
                };

                return delta * factor;
            }

            if (key == "volume" && VolumeCounterUnits.Contains(normalized))
            {
                var delta = currentValue >= previousValue.Value
                    ? currentValue - previousValue.Value
                    : Math.Max(0m, currentValue);

                var factor = normalized switch
                {
                    "ml" => 0.000001m,
                    "l" or "liter" or "litre" => 0.001m,
                    "m3" => 1m,
                    _ => 0m
                };

                return delta * factor;
            }

            if (!previousTimestamp.HasValue || currentTimestamp <= previousTimestamp.Value)
                return 0m;

            var hours = (decimal)(currentTimestamp - previousTimestamp.Value).TotalHours;
            if (hours <= 0m)
                return 0m;

            if (key == "energy" && PowerUnits.Contains(normalized))
            {
                return Math.Max(0m, previousValue.Value)
                       * (decimal)PowerFactorToKw(normalized)
                       * hours;
            }

            if (key == "volume" && FlowUnits.Contains(normalized))
            {
                return Math.Max(0m, previousValue.Value)
                       * (decimal)FlowFactorToM3PerHour(normalized)
                       * hours;
            }

            return 0m;
        }

        public static string SqlNormalizedUnit(string columnSql) =>
            $"LOWER(REPLACE(REPLACE(REPLACE(REPLACE(COALESCE({columnSql}, ''), ' ', ''), CHR(160), ''), '³', '3'), '°', ''))";

        public static string SqlMetricPredicate(string? metric, string unitColumnSql)
        {
            var key = NormalizeMetric(metric);
            var unit = SqlNormalizedUnit(unitColumnSql);

            string In(IEnumerable<string> values) =>
                string.Join(", ", values.Select(v => $"'{v.Replace("'", "''")}'"));

            var recognized = EnergyCounterUnits
                .Concat(PowerUnits)
                .Concat(VolumeCounterUnits)
                .Concat(FlowUnits)
                .Concat(TemperatureUnits)
                .Concat(PressureUnits)
                .Concat(PercentageUnits)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            return key switch
            {
                "energy" => $"{unit} IN ({In(EnergyCounterUnits.Concat(PowerUnits))})",
                "power" => $"{unit} IN ({In(PowerUnits)})",
                "volume" => $"{unit} IN ({In(VolumeCounterUnits.Concat(FlowUnits))})",
                "flow" => $"{unit} IN ({In(FlowUnits)})",
                "temperature" => $"{unit} IN ({In(TemperatureUnits)})",
                "pressure" => $"{unit} IN ({In(PressureUnits)})",
                "percentage" => $"{unit} IN ({In(PercentageUnits)})",
                "raw" => $"({unit} <> '' AND {unit} NOT IN ({In(recognized)}))",
                _ => $"{unit} IN ({In(EnergyCounterUnits.Concat(PowerUnits))})"
            };
        }

        public static string SqlInstantValueExpression(
            string? metric,
            string unitColumnSql,
            string valueColumnSql)
        {
            var key = NormalizeMetric(metric);
            var unit = SqlNormalizedUnit(unitColumnSql);

            return key switch
            {
                "power" => $@"({valueColumnSql}) * CASE {unit}
                    WHEN 'w' THEN 0.001
                    WHEN 'kw' THEN 1.0
                    WHEN 'mw' THEN 1000.0
                    ELSE NULL END",
                "flow" => $@"({valueColumnSql}) * CASE {unit}
                    WHEN 'l/h' THEN 0.001
                    WHEN 'l/min' THEN 0.06
                    WHEN 'l/s' THEN 3.6
                    WHEN 'm3/h' THEN 1.0
                    WHEN 'm3/min' THEN 60.0
                    WHEN 'm3/s' THEN 3600.0
                    ELSE NULL END",
                "temperature" => $@"CASE
                    WHEN {unit} IN ('f', 'degf', 'fahrenheit')
                        THEN (({valueColumnSql}) - 32.0) * 5.0 / 9.0
                    ELSE ({valueColumnSql})
                    END",
                "pressure" => $@"({valueColumnSql}) * CASE {unit}
                    WHEN 'pa' THEN 0.00001
                    WHEN 'kpa' THEN 0.01
                    WHEN 'mpa' THEN 10.0
                    WHEN 'bar' THEN 1.0
                    ELSE NULL END",
                "percentage" => $"({valueColumnSql})",
                "raw" => $"({valueColumnSql})",
                _ => $"({valueColumnSql})"
            };
        }

        public static string SqlIntervalQuantityExpression(
            string? metric,
            string unitColumnSql,
            string previousValueColumnSql,
            string previousTimestampColumnSql,
            string currentValueColumnSql,
            string currentTimestampColumnSql)
        {
            var key = NormalizeMetric(metric);
            var unit = SqlNormalizedUnit(unitColumnSql);

            if (key == "energy")
            {
                return $@"CASE
                    WHEN {unit} IN ('wh','kwh','mwh') THEN
                        CASE
                            WHEN {previousValueColumnSql} IS NULL THEN 0
                            ELSE (
                                CASE
                                    WHEN {currentValueColumnSql} >= {previousValueColumnSql}
                                        THEN {currentValueColumnSql} - {previousValueColumnSql}
                                    WHEN {currentValueColumnSql} >= 0
                                        THEN {currentValueColumnSql}
                                    ELSE 0
                                END
                            ) * CASE {unit}
                                WHEN 'wh' THEN 0.001
                                WHEN 'kwh' THEN 1.0
                                WHEN 'mwh' THEN 1000.0
                                ELSE 0
                            END
                        END
                    WHEN {unit} IN ('w','kw','mw') THEN
                        CASE
                            WHEN {previousValueColumnSql} IS NULL
                              OR {previousTimestampColumnSql} IS NULL
                              OR {currentTimestampColumnSql} <= {previousTimestampColumnSql}
                                THEN 0
                            ELSE GREATEST({previousValueColumnSql}, 0) *
                                (EXTRACT(EPOCH FROM ({currentTimestampColumnSql} - {previousTimestampColumnSql})) / 3600.0) *
                                CASE {unit}
                                    WHEN 'w' THEN 0.001
                                    WHEN 'kw' THEN 1.0
                                    WHEN 'mw' THEN 1000.0
                                    ELSE 0
                                END
                        END
                    ELSE 0
                END";
            }

            if (key == "volume")
            {
                return $@"CASE
                    WHEN {unit} IN ('ml','l','liter','litre','m3') THEN
                        CASE
                            WHEN {previousValueColumnSql} IS NULL THEN 0
                            ELSE (
                                CASE
                                    WHEN {currentValueColumnSql} >= {previousValueColumnSql}
                                        THEN {currentValueColumnSql} - {previousValueColumnSql}
                                    WHEN {currentValueColumnSql} >= 0
                                        THEN {currentValueColumnSql}
                                    ELSE 0
                                END
                            ) * CASE {unit}
                                WHEN 'ml' THEN 0.000001
                                WHEN 'l' THEN 0.001
                                WHEN 'liter' THEN 0.001
                                WHEN 'litre' THEN 0.001
                                WHEN 'm3' THEN 1.0
                                ELSE 0
                            END
                        END
                    WHEN {unit} IN ('l/h','l/min','l/s','m3/h','m3/min','m3/s') THEN
                        CASE
                            WHEN {previousValueColumnSql} IS NULL
                              OR {previousTimestampColumnSql} IS NULL
                              OR {currentTimestampColumnSql} <= {previousTimestampColumnSql}
                                THEN 0
                            ELSE GREATEST({previousValueColumnSql}, 0) *
                                (EXTRACT(EPOCH FROM ({currentTimestampColumnSql} - {previousTimestampColumnSql})) / 3600.0) *
                                CASE {unit}
                                    WHEN 'l/h' THEN 0.001
                                    WHEN 'l/min' THEN 0.06
                                    WHEN 'l/s' THEN 3.6
                                    WHEN 'm3/h' THEN 1.0
                                    WHEN 'm3/min' THEN 60.0
                                    WHEN 'm3/s' THEN 3600.0
                                    ELSE 0
                                END
                        END
                    ELSE 0
                END";
            }

            return "0";
        }

        private static double PowerFactorToKw(string normalizedUnit) =>
            normalizedUnit switch
            {
                "w" => 0.001d,
                "kw" => 1d,
                "mw" => 1000d,
                _ => 0d
            };

        private static double FlowFactorToM3PerHour(string normalizedUnit) =>
            normalizedUnit switch
            {
                "l/h" => 0.001d,
                "l/min" => 0.06d,
                "l/s" => 3.6d,
                "m3/h" => 1d,
                "m3/min" => 60d,
                "m3/s" => 3600d,
                _ => 0d
            };
    }
}
