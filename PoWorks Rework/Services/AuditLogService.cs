using Npgsql;
using PoWorks_Rework.Models;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PoWorks_Rework.Services
{
    public interface IAuditLogService
    {
        Task LogAsync(AuditEvent auditEvent, CancellationToken cancellationToken = default);
    }

    public sealed class AuditLogService : IAuditLogService
    {
        private static readonly SemaphoreSlim FileLock = new(1, 1);
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = false
        };

        private readonly DatabaseService _databaseService;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly IConfiguration _configuration;
        private readonly ILogger<AuditLogService> _logger;

        public AuditLogService(
            DatabaseService databaseService,
            IHttpContextAccessor httpContextAccessor,
            IConfiguration configuration,
            ILogger<AuditLogService> logger)
        {
            _databaseService = databaseService;
            _httpContextAccessor = httpContextAccessor;
            _configuration = configuration;
            _logger = logger;
        }

        public async Task LogAsync(AuditEvent auditEvent, CancellationToken cancellationToken = default)
        {
            var context = _httpContextAccessor.HttpContext;
            var principal = context?.User;

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
                auditEvent.Action,
                auditEvent.EntityType,
                auditEvent.EntityId,
                auditEvent.Summary,
                before = auditEvent.Before,
                after = auditEvent.After,
                auditEvent.Success,
                ipAddress = ip,
                correlationId
            };

            await WriteFileAsync(record, timestamp, cancellationToken);

            if (!_databaseService.IsInitialized)
                return;

            try
            {
                await using var connection = _databaseService.CreateNewConnection();
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
                cmd.Parameters.AddWithValue("userName", (object?)actorName ?? DBNull.Value);
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
                _logger.LogError(ex,
                    "Audit event could not be persisted to PostgreSQL. Action={Action}, Entity={EntityType}, EntityId={EntityId}",
                    auditEvent.Action, auditEvent.EntityType, auditEvent.EntityId);
            }
        }

        private async Task WriteFileAsync(object record, DateTimeOffset timestamp, CancellationToken cancellationToken)
        {
            try
            {
                var root = FileDiagnostics.ResolveLogRoot(_configuration);
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
                _logger.LogError(ex,
                    "Audit event could not be written to the audit file. Action={Action}, Entity={EntityType}, EntityId={EntityId}",
                    ((dynamic)record).Action, ((dynamic)record).EntityType, ((dynamic)record).EntityId);
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

    public sealed class NullAuditLogService : IAuditLogService
    {
        public static NullAuditLogService Instance { get; } = new();

        private NullAuditLogService()
        {
        }

        public Task LogAsync(AuditEvent auditEvent, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }
}
