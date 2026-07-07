using System.ComponentModel.DataAnnotations;

namespace PoWorks_Rework.Models
{
    public class SqlServerSettings
    {
        [Display(Name = "Host")]
        public string Host { get; set; } = "localhost";

        [Display(Name = "Port")]
        public string Port { get; set; } = "1433";

        [Display(Name = "Database")]
        public string Database { get; set; } = "";

        [Display(Name = "Username")]
        public string Username { get; set; } = "";

        [Display(Name = "Password")]
        public string Password { get; set; } = "";

        [Display(Name = "PCVue Project Name")]
        public string ProjectName { get; set; } = "";

        [Display(Name = "Connection ID")]
        public string ConnectionId { get; set; } = Guid.NewGuid().ToString();

        [Display(Name = "Connection Name")]
        public string ConnectionName { get; set; } = "";

        [Display(Name = "Is Default Connection")]
        public bool IsDefault { get; set; } = false;

        // Helper method to generate SQL Server connection string
        public string ToConnectionString()
        {
            // Gère le port vide ou null
            var server = string.IsNullOrWhiteSpace(Port)
                ? Host
                : $"{Host},{Port}";

            return $"Server={server};" +
                   $"Database={Database};" +
                   $"User Id={Username};" +
                   $"Password={Password};" +
                   $"TrustServerCertificate=True;" +
                   $"Connection Timeout=30;";
        }
    }
}