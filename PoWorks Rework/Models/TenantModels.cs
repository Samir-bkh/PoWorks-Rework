// Models/TenantModels.cs
using PoWorks_Rework.Models;

namespace PoWorks_Rework.Models
{
    public class TenantViewModel
    {
        public string SearchCriteria { get; set; } = "Company Name";
        public string SearchTerm { get; set; } = "";
        public List<Tenant> SearchResults { get; set; } = new List<Tenant>();
        public Tenant SelectedTenant { get; set; } = new Tenant();
        public TenantConsumptionData ConsumptionData { get; set; } = new TenantConsumptionData();
        public int TotalPages { get; set; } = 1;
        public int CurrentPage { get; set; } = 1;
        public int TotalItems { get; set; } = 1;
    }

    public class Tenant
    {
        // Tenant Details
        public int Id { get; set; }
        public string CompanyName { get; set; } = "PoWorks";
        public string Contact { get; set; } = "Abdul";
        public string Email { get; set; } = "ww@cs.com";
        public string Phone { get; set; } = "3333333333";
        public string Address1 { get; set; } = "here";
        public string Address2 { get; set; } = "";
        public string PostCode { get; set; } = "12345";
        public string City { get; set; } = "KL";
        public string Unit { get; set; } = "101";
        public bool Active { get; set; } = true;

        // Tenant Management
        public string StartDate { get; set; } = "2017-5-23";
        public string Period { get; set; } = "Monthly";

        // Tariff
        public string TariffType { get; set; } = "Company";
        public decimal BaseRate { get; set; } = 0.5m;
        public decimal Threshold1 { get; set; } = 100m;
        public decimal Threshold1Rate { get; set; } = 0.6m;
        public decimal Threshold2 { get; set; } = 200m;
        public decimal Threshold2Rate { get; set; } = 0.8m;

        // Other Information
        public decimal Deposit { get; set; } = 3500m;
        public decimal Outstanding { get; set; } = 0m;
        public decimal Overdue { get; set; } = 0m;
        public bool EmailAlert { get; set; } = true;
        public bool PrintBill { get; set; } = true;
        public bool EmailBill { get; set; } = true;
    }
}

// Add to Models/TenantModels.cs
public class TenantConsumptionData
{
    public decimal Overdue { get; set; } = 0m;
    public decimal TotalBilledOutstanding { get; set; } = 0m;
    public decimal TotalMonthUnbilled { get; set; } = 0m;

    public List<MonthlyConsumption> YearlyData { get; set; } = new List<MonthlyConsumption>();
    public List<DailyConsumption> WeeklyData { get; set; } = new List<DailyConsumption>();
    public List<MeterData> Meters { get; set; } = new List<MeterData>();
}

public class MonthlyConsumption
{
    public string Month { get; set; } = "";
    public decimal Value { get; set; }
    public bool IsHighlighted { get; set; } = false;
}

public class DailyConsumption
{
    public string Date { get; set; } = "";
    public decimal Value { get; set; }
    public bool IsHighlighted { get; set; } = false;
}

public class MeterData
{
    public string Name { get; set; } = "";
    public string Unit { get; set; } = "";
    public string LastReading { get; set; } = "";
    public bool Active { get; set; } = true;
}