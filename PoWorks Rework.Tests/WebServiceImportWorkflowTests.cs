using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using PoWorks_Rework.Controllers;
using PoWorks_Rework.Services;
using Xunit;

namespace PoWorks_Rework.Tests;

public class WebServiceImportWorkflowTests
{
    [Theory]
    [InlineData("System.LocalHost.User")]
    [InlineData("system.localhost.resource.memoryusage")]
    [InlineData("SYSTEM/LOCALHOST/RESOURCE/RAM")]
    [InlineData("System\\LocalHost\\User")]
    [InlineData(" System ")]
    public void ImportPolicy_RecognizesSystemVariables(string variableName)
    {
        Assert.True(WebServiceImportPolicy.IsSystemVariable(variableName));
    }

    [Theory]
    [InlineData("BatimentA.Locataire1.Light1")]
    [InlineData("Systematic.Power")]
    [InlineData("MySystem.Value")]
    [InlineData("")]
    public void ImportPolicy_DoesNotMisclassifyNormalVariables(string variableName)
    {
        Assert.False(WebServiceImportPolicy.IsSystemVariable(variableName));
    }

    [Fact]
    public void ExistingMeter_BlankImportedUnit_DoesNotEraseConfiguredUnit()
    {
        Assert.False(WebServiceImportPolicy.ShouldUpdateExistingUnit("kWh", ""));
        Assert.False(WebServiceImportPolicy.ShouldUpdateExistingUnit("kWh", "   "));
        Assert.False(WebServiceImportPolicy.ShouldUpdateExistingUnit("kWh", null));
    }

    [Fact]
    public void ExistingMeter_ExplicitDifferentUnit_IsUpdated()
    {
        Assert.True(WebServiceImportPolicy.ShouldUpdateExistingUnit("", "kWh"));
        Assert.True(WebServiceImportPolicy.ShouldUpdateExistingUnit("Wh", "kWh"));
    }

    [Fact]
    public void ExistingMeter_SameUnit_IsNotCountedAsUpdate()
    {
        Assert.False(WebServiceImportPolicy.ShouldUpdateExistingUnit("kWh", " kWh "));
        Assert.False(WebServiceImportPolicy.ShouldUpdateExistingUnit("KWH", "kwh"));
    }

    [Theory]
    [InlineData(" kWh ", "kWh")]
    [InlineData(" °C ", "°C")]
    [InlineData(null, "")]
    public void UnitNormalization_IsDeterministic(string? source, string expected)
    {
        Assert.Equal(expected, WebServiceImportPolicy.NormalizeUnit(source));
    }

    [Theory]
    [InlineData("sub", "sub")]
    [InlineData("SUB", "sub")]
    [InlineData("main", "main")]
    [InlineData("", "main")]
    [InlineData(null, "main")]
    public void MeterTypeNormalization_IsSafe(string? source, string expected)
    {
        Assert.Equal(expected, WebServiceImportPolicy.NormalizeMeterType(source));
    }

    [Fact]
    public void BrowseParser_ExcludesPcVueSystemBranch_WhenOptionIsDisabled()
    {
        var service = new VariableBrowseParsingService(NullLogger<VariableBrowseParsingService>.Instance);
        using var document = JsonDocument.Parse("""
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
              "branches": ["BatimentA", "Locataire1"],
              "variableType": "Register",
              "VariableName": "Light1",
              "IsReadOnly": false,
              "IsLeaf": true
            }
          ]
        }
        """);

        var result = service.ParseBrowseVariablesResponse(document.RootElement.Clone(), includeSystemVariables: false);

        Assert.True(result.Success);
        Assert.Single(result.Variables);
        Assert.Equal("BatimentA.Locataire1.Light1", result.Variables[0].FullPath);
        Assert.DoesNotContain(result.Variables, v => v.FullPath.StartsWith("System.", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void BrowseParser_IncludesPcVueSystemBranch_WhenOptionIsEnabled()
    {
        var service = new VariableBrowseParsingService(NullLogger<VariableBrowseParsingService>.Instance);
        using var document = JsonDocument.Parse("""
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
              "branches": ["BatimentA", "Locataire1"],
              "variableType": "Register",
              "VariableName": "Light1",
              "IsReadOnly": false,
              "IsLeaf": true
            }
          ]
        }
        """);

        var result = service.ParseBrowseVariablesResponse(document.RootElement.Clone(), includeSystemVariables: true);

        Assert.True(result.Success);
        Assert.Equal(2, result.TotalCount);
        Assert.Contains(result.Variables, v => v.FullPath.Equals("system.localhost.User", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void UpsertController_IsCompanyScoped_AndUpdatesOnlyUnitForExistingMeters()
    {
        var source = ReadSource("Controllers", "WebServicesMeterImportV2Controller.cs");

        Assert.Contains(@"""Name"" = @name AND ""CompanyId"" = @companyId", source);
        Assert.Contains(@"SET ""Unit"" = @unit", source);
        Assert.DoesNotContain(@"UPDATE ""Meters""\n                                    SET ""TenantID""", source);
        Assert.Contains("ShouldUpdateExistingUnit", source);
        Assert.Contains("filteredSystemCount", source);
        Assert.Contains("IsSystemVariable", source);
    }

    [Fact]
    public void WebServiceImportEnhancement_ProvidesBulkUnitsAndSafeUpsertEndpoint()
    {
        var script = ReadSource("wwwroot", "js", "import", "webservices_import_v2.js");
        var layout = ReadSource("Views", "Shared", "_Layout.cshtml");

        Assert.Contains("webServiceBulkUnit", script);
        Assert.Contains("Apply to selected", script);
        Assert.Contains("Fill empty selected", script);
        Assert.Contains("UpsertWebServiceVariablesWithTrends", script);
        Assert.Contains("includeSystemVariables", script);
        Assert.Contains("isSystemVariable", script);
        Assert.Contains("existing PoWorks unit will never be erased", script);
        Assert.Contains("webservices_import_v2.js", layout);
    }

    private static string ReadSource(params string[] parts)
    {
        var root = FindRepositoryRoot();
        var path = Path.Combine(new[] { root, "PoWorks Rework" }.Concat(parts).ToArray());
        return File.ReadAllText(path);
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current != null)
        {
            if (Directory.Exists(Path.Combine(current.FullName, "PoWorks Rework")) &&
                Directory.Exists(Path.Combine(current.FullName, "PoWorks Rework.Tests")))
            {
                return current.FullName;
            }
            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root.");
    }
}
