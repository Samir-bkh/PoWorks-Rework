namespace PoWorks_Rework.Models
{
    public class CompanySettings
    {
        public string DateFormat { get; set; } = "20-12-2016";
        public string TimeFormat { get; set; } = "16:01:01";
        public int ReadingInterval { get; set; } = 60;
        public string OutputFolder { get; set; } = "C:/Output";
        public string Prefix { get; set; } = "INV";
        public string Suffix { get; set; } = "";
        public int NumberOfDigits { get; set; } = 5;
        public string Format { get; set; } = "{PREFIX}{NUMBER}{SUFFIX}";
        public string EmailServer { get; set; } = "smtp.example.com";
        public string EmailUsername { get; set; } = "user@example.com";
        public string EmailPassword { get; set; } = "";
        public string SmsLink { get; set; } = "https://sms-api.example.com";
        public string SmsUsername { get; set; } = "smsuser";
        public string SmsPassword { get; set; } = "";
    }
}