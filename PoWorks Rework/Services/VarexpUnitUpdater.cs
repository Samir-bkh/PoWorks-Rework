using Npgsql;

namespace PoWorks_Rework.Services;

/// <summary>Safely updates units on existing meters during VAREXP reimport.</summary>
public static class VarexpUnitUpdater
{
    public static async Task<bool> UpdateAsync(
        NpgsqlConnection connection, int companyId, int meterId, string? unit)
    {
        var normalized = WebServiceImportPolicy.NormalizeUnit(unit);
        if (normalized.Length == 0) return false;

        await using var command = new NpgsqlCommand("""
            UPDATE "Meters"
            SET "Unit" = @unit
            WHERE "MeterId" = @meterId
              AND "CompanyId" = @companyId
              AND "Unit" IS DISTINCT FROM @unit
            """, connection);
        command.Parameters.AddWithValue("unit", normalized);
        command.Parameters.AddWithValue("meterId", meterId);
        command.Parameters.AddWithValue("companyId", companyId);
        return await command.ExecuteNonQueryAsync() > 0;
    }
}
