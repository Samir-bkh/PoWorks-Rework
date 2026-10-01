using Npgsql;

namespace PoWorks_Rework.Services
{
    public sealed record WorkspaceMeterResetResult(
        int Meters,
        int RawReadings,
        int DailyReadings,
        int MonthlyReadings,
        int YearlyReadings,
        int InvoiceLinksDetached)
    {
        public long Readings => (long)RawReadings + DailyReadings + MonthlyReadings + YearlyReadings;
    }

    /// <summary>
    /// Removes imported meters and readings from one workspace in the caller's
    /// transaction. Existing invoices retain their saved line details and totals.
    /// </summary>
    public static class WorkspaceMeterReset
    {
        public static async Task<WorkspaceMeterResetResult> ExecuteAsync(
            NpgsqlConnection connection,
            NpgsqlTransaction transaction,
            int companyId,
            CancellationToken cancellationToken = default)
        {
            if (companyId <= 0)
                throw new ArgumentOutOfRangeException(nameof(companyId));

            // A line item keeps its archived meter name even if it was created
            // before the application started storing that snapshot explicitly.
            var invoiceLinks = await ExecuteAsync(@"
                UPDATE ""BillLineItems"" AS line
                SET ""MeterName"" = COALESCE(NULLIF(line.""MeterName"", ''), meter.""Name""),
                    ""MeterId"" = NULL
                FROM ""Meters"" AS meter, ""Bills"" AS bill
                WHERE line.""MeterId"" = meter.""MeterId""
                  AND line.""BillId"" = bill.""BillId""
                  AND meter.""CompanyId"" = @CompanyId
                  AND bill.""CompanyId"" = @CompanyId", connection, transaction,
                companyId, cancellationToken);

            // Legacy imports can have the wrong CompanyId even though their
            // MeterId still references a meter in this workspace. Remove both
            // kinds of rows so the meter foreign keys cannot block the reset.
            var raw = await DeleteReadingsAsync("MeterReadings", connection, transaction,
                companyId, cancellationToken);
            var daily = await DeleteReadingsAsync("MeterReadingsDaily", connection, transaction,
                companyId, cancellationToken);
            var monthly = await DeleteReadingsAsync("MeterReadingsMonthly", connection, transaction,
                companyId, cancellationToken);
            var yearly = await DeleteReadingsAsync("MeterReadingsYearly", connection, transaction,
                companyId, cancellationToken);

            // Detach the hierarchy before deleting both parents and children.
            await ExecuteAsync(@"
                UPDATE ""Meters""
                SET ""ParentId"" = NULL
                WHERE ""CompanyId"" = @CompanyId
                  AND ""ParentId"" IS NOT NULL", connection, transaction,
                companyId, cancellationToken);

            var meters = await ExecuteAsync(@"
                DELETE FROM ""Meters""
                WHERE ""CompanyId"" = @CompanyId", connection, transaction,
                companyId, cancellationToken);

            return new WorkspaceMeterResetResult(meters, raw, daily, monthly,
                yearly, invoiceLinks);
        }

        private static Task<int> DeleteReadingsAsync(
            string tableName,
            NpgsqlConnection connection,
            NpgsqlTransaction transaction,
            int companyId,
            CancellationToken cancellationToken) =>
            ExecuteAsync($@"
                DELETE FROM ""{tableName}"" AS reading
                WHERE reading.""CompanyId"" = @CompanyId
                   OR reading.""MeterId"" IN (
                       SELECT ""MeterId"" FROM ""Meters""
                       WHERE ""CompanyId"" = @CompanyId)",
                connection, transaction, companyId, cancellationToken);

        private static async Task<int> ExecuteAsync(
            string sql,
            NpgsqlConnection connection,
            NpgsqlTransaction transaction,
            int companyId,
            CancellationToken cancellationToken)
        {
            await using var command = new NpgsqlCommand(sql, connection, transaction)
            {
                CommandTimeout = 300
            };
            command.Parameters.AddWithValue("@CompanyId", companyId);
            return await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }
}
