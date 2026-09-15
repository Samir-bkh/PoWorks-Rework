using Npgsql;
using PoWorks_Rework.Models;

namespace PoWorks_Rework.Services
{
    /// <summary>
    /// Calculates and persists tenant bills.
    /// Consumption is delegated to ConsumptionCalculationService so invoices
    /// use the exact same meter/date semantics as the dashboard.
    /// </summary>
    public class BillingService
    {
        private const decimal MALAYSIA_SST_RATE = 0.08m;

        private readonly DatabaseService _databaseService;
        private readonly ICompanyContext _companyContext;
        private readonly ConsumptionCalculationService _consumptionCalculationService;
        private readonly ILogger<BillingService> _logger;

        public BillingService(
            DatabaseService databaseService,
            ICompanyContext companyContext,
            ConsumptionCalculationService consumptionCalculationService,
            ILogger<BillingService> logger)
        {
            _databaseService = databaseService;
            _companyContext = companyContext;
            _consumptionCalculationService = consumptionCalculationService;
            _logger = logger;
        }

        /// <summary>
        /// Calculates a complete bill for a tenant over a specified date range.
        /// Each active assigned meter uses the shared consumption engine, then
        /// the tenant's progressive tariff is applied to that meter's consumption.
        /// </summary>
        public async Task<BillEntity> CalculateBillAsync(
            int tenantId,
            DateTime startDate,
            DateTime endDate)
        {
            if (tenantId <= 0)
                throw new ArgumentOutOfRangeException(nameof(tenantId));

            if (endDate.Date < startDate.Date)
                throw new ArgumentException("Billing end date cannot be before the start date.");

            var adjustedStartDate = startDate.Date;
            var adjustedEndDate = endDate.Date.AddDays(1).AddTicks(-1);
            var companyId = _companyContext.CurrentCompanyId;

            _logger.LogInformation(
                "Calculating bill for Tenant {TenantId} in workspace {CompanyId} from {Start} to {End}",
                tenantId,
                companyId,
                adjustedStartDate,
                adjustedEndDate);

            var bill = new BillEntity
            {
                TenantID = tenantId,
                PeriodStart = adjustedStartDate,
                PeriodEnd = endDate.Date,
                GeneratedAt = DateTime.Now,
                Status = "Draft"
            };

            TenantTariff tariff;
            List<BillingMeter> meters;

            await using (var connection = _databaseService.CreateNewConnection())
            {
                await connection.OpenAsync();

                tariff = await LoadTenantTariffAsync(
                    connection,
                    tenantId,
                    companyId);

                bill.TenantName = tariff.TenantName;

                meters = await LoadActiveTenantMetersAsync(
                    connection,
                    tenantId,
                    companyId);
            }

            var consumptionByMeter =
                await _consumptionCalculationService.GetMeterConsumptionTotalsAsync(
                    meters.Select(m => m.Id).ToArray(),
                    adjustedStartDate,
                    adjustedEndDate);

            foreach (var meter in meters)
            {
                var consumption = consumptionByMeter.GetValueOrDefault(meter.Id);
                if (consumption <= 0m)
                    continue;

                var lineTotal = BillingCalculationEngine.CalculateTieredCharge(
                    consumption,
                    tariff.BaseRate,
                    tariff.Threshold1,
                    tariff.Threshold1Rate,
                    tariff.Threshold2,
                    tariff.Threshold2Rate);

                var effectiveUnitPrice = consumption > 0m
                    ? Math.Round(lineTotal / consumption, 4)
                    : 0m;

                bill.LineItems.Add(new BillLineItemEntity
                {
                    MeterId = meter.Id,
                    MeterName = meter.Name,
                    Unit = ConsumptionFormula.GetConsumptionUnit(meter.Unit),
                    Consumption = Math.Round(consumption, 3),
                    UnitPrice = effectiveUnitPrice,
                    LineTotalExclTax = lineTotal
                });

                if (ConsumptionFormula.IsSupportedEnergyUnit(meter.Unit))
                    bill.TotalKWh += Math.Round(consumption, 3);

                bill.AmountExclTax += lineTotal;
            }

            bill.AmountExclTax =
                Math.Round(bill.AmountExclTax + tariff.MonthlyFee, 2);
            bill.TaxAmount = BillingCalculationEngine.CalculateTax(
                bill.AmountExclTax,
                MALAYSIA_SST_RATE);
            bill.AmountInclTax =
                Math.Round(bill.AmountExclTax + bill.TaxAmount, 2);

            return bill;
        }

