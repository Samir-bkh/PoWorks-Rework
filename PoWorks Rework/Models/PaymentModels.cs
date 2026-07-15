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
    }
}