using System;

namespace PoWorks_Rework.Models
{
    /// <summary>
    /// Represents a payment transaction record.
    /// Tracks all payment details including amount, method, and associated bill.
    /// </summary>
    public class PaymentEntity
    {
        /// <summary>
        /// Unique identifier for this payment record
        /// </summary>
        public int PaymentId { get; set; }

        /// <summary>
        /// Foreign key linking to the bill being paid
        /// </summary>
        public int BillId { get; set; }

        /// <summary>
        /// Foreign key linking to the tenant who made the payment
        /// </summary>
        public int TenantID { get; set; }

        /// <summary>
        /// Date when the payment was received/processed
        /// </summary>
        public DateTime? PaymentDate { get; set; }

        /// <summary>
        /// Amount received in this payment transaction
        /// </summary>
        public decimal AmountPaid { get; set; }

        /// <summary>
        /// Method of payment (e.g., Cash, Check, Bank Transfer, Credit Card)
        /// </summary>
        public string PaymentMethod { get; set; }

        /// <summary>
        /// Reference number for the payment (e.g., check number, transaction ID)
        /// </summary>
        public string Reference { get; set; }

        /// <summary>
        /// Additional notes about the payment
        /// </summary>
        public string Notes { get; set; }

        /// <summary>
        /// Timestamp when this payment record was created in the system
        /// </summary>
        public DateTime? RecordedAt { get; set; }

        /// <summary>
        /// Username or ID of the user who recorded this payment
        /// </summary>
        public string RecordedBy { get; set; }

        /// <summary>
        /// Tenant name for display purposes
        /// </summary>
        public string TenantName { get; set; }

        /// <summary>
        /// Original bill total amount
        /// </summary>
        public decimal BillTotalAmount { get; set; }

        /// <summary>
        /// Status of the associated bill
        /// </summary>
        public string BillStatus { get; set; }
    }
}