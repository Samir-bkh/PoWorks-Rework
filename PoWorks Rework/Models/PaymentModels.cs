using System.Collections.Generic;

namespace PoWorks_Rework.Models
{
    public class PaymentDashboardViewModel
    {
        // --- 1. Les KPI (Cartes en haut de l'écran) ---
        public decimal TotalCollectedThisMonth { get; set; }
        public int PendingBillsCount { get; set; }
        public int OverdueBillsCount { get; set; }

        // --- 2. Le Registre Historique (Tableau central) ---
        public List<PaymentEntity> RecentPayments { get; set; } = new List<PaymentEntity>();

        // --- 3. Pour le Modal de saisie manuelle ---
        public List<InvoiceLookupOption> ActiveInvoices { get; set; } = new List<InvoiceLookupOption>();
    }

    public class InvoiceLookupOption
    {
        public int BillId { get; set; }
        public string BillNumber { get; set; }
        public string TenantName { get; set; }
        public decimal GrandTotal { get; set; }
        public decimal RemainingAmount { get; set; }
    }
}