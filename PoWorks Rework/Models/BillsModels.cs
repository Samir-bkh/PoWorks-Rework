// Models/BillsModels.cs
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

        // Options for dropdowns
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
}