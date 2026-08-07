using System.ComponentModel.DataAnnotations;

namespace PoWorks_Rework.Models
{
    /// <summary>
    /// Stores SQL Server connection configuration.
    /// Used for importing meter data from HDS or other SQL Server data sources.
    /// </summary>
    public class SqlServerSettings
    {
        /// <summary>
        /// Server hostname or IP address
        /// </summary>
        [Display(Name = "Host")]
        public string Host { get; set; } = "localhost";

        /// <summary>
        /// Port number for SQL Server (default 1433)
        /// </summary>
        [Display(Name = "Port")]
        public string Port { get; set; } = "1433";

        /// <summary>
        /// Database name to connect to
        /// </summary>
        [Display(Name = "Database")]
        public string Database { get; set; } = "";

        /// <summary>
        /// Username for SQL Server authentication
        /// </summary>
        [Display(Name = "Username")]
        public string Username { get; set; } = "";

        /// <summary>
        /// Password for SQL Server authentication
        /// </summary>
        [Display(Name = "Password")]
        public string Password { get; set; } = "";

        /// <summary>
        /// Name of the PCVue project associated with this connection
        /// </summary>
        [Display(Name = "PCVue Project Name")]
        public string ProjectName { get; set; } = "";

        /// <summary>
        /// Unique identifier for this connection
        /// </summary>
        [Display(Name = "Connection ID")]
        public string ConnectionId { get; set; } = Guid.NewGuid().ToString();

        /// <summary>
        /// User-friendly name for this connection
        /// </summary>
        [Display(Name = "Connection Name")]
        public string ConnectionName { get; set; } = "";

        /// <summary>
        /// Whether this is the default connection to use
        /// </summary>
        [Display(Name = "Is Default Connection")]
        public bool IsDefault { get; set; } = false;

        /// <summary>
        /// Generates a SQL Server connection string from the configured parameters.
        /// Includes SSL trust and connection timeout settings.
        /// </summary>
        /// <summary>
        /// Whether to use Windows Authentication (Integrated Security) instead of SQL authentication
        /// </summary>
        [Display(Name = "Use Windows Authentication")]
        public bool UseWindowsAuth { get; set; } = false;

        /// <summary>
        /// Generates a SQL Server connection string from the configured parameters.
        /// Includes SSL trust and connection timeout settings.
        /// </summary>
        public string ToConnectionString()
        {
            var server = string.IsNullOrWhiteSpace(Port)
                ? Host
                : $"{Host},{Port}";

            if (UseWindowsAuth)
            {
                return $"Server={server};" +
                       $"Database={Database};" +
                       $"Integrated Security=True;" +
                       $"TrustServerCertificate=True;" +
                       $"Connection Timeout=30;";
            }

            return $"Server={server};" +
                   $"Database={Database};" +
                   $"User Id={Username};" +
                   $"Password={Password};" +
                   $"TrustServerCertificate=True;" +
                   $"Connection Timeout=30;";
        }
    }
}