namespace PoWorks_Rework.Models
{
    /// <summary>
    /// Represents a meter item imported from HDS (Honeywell Data Source).
    /// Used during the import wizard to select and configure meters from HDS database.
    /// </summary>
    public class HDSMeterItem
    {
        /// <summary>
        /// Name of the meter in the HDS source system
        /// </summary>
        public string HdsMeterName { get; set; } = "";

        /// <summary>
        /// Unit of measurement (e.g., kWh, m³, L)
        /// </summary>
        public string? Unit { get; set; }

        /// <summary>
        /// Type of meter (Main, Sub, Distribution, etc.)
        /// </summary>
        public string Type { get; set; } = "Main";

        /// <summary>
        /// ID of parent meter if this is a sub-meter
        /// </summary>
        public string? ParentMeterId { get; set; }

        /// <summary>
        /// Whether this meter should be imported
        /// </summary>
        public bool Active { get; set; } = true;

        /// <summary>
        /// Whether this meter is selected for import in the wizard
        /// </summary>
        public bool IsSelected { get; set; } = true;

        /// <summary>
        /// Last recorded reading value from the source
        /// </summary>
        public string? LastReading { get; set; }
    }
}