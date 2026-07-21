namespace PoWorks_Rework.Models
{
    public class GeneralSettingsViewModel
    {
        public DatabaseSettings PostgreSql { get; set; } = new DatabaseSettings();
        public List<SqlServerSettings> SqlServerConnections { get; set; } = new List<SqlServerSettings>();
        public List<PCVueWebServiceSettings> WebServiceConnections { get; set; } = new List<PCVueWebServiceSettings>();
        public SqlServerSettings SqlServer
        {
            get
            {
                return SqlServerConnections.FirstOrDefault(c => c.IsDefault) ?? SqlServerConnections.FirstOrDefault() ?? new SqlServerSettings();
            }
        }
        public PCVueWebServiceSettings DefaultWebServiceConnection
        {
            get
            {
                return WebServiceConnections.FirstOrDefault(c => c.IsDefault) ?? WebServiceConnections.FirstOrDefault() ?? new PCVueWebServiceSettings();
            }
        }
    }
}