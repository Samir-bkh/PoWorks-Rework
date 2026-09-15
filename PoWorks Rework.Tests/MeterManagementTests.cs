using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PoWorks_Rework.Controllers;
using PoWorks_Rework.Models;
using PoWorks_Rework.Services;
using Xunit;

namespace PoWorks_Rework.Tests;

public class MeterManagementTests
{
    [Fact]
    public void Validation_RejectsMissingNameInvalidTypeAndOversizedUnit()
    {
        var meter = new Meter
        {
            Name = "",
            Type = "Other",
            Unit = new string('x', 21)
        };

        var errors = MeterLifecycleRules.Validate(meter);

        Assert.Contains(errors, x => x.Contains("name", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(errors, x => x.Contains("type", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(errors, x => x.Contains("unit", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void PermanentDelete_IsAllowedOnlyWhenMeterHasNoHistoricalDependencies()
    {
        Assert.True(MeterLifecycleRules.CanPermanentlyDelete(new MeterDependencySummary()));

        Assert.False(MeterLifecycleRules.CanPermanentlyDelete(new MeterDependencySummary
        {
            RawReadingCount = 1
        }));

        Assert.False(MeterLifecycleRules.CanPermanentlyDelete(new MeterDependencySummary
        {
            BillLineCount = 1
        }));

        Assert.False(MeterLifecycleRules.CanPermanentlyDelete(new MeterDependencySummary
        {
            ChildMeterCount = 1
        }));
    }

    [Fact]
    public void MeterController_RequiresManagementAccess()
    {
        var attributes = typeof(MeterController)
            .GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
            .Cast<AuthorizeAttribute>()
            .ToList();

        Assert.Contains(attributes, attribute => attribute.Policy == "ManagementAccess");
    }

    [Fact]
    public void MeterSearch_IsReadOnlyGet()
    {
        var method = typeof(MeterController).GetMethod(nameof(MeterController.Search));
        Assert.NotNull(method);

        Assert.NotNull(method!.GetCustomAttributes(typeof(HttpGetAttribute), inherit: true).SingleOrDefault());
        Assert.Empty(method.GetCustomAttributes(typeof(HttpPostAttribute), inherit: true));
    }

    [Fact]
    public void MeterController_HasNoDebugEndpointOrRawConsoleDumping()
    {
        var source = ReadSource("Controllers", "MeterController.cs");

        Assert.DoesNotContain("IActionResult> Debug", source);
        Assert.DoesNotContain("Console.WriteLine", source);
        Assert.DoesNotContain("Request.Form.Keys", source);
    }

    [Fact]
    public void MeterWrites_ValidateWorkspaceTenantParentAndCycles()
    {
        var source = ReadSource("Controllers", "MeterController.cs");

        Assert.Contains("\"\"CompanyId\"\" = @CompanyId", source);
        Assert.Contains("ValidateTenantAssignmentAsync", source);
        Assert.Contains("ValidateParentAssignmentAsync", source);
        Assert.Contains("WITH RECURSIVE ancestors", source);
        Assert.Contains("A meter cannot be its own parent.", source);
        Assert.Contains("Only a Main meter can be used as a parent.", source);
    }

    [Fact]
    public void MeterDeletion_PreservesHistoryByBlockingPermanentDelete()
    {
        var source = ReadSource("Controllers", "MeterController.cs");

        Assert.Contains(@"""MeterReadings""", source);
        Assert.Contains(@"""MeterReadingsDaily""", source);
        Assert.Contains(@"""MeterReadingsMonthly""", source);
        Assert.Contains(@"""MeterReadingsYearly""", source);
        Assert.Contains(@"""BillLineItems""", source);
        Assert.Contains("DELETE_BLOCKED", source);
        Assert.Contains("Disable it instead", source);
        Assert.DoesNotContain(@"DELETE FROM ""MeterReadings""", source);
    }

    [Fact]
    public void BulkMeterActions_AreAuditedWithBusinessActions()
    {
        var source = ReadSource("Controllers", "MeterController.cs");

        Assert.Contains("BULK_ASSIGN", source);
        Assert.Contains("BULK_UNASSIGN", source);
        Assert.Contains("BULK_ENABLE", source);
        Assert.Contains("BULK_DISABLE", source);
        Assert.Contains("BULK_UPDATE", source);
        Assert.Contains("Historical readings were preserved.", source);
    }

    [Fact]
    public void Repository_ScopesTenantParentJoinsAndFilteredBulkSelection()
    {
        var source = ReadSource("Repositories", "MeterRepository.cs");

        Assert.Contains("p.\"\"CompanyId\"\" = m.\"\"CompanyId\"\"", source);
        Assert.Contains("t.\"\"CompanyId\"\" = m.\"\"CompanyId\"\"", source);
        Assert.Contains("t2.\"\"CompanyId\"\" = @CompanyId", source);
        Assert.Contains("GetMeterIdsAsync", source);
        Assert.Contains("StatusFilter", source);
        Assert.Contains("AssignmentFilter", source);
    }

    [Fact]
    public void ParentMeterQuery_DoesNotSendUntypedNullPostgresParameter()
    {
        var source = ReadSource("Repositories", "MeterRepository.cs");

        Assert.Contains("var excludeClause = excludeMeterId.HasValue", source);
        Assert.Contains("if (excludeMeterId.HasValue)", source);
        Assert.Contains("cmd.Parameters.AddWithValue(\"@ExcludeMeterId\", excludeMeterId.Value)", source);
        Assert.DoesNotContain("@ExcludeMeterId IS NULL", source);
        Assert.DoesNotContain("excludeMeterId.HasValue ? excludeMeterId.Value : DBNull.Value", source);
    }

    [Fact]
    public void MeterView_ProvidesClientFriendlyFiltersBulkAssignmentAndCsrf()
    {
        var view = ReadSource("Views", "Meter", "Management.cshtml");

        Assert.Contains("method=\"get\"", view);
        Assert.Contains("statusFilter", view);
        Assert.Contains("assignmentFilter", view);
        Assert.Contains("quickAssignModal", view);
        Assert.Contains("Select all @Model.TotalItems filtered meter(s)", view);
        Assert.Contains("RequestVerificationToken", view);
        Assert.Contains("Permanent deletion protected.", view);
        Assert.DoesNotContain("Save as New", view);
    }

    [Fact]
    public void Schema_HasWorkspaceMeterIndexes()
    {
        var schema = ReadSource("wwwroot", "sql", "initial_schema.sql");

        Assert.Contains("idx_meters_companyid", schema);
        Assert.Contains("idx_meters_company_tenant", schema);
        Assert.Contains("idx_meters_company_active", schema);
        Assert.Contains("idx_meters_company_parent", schema);
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
