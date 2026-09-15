namespace PoWorks_Rework.Models
{
    public sealed class AuditEvent
    {
        public string Action { get; init; } = "";
        public string EntityType { get; init; } = "";
        public string? EntityId { get; init; }
        public string Summary { get; init; } = "";
        public object? Before { get; init; }
        public object? After { get; init; }
        public bool Success { get; init; } = true;
        public int? CompanyId { get; init; }
        public string? ActorUserName { get; init; }
        public string? ActorUserId { get; init; }
        public string? ActorUserType { get; init; }
    }

    public sealed class AuditLogItem
    {
        public long AuditLogId { get; set; }
        public DateTimeOffset TimestampUtc { get; set; }
        public string? UserName { get; set; }
        public string? UserType { get; set; }
        public int? CompanyId { get; set; }
        public string Action { get; set; } = "";
        public string EntityType { get; set; } = "";
        public string? EntityId { get; set; }
        public string Summary { get; set; } = "";
        public string? BeforeJson { get; set; }
        public string? AfterJson { get; set; }
        public bool Success { get; set; }
        public string? IpAddress { get; set; }
        public string? CorrelationId { get; set; }
    }

    public sealed class AuditLogPageViewModel
    {
        public List<AuditLogItem> Items { get; set; } = new();
        public string? Search { get; set; }
        public string? Action { get; set; }
        public string? EntityType { get; set; }
        public int? CompanyId { get; set; }
        public DateTime? From { get; set; }
        public DateTime? To { get; set; }
        public bool ShowTechnical { get; set; }
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 20;
        public int TotalCount { get; set; }
        public int TotalPages => Math.Max(1, (int)Math.Ceiling(TotalCount / (double)PageSize));
        public string LogDirectory { get; set; } = "";
    }
}
