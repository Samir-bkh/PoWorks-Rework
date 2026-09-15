using Microsoft.AspNetCore.Http;
using PoWorks_Rework.Models;

namespace PoWorks_Rework.Services
{
    public static class AuditRequestClassifier
    {
        public static bool IsMutation(string method) =>
            HttpMethods.IsPost(method) ||
            HttpMethods.IsPut(method) ||
            HttpMethods.IsPatch(method) ||
            HttpMethods.IsDelete(method);

        public static bool ShouldAudit(HttpRequest request)
        {
            if (!IsMutation(request.Method))
                return false;

            var path = request.Path.Value ?? string.Empty;

            // Authentication is logged with richer dedicated events in AuthController.
            if (path.StartsWith("/Auth/Login", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("/Auth/Logout", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return true;
        }
    }

    /// <summary>
    /// Safety-net audit middleware. Every mutating HTTP request is written to the audit trail,
    /// even if the target controller has not yet received entity-specific audit instrumentation.
    /// Request bodies and query strings are intentionally never recorded to avoid passwords,
    /// tokens or other sensitive values leaking into logs.
    /// </summary>
    public sealed class AuditMutationMiddleware
    {
        private readonly RequestDelegate _next;

        public AuditMutationMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task InvokeAsync(HttpContext context, DatabaseService databaseService)
        {
            if (!AuditRequestClassifier.ShouldAudit(context.Request))
            {
                await _next(context);
                return;
            }

            var started = DateTimeOffset.UtcNow;
            Exception? failure = null;

            try
            {
                await _next(context);
            }
            catch (Exception ex)
            {
                failure = ex;
                throw;
            }
            finally
            {
                try
                {
                    int? companyId = null;
                    var companyContext = context.RequestServices.GetService<ICompanyContext>();
                    if (companyContext != null)
                    {
                        try
                        {
                            companyId = companyContext.CurrentCompanyId;
                        }
                        catch
                        {
                            // Claims may not exist yet. AuditTrail will fall back to what it can resolve.
                        }
                    }

                    var statusCode = failure == null
                        ? context.Response.StatusCode
                        : StatusCodes.Status500InternalServerError;

                    var success = failure == null && statusCode < 400;
                    var elapsedMs = (DateTimeOffset.UtcNow - started).TotalMilliseconds;
                    var method = context.Request.Method.ToUpperInvariant();
                    var path = context.Request.Path.Value ?? "/";

                    await AuditTrail.LogAsync(databaseService, context, new AuditEvent
                    {
                        Action = success ? "HTTP_MUTATION" : "HTTP_MUTATION_FAILED",
                        EntityType = "Request",
                        EntityId = $"{method} {path}",
                        CompanyId = companyId,
                        Success = success,
                        Summary = $"{method} {path} completed with HTTP {statusCode} in {elapsedMs:0} ms."
                    });
                }
                catch
                {
                    // Auditing must never alter the outcome of the business request.
                }
            }
        }
    }
}
