using PoWorks_Rework.Services;
using Xunit;

namespace PoWorks_Rework.Tests;

public class AutoImportIsolationTests
{
    [Fact]
    public void ActiveMeterQuery_IsExplicitlyScopedToCompany()
    {
        Assert.Contains(@"""CompanyId"" = @companyId", AutoImportQueries.ActiveMeters);
        Assert.Contains(@"""Active"" = TRUE", AutoImportQueries.ActiveMeters);
    }

    [Fact]
    public void LastReadingQuery_IsExplicitlyScopedToCompany()
    {
        Assert.Contains(@"""CompanyId"" = @companyId", AutoImportQueries.LastReadings);
    }

    [Fact]
    public void ApiSettingsQuery_IsExplicitlyScopedToCompanyAndActiveConnection()
    {
        Assert.Contains(@"""CompanyId"" = @companyId", AutoImportQueries.ApiSettings);
        Assert.Contains(@"""IsActive"" = TRUE", AutoImportQueries.ApiSettings);
    }
}
