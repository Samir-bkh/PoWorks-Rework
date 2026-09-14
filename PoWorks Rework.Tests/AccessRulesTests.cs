using Microsoft.AspNetCore.Identity;
using System.Security.Claims;
using PoWorks_Rework.Services;
using Xunit;

namespace PoWorks_Rework.Tests;

public class AccessRulesTests
{
    [Theory]
    [InlineData("Admin", true)]
    [InlineData("admin", true)]
    [InlineData("ADMIN", true)]
    [InlineData("manager", false)]
    [InlineData(null, false)]
    public void IsAdmin_IsCaseInsensitiveAndStrict(string? username, bool expected)
    {
        Assert.Equal(expected, AccessRules.IsAdmin(username));
    }

    [Theory]
    [InlineData("Tenant", true)]
    [InlineData("tenant", true)]
    [InlineData("Management", false)]
    [InlineData(null, false)]
    public void IsTenant_IsCaseInsensitiveAndStrict(string? userType, bool expected)
    {
        Assert.Equal(expected, AccessRules.IsTenant(userType));
    }

    [Theory]
    [InlineData("/", true)]
    [InlineData("/Home", true)]
    [InlineData("/Home/Index", true)]
    [InlineData("/Dashboard", true)]
    [InlineData("/Dashboard/GetConsumptionData", true)]
    [InlineData("/Auth/Logout", true)]
    [InlineData("/Meter/Management", false)]
    [InlineData("/Settings/General", false)]
    [InlineData("/Import", false)]
    public void TenantRouteAllowed_OnlyAllowsTenantSurface(string path, bool expected)
    {
        Assert.Equal(expected, AccessRules.TenantRouteAllowed(path));
    }

    [Fact]
    public void TenantClaims_ContainTenantAndNoManagementPermissions()
    {
        var claims = AccessRules.BuildAccessClaims(
            "Tenant", 4, 12,
            canViewPcVueConfig: true,
            canViewImportExport: true,
            canViewGeneralSettings: true);

        Assert.Contains(claims, c => c.Type == "UserType" && c.Value == "Tenant");
        Assert.Contains(claims, c => c.Type == "CompanyId" && c.Value == "4");
        Assert.Contains(claims, c => c.Type == "TenantId" && c.Value == "12");
        Assert.DoesNotContain(claims, c => c.Type == "Permission");
    }

    [Fact]
    public void ManagementClaims_ContainSelectedPermissionsAndNoTenantClaim()
    {
        var claims = AccessRules.BuildAccessClaims(
            "Management", 7, 99,
            canViewPcVueConfig: true,
            canViewImportExport: false,
            canViewGeneralSettings: true);

        Assert.Contains(claims, c => c.Type == "UserType" && c.Value == "Management");
        Assert.Contains(claims, c => c.Type == "CompanyId" && c.Value == "7");
        Assert.DoesNotContain(claims, c => c.Type == "TenantId");
        Assert.Contains(claims, c => c.Type == "Permission" && c.Value == "ViewPcVueConfig");
        Assert.DoesNotContain(claims, c => c.Type == "Permission" && c.Value == "ViewImportExport");
        Assert.Contains(claims, c => c.Type == "Permission" && c.Value == "ViewGeneralSettings");
    }

    [Fact]
    public void Permission_AdminAlwaysAllowed()
    {
        var user = Principal("Admin");

        Assert.True(AccessRules.HasPermissionOrAdmin(user, "ViewImportExport"));
        Assert.True(AccessRules.HasPermissionOrAdmin(user, "ViewGeneralSettings"));
    }

    [Fact]
    public void Permission_ManagementNeedsClaim()
    {
        var allowed = Principal("manager", new Claim("Permission", "ViewImportExport"));
        var denied = Principal("manager");

        Assert.True(AccessRules.HasPermissionOrAdmin(allowed, "ViewImportExport"));
        Assert.False(AccessRules.HasPermissionOrAdmin(denied, "ViewImportExport"));
    }

    [Fact]
    public void UserEnabled_ReflectsEffectiveLockout()
    {
        var now = DateTimeOffset.UtcNow;

        Assert.True(AccessRules.IsUserEnabled(new IdentityUser { LockoutEnabled = false }, now));
        Assert.True(AccessRules.IsUserEnabled(new IdentityUser { LockoutEnabled = true, LockoutEnd = now.AddMinutes(-1) }, now));
        Assert.False(AccessRules.IsUserEnabled(new IdentityUser { LockoutEnabled = true, LockoutEnd = now.AddMinutes(1) }, now));
    }

    private static ClaimsPrincipal Principal(string name, params Claim[] claims)
    {
        var allClaims = new List<Claim> { new(ClaimTypes.Name, name) };
        allClaims.AddRange(claims);
        return new ClaimsPrincipal(new ClaimsIdentity(allClaims, "TestAuth"));
    }
}
