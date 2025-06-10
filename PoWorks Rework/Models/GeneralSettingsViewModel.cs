namespace PoWorks_Rework.Models
{
    public class GeneralSettingsViewModel
    {
        public DatabaseSettings PostgreSql { get; set; } = new DatabaseSettings();
        public List<SqlServerSettings> SqlServerConnections { get; set; } = new List<SqlServerSettings>();

        // Backward compatibility - returns the default connection
        public SqlServerSettings SqlServer
        {
            get
            {
                return SqlServerConnections.FirstOrDefault(c => c.IsDefault) ?? SqlServerConnections.FirstOrDefault() ?? new SqlServerSettings();
            }
        }
    }
}