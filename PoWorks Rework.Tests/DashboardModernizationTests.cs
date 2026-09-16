using Microsoft.Extensions.Logging.Abstractions;
using PoWorks_Rework.Models;
using PoWorks_Rework.Services;
using Xunit;

namespace PoWorks_Rework.Tests;

public class DashboardModernizationTests
{
    [Fact]
    public void AverageDaily_UsesEntireSelectedCalendarRange()
    {
        var service = CreateDashboardService();
        var filters = new MeterReadingFilters
        {
            StartDate = new DateTime(2026, 7, 1),
            EndDate = new DateTime(2026, 9, 15, 23, 59, 59),
            DateFilter = "daily"
        };

        var data = new List<ConsumptionQueryResult>
        {
            new()
            {
                MeterId = 1,
                MeterName = "BacNet01.Light1",
                Unit = "kWh",
                ReadingDate = "2026-08-05",
                TotalConsumption = 56155.76
            },
            new()
            {
                MeterId = 1,
                MeterName = "BacNet01.Light1",
                Unit = "kWh",
                ReadingDate = "2026-08-20",
                TotalConsumption = 9105.14
            }
        };

        var summary = service.CalculateSummary(data, filters);

        Assert.Equal(77, summary.PeriodDays);
        Assert.Equal(65260.90, summary.TotalConsumption, 2);
        Assert.Equal(65260.90 / 77d, summary.AverageDaily, 6);
        Assert.Equal("kWh", summary.Unit);
        Assert.False(summary.HasMixedUnits);
    }

    [Fact]
    public void PeakConsumption_IsHighestTotalBucketAcrossSelectedMeters()
    {
        var service = CreateDashboardService();
        var filters = new MeterReadingFilters
        {
            StartDate = new DateTime(2026, 9, 1),
            EndDate = new DateTime(2026, 9, 3, 23, 59, 59),
            DateFilter = "daily"
        };

        var data = new List<ConsumptionQueryResult>
        {
            new() { MeterId = 1, MeterName = "A", Unit = "kWh", ReadingDate = "2026-09-01", TotalConsumption = 10 },
            new() { MeterId = 2, MeterName = "B", Unit = "kWh", ReadingDate = "2026-09-01", TotalConsumption = 20 },
            new() { MeterId = 1, MeterName = "A", Unit = "kWh", ReadingDate = "2026-09-02", TotalConsumption = 25 }
        };

        var summary = service.CalculateSummary(data, filters);

        Assert.Equal(30d, summary.PeakUsage);
        Assert.Equal("Highest daily total", summary.PeakPeriodLabel);
    }

    [Fact]
    public void MixedUnits_AreExplicitlyFlaggedInsteadOfSilentlyPresentedAsOneUnit()
    {
        var service = CreateDashboardService();
        var filters = new MeterReadingFilters
        {
            StartDate = new DateTime(2026, 9, 1),
            EndDate = new DateTime(2026, 9, 1, 23, 59, 59),
            DateFilter = "daily"
        };

        var data = new List<ConsumptionQueryResult>
        {
            new() { MeterId = 1, MeterName = "Electricity", Unit = "kWh", ReadingDate = "2026-09-01", TotalConsumption = 10 },
            new() { MeterId = 2, MeterName = "Water", Unit = "m3", ReadingDate = "2026-09-01", TotalConsumption = 4 }
        };

        var summary = service.CalculateSummary(data, filters);

        Assert.True(summary.HasMixedUnits);
        Assert.Equal("mixed", summary.Unit);
    }

    [Fact]
    public void ChartData_CarriesMeterTenantAndUnitMetadataForTooltips()
    {
        var service = CreateDashboardService();
        var data = new List<ConsumptionQueryResult>
        {
            new()
            {
                MeterId = 7,
                MeterName = "Light1",
                Unit = "kWh",
                TenantName = "Arcinfo",
                ReadingDate = "2026-09-01",
                TotalConsumption = 12.5
            }
        };

        var result = service.ProcessChartData(data);

        var dataset = Assert.Single(result.Datasets);
        Assert.Equal(7, dataset.MeterId);
        Assert.Equal("Light1", dataset.MeterName);
        Assert.Equal("Arcinfo", dataset.TenantName);
        Assert.Equal("kWh", dataset.Unit);
    }

    [Fact]
    public void ChartData_MissingMeterBucket_RemainsNullInsteadOfFakeZero()
    {
        var service = CreateDashboardService();
        var data = new List<ConsumptionQueryResult>
        {
            new() { MeterId = 1, MeterName = "Electricity A", Unit = "kWh", ReadingDate = "2026-09-01", TotalConsumption = 10 },
            new() { MeterId = 1, MeterName = "Electricity A", Unit = "kWh", ReadingDate = "2026-09-02", TotalConsumption = 12 },
            new() { MeterId = 2, MeterName = "Electricity B", Unit = "kWh", ReadingDate = "2026-09-01", TotalConsumption = 8 }
        };

        var result = service.ProcessChartData(data);
        var meterB = Assert.Single(result.Datasets, d => d.MeterName == "Electricity B");

        Assert.Equal(2, meterB.Data.Count);
        Assert.Equal(8d, meterB.Data[0]);
        Assert.Null(meterB.Data[1]);
    }

    [Fact]
    public void DashboardView_UsesCompactEnterpriseWorkbenchWithoutMarketingHero()
    {
        var view = ReadSource("Views", "Home", "Index.cshtml");
        var css = ReadSource("wwwroot", "css", "dashboard-modern.css");

        Assert.Contains("<h1>Analytics</h1>", view);
        Assert.DoesNotContain("POWORKS · BUILDING ANALYTICS", view);
        Assert.DoesNotContain("Operational analytics", view);
        Assert.DoesNotContain("Analyse consumption and building measurements", view);

        Assert.Contains("analytics-commandbar", view);
        Assert.Contains("advancedAnalyticsPanel", view);
        Assert.Contains("analytics-kpi-strip", view);
        Assert.Contains("analytics-workbench", view);
        Assert.Contains("analytics-chart-panel", view);
        Assert.Contains("analytics-side-panel", view);

        Assert.Contains("measurementMetric", view);
        Assert.Contains("scopeMode", view);
        Assert.Contains("aggregationMode", view);
        Assert.Contains("Aggregate selection", view);
        Assert.Contains("Break down by tenant", view);
        Assert.Contains("Individual meters", view);
        Assert.Contains("comparisonResolvedRange", view);
        Assert.Contains("Custom start", view);
        Assert.DoesNotContain("compareEndDate", view);
        Assert.Contains("coverageBadge", view);
        Assert.Contains("meterCompatibilityHint", view);
        Assert.Contains("id=\"kpi@(i)Label\"", view);
        Assert.Contains("id=\"kpi@(i)Value\"", view);
        Assert.Contains("id=\"kpi@(i)Detail\"", view);
        Assert.Contains("topConsumersList", view);
        Assert.Contains("exportChart", view);
        Assert.Contains("exportCsv", view);
        Assert.Contains("energy-chart-core.js", view);

        Assert.Contains(".analytics-commandbar", css);
        Assert.Contains(".analytics-kpi-strip", css);
        Assert.Contains(".analytics-workbench", css);
        Assert.Contains(".analytics-side-panel", css);
        Assert.Contains("height:clamp(350px,49vh,495px)", css);
        Assert.Contains(".dashboard-meter-item.is-incompatible", css);
        Assert.Contains(".dashboard-quality-pill", css);
        Assert.Contains(".dashboard-kpi-compare", css);
        Assert.Contains(".dashboard-ranking-row.is-actionable", css);
        Assert.Contains(":focus-visible", css);
        Assert.DoesNotContain("linear-gradient(115deg", css);
    }

    [Fact]
    public void DashboardChart_UsesOnlyRealBucketHitTargetsForTooltips()
    {
        var script = ReadSource("wwwroot", "js", "energy-dashboard.js");
        var core = ReadSource("wwwroot", "js", "energy-chart-core.js");

        Assert.Contains("series.bullets.push(function (bulletRoot, _series, dataItem)", script);
        Assert.Contains("hit.events.on('pointerover'", script);
        Assert.Contains("hit.events.on('pointerout'", script);
        Assert.Contains("fillOpacity: .001", script);
        Assert.Contains("hit.states.create('hover'", script);
        Assert.Contains("poworks-chart-hover-tooltip", script);
        Assert.Contains("chartCore.validateData(data)", script);
        Assert.DoesNotContain("globalpointermove", script);
        Assert.DoesNotContain("distanceToSegment", script);
        Assert.DoesNotContain("snapToSeries", script);
        Assert.DoesNotContain("cursor.events.on('cursormoved'", script);
        Assert.Contains("seriesKey", core);
        Assert.Contains("buildComparisonPairs", core);
        Assert.Contains("minY", core);
    }

    [Fact]
    public void Dashboard_ImplementsMeetingAggregationComparisonAndClientFreedom()
    {
        var script = ReadSource("wwwroot", "js", "energy-dashboard.js");
        var view = ReadSource("Views", "Home", "Index.cshtml");
        var controller = ReadSource("Controllers", "DashboardApiController.cs");
        var dataService = ReadSource("Services", "DashboardDataService.cs");

        // Equal-duration comparison: custom mode selects only its starting date.
        Assert.Contains("computeComparisonRange", script);
        Assert.Contains("durationDays", script);
        Assert.Contains("DashboardComparisonPeriodResolver.Resolve", controller);
        Assert.Contains("request.CompareStartDate.Value", controller);
        Assert.Contains("resolvedComparison.EndDate", controller);
        Assert.Contains("SeriesKeys = current.ChartData.Datasets", controller);
        Assert.Contains("dataset.SeriesKey", controller);
        Assert.DoesNotContain("id=\"compareEndDate\"", view);

        // Client can choose global aggregate, tenant breakdown or individual meters.
        Assert.Contains("value=\"aggregate\"", view);
        Assert.Contains("value=\"tenant\"", view);
        Assert.Contains("value=\"meter\"", view);
        Assert.Contains("measurementMetric", view);
        Assert.Contains("aggregationMode", view);
        Assert.Contains("maxCurves", view);
        Assert.Contains("meterLimit", view);

        // Granularity remains independent of the chosen primary date range.
        Assert.Contains("tabHourly", view);
        Assert.Contains("tabDaily", view);
        Assert.Contains("tabMonthly", view);
        Assert.Contains("tabYearly", view);
        Assert.Contains("switchGranularity", script);
        Assert.DoesNotContain("startDate.value = formatDate", script);

        // All measurements remain discoverable; compatibility is handled analytically.
        Assert.DoesNotContain("supportedEnergyUnit", dataService);
        Assert.Contains("compatibleMetrics", controller);
        Assert.Contains("Other measurements", script);

        // User choices are persisted instead of being forced on every visit.
        Assert.Contains("poworks.dashboard.analytics.v3", script);
        Assert.Contains("localStorage.setItem", script);

        // Export is useful outside PoWorks and spreadsheet cells are protected
        // against formula injection from configurable labels/names.
        Assert.Contains("function exportCsv()", script);
        Assert.Contains("text/csv;charset=utf-8", script);
        Assert.Contains("function csvCell(value)", script);
        Assert.Contains("typeof value === 'number' && Number.isFinite(value)", script);
        Assert.Contains("/^[\\t\\r\\n ]*[=+\\-@]/", script);
        Assert.Contains("'\\uFEFF'", script);
        Assert.Contains("lastAnalyticsPayload = null", script);
        Assert.Contains("Use automatic scope", script);

        // Rankings are navigation, not decorative lists.
        Assert.Contains("function drillIntoRanking(key)", script);
        Assert.Contains("data-ranking-key", script);
        Assert.Contains("tenantToken === 'facility'", script);
        Assert.Contains("event.key !== 'Enter' && event.key !== ' '", script);
    }

    private static DashboardDataService CreateDashboardService() =>
        new(
            null!,
            null!,
            null!,
            NullLogger<DashboardDataService>.Instance);

    private static string ReadSource(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "PoWorks Rework.sln")))
            {
                var path = Path.Combine(
                    new[] { directory.FullName, "PoWorks Rework" }
                        .Concat(parts)
                        .ToArray());

                Assert.True(File.Exists(path), $"Source file not found: {path}");
                return File.ReadAllText(path);
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root.");
    }
}
