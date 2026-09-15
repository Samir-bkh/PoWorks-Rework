using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using PoWorks_Rework.Models;
using PoWorks_Rework.Services;
using Xunit;

namespace PoWorks_Rework.Tests;

public class ConsumptionBillingConsistencyTests
{
    [Fact]
    public void KnownKwhCounter_ProducesPredictable150Kwh()
    {
        var readings = new[]
        {
            new ConsumptionReadingPoint(new DateTime(2026, 9, 1, 0, 0, 0), 100m),
            new ConsumptionReadingPoint(new DateTime(2026, 9, 1, 1, 0, 0), 160m),
            new ConsumptionReadingPoint(new DateTime(2026, 9, 1, 2, 0, 0), 250m)
        };

        var consumption = ConsumptionFormula.CalculateConsumption("kWh", readings);

        Assert.Equal(150m, consumption);
    }

    [Fact]
    public void KwhCounterReset_DoesNotCreateNegativeConsumption()
    {
        var readings = new[]
        {
            new ConsumptionReadingPoint(new DateTime(2026, 9, 1, 0, 0, 0), 100m),
            new ConsumptionReadingPoint(new DateTime(2026, 9, 1, 1, 0, 0), 120m),
            new ConsumptionReadingPoint(new DateTime(2026, 9, 1, 2, 0, 0), 5m),
            new ConsumptionReadingPoint(new DateTime(2026, 9, 1, 3, 0, 0), 25m)
        };

        var consumption = ConsumptionFormula.CalculateConsumption("kWh", readings);

        Assert.Equal(45m, consumption);
    }

    [Fact]
    public void RateStyleReadings_PreserveLegacyTimeIntegrationSemantics()
    {
        var readings = new[]
        {
            new ConsumptionReadingPoint(new DateTime(2026, 9, 1, 0, 0, 0), 10m),
            new ConsumptionReadingPoint(new DateTime(2026, 9, 1, 1, 0, 0), 20m),
            new ConsumptionReadingPoint(new DateTime(2026, 9, 1, 2, 0, 0), 30m)
        };

        var consumption = ConsumptionFormula.CalculateConsumption("kW", readings);

        Assert.Equal(30m, consumption);
    }

    [Theory]
    [InlineData("Wh")]
    [InlineData("kWh")]
    [InlineData("MWh")]
    [InlineData("W")]
    [InlineData("kW")]
    [InlineData("MW")]
    public void EnergyUnits_AreRecognized(string unit)
    {
        Assert.True(ConsumptionFormula.IsSupportedEnergyUnit(unit));
        Assert.Equal("kWh", ConsumptionFormula.GetConsumptionUnit(unit));
    }

    [Theory]
    [InlineData("°C")]
    [InlineData("bar")]
    [InlineData("m3")]
    [InlineData("m³/h")]
    [InlineData("%")]
    [InlineData("")]
    public void NonEnergySensorUnits_AreNotTreatedAsConsumption(string unit)
    {
        Assert.False(ConsumptionFormula.IsSupportedEnergyUnit(unit));

        var readings = new[]
        {
            new ConsumptionReadingPoint(new DateTime(2026, 9, 1, 0, 0, 0), 10m),
            new ConsumptionReadingPoint(new DateTime(2026, 9, 1, 1, 0, 0), 20m)
        };

        Assert.Equal(0m, ConsumptionFormula.CalculateConsumption(unit, readings));
    }

    [Fact]
    public void WhAndMWhCounters_AreNormalizedToKwh()
    {
        var start = new DateTime(2026, 9, 1, 0, 0, 0);
        var wh = new[]
        {
            new ConsumptionReadingPoint(start, 1000m),
            new ConsumptionReadingPoint(start.AddHours(1), 2500m)
        };
        var mwh = new[]
        {
            new ConsumptionReadingPoint(start, 2m),
            new ConsumptionReadingPoint(start.AddHours(1), 2.5m)
        };

        Assert.Equal(1.5m, ConsumptionFormula.CalculateConsumption("Wh", wh));
        Assert.Equal(500m, ConsumptionFormula.CalculateConsumption("MWh", mwh));
    }

