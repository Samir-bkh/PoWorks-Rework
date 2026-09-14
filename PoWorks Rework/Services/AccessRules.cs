using Microsoft.AspNetCore.Identity;
using System.Security.Claims;

namespace PoWorks_Rework.Services
{
    public static class AccessRules
    {
        public static bool IsAdmin(string? username) =>
            string.Equals(username, "Admin", StringComparison.OrdinalIgnoreCase);

        public static bool IsTenant(string? userType) =>
            string.Equals(userType, "Tenant", StringComparison.OrdinalIgnoreCase);

        public static bool IsUserEnabled(IdentityUser user, DateTimeOffset now) =>
            !(user.LockoutEnabled && user.LockoutEnd.HasValue && user.LockoutEnd.Value > now);

        public static bool TenantRouteAllowed(string? path)
        {
            path ??= string.Empty;
            return path == "/" ||
                   path.StartsWith("/Home", StringComparison.OrdinalIgnoreCase) ||
                   path.StartsWith("/Dashboard", StringComparison.OrdinalIgnoreCase) ||
                   path.StartsWith("/Auth", StringComparison.OrdinalIgnoreCase);
        }

        public static bool HasPermissionOrAdmin(ClaimsPrincipal user, string permission) =>
            IsAdmin(user.Identity?.Name) || user.HasClaim("Permission", permission);

        public static List<Claim> BuildAccessClaims(
            string userType,
            int companyId,
            int? tenantId,
            bool canViewPcVueConfig,
            bool canViewImportExport,
            bool canViewGeneralSettings)
        {
            var tenant = IsTenant(userType);
            var claims = new List<Claim>
            {
                new("UserType", tenant ? "Tenant" : "Management"),
                new("CompanyId", companyId.ToString())
            };

            if (tenant)
            {
                if (tenantId.HasValue)
                    claims.Add(new Claim("TenantId", tenantId.Value.ToString()));

                return claims;
            }

            if (canViewPcVueConfig) claims.Add(new Claim("Permission", "ViewPcVueConfig"));
            if (canViewImportExport) claims.Add(new Claim("Permission", "ViewImportExport"));
            if (canViewGeneralSettings) claims.Add(new Claim("Permission", "ViewGeneralSettings"));

            return claims;
        }
    }
}
