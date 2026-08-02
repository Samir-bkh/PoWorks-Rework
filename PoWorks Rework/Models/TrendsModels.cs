using PoWorks_Rework.Models;

/// <summary>
/// Represents a meter with its associated trends analysis information.
/// Contains meter details and status of trends data availability from external sources.
/// </summary>
public class MeterForTrendsAnalysis
{
    /// <summary>
    /// Unique identifier for the meter
    /// </summary>
    public int MeterId { get; set; }

    /// <summary>
    /// Display name of the meter
    /// </summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// Optional label or alias for the meter
    /// </summary>
    public string? Label { get; set; }

    /// <summary>
    /// Unit of measurement for this meter (e.g., kWh, m³)
    /// </summary>
    public string Unit { get; set; } = "";

    /// <summary>
    /// Type of meter (e.g., Electric, Gas, Water)
    /// </summary>
    public string Type { get; set; } = "";

    /// <summary>
    /// Whether the meter is currently active
    /// </summary>
    public bool Active { get; set; }

    /// <summary>
    /// Foreign key linking to the tenant who owns this meter
    /// </summary>
    public int? TenantId { get; set; }

    /// <summary>
    /// Name of the tenant for display purposes
    /// </summary>
    public string? TenantName { get; set; }

    /// <summary>
    /// Original variable name from the external source system
    /// </summary>
    public string OriginalVariableName { get; set; } = "";

    /// <summary>
    /// Connection ID used to retrieve trends data
    /// </summary>
    public string? AssignedConnectionId { get; set; }

    /// <summary>
    /// Timestamp of when trends data was last checked
    /// </summary>
    public DateTime? LastTrendsCheck { get; set; }

    /// <summary>
    /// Whether trends data is available for this meter
    /// </summary>
    public bool HasTrendsData { get; set; }

    /// <summary>
    /// Error message if trends data retrieval failed
    /// </summary>
    public string? TrendsErrorMessage { get; set; }
}

/// <summary>
/// Request payload for retrieving trends data from imported meters.
/// Specifies connection, date range, and filtering criteria for the operation.
/// </summary>
public class GetTrendsForImportedMetersRequest
{
    /// <summary>
    /// Connection ID to use for retrieving trends data
    /// </summary>
    public string ConnectionId { get; set; } = "";

    /// <summary>
    /// Start date for the trends data retrieval period
    /// </summary>
    public DateTime StartDate { get; set; }

    /// <summary>
    /// End date for the trends data retrieval period
    /// </summary>
    public DateTime EndDate { get; set; }

    /// <summary>
    /// List of specific meter IDs to retrieve data for
    /// </summary>
    public List<int> SpecificMeterIds { get; set; } = new();

    /// <summary>
    /// If true, retrieve trends for all imported meters (subject to meter limit)
    /// </summary>
    public bool GetAllImported { get; set; } = true;

    /// <summary>
    /// If true, only include active meters in retrieval
    /// </summary>
    public bool ActiveOnly { get; set; } = true;

    /// <summary>
    /// Maximum number of meters to process (0 = no limit)
    /// </summary>
    public int MeterLimit { get; set; } = 0;
}

/// <summary>
/// Response containing results of trends data retrieval and import operation.
/// Includes success status, individual meter results, and processing summary.
/// </summary>
public class ImportedMetersTrendsResponse
{
    /// <summary>
    /// Overall success status of the operation
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// General error message if the operation failed
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Results for each meter processed in the operation
    /// </summary>
    public List<MeterTrendsResult> MeterResults { get; set; } = new();

    /// <summary>
    /// Summary statistics of the entire processing operation
    /// </summary>
    public TrendsProcessingSummary Summary { get; set; } = new();

    /// <summary>
    /// Timestamp when this response was generated
    /// </summary>
    public DateTime ProcessedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Detailed results for a single meter's trends data retrieval and import.
/// Tracks success/failure at each stage and includes data statistics.
/// </summary>
public class MeterTrendsResult
{
    /// <summary>
    /// Unique identifier for the meter
    /// </summary>
    public int MeterId { get; set; }

    /// <summary>
    /// Display name of the meter
    /// </summary>
    public string MeterName { get; set; } = "";

