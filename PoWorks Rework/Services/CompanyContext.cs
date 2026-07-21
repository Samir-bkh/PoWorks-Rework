using System.Security.Claims;

namespace PoWorks_Rework.Services
{
    public interface ICompanyContext
    {
        int CurrentCompanyId { get; }
    }
    public class WebCompanyContext : ICompanyContext
    {
        private readonly IHttpContextAccessor _httpContextAccessor;

        public WebCompanyContext(IHttpContextAccessor httpContextAccessor)
        {
            _httpContextAccessor = httpContextAccessor;
        }

        public int CurrentCompanyId
        {
            get
            {
                var user = _httpContextAccessor.HttpContext?.User;
                if (user != null && user.Identity != null && user.Identity.IsAuthenticated)
                {
                    var companyClaim = user.FindFirst("CompanyId");
                    if (companyClaim != null && int.TryParse(companyClaim.Value, out int companyId))
                    {
                        return companyId;
                    }
                }
                return 1;
            }
        }
    }
}