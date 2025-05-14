// Models/SqlServerSettings.cs
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

        // Helper method to generate SQL Server connection string
        public string ToConnectionString()
        {
            return $"Server={Host},{Port};Database={Database};User Id={Username};Password={Password};TrustServerCertificate=True;";
        }
    }
}