using PoWorks_Rework.Models;
public class MeterForTrendsAnalysis
{
    public int MeterId { get; set; }
    public string Name { get; set; } = "";
    public string? Label { get; set; }
    public string Unit { get; set; } = "";
    public string Type { get; set; } = "";
    public bool Active { get; set; }
    public int? TenantId { get; set; }
    public string? TenantName { get; set; }
    public string OriginalVariableName { get; set; } = "";
    public string? AssignedConnectionId { get; set; }
    public DateTime? LastTrendsCheck { get; set; }
    public bool HasTrendsData { get; set; }
    public string? TrendsErrorMessage { get; set; }
}
public class GetTrendsForImportedMetersRequest
{
    public string ConnectionId { get; set; } = "";
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public List<int> SpecificMeterIds { get; set; } = new();
    public bool GetAllImported { get; set; } = true;
    public bool ActiveOnly { get; set; } = true;
    public int MeterLimit { get; set; } = 0; 
}
public class ImportedMetersTrendsResponse
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public List<MeterTrendsResult> MeterResults { get; set; } = new();
    public TrendsProcessingSummary Summary { get; set; } = new();
    public DateTime ProcessedAt { get; set; } = DateTime.UtcNow;
}
public class MeterTrendsResult
{
    public int MeterId { get; set; }
    public string MeterName { get; set; } = "";
    public string OriginalVariableName { get; set; } = "";
    public bool GetTrendsDataSuccess { get; set; }
    public string? GetTrendsDataError { get; set; }
    public List<TrendDataPoint>? TrendsData { get; set; }
    public int TrendsDataPointsCount { get; set; }
    public string? TrendsRequestId { get; set; }
    public bool ImportTrendsSuccess { get; set; }
    public string? ImportTrendsError { get; set; }
    public string ImportAction { get; set; } = ""; 
    public int ImportedDataPoints { get; set; }
    public double? MinValue { get; set; }
    public double? MaxValue { get; set; }
    public double? AverageValue { get; set; }
    public DateTime? FirstTimestamp { get; set; }
    public DateTime? LastTimestamp { get; set; }
    public TimeSpan ProcessingDuration { get; set; }
}
public class TrendsProcessingSummary
{
    public int TotalMetersProcessed { get; set; }
    public int SuccessfulMeters { get; set; }
    public int FailedMeters { get; set; }
    public int TotalDataPointsRetrieved { get; set; }
    public int TotalDataPointsImported { get; set; }
    public TimeSpan TotalProcessingTime { get; set; }
    public string ConnectionUsed { get; set; } = "";
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public List<string> Errors { get; set; } = new();
    public double SuccessRate => TotalMetersProcessed > 0 ? (double)SuccessfulMeters / TotalMetersProcessed * 100 : 0;
    public double FailureRate => TotalMetersProcessed > 0 ? (double)FailedMeters / TotalMetersProcessed * 100 : 0;
    public double AverageDataPointsPerMeter => SuccessfulMeters > 0 ? (double)TotalDataPointsRetrieved / SuccessfulMeters : 0;
}