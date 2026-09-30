using Npgsql;
using PoWorks_Rework.Services;
using Xunit;

namespace PoWorks_Rework.Tests;

public class AutoImportSnapshotWriterTests
{
    [Fact]
    public async Task TwoPollsWithSameValue_KeepTwoReadingsAndExistingHistory()
    {
        var connectionString = Environment.GetEnvironmentVariable("POWORKS_TEST_POSTGRES");
        if (string.IsNullOrWhiteSpace(connectionString)) return;

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using (var setup = new NpgsqlCommand(
            """
            CREATE TEMP TABLE "MeterReadings" (
                "ReadingId" SERIAL PRIMARY KEY,
                "MeterId" INTEGER NOT NULL,
                "Timestamp" TIMESTAMP NOT NULL,
                "Value" NUMERIC NOT NULL,
                "Quality" INTEGER,
                "CompanyId" INTEGER NOT NULL,
                UNIQUE ("MeterId", "Timestamp")
            );
            INSERT INTO "MeterReadings" ("MeterId", "Timestamp", "Value", "Quality", "CompanyId")
            VALUES (7, '2026-09-30 09:58:00', 550, 192, 1);
            """, connection))
        {
            await setup.ExecuteNonQueryAsync();
        }

        var start = new DateTime(2026, 9, 30, 10, 0, 0);
        async Task<int> Write(DateTime timestamp)
        {
            await using var transaction = await connection.BeginTransactionAsync();
            var inserted = await AutoImportSnapshotWriter.InsertAsync(
                connection, transaction, 1,
                new[] { new AutoImportSnapshotReader.Snapshot(7, timestamp, 600m, 192) });
            await transaction.CommitAsync();
            return inserted;
        }

        Assert.Equal(1, await Write(start));
        Assert.Equal(1, await Write(start.AddMinutes(2)));
        Assert.Equal(0, await Write(start.AddMinutes(2)));

        await using var verify = new NpgsqlCommand(
            "SELECT \"Timestamp\", \"Value\" FROM \"MeterReadings\" WHERE \"MeterId\" = 7 ORDER BY \"Timestamp\"",
            connection);
        await using var reader = await verify.ExecuteReaderAsync();
        var rows = new List<(DateTime Timestamp, decimal Value)>();
        while (await reader.ReadAsync())
            rows.Add((reader.GetDateTime(0), reader.GetDecimal(1)));

        Assert.Equal(new[]
        {
            (start.AddMinutes(-2), 550m),
            (start, 600m),
            (start.AddMinutes(2), 600m)
        }, rows);
    }
}
