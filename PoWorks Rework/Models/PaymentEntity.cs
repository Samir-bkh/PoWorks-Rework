using System;

namespace PoWorks_Rework.Models
{
    public class PaymentEntity
    {
        public int PaymentId { get; set; }
        public int BillId { get; set; }
        public int TenantID { get; set; }
        public DateTime? PaymentDate { get; set; }
        public decimal AmountPaid { get; set; }
        public string PaymentMethod { get; set; }
        public string Reference { get; set; }
        public string Notes { get; set; }
        public DateTime? RecordedAt { get; set; }
        public string RecordedBy { get; set; }
        public string TenantName { get; set; }
        public decimal BillTotalAmount { get; set; }
        public string BillStatus { get; set; }
    }
}