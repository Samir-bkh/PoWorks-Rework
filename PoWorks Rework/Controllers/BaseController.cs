using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Npgsql;
using PoWorks_Rework.Models;
using PoWorks_Rework.Services;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;

namespace PoWorks_Rework.Controllers
{
    [Authorize]
    public abstract class BaseController : Controller
    {
        protected readonly DatabaseService _databaseService;

        public BaseController(DatabaseService databaseService)
        {
            _databaseService = databaseService;
        }

        protected NpgsqlConnection GetDatabaseConnection()
        {
            if (!_databaseService.IsInitialized)
            {
                throw new InvalidOperationException("Database has not been initialized. Please configure database settings first.");
            }

            return _databaseService.GetConnection();
        }

        protected bool IsTenantUser =>
            string.Equals(User.FindFirstValue("UserType"), "Tenant", StringComparison.OrdinalIgnoreCase);

        protected int? CurrentTenantId
        {
            get
            {
                var value = User.FindFirstValue("TenantId");
                return int.TryParse(value, out var tenantId) ? tenantId : null;
            }
        }

        /// <summary>
        /// Generic audit safety net for every mutating action on controllers derived from BaseController.
        /// It deliberately records no request body or query string, so passwords, tokens and imported
        /// payloads cannot accidentally be copied into the audit trail. Controllers may additionally
        /// write richer entity-specific before/after events when needed.
        /// </summary>
        public override async Task OnActionExecutionAsync(
            ActionExecutingContext context,
            ActionExecutionDelegate next)
        {
            if (!AuditRequestClassifier.IsMutation(context.HttpContext.Request.Method))
            {
                await next();
                return;
            }

            var started = DateTimeOffset.UtcNow;
            var executed = await next();

            try
            {
                int? companyId = null;
                var companyContext = HttpContext.RequestServices.GetService<ICompanyContext>();
                if (companyContext != null)
                {
                    try
                    {
                        companyId = companyContext.CurrentCompanyId;
                    }
                    catch
                    {
                        // The audit event can still be written without a workspace id.
                    }
                }

                var controller = context.RouteData.Values["controller"]?.ToString() ?? GetType().Name;
                var action = context.RouteData.Values["action"]?.ToString() ?? "Unknown";
                var method = context.HttpContext.Request.Method.ToUpperInvariant();
                var statusCode = context.HttpContext.Response.StatusCode;
                var success = executed.Exception == null && statusCode < 400;
                var elapsedMs = (DateTimeOffset.UtcNow - started).TotalMilliseconds;

                await AuditTrail.LogAsync(_databaseService, HttpContext, new AuditEvent
                {
                    Action = success ? "MUTATION" : "MUTATION_FAILED",
                    EntityType = controller,
                    EntityId = action,
                    CompanyId = companyId,
                    Success = success,
                    Summary = $"{method} {controller}/{action} completed with HTTP {statusCode} in {elapsedMs:0} ms."
                });
            }
            catch
            {
                // Audit logging must never change the result of the business action.
            }
        }
    }
}
