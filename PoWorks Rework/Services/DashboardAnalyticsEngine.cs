using PoWorks_Rework.Models;

namespace PoWorks_Rework.Services
{
    /// <summary>
    /// Pure, database-independent analytics engine. It turns normalized
    /// per-meter/per-bucket measurements into user-facing series, KPIs and
    /// rankings. Keeping this logic pure makes every aggregation mode unit-testable.
    /// </summary>
    public static class DashboardAnalyticsEngine
    {
        private static readonly string[] Colors =
        {
            "#2563EB", "#0EA5E9", "#10B981", "#8B5CF6",
            "#F59E0B", "#EF4444", "#14B8A6", "#6366F1",
            "#84CC16", "#EC4899", "#64748B", "#0891B2"
        };

        public static DashboardAnalyticsResult Build(
            DashboardAnalyticsQuery query,
            IReadOnlyList<MeasurementBucketResult> rows)
        {
            var definition = MeasurementSemantics.GetDefinition(query.Metric);
            var aggregation = MeasurementSemantics.ResolveAggregation(
                definition.Key,
                query.Aggregation);
            var scopeMode = NormalizeScope(query.ScopeMode);

            var result = new DashboardAnalyticsResult
            {
                Metadata = new DashboardAnalyticsMetadata
                {
                    Metric = definition.Key,
                    MetricLabel = definition.Label,
                    Description = definition.Description,
                    ValueKind = definition.ValueKind,
                    CanonicalUnit = definition.CanonicalUnit,
                    DefaultAggregation = definition.DefaultAggregation,
                    AllowedAggregations = definition.AllowedAggregations.ToList(),
                    ScopeMode = scopeMode,
                    Aggregation = aggregation,
                    SourceMeterCount = rows.Select(r => r.MeterId).Distinct().Count()
                }
            };

            if (rows.Count == 0)
            {
                result.Summary = BuildSummary(query, definition, aggregation, rows, Array.Empty<double>());
                return result;
            }

            var units = rows
                .Select(r => r.CanonicalUnit?.Trim() ?? string.Empty)
                .Where(unit => !string.IsNullOrWhiteSpace(unit))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (units.Count > 1)
            {
                throw new InvalidOperationException(
                    "The selected raw measurements use different units. " +
                    "Select meters with the same unit or choose a classified measurement.");
            }

            var canonicalUnit = units.FirstOrDefault() ?? definition.CanonicalUnit;
            result.Metadata.CanonicalUnit = canonicalUnit;

            var labels = rows
                .Select(r => r.ReadingDate)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(label => label, StringComparer.Ordinal)
                .ToList();

            var candidates = BuildSeriesCandidates(
                query,
                rows,
                scopeMode,
                aggregation,
                canonicalUnit,
                labels);

            if (scopeMode != "aggregate" && candidates.Count > query.MaxSeries)
            {
                var keep = Math.Max(1, query.MaxSeries);
                result.Metadata.OmittedSeries = candidates.Count - keep;
                candidates = candidates
                    .OrderByDescending(candidate => candidate.Score)
                    .ThenBy(candidate => candidate.Label, StringComparer.OrdinalIgnoreCase)
                    .Take(keep)
                    .ToList();
            }

            result.ChartData.Labels = labels;
            for (var index = 0; index < candidates.Count; index++)
            {
                var candidate = candidates[index];
                var color = Colors[index % Colors.Length];

                result.ChartData.Datasets.Add(new ChartDataset
                {
                    MeterId = candidate.MeterId,
                    TenantId = candidate.TenantId,
                    SeriesKey = candidate.SeriesKey,
                    Label = candidate.Label,
                    MeterName = candidate.Label,
                    TenantName = candidate.TenantName,
                    Unit = canonicalUnit,
                    MeasurementMetric = definition.Key,
                    IsAggregate = candidate.IsAggregate,
                    SourceCount = candidate.SourceCount,
                    BackgroundColor = color,
                    BorderColor = color,
                    Data = labels
                        .Select(label => candidate.Values.TryGetValue(label, out var value)
                            ? value
                            : (double?)null)
                        .ToList()
                });
            }

            var aggregateByBucket = rows
                .GroupBy(r => r.ReadingDate, StringComparer.Ordinal)
                .OrderBy(group => group.Key, StringComparer.Ordinal)
                .Select(group => AggregateValues(
                    group.Select(r => r.Value),
                    aggregation))
                .ToList();

            result.Summary = BuildSummary(
                query,
                definition,
                aggregation,
                rows,
                aggregateByBucket,
                canonicalUnit,
                candidates.Count);

            result.Ranking = BuildRanking(
                query,
                definition,
                rows,
                canonicalUnit)
                .Take(Math.Max(1, query.RankingLimit))
                .ToList();

            return result;
        }

