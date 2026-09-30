using Npgsql;
using PoWorks_Rework.Services;
using Xunit;

namespace PoWorks_Rework.Tests;

public class SettingsConnectionDeletionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DeleteConnection_CannotDeleteAnotherWorkspace(bool webService)
    {
        var connectionString = Environment.GetEnvironmentVariable("POWORKS_TEST_POSTGRES");
        if (string.IsNullOrWhiteSpace(connectionString)) return;

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        var table = webService ? "WebServiceConnections" : "SqlServerConnections";
        await using (var setup = new NpgsqlCommand($"""
            CREATE TEMP TABLE "{table}" ("ConnectionId" TEXT NOT NULL, "CompanyId" INTEGER NOT NULL) ON COMMIT DROP;
            INSERT INTO "{table}" ("ConnectionId", "CompanyId")
            VALUES ('shared', 1), ('shared', 2), ('foreign', 2);
            """, connection, transaction))
        {
            await setup.ExecuteNonQueryAsync();
        }

        Task<int> Delete(string id, int companyId) => webService
            ? SettingsConnectionDeletion.DeleteWebServiceAsync(connection, id, companyId, transaction)
            : SettingsConnectionDeletion.DeleteSqlServerAsync(connection, id, companyId, transaction);

        Assert.Equal(0, await Delete("foreign", 1));
        Assert.Equal(1, await Delete("shared", 1));
        Assert.Equal(0, await Delete("shared", 1));
        Assert.Equal(0, await Delete("", 1));

        await using var verify = new NpgsqlCommand(
            $"SELECT \"ConnectionId\", \"CompanyId\" FROM \"{table}\" ORDER BY \"ConnectionId\", \"CompanyId\"",
            connection, transaction);
        await using var reader = await verify.ExecuteReaderAsync();
        var remaining = new List<(string Id, int CompanyId)>();
        while (await reader.ReadAsync())
            remaining.Add((reader.GetString(0), reader.GetInt32(1)));

        Assert.Equal(new[] { ("foreign", 2), ("shared", 2) }, remaining);
    }
}