    /// <summary>
    /// Original variable name from the external source
    /// </summary>
    public string OriginalVariableName { get; set; } = "";

    /// <summary>
    /// Whether trends data was successfully retrieved
    /// </summary>
    public bool GetTrendsDataSuccess { get; set; }

    /// <summary>
    /// Error message if trends retrieval failed
    /// </summary>
    public string? GetTrendsDataError { get; set; }

    /// <summary>
    /// List of retrieved trend data points
    /// </summary>
    public List<TrendDataPoint>? TrendsData { get; set; }

    /// <summary>
    /// Total count of data points retrieved
    /// </summary>
    public int TrendsDataPointsCount { get; set; }

    /// <summary>
    /// Request ID from external service for tracking
    /// </summary>
    public string? TrendsRequestId { get; set; }

    /// <summary>
    /// Whether trends data was successfully imported to database
    /// </summary>
    public bool ImportTrendsSuccess { get; set; }

    /// <summary>
    /// Error message if import failed
    /// </summary>
    public string? ImportTrendsError { get; set; }

    /// <summary>
    /// Action performed during import (e.g., "insert", "update", "replace")
    /// </summary>
    public string ImportAction { get; set; } = "";

    /// <summary>
    /// Number of data points successfully imported
    /// </summary>
    public int ImportedDataPoints { get; set; }

    /// <summary>
    /// Minimum value in the trends dataset
    /// </summary>
    public double? MinValue { get; set; }

    /// <summary>
    /// Maximum value in the trends dataset
    /// </summary>
    public double? MaxValue { get; set; }

    /// <summary>
    /// Average value in the trends dataset
    /// </summary>
    public double? AverageValue { get; set; }

    /// <summary>
    /// Timestamp of the first data point
    /// </summary>
    public DateTime? FirstTimestamp { get; set; }

    /// <summary>
    /// Timestamp of the last data point
    /// </summary>
    public DateTime? LastTimestamp { get; set; }

    /// <summary>
    /// Total time taken to process this meter
    /// </summary>
    public TimeSpan ProcessingDuration { get; set; }
}

/// <summary>
/// Aggregated statistics and summary of the entire trends import operation.
/// Provides overall metrics for monitoring and auditing purposes.
/// </summary>
public class TrendsProcessingSummary
{
    /// <summary>
    /// Total number of meters processed
    /// </summary>
    public int TotalMetersProcessed { get; set; }

    /// <summary>
    /// Number of meters successfully processed
    /// </summary>
    public int SuccessfulMeters { get; set; }

    /// <summary>
    /// Number of meters that failed processing
    /// </summary>
    public int FailedMeters { get; set; }

    /// <summary>
    /// Total data points retrieved from external source
    /// </summary>
    public int TotalDataPointsRetrieved { get; set; }

    /// <summary>
    /// Total data points successfully imported to database
    /// </summary>
    public int TotalDataPointsImported { get; set; }

    /// <summary>
    /// Total time spent processing all meters
    /// </summary>
    public TimeSpan TotalProcessingTime { get; set; }

    /// <summary>
    /// Name of the connection used for data retrieval
    /// </summary>
    public string ConnectionUsed { get; set; } = "";

    /// <summary>
    /// Timestamp when processing started
    /// </summary>
    public DateTime StartTime { get; set; }

    /// <summary>
    /// Timestamp when processing completed
    /// </summary>
    public DateTime EndTime { get; set; }

    /// <summary>
    /// List of error messages encountered during processing
    /// </summary>
    public List<string> Errors { get; set; } = new();

    /// <summary>
    /// Percentage of meters successfully processed (0-100)
    /// </summary>
    public double SuccessRate => TotalMetersProcessed > 0 ? (double)SuccessfulMeters / TotalMetersProcessed * 100 : 0;

    /// <summary>
    /// Percentage of meters that failed (0-100)
    /// </summary>
    public double FailureRate => TotalMetersProcessed > 0 ? (double)FailedMeters / TotalMetersProcessed * 100 : 0;

    /// <summary>
    /// Average number of data points retrieved per successfully processed meter
    /// </summary>
    public double AverageDataPointsPerMeter => SuccessfulMeters > 0 ? (double)TotalDataPointsRetrieved / SuccessfulMeters : 0;
}