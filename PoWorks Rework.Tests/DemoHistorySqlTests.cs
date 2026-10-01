using Npgsql;
using Xunit;

namespace PoWorks_Rework.Tests;

public class DemoHistorySqlTests
{
    [Fact]
    public async Task DemoScript_CoversAllYears_IsIdempotent_AndLeavesOtherWorkspaceUntouched()
    {
        var connectionString = Environment.GetEnvironmentVariable("POWORKS_TEST_POSTGRES");
        if (string.IsNullOrWhiteSpace(connectionString)) return;

        var schema = "demo_" + Guid.NewGuid().ToString("N");
        await using var admin = new NpgsqlConnection(connectionString);
        await admin.OpenAsync();
        await ExecuteAsync(admin, $@"CREATE SCHEMA ""{schema}""");

        try
        {
            var builder = new NpgsqlConnectionStringBuilder(connectionString) { SearchPath = schema };
            await using var connection = new NpgsqlConnection(builder.ConnectionString);
            await connection.OpenAsync();
            await ExecuteAsync(connection, @"
                CREATE TABLE ""Meters"" (
                    ""MeterId"" SERIAL PRIMARY KEY, ""Name"" VARCHAR(100) NOT NULL,
                    ""Label"" VARCHAR(150), ""Unit"" VARCHAR(20) NOT NULL,
                    ""Type"" VARCHAR(10) NOT NULL, ""Active"" BOOLEAN NOT NULL,
                    ""CompanyId"" INTEGER NOT NULL);
                CREATE TABLE ""MeterReadings"" (
                    ""ReadingId"" SERIAL PRIMARY KEY,
                    ""MeterId"" INTEGER REFERENCES ""Meters""(""MeterId""),
                    ""Timestamp"" TIMESTAMP NOT NULL, ""Value"" NUMERIC NOT NULL,
                    ""Quality"" INTEGER, ""CompanyId"" INTEGER NOT NULL,
                    UNIQUE (""MeterId"", ""Timestamp""));
                CREATE TABLE ""MeterReadingsDaily"" (
                    ""MeterId"" INTEGER REFERENCES ""Meters""(""MeterId""), ""CompanyId"" INTEGER NOT NULL);
                CREATE TABLE ""MeterReadingsMonthly"" (
                    ""MeterId"" INTEGER REFERENCES ""Meters""(""MeterId""), ""CompanyId"" INTEGER NOT NULL);
                CREATE TABLE ""MeterReadingsYearly"" (
                    ""MeterId"" INTEGER REFERENCES ""Meters""(""MeterId""), ""CompanyId"" INTEGER NOT NULL);
                INSERT INTO ""Meters"" (""Name"", ""Label"", ""Unit"", ""Type"", ""Active"", ""CompanyId"")
                    VALUES ('DEMO.Building.Power.kW', 'Other workspace', 'kW', 'main', TRUE, 2),
                           ('DEMO.Building.Power.kW', 'Old demo', 'kW', 'main', TRUE, 1);
                INSERT INTO ""MeterReadings"" (""MeterId"", ""Timestamp"", ""Value"", ""Quality"", ""CompanyId"")
                    VALUES (1, '2025-01-01', 99, 192, 2),
                           (2, '2025-01-01', 123, 192, 1);
                INSERT INTO ""MeterReadingsDaily"" (""MeterId"", ""CompanyId"") VALUES (2, 1);
                INSERT INTO ""MeterReadingsMonthly"" (""MeterId"", ""CompanyId"") VALUES (2, 1);
                INSERT INTO ""MeterReadingsYearly"" (""MeterId"", ""CompanyId"") VALUES (2, 1);");

            var script = ReadScript();
            await ExecuteAsync(connection, script);

            await using (var command = new NpgsqlCommand(@"
                SELECT COUNT(DISTINCT m.""MeterId""), COUNT(*),
                       COUNT(*) FILTER (WHERE EXTRACT(YEAR FROM r.""Timestamp"") = 2024),
                       COUNT(*) FILTER (WHERE EXTRACT(YEAR FROM r.""Timestamp"") = 2025),
                       COUNT(*) FILTER (WHERE EXTRACT(YEAR FROM r.""Timestamp"") = 2026),
                       MIN(r.""Quality""), MAX(r.""Quality"")
                FROM ""MeterReadings"" r
                JOIN ""Meters"" m ON m.""MeterId"" = r.""MeterId""
                WHERE m.""CompanyId"" = 1", connection))
            await using (var reader = await command.ExecuteReaderAsync())
            {
                Assert.True(await reader.ReadAsync());
                Assert.Equal(4, reader.GetInt64(0));
                Assert.True(reader.GetInt64(1) > 25000);
                Assert.True(reader.GetInt64(2) > 0);
                Assert.True(reader.GetInt64(3) > 0);
                Assert.True(reader.GetInt64(4) > 0);
                Assert.Equal(192, reader.GetInt32(5));
                Assert.Equal(192, reader.GetInt32(6));
            }

            await using (var command = new NpgsqlCommand(@"
                SELECT ""Value"" FROM ""MeterReadings""
                WHERE ""MeterId"" = 2 AND ""Timestamp"" = '2025-01-01'", connection))
                Assert.NotEqual(123m, (decimal)(await command.ExecuteScalarAsync())!);

            foreach (var table in new[] { "MeterReadingsDaily", "MeterReadingsMonthly",
                     "MeterReadingsYearly" })
            {
                await using var command = new NpgsqlCommand(
                    $@"SELECT COUNT(*) FROM ""{table}"" WHERE ""CompanyId"" = 1", connection);
                Assert.Equal(0L, (long)(await command.ExecuteScalarAsync())!);
            }

            await using (var command = new NpgsqlCommand(@"
                SELECT COUNT(DISTINCT r.""Value"")
                FROM ""MeterReadings"" r JOIN ""Meters"" m ON m.""MeterId"" = r.""MeterId""
                WHERE m.""CompanyId"" = 1 AND m.""Name"" = 'DEMO.Building.Power.kW'
                  AND r.""Timestamp"" >= '2025-09-01'
                  AND r.""Timestamp"" < '2025-09-08'", connection))
                Assert.True((long)(await command.ExecuteScalarAsync())! > 40);

            var before = await CountAsync(connection);
            var firstPower = await ValueAtAsync(connection, "DEMO.Building.Power.kW",
                new DateTime(2025, 1, 1));
            await ExecuteAsync(connection, script);
            Assert.Equal(before, await CountAsync(connection));
            Assert.Equal(firstPower, await ValueAtAsync(connection, "DEMO.Building.Power.kW",
                new DateTime(2025, 1, 1)));

            await using (var command = new NpgsqlCommand(@"
                SELECT COUNT(*) FROM ""MeterReadings"" WHERE ""CompanyId"" = 2", connection))
                Assert.Equal(1L, (long)(await command.ExecuteScalarAsync())!);
        }
        finally
        {
            await ExecuteAsync(admin, $@"DROP SCHEMA ""{schema}"" CASCADE");
        }
    }

    private static async Task<decimal> ValueAtAsync(
        NpgsqlConnection connection, string meterName, DateTime timestamp)
    {
        await using var command = new NpgsqlCommand(@"
            SELECT r.""Value""
            FROM ""MeterReadings"" r JOIN ""Meters"" m ON m.""MeterId"" = r.""MeterId""
            WHERE m.""CompanyId"" = 1 AND m.""Name"" = @name AND r.""Timestamp"" = @timestamp", connection);
        command.Parameters.AddWithValue("name", meterName);
        command.Parameters.AddWithValue("timestamp", timestamp);
        return (decimal)(await command.ExecuteScalarAsync())!;
    }

    private static async Task<long> CountAsync(NpgsqlConnection connection)
    {
        await using var command = new NpgsqlCommand(@"
            SELECT COUNT(*) FROM ""MeterReadings"" WHERE ""CompanyId"" = 1", connection);
        return (long)(await command.ExecuteScalarAsync())!;
    }

    private static async Task ExecuteAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection) { CommandTimeout = 300 };
        await command.ExecuteNonQueryAsync();
    }

    private static string ReadScript()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            var path = Path.Combine(directory.FullName, "scripts", "demo_history.sql");
            if (File.Exists(path)) return File.ReadAllText(path);
            directory = directory.Parent;
        }
        throw new FileNotFoundException("Could not locate demo_history.sql in the repository.");
    }
}
