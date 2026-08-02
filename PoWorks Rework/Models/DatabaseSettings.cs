using System.ComponentModel.DataAnnotations;
namespace PoWorks_Rework.Models
{
    /// <summary>
    /// Represents PostgreSQL database connection configuration.
    /// Stores credentials and connection parameters for the primary database.
    /// </summary>
    public class DatabaseSettings
    {
        /// <summary>
        /// Database server hostname or IP address
        /// </summary>
        [Display(Name = "Host")]
        public string Host { get; set; } = "localhost";

        /// <summary>
        /// Port number for database connection (default 5432 for PostgreSQL)
        /// </summary>
        [Display(Name = "Port")]
        public string Port { get; set; } = "5432";

        /// <summary>
        /// Name of the database to connect to
        /// </summary>
        [Display(Name = "Database")]
        public string Database { get; set; } = "";

        /// <summary>
        /// Username for database authentication
        /// </summary>
        [Display(Name = "Username")]
        public string Username { get; set; } = "postgres";

        /// <summary>
        /// Password for database authentication
        /// </summary>
        [Display(Name = "Password")]
        public string Password { get; set; } = "";

        /// <summary>
        /// SSL/TLS mode for secure connection (Prefer, Require, Disable, etc.)
        /// </summary>
        [Display(Name = "SSL Mode")]
        public string SSLMode { get; set; } = "Prefer";

        /// <summary>
        /// Generates a PostgreSQL connection string from configured parameters.
        /// Includes command timeout, connection timeout, and keepalive settings.
        /// </summary>
        public string ToConnectionString()
        {
            return $"Host={Host};Port={Port};Database={Database};Username={Username};Password={Password};SSL Mode={SSLMode};" +
                   $"Command Timeout=300;Timeout=30;Keepalive=30;";
        }
    }
}