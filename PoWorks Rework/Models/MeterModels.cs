using Microsoft.AspNetCore.Mvc.Rendering;

namespace PoWorks_Rework.Models
{
    public class MeterManagementViewModel
    {
        public MeterSearchCriteria SearchCriteria { get; set; } = new();
        public List<Meter> SearchResults { get; set; } = new();
        public Meter? SelectedMeter { get; set; }
        public List<Meter> SubMeters { get; set; } = new();
        public List<SelectListItem> TenantOptions { get; set; } = new();
        public List<SelectListItem> ParentMeterOptions { get; set; } = new();
        public MeterDependencySummary SelectedMeterDependencies { get; set; } = new();
        public MeterSummary Summary { get; set; } = new();
        public int TotalPages { get; set; } = 1;
        public int CurrentPage { get; set; } = 1;
        public int TotalItems { get; set; }
        public int PageSize { get; set; } = 20;
    }

    public class MeterSearchCriteria
    {
        public string SearchField { get; set; } = "Name";
        public string? SearchTerm { get; set; }
        public string StatusFilter { get; set; } = "All";
        public string AssignmentFilter { get; set; } = "All";
    }

    public class Meter
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public string? Label { get; set; }
        public string Type { get; set; } = "Main";
        public string? ParentMeterId { get; set; }
        public string? ParentMeterName { get; set; }
        public string LastReading { get; set; } = "";
        public string Unit { get; set; } = "";
        public string? TenantId { get; set; }
        public string? TenantName { get; set; }
        public bool Active { get; set; } = true;
    }

    public class MeterSummary
    {
        public int Total { get; set; }
        public int Active { get; set; }
        public int Disabled { get; set; }
        public int Assigned { get; set; }
        public int Unassigned { get; set; }
    }

    public class MeterDependencySummary
    {
        public long RawReadingCount { get; set; }
        public long DailyReadingCount { get; set; }
        public long MonthlyReadingCount { get; set; }
        public long YearlyReadingCount { get; set; }
        public long BillLineCount { get; set; }
        public long ChildMeterCount { get; set; }

        public long HistoricalReadingCount =>
            RawReadingCount + DailyReadingCount + MonthlyReadingCount + YearlyReadingCount;

        public bool HasDependencies =>
            HistoricalReadingCount > 0 || BillLineCount > 0 || ChildMeterCount > 0;

        public bool CanDelete => !HasDependencies;
    }

    public class BulkEditMetersRequest
    {
        public List<int> MeterIds { get; set; } = new();
        public bool SelectAllMatching { get; set; }
        public string? SearchField { get; set; }
        public string? SearchTerm { get; set; }
        public string? StatusFilter { get; set; }
        public string? AssignmentFilter { get; set; }

        public bool UpdateTenant { get; set; }
        public int? TenantId { get; set; }

        public bool UpdateUnit { get; set; }
        public string? Unit { get; set; }

        public bool UpdateType { get; set; }
        public string? Type { get; set; }

        public bool UpdateParent { get; set; }
        public int? ParentId { get; set; }

        public bool UpdateActive { get; set; }
        public bool Active { get; set; }
    }
}