    [Fact]
    public void PowerUnits_AreIntegratedAndNormalizedToKwh()
    {
        var start = new DateTime(2026, 9, 1, 0, 0, 0);
        var watts = new[]
        {
            new ConsumptionReadingPoint(start, 1000m),
            new ConsumptionReadingPoint(start.AddHours(1), 1000m)
        };
        var megawatts = new[]
        {
            new ConsumptionReadingPoint(start, 0.001m),
            new ConsumptionReadingPoint(start.AddHours(1), 0.001m)
        };

        Assert.Equal(1m, ConsumptionFormula.CalculateConsumption("W", watts));
        Assert.Equal(1m, ConsumptionFormula.CalculateConsumption("MW", megawatts));
    }

    [Fact]
    public void TieredTariff_Known150Kwh_ProducesPredictableCharge()
    {
        var charge = BillingCalculationEngine.CalculateTieredCharge(
            consumption: 150m,
            baseRate: 0.50m,
            threshold1: 100m,
            threshold1Rate: 0.60m,
            threshold2: 200m,
            threshold2Rate: 0.80m);

        Assert.Equal(80m, charge);

        var subtotalWithMonthlyFee = charge + 10m;
        var tax = BillingCalculationEngine.CalculateTax(subtotalWithMonthlyFee, 0.08m);

        Assert.Equal(7.20m, tax);
        Assert.Equal(97.20m, subtotalWithMonthlyFee + tax);
    }

    [Fact]
    public void DashboardAndBilling_UseSameConsumptionService()
    {
        var dashboard = ReadSource("Services", "DashboardDataService.cs");
        var billing = ReadSource("Services", "BillingService.cs");
        var shared = ReadSource("Services", "ConsumptionCalculationService.cs");

        Assert.Contains("_consumptionCalculationService.GetConsumptionSeriesAsync(filters)", dashboard);
        Assert.Contains("_consumptionCalculationService.GetMeterConsumptionTotalsAsync", billing);

        Assert.Contains("LAG(mr.\"\"Value\"\")", shared);
        Assert.Contains("mr.\"\"CompanyId\"\" = @CompanyId", shared);
        Assert.Contains("m.\"\"CompanyId\"\" = mr.\"\"CompanyId\"\"", shared);

        Assert.DoesNotContain("MAX(\"\"Value\"\") - MIN(\"\"Value\"\")", billing);
        Assert.DoesNotContain("SUM(mr.\"\"Value\"\") as TotalConsumption", dashboard);
    }

    [Fact]
    public void Billing_UsesStructuredProgressiveTenantTariff()
    {
        var billing = ReadSource("Services", "BillingService.cs");

        Assert.Contains(@"""BaseRate""", billing);
        Assert.Contains(@"""Threshold1""", billing);
        Assert.Contains(@"""Threshold1Rate""", billing);
        Assert.Contains(@"""Threshold2""", billing);
        Assert.Contains(@"""Threshold2Rate""", billing);
        Assert.Contains("BillingCalculationEngine.CalculateTieredCharge", billing);
        Assert.Contains("ConsumptionFormula.IsSupportedEnergyUnit", billing);
        Assert.Contains("ConsumptionFormula.GetConsumptionUnit", billing);
    }

    [Fact]
    public void Schema_IndexesWorkspaceMeterTimestampConsumptionQueries()
    {
        var schema = ReadSource("wwwroot", "sql", "initial_schema.sql");

        Assert.Contains("idx_meterreadings_company_meter_timestamp", schema);
        Assert.Contains(@"""CompanyId"", ""MeterId"", ""Timestamp""", schema);
    }

