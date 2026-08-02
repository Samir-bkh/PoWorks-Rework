namespace PoWorks_Rework.Models
{
    /// <summary>
    /// Stores global company-wide settings and configuration.
    /// Controls date/time formats, invoice numbering, folder paths, and email/SMS integration settings.
    /// </summary>
    public class CompanySettings
    {
        /// <summary>
        /// Display format for dates in the application
        /// </summary>
        public string DateFormat { get; set; } = "20-12-2016";

        /// <summary>
        /// Display format for times in the application
        /// </summary>
        public string TimeFormat { get; set; } = "16:01:01";

        /// <summary>
        /// Interval in minutes for meter reading collection
        /// </summary>
        public int ReadingInterval { get; set; } = 60;

        /// <summary>
        /// Directory path where generated files (invoices, reports) are saved
        /// </summary>
        public string OutputFolder { get; set; } = "C:/Output";

        /// <summary>
        /// Prefix text prepended to invoice/document numbers
        /// </summary>
        public string Prefix { get; set; } = "INV";

        /// <summary>
        /// Suffix text appended to invoice/document numbers
        /// </summary>
        public string Suffix { get; set; } = "";

        /// <summary>
        /// Number of digits used in the sequential invoice number
        /// </summary>
        public int NumberOfDigits { get; set; } = 5;

        /// <summary>
        /// Template format for invoice numbers (e.g., {PREFIX}{NUMBER}{SUFFIX})
        /// </summary>
        public string Format { get; set; } = "{PREFIX}{NUMBER}{SUFFIX}";

        /// <summary>
        /// SMTP server address for sending emails
        /// </summary>
        public string EmailServer { get; set; } = "smtp.example.com";

        /// <summary>
        /// Username for SMTP email authentication
        /// </summary>
        public string EmailUsername { get; set; } = "user@example.com";

        /// <summary>
        /// Password for SMTP email authentication
        /// </summary>
        public string EmailPassword { get; set; } = "";

        /// <summary>
        /// API endpoint URL for sending SMS messages
        /// </summary>
        public string SmsLink { get; set; } = "https://sms-api.example.com";

        /// <summary>
        /// Username for SMS API authentication
        /// </summary>
        public string SmsUsername { get; set; } = "smsuser";

        /// <summary>
        /// Password for SMS API authentication
        /// </summary>
        public string SmsPassword { get; set; } = "";
    }
}