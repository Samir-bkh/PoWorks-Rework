using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using PoWorks_Rework.Models;
using PoWorks_Rework.Services;
using System.Text;

namespace PoWorks_Rework.Controllers
{
    [Authorize(Policy = "AdminOnly")]
    public class AuditLogController : Controller
    {
        private readonly DatabaseService _databaseService;
        private readonly IConfiguration _configuration;

        public AuditLogController(DatabaseService databaseService, IConfiguration configuration)
        {
            _databaseService = databaseService;
            _configuration = configuration;
        }

        public IActionResult Index(
            string? search,
            [FromQuery(Name = "auditAction")] string? auditAction,
            string? entityType,
            int? companyId,
            DateTime? from,
            DateTime? to,
            bool showTechnical = false,
            int pageSize = 15,
            int page = 1)
        {
            var model = new AuditLogPageViewModel
            {
                Search = search,
                Action = auditAction,
                EntityType = entityType,
                CompanyId = companyId,
                From = from,
                To = to,
                ShowTechnical = showTechnical,
                Page = Math.Max(1, page),
                PageSize = NormalizePageSize(pageSize),
                LogDirectory = FileDiagnostics.ResolveLogRoot(_configuration)
            };

            if (!_databaseService.IsInitialized)
                return View(model);

            using var connection = _databaseService.CreateNewConnection();
            connection.Open();

            var where = new StringBuilder(" WHERE 1=1 ");
            var parameters = new List<NpgsqlParameter>();

            // Keep successful generic request-level safety-net events available,
            // but hide them by default so the page focuses on meaningful business events.
            // MUTATION_FAILED stays visible because failures should never be hidden.
            if (!showTechnical)
            {
                where.Append(@" AND NOT (""Action"" = 'MUTATION' AND ""Success"" = TRUE)");
            }

            if (!string.IsNullOrWhiteSpace(search))
            {
                where.Append(@" AND (
                    COALESCE(""UserName"", '') ILIKE @search OR
                    COALESCE(""Summary"", '') ILIKE @search OR
                    COALESCE(""EntityId"", '') ILIKE @search OR
                    COALESCE(""BeforeJson"", '') ILIKE @search OR
                    COALESCE(""AfterJson"", '') ILIKE @search)");
                parameters.Add(new NpgsqlParameter("search", $"%{search.Trim()}%"));
            }

            if (!string.IsNullOrWhiteSpace(auditAction))
            {
                where.Append(@" AND ""Action"" = @action");
                parameters.Add(new NpgsqlParameter("action", auditAction));
            }

            if (!string.IsNullOrWhiteSpace(entityType))
            {
                where.Append(@" AND ""EntityType"" = @entityType");
                parameters.Add(new NpgsqlParameter("entityType", entityType));
            }

            if (companyId.HasValue)
            {
                where.Append(@" AND ""CompanyId"" = @companyId");
                parameters.Add(new NpgsqlParameter("companyId", companyId.Value));
            }

            if (from.HasValue)
            {
                where.Append(@" AND ""TimestampUtc"" >= @from");
                parameters.Add(new NpgsqlParameter("from", new DateTimeOffset(from.Value.Date, TimeSpan.Zero)));
            }

            if (to.HasValue)
            {
                where.Append(@" AND ""TimestampUtc"" < @toExclusive");
                parameters.Add(new NpgsqlParameter("toExclusive", new DateTimeOffset(to.Value.Date.AddDays(1), TimeSpan.Zero)));
            }

            using (var countCmd = new NpgsqlCommand(@"SELECT COUNT(*) FROM ""AuditLogs""" + where, connection))
            {
                countCmd.Parameters.AddRange(parameters.Select(p => new NpgsqlParameter(p.ParameterName, p.Value)).ToArray());
                model.TotalCount = Convert.ToInt32(countCmd.ExecuteScalar());
            }

            var offset = (model.Page - 1) * model.PageSize;
            var sql = @"
                SELECT ""AuditLogId"", ""TimestampUtc"", ""UserName"", ""UserType"", ""CompanyId"",
                       ""Action"", ""EntityType"", ""EntityId"", ""Summary"",
                       ""BeforeJson"", ""AfterJson"", ""Success"", ""IpAddress"", ""CorrelationId""
                FROM ""AuditLogs""" + where + @"
                ORDER BY ""TimestampUtc"" DESC
                LIMIT @limit OFFSET @offset";

            using var cmd = new NpgsqlCommand(sql, connection);
            foreach (var p in parameters)
                cmd.Parameters.Add(new NpgsqlParameter(p.ParameterName, p.Value));
            cmd.Parameters.AddWithValue("limit", model.PageSize);
            cmd.Parameters.AddWithValue("offset", offset);

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                model.Items.Add(new AuditLogItem
                {
                    AuditLogId = reader.GetInt64(0),
                    TimestampUtc = reader.GetFieldValue<DateTimeOffset>(1),
                    UserName = reader.IsDBNull(2) ? null : reader.GetString(2),
                    UserType = reader.IsDBNull(3) ? null : reader.GetString(3),
                    CompanyId = reader.IsDBNull(4) ? null : reader.GetInt32(4),
                    Action = reader.GetString(5),
                    EntityType = reader.GetString(6),
                    EntityId = reader.IsDBNull(7) ? null : reader.GetString(7),
                    Summary = reader.GetString(8),
                    BeforeJson = reader.IsDBNull(9) ? null : reader.GetString(9),
                    AfterJson = reader.IsDBNull(10) ? null : reader.GetString(10),
                    Success = reader.GetBoolean(11),
                    IpAddress = reader.IsDBNull(12) ? null : reader.GetString(12),
                    CorrelationId = reader.IsDBNull(13) ? null : reader.GetString(13)
                });
            }

            return View(model);
        }

        private static int NormalizePageSize(int pageSize) =>
            pageSize switch
            {
                15 => 15,
                25 => 25,
                50 => 50,
                _ => 15
            };
    }
}