    [Fact]
    public async Task PostgreSql_KnownData_FlowsConsistentlyFromDashboardToSavedBill()
    {
        var connectionString = Environment.GetEnvironmentVariable("POWORKS_TEST_POSTGRES");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            // Local developers do not need PostgreSQL just to run the unit suite.
            // CI always supplies this variable and executes the real database path.
            return;
        }

        await ResetIntegrationSchemaAsync(connectionString);

        var connectionBuilder = new NpgsqlConnectionStringBuilder(connectionString);
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DatabaseSettings:Host"] = connectionBuilder.Host,
                ["DatabaseSettings:Port"] = connectionBuilder.Port.ToString(),
                ["DatabaseSettings:Database"] = connectionBuilder.Database,
                ["DatabaseSettings:Username"] = connectionBuilder.Username,
                ["DatabaseSettings:Password"] = connectionBuilder.Password,
                ["DatabaseSettings:SSLMode"] = "Disable",
                ["EncryptionKey"] = "integration-test-key"
            })
            .Build();

        var encryption = new EncryptionService(configuration);
        var database = new DatabaseService(configuration, encryption);
        var companyContext = new FixedCompanyContext(1);

        var consumptionService = new ConsumptionCalculationService(
            database,
            companyContext,
            NullLogger<ConsumptionCalculationService>.Instance);

        var dashboardService = new DashboardDataService(
            database,
            companyContext,
            consumptionService,
            NullLogger<DashboardDataService>.Instance);

        var billingService = new BillingService(
            database,
            companyContext,
            consumptionService,
            NullLogger<BillingService>.Instance);

        var start = new DateTime(2026, 9, 1);
        var end = new DateTime(2026, 9, 1, 23, 59, 59);

        var totals = await consumptionService.GetMeterConsumptionTotalsAsync(
            new[] { 101, 201 },
            start,
            end);

        Assert.Equal(150m, totals[101]);
        Assert.Equal(0m, totals[201]); // meter 201 belongs to workspace 2 and must never leak

        var dashboardSeries = await dashboardService.GetMeterReadingsAsync(
            new MeterReadingFilters
            {
                MeterIds = new List<int> { 101, 102, 103 },
                StartDate = start,
                EndDate = end,
                DateFilter = "daily",
                GroupBy = "meter",
                ActiveOnly = true,
                IncludeNullTenants = true
            });

        Assert.Equal(2, dashboardSeries.Count);
        Assert.DoesNotContain(dashboardSeries, row => row.MeterId == 102);
        Assert.All(dashboardSeries, row => Assert.Equal("kWh", row.Unit));
        Assert.Equal(
            150d,
            dashboardSeries.Single(row => row.MeterId == 101).TotalConsumption,
            6);
        Assert.Equal(
            30d,
            dashboardSeries.Single(row => row.MeterId == 103).TotalConsumption,
            6);

        var visibleMeters = await dashboardService.GetActiveMetersWithDataAsync(
            new MeterReadingFilters
            {
                StartDate = start,
                EndDate = end,
                Limit = 20,
                IncludeNullTenants = true,
                ActiveOnly = true
            });

        Assert.Contains(visibleMeters, meter => meter.MeterId == 101);
        Assert.Contains(visibleMeters, meter => meter.MeterId == 103);
        Assert.DoesNotContain(visibleMeters, meter => meter.MeterId == 102);
        Assert.DoesNotContain(visibleMeters, meter => meter.MeterId == 201);

        var bill = await billingService.CalculateBillAsync(10, start, end);

        Assert.Single(bill.LineItems);
        Assert.Equal(150m, bill.LineItems[0].Consumption);
        Assert.Equal(80m, bill.LineItems[0].LineTotalExclTax);
        Assert.Equal(150m, bill.TotalKWh);
        Assert.Equal(90m, bill.AmountExclTax);
        Assert.Equal(7.20m, bill.TaxAmount);
        Assert.Equal(97.20m, bill.AmountInclTax);

        var billId = await billingService.SaveBillAsync(bill);
        Assert.True(billId > 0);

        await using var verify = new NpgsqlConnection(connectionString);
        await verify.OpenAsync();

        await using (var billCommand = new NpgsqlCommand(@"
            SELECT ""TotalKWh"", ""MontantHT"", ""MontantTVA"", ""MontantTTC"", ""CompanyId""
            FROM ""Bills""
            WHERE ""BillId"" = @billId", verify))
        {
            billCommand.Parameters.AddWithValue("billId", billId);

            await using var reader = await billCommand.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.Equal(150m, reader.GetDecimal(0));
            Assert.Equal(90m, reader.GetDecimal(1));
            Assert.Equal(7.20m, reader.GetDecimal(2));
            Assert.Equal(97.20m, reader.GetDecimal(3));
            Assert.Equal(1, reader.GetInt32(4));
        }

        await using (var lineCommand = new NpgsqlCommand(@"
            SELECT ""Consumption"", ""LineTotalHT""
            FROM ""BillLineItems""
            WHERE ""BillId"" = @billId", verify))
        {
            lineCommand.Parameters.AddWithValue("billId", billId);

            await using var reader = await lineCommand.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.Equal(150m, reader.GetDecimal(0));
            Assert.Equal(80m, reader.GetDecimal(1));
            Assert.False(await reader.ReadAsync());
        }
    }

    private static async Task ResetIntegrationSchemaAsync(string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();

        const string sql = @"
            DROP TABLE IF EXISTS ""BillLineItems"";
            DROP TABLE IF EXISTS ""Bills"";
            DROP TABLE IF EXISTS ""MeterReadings"";
            DROP TABLE IF EXISTS ""Meters"";
            DROP TABLE IF EXISTS ""TenantDetails"";
            DROP TABLE IF EXISTS ""Tenants"";

            CREATE TABLE ""Tenants"" (
                ""TenantID"" INTEGER PRIMARY KEY,
                ""DisplayName"" VARCHAR(100) NOT NULL,
                ""CompanyId"" INTEGER NOT NULL
            );

            CREATE TABLE ""TenantDetails"" (
                ""TenantID"" INTEGER NOT NULL,
                ""CompanyId"" INTEGER NOT NULL,
                ""CompanyName"" VARCHAR(100),
                ""BaseRate"" NUMERIC(12,4),
                ""Threshold1"" NUMERIC(12,3),
                ""Threshold1Rate"" NUMERIC(12,4),
                ""Threshold2"" NUMERIC(12,3),
                ""Threshold2Rate"" NUMERIC(12,4),
                ""Tarif_1"" NUMERIC(12,4),
                ""Tarif_2"" NUMERIC(12,4),
                ""Tarif_3"" NUMERIC(12,4),
                ""AbonnementMensuel"" NUMERIC(10,2)
            );

            CREATE TABLE ""Meters"" (
                ""MeterId"" INTEGER PRIMARY KEY,
                ""Name"" VARCHAR(100) NOT NULL,
                ""Label"" VARCHAR(150),
                ""Unit"" VARCHAR(20) NOT NULL,
                ""Type"" VARCHAR(20) NOT NULL DEFAULT 'Main',
                ""LastReading"" INTEGER NOT NULL DEFAULT 0,
                ""Active"" BOOLEAN NOT NULL,
                ""TenantID"" INTEGER,
                ""CompanyId"" INTEGER NOT NULL
            );

            CREATE TABLE ""MeterReadings"" (
                ""ReadingId"" SERIAL PRIMARY KEY,
                ""MeterId"" INTEGER NOT NULL,
                ""Timestamp"" TIMESTAMP NOT NULL,
                ""Value"" NUMERIC NOT NULL,
                ""CompanyId"" INTEGER NOT NULL
            );

            CREATE TABLE ""Bills"" (
                ""BillId"" SERIAL PRIMARY KEY,
                ""TenantID"" INTEGER NOT NULL,
                ""BillNumber"" VARCHAR(50),
                ""PeriodStart"" DATE NOT NULL,
                ""PeriodEnd"" DATE NOT NULL,
                ""TotalKWh"" NUMERIC(12,3),
                ""MontantHT"" NUMERIC(10,2),
                ""MontantTVA"" NUMERIC(10,2),
                ""MontantTTC"" NUMERIC(10,2),
                ""GrandTotal"" NUMERIC(10,2),
                ""Status"" VARCHAR(20),
                ""CompanyId"" INTEGER NOT NULL
            );

            CREATE TABLE ""BillLineItems"" (
                ""LineItemId"" SERIAL PRIMARY KEY,
                ""BillId"" INTEGER NOT NULL,
                ""MeterId"" INTEGER,
                ""MeterName"" VARCHAR(100),
                ""Consumption"" NUMERIC(12,3),
                ""Unit"" VARCHAR(20),
                ""UnitPrice"" NUMERIC(10,4),
                ""LineTotalHT"" NUMERIC(10,2)
            );

            INSERT INTO ""Tenants"" (""TenantID"", ""DisplayName"", ""CompanyId"")
            VALUES
                (10, 'Known Tenant', 1),
                (20, 'Other Workspace Tenant', 2);

            INSERT INTO ""TenantDetails"" (
                ""TenantID"", ""CompanyId"", ""CompanyName"",
                ""BaseRate"", ""Threshold1"", ""Threshold1Rate"",
                ""Threshold2"", ""Threshold2Rate"",
                ""Tarif_1"", ""Tarif_2"", ""Tarif_3"", ""AbonnementMensuel"")
            VALUES (
                10, 1, 'Known Tenant',
                0.50, 100, 0.60,
                200, 0.80,
                0.50, 0.60, 0.80, 10.00);

            INSERT INTO ""Meters"" (
                ""MeterId"", ""Name"", ""Unit"", ""Active"", ""TenantID"", ""CompanyId"")
            VALUES
                (101, 'Known.kWh', 'kWh', TRUE, 10, 1),
                (102, 'Room.Temperature', '°C', TRUE, 10, 1),
                (103, 'Plant.Power', 'kW', TRUE, NULL, 1),
                (201, 'OtherWorkspace.kWh', 'kWh', TRUE, 20, 2);

            INSERT INTO ""MeterReadings"" (
                ""MeterId"", ""Timestamp"", ""Value"", ""CompanyId"")
            VALUES
                (101, '2026-09-01 00:00:00', 100, 1),
                (101, '2026-09-01 01:00:00', 160, 1),
                (101, '2026-09-01 02:00:00', 250, 1),
                (102, '2026-09-01 00:00:00', 22, 1),
                (102, '2026-09-01 01:00:00', 24, 1),
                (102, '2026-09-01 02:00:00', 23, 1),
                (103, '2026-09-01 00:00:00', 10, 1),
                (103, '2026-09-01 01:00:00', 20, 1),
                (103, '2026-09-01 02:00:00', 30, 1),
                (201, '2026-09-01 00:00:00', 1000, 2),
                (201, '2026-09-01 01:00:00', 9000, 2);";

        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private static string ReadSource(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "PoWorks Rework.sln")))
            {
                var path = Path.Combine(
                    new[] { directory.FullName, "PoWorks Rework" }
                        .Concat(parts)
                        .ToArray());

                Assert.True(File.Exists(path), $"Source file not found: {path}");
                return File.ReadAllText(path);
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root.");
    }

    private sealed class FixedCompanyContext : ICompanyContext
    {
        public FixedCompanyContext(int companyId)
        {
            CurrentCompanyId = companyId;
        }

        public int CurrentCompanyId { get; }
    }
}
