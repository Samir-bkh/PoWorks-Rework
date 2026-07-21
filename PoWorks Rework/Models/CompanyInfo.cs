namespace PoWorks_Rework.Models
{
    public class CompanyInfo
    {
        public string CompanyName { get; set; } = string.Empty;
        public string RegistrationNumber { get; set; } = string.Empty;
        public string Address1 { get; set; } = string.Empty;
        public string Address2 { get; set; } = string.Empty;
        public string PostCode { get; set; } = string.Empty;
        public string Country { get; set; } = string.Empty;
        public string City { get; set; } = string.Empty;
        public string GstId { get; set; } = string.Empty;
        public decimal GstPercentage { get; set; }
        public string Phone { get; set; } = string.Empty;
        public string Fax { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string LogoPath { get; set; } = string.Empty;
    }
}