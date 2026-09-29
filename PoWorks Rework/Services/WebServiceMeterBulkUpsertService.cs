using Npgsql;
using NpgsqlTypes;
using PoWorks_Rework.Models;

namespace PoWorks_Rework.Services;

/// <summary>
/// Performs Web Service meter metadata imports with a constant number of PostgreSQL
/// round trips. Input rows are streamed into a temporary table with COPY, then existing
/// meters are updated and new meters are inserted with set-based SQL.
///
/// The semantics intentionally match the previously validated V2 workflow:
/// - meter identity is Name + CompanyId;
/// - existing MeterId, tenant assignment, parent, active flag and history are preserved;
/// - a non-empty imported unit may update an existing unit;
/// - an empty imported unit never erases an existing unit;
/// - a parent is accepted only when it belongs to the same company.
/// </summary>
public static class WebServiceMeterBulkUpsertService
{
    private const int AdvisoryLockNamespace = 50917;
    private const int MaxMeterNameLength = 100;
    private const int MaxUnitLength = 20;

    public static async Task<WebServiceMeterBulkUpsertResult> UpsertAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        int companyId,
        IReadOnlyCollection<WebServiceVariableWithTrends> variables,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(variables);

        var errors = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var prepared = PrepareRows(variables, errors);
        if (prepared.Count == 0)
        {
            return new WebServiceMeterBulkUpsertResult(
                new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase),
                0,
                0,
                0,
                errors);
        }

        // No schema migration is required for safe concurrency. The transaction-scoped
        // advisory lock serializes Web Service metadata upserts only inside one company.
        await using (var advisoryLock = new NpgsqlCommand(
            "SELECT pg_advisory_xact_lock(@namespace, @companyId)",
            connection,
            transaction))
        {
            advisoryLock.Parameters.AddWithValue("namespace", AdvisoryLockNamespace);
            advisoryLock.Parameters.AddWithValue("companyId", companyId);
            await advisoryLock.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var createStage = new NpgsqlCommand(
            """
            CREATE TEMP TABLE "TempWebServiceMeterUpsert" (
                "Ordinal" INTEGER NOT NULL,
                "Name" VARCHAR(100) NOT NULL,
                "Unit" VARCHAR(20) NOT NULL,
                "ParentId" INTEGER NULL,
                "Type" VARCHAR(10) NOT NULL,
                "Active" BOOLEAN NOT NULL
            ) ON COMMIT DROP;
            """,
            connection,
            transaction))
        {
            await createStage.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var writer = await connection.BeginBinaryImportAsync(
            """
            COPY "TempWebServiceMeterUpsert"
                ("Ordinal", "Name", "Unit", "ParentId", "Type", "Active")
            FROM STDIN (FORMAT BINARY)
            """,
            cancellationToken))
        {
            foreach (var row in prepared)
            {
                await writer.StartRowAsync(cancellationToken);
                await writer.WriteAsync(row.Ordinal, NpgsqlDbType.Integer, cancellationToken);
                await writer.WriteAsync(row.Name, NpgsqlDbType.Varchar, cancellationToken);
                await writer.WriteAsync(row.Unit, NpgsqlDbType.Varchar, cancellationToken);

                if (row.ParentId.HasValue)
                    await writer.WriteAsync(row.ParentId.Value, NpgsqlDbType.Integer, cancellationToken);
                else
                    await writer.WriteNullAsync(cancellationToken);

                await writer.WriteAsync(row.Type, NpgsqlDbType.Varchar, cancellationToken);
                await writer.WriteAsync(row.Active, NpgsqlDbType.Boolean, cancellationToken);
            }

            await writer.CompleteAsync(cancellationToken);
        }

        // Resolve one canonical existing meter per staged name. The old workflow used
        // SELECT ... LIMIT 1; ORDER BY MeterId makes that behavior deterministic even if
        // a legacy database already contains duplicate names.
        await using (var snapshot = new NpgsqlCommand(
            """
            CREATE TEMP TABLE "TempWebServiceMeterExisting" ON COMMIT DROP AS
            SELECT s."Ordinal",
                   s."Name",
                   existing."MeterId",
                   COALESCE(existing."Unit", '') AS "ExistingUnit"
            FROM "TempWebServiceMeterUpsert" s
            LEFT JOIN LATERAL (
                SELECT m."MeterId", m."Unit"
                FROM "Meters" m
                WHERE m."CompanyId" = @companyId
                  AND m."Name" = s."Name"
                ORDER BY m."MeterId"
                LIMIT 1
            ) existing ON TRUE;
            """,
            connection,
            transaction))
        {
            snapshot.Parameters.AddWithValue("companyId", companyId);
            await snapshot.ExecuteNonQueryAsync(cancellationToken);
        }

        int existingCount;
        await using (var countExisting = new NpgsqlCommand(
            """
            SELECT COUNT(*)
            FROM "TempWebServiceMeterExisting"
            WHERE "MeterId" IS NOT NULL
            """,
            connection,
            transaction))
        {
            existingCount = Convert.ToInt32(
                await countExisting.ExecuteScalarAsync(cancellationToken));
        }

        int updatedCount;
        await using (var updateExisting = new NpgsqlCommand(
            """
            UPDATE "Meters" m
            SET "Unit" = staged."Unit"
            FROM "TempWebServiceMeterUpsert" staged
            INNER JOIN "TempWebServiceMeterExisting" existing
              ON existing."Ordinal" = staged."Ordinal"
            WHERE existing."MeterId" = m."MeterId"
              AND m."CompanyId" = @companyId
              AND staged."Unit" <> ''
              AND LOWER(COALESCE(m."Unit", '')) <> LOWER(staged."Unit")
            """,
            connection,
            transaction))
        {
            updateExisting.Parameters.AddWithValue("companyId", companyId);
            updatedCount = await updateExisting.ExecuteNonQueryAsync(cancellationToken);
        }

        int createdCount;
        await using (var insertNew = new NpgsqlCommand(
            """
            INSERT INTO "Meters"
                ("Name", "Label", "Unit", "ParentId", "LastReading", "Type", "Active", "TenantID", "CompanyId")
            SELECT staged."Name",
                   staged."Name",
                   staged."Unit",
                   parent."MeterId",
                   0,
                   staged."Type",
                   staged."Active",
                   NULL,
                   @companyId
            FROM "TempWebServiceMeterUpsert" staged
            INNER JOIN "TempWebServiceMeterExisting" existing
              ON existing."Ordinal" = staged."Ordinal"
            LEFT JOIN "Meters" parent
              ON parent."MeterId" = staged."ParentId"
             AND parent."CompanyId" = @companyId
            WHERE existing."MeterId" IS NULL
            ORDER BY staged."Ordinal"
            """,
            connection,
            transaction))
        {
            insertNew.Parameters.AddWithValue("companyId", companyId);
            createdCount = await insertNew.ExecuteNonQueryAsync(cancellationToken);
        }

        var meterIds = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        await using (var resolveIds = new NpgsqlCommand(
            """
            SELECT staged."Name", resolved."MeterId"
            FROM "TempWebServiceMeterUpsert" staged
            INNER JOIN LATERAL (
                SELECT m."MeterId"
                FROM "Meters" m
                WHERE m."CompanyId" = @companyId
                  AND m."Name" = staged."Name"
                ORDER BY m."MeterId"
                LIMIT 1
            ) resolved ON TRUE
            ORDER BY staged."Ordinal"
            """,
            connection,
            transaction))
        {
            resolveIds.Parameters.AddWithValue("companyId", companyId);
            await using var reader = await resolveIds.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                meterIds[reader.GetString(0)] = reader.GetInt32(1);
            }
        }

        return new WebServiceMeterBulkUpsertResult(
            meterIds,
            createdCount,
            updatedCount,
            Math.Max(0, existingCount - updatedCount),
            errors);
    }

    private static List<PreparedMeterRow> PrepareRows(
        IReadOnlyCollection<WebServiceVariableWithTrends> variables,
        IDictionary<string, string> errors)
    {
        var rows = new List<PreparedMeterRow>(variables.Count);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var ordinal = 0;

        foreach (var variable in variables)
        {
            if (variable == null || string.IsNullOrWhiteSpace(variable.VariableName))
            {
                errors[$"<invalid-{ordinal}>"] = "Variable name is required.";
                ordinal++;
                continue;
            }

            var name = variable.VariableName.Trim();
            if (!names.Add(name))
            {
                ordinal++;
                continue;
            }

            if (name.Length > MaxMeterNameLength)
            {
                errors[name] = $"Variable name exceeds the {MaxMeterNameLength}-character meter limit.";
                ordinal++;
                continue;
            }

            var unit = WebServiceImportPolicy.NormalizeUnit(variable.Unit);
            if (unit.Length > MaxUnitLength)
            {
                errors[name] = $"Unit exceeds the {MaxUnitLength}-character meter limit.";
                ordinal++;
                continue;
            }

            int? parentId = null;
            if (int.TryParse(variable.ParentMeterId, out var parsedParentId) && parsedParentId > 0)
                parentId = parsedParentId;

            rows.Add(new PreparedMeterRow(
                ordinal,
                name,
                unit,
                parentId,
                WebServiceImportPolicy.NormalizeMeterType(variable.Type),
                variable.Active));
            ordinal++;
        }

        return rows;
    }

    private sealed record PreparedMeterRow(
        int Ordinal,
        string Name,
        string Unit,
        int? ParentId,
        string Type,
        bool Active);
}

public sealed record WebServiceMeterBulkUpsertResult(
    IReadOnlyDictionary<string, int> MeterIds,
    int CreatedCount,
    int UpdatedCount,
    int UnchangedCount,
    IReadOnlyDictionary<string, string> Errors);
