using Xunit;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using PoWorks_Rework.Services;

namespace PoWorks_Rework.Tests;

public class CompanyContextTests
{
    [Fact]
    public void RegularUser_UsesCompanyClaim()
    {
        var http = new DefaultHttpContext();
        http.User = Principal("manager", new Claim("CompanyId", "7"));

        var sut = new CompanyContext(new HttpContextAccessor { HttpContext = http });

        Assert.Equal(7, sut.CurrentCompanyId);
    }

    [Fact]
    public void Admin_UsesSelectedWorkspaceCookie()
    {
        var http = new DefaultHttpContext();
        http.User = Principal("Admin", new Claim("CompanyId", "1"));
        http.Request.Headers.Cookie = "AdminSelectedCompanyId=9";

        var sut = new CompanyContext(new HttpContextAccessor { HttpContext = http });

        Assert.Equal(9, sut.CurrentCompanyId);
    }

    [Fact]
    public void MissingCompanyContext_FallsBackToDefaultWorkspace()
    {
        var http = new DefaultHttpContext();
        http.User = Principal("manager");

        var sut = new CompanyContext(new HttpContextAccessor { HttpContext = http });

        Assert.Equal(1, sut.CurrentCompanyId);
    }

    [Fact]
    public void RegularUser_CannotOverrideCompanyWithAdminCookie()
    {
        var http = new DefaultHttpContext();
        http.User = Principal("manager", new Claim("CompanyId", "4"));
        http.Request.Headers.Cookie = "AdminSelectedCompanyId=99";

        var sut = new CompanyContext(new HttpContextAccessor { HttpContext = http });

        Assert.Equal(4, sut.CurrentCompanyId);
    }

    private static ClaimsPrincipal Principal(string name, params Claim[] claims)
    {
        var allClaims = new List<Claim> { new(ClaimTypes.Name, name) };
        allClaims.AddRange(claims);
        return new ClaimsPrincipal(new ClaimsIdentity(allClaims, "TestAuth"));
    }
}