        public async Task<int> SaveBillAsync(BillEntity bill)
        {
            var companyId = _companyContext.CurrentCompanyId;

            await using var connection = _databaseService.CreateNewConnection();
            await connection.OpenAsync();
            await using var transaction = await connection.BeginTransactionAsync();

            try
            {
                await using (var tenantGuard = new NpgsqlCommand(@"
                    SELECT COUNT(*)
                    FROM ""Tenants""
                    WHERE ""TenantID"" = @tenantId
                      AND ""CompanyId"" = @companyId", connection, transaction))
                {
                    tenantGuard.Parameters.AddWithValue("tenantId", bill.TenantID);
                    tenantGuard.Parameters.AddWithValue("companyId", companyId);

                    var tenantCount =
                        Convert.ToInt32(await tenantGuard.ExecuteScalarAsync());

                    if (tenantCount == 0)
                    {
                        throw new InvalidOperationException(
                            "Tenant does not belong to the current workspace.");
                    }
                }

                const string insertBillQuery = @"
                    INSERT INTO ""Bills"" (
                        ""TenantID"", ""BillNumber"", ""PeriodStart"", ""PeriodEnd"",
                        ""TotalKWh"", ""MontantHT"", ""MontantTVA"", ""MontantTTC"",
                        ""GrandTotal"", ""Status"", ""CompanyId"")
                    VALUES (
                        @tenantId, @billNumber, @start, @end,
                        @totalKwh, @subTotal, @tax, @grandTotal,
                        @grandTotal, 'Draft', @companyId)
                    RETURNING ""BillId"";";

                await using var cmdBill =
                    new NpgsqlCommand(insertBillQuery, connection, transaction);

                cmdBill.Parameters.AddWithValue("tenantId", bill.TenantID);
                cmdBill.Parameters.AddWithValue(
                    "billNumber",
                    $"BILL-{bill.TenantID}-{DateTime.UtcNow:yyyyMMddHHmmssfff}");
                cmdBill.Parameters.AddWithValue("start", bill.PeriodStart);
                cmdBill.Parameters.AddWithValue("end", bill.PeriodEnd);
                cmdBill.Parameters.AddWithValue("totalKwh", bill.TotalKWh);
                cmdBill.Parameters.AddWithValue("subTotal", bill.AmountExclTax);
                cmdBill.Parameters.AddWithValue("tax", bill.TaxAmount);
                cmdBill.Parameters.AddWithValue("grandTotal", bill.AmountInclTax);
                cmdBill.Parameters.AddWithValue("companyId", companyId);

                var newBillId =
                    Convert.ToInt32(await cmdBill.ExecuteScalarAsync());

                const string insertLineQuery = @"
                    INSERT INTO ""BillLineItems"" (
                        ""BillId"", ""MeterId"", ""MeterName"", ""Consumption"",
                        ""Unit"", ""UnitPrice"", ""LineTotalHT"")
                    VALUES (
                        @billId, @meterId, @meterName, @consumption,
                        @unit, @unitPrice, @lineTotal);";

                foreach (var item in bill.LineItems)
                {
                    await using var cmdLine =
                        new NpgsqlCommand(insertLineQuery, connection, transaction);

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
                _logger.LogError(
                    ex,
                    "Error saving invoice for tenant {TenantId} in workspace {CompanyId}",
                    bill.TenantID,
                    companyId);
                throw;
            }
        }

        private static async Task<TenantTariff> LoadTenantTariffAsync(
            NpgsqlConnection connection,
            int tenantId,
            int companyId)
        {
            const string sql = @"
                SELECT
                    COALESCE(NULLIF(td.""CompanyName"", ''), t.""DisplayName""),
                    COALESCE(td.""BaseRate"", td.""Tarif_1""::numeric, 0),
                    COALESCE(td.""Threshold1"", 0),
                    COALESCE(
                        td.""Threshold1Rate"",
                        td.""Tarif_2""::numeric,
                        td.""BaseRate"",
                        td.""Tarif_1""::numeric,
                        0),
                    COALESCE(td.""Threshold2"", td.""Threshold1"", 0),
                    COALESCE(
                        td.""Threshold2Rate"",
                        td.""Tarif_3""::numeric,
                        td.""Threshold1Rate"",
                        td.""Tarif_2""::numeric,
                        td.""BaseRate"",
                        td.""Tarif_1""::numeric,
                        0),
                    COALESCE(td.""AbonnementMensuel"", 0)
                FROM ""Tenants"" t
                LEFT JOIN ""TenantDetails"" td
                  ON td.""TenantID"" = t.""TenantID""
                 AND td.""CompanyId"" = t.""CompanyId""
                WHERE t.""TenantID"" = @tenantId
                  AND t.""CompanyId"" = @companyId";

            await using var cmd = new NpgsqlCommand(sql, connection);
            cmd.Parameters.AddWithValue("tenantId", tenantId);
            cmd.Parameters.AddWithValue("companyId", companyId);

            await using var reader = await cmd.ExecuteReaderAsync();
            if (!await reader.ReadAsync())
                throw new InvalidOperationException($"Tenant not found (ID: {tenantId}).");

            return new TenantTariff(
                TenantName: reader.GetString(0),
                BaseRate: reader.GetDecimal(1),
                Threshold1: reader.GetDecimal(2),
                Threshold1Rate: reader.GetDecimal(3),
                Threshold2: reader.GetDecimal(4),
                Threshold2Rate: reader.GetDecimal(5),
                MonthlyFee: reader.GetDecimal(6));
        }

        private static async Task<List<BillingMeter>> LoadActiveTenantMetersAsync(
            NpgsqlConnection connection,
            int tenantId,
            int companyId)
        {
            const string sql = @"
                SELECT ""MeterId"", ""Name"", COALESCE(""Unit"", '')
                FROM ""Meters""
                WHERE ""TenantID"" = @tenantId
                  AND ""CompanyId"" = @companyId
                  AND ""Active"" = TRUE
                ORDER BY ""MeterId""";

            await using var cmd = new NpgsqlCommand(sql, connection);
            cmd.Parameters.AddWithValue("tenantId", tenantId);
            cmd.Parameters.AddWithValue("companyId", companyId);

            var meters = new List<BillingMeter>();
            await using var reader = await cmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                meters.Add(new BillingMeter(
                    reader.GetInt32(0),
                    reader.GetString(1),
                    reader.GetString(2)));
            }

            return meters;
        }

        private sealed record BillingMeter(int Id, string Name, string Unit);

        private sealed record TenantTariff(
            string TenantName,
            decimal BaseRate,
            decimal Threshold1,
            decimal Threshold1Rate,
            decimal Threshold2,
            decimal Threshold2Rate,
            decimal MonthlyFee);
    }
}
