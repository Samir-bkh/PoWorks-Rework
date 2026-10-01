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
                INSERT INTO ""Meters"" (""Name"", ""Label"", ""Unit"", ""Type"", ""Active"", ""CompanyId"")
                    VALUES ('DEMO.Building.Power.kW', 'Other workspace', 'kW', 'main', TRUE, 2);
                INSERT INTO ""MeterReadings"" (""MeterId"", ""Timestamp"", ""Value"", ""Quality"", ""CompanyId"")
                    VALUES (1, '2025-01-01', 99, 192, 2);");

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
                Assert.True(reader.GetInt64(1) > 10000);
                Assert.True(reader.GetInt64(2) > 0);
                Assert.True(reader.GetInt64(3) > 0);
                Assert.True(reader.GetInt64(4) > 0);
                Assert.Equal(192, reader.GetInt32(5));
                Assert.Equal(192, reader.GetInt32(6));
            }

            var before = await CountAsync(connection);
            await ExecuteAsync(connection, script);
            Assert.Equal(before, await CountAsync(connection));

            await using (var command = new NpgsqlCommand(@"
                SELECT COUNT(*) FROM ""MeterReadings"" WHERE ""CompanyId"" = 2", connection))
                Assert.Equal(1L, (long)(await command.ExecuteScalarAsync())!);
        }
        finally
        {
            await ExecuteAsync(admin, $@"DROP SCHEMA ""{schema}"" CASCADE");
        }
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
