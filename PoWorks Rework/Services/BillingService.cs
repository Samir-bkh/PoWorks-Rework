using Microsoft.Extensions.Logging;
using Npgsql;
using PoWorks_Rework.Models;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace PoWorks_Rework.Services
{
    /// <summary>
    /// Service for billing calculations and invoice generation.
    /// Handles consumption-based bill calculation with tiered pricing and tax application.
    /// Supports multiple meters per tenant with flexible rate structures.
    /// </summary>
    public class BillingService
    {
        private readonly DatabaseService _databaseService;
        private readonly ILogger<BillingService> _logger;
        /// <summary>
        /// Malaysia Service and Sales Tax (SST) rate (8%)
        /// </summary>
        private const decimal MALAYSIA_SST_RATE = 0.08m;

        /// <summary>
        /// Initializes the billing service with database and logging dependencies.
        /// </summary>
        public BillingService(DatabaseService databaseService, ILogger<BillingService> logger)
        {
            _databaseService = databaseService;
            _logger = logger;
        }

        /// <summary>
        /// Calculates a complete bill for a tenant over a specified date range.
        /// Queries all active meters, calculates consumption, applies rates, and computes tax.
        /// </summary>
        public async Task<BillEntity> CalculateBillAsync(int tenantId, DateTime startDate, DateTime endDate)
        {
      
            DateTime adjustedEndDate = endDate.Date.AddDays(1).AddTicks(-1);

            _logger.LogInformation("Calculating bill for Tenant {TenantId} from {Start} to {End}", tenantId, startDate, adjustedEndDate);

            var bill = new BillEntity
            {
                TenantID = tenantId,
                PeriodStart = startDate,
                PeriodEnd = endDate, 
                GeneratedAt = DateTime.Now,
                Status = "Draft"
            };

            var connection = _databaseService.GetConnection();
            if (connection.State == System.Data.ConnectionState.Closed)
            {
                await connection.OpenAsync();
            }
            string tenantQuery = @"
                SELECT t.""DisplayName"", td.""Tarif_1"", td.""AbonnementMensuel"" 
                FROM ""Tenants"" t
                LEFT JOIN ""TenantDetails"" td ON t.""TenantID"" = td.""TenantID""
                WHERE t.""TenantID"" = @tenantId";

            using var cmdTenant = new NpgsqlCommand(tenantQuery, connection);
            cmdTenant.Parameters.AddWithValue("tenantId", tenantId);

            decimal unitPriceRM = 0;
            decimal monthlyFeeRM = 0;

            using (var reader = await cmdTenant.ExecuteReaderAsync())
            {
                if (await reader.ReadAsync())
                {
                    bill.TenantName = reader.GetString(0);
                    unitPriceRM = reader.IsDBNull(1) ? 0 : reader.GetDecimal(1);
                    monthlyFeeRM = reader.IsDBNull(2) ? 0 : reader.GetDecimal(2);
                }
                else
                {
                    throw new Exception($"Tenant not found (ID: {tenantId})");
                }
            }
            string metersQuery = @"SELECT ""MeterId"", ""Name"", ""Unit"" FROM ""Meters"" WHERE ""TenantID"" = @tenantId AND ""Active"" = true";
            using var cmdMeters = new NpgsqlCommand(metersQuery, connection);
            cmdMeters.Parameters.AddWithValue("tenantId", tenantId);

            var meters = new List<(int Id, string Name, string Unit)>();
            using (var reader = await cmdMeters.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                {
                    meters.Add((reader.GetInt32(0), reader.GetString(1), reader.GetString(2)));
                }
            }
            foreach (var meter in meters)
            {
                decimal consumption = await CalculateMeterConsumptionAsync(connection, meter.Id, meter.Unit, startDate, adjustedEndDate);

                if (consumption > 0)
                {
                    var lineItem = new BillLineItemEntity
                    {
                        MeterId = meter.Id,
                        MeterName = meter.Name,
                        Unit = meter.Unit,
                        Consumption = consumption,
                        UnitPrice = unitPriceRM,
                        LineTotalExclTax = Math.Round(consumption * unitPriceRM, 2)
                    };

                    bill.LineItems.Add(lineItem);
                    bill.TotalKWh += consumption;
                    bill.AmountExclTax += lineItem.LineTotalExclTax;
                }
            }
            bill.AmountExclTax += monthlyFeeRM;
            bill.TaxAmount = Math.Round(bill.AmountExclTax * MALAYSIA_SST_RATE, 2);
            bill.AmountInclTax = bill.AmountExclTax + bill.TaxAmount;

            return bill;
        }

        /// <summary>
        /// Calculates the consumption for a single meter over the given period.
        /// For kWh meters, uses the difference between max and min readings.
        /// For other units, integrates the value over time using hour deltas.
        /// </summary>
        /// <param name="connection">The database connection to use.</param>
        /// <param name="meterId">The meter ID to calculate consumption for.</param>
        /// <param name="unit">The meter's unit of measurement.</param>
        /// <param name="start">The start of the billing period.</param>
        /// <param name="end">The end of the billing period.</param>
        /// <returns>The calculated consumption value.</returns>
        private async Task<decimal> CalculateMeterConsumptionAsync(NpgsqlConnection connection, int meterId, string unit, DateTime start, DateTime end)
        {
            if (unit.Equals("kWh", StringComparison.OrdinalIgnoreCase))
            {
                string query = @"
                    SELECT COALESCE(MAX(""Value"") - MIN(""Value""), 0) 
                    FROM ""MeterReadings"" 
                    WHERE ""MeterId"" = @meterId 
                    AND ""Timestamp"" >= @start AND ""Timestamp"" <= @end";

                using var cmd = new NpgsqlCommand(query, connection);
                cmd.Parameters.AddWithValue("meterId", meterId);
                cmd.Parameters.AddWithValue("start", start);
                cmd.Parameters.AddWithValue("end", end);

                var result = await cmd.ExecuteScalarAsync();
                return result != DBNull.Value ? Convert.ToDecimal(result) : 0;
            }
            else
            {
                string query = @"
                    WITH DataWithDelta AS (
                        SELECT ""Value"", 
                               EXTRACT(EPOCH FROM (LEAD(""Timestamp"") OVER (ORDER BY ""Timestamp"") - ""Timestamp"")) / 3600.0 AS HoursDelta
                        FROM ""MeterReadings""
                        WHERE ""MeterId"" = @meterId AND ""Timestamp"" >= @start AND ""Timestamp"" <= @end
                    )
                    SELECT COALESCE(SUM(""Value"" * HoursDelta), 0) FROM DataWithDelta WHERE HoursDelta IS NOT NULL;";

                using var cmd = new NpgsqlCommand(query, connection);
                cmd.Parameters.AddWithValue("meterId", meterId);
                cmd.Parameters.AddWithValue("start", start);
                cmd.Parameters.AddWithValue("end", end);

                var result = await cmd.ExecuteScalarAsync();
                return result != DBNull.Value ? Math.Round(Convert.ToDecimal(result), 3) : 0;
            }
        }


        /// <summary>
        /// Saves a calculated bill and its line items to the database in a transaction.
        /// </summary>
        /// <param name="bill">The bill entity to persist.</param>
        /// <returns>The newly created bill ID.</returns>
        public async Task<int> SaveBillAsync(BillEntity bill)
        {
            var connection = _databaseService.GetConnection();
            if (connection.State == System.Data.ConnectionState.Closed)
            {
                await connection.OpenAsync();
            }
            using var transaction = await connection.BeginTransactionAsync();
            try
            {
                string insertBillQuery = @"
    INSERT INTO ""Bills"" (
        ""TenantID"", ""BillNumber"", ""PeriodStart"", ""PeriodEnd"", 
        ""TotalKWh"", ""MontantHT"", ""MontantTVA"", ""MontantTTC"", ""GrandTotal"", ""Status""
    ) 
    VALUES (
        @tenantId, @billNumber, @start, @end, 
        @totalKwh, @subTotal, @tax, @grandTotal, @grandTotal, 'Draft'
    ) RETURNING ""BillId"";";

                using var cmdBill = new NpgsqlCommand(insertBillQuery, connection, transaction);
                cmdBill.Parameters.AddWithValue("tenantId", bill.TenantID);
                cmdBill.Parameters.AddWithValue("billNumber", $"BILL-{bill.TenantID}-{DateTime.Now:yyyyMMddHHmmss}");
                cmdBill.Parameters.AddWithValue("start", bill.PeriodStart);
                cmdBill.Parameters.AddWithValue("end", bill.PeriodEnd);
                cmdBill.Parameters.AddWithValue("totalKwh", bill.TotalKWh);
                cmdBill.Parameters.AddWithValue("subTotal", bill.AmountExclTax);
                cmdBill.Parameters.AddWithValue("tax", bill.TaxAmount);
                cmdBill.Parameters.AddWithValue("grandTotal", bill.AmountInclTax);
                int newBillId = Convert.ToInt32(await cmdBill.ExecuteScalarAsync());
                string insertLineQuery = @"
    INSERT INTO ""BillLineItems"" (
        ""BillId"", ""MeterId"", ""MeterName"", ""Consumption"", 
        ""Unit"", ""UnitPrice"", ""LineTotalHT""
    ) 
    VALUES (
        @billId, @meterId, @meterName, @consumption, 
        @unit, @unitPrice, @lineTotal
    );";

                foreach (var item in bill.LineItems)
                {
                    using var cmdLine = new NpgsqlCommand(insertLineQuery, connection, transaction);
                    cmdLine.Parameters.AddWithValue("billId", newBillId);
                    cmdLine.Parameters.AddWithValue("meterId", item.MeterId);
                    cmdLine.Parameters.AddWithValue("meterName", item.MeterName);
                    cmdLine.Parameters.AddWithValue("consumption", item.Consumption);
                    cmdLine.Parameters.AddWithValue("unit", item.Unit);
                    cmdLine.Parameters.AddWithValue("unitPrice", item.UnitPrice);
                    cmdLine.Parameters.AddWithValue("lineTotal", item.LineTotalExclTax);

                    await cmdLine.ExecuteNonQueryAsync();
                }
                await transaction.CommitAsync();
                return newBillId;
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex, "Error saving the invoice");
                throw;
            }
        }
    }
}