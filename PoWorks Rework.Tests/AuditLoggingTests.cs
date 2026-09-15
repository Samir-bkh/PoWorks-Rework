using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using PoWorks_Rework.Services;
using PoWorks_Rework.Controllers;
using Xunit;

namespace PoWorks_Rework.Tests;

public class AuditLoggingTests
{
    [Theory]
    [InlineData("POST", true)]
    [InlineData("PUT", true)]
    [InlineData("PATCH", true)]
    [InlineData("DELETE", true)]
    [InlineData("GET", false)]
    [InlineData("HEAD", false)]
    public void MutationClassifier_OnlyFlagsStateChangingVerbs(string method, bool expected)
    {
        Assert.Equal(expected, AuditRequestClassifier.IsMutation(method));
    }

    [Fact]
    public void RequestClassifier_DoesNotDuplicateDedicatedLoginAudit()
    {
        var login = new DefaultHttpContext();
        login.Request.Method = HttpMethods.Post;
        login.Request.Path = "/Auth/Login";

        var meterUpdate = new DefaultHttpContext();
        meterUpdate.Request.Method = HttpMethods.Post;
        meterUpdate.Request.Path = "/Meter/BulkUpdate";

        Assert.False(AuditRequestClassifier.ShouldAudit(login.Request));
        Assert.True(AuditRequestClassifier.ShouldAudit(meterUpdate.Request));
    }


    [Fact]
    public void AuditLogFilter_DoesNotReuseMvcActionRouteValue()
    {
        var method = typeof(AuditLogController).GetMethod(nameof(AuditLogController.Index));
        Assert.NotNull(method);

        var parameterNames = method!.GetParameters().Select(p => p.Name).ToList();

        Assert.Contains("auditAction", parameterNames);
        Assert.DoesNotContain("action", parameterNames);

        var view = ReadSource("Views", "AuditLog", "Index.cshtml");
        Assert.Contains("name=\"auditAction\"", view);
        Assert.Contains("asp-route-auditAction", view);
        Assert.DoesNotContain("name=\"action\"", view);
    }


    [Fact]
    public void AuditLogView_ShowsBrowserLocalTimeAndKeepsTechnicalIdsOutOfMainTable()
    {
        var view = ReadSource("Views", "AuditLog", "Index.cshtml");

        Assert.Contains("Time (local)", view);
        Assert.Contains("class=\"audit-local-time\"", view);
        Assert.Contains("data-utc=", view);
        Assert.Contains("new Date(utcValue)", view);
        Assert.Contains("Guid.TryParse(item.EntityId", view);
        Assert.Contains("Technical ID:", view);
        Assert.Contains("UTC:", view);
    }

    [Fact]
    public void AuditLogPage_DefaultsToCompactMeaningfulEvents()
    {
        var method = typeof(AuditLogController).GetMethod(nameof(AuditLogController.Index));
        Assert.NotNull(method);

        var parameters = method!.GetParameters().ToDictionary(p => p.Name!);

        Assert.Equal(false, parameters["showTechnical"].DefaultValue);
        Assert.Equal(15, parameters["pageSize"].DefaultValue);

        var controller = ReadSource("Controllers", "AuditLogController.cs");
        Assert.Contains(@"""Action"" = 'MUTATION'", controller);
        Assert.Contains(@"""Success"" = TRUE", controller);
        Assert.DoesNotContain(@"""Action"" = 'MUTATION_FAILED' AND ""Success"" = TRUE", controller);
        Assert.Contains("NormalizePageSize", controller);
    }

    [Fact]
    public void AuditLogView_IsCompactAndPreservesDisplayOptions()
    {
        var view = ReadSource("Views", "AuditLog", "Index.cshtml");

        Assert.Contains("table-sm table-hover audit-table", view);
        Assert.Contains("audit-summary", view);
        Assert.Contains("name=\"pageSize\"", view);
        Assert.Contains("name=\"showTechnical\"", view);
        Assert.Contains("Technical MUTATION events hidden", view);
        Assert.Contains("asp-route-showTechnical", view);
        Assert.Contains("asp-route-pageSize", view);
        Assert.Contains("Offline log files", view);
    }

    [Fact]
    public void ConfiguredLogDirectory_IsCreatedAndResolved()
    {
        var root = Path.Combine(Path.GetTempPath(), "poworks-audit-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Diagnostics:LogDirectory"] = root
                })
                .Build();

            var resolved = FileDiagnostics.ResolveLogRoot(configuration);

            Assert.True(Directory.Exists(resolved));
            Assert.Equal(Path.GetFullPath(root), resolved);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void AuditSchema_HasPersistentTableAndIndexes()
    {
        var source = ReadSource("wwwroot", "sql", "initial_schema.sql");

        Assert.Contains("CREATE TABLE IF NOT EXISTS \"AuditLogs\"", source);
        Assert.Contains("idx_auditlogs_timestamp", source);
        Assert.Contains("idx_auditlogs_company", source);
        Assert.Contains("idx_auditlogs_user", source);
        Assert.Contains("idx_auditlogs_entity", source);
    }

    [Fact]
    public void BootstrapDiagnostics_StartBeforeProgramAndWriteDailyConsoleFiles()
    {
        var source = ReadSource("Services", "BootstrapFileDiagnostics.cs");

        Assert.Contains("[ModuleInitializer]", source);
        Assert.Contains("console-{today:yyyy-MM-dd}.log", source);
        Assert.Contains("UnhandledException", source);
        Assert.Contains("UnobservedTaskException", source);
    }

    [Fact]
    public void BaseController_HasGenericMutationAuditSafetyNet()
    {
        var source = ReadSource("Controllers", "BaseController.cs");

        Assert.Contains("OnActionExecutionAsync", source);
        Assert.Contains("AuditRequestClassifier.IsMutation", source);
        Assert.Contains("AuditTrail.LogAsync", source);
        Assert.Contains("MUTATION_FAILED", source);
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
