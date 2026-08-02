namespace PoWorks_Rework.Models
{
    /// <summary>
    /// View model for tenant search and management.
    /// Handles search criteria, pagination, and displays tenant details with consumption data.
    /// </summary>
    public class TenantViewModel
    {
        /// <summary>
        /// The criteria to search by (e.g., "Company Name", "Email")
        /// </summary>
        public string SearchCriteria { get; set; } = "Company Name";

        /// <summary>
        /// The search term entered by the user
        /// </summary>
        public string SearchTerm { get; set; } = "";

        /// <summary>
        /// List of tenants matching the search criteria
        /// </summary>
        public List<Tenant> SearchResults { get; set; } = new List<Tenant>();

        /// <summary>
        /// The currently selected tenant object
        /// </summary>
        public Tenant SelectedTenant { get; set; } = new Tenant();

        /// <summary>
        /// Consumption data for the selected tenant
        /// </summary>
        public TenantConsumptionData ConsumptionData { get; set; } = new TenantConsumptionData();

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
    }

    /// <summary>
    /// Represents a tenant (customer/company) entity.
    /// Stores all tenant information including billing details, contact info, and tariff rates.
    /// </summary>
    public class Tenant
    {
        /// <summary>
        /// Unique identifier for the tenant
        /// </summary>
        public int Id { get; set; }

        /// <summary>
        /// Name of the tenant company
        /// </summary>
        public string CompanyName { get; set; } = "";

        /// <summary>
        /// Primary contact person for the tenant
        /// </summary>
        public string Contact { get; set; } = "";

        /// <summary>
        /// Email address for the tenant
        /// </summary>
        public string Email { get; set; } = "";

        /// <summary>
        /// Phone number for the tenant
        /// </summary>
        public string Phone { get; set; } = "";

        /// <summary>
        /// First line of postal address
        /// </summary>
        public string Address1 { get; set; } = "";

        /// <summary>
        /// Second line of postal address
        /// </summary>
        public string Address2 { get; set; } = "";

        /// <summary>
        /// Postal code for the tenant location
        /// </summary>
        public string PostCode { get; set; } = "";

        /// <summary>
        /// City name for the tenant location
        /// </summary>
        public string City { get; set; } = "";

        /// <summary>
        /// Building unit or apartment number
        /// </summary>
        public string Unit { get; set; } = "";

        /// <summary>
        /// Indicates whether the tenant account is active
        /// </summary>
        public bool Active { get; set; } = true;

        /// <summary>
        /// Service start date in YYYY-MM-DD format
        /// </summary>
        public string StartDate { get; set; } = System.DateTime.Now.ToString("yyyy-MM-dd");

        /// <summary>
        /// Billing period (e.g., Monthly, Quarterly)
        /// </summary>
        public string Period { get; set; } = "Monthly";

        /// <summary>
        /// Type of tariff applied (e.g., Company, Domestic)
        /// </summary>
        public string TariffType { get; set; } = "Company";

        /// <summary>
        /// Base rate for consumption billing
        /// </summary>
        public decimal BaseRate { get; set; } = 0.5m;

        /// <summary>
        /// First consumption threshold for tiered pricing
        /// </summary>
        public decimal Threshold1 { get; set; } = 100m;

        /// <summary>
        /// Rate applied for consumption between Threshold1 and Threshold2
        /// </summary>
        public decimal Threshold1Rate { get; set; } = 0.6m;

        /// <summary>
        /// Second consumption threshold for tiered pricing
        /// </summary>
        public decimal Threshold2 { get; set; } = 200m;

        /// <summary>
        /// Rate applied for consumption above Threshold2
        /// </summary>
        public decimal Threshold2Rate { get; set; } = 0.8m;

        /// <summary>
        /// Security deposit amount held by the company
        /// </summary>
        public decimal Deposit { get; set; } = 0m;

        /// <summary>
        /// Amount owed by the tenant (not yet overdue)
        /// </summary>
        public decimal Outstanding { get; set; } = 0m;

        /// <summary>
        /// Amount overdue and past payment deadline
        /// </summary>
        public decimal Overdue { get; set; } = 0m;

        /// <summary>
        /// Whether to send email alerts to the tenant
        /// </summary>
        public bool EmailAlert { get; set; } = true;

        /// <summary>
        /// Whether to print physical bills for the tenant
        /// </summary>
        public bool PrintBill { get; set; } = true;

        /// <summary>
        /// Whether to email bills to the tenant
        /// </summary>
        public bool EmailBill { get; set; } = true;
    }

    /// <summary>
    /// Aggregated consumption data for a tenant.
    /// Groups consumption metrics by time period (yearly, weekly) and includes meter-level details.
    /// </summary>
    public class TenantConsumptionData
    {
        /// <summary>
        /// Total overdue amount for the tenant
        /// </summary>
        public decimal Overdue { get; set; } = 0m;

        /// <summary>
        /// Total outstanding amount across all billed items
        /// </summary>
        public decimal TotalBilledOutstanding { get; set; } = 0m;

        /// <summary>
        /// Total consumption amount not yet billed for the current month
        /// </summary>
        public decimal TotalMonthUnbilled { get; set; } = 0m;

        /// <summary>
        /// Monthly consumption data for the year
        /// </summary>
        public List<MonthlyConsumption> YearlyData { get; set; } = new List<MonthlyConsumption>();

        /// <summary>
        /// Daily consumption data for the current week
        /// </summary>
        public List<DailyConsumption> WeeklyData { get; set; } = new List<DailyConsumption>();

        /// <summary>
        /// Details of all meters associated with this tenant
        /// </summary>
        public List<MeterData> Meters { get; set; } = new List<MeterData>();
    }

    /// <summary>
    /// Represents consumption data for a specific month.
    /// Stores value and highlighting for visual presentation in charts.
    /// </summary>
    public class MonthlyConsumption
    {
        /// <summary>
        /// Month identifier (e.g., "January", "Jan 2024", or month number)
        /// </summary>
        public string Month { get; set; } = "";

        /// <summary>
        /// Consumption value for the month
        /// </summary>
        public decimal Value { get; set; }

        /// <summary>
        /// Whether this month should be highlighted in visualization
        /// </summary>
        public bool IsHighlighted { get; set; } = false;
    }

    /// <summary>
    /// Represents consumption data for a specific day.
    /// Stores value and highlighting for visual presentation in charts.
    /// </summary>
    public class DailyConsumption
    {
        /// <summary>
        /// Date of consumption in string format
        /// </summary>
        public string Date { get; set; } = "";

        /// <summary>
        /// Consumption value for the day
        /// </summary>
        public decimal Value { get; set; }

        /// <summary>
        /// Whether this day should be highlighted in visualization
        /// </summary>
        public bool IsHighlighted { get; set; } = false;
    }

    /// <summary>
    /// Represents basic meter information for display.
    /// Contains read-only meter details used in tenant views.
    /// </summary>
    public class MeterData
    {
        /// <summary>
        /// Display name of the meter
        /// </summary>
        public string Name { get; set; } = "";

        /// <summary>
        /// Unit of measurement (e.g., kWh, m³, L)
        /// </summary>
        public string Unit { get; set; } = "";

        /// <summary>
        /// Most recent meter reading value
        /// </summary>
        public string LastReading { get; set; } = "";

        /// <summary>
        /// Whether the meter is actively monitored
        /// </summary>
        public bool Active { get; set; } = true;
    }
}