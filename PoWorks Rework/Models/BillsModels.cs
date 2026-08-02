using System;
using System.Collections.Generic;

namespace PoWorks_Rework.Models
{
    /// <summary>
    /// View model for bills search and management interface.
    /// Handles search, pagination, and filtering with dropdown options.
    /// </summary>
    public class BillsViewModel
    {
        /// <summary>
        /// Field to search by (e.g., "Meter Name", "Tenant Name")
        /// </summary>
        public string SearchCriteria { get; set; } = "Meter Name";

        /// <summary>
        /// Search term entered by the user
        /// </summary>
        public string SearchTerm { get; set; } = "";

        /// <summary>
        /// List of bills matching the search criteria
        /// </summary>
        public List<Bill> SearchResults { get; set; } = new List<Bill>();

        /// <summary>
        /// Total number of pages for paginated results
        /// </summary>
        public int TotalPages { get; set; } = 1;

        /// <summary>
        /// Current page number in pagination
        /// </summary>
        public int CurrentPage { get; set; } = 1;

        /// <summary>
        /// Total number of items matching the search
        /// </summary>
        public int TotalItems { get; set; } = 0;

        /// <summary>
        /// List of available meters for filtering dropdown
        /// </summary>
        public List<DropdownOption> MeterOptions { get; set; } = new List<DropdownOption>();

        /// <summary>
        /// List of available tenants for filtering dropdown
        /// </summary>
        public List<DropdownOption> TenantOptions { get; set; } = new List<DropdownOption>();
    }

    /// <summary>
    /// Represents a bill summary for display in lists.
    /// Contains essential billing information for quick viewing.
    /// </summary>
    public class Bill
    {
        /// <summary>
        /// Unique identifier for the bill
        /// </summary>
        public int Id { get; set; }

        /// <summary>
        /// Name of the tenant being billed
        /// </summary>
        public string Tenant { get; set; } = "";

        /// <summary>
        /// Name of the meter associated with this bill
        /// </summary>
        public string Meter { get; set; } = "";

        /// <summary>
        /// Date when the bill was generated
        /// </summary>
        public string BillDate { get; set; } = "";

        /// <summary>
        /// Total consumption quantity for the billing period
        /// </summary>
        public decimal TotalConsumption { get; set; }

        /// <summary>
        /// Net total amount of the bill (before tax)
        /// </summary>
        public decimal NetTotal { get; set; }
    }

    /// <summary>
    /// Generic dropdown option model for UI selection lists.
    /// Used in various views for filtering and selection.
    /// </summary>
    public class DropdownOption
    {
        /// <summary>
        /// Value submitted with form (typically an ID)
        /// </summary>
        public string Value { get; set; } = "";

        /// <summary>
        /// Display text shown to the user
        /// </summary>
        public string Text { get; set; } = "";
    }

    /// <summary>
    /// Complete bill entity with full details and line items.
    /// Represents a complete bill record stored in the database.
    /// </summary>
    public class BillEntity
    {
        /// <summary>
        /// Unique identifier for the bill
        /// </summary>
        public int BillId { get; set; }

        /// <summary>
        /// Foreign key linking to the tenant
        /// </summary>
        public int TenantID { get; set; }

        /// <summary>
        /// Tenant name for display
        /// </summary>
        public string? TenantName { get; set; }

        /// <summary>
        /// Unique bill number for reference
        /// </summary>
        public string? BillNumber { get; set; }

        /// <summary>
        /// Start date of the billing period
        /// </summary>
        public DateTime PeriodStart { get; set; }

        /// <summary>
        /// End date of the billing period
        /// </summary>
        public DateTime PeriodEnd { get; set; }

        /// <summary>
        /// Total kilowatt-hours consumed during the period
        /// </summary>
        public decimal TotalKWh { get; set; }

        /// <summary>
        /// Total amount before tax
        /// </summary>
        public decimal AmountExclTax { get; set; }

        /// <summary>
        /// Tax amount calculated on the bill
        /// </summary>
        public decimal TaxAmount { get; set; }

        /// <summary>
        /// Total amount including tax
        /// </summary>
        public decimal AmountInclTax { get; set; }

        /// <summary>
        /// Current status of the bill (e.g., Draft, Generated, Sent, Paid)
        /// </summary>
        public string Status { get; set; } = "Draft";

        /// <summary>
        /// Timestamp when bill was created
        /// </summary>
        public DateTime GeneratedAt { get; set; }

        /// <summary>
        /// Timestamp when bill was validated
        /// </summary>
        public DateTime? ValidatedAt { get; set; }

        /// <summary>
        /// Timestamp when bill was paid
        /// </summary>
        public DateTime? PaidAt { get; set; }

        /// <summary>
        /// Additional notes or comments on the bill
        /// </summary>
        public string? Notes { get; set; }

        /// <summary>
        /// Collection of line items (charges) on this bill
        /// </summary>
        public List<BillLineItemEntity> LineItems { get; set; } = new();
    }

    /// <summary>
    /// Represents a single line item (charge) on a bill.
    /// Each line item corresponds to a meter's consumption.
    /// </summary>
    public class BillLineItemEntity
    {
        /// <summary>
        /// Unique identifier for this line item
        /// </summary>
        public int LineItemId { get; set; }

        /// <summary>
        /// Foreign key reference to the bill
        /// </summary>
        public int BillId { get; set; }

        /// <summary>
        /// Foreign key reference to the meter
        /// </summary>
        public int MeterId { get; set; }

        /// <summary>
        /// Display name of the meter
        /// </summary>
        public string MeterName { get; set; } = "";

        /// <summary>
        /// Quantity of consumption
        /// </summary>
        public decimal Consumption { get; set; }

        /// <summary>
        /// Unit of measurement (e.g., kWh, m³, L)
        /// </summary>
        public string Unit { get; set; } = "";

        /// <summary>
        /// Price per unit
        /// </summary>
        public decimal UnitPrice { get; set; }

        /// <summary>
        /// Total for this line (Consumption × UnitPrice), before tax
        /// </summary>
        public decimal LineTotalExclTax { get; set; }
    }

    /// <summary>
    /// Request payload for generating a new bill.
    /// Specifies tenant and billing period for calculation.
    /// </summary>
    public class GenerateBillRequest
    {
        /// <summary>
        /// ID of the tenant to bill
        /// </summary>
        public int TenantID { get; set; }

        /// <summary>
        /// Start date of the billing period
        /// </summary>
        public DateTime PeriodStart { get; set; }

        /// <summary>
        /// End date of the billing period
        /// </summary>
        public DateTime PeriodEnd { get; set; }
    }
}