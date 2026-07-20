using System.Security.Claims;

namespace PoWorks_Rework.Services
{
    // L'interface qui définit ce qu'est un Contexte d'Entreprise
    public interface ICompanyContext
    {
        int CurrentCompanyId { get; }
    }

    // L'implémentation pour le site Web (qui lit le Cookie)
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

                // Si l'utilisateur est connecté, on lit le "CompanyId" dans son cookie
                if (user != null && user.Identity != null && user.Identity.IsAuthenticated)
                {
                    var companyClaim = user.FindFirst("CompanyId");
                    if (companyClaim != null && int.TryParse(companyClaim.Value, out int companyId))
                    {
                        return companyId;
                    }
                }

                // Sécurité par défaut : si on ne trouve rien, on renvoie 1 (Legacy Client)
                return 1;
            }
        }
    }
}