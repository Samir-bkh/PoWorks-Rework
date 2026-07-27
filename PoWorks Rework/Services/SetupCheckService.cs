using Npgsql;

namespace PoWorks_Rework.Services
{
    public class SetupCheckService
    {
        private readonly DatabaseService _databaseService;

        public SetupCheckService(DatabaseService databaseService)
        {
            _databaseService = databaseService;
        }

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