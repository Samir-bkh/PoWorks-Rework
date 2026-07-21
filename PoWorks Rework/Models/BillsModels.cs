using System;
using System.Collections.Generic;

namespace PoWorks_Rework.Models
{

    public class BillsViewModel
    {
        public string SearchCriteria { get; set; } = "Meter Name";
        public string SearchTerm { get; set; } = "";
        public List<Bill> SearchResults { get; set; } = new List<Bill>();
        public int TotalPages { get; set; } = 1;
        public int CurrentPage { get; set; } = 1;
        public int TotalItems { get; set; } = 0;
        public List<DropdownOption> MeterOptions { get; set; } = new List<DropdownOption>();
        public List<DropdownOption> TenantOptions { get; set; } = new List<DropdownOption>();
    }
    public class Bill
    {
        public int Id { get; set; }
        public string Tenant { get; set; } = "";
        public string Meter { get; set; } = "";
        public string BillDate { get; set; } = "";
        public decimal TotalConsumption { get; set; }
        public decimal NetTotal { get; set; }
    }

    public class DropdownOption
    {
        public string Value { get; set; } = "";
        public string Text { get; set; } = "";
    }
    public class BillEntity
    {
        public int BillId { get; set; }
        public int TenantID { get; set; }
        public string? TenantName { get; set; } 
        public string? BillNumber { get; set; }
        public DateTime PeriodStart { get; set; }
        public DateTime PeriodEnd { get; set; }
        public decimal TotalKWh { get; set; }
        public decimal AmountExclTax { get; set; }
        public decimal TaxAmount { get; set; }
        public decimal AmountInclTax { get; set; }
        public string Status { get; set; } = "Draft";
        public DateTime GeneratedAt { get; set; }
        public DateTime? ValidatedAt { get; set; }
        public DateTime? PaidAt { get; set; }
        public string? Notes { get; set; }

        public List<BillLineItemEntity> LineItems { get; set; } = new();
    }
    public class BillLineItemEntity
    {
        public int LineItemId { get; set; }
        public int BillId { get; set; }
        public int MeterId { get; set; }
        public string MeterName { get; set; } = "";
        public decimal Consumption { get; set; }
        public string Unit { get; set; } = "";
        public decimal UnitPrice { get; set; }
        public decimal LineTotalExclTax { get; set; }
    }
    public class GenerateBillRequest
    {
        public int TenantID { get; set; }
        public DateTime PeriodStart { get; set; }
        public DateTime PeriodEnd { get; set; }
    }
}