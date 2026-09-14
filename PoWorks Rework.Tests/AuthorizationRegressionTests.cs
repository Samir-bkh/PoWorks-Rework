using Microsoft.AspNetCore.Authorization;
using PoWorks_Rework.Controllers;
using Xunit;

namespace PoWorks_Rework.Tests;

public class AuthorizationRegressionTests
{
    [Fact]
    public void UserManagement_IsAdminOnly()
    {
        AssertControllerPolicy<UserManagementController>("AdminOnly");
    }

    [Fact]
    public void VarexpImport_RequiresImportExportPermission()
    {
        AssertControllerPolicy<VarexpImportController>("ImportExportAccess");
    }

    [Fact]
    public void WebServiceImport_RequiresImportExportPermission()
    {
        AssertControllerPolicy<WebServicesImportController>("ImportExportAccess");
    }

    [Fact]
    public void GeneralSettings_RequiresGeneralSettingsPermission()
    {
        AssertControllerPolicy<SettingsController>("GeneralSettingsAccess");
    }

    [Theory]
    [InlineData(nameof(CompanyController.Management))]
    [InlineData(nameof(CompanyController.CreateCompany))]
    [InlineData(nameof(CompanyController.EnableCompany))]
    [InlineData(nameof(CompanyController.DisableCompany))]
    [InlineData(nameof(CompanyController.DeleteCompany))]
    public void WorkspaceAdministration_IsAdminOnly(string methodName)
    {
        var method = typeof(CompanyController).GetMethod(methodName);
        Assert.NotNull(method);

        var policies = method!.GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
            .Cast<AuthorizeAttribute>()
            .Select(a => a.Policy)
            .ToList();

        Assert.Contains("AdminOnly", policies);
    }

    private static void AssertControllerPolicy<TController>(string policy)
    {
        var policies = typeof(TController)
            .GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
            .Cast<AuthorizeAttribute>()
            .Select(a => a.Policy)
            .ToList();

        Assert.Contains(policy, policies);
    }
}
