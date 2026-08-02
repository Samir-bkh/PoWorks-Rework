using Npgsql;

namespace PoWorks_Rework.Services
{
    /// <summary>
    /// Checks the installation and setup status of the application.
    /// Used to determine if initial setup/configuration has been completed.
    /// </summary>
    public class SetupCheckService
    {
        private readonly DatabaseService _databaseService;

        /// <summary>
        /// Initializes the setup check service with a database service.
        /// </summary>
        public SetupCheckService(DatabaseService databaseService)
        {
            _databaseService = databaseService;
        }

        /// <summary>
        /// Checks if the application has been installed by verifying if users exist in the database.
        /// Returns true if at least one user record exists, false if database is empty or unreachable.
        /// </summary>
        public async Task<bool> IsApplicationInstalledAsync()
        {
            try
            {
                using var connection = _databaseService.CreateNewConnection();
                await connection.OpenAsync();

                using var cmd = new NpgsqlCommand(@"SELECT COUNT(*) FROM ""Users""", connection);
                var count = Convert.ToInt32(await cmd.ExecuteScalarAsync());
                return count > 0;
            }
            catch
            {
                return false;
            }
        }
    }
}