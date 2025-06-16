// Models/GeneralSettingsViewModel.cs
namespace PoWorks_Rework.Models
{
    public class GeneralSettingsViewModel
    {
        public DatabaseSettings PostgreSql { get; set; } = new DatabaseSettings();
        public List<SqlServerSettings> SqlServerConnections { get; set; } = new List<SqlServerSettings>();

        // NEW: Web Service connections - THIS WAS MISSING!
        public List<PCVueWebServiceSettings> WebServiceConnections { get; set; } = new List<PCVueWebServiceSettings>();

        // Backward compatibility - returns the default SQL Server connection
        public SqlServerSettings SqlServer
        {
            get
            {
                return SqlServerConnections.FirstOrDefault(c => c.IsDefault) ?? SqlServerConnections.FirstOrDefault() ?? new SqlServerSettings();
            }
        }

        // Helper to get default Web Service connection
        public PCVueWebServiceSettings DefaultWebServiceConnection
        {
            get
            {
                return WebServiceConnections.FirstOrDefault(c => c.IsDefault) ?? WebServiceConnections.FirstOrDefault() ?? new PCVueWebServiceSettings();
            }
        }
    }
}