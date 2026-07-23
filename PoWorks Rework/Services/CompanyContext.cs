using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace PoWorks_Rework.Services
{
    public interface ICompanyContext
    {
        int CurrentCompanyId { get; }
    }

    public class CompanyContext : ICompanyContext
    {
        private readonly IHttpContextAccessor _httpContextAccessor;

        public CompanyContext(IHttpContextAccessor httpContextAccessor)
        {
            _httpContextAccessor = httpContextAccessor;
        }

        public int CurrentCompanyId
        {
            get
            {
                var httpContext = _httpContextAccessor.HttpContext;
                var user = httpContext?.User;

                if (user == null) return 1;

         
                if (user.Identity?.Name?.ToLower() == "admin")
                {
                    var cookieValue = httpContext?.Request.Cookies["AdminSelectedCompanyId"];
                    if (!string.IsNullOrEmpty(cookieValue) && int.TryParse(cookieValue, out int selectedCompanyId))
                    {
                
                        return selectedCompanyId;
                    }
                }

          
                var companyClaim = user.FindFirst("CompanyId");
                if (companyClaim != null && int.TryParse(companyClaim.Value, out int companyId))
                {
                    return companyId;
                }

                return 1; 
            }
        }
    }
}