        public static double AggregateValues(
            IEnumerable<double> values,
            string aggregation)
        {
            var finite = values.Where(double.IsFinite).ToList();
            if (finite.Count == 0)
                return 0d;

            return aggregation.ToLowerInvariant() switch
            {
                "average" => finite.Average(),
                "min" => finite.Min(),
                "max" => finite.Max(),
                _ => finite.Sum()
            };
        }

        private static List<SeriesCandidate> BuildSeriesCandidates(
            DashboardAnalyticsQuery query,
            IReadOnlyList<MeasurementBucketResult> rows,
            string scopeMode,
            string aggregation,
            string canonicalUnit,
            IReadOnlyList<string> labels)
        {
            if (scopeMode == "aggregate")
            {
                var tenantName = query.TenantId.HasValue
                    ? rows.Select(r => r.TenantName)
                        .FirstOrDefault(name => !string.IsNullOrWhiteSpace(name))
                    : string.Empty;

                var label = query.TenantId.HasValue
                    ? $"{tenantName ?? "Selected tenant"} · aggregate"
                    : query.MeterIds.Count > 0
                        ? "Selected meters · aggregate"
                        : "Building · aggregate";

                return new List<SeriesCandidate>
                {
                    BuildCandidate(
                        "aggregate",
                        label,
                        rows,
                        aggregation,
                        canonicalUnit,
                        isAggregate: true,
                        tenantId: query.TenantId,
                        tenantName: tenantName ?? string.Empty,
                        meterId: 0)
                };
            }

            if (scopeMode == "tenant")
            {
                return rows
                    .GroupBy(row => new
                    {
                        row.TenantId,
                        Name = string.IsNullOrWhiteSpace(row.TenantName)
                            ? "Facility / unassigned"
                            : row.TenantName
                    })
                    .Select(group => BuildCandidate(
                        $"tenant:{group.Key.TenantId?.ToString() ?? "facility"}",
                        group.Key.Name,
                        group,
                        aggregation,
                        canonicalUnit,
                        isAggregate: true,
                        tenantId: group.Key.TenantId,
                        tenantName: group.Key.Name,
                        meterId: group.Key.TenantId ?? 0))
                    .ToList();
            }

            return rows
                .GroupBy(row => new
                {
                    row.MeterId,
                    row.MeterName,
                    row.TenantId,
                    row.TenantName
                })
                .Select(group => BuildCandidate(
                    $"meter:{group.Key.MeterId}",
                    group.Key.MeterName,
                    group,
                    "average",
                    canonicalUnit,
                    isAggregate: false,
                    tenantId: group.Key.TenantId,
                    tenantName: group.Key.TenantName,
                    meterId: group.Key.MeterId))
                .ToList();
        }

        private static SeriesCandidate BuildCandidate(
            string seriesKey,
            string label,
            IEnumerable<MeasurementBucketResult> sourceRows,
            string aggregation,
            string canonicalUnit,
            bool isAggregate,
            int? tenantId,
            string tenantName,
            int meterId)
        {
            var materialized = sourceRows.ToList();
            var values = materialized
                .GroupBy(row => row.ReadingDate, StringComparer.Ordinal)
                .ToDictionary(
                    group => group.Key,
                    group => AggregateValues(group.Select(row => row.Value), aggregation),
                    StringComparer.Ordinal);

            return new SeriesCandidate
            {
                SeriesKey = seriesKey,
                Label = label,
                TenantId = tenantId,
                TenantName = tenantName,
                MeterId = meterId,
                IsAggregate = isAggregate,
                SourceCount = materialized.Select(row => row.MeterId).Distinct().Count(),
                Values = values,
                Score = RankingScore(
                    materialized,
                    MeasurementSemantics.GetDefinition(
                        materialized.Count > 0
                            ? InferMetricFromCanonicalUnit(canonicalUnit)
                            : "raw"))
            };
        }

