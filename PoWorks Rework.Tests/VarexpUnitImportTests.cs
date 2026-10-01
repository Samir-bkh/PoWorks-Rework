using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using PoWorks_Rework.Services;
using Xunit;

namespace PoWorks_Rework.Tests;

public sealed class VarexpUnitImportTests
{
    [Theory]
    [InlineData(850)]
    [InlineData(65001)]
    public async Task ParserPreservesPcVueDegreeSignAndUnitHeader(int codePage)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var header = new string[66];
        header[0] = "Class";
        header[22] = "Source";
        header[65] = "Unit";
        var variable = new string[66];
        variable[0] = "CTV";
        variable[1] = "1";
        variable[2] = "Building";
        variable[3] = "ExternalTemp";
        variable[65] = "°C";
        var content = string.Join(",", header) + "\n" + string.Join(",", variable) + "\n";
        var bytes = Encoding.GetEncoding(codePage).GetBytes(content);

        using var stream = new MemoryStream(bytes);
        var file = new FormFile(stream, 0, bytes.Length, "VarexpFile", "varexp.dat");
        var parser = new VarexpParserService(NullLogger<VarexpParserService>.Instance);

        var rows = await parser.ParseVarexpAsync(file);
        var headerRow = Assert.Single(rows.Where(row => row[0] == "Class"));
        var unitIndex = Array.IndexOf(headerRow, "Unit");
        var meter = Assert.Single(rows.Where(row => row[0] == "CTV"));

        Assert.Equal("Building.ExternalTemp", meter[1]);
        Assert.Equal("°C", meter[unitIndex]);
    }

    [Fact]
    public async Task ExistingMeterUnitUpdatePreservesOtherFieldsAndCompanyIsolation()
    {
        var connectionString = Environment.GetEnvironmentVariable("POWORKS_TEST_POSTGRES");
        if (string.IsNullOrWhiteSpace(connectionString)) return;

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await using (var create = new NpgsqlCommand("""
            CREATE TEMP TABLE "Meters" (
                "MeterId" INTEGER PRIMARY KEY,
                "CompanyId" INTEGER NOT NULL,
                "Unit" VARCHAR(20) NOT NULL,
                "Type" VARCHAR(10) NOT NULL,
                "ParentId" INTEGER,
                "Active" BOOLEAN NOT NULL,
                "TenantID" INTEGER,
                "LastReading" INTEGER
            ) ON COMMIT DROP;
            INSERT INTO "Meters" VALUES (10, 1, 'kWh', 'sub', 7, false, 123, 500);
            """, connection, transaction))
            await create.ExecuteNonQueryAsync();

        Assert.False(await VarexpUnitUpdater.UpdateAsync(connection, 2, 10, "°C"));
        Assert.False(await VarexpUnitUpdater.UpdateAsync(connection, 1, 10, ""));
        Assert.True(await VarexpUnitUpdater.UpdateAsync(connection, 1, 10, " °C "));
        Assert.False(await VarexpUnitUpdater.UpdateAsync(connection, 1, 10, "°C"));

        await using var check = new NpgsqlCommand(
            "SELECT \"Unit\", \"Type\", \"ParentId\", \"Active\", \"TenantID\", \"LastReading\" FROM \"Meters\" WHERE \"MeterId\" = 10",
            connection, transaction);
        await using var reader = await check.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal("°C", reader.GetString(0));
        Assert.Equal("sub", reader.GetString(1));
        Assert.Equal(7, reader.GetInt32(2));
        Assert.False(reader.GetBoolean(3));
        Assert.Equal(123, reader.GetInt32(4));
        Assert.Equal(500, reader.GetInt32(5));
    }
}
