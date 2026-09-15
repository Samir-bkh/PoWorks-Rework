using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using PoWorks_Rework.Services;
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
