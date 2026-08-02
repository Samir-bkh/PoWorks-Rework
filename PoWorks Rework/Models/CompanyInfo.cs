namespace PoWorks_Rework.Models
{
    /// <summary>
    /// Represents company information and configuration.
    /// Stores branding, tax information, and contact details used across the application.
    /// </summary>
    public class CompanyInfo
    {
        /// <summary>
        /// Legal name of the company
        /// </summary>
        public string CompanyName { get; set; } = string.Empty;

        /// <summary>
        /// Company registration or business registration number
        /// </summary>
        public string RegistrationNumber { get; set; } = string.Empty;

        /// <summary>
        /// First line of the company address
        /// </summary>
        public string Address1 { get; set; } = string.Empty;

        /// <summary>
        /// Second line of the company address
        /// </summary>
        public string Address2 { get; set; } = string.Empty;

        /// <summary>
        /// Postal code for the company location
        /// </summary>
        public string PostCode { get; set; } = string.Empty;

        /// <summary>
        /// Country where the company is based
        /// </summary>
        public string Country { get; set; } = string.Empty;

        /// <summary>
        /// City where the company is located
        /// </summary>
        public string City { get; set; } = string.Empty;

        /// <summary>
        /// Goods and Services Tax identification number
        /// </summary>
        public string GstId { get; set; } = string.Empty;

        /// <summary>
        /// GST percentage applicable for tax calculations
        /// </summary>
        public decimal GstPercentage { get; set; }

        /// <summary>
        /// Primary phone number for the company
        /// </summary>
        public string Phone { get; set; } = string.Empty;

        /// <summary>
        /// Fax number for the company
        /// </summary>
        public string Fax { get; set; } = string.Empty;

        /// <summary>
        /// Email address for company contact
        /// </summary>
        public string Email { get; set; } = string.Empty;

        /// <summary>
        /// File path to the company logo for branding
        /// </summary>
        public string LogoPath { get; set; } = string.Empty;
    }
}