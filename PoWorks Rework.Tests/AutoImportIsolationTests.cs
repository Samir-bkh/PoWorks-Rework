using Npgsql;
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
    public void ApiSettingsQuery_IsExplicitlyScopedToCompanyAndActiveConnection()
    {
        Assert.Contains(@"""CompanyId"" = @companyId", AutoImportQueries.ApiSettings);
        Assert.Contains(@"""IsActive"" = TRUE", AutoImportQueries.ApiSettings);
    }

    [Fact]
    public async Task ApiSettingsQuery_ReturnsTheSelectedConnectionInterval()
    {
        var connectionString = Environment.GetEnvironmentVariable("POWORKS_TEST_POSTGRES");
        if (string.IsNullOrWhiteSpace(connectionString)) return;

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        await using (var setup = new NpgsqlCommand(
            """
            CREATE TEMP TABLE "WebServiceConnections" (
                "ConnectionId" TEXT NOT NULL,
                "ConnectionName" TEXT,
                "BaseUrl" TEXT,
                "ClientId" TEXT,
                "ClientSecret" TEXT,
                "ApiKey" TEXT,
                "Username" TEXT,
                "Password" TEXT,
                "AuthType" INTEGER,
                "TimeoutSeconds" INTEGER,
                "ProjectName" TEXT,
                "IsDefault" BOOLEAN,
                "IsActive" BOOLEAN,
                "EnableAutomaticImport" BOOLEAN,
                "AutoImportIntervalMinutes" INTEGER,
                "CompanyId" INTEGER NOT NULL
            ) ON COMMIT DROP;

            INSERT INTO "WebServiceConnections"
                ("ConnectionId", "IsDefault", "IsActive", "EnableAutomaticImport", "AutoImportIntervalMinutes", "CompanyId")
            VALUES
                ('selected', TRUE, TRUE, TRUE, 2, 1),
                ('other-workspace', TRUE, TRUE, TRUE, 1, 2),
                ('inactive', TRUE, FALSE, TRUE, 1, 1);
            """,
            connection,
            transaction))
        {
            await setup.ExecuteNonQueryAsync();
        }

        await using var command = new NpgsqlCommand(AutoImportQueries.ApiSettings, connection, transaction);
        command.Parameters.AddWithValue("companyId", 1);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal("selected", reader.GetString(0));
        Assert.True(reader.GetBoolean(13));
        Assert.Equal(2, reader.GetInt32(14));
        Assert.False(await reader.ReadAsync());
    }
}
