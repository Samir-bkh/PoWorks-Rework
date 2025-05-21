// Models/HDSMeterItem.cs
namespace PoWorks_Rework.Models
{
    /// <summary>
    /// Represents a meter item imported from HDS (Historical Data Storage)
    /// </summary>
    public class HDSMeterItem
    {
        /// <summary>
        /// The original meter name from the HDS system
        /// </summary>
        public string HdsMeterName { get; set; } = "";

        /// <summary>
        /// The unit of measurement for this meter
        /// </summary>
        public string? Unit { get; set; }

        /// <summary>
        /// The type of meter (Main or Sub)
        /// </summary>
        public string Type { get; set; } = "Main";

        /// <summary>
        /// Parent meter ID if this is a sub-meter
        /// </summary>
        public string? ParentMeterId { get; set; }

        /// <summary>
        /// Whether the meter is active
        /// </summary>
        public bool Active { get; set; } = true;

        /// <summary>
        /// Whether this meter is selected for import
        /// </summary>
        public bool IsSelected { get; set; } = true;

        /// <summary>
        /// Last reading value from HDS
        /// </summary>
        public string? LastReading { get; set; }
    }
}