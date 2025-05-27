// Models/MeterModels.cs
using System.Collections.Generic;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace PoWorks_Rework.Models
{
    public class MeterManagementViewModel
    {
        public MeterSearchCriteria SearchCriteria { get; set; } = new MeterSearchCriteria();
        public List<Meter> SearchResults { get; set; } = new List<Meter>();
        public Meter SelectedMeter { get; set; } = new Meter();
        public List<Meter> SubMeters { get; set; } = new List<Meter>();
        public int TotalPages { get; set; } = 1;
        public int CurrentPage { get; set; } = 1;
        public int TotalItems { get; set; } = 0;

        public List<SelectListItem> TenantOptions { get; set; } = new List<SelectListItem>();
    }

    public class MeterSearchCriteria
    {
        public string SearchField { get; set; } = "Name";
        public string SearchTerm { get; set; } = "";
    }

    public class Meter
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public string Type { get; set; } = "Main";
        public string? ParentMeterId { get; set; }
        public string? ParentMeterName { get; set; }
        public string LastReading { get; set; } = "";
        public string Unit { get; set; } = "";
        public string? TenantId { get; set; }
        public string? TenantName { get; set; }
        public bool Active { get; set; } = true;
    }
}
