using Microsoft.AspNetCore.Mvc;
using Npgsql;
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
    }
}