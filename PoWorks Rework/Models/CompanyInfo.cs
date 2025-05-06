// Models/CompanyInfo.cs
namespace PoWorks_Rework.Models
{
    public class CompanyInfo
    {
        // General
        public string CompanyName { get; set; } = string.Empty;
        public string RegistrationNumber { get; set; } = string.Empty;
        public string Address1 { get; set; } = string.Empty;
        public string Address2 { get; set; } = string.Empty;
        public string PostCode { get; set; } = string.Empty;
        public string Country { get; set; } = string.Empty;
        public string City { get; set; } = string.Empty;

        // Tax
        public string GstId { get; set; } = string.Empty;
        public decimal GstPercentage { get; set; }

        // Contacts
        public string Phone { get; set; } = string.Empty;
        public string Fax { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;

        // Logo
        public string LogoPath { get; set; } = string.Empty;
    }
}