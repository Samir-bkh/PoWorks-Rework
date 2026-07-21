using Microsoft.AspNetCore.Mvc;
using Npgsql;
using PoWorks_Rework.Services;

namespace PoWorks_Rework.Controllers
{
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
    }
}