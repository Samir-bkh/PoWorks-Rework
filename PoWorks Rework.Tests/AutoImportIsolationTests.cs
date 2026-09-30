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
    public void LastReadingQuery_IsExplicitlyScopedToCompanyAndCurrentMeterBatch()
    {
        Assert.Contains(@"""CompanyId"" = @companyId", AutoImportQueries.LastReadings);
        Assert.Contains(@"""MeterId"" = ANY(@meterIds)", AutoImportQueries.LastReadings);
        Assert.Contains(@"ORDER BY ""MeterId"", ""Timestamp"" DESC", AutoImportQueries.LastReadings);
    }

    [Fact]
    public async Task LastReadingQuery_ReturnsOnlyRequestedMetersAndTheirLatestPoint()
    {
        var connectionString = Environment.GetEnvironmentVariable("POWORKS_TEST_POSTGRES");
        if (string.IsNullOrWhiteSpace(connectionString)) return;

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        await using (var setup = new NpgsqlCommand(
            """
            CREATE TEMP TABLE "MeterReadings" (
                "ReadingId" SERIAL PRIMARY KEY,
                "MeterId" INTEGER NOT NULL,
                "Timestamp" TIMESTAMP NOT NULL,
                "Value" NUMERIC NOT NULL,
                "Quality" INTEGER,
                "CompanyId" INTEGER NOT NULL
            ) ON COMMIT DROP;

            CREATE INDEX idx_test_autoimport_last_reading
                ON "MeterReadings"("CompanyId", "MeterId", "Timestamp" DESC);

            INSERT INTO "MeterReadings"
                ("MeterId", "Timestamp", "Value", "Quality", "CompanyId")
            VALUES
                (1, '2026-09-29 08:00:00', 10, 192, 1),
                (1, '2026-09-29 09:00:00', 20, 192, 1),
                (2, '2026-09-29 10:00:00', 30, 192, 1),
                (3, '2026-09-29 11:00:00', 40, 192, 2);
            """,
            connection,
            transaction))
        {
            await setup.ExecuteNonQueryAsync();
        }

        await using var command = new NpgsqlCommand(
            AutoImportQueries.LastReadings,
            connection,
            transaction);
        command.Parameters.AddWithValue("companyId", 1);
        command.Parameters.AddWithValue("meterIds", new[] { 1 });

        await using (var reader = await command.ExecuteReaderAsync())
        {
            Assert.True(await reader.ReadAsync());
            Assert.Equal(1, reader.GetInt32(0));
            Assert.Equal(new DateTime(2026, 9, 29, 9, 0, 0), reader.GetDateTime(1));
            Assert.Equal(20m, reader.GetDecimal(2));
            Assert.False(await reader.ReadAsync());
        }

        await transaction.RollbackAsync();
    }

    [Fact]
    public void ApiSettingsQuery_IsExplicitlyScopedToCompanyAndActiveConnection()
    {
        Assert.Contains(@"""CompanyId"" = @companyId", AutoImportQueries.ApiSettings);
        Assert.Contains(@"""IsActive"" = TRUE", AutoImportQueries.ApiSettings);
    }
}
