using PoWorks_Rework.Models;
using PoWorks_Rework.Services;
using Xunit;

namespace PoWorks_Rework.Tests;

public class DashboardAnalyticsEngineTests
{
    [Fact]
    public void BuildingAggregate_SumsAllEnergyMetersIntoOneSeries()
    {
        var query = BaseQuery("energy", "aggregate", "sum");
        var rows = new List<MeasurementBucketResult>
        {
            Row(1, "A", 10, "Tenant A", "2026-09-15", 12),
            Row(2, "B", 20, "Tenant B", "2026-09-15", 8),
            Row(1, "A", 10, "Tenant A", "2026-09-16", 5),
            Row(2, "B", 20, "Tenant B", "2026-09-16", 15)
        };

        var result = DashboardAnalyticsEngine.Build(query, rows);

        var dataset = Assert.Single(result.ChartData.Datasets);
        Assert.Equal("aggregate", dataset.SeriesKey);
        Assert.True(dataset.IsAggregate);
        Assert.Equal(2, dataset.SourceCount);
        Assert.Equal(new double?[] { 20, 20 }, dataset.Data);
        Assert.Equal(40d, result.Summary.Kpis.Single(k => k.Key == "total").Value);
    }

    [Fact]
    public void SelectedTenant_AggregatesOnlyRowsPassedForThatTenant()
    {
        var query = BaseQuery("energy", "aggregate", "sum");
        query.TenantId = 10;

        var rows = new List<MeasurementBucketResult>
        {
            Row(1, "Light", 10, "Arcinfo", "2026-09-15", 12),
            Row(2, "HVAC", 10, "Arcinfo", "2026-09-15", 8)
        };

        var result = DashboardAnalyticsEngine.Build(query, rows);

        var dataset = Assert.Single(result.ChartData.Datasets);
        Assert.Contains("Arcinfo", dataset.Label);
        Assert.Equal(20d, dataset.Data[0]);
    }

    [Fact]
    public void ByTenant_CreatesTenantAndFacilitySeries()
    {
        var query = BaseQuery("energy", "tenant", "sum");
        var rows = new List<MeasurementBucketResult>
        {
            Row(1, "A", 10, "Tenant A", "2026-09-15", 10),
            Row(2, "B", 20, "Tenant B", "2026-09-15", 20),
            Row(3, "Hallway", null, "", "2026-09-15", 5)
        };

        var result = DashboardAnalyticsEngine.Build(query, rows);

        Assert.Equal(3, result.ChartData.Datasets.Count);
        Assert.Contains(result.ChartData.Datasets, d => d.SeriesKey == "tenant:10");
        Assert.Contains(result.ChartData.Datasets, d => d.SeriesKey == "tenant:20");
        Assert.Contains(result.ChartData.Datasets, d => d.SeriesKey == "tenant:facility");
        Assert.Contains(result.ChartData.Datasets, d => d.Label == "Facility / unassigned");
    }

    [Fact]
    public void TemperatureAutoAggregation_UsesAverageNotSum()
    {
        var query = BaseQuery("temperature", "aggregate", "auto");
        var rows = new List<MeasurementBucketResult>
        {
            StateRow(1, "Room A", 10, "Tenant A", "2026-09-15 10:00", 20, "°C"),
            StateRow(2, "Room B", 10, "Tenant A", "2026-09-15 10:00", 24, "°C")
        };

        var result = DashboardAnalyticsEngine.Build(query, rows);

        var dataset = Assert.Single(result.ChartData.Datasets);
        Assert.Equal(22d, dataset.Data[0]);
        Assert.Equal("average", result.Metadata.Aggregation);
        Assert.Equal(22d, result.Summary.Kpis.Single(k => k.Key == "average").Value);
    }

    [Fact]
    public void IndividualMeters_PreserveMissingBucketsAsNull()
    {
        var query = BaseQuery("energy", "meter", "sum");
        var rows = new List<MeasurementBucketResult>
        {
            Row(1, "A", 10, "Tenant A", "2026-09-15", 10),
            Row(1, "A", 10, "Tenant A", "2026-09-16", 12),
            Row(2, "B", 10, "Tenant A", "2026-09-15", 8)
        };

        var result = DashboardAnalyticsEngine.Build(query, rows);
        var meterB = result.ChartData.Datasets.Single(d => d.MeterId == 2);

        Assert.Equal(new double?[] { 8, null }, meterB.Data);
    }

    [Fact]
    public void SeriesLimit_IsAppliedOnlyToBreakdownAndReported()
    {
        var query = BaseQuery("energy", "meter", "sum");
        query.MaxSeries = 2;

        var rows = new List<MeasurementBucketResult>
        {
            Row(1, "A", 10, "T", "2026-09-15", 100),
            Row(2, "B", 10, "T", "2026-09-15", 50),
            Row(3, "C", 10, "T", "2026-09-15", 10)
        };

        var result = DashboardAnalyticsEngine.Build(query, rows);

        Assert.Equal(2, result.ChartData.Datasets.Count);
        Assert.Equal(1, result.Metadata.OmittedSeries);
        Assert.Contains(result.ChartData.Datasets, d => d.MeterId == 1);
        Assert.Contains(result.ChartData.Datasets, d => d.MeterId == 2);
    }

    [Fact]
    public void AllTenantAggregate_RankingIsTenantBased()
    {
        var query = BaseQuery("energy", "aggregate", "sum");
        query.RankingLimit = 5;

        var rows = new List<MeasurementBucketResult>
        {
            Row(1, "A1", 10, "Tenant A", "2026-09-15", 20),
            Row(2, "A2", 10, "Tenant A", "2026-09-15", 30),
            Row(3, "B1", 20, "Tenant B", "2026-09-15", 40)
        };

        var result = DashboardAnalyticsEngine.Build(query, rows);

        Assert.Equal("Tenant A", result.Ranking[0].Name);
        Assert.Equal(50d, result.Ranking[0].Value);
        Assert.Equal(2, result.Ranking[0].MeterCount);
        Assert.Equal("Tenant B", result.Ranking[1].Name);
    }

    [Fact]
    public void SelectedTenantAggregate_RankingDrillsDownToMeters()
    {
        var query = BaseQuery("energy", "aggregate", "sum");
        query.TenantId = 10;

        var rows = new List<MeasurementBucketResult>
        {
            Row(1, "Light", 10, "Tenant A", "2026-09-15", 20),
            Row(2, "HVAC", 10, "Tenant A", "2026-09-15", 30)
        };

        var result = DashboardAnalyticsEngine.Build(query, rows);

        Assert.Equal("HVAC", result.Ranking[0].Name);
        Assert.Equal(30d, result.Ranking[0].Value);
        Assert.Equal(1, result.Ranking[0].MeterCount);
    }

    [Fact]
    public void RawMixedUnits_AreRejectedInsteadOfSharingOneAxis()
    {
        var query = BaseQuery("raw", "meter", "average");
        var rows = new List<MeasurementBucketResult>
        {
            StateRow(1, "RPM", 10, "Tenant", "2026-09-15", 1500, "rpm"),
            StateRow(2, "Voltage", 10, "Tenant", "2026-09-15", 230, "V")
        };

        var error = Assert.Throws<InvalidOperationException>(
            () => DashboardAnalyticsEngine.Build(query, rows));

        Assert.Contains("different units", error.Message);
    }

    [Fact]
    public void CoverageReportsMissingMeterBuckets()
    {
        var query = BaseQuery("temperature", "aggregate", "average");
        var rows = new List<MeasurementBucketResult>
        {
            StateRow(1, "A", 10, "T", "2026-09-15", 20, "°C"),
            StateRow(1, "A", 10, "T", "2026-09-16", 21, "°C"),
            StateRow(2, "B", 10, "T", "2026-09-15", 22, "°C")
        };

        var result = DashboardAnalyticsEngine.Build(query, rows);

        Assert.Equal(75d, result.Summary.CoveragePercent, 6);
    }

    [Fact]
    public void QuantityAndStateMetricsExposeDifferentKpis()
    {
        var energy = DashboardAnalyticsEngine.Build(
            BaseQuery("energy", "aggregate", "sum"),
            new[] { Row(1, "A", 10, "T", "2026-09-15", 12) });

        var temperature = DashboardAnalyticsEngine.Build(
            BaseQuery("temperature", "aggregate", "average"),
            new[] { StateRow(1, "T1", 10, "T", "2026-09-15", 22, "°C") });

        Assert.Contains(energy.Summary.Kpis, k => k.Key == "total");
        Assert.Contains(energy.Summary.Kpis, k => k.Key == "dailyAverage");
        Assert.Contains(temperature.Summary.Kpis, k => k.Key == "average");
        Assert.Contains(temperature.Summary.Kpis, k => k.Key == "minimum");
        Assert.Contains(temperature.Summary.Kpis, k => k.Key == "maximum");
    }

    private static DashboardAnalyticsQuery BaseQuery(
        string metric,
        string scope,
        string aggregation) => new()
        {
            Metric = metric,
            ScopeMode = scope,
            Aggregation = aggregation,
            DateFilter = "daily",
            StartDate = new DateTime(2026, 9, 15),
            EndDate = new DateTime(2026, 9, 16, 23, 59, 59),
            MaxSeries = 10,
            RankingLimit = 5
        };

    private static MeasurementBucketResult Row(
        int meterId,
        string name,
        int? tenantId,
        string tenant,
        string date,
        double value) => new()
        {
            MeterId = meterId,
            MeterName = name,
            TenantId = tenantId,
            TenantName = tenant,
            SourceUnit = "kWh",
            CanonicalUnit = "kWh",
            ReadingDate = date,
            Value = value
        };

    private static MeasurementBucketResult StateRow(
        int meterId,
        string name,
        int? tenantId,
        string tenant,
        string date,
        double value,
        string unit) => new()
        {
            MeterId = meterId,
            MeterName = name,
            TenantId = tenantId,
            TenantName = tenant,
            SourceUnit = unit,
            CanonicalUnit = unit,
            ReadingDate = date,
            Value = value
        };
}
