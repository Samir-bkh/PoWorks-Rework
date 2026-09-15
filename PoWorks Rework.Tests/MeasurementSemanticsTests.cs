using PoWorks_Rework.Services;
using Xunit;

namespace PoWorks_Rework.Tests;

public class MeasurementSemanticsTests
{
    [Theory]
    [InlineData("kWh", "energy")]
    [InlineData("MWh", "energy")]
    [InlineData("kW", "power")]
    [InlineData("m³", "volume")]
    [InlineData("m3/h", "flow")]
    [InlineData("°C", "temperature")]
    [InlineData("°F", "temperature")]
    [InlineData("kPa", "pressure")]
    [InlineData("%", "percentage")]
    [InlineData("rpm", "raw")]
    public void Units_AreClassifiedIntoPhysicalFamilies(string unit, string expected)
    {
        Assert.Equal(expected, MeasurementSemantics.GetFamilyForUnit(unit));
    }

    [Fact]
    public void PowerSources_AreAvailableAsPowerAndDerivedEnergy()
    {
        var metrics = MeasurementSemantics.GetCompatibleMetrics("kW");

        Assert.Contains("power", metrics);
        Assert.Contains("energy", metrics);
    }

    [Fact]
    public void FlowSources_AreAvailableAsFlowAndDerivedVolume()
    {
        var metrics = MeasurementSemantics.GetCompatibleMetrics("L/min");

        Assert.Contains("flow", metrics);
        Assert.Contains("volume", metrics);
    }

    [Theory]
    [InlineData("temperature", "sum", "average")]
    [InlineData("pressure", "sum", "average")]
    [InlineData("percentage", "sum", "average")]
    [InlineData("energy", "sum", "sum")]
    [InlineData("power", "auto", "sum")]
    public void InvalidAggregations_FallBackToEngineeringDefault(
        string metric,
        string requested,
        string expected)
    {
        Assert.Equal(
            expected,
            MeasurementSemantics.ResolveAggregation(metric, requested));
    }

    [Fact]
    public void InstantValues_AreNormalizedToCanonicalUnits()
    {
        Assert.Equal(1d, MeasurementSemantics.ConvertInstantValue("power", "1000W".Replace("1000", ""), 1000), 6);
        Assert.Equal(3.6d, MeasurementSemantics.ConvertInstantValue("flow", "L/s", 1), 6);
        Assert.Equal(20d, MeasurementSemantics.ConvertInstantValue("temperature", "°F", 68), 6);
        Assert.Equal(1d, MeasurementSemantics.ConvertInstantValue("pressure", "kPa", 100), 6);
    }

    [Fact]
    public void EnergyCounter_Reset_RemainsPositiveAndNormalizedToKwh()
    {
        var start = new DateTime(2026, 9, 15, 10, 0, 0);

        var normal = MeasurementSemantics.CalculateIntervalQuantity(
            "energy", "Wh", 1000m, start, 2500m, start.AddHours(1));
        var reset = MeasurementSemantics.CalculateIntervalQuantity(
            "energy", "kWh", 120m, start, 5m, start.AddHours(1));

        Assert.Equal(1.5m, normal);
        Assert.Equal(5m, reset);
    }

    [Fact]
    public void Power_IsIntegratedToEnergyUsingPreviousDemand()
    {
        var start = new DateTime(2026, 9, 15, 10, 0, 0);

        var energy = MeasurementSemantics.CalculateIntervalQuantity(
            "energy", "kW", 12m, start, 20m, start.AddMinutes(30));

        Assert.Equal(6m, energy);
    }

    [Fact]
    public void Flow_IsIntegratedToCubicMeters()
    {
        var start = new DateTime(2026, 9, 15, 10, 0, 0);

        var volume = MeasurementSemantics.CalculateIntervalQuantity(
            "volume", "L/min", 60m, start, 60m, start.AddHours(1));

        Assert.Equal(3.6m, volume);
    }

    [Fact]
    public void Temperature_DoesNotOfferPhysicallyMeaninglessSum()
    {
        var definition = MeasurementSemantics.GetDefinition("temperature");

        Assert.Equal("average", definition.DefaultAggregation);
        Assert.DoesNotContain("sum", definition.AllowedAggregations);
        Assert.Equal("°C", definition.CanonicalUnit);
    }

    [Fact]
    public void RawMeasurements_KeepTheirOriginalUnit()
    {
        Assert.Equal(
            "rpm",
            MeasurementSemantics.GetCanonicalUnit("raw", "rpm"));
    }
}
