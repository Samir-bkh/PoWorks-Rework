using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using PoWorks_Rework.Models;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PoWorks_Rework.Services
{
    public static class AuditTrail
    {
        private static readonly SemaphoreSlim FileLock = new(1, 1);
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = false
        };

        public static async Task LogAsync(
            DatabaseService databaseService,
            HttpContext? context,
            AuditEvent auditEvent,
            CancellationToken cancellationToken = default)
        {
            var principal = context?.User;
            var configuration = context?.RequestServices.GetService<IConfiguration>();
            var logger = context?.RequestServices.GetService<ILoggerFactory>()?.CreateLogger("AuditTrail");

            var actorName = auditEvent.ActorUserName
                ?? principal?.Identity?.Name
                ?? "System";

            var actorId = auditEvent.ActorUserId
                ?? principal?.FindFirstValue(ClaimTypes.NameIdentifier);

            var actorType = auditEvent.ActorUserType
                ?? principal?.FindFirst("UserType")?.Value
                ?? (AccessRules.IsAdmin(actorName) ? "Admin" : null);

            int? companyId = auditEvent.CompanyId;
            if (!companyId.HasValue &&
                int.TryParse(principal?.FindFirst("CompanyId")?.Value, out var claimCompanyId))
            {
                companyId = claimCompanyId;
            }

            var beforeJson = SerializeSafely(auditEvent.Before);
            var afterJson = SerializeSafely(auditEvent.After);
            var timestamp = DateTimeOffset.UtcNow;
            var ip = context?.Connection.RemoteIpAddress?.ToString();
            var correlationId = context?.TraceIdentifier ?? Guid.NewGuid().ToString("N");

            var record = new
            {
                timestampUtc = timestamp,
                actorUserId = actorId,
                actorUserName = actorName,
                actorUserType = actorType,
                companyId,
                action = auditEvent.Action,
                entityType = auditEvent.EntityType,
                entityId = auditEvent.EntityId,
                summary = auditEvent.Summary,
                before = auditEvent.Before,
                after = auditEvent.After,
                success = auditEvent.Success,
                ipAddress = ip,
                correlationId
            };

            try
            {
                var root = FileDiagnostics.ResolveLogRoot(configuration);
                var auditDirectory = Path.Combine(root, "audit");
                Directory.CreateDirectory(auditDirectory);
                var path = Path.Combine(auditDirectory, $"audit-{timestamp:yyyy-MM-dd}.jsonl");
                var line = JsonSerializer.Serialize(record, JsonOptions) + Environment.NewLine;

                await FileLock.WaitAsync(cancellationToken);
                try
                {
                    await File.AppendAllTextAsync(path, line, cancellationToken);
                }
                finally
                {
                    FileLock.Release();
                }
            }
            catch (Exception ex)
            {
                logger?.LogError(ex,
                    "Audit event could not be written to file. Action={Action}, Entity={EntityType}, EntityId={EntityId}",
                    auditEvent.Action, auditEvent.EntityType, auditEvent.EntityId);
            }

            if (!databaseService.IsInitialized)
                return;

            try
            {
                await using var connection = databaseService.CreateNewConnection();
                await connection.OpenAsync(cancellationToken);

                const string sql = @"
                    INSERT INTO ""AuditLogs"" (
                        ""TimestampUtc"", ""UserId"", ""UserName"", ""UserType"", ""CompanyId"",
                        ""Action"", ""EntityType"", ""EntityId"", ""Summary"",
                        ""BeforeJson"", ""AfterJson"", ""Success"", ""IpAddress"", ""CorrelationId"")
                    VALUES (
                        @timestampUtc, @userId, @userName, @userType, @companyId,
                        @action, @entityType, @entityId, @summary,
                        @beforeJson, @afterJson, @success, @ipAddress, @correlationId)";

                await using var cmd = new NpgsqlCommand(sql, connection);
                cmd.Parameters.AddWithValue("timestampUtc", timestamp);
                cmd.Parameters.AddWithValue("userId", (object?)actorId ?? DBNull.Value);
                cmd.Parameters.AddWithValue("userName", actorName);
                cmd.Parameters.AddWithValue("userType", (object?)actorType ?? DBNull.Value);
                cmd.Parameters.AddWithValue("companyId", (object?)companyId ?? DBNull.Value);
                cmd.Parameters.AddWithValue("action", auditEvent.Action);
                cmd.Parameters.AddWithValue("entityType", auditEvent.EntityType);
                cmd.Parameters.AddWithValue("entityId", (object?)auditEvent.EntityId ?? DBNull.Value);
                cmd.Parameters.AddWithValue("summary", auditEvent.Summary);
                cmd.Parameters.AddWithValue("beforeJson", (object?)beforeJson ?? DBNull.Value);
                cmd.Parameters.AddWithValue("afterJson", (object?)afterJson ?? DBNull.Value);
                cmd.Parameters.AddWithValue("success", auditEvent.Success);
                cmd.Parameters.AddWithValue("ipAddress", (object?)ip ?? DBNull.Value);
                cmd.Parameters.AddWithValue("correlationId", correlationId);
                await cmd.ExecuteNonQueryAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                logger?.LogError(ex,
                    "Audit event could not be persisted to PostgreSQL. Action={Action}, Entity={EntityType}, EntityId={EntityId}",
                    auditEvent.Action, auditEvent.EntityType, auditEvent.EntityId);
            }
        }

        private static string? SerializeSafely(object? value)
        {
            if (value == null)
                return null;

            try
            {
                return JsonSerializer.Serialize(value, JsonOptions);
            }
            catch
            {
                return JsonSerializer.Serialize(new { serializationError = true }, JsonOptions);
            }
        }
    }
}
