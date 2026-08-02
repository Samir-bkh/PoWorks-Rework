using Microsoft.AspNetCore.Mvc.Rendering;

namespace PoWorks_Rework.Models
{
    /// <summary>
    /// View model for meter search, management, and editing interface.
    /// Handles pagination, search criteria, and display of meter hierarchies.
    /// </summary>
    public class MeterManagementViewModel
    {
        /// <summary>
        /// Search criteria (field and term) for filtering meters
        /// </summary>
        public MeterSearchCriteria SearchCriteria { get; set; } = new MeterSearchCriteria();

        /// <summary>
        /// List of meters matching the search criteria
        /// </summary>
        public List<Meter> SearchResults { get; set; } = new List<Meter>();

        /// <summary>
        /// The currently selected meter for detailed view or editing
        /// </summary>
        public Meter SelectedMeter { get; set; } = new Meter();

        /// <summary>
        /// Sub-meters that are children of the selected meter
        /// </summary>
        public List<Meter> SubMeters { get; set; } = new List<Meter>();

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
        /// Options for tenant selection dropdown
        /// </summary>
        public List<SelectListItem> TenantOptions { get; set; } = new List<SelectListItem>();
    }

    /// <summary>
    /// Encapsulates search field and search term for meter queries.
    /// </summary>
    public class MeterSearchCriteria
    {
        /// <summary>
        /// Name of the field to search in (e.g., "Name", "Label", "TenantName")
        /// </summary>
        public string SearchField { get; set; } = "Name";

        /// <summary>
        /// Value to search for in the specified field
        /// </summary>
        public string? SearchTerm { get; set; }
    }

    /// <summary>
    /// Represents a meter entity with configuration and status information.
    /// Can be a main meter or a sub-meter linked to a parent.
    /// </summary>
    public class Meter
    {
        /// <summary>
        /// Unique identifier for the meter
        /// </summary>
        public int Id { get; set; }

        /// <summary>
        /// Display name of the meter
        /// </summary>
        public string Name { get; set; } = "";

        /// <summary>
        /// Optional label or alias for the meter
        /// </summary>
        public string? Label { get; set; }

        /// <summary>
        /// Type of meter (e.g., "Main", "Sub", "Distribution")
        /// </summary>
        public string Type { get; set; } = "Main";

        /// <summary>
        /// ID of the parent meter if this is a sub-meter
        /// </summary>
        public string? ParentMeterId { get; set; }

        /// <summary>
        /// Name of the parent meter for display
        /// </summary>
        public string? ParentMeterName { get; set; }

        /// <summary>
        /// Most recent reading value
        /// </summary>
        public string LastReading { get; set; } = "";

        /// <summary>
        /// Unit of measurement (e.g., kWh, m³, L)
        /// </summary>
        public string Unit { get; set; } = "";

        /// <summary>
        /// ID of the tenant who owns this meter
        /// </summary>
        public string? TenantId { get; set; }

        /// <summary>
        /// Name of the tenant for display
        /// </summary>
        public string? TenantName { get; set; }

        /// <summary>
        /// Whether this meter is actively in use
        /// </summary>
        public bool Active { get; set; } = true;
    }

    /// <summary>
    /// Request model for bulk editing multiple meters at once.
    /// Allows updating specific fields across selected meters.
    /// </summary>
    public class BulkEditMetersRequest
    {
        /// <summary>
        /// List of meter IDs to edit
        /// </summary>
        public List<int> MeterIds { get; set; } = new List<int>();

        /// <summary>
        /// If true, apply edit to all meters matching search criteria
        /// </summary>
        public bool SelectAllMatching { get; set; }

        /// <summary>
        /// Search field used to find meters (if SelectAllMatching is true)
        /// </summary>
        public string? SearchField { get; set; }

        /// <summary>
        /// Search term used to find meters (if SelectAllMatching is true)
        /// </summary>
        public string? SearchTerm { get; set; }

        /// <summary>
        /// If true, update the tenant assignment
        /// </summary>
        public bool UpdateTenant { get; set; }

        /// <summary>
        /// New tenant ID to assign
        /// </summary>
        public int? TenantId { get; set; }

        /// <summary>
        /// If true, update the unit of measurement
        /// </summary>
        public bool UpdateUnit { get; set; }

        /// <summary>
        /// New unit to assign
        /// </summary>
        public string? Unit { get; set; }

        /// <summary>
        /// If true, update the meter type
        /// </summary>
        public bool UpdateType { get; set; }

        /// <summary>
        /// New type to assign
        /// </summary>
        public string? Type { get; set; }

        /// <summary>
        /// If true, update the parent meter relationship
        /// </summary>
        public bool UpdateParent { get; set; }

        /// <summary>
        /// New parent meter ID
        /// </summary>
        public int? ParentId { get; set; }
    }
}