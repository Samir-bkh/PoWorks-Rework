namespace PoWorks_Rework.Models
{
    public class TenantViewModel
    {
        public string SearchCriteria { get; set; } = "Company Name";
        public string SearchTerm { get; set; } = "";
        public List<Tenant> SearchResults { get; set; } = new();
        public Tenant SelectedTenant { get; set; } = new();
        public TenantConsumptionData ConsumptionData { get; set; } = new();
        public TenantDependencySummary Dependencies { get; set; } = new();
        public int TotalPages { get; set; } = 1;
        public int CurrentPage { get; set; } = 1;
        public int TotalItems { get; set; }
    }

    public class Tenant
    {
        public int Id { get; set; }
        public string CompanyName { get; set; } = "";
        public string Contact { get; set; } = "";
        public string Email { get; set; } = "";
        public string Phone { get; set; } = "";
        public string Address1 { get; set; } = "";
        public string Address2 { get; set; } = "";
        public string PostCode { get; set; } = "";
        public string City { get; set; } = "";
        public string Unit { get; set; } = "";
        public bool Active { get; set; } = true;
        public string StartDate { get; set; } = DateTime.Now.ToString("yyyy-MM-dd");
        public string Period { get; set; } = "Monthly";
        public string TariffType { get; set; } = "Company";
        public decimal BaseRate { get; set; } = 0.5m;
        public decimal Threshold1 { get; set; } = 100m;
        public decimal Threshold1Rate { get; set; } = 0.6m;
        public decimal Threshold2 { get; set; } = 200m;
        public decimal Threshold2Rate { get; set; } = 0.8m;
        public decimal Deposit { get; set; }
        public decimal Outstanding { get; set; }
        public decimal Overdue { get; set; }
        public bool EmailAlert { get; set; } = true;
        public bool PrintBill { get; set; } = true;
        public bool EmailBill { get; set; } = true;
        public int AssignedMeterCount { get; set; }
        public int UserCount { get; set; }
        public int BillCount { get; set; }
    }

    public class TenantDependencySummary
    {
        public int UserCount { get; set; }
        public int MeterCount { get; set; }
        public int BillCount { get; set; }
        public int PaymentCount { get; set; }
        public bool HasDependencies => UserCount > 0 || MeterCount > 0 || BillCount > 0 || PaymentCount > 0;
        public bool CanDelete => !HasDependencies;
    }

    public class TenantConsumptionData
    {
        public decimal Overdue { get; set; }
        public decimal TotalBilledOutstanding { get; set; }
        public decimal TotalMonthUnbilled { get; set; }
        public List<MonthlyConsumption> YearlyData { get; set; } = new();
        public List<DailyConsumption> WeeklyData { get; set; } = new();
        public List<MeterData> Meters { get; set; } = new();
    }

    public class MonthlyConsumption
    {
        public string Month { get; set; } = "";
        public decimal Value { get; set; }
        public bool IsHighlighted { get; set; }
    }

    public class DailyConsumption
    {
        public string Date { get; set; } = "";
        public decimal Value { get; set; }
        public bool IsHighlighted { get; set; }
    }

    public class MeterData
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public string Unit { get; set; } = "";
        public string LastReading { get; set; } = "";
        public bool Active { get; set; } = true;
    }
}
