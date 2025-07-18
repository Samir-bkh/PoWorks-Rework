using static PoWorks_Rework.Controllers.ImportController;
namespace PoWorks_Rework.Models
{
    public class ImportExportViewModel
    {
        public List<string> HdsTables { get; set; } = new List<string>();
        public string SelectedTable { get; set; }
        public string StartDate { get; set; }
        public string EndDate { get; set; }
        public int Limit { get; set; } = 1000;
        public IFormFile VarexpFile { get; set; }
        public List<string[]> VarexpRecords { get; set; } = new List<string[]>();
    }
}


namespace PoWorks_Rework.Models
{
    // ================================
    // TRENDS-RELATED MODELS
    // ================================

    /// <summary>
    /// Request model for processing trends data for multiple variables
    /// </summary>
    public class ProcessTrendsRequest
    {
        public string ConnectionId { get; set; } = "";
        public List<string> VariableNames { get; set; } = new();
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public TrendsOptions Options { get; set; } = new();
    }

    /// <summary>
    /// Options for trends processing
    /// </summary>
    public class TrendsOptions
    {
        public int ElementMaxNumber { get; set; } = 100000;
        public int AggregateFunction { get; set; } = 0; // 0 = Raw, 1 = WindowPixelSize
        public int AggregateParam1 { get; set; } = 0;
        public List<string> Properties { get; set; } = new() { "VariableName", "Description", "StandardLabel" };
        public bool IncludeStartBound { get; set; } = true;
        public bool IncludeEndBound { get; set; } = true;
    }

    /// <summary>
    /// Request payload for creating a single trend request (matches API specification)
    /// </summary>
    public class TrendCreateRequest
    {
        public string VariableName { get; set; } = "";
        public int ElementMaxNumber { get; set; } = 100000;
        public int AggregateFunction { get; set; } = 0; // 0 = Raw, 1 = WindowPixelSize
        public int AggregateParam1 { get; set; } = 0;
        public List<string> Properties { get; set; } = new() { "VariableName", "Description", "StandardLabel" };
        public string Context { get; set; } = "";
        public bool IncludeStartBound { get; set; } = true;
        public bool IncludeEndBound { get; set; } = true;
    }

    /// <summary>
    /// Response model for trends processing (returned to client)
    /// </summary>
    public class ProcessTrendsResponse
    {
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public List<VariableTrendsResult> Results { get; set; } = new();
        public TrendsSummary Summary { get; set; } = new();
        public DateTime ProcessedAt { get; set; } = DateTime.UtcNow;
    }

    /// <summary>
    /// Result for a single variable's trends processing
    /// </summary>
    public class VariableTrendsResult
    {
        public string VariableName { get; set; } = "";
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public string? RequestId { get; set; }
        public List<TrendDataPoint> TrendData { get; set; } = new();
        public bool MaxNumberExceeded { get; set; }
        public int DataPointsCount { get; set; }
        public DateTime? FirstTimestamp { get; set; }
        public DateTime? LastTimestamp { get; set; }
    }

    /// <summary>
    /// Individual trend data point (matches API response structure)
    /// </summary>
    public class TrendDataPoint
    {
        public double Value { get; set; }
        public string Timestamp { get; set; } = "";
        public string Quality { get; set; } = "";
        public int QualityValue { get; set; }
        public object? Properties { get; set; }

        // Helper properties for easier processing
        public DateTime? TimestampParsed
        {
            get
            {
                if (DateTime.TryParse(Timestamp, out var result))
                    return result;
                return null;
            }
        }