        private static string InferMetricFromCanonicalUnit(string canonicalUnit) =>
            canonicalUnit switch
            {
                "kWh" => "energy",
                "kW" => "power",
                "m³" => "volume",
                "m³/h" => "flow",
                "°C" => "temperature",
                "bar" => "pressure",
                "%" => "percentage",
                _ => "raw"
            };

        private static DashboardAnalyticsSummary BuildSummary(
            DashboardAnalyticsQuery query,
            MeasurementMetricDefinition definition,
            string aggregation,
            IReadOnlyList<MeasurementBucketResult> rows,
            IReadOnlyList<double> aggregateByBucket,
            string? unitOverride = null,
            int seriesCount = 0)
        {
            var unit = unitOverride ?? definition.CanonicalUnit;
            var activeMeters = rows.Select(row => row.MeterId).Distinct().Count();
            var bucketCount = rows.Select(row => row.ReadingDate).Distinct().Count();
            var periodDays = Math.Max(
                1,
                (query.EndDate.Date - query.StartDate.Date).Days + 1);
            var expectedBucketCount = ExpectedBucketCount(
                query.StartDate,
                query.EndDate,
                query.DateFilter);
            var expectedCells = Math.Max(
                1,
                activeMeters * Math.Max(1, expectedBucketCount));
            var actualCells = rows
                .GroupBy(row => new { row.MeterId, row.ReadingDate })
                .Count();
            var coverage = activeMeters == 0
                ? 0d
                : Math.Min(100d, actualCells * 100d / expectedCells);

            var summary = new DashboardAnalyticsSummary
            {
                Metric = definition.Key,
                MetricLabel = definition.Label,
                Unit = unit,
                ScopeMode = NormalizeScope(query.ScopeMode),
                Aggregation = aggregation,
                ActiveMeters = activeMeters,
                SeriesCount = seriesCount,
                PeriodDays = periodDays,
                DataBuckets = bucketCount,
                CoveragePercent = coverage
            };

            var values = aggregateByBucket.Where(double.IsFinite).ToList();
            var average = values.Count == 0 ? 0d : values.Average();
            var minimum = values.Count == 0 ? 0d : values.Min();
            var maximum = values.Count == 0 ? 0d : values.Max();

            if (definition.ValueKind == "quantity")
            {
                var total = values.Sum();
                var bucketLabel = query.DateFilter?.ToLowerInvariant() switch
                {
                    "hourly" => "Highest hourly value",
                    "monthly" => "Highest monthly value",
                    "yearly" => "Highest annual value",
                    _ => "Highest daily value"
                };

                summary.Kpis.Add(new DashboardKpi
                {
                    Key = "total",
                    Label = definition.Key == "volume"
                        ? "Total volume"
                        : "Total consumption",
                    Value = total,
                    Unit = unit,
                    Detail = "Across the complete selected period"
                });
                summary.Kpis.Add(new DashboardKpi
                {
                    Key = "dailyAverage",
                    Label = "Average per day",
                    Value = total / periodDays,
                    Unit = $"{unit}/day",
                    Detail = $"Normalized over {periodDays} calendar day(s)"
                });
                summary.Kpis.Add(new DashboardKpi
                {
                    Key = "peak",
                    Label = "Peak period",
                    Value = maximum,
                    Unit = unit,
                    Detail = bucketLabel
                });
            }
            else
            {
                var prefix = definition.Key switch
                {
                    "power" => "demand",
                    "flow" => "flow",
                    "temperature" => "temperature",
                    "pressure" => "pressure",
                    "percentage" => "value",
                    _ => "value"
                };

                summary.Kpis.Add(new DashboardKpi
                {
                    Key = "average",
                    Label = $"Average {prefix}",
                    Value = average,
                    Unit = unit,
                    Detail = "Average of the displayed analytical scope"
                });
                summary.Kpis.Add(new DashboardKpi
                {
                    Key = "minimum",
                    Label = $"Minimum {prefix}",
                    Value = minimum,
                    Unit = unit,
                    Detail = "Lowest displayed period value"
                });
                summary.Kpis.Add(new DashboardKpi
                {
                    Key = "maximum",
                    Label = definition.Key == "power"
                        ? "Peak demand"
                        : $"Maximum {prefix}",
                    Value = maximum,
                    Unit = unit,
                    Detail = "Highest displayed period value"
                });
            }

            summary.Kpis.Add(new DashboardKpi
            {
                Key = "meters",
                Label = definition.ValueKind == "state"
                    ? "Active sensors"
                    : "Active meters",
                Value = activeMeters,
                Unit = string.Empty,
                Detail = $"{coverage:0.#}% bucket coverage"
            });

            return summary;
        }

        private static int ExpectedBucketCount(
            DateTime startDate,
            DateTime endDate,
            string? dateFilter)
        {
            if (endDate < startDate)
                return 0;

            return dateFilter?.Trim().ToLowerInvariant() switch
            {
                "hourly" => Math.Max(
                    1,
                    (int)Math.Floor((endDate - startDate).TotalHours) + 1),
                "monthly" => Math.Max(
                    1,
                    ((endDate.Year - startDate.Year) * 12)
                    + endDate.Month
                    - startDate.Month
                    + 1),
                "yearly" => Math.Max(
                    1,
                    endDate.Year - startDate.Year + 1),
                _ => Math.Max(
                    1,
                    (endDate.Date - startDate.Date).Days + 1)
            };
        }

        private static IEnumerable<DashboardRankingItem> BuildRanking(
            DashboardAnalyticsQuery query,
            MeasurementMetricDefinition definition,
            IReadOnlyList<MeasurementBucketResult> rows,
            string canonicalUnit)
        {
            var scopeMode = NormalizeScope(query.ScopeMode);
            var rankTenants =
                scopeMode == "tenant" ||
                (scopeMode == "aggregate" &&
                 !query.TenantId.HasValue &&
                 query.MeterIds.Count == 0);

            if (rankTenants)
            {
                return rows
                    .GroupBy(row => new
                    {
                        row.TenantId,
                        Name = string.IsNullOrWhiteSpace(row.TenantName)
                            ? "Facility / unassigned"
                            : row.TenantName
                    })
                    .Select(group => new DashboardRankingItem
                    {
                        Key = $"tenant:{group.Key.TenantId?.ToString() ?? "facility"}",
                        Name = group.Key.Name,
                        TenantName = string.Empty,
                        Value = RankingScore(group.ToList(), definition),
                        Unit = canonicalUnit,
                        MeterCount = group.Select(row => row.MeterId).Distinct().Count()
                    })
                    .OrderByDescending(item => item.Value)
                    .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase);
            }

            return rows
                .GroupBy(row => new
                {
                    row.MeterId,
                    row.MeterName,
                    row.TenantName
                })
                .Select(group => new DashboardRankingItem
                {
                    Key = $"meter:{group.Key.MeterId}",
                    Name = group.Key.MeterName,
                    TenantName = group.Key.TenantName,
                    Value = RankingScore(group.ToList(), definition),
                    Unit = canonicalUnit,
                    MeterCount = 1
                })
                .OrderByDescending(item => item.Value)
                .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase);
        }

        private static double RankingScore(
            IReadOnlyList<MeasurementBucketResult> rows,
            MeasurementMetricDefinition definition)
        {
            if (rows.Count == 0)
                return 0d;

            return definition.ValueKind == "quantity"
                ? rows.Sum(row => row.Value)
                : rows.Average(row => row.Value);
        }

        private static string NormalizeScope(string? scope)
        {
            var normalized = (scope ?? string.Empty).Trim().ToLowerInvariant();
            return normalized switch
            {
                "tenant" => "tenant",
                "meter" => "meter",
                _ => "aggregate"
            };
        }

        private sealed class SeriesCandidate
        {
            public string SeriesKey { get; set; } = string.Empty;
            public string Label { get; set; } = string.Empty;
            public int MeterId { get; set; }
            public int? TenantId { get; set; }
            public string TenantName { get; set; } = string.Empty;
            public bool IsAggregate { get; set; }
            public int SourceCount { get; set; }
            public Dictionary<string, double> Values { get; set; } = new(StringComparer.Ordinal);
            public double Score { get; set; }
        }
    }
}
