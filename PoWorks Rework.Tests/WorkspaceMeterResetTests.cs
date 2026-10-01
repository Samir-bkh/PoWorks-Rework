using Npgsql;
using PoWorks_Rework.Services;
using Xunit;

namespace PoWorks_Rework.Tests;

public class WorkspaceMeterResetTests
{
    [Fact]
    public async Task Reset_RemovesWorkspaceMetersAndHistory_ButPreservesOtherWorkspaceAndInvoices()
    {
        var connectionString = Environment.GetEnvironmentVariable("POWORKS_TEST_POSTGRES");
        if (string.IsNullOrWhiteSpace(connectionString)) return;

        await WithIsolatedSchemaAsync(connectionString, async connection =>
        {
            await ExecuteAsync(connection, @"
                INSERT INTO ""Meters"" (""MeterId"", ""Name"", ""CompanyId"") VALUES
                    (1, 'Building.Power.kW', 1), (3, 'Other.Power.kW', 2);
                INSERT INTO ""Meters"" (""MeterId"", ""Name"", ""ParentId"", ""CompanyId"")
                    VALUES (2, 'Building.Room.Power', 1, 1);
                INSERT INTO ""MeterReadings"" (""MeterId"", ""CompanyId"") VALUES
                    (1, 1), (2, 1), (NULL, 1), (1, 2), (3, 2);
                INSERT INTO ""MeterReadingsDaily"" (""MeterId"", ""CompanyId"") VALUES (1, 1), (2, 2), (3, 2);
                INSERT INTO ""MeterReadingsMonthly"" (""MeterId"", ""CompanyId"") VALUES (1, 1), (2, 2), (3, 2);
                INSERT INTO ""MeterReadingsYearly"" (""MeterId"", ""CompanyId"") VALUES (1, 1), (2, 2), (3, 2);
                INSERT INTO ""Bills"" (""BillId"", ""CompanyId"", ""Amount"") VALUES
                    (11, 1, 80), (12, 2, 95);
                INSERT INTO ""BillLineItems"" (""LineItemId"", ""BillId"", ""MeterId"", ""MeterName"") VALUES
                    (101, 11, 1, ''), (102, 12, 3, 'Other.Power.kW');");

            await using (var tx = await connection.BeginTransactionAsync())
            {
                var result = await WorkspaceMeterReset.ExecuteAsync(connection, tx, 1);
                await tx.CommitAsync();

                Assert.Equal(2, result.Meters);
                Assert.Equal(4, result.RawReadings);
                Assert.Equal(2, result.DailyReadings);
                Assert.Equal(2, result.MonthlyReadings);
                Assert.Equal(2, result.YearlyReadings);
                Assert.Equal(10, result.Readings);
                Assert.Equal(1, result.InvoiceLinksDetached);
            }

            foreach (var table in new[] { "Meters", "MeterReadings", "MeterReadingsDaily",
                     "MeterReadingsMonthly", "MeterReadingsYearly" })
            {
                Assert.Equal(0, await CountAsync(connection, table, 1));
                Assert.Equal(1, await CountAsync(connection, table, 2));
            }

            // Rows mislabeled as workspace 2 must not survive with a dangling
            // foreign key to a deleted workspace 1 meter.
            await using (var command = new NpgsqlCommand(@"
                SELECT COUNT(*) FROM ""MeterReadings"" WHERE ""MeterId"" = 1", connection))
                Assert.Equal(0L, (long)(await command.ExecuteScalarAsync())!);

            await using (var command = new NpgsqlCommand(@"
                SELECT line.""MeterId"", line.""MeterName"", bill.""Amount""
                FROM ""BillLineItems"" line
                JOIN ""Bills"" bill ON bill.""BillId"" = line.""BillId""
                WHERE line.""LineItemId"" = 101", connection))
            await using (var reader = await command.ExecuteReaderAsync())
            {
                Assert.True(await reader.ReadAsync());
                Assert.True(reader.IsDBNull(0));
                Assert.Equal("Building.Power.kW", reader.GetString(1));
                Assert.Equal(80m, reader.GetDecimal(2));
            }

            await using (var command = new NpgsqlCommand(@"
                SELECT ""MeterId"" FROM ""BillLineItems"" WHERE ""LineItemId"" = 102", connection))
                Assert.Equal(3, (int)(await command.ExecuteScalarAsync())!);

            await using var secondTx = await connection.BeginTransactionAsync();
            var second = await WorkspaceMeterReset.ExecuteAsync(connection, secondTx, 1);
            await secondTx.CommitAsync();
            Assert.Equal(0, second.Meters);
            Assert.Equal(0, second.Readings);
        });
    }

    [Fact]
    public async Task Reset_RollsBackAllChanges_WhenAnUnexpectedCrossWorkspaceReferenceBlocksDeletion()
    {
        var connectionString = Environment.GetEnvironmentVariable("POWORKS_TEST_POSTGRES");
        if (string.IsNullOrWhiteSpace(connectionString)) return;

        await WithIsolatedSchemaAsync(connectionString, async connection =>
        {
            await ExecuteAsync(connection, @"
                INSERT INTO ""Meters"" (""MeterId"", ""Name"", ""CompanyId"") VALUES (1, 'Main', 1);
                INSERT INTO ""Meters"" (""MeterId"", ""Name"", ""ParentId"", ""CompanyId"")
                    VALUES (2, 'Inconsistent child', 1, 2);
                INSERT INTO ""MeterReadings"" (""MeterId"", ""CompanyId"") VALUES (1, 1);
                INSERT INTO ""Bills"" (""BillId"", ""CompanyId"", ""Amount"") VALUES (11, 1, 80);
                INSERT INTO ""BillLineItems"" (""LineItemId"", ""BillId"", ""MeterId"", ""MeterName"")
                    VALUES (101, 11, 1, 'Main');");

            await using (var tx = await connection.BeginTransactionAsync())
            {
                await Assert.ThrowsAsync<PostgresException>(() =>
                    WorkspaceMeterReset.ExecuteAsync(connection, tx, 1));
                await tx.RollbackAsync();
            }

            Assert.Equal(1, await CountAsync(connection, "Meters", 1));
            Assert.Equal(1, await CountAsync(connection, "Meters", 2));
            Assert.Equal(1, await CountAsync(connection, "MeterReadings", 1));
            await using var command = new NpgsqlCommand(@"
                SELECT ""MeterId"" FROM ""BillLineItems"" WHERE ""LineItemId"" = 101", connection);
            Assert.Equal(1, (int)(await command.ExecuteScalarAsync())!);
        });
    }

    private static async Task WithIsolatedSchemaAsync(
        string connectionString, Func<NpgsqlConnection, Task> action)
    {
        var schema = "reset_" + Guid.NewGuid().ToString("N");
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
                    ""MeterId"" INTEGER PRIMARY KEY,
                    ""Name"" TEXT NOT NULL,
                    ""ParentId"" INTEGER REFERENCES ""Meters""(""MeterId""),
                    ""CompanyId"" INTEGER NOT NULL);
                CREATE TABLE ""MeterReadings"" (
                    ""MeterId"" INTEGER REFERENCES ""Meters""(""MeterId""), ""CompanyId"" INTEGER NOT NULL);
                CREATE TABLE ""MeterReadingsDaily"" (
                    ""MeterId"" INTEGER REFERENCES ""Meters""(""MeterId""), ""CompanyId"" INTEGER NOT NULL);
                CREATE TABLE ""MeterReadingsMonthly"" (
                    ""MeterId"" INTEGER REFERENCES ""Meters""(""MeterId""), ""CompanyId"" INTEGER NOT NULL);
                CREATE TABLE ""MeterReadingsYearly"" (
                    ""MeterId"" INTEGER REFERENCES ""Meters""(""MeterId""), ""CompanyId"" INTEGER NOT NULL);
                CREATE TABLE ""Bills"" (
                    ""BillId"" INTEGER PRIMARY KEY, ""CompanyId"" INTEGER NOT NULL, ""Amount"" NUMERIC NOT NULL);
                CREATE TABLE ""BillLineItems"" (
                    ""LineItemId"" INTEGER PRIMARY KEY,
                    ""BillId"" INTEGER NOT NULL REFERENCES ""Bills""(""BillId""),
                    ""MeterId"" INTEGER REFERENCES ""Meters""(""MeterId""),
                    ""MeterName"" TEXT);");

            await action(connection);
        }
        finally
        {
            await ExecuteAsync(admin, $@"DROP SCHEMA ""{schema}"" CASCADE");
        }
    }

    private static async Task<int> CountAsync(NpgsqlConnection connection, string table, int companyId)
    {
        await using var command = new NpgsqlCommand(
            $@"SELECT COUNT(*) FROM ""{table}"" WHERE ""CompanyId"" = @companyId", connection);
        command.Parameters.AddWithValue("companyId", companyId);
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private static async Task ExecuteAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }
}
