namespace PoWorks_Rework.Models
{

    public class HDSMeterItem
    {
        public string HdsMeterName { get; set; } = "";

        public string? Unit { get; set; }

        public string Type { get; set; } = "Main";

        public string? ParentMeterId { get; set; }

        public bool Active { get; set; } = true;

        public bool IsSelected { get; set; } = true;

        public string? LastReading { get; set; }
    }
}