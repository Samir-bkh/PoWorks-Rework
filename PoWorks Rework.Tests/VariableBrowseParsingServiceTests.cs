using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using PoWorks_Rework.Services;
using Xunit;

namespace PoWorks_Rework.Tests;

public class VariableBrowseParsingServiceTests
{
    [Fact]
    public void ParseBrowseVariablesResponse_FiltersAllSystemRootsWhenDisabled()
    {
        var service = new VariableBrowseParsingService(NullLogger<VariableBrowseParsingService>.Instance);
        using var json = JsonDocument.Parse("""
        {
          "variableCollections": [
            {
              "branches": ["system", "localhost"],
              "variableType": "Text",
              "VariableName": "User",
              "IsReadOnly": true,
              "IsLeaf": true
            },
            {
              "Branches": ["$System", "HDS"],
              "VariableType": "Register",
              "variableName": "Status",
              "isReadOnly": false,
              "isLeaf": true
            },
            {
              "branches": ["BatimentA", "Locataire1"],
              "variableType": "Real",
              "VariableName": "Light1",
              "IsReadOnly": false,
              "IsLeaf": true
            }
          ]
        }
        """);

        var result = service.ParseBrowseVariablesResponse(json.RootElement, includeSystemVariables: false);

        Assert.True(result.Success);
        Assert.Equal(1, result.TotalCount);
        Assert.Equal(2, result.FilteredSystemVariables);
        Assert.Single(result.Variables);
        Assert.Equal("BatimentA.Locataire1.Light1", result.Variables[0].FullPath);
        Assert.False(result.Variables[0].IsSystemVariable);
    }

    [Fact]
    public void ParseBrowseVariablesResponse_IncludesSystemRootsOnlyWhenExplicitlyEnabled()
    {
        var service = new VariableBrowseParsingService(NullLogger<VariableBrowseParsingService>.Instance);
        using var json = JsonDocument.Parse("""
        {
          "variableCollections": [
            { "branches": ["System", "LocalHost"], "variableType": "Text", "VariableName": "User", "IsLeaf": true },
            { "branches": ["Building"], "variableType": "Real", "VariableName": "Temperature", "IsLeaf": true }
          ]
        }
        """);

        var result = service.ParseBrowseVariablesResponse(json.RootElement, includeSystemVariables: true);

        Assert.True(result.Success);
        Assert.Equal(2, result.TotalCount);
        Assert.Equal(0, result.FilteredSystemVariables);
        Assert.Contains(result.Variables, variable => variable.IsSystemVariable && variable.FullPath == "System.LocalHost.User");
        Assert.Contains(result.Variables, variable => !variable.IsSystemVariable && variable.FullPath == "Building.Temperature");
    }

    [Theory]
    [InlineData("System.LocalHost.User")]
    [InlineData("system/hds/status")]
    [InlineData("$System.HDS.Archive")]
    [InlineData("_SYSTEM\\Network\\State")]
    [InlineData("Plant.System.Status")]
    public void IsSystemVariablePath_RecognizesSystemSegments(string path)
    {
        Assert.True(VariableBrowseParsingService.IsSystemVariablePath(path));
    }

    [Theory]
    [InlineData("BatimentA.Locataire1.Light1")]
    [InlineData("Production.Systematic.Counter")]
    [InlineData("BuildingA.HVAC.Temperature")]
    public void IsSystemVariablePath_DoesNotMatchOrdinaryNames(string path)
    {
        Assert.False(VariableBrowseParsingService.IsSystemVariablePath(path));
    }

    [Fact]
    public void ParseBrowseVariablesResponse_DoesNotDuplicateAlreadyQualifiedName()
    {
        var service = new VariableBrowseParsingService(NullLogger<VariableBrowseParsingService>.Instance);
        using var json = JsonDocument.Parse("""
        {
          "variableCollections": [
            {
              "branches": ["BatimentA", "Locataire1"],
              "variableType": "Real",
              "VariableName": "BatimentA.Locataire1.Light1",
              "IsLeaf": true
            }
          ]
        }
        """);

        var result = service.ParseBrowseVariablesResponse(json.RootElement);

        Assert.Single(result.Variables);
        Assert.Equal("BatimentA.Locataire1.Light1", result.Variables[0].FullPath);
    }
}