        public bool IsGoodQuality => Quality.Equals("Good", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Summary information for trends processing
    /// </summary>
    public class TrendsSummary
    {
        public int TotalVariables { get; set; }
        public int SuccessfulVariables { get; set; }
        public int FailedVariables { get; set; }
        public int TotalDataPoints { get; set; }
        public DateTime? OverallStartTime { get; set; }
        public DateTime? OverallEndTime { get; set; }
        public TimeSpan ProcessingDuration { get; set; }

        public double SuccessRate => TotalVariables > 0 ? (double)SuccessfulVariables / TotalVariables * 100 : 0;
    }

    /// <summary>
    /// Request model for importing web service variables with trends data
    /// </summary>
    public class ImportWebServiceVariablesWithTrendsRequest
    {
        public List<WebServiceVariableWithTrends> Variables { get; set; } = new();
        public bool SkipExisting { get; set; }
        public bool UpdateExisting { get; set; }
        public bool ImportTrendsData { get; set; } = true;
        public DateTime? TrendsStartDate { get; set; }
        public DateTime? TrendsEndDate { get; set; }
        public string ConnectionId { get; set; } = "";
    }

    /// <summary>
    /// Web service variable with associated trends data
    /// </summary>
    public class WebServiceVariableWithTrends : WebServiceVariableItem
    {
        public List<TrendDataPoint> TrendsData { get; set; } = new();
        public bool TrendsDataAvailable { get; set; }
        public string? TrendsErrorMessage { get; set; }
        public int TrendsDataPointsCount { get; set; }
        public DateTime? TrendsStartDate { get; set; }
        public DateTime? TrendsEndDate { get; set; }
    }

    /// <summary>
    /// Response model for variable import with trends
    /// </summary>
    public class ImportVariablesWithTrendsResponse
    {
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public ImportSummary ImportSummary { get; set; } = new();
        public TrendsSummary TrendsSummary { get; set; } = new();
        public List<VariableImportResult> Results { get; set; } = new();
    }

    /// <summary>
    /// Summary for variable import operations
    /// </summary>
    public class ImportSummary
    {
        public int TotalVariables { get; set; }
        public int ImportedVariables { get; set; }
        public int SkippedVariables { get; set; }
        public int UpdatedVariables { get; set; }
        public int FailedVariables { get; set; }
        public List<string> Errors { get; set; } = new();
    }

    /// <summary>
    /// Result for individual variable import
    /// </summary>
    public class VariableImportResult
    {
        public string VariableName { get; set; } = "";
        public bool ImportSuccess { get; set; }
        public bool TrendsSuccess { get; set; }
        public string? ImportErrorMessage { get; set; }
        public string? TrendsErrorMessage { get; set; }
        public string Action { get; set; } = ""; // "Created", "Updated", "Skipped", "Failed"
        public int? MeterId { get; set; }
        public int TrendsDataPointsImported { get; set; }
    }

    // ================================
    // INTERNAL SERVICE MODELS (used by TrendsService)
    // ================================

    /// <summary>
    /// Internal result model for trend request creation
    /// </summary>
    public class TrendRequestResult
    {
        public bool Success { get; set; }
        public string? RequestId { get; set; }
        public string? VariableName { get; set; }
        public string? ErrorMessage { get; set; }
    }

    /// <summary>
    /// Internal result model for trend data retrieval
    /// </summary>
    public class TrendDataResult
    {
        public bool Success { get; set; }
        public string? RequestId { get; set; }
        public List<TrendDataPoint> Values { get; set; } = new();
        public bool MaxNumberExceeded { get; set; }
        public string? ErrorMessage { get; set; }
    }

    /// <summary>
    /// Internal result model for variable trends processing
    /// </summary>
    public class VariableTrendResult
    {
        public string VariableName { get; set; } = "";
        public bool Success { get; set; }
        public string? RequestId { get; set; }
        public List<TrendDataPoint> TrendData { get; set; } = new();
        public bool MaxNumberExceeded { get; set; }
        public string? ErrorMessage { get; set; }
    }

    /// <summary>
    /// API response structure for trends (matches PCVue API)
    /// </summary>
    public class TrendApiResponse
    {
        public List<TrendDataPoint> Values { get; set; } = new();
        public bool MaxNumberExceeded { get; set; }
    }
}