// Models/PaymentModels.cs
namespace PoWorks_Rework.Models
{
    public class PaymentViewModel
    {
        public string BillNumber { get; set; } = "";
        public string TenantName { get; set; } = "";
        public decimal BillAmount { get; set; }
        public decimal PaidAmount { get; set; }
        public string MeterName { get; set; } = "";
        public string BillDate { get; set; } = "";
        public decimal RemainingAmount { get; set; }
    }
}