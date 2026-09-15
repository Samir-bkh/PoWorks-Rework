using Microsoft.AspNetCore.Mvc;
using PoWorks_Rework.Controllers;
using PoWorks_Rework.Models;
using PoWorks_Rework.Services;
using Xunit;

namespace PoWorks_Rework.Tests;

public class TenantManagementTests
{
    [Fact]
    public void Validation_AcceptsValidTenant()
    {
        var tenant = new Tenant
        {
            CompanyName = "Tenant A",
            Email = "contact@example.com",
            Period = "Monthly",
            TariffType = "Company",
            BaseRate = 0.5m,
            Threshold1 = 100m,
            Threshold1Rate = 0.6m,
            Threshold2 = 200m,
            Threshold2Rate = 0.8m,
            Deposit = 10m,
            StartDate = "2026-09-15"
        };

        Assert.Empty(TenantLifecycleRules.Validate(tenant));
    }

    [Fact]
    public void Validation_RejectsInvalidThresholdsRatesAndEmail()
    {
        var tenant = new Tenant
        {
            CompanyName = "Tenant A",
            Email = "not-an-email",
            Period = "Monthly",
            TariffType = "Company",
            BaseRate = -1m,
            Threshold1 = 200m,
            Threshold2 = 100m,
            StartDate = "2026-09-15"
        };

        var errors = TenantLifecycleRules.Validate(tenant);

        Assert.Contains(errors, x => x.Contains("Email", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(errors, x => x.Contains("rates", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(errors, x => x.Contains("Threshold 2", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void PermanentDelete_IsAllowedOnlyForEmptyTenant()
    {
        Assert.True(TenantLifecycleRules.CanPermanentlyDelete(new TenantDependencySummary()));

        Assert.False(TenantLifecycleRules.CanPermanentlyDelete(new TenantDependencySummary
        {
            MeterCount = 1
        }));

        Assert.False(TenantLifecycleRules.CanPermanentlyDelete(new TenantDependencySummary
        {
            UserCount = 1,
            BillCount = 1,
            PaymentCount = 1
        }));
    }

    [Fact]
    public void Search_IsReadOnlyGet_NotMutationPost()
    {
        var method = typeof(TenantController).GetMethod(nameof(TenantController.Search));
        Assert.NotNull(method);

        Assert.NotNull(method!.GetCustomAttributes(typeof(HttpGetAttribute), inherit: true).SingleOrDefault());
        Assert.Empty(method.GetCustomAttributes(typeof(HttpPostAttribute), inherit: true));
    }

    [Fact]
    public void TenantWrites_AreExplicitlyWorkspaceScoped()
    {
        var source = ReadSource("Controllers", "TenantManagementController.cs");

        Assert.Contains(@"""TenantID"" = @tenantId", source);
        Assert.Contains(@"""CompanyId"" = @companyId", source);
        Assert.Contains("TenantExistsInWorkspace", source);
        Assert.DoesNotContain("GetCurrentUserId", source);
        Assert.Contains("ClaimTypes.NameIdentifier", source);
    }

    [Fact]
    public void TenantLifecycle_HasRichAuditAndSafeDependencyChecks()
    {
        var source = ReadSource("Controllers", "TenantManagementController.cs");

        Assert.Contains("DELETE_BLOCKED", source);
        Assert.Contains("EnableTenant", source);
        Assert.Contains("DisableTenant", source);
        Assert.Contains("EntityType = "Tenant"", source);
        Assert.Contains(@"""AspNetUserClaims""", source);
        Assert.Contains(@"""Meters""", source);
        Assert.Contains(@"""Bills""", source);
        Assert.Contains(@"""Payments""", source);
        Assert.Contains("Disable it instead", source);
    }

    [Fact]
    public void Schema_PersistsTenantWorkspaceAddressAndTariffFields()
    {
        var schema = ReadSource("wwwroot", "sql", "initial_schema.sql");

        Assert.Contains(@"ALTER TABLE ""TenantDetails"" ADD COLUMN IF NOT EXISTS ""CompanyId""", schema);
        Assert.Contains(@"ADD COLUMN IF NOT EXISTS ""Address1""", schema);
        Assert.Contains(@"ADD COLUMN IF NOT EXISTS ""PostCode""", schema);
        Assert.Contains(@"ADD COLUMN IF NOT EXISTS ""TariffType""", schema);
        Assert.Contains(@"ADD COLUMN IF NOT EXISTS ""Threshold1""", schema);
        Assert.Contains(@"ADD COLUMN IF NOT EXISTS ""Threshold2Rate""", schema);
        Assert.Contains(@"SET ""CompanyId"" = t.""CompanyId""", schema);
        Assert.Contains("idx_tenantdetails_company_tenant", schema);
    }

    [Fact]
    public void TenantViews_UseGetSearchAndShowRealAssignedMeters()
    {
        var management = ReadSource("Views", "Tenant", "Management.cshtml");
        var consumption = ReadSource("Views", "Tenant", "_Consumption.cshtml");
        var editor = ReadSource("Views", "Tenant", "_TenantManagement.cshtml");

        Assert.Contains("method="get"", management);
        Assert.Contains("AssignedMeterCount", management);
        Assert.Contains("Model.ConsumptionData.Meters.Any()", consumption);
        Assert.Contains("Open Meter Management", consumption);
        Assert.DoesNotContain("Meters functionality will be implemented in the future", consumption);
        Assert.Contains("DisableTenant", editor);
        Assert.Contains("EnableTenant", editor);
        Assert.Contains("DeleteTenant", editor);
    }

    private static string ReadSource(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "PoWorks Rework.sln")))
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

        throw new InvalidOperationException("Could not locate repository root.");
    }
}
