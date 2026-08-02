using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace PoWorks_Rework.Services
{
    /// <summary>
    /// Interface for accessing the current company context in a multi-tenant application.
    /// </summary>
    public interface ICompanyContext
    {
        /// <summary>
        /// Gets the ID of the current company for the logged-in user.
        /// </summary>
        int CurrentCompanyId { get; }
    }

    /// <summary>
    /// Determines the current company ID from HTTP request context.
    /// Supports admin users selecting different companies via cookie, and regular users bound to their company claim.
    /// </summary>
    public class CompanyContext : ICompanyContext
    {
        private readonly IHttpContextAccessor _httpContextAccessor;

        /// <summary>
        /// Initializes the company context with HTTP context accessor for multi-tenant isolation.
        /// </summary>
        public CompanyContext(IHttpContextAccessor httpContextAccessor)
        {
            _httpContextAccessor = httpContextAccessor;
        }

        /// <summary>
        /// Gets the current company ID based on user identity and context.
        /// Priority: 1) Admin-selected company (from cookie), 2) User's assigned company (from claim), 3) Default company ID 1.
        /// </summary>
        public int CurrentCompanyId
        {
            get
            {
                var httpContext = _httpContextAccessor.HttpContext;
                var user = httpContext?.User;

                if (user == null) return 1;

                // Admin users can select different companies via cookie
                if (user.Identity?.Name?.ToLower() == "admin")
                {
                    var cookieValue = httpContext?.Request.Cookies["AdminSelectedCompanyId"];
                    if (!string.IsNullOrEmpty(cookieValue) && int.TryParse(cookieValue, out int selectedCompanyId))
                    {
                        return selectedCompanyId;
                    }
                }

                // Regular users have company ID in claim
                var companyClaim = user.FindFirst("CompanyId");
                if (companyClaim != null && int.TryParse(companyClaim.Value, out int companyId))
                {
                    return companyId;
                }

                // Fallback to default company
                return 1; 
            }
        }
    }
}