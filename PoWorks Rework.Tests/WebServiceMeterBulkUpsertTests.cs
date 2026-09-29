using System.Diagnostics;
using Npgsql;
using PoWorks_Rework.Models;
using PoWorks_Rework.Services;
using Xunit;
using Xunit.Abstractions;

namespace PoWorks_Rework.Tests;

public class WebServiceMeterBulkUpsertTests
{
    private readonly ITestOutputHelper _output;

    public WebServiceMeterBulkUpsertTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task BulkUpsert_PreservesValidatedReimportSemanticsAndCompanyIsolation()
    {
        var connectionString = Environment.GetEnvironmentVariable("POWORKS_TEST_POSTGRES");
        if (string.IsNullOrWhiteSpace(connectionString)) return;

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await CreateTemporaryMetersTableAsync(connection, transaction);

        var parentId = await InsertMeterAsync(
            connection, transaction, "Parent", "kWh", 1, tenantId: 10, active: true);
        var existingId = await InsertMeterAsync(
            connection, transaction, "Existing.Energy", "Wh", 1, tenantId: 55, active: false);
        var keepUnitId = await InsertMeterAsync(
            connection, transaction, "Existing.Pressure", "bar", 1, tenantId: 56, active: true);
        var crossCompanyParentId = await InsertMeterAsync(
            connection, transaction, "Other.Parent", "kWh", 2, tenantId: null, active: true);
        var otherCompanySameNameId = await InsertMeterAsync(
            connection, transaction, "Cross.Company", "MWh", 2, tenantId: null, active: true);

        var variables = new List<WebServiceVariableWithTrends>
        {
            Variable("Existing.Energy", "kWh", active: true),
            Variable("Existing.Pressure", "", active: false),
            Variable("New.Child", "kWh", parentId: parentId.ToString()),
            Variable("New.InvalidParent", "kWh", parentId: crossCompanyParentId.ToString()),
            Variable("Cross.Company", "kWh"),
            Variable(new string('N', 101), "kWh"),
            Variable("Bad.Unit", new string('U', 21))
        };

        var result = await WebServiceMeterBulkUpsertService.UpsertAsync(
            connection,
            transaction,
            companyId: 1,
            variables);

        Assert.Equal(3, result.CreatedCount);
        Assert.Equal(1, result.UpdatedCount);
        Assert.Equal(1, result.UnchangedCount);
        Assert.Equal(2, result.Errors.Count);
        Assert.Equal(5, result.MeterIds.Count);

        Assert.Equal(existingId, result.MeterIds["Existing.Energy"]);
        Assert.Equal(keepUnitId, result.MeterIds["Existing.Pressure"]);

        var existing = await ReadMeterAsync(connection, transaction, existingId);
        Assert.Equal("kWh", existing.Unit);
        Assert.Equal(55, existing.TenantId);
        Assert.False(existing.Active); // Reimport must not overwrite existing metadata.

        var keepUnit = await ReadMeterAsync(connection, transaction, keepUnitId);
        Assert.Equal("bar", keepUnit.Unit); // Empty imported unit never erases PoWorks unit.
        Assert.Equal(56, keepUnit.TenantId);
        Assert.True(keepUnit.Active);

        var child = await ReadMeterAsync(connection, transaction, result.MeterIds["New.Child"]);
        Assert.Equal(parentId, child.ParentId);

        var invalidParent = await ReadMeterAsync(
            connection, transaction, result.MeterIds["New.InvalidParent"]);
        Assert.Null(invalidParent.ParentId);

        var companyOneCross = await ReadMeterAsync(
            connection, transaction, result.MeterIds["Cross.Company"]);
        Assert.Equal(1, companyOneCross.CompanyId);
        Assert.NotEqual(otherCompanySameNameId, companyOneCross.MeterId);

        var companyTwoCross = await ReadMeterAsync(connection, transaction, otherCompanySameNameId);
        Assert.Equal("MWh", companyTwoCross.Unit);
        Assert.Equal(2, companyTwoCross.CompanyId);

        await transaction.RollbackAsync();
    }

    [Fact]
    public async Task BulkUpsert_HandlesFiveThousandMetersInOneSetBasedOperation()
    {
        var connectionString = Environment.GetEnvironmentVariable("POWORKS_TEST_POSTGRES");
        if (string.IsNullOrWhiteSpace(connectionString)) return;

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await CreateTemporaryMetersTableAsync(connection, transaction);

        const int count = 5000;
        var variables = Enumerable.Range(0, count)
            .Select(index => Variable($"Load.Variable.{index:D5}", "kWh"))
            .ToList();

        var stopwatch = Stopwatch.StartNew();
        var first = await WebServiceMeterBulkUpsertService.UpsertAsync(
            connection,
            transaction,
            companyId: 1,
            variables);
        stopwatch.Stop();

        _output.WriteLine(
            "Bulk upsert of {0:N0} meters took {1:N0} ms on the CI PostgreSQL instance.",
            count,
            stopwatch.ElapsedMilliseconds);

        Assert.Equal(count, first.CreatedCount);
        Assert.Equal(0, first.UpdatedCount);
        Assert.Equal(0, first.UnchangedCount);
        Assert.Empty(first.Errors);
        Assert.Equal(count, first.MeterIds.Count);

        // This threshold is intentionally generous. It is not a micro-benchmark; it
        // catches accidental regressions back to pathological per-row round trips.
        Assert.True(
            stopwatch.Elapsed < TimeSpan.FromSeconds(20),
            $"Bulk import unexpectedly took {stopwatch.Elapsed}.");

        var reimport = variables
            .Select((variable, index) => Variable(
                variable.VariableName,
                index < 100 ? "MWh" : ""))
            .ToList();

        var second = await WebServiceMeterBulkUpsertService.UpsertAsync(
            connection,
            transaction,
            companyId: 1,
            reimport);

        Assert.Equal(0, second.CreatedCount);
        Assert.Equal(100, second.UpdatedCount);
        Assert.Equal(count - 100, second.UnchangedCount);
        Assert.Equal(count, second.MeterIds.Count);

        await transaction.RollbackAsync();
    }

    private static WebServiceVariableWithTrends Variable(
        string name,
        string unit,
        string? parentId = null,
        bool active = true) => new()
        {
            VariableName = name,
            Unit = unit,
            ParentMeterId = parentId ?? string.Empty,
            Type = "main",
            Active = active
        };

    private static async Task CreateTemporaryMetersTableAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction)
    {
        await using var command = new NpgsqlCommand(
            """
            CREATE TEMP TABLE "Meters" (
                "MeterId" SERIAL PRIMARY KEY,
                "Name" VARCHAR(100) NOT NULL,
                "Label" VARCHAR(150),
                "Unit" VARCHAR(20) NOT NULL DEFAULT '',
                "ParentId" INTEGER,
                "LastReading" INTEGER DEFAULT 0,
                "Type" VARCHAR(10) NOT NULL,
                "Active" BOOLEAN DEFAULT TRUE,
                "TenantID" INTEGER,
                "CompanyId" INTEGER
            ) ON COMMIT DROP;
            """,
            connection,
            transaction);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<int> InsertMeterAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string name,
        string unit,
        int companyId,
        int? tenantId,
        bool active)
    {
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO "Meters"
                ("Name", "Label", "Unit", "Type", "Active", "TenantID", "CompanyId")
            VALUES
                (@name, @name, @unit, 'main', @active, @tenantId, @companyId)
            RETURNING "MeterId"
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue("name", name);
        command.Parameters.AddWithValue("unit", unit);
        command.Parameters.AddWithValue("active", active);
        command.Parameters.AddWithValue(
            "tenantId",
            tenantId.HasValue ? tenantId.Value : DBNull.Value);
        command.Parameters.AddWithValue("companyId", companyId);
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private static async Task<MeterSnapshot> ReadMeterAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        int meterId)
    {
        await using var command = new NpgsqlCommand(
            """
            SELECT "MeterId", "Unit", "ParentId", "TenantID", "Active", "CompanyId"
            FROM "Meters"
            WHERE "MeterId" = @meterId
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue("meterId", meterId);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());

        return new MeterSnapshot(
            reader.GetInt32(0),
            reader.GetString(1),
            reader.IsDBNull(2) ? null : reader.GetInt32(2),
            reader.IsDBNull(3) ? null : reader.GetInt32(3),
            reader.GetBoolean(4),
            reader.GetInt32(5));
    }

    private sealed record MeterSnapshot(
        int MeterId,
        string Unit,
        int? ParentId,
        int? TenantId,
        bool Active,
        int CompanyId);
}
