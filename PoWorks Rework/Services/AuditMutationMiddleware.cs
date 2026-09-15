using Microsoft.AspNetCore.Http;

namespace PoWorks_Rework.Services
{
    /// <summary>
    /// Shared request classification used by the audit safety net.
    /// Only state-changing HTTP verbs are considered mutations.
    /// </summary>
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

            // Authentication has richer dedicated audit events in AuthController.
            return !path.StartsWith("/Auth/Login", StringComparison.OrdinalIgnoreCase) &&
                   !path.StartsWith("/Auth/Logout", StringComparison.OrdinalIgnoreCase);
        }
    }
}
