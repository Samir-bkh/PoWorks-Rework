// Models/TenantModels.cs
using System.Collections.Generic;

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
        public int TotalItems { get; set; } = 0;
    }

    public class Tenant
    {
        // Tenant Details
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

        // Tenant Management
        public string StartDate { get; set; } = System.DateTime.Now.ToString("yyyy-MM-dd");
        public string Period { get; set; } = "Monthly";

        // Tariff
        public string TariffType { get; set; } = "Company";
        public decimal BaseRate { get; set; } = 0.5m;
        public decimal Threshold1 { get; set; } = 100m;
        public decimal Threshold1Rate { get; set; } = 0.6m;
        public decimal Threshold2 { get; set; } = 200m;
        public decimal Threshold2Rate { get; set; } = 0.8m;

        // Other Information
        public decimal Deposit { get; set; } = 0m;
        public decimal Outstanding { get; set; } = 0m;
        public decimal Overdue { get; set; } = 0m;
        public bool EmailAlert { get; set; } = true;
        public bool PrintBill { get; set; } = true;
        public bool EmailBill { get; set; } = true;
    }

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
}