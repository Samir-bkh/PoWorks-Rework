using PoWorks_Rework.Services;
using Xunit;

namespace PoWorks_Rework.Tests;

public class WebServiceMeterImportRulesTests
{
    [Theory]
    [InlineData("", "kWh", "kWh")]
    [InlineData("Wh", "kWh", "kWh")]
    [InlineData("bar", "  °C  ", "°C")]
    public void ResolveUnit_NonEmptyIncomingValueWins(string existing, string incoming, string expected)
    {
        Assert.Equal(expected, WebServiceMeterImportRules.ResolveUnit(existing, incoming));
    }

    [Theory]
    [InlineData("kWh", "", "kWh")]
    [InlineData("°C", "   ", "°C")]
    [InlineData("bar", null, "bar")]
    public void ResolveUnit_EmptyIncomingValueNeverErasesExistingUnit(string existing, string? incoming, string expected)
    {
        Assert.Equal(expected, WebServiceMeterImportRules.ResolveUnit(existing, incoming));
    }

    [Theory]
    [InlineData("main", "sub", "sub")]
    [InlineData("sub", "main", "main")]
    [InlineData("sub", "", "sub")]
    [InlineData("invalid", "", "main")]
    public void ResolveMeterType_UsesOnlySupportedHierarchyTypes(string existing, string incoming, string expected)
    {
        Assert.Equal(expected, WebServiceMeterImportRules.ResolveMeterType(existing, incoming));
    }

    [Theory]
    [InlineData("42", 42)]
    [InlineData(" 7 ", 7)]
    [InlineData("0", null)]
    [InlineData("-1", null)]
    [InlineData("abc", null)]
    [InlineData("", null)]
    public void ParseParentId_OnlyAcceptsPositiveIntegerIds(string input, int? expected)
    {
        Assert.Equal(expected, WebServiceMeterImportRules.ParseParentId(input));
    }
}
