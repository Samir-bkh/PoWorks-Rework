using System.ComponentModel.DataAnnotations;

namespace PoWorks_Rework.Models
{
    public class DatabaseSettings
    {
        [Display(Name = "Host")]
        public string Host { get; set; } = "localhost";

        [Display(Name = "Port")]
        public string Port { get; set; } = "5432";

        [Display(Name = "Database")]
        public string Database { get; set; } = "";

        [Display(Name = "Username")]
        public string Username { get; set; } = "postgres";

        [Display(Name = "Password")]
        public string Password { get; set; } = "";

        [Display(Name = "SSL Mode")]
        public string SSLMode { get; set; } = "Prefer";

        // Helper method to generate Npgsql connection string
        public string ToConnectionString()
        {
            return $"Host={Host};Port={Port};Database={Database};Username={Username};Password={Password};SSL Mode={SSLMode};";
        }
    }
}