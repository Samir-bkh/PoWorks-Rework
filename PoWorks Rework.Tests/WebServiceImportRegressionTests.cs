using Xunit;

namespace PoWorks_Rework.Tests;

public class WebServiceImportRegressionTests
{
    [Fact]
    public void ExistingMeterReimport_PerformsRealMetadataUpdateWithoutOverwritingBusinessOwnership()
    {
        var controller = ReadSource("Controllers", "WebServicesImportController.cs");

        Assert.Contains("UPDATE \"\"Meters\"\"", controller);
        Assert.Contains("\"\"Unit\"\" = @unit", controller);
        Assert.Contains("\"\"Type\"\" = @type", controller);
        Assert.Contains("\"\"Active\"\" = @active", controller);
        Assert.Contains("\"\"ParentId\"\" = @parentId", controller);
        Assert.Contains("WebServiceMeterImportRules.ResolveUnit(existing.Unit, variable.Unit)", controller);

        var updateStart = controller.IndexOf("UPDATE \"\"Meters\"\"", StringComparison.Ordinal);
        var updateEnd = controller.IndexOf("update.Parameters.AddWithValue(\"companyId\"", updateStart, StringComparison.Ordinal);
        Assert.True(updateStart >= 0 && updateEnd > updateStart);
        var updateBlock = controller[updateStart..updateEnd];
        Assert.DoesNotContain("TenantID", updateBlock);
        Assert.DoesNotContain("Label", updateBlock);
        Assert.DoesNotContain("LastReading", updateBlock);
    }

    [Fact]
    public void SystemVariableExclusion_IsEnforcedOnBrowseAndAgainOnImport()
    {
        var controller = ReadSource("Controllers", "WebServicesImportController.cs");
        var parser = ReadSource("Services", "VariableBrowseParsingService.cs");
        var script = ReadSource("wwwroot", "js", "import", "webservices_import.js");

        Assert.Contains("ParseBrowseVariablesResponse(jsonData, request.IncludeSystemVariables)", controller);
        Assert.Contains("!request.IncludeSystemVariables", controller);
        Assert.Contains("VariableBrowseParsingService.IsSystemVariablePath(variable.VariableName)", controller);
        Assert.Contains("IsSystemVariablePath", parser);
        Assert.Contains("includeSystemVariables", script);
        Assert.Contains("!includeSystemVariables && isSystemVariablePath(variableName)", script);
    }

    [Fact]
    public void UnitWorkflow_SupportsPcVueEnrichmentAndBulkManualAssignment()
    {
        var controller = ReadSource("Controllers", "WebServicesImportController.cs");
        var script = ReadSource("wwwroot", "js", "import", "webservices_import.js");

        Assert.Contains("ResolveVariableUnits", controller);
        Assert.Contains("/RealTimeData/v2/BulkRead", controller);
        Assert.Contains("new[] { \"VariableName\", \"Unit\" }", controller);
        Assert.Contains("existingUnit", controller);

        Assert.Contains("webServiceBulkUnit", script);
        Assert.Contains("data-ws-action=\"fill-blank-units\"", script);
        Assert.Contains("data-ws-action=\"apply-unit\"", script);
        Assert.Contains("data-ws-action=\"read-units\"", script);
        Assert.Contains("webServiceExistingMode", script);
        Assert.Contains("Update metadata", script);
        Assert.DoesNotContain("Skip existing meters?", script);
    }

    [Fact]
    public void BrowseUsesPcVueBranchPathInsteadOfLegacyIdQueryParameter()
    {
        var controller = ReadSource("Controllers", "WebServicesImportController.cs");

        Assert.Contains("endpoint += \"/\" + string.Join(\"/\", segments)", controller);
        Assert.DoesNotContain("queryParams.Add($\"Id=", controller);
    }

    [Fact]
    public void WebServiceImportJavascript_IsIncludedAndUsesUnifiedImportContract()
    {
        var view = ReadSource("Views", "Import", "Index.cshtml");
        var script = ReadSource("wwwroot", "js", "import", "webservices_import.js");

        Assert.Contains("~/js/import/webservices_import.js", view);
        Assert.Contains("function importWebServiceVariables()", script);
        Assert.Contains("/Import/ImportWebServiceVariablesWithTrends", script);
        Assert.Contains("updateExisting: existingMode !== 'skip'", script);
    }

    private static string ReadSource(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            var solution = Path.Combine(directory.FullName, "PoWorks Rework.sln");
            if (File.Exists(solution))
            {
                var path = Path.Combine(new[] { directory.FullName, "PoWorks Rework" }.Concat(parts).ToArray());
                Assert.True(File.Exists(path), $"Source file not found: {path}");
                return File.ReadAllText(path);
            }
            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root from test output directory.");
    }
}
