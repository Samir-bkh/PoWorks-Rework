using Xunit;

namespace PoWorks_Rework.Tests;

public class WorkspaceIsolationSourceRegressionTests
{
    [Fact]
    public void Billing_IsExplicitlyWorkspaceScoped()
    {
        var source = ReadSource("Services", "BillingService.cs");

        Assert.Contains("t.\"\"CompanyId\"\" = @companyId", source);
        Assert.Contains("\"\"TenantID\"\" = @tenantId AND \"\"CompanyId\"\" = @companyId", source);
        Assert.Contains("\"\"CompanyId\"\" = @companyId", source);
        Assert.Contains("Tenant does not belong to the current workspace.", source);
        Assert.Contains("\"\"Status\"\", \"\"CompanyId\"\"", source);
        Assert.Contains("cmdBill.Parameters.AddWithValue(\"companyId\", companyId)", source);
    }

    [Fact]
    public void VarexpImport_IsExplicitlyWorkspaceScoped()
    {
        var source = ReadSource("Controllers", "VarexpImportController.cs");

        Assert.Contains("[Authorize(Policy = \"ImportExportAccess\")]", source);
        Assert.Contains("\"\"CompanyId\"\" = @companyId", source);
        Assert.Contains("\"\"CompanyId\"\"", source);
        Assert.Contains("_companyContext.CurrentCompanyId", source);
    }

    [Fact]
    public void WebServiceImport_IsExplicitlyWorkspaceScoped()
    {
        var source = ReadSource("Controllers", "WebServicesImportController.cs");

        Assert.Contains("[Authorize(Policy = \"ImportExportAccess\")]", source);
        Assert.Contains("\"\"CompanyId\"\" = @companyId", source);
        Assert.Contains("cmd.Parameters.AddWithValue(\"companyId\", companyId)", source);
    }

    [Fact]
    public void AutomaticBillPayments_PersistWorkspace()
    {
        var source = ReadSource("Controllers", "BillsController.cs");

        Assert.Contains("\"\"PaymentMethod\"\", \"\"CompanyId\"\"", source);
        Assert.Contains("cmdInsert.Parameters.AddWithValue(\"companyId\", _companyContext.CurrentCompanyId)", source);
    }

    [Fact]
    public void DashboardQueries_AreExplicitlyWorkspaceScoped()
    {
        var source = ReadSource("Services", "DashboardDataService.cs");

        Assert.Contains("m.\"\"CompanyId\"\" = @CompanyId", source);
        Assert.Contains("WHERE \"\"CompanyId\"\" = @CompanyId AND \"\"TenantID\"\" = @TenantId", source);
    }

    [Fact]
    public void MeterDeletion_IsExplicitlyWorkspaceScoped()
    {
        var source = ReadSource("Controllers", "MeterController.cs");

        Assert.Contains("\"\"CompanyId\"\" = @CompanyId", source);
        Assert.Contains("BulkDeleteMeters", source);
    }

    private static string ReadSource(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory != null)
        {
            var solution = Path.Combine(directory.FullName, "PoWorks Rework.sln");
            if (File.Exists(solution))
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

        throw new InvalidOperationException("Could not locate repository root from test output directory.");
    }
}
