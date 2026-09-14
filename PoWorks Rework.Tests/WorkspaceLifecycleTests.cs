using PoWorks_Rework.Controllers;
using Xunit;

namespace PoWorks_Rework.Tests;

public class WorkspaceLifecycleTests
{
    [Fact]
    public void DefaultWorkspace_CannotBePermanentlyDeleted()
    {
        var item = new CompanyAdminItem
        {
            CompanyId = 1,
            UserCount = 0,
            TenantCount = 0,
            MeterCount = 0,
            BillCount = 0
        };

        Assert.False(item.CanDelete);
    }

    [Fact]
    public void EmptyNonDefaultWorkspace_CanBePermanentlyDeleted()
    {
        var item = new CompanyAdminItem
        {
            CompanyId = 8,
            UserCount = 0,
            TenantCount = 0,
            MeterCount = 0,
            BillCount = 0
        };

        Assert.True(item.CanDelete);
    }

    [Theory]
    [InlineData(1, 0, 0, 0)]
    [InlineData(0, 1, 0, 0)]
    [InlineData(0, 0, 1, 0)]
    [InlineData(0, 0, 0, 1)]
    public void WorkspaceWithBusinessDependencies_CannotBePermanentlyDeleted(
        int users, int tenants, int meters, int bills)
    {
        var item = new CompanyAdminItem
        {
            CompanyId = 5,
            UserCount = users,
            TenantCount = tenants,
            MeterCount = meters,
            BillCount = bills
        };

        Assert.False(item.CanDelete);
    }
}
