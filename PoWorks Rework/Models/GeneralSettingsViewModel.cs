namespace PoWorks_Rework.Models
{
    /// <summary>
    /// Aggregates all general settings and configurations.
    /// Combines database, SQL Server, and web service connection settings into a single view model.
    /// </summary>
    public class GeneralSettingsViewModel
    {
        /// <summary>
        /// PostgreSQL database configuration settings
        /// </summary>
        public DatabaseSettings PostgreSql { get; set; } = new DatabaseSettings();

        /// <summary>
        /// List of SQL Server connections available for data import
        /// </summary>
        public List<SqlServerSettings> SqlServerConnections { get; set; } = new List<SqlServerSettings>();

        /// <summary>
        /// List of web service connections for external data access
        /// </summary>
        public List<PCVueWebServiceSettings> WebServiceConnections { get; set; } = new List<PCVueWebServiceSettings>();

        /// <summary>
        /// Gets the default or first SQL Server connection.
        /// Returns a new empty SqlServerSettings if no connections exist.
        /// </summary>
        public SqlServerSettings SqlServer
        {
            get
            {
                return SqlServerConnections.FirstOrDefault(c => c.IsDefault) ?? SqlServerConnections.FirstOrDefault() ?? new SqlServerSettings();
            }
        }

        /// <summary>
        /// Gets the default or first web service connection.
        /// Returns a new empty PCVueWebServiceSettings if no connections exist.
        /// </summary>
        public PCVueWebServiceSettings DefaultWebServiceConnection
        {
            get
            {
                return WebServiceConnections.FirstOrDefault(c => c.IsDefault) ?? WebServiceConnections.FirstOrDefault() ?? new PCVueWebServiceSettings();
            }
        }
    }
}