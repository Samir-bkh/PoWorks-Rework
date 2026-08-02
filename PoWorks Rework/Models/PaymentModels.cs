using System.Collections.Generic;

namespace PoWorks_Rework.Models
{
    /// <summary>
    /// View model for the payment dashboard providing financial overview.
    /// Aggregates payment metrics and recent transactions for dashboard display.
    /// </summary>
    public class PaymentDashboardViewModel
    {
        /// <summary>
        /// Total payment amount collected during the current month
        /// </summary>
        public decimal TotalCollectedThisMonth { get; set; }

        /// <summary>
        /// Count of bills awaiting payment (not yet overdue)
        /// </summary>
        public int PendingBillsCount { get; set; }

        /// <summary>
        /// Count of bills past their due date
        /// </summary>
        public int OverdueBillsCount { get; set; }

        /// <summary>
        /// List of recently recorded payments
        /// </summary>
        public List<PaymentEntity> RecentPayments { get; set; } = new List<PaymentEntity>();

        /// <summary>
        /// List of active invoices available for payment recording
        /// </summary>
        public List<InvoiceLookupOption> ActiveInvoices { get; set; } = new List<InvoiceLookupOption>();
    }

    /// <summary>
    /// Represents an invoice option for payment lookup and recording.
    /// Used in payment entry forms to select which invoice to apply a payment to.
    /// </summary>
    public class InvoiceLookupOption
    {
        /// <summary>
        /// Unique identifier for the bill/invoice
        /// </summary>
        public int BillId { get; set; }

        /// <summary>
        /// Invoice reference number for customer communication
        /// </summary>
        public string BillNumber { get; set; }

        /// <summary>
        /// Name of the tenant/customer
        /// </summary>
        public string TenantName { get; set; }

        /// <summary>
        /// Total bill amount including all charges and tax
        /// </summary>
        public decimal GrandTotal { get; set; }

        /// <summary>
        /// Outstanding amount still unpaid
        /// </summary>
        public decimal RemainingAmount { get; set; }
    }
}