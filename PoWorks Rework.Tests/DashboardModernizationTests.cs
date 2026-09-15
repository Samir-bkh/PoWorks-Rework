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
        var meterB = Assert.Single(result.Datasets.Where(d => d.MeterName == "Electricity B"));

        Assert.Equal(2, meterB.Data.Count);
        Assert.Equal(8d, meterB.Data[0]);
        Assert.Null(meterB.Data[1]);
    }

    [Fact]
    public void DashboardView_UsesModernContextKpisFiltersAndEmptyState()
    {
        var view = ReadSource("Views", "Home", "Index.cshtml");
        var css = ReadSource("wwwroot", "css", "dashboard-modern.css");

        Assert.Contains("dashboard-hero", view);
        Assert.Contains("Average per Day", view);
        Assert.Contains("Peak Period Consumption", view);
        Assert.Contains("dashboardTenantContext", view);
        Assert.Contains("chartUnitBadge", view);
        Assert.Contains("chartEmptyState", view);
        Assert.Contains("Previous period (same duration)", view);
        Assert.Contains("Same period last year", view);

        Assert.DoesNotContain("radial-gradient", css);
        Assert.DoesNotContain("linear-gradient(115deg", css);
        Assert.Contains("background: #ffffff", css);
        Assert.Contains("border-left: 4px solid var(--pw-primary)", css);
        Assert.Contains(".dashboard-kpi", css);
        Assert.Contains(".dashboard-ranking-row", css);
    }

    [Fact]
    public void DashboardChart_UsesDeterministicDataPointHitTargets()
    {
        var script = ReadSource("wwwroot", "js", "energy-dashboard.js");
        var core = ReadSource("wwwroot", "js", "energy-chart-core.js");
        var view = ReadSource("Views", "Home", "Index.cshtml");

        Assert.Contains("series.bullets.push((bulletRoot, _series, dataItem)", script);
        Assert.Contains("hit.events.on('pointerover'", script);
        Assert.Contains("hit.events.on('pointerout'", script);
        Assert.Contains("fillOpacity: .001", script);
        Assert.Contains("hit.states.create('hover'", script);
        Assert.Contains("poworks-chart-hover-tooltip", script);
        Assert.Contains("chartCore.validateData(data)", script);
        Assert.DoesNotContain("globalpointermove", script);
        Assert.DoesNotContain("distanceToSegment", script);
        Assert.DoesNotContain("snapToSeries", script);
        Assert.DoesNotContain("min: startTs", script);
        Assert.Contains("getCommonUnit", core);
        Assert.Contains("buildComparisonPairs", core);
        Assert.Contains("energy-chart-core.js", view);
        Assert.Contains("Hover a measured bucket", view);
    }

    [Fact]
    public void Dashboard_PreservesMeetingComparisonAndCurveReadabilityFeatures()
    {
        var script = ReadSource("wwwroot", "js", "energy-dashboard.js");
        var view = ReadSource("Views", "Home", "Index.cshtml");

        Assert.Contains("lastYear", script);
        Assert.Contains("computeCompareRange", script);
        Assert.Contains("applyCurveLimit", script);
        Assert.Contains("Others", script);
        Assert.Contains("tabHourly", view);
        Assert.Contains("tabDaily", view);
        Assert.Contains("tabMonthly", view);
        Assert.Contains("tabYearly", view);
        Assert.Contains("maxCurves", view);
        Assert.Contains("modeComparison", view);
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
