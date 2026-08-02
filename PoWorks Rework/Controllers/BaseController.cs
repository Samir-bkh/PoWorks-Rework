using Microsoft.AspNetCore.Mvc;
using Npgsql;
using PoWorks_Rework.Services;
using Microsoft.AspNetCore.Authorization;

namespace PoWorks_Rework.Controllers
{
    /// <summary>
    /// Base controller class for all auth-protected controllers.
    /// Provides common database access and authorization checks.
    /// </summary>
    [Authorize]
    public abstract class BaseController : Controller
    {
        /// <summary>
        /// Injected database service for data access operations.
        /// </summary>
        protected readonly DatabaseService _databaseService;

        /// <summary>
        /// Initializes the base controller with a database service.
        /// All derived controllers must inject this dependency.
        /// </summary>
        public BaseController(DatabaseService databaseService)
        {
            _databaseService = databaseService;
        }

        /// <summary>
        /// Gets a database connection after verifying the database is initialized.
        /// Throws InvalidOperationException if database setup is incomplete.
        /// </summary>
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