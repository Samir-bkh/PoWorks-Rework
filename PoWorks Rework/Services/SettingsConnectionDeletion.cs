using Npgsql;

namespace PoWorks_Rework.Services;

public static class SettingsConnectionDeletion
{
    public static Task<int> DeleteSqlServerAsync(
        NpgsqlConnection connection,
        string connectionId,
        int companyId,
        NpgsqlTransaction? transaction = null) =>
        DeleteAsync(connection, transaction, connectionId, companyId,
            "DELETE FROM \"SqlServerConnections\" WHERE \"ConnectionId\" = @id AND \"CompanyId\" = @companyId");

    public static Task<int> DeleteWebServiceAsync(
        NpgsqlConnection connection,
        string connectionId,
        int companyId,
        NpgsqlTransaction? transaction = null) =>
        DeleteAsync(connection, transaction, connectionId, companyId,
            "DELETE FROM \"WebServiceConnections\" WHERE \"ConnectionId\" = @id AND \"CompanyId\" = @companyId");

    private static async Task<int> DeleteAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        string connectionId,
        int companyId,
        string sql)
    {
        if (string.IsNullOrWhiteSpace(connectionId)) return 0;

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("id", connectionId);
        command.Parameters.AddWithValue("companyId", companyId);
        return await command.ExecuteNonQueryAsync();
    }
}
