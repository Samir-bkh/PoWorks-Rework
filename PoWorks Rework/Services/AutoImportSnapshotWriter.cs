using Npgsql;
using NpgsqlTypes;

namespace PoWorks_Rework.Services;

public static class AutoImportSnapshotWriter
{
    public static async Task<int> InsertAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        int companyId,
        IReadOnlyList<AutoImportSnapshotReader.Snapshot> snapshots,
        CancellationToken cancellationToken = default)
    {
        if (snapshots.Count == 0) return 0;

        using var tempTableCmd = new NpgsqlCommand(@"
            CREATE TEMP TABLE ""TempMeterReadings"" (LIKE ""MeterReadings"" EXCLUDING CONSTRAINTS) ON COMMIT DROP;
            ALTER TABLE ""TempMeterReadings"" DROP COLUMN ""ReadingId"";
        ", connection, transaction);
        await tempTableCmd.ExecuteNonQueryAsync(cancellationToken);

        using (var writer = await connection.BeginBinaryImportAsync(@"
            COPY ""TempMeterReadings""
            (""MeterId"", ""Timestamp"", ""Value"", ""Quality"", ""CompanyId"")
            FROM STDIN (FORMAT BINARY)", cancellationToken))
        {
            foreach (var snapshot in snapshots)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await writer.StartRowAsync(cancellationToken);
                await writer.WriteAsync(snapshot.MeterId, NpgsqlDbType.Integer, cancellationToken);
                await writer.WriteAsync(snapshot.Timestamp, NpgsqlDbType.Timestamp, cancellationToken);
                await writer.WriteAsync(snapshot.Value, NpgsqlDbType.Numeric, cancellationToken);
                await writer.WriteAsync(snapshot.Quality, NpgsqlDbType.Integer, cancellationToken);
                await writer.WriteAsync(companyId, NpgsqlDbType.Integer, cancellationToken);
            }
            await writer.CompleteAsync(cancellationToken);
        }

        using var insertCmd = new NpgsqlCommand(@"
            INSERT INTO ""MeterReadings""
            (""MeterId"", ""Timestamp"", ""Value"", ""Quality"", ""CompanyId"")
            SELECT ""MeterId"", ""Timestamp"", ""Value"", ""Quality"", @companyId
            FROM ""TempMeterReadings""
            ON CONFLICT (""MeterId"", ""Timestamp"") DO NOTHING", connection, transaction);
        insertCmd.Parameters.AddWithValue("companyId", companyId);
        insertCmd.CommandTimeout = 300;
        return await insertCmd.ExecuteNonQueryAsync(cancellationToken);
    }
}
