using PoWorks_Rework.Models;
/// <summary>
/// Model representing a meter for trends analysis
/// Contains both meter database info and WebService variable mapping
/// </summary>
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

    // WebService-specific properties
    public string OriginalVariableName { get; set; } = "";

    // Runtime properties for trends processing
    public string? AssignedConnectionId { get; set; }
    public DateTime? LastTrendsCheck { get; set; }
    public bool HasTrendsData { get; set; }
    public string? TrendsErrorMessage { get; set; }
}

/// <summary>
/// Request model for getting trends data for imported meters
/// </summary>
public class GetTrendsForImportedMetersRequest
{
    public string ConnectionId { get; set; } = "";
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public List<int> SpecificMeterIds { get; set; } = new();
    public bool GetAllImported { get; set; } = true;
    public bool ActiveOnly { get; set; } = true;
    public int MeterLimit { get; set; } = 0; // 0 = no limit
}

/// <summary>
/// Response model for meter trends processing results
/// </summary>
public class ImportedMetersTrendsResponse
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public List<MeterTrendsResult> MeterResults { get; set; } = new();
    public TrendsProcessingSummary Summary { get; set; } = new();
    public DateTime ProcessedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Individual meter trends processing result
/// </summary>
public class MeterTrendsResult
{
    public int MeterId { get; set; }
    public string MeterName { get; set; } = "";
    public string OriginalVariableName { get; set; } = "";

    // Results from GetTrendsData endpoint
    public bool GetTrendsDataSuccess { get; set; }
    public string? GetTrendsDataError { get; set; }
    public List<TrendDataPoint>? TrendsData { get; set; }
    public int TrendsDataPointsCount { get; set; }
    public string? TrendsRequestId { get; set; }

    // Results from ImportWebServiceVariablesWithTrends endpoint  
    public bool ImportTrendsSuccess { get; set; }
    public string? ImportTrendsError { get; set; }
    public string ImportAction { get; set; } = ""; // "Created", "Updated", "Skipped", "Failed"
    public int ImportedDataPoints { get; set; }

    // Analysis data
    public double? MinValue { get; set; }
    public double? MaxValue { get; set; }
    public double? AverageValue { get; set; }
    public DateTime? FirstTimestamp { get; set; }
    public DateTime? LastTimestamp { get; set; }
    public TimeSpan ProcessingDuration { get; set; }
}

/// <summary>
/// Overall summary of trends processing operation
/// </summary>
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

    // Calculated properties
    public double SuccessRate => TotalMetersProcessed > 0 ? (double)SuccessfulMeters / TotalMetersProcessed * 100 : 0;
    public double FailureRate => TotalMetersProcessed > 0 ? (double)FailedMeters / TotalMetersProcessed * 100 : 0;
    public double AverageDataPointsPerMeter => SuccessfulMeters > 0 ? (double)TotalDataPointsRetrieved / SuccessfulMeters : 0;
}