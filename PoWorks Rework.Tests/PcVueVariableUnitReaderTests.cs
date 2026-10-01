using System.Text.Json;
using PoWorks_Rework.Services;
using Xunit;

namespace PoWorks_Rework.Tests;

public sealed class PcVueVariableUnitReaderTests
{
    [Fact]
    public async Task ReadsConfiguredUnitEvenWhenCurrentValueIsBadOrMissing()
    {
        var names = new[] { "Building.ExternalTemp", "Building.Power.kW", "Building.Info" };
        var reader = new PcVueVariableUnitReader((batch, _) =>
        {
            Assert.Equal(names, batch);
            return Task.FromResult("""
            {
              "Building.ExternalTemp": {
                "result": { "code": { "value": 1, "label": "S_Ok" } },
                "value": null, "quality": "Bad", "QualityValue": 0,
                "properties": ["°C"]
              },
              "Building.Power.kW": {
                "result": { "code": { "value": 1, "label": "S_Ok" } },
                "value": 1.6, "quality": "Good", "QualityValue": 192,
                "properties": [" kW "]
              },
              "Building.Info": {
                "result": { "code": { "value": 1, "label": "S_Ok" } },
                "value": "text", "quality": "Good", "properties": [""]
              }
            }
            """);
        });

        var result = await reader.ReadAsync(names);

        Assert.True(result.Complete);
        Assert.Equal("°C", result.Units["Building.ExternalTemp"]);
        Assert.Equal("kW", result.Units["Building.Power.kW"]);
        Assert.False(result.Units.ContainsKey("Building.Info"));
    }

    [Fact]
    public async Task IsolatesUnknownVariableAndKeepsUnitsForOtherNames()
    {
        var requests = new List<string[]>();
        var reader = new PcVueVariableUnitReader((names, _) =>
        {
            requests.Add(names);
            if (names.Contains("Legacy.Unknown"))
                return Task.FromResult("""[{"code":{"value":-1,"label":"E_UnknownVariable"}}]""");

            var payload = names.ToDictionary(
                name => name,
                _ => new { result = new { code = new { value = 1 } }, properties = new[] { "%" } });
            return Task.FromResult(JsonSerializer.Serialize(payload));
        });

        var result = await reader.ReadAsync(new[] {
            "Building.A", "Legacy.Unknown", "Building.B", "Building.C" });

        Assert.False(result.Complete);
        Assert.Equal(3, result.Units.Count);
        Assert.All(result.Units.Values, value => Assert.Equal("%", value));
        Assert.Contains(requests, batch => batch.SequenceEqual(new[] { "Legacy.Unknown" }));
    }

    [Fact]
    public async Task UsesBoundedBatchesAndRetainsCompletedBatchOnFailure()
    {
        var requests = new List<string[]>();
        var reader = new PcVueVariableUnitReader((names, _) =>
        {
            requests.Add(names);
            if (requests.Count == 2) throw new HttpRequestException("unavailable");
            var payload = names.ToDictionary(
                name => name,
                _ => new { result = new { code = new { value = 1 } }, properties = new[] { "kW" } });
            return Task.FromResult(JsonSerializer.Serialize(payload));
        });

        var result = await reader.ReadAsync(Enumerable.Range(0, 81).Select(i => $"Building.Var{i}"));

        Assert.False(result.Complete);
        Assert.Equal(40, result.Units.Count);
        Assert.Equal(2, requests.Count);
        Assert.All(requests, batch => Assert.True(batch.Length <= 40));
    }

    [Fact]
    public async Task IgnoresUnsuccessfulPropertiesRatherThanUsingStaleUnit()
    {
        var reader = new PcVueVariableUnitReader((_, _) => Task.FromResult("""
        {
          "Building.A": {
            "result": { "code": { "value": -1, "label": "E_UnknownVariable" } },
            "properties": ["kWh"]
          }
        }
        """));

        var result = await reader.ReadAsync(new[] { "Building.A" });

        Assert.True(result.Complete);
        Assert.Empty(result.Units);
    }
}
