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
    public class ProcessTrendsRequest
    {
        public string ConnectionId { get; set; } = "";
        public List<string> VariableNames { get; set; } = new();
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public TrendsOptions Options { get; set; } = new();
    }

    public class TrendsOptions
    {
        public int ElementMaxNumber { get; set; } = 100000;
        public int AggregateFunction { get; set; } = 0; 
        public int AggregateParam1 { get; set; } = 0;
        public List<string> Properties { get; set; } = new() { "VariableName", "Description", "StandardLabel" };
        public bool IncludeStartBound { get; set; } = false; 
        public bool IncludeEndBound { get; set; } = false;   
    }

    public class TrendCreateRequest
    {
        public string VariableName { get; set; } = "";
        public int ElementMaxNumber { get; set; } = 100000;
        public int AggregateFunction { get; set; } = 0; 
        public int AggregateParam1 { get; set; } = 0;
        public List<string> Properties { get; set; } = new() { "VariableName", "Description", "StandardLabel" };
        public string Context { get; set; } = "";
        public bool IncludeStartBound { get; set; } = false; 
        public bool IncludeEndBound { get; set; } = false;   
    }
    public class ProcessTrendsResponse
    {
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public List<VariableTrendsResult> Results { get; set; } = new();
        public TrendsSummary Summary { get; set; } = new();
        public DateTime ProcessedAt { get; set; } = DateTime.UtcNow;
    }
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
    public class TrendDataPoint
    {
        public double Value { get; set; }
        public string Timestamp { get; set; } = "";
        public string Quality { get; set; } = "";
        public int QualityValue { get; set; }
        public object? Properties { get; set; }
        public string? Variable { get; set; }
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
    public class WebServiceVariableWithTrends : WebServiceVariableItem
    {
        public List<TrendDataPoint> TrendsData { get; set; } = new();
        public bool TrendsDataAvailable { get; set; }
        public string? TrendsErrorMessage { get; set; }
        public int TrendsDataPointsCount { get; set; }
        public DateTime? TrendsStartDate { get; set; }
        public DateTime? TrendsEndDate { get; set; }
    }
    public class ImportVariablesWithTrendsResponse
    {
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public ImportSummary ImportSummary { get; set; } = new();
        public TrendsSummary TrendsSummary { get; set; } = new();
        public List<VariableImportResult> Results { get; set; } = new();
    }
    public class ImportSummary
    {
        public int TotalVariables { get; set; }
        public int ImportedVariables { get; set; }
        public int SkippedVariables { get; set; }
        public int UpdatedVariables { get; set; }
        public int FailedVariables { get; set; }
        public List<string> Errors { get; set; } = new();
    }
    public class VariableImportResult
    {
        public string VariableName { get; set; } = "";
        public bool ImportSuccess { get; set; }
        public bool TrendsSuccess { get; set; }
        public string? ImportErrorMessage { get; set; }
        public string? TrendsErrorMessage { get; set; }
        public string Action { get; set; } = ""; 
        public int? MeterId { get; set; }
        public int TrendsDataPointsImported { get; set; }
    }
    public class TrendRequestResult
    {
        public bool Success { get; set; }
        public string? RequestId { get; set; }
        public string? VariableName { get; set; }
        public string? ErrorMessage { get; set; }
    }
    public class TrendDataResult
    {
        public bool Success { get; set; }
        public string? RequestId { get; set; }
        public List<TrendDataPoint> Values { get; set; } = new();
        public bool MaxNumberExceeded { get; set; }
        public string? ErrorMessage { get; set; }
    }
    public class VariableTrendResult
    {
        public string VariableName { get; set; } = "";
        public bool Success { get; set; }
        public string? RequestId { get; set; }
        public List<TrendDataPoint> TrendData { get; set; } = new();
        public bool MaxNumberExceeded { get; set; }
        public string? ErrorMessage { get; set; }
    }
    public class TrendApiResponse
    {
        public List<TrendDataPoint> Values { get; set; } = new();
        public bool MaxNumberExceeded { get; set; }
    }
}
public class ImportReadingsRequest
{
    public string TableName { get; set; }
    public List<string> MeterNames { get; set; }
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public int? Limit { get; set; }
}

public class PrintHDSMetersRequest
{
    public string TableName { get; set; } = "";
    public string ConnectionId { get; set; } = "";
    public List<HDSMeterPrintItem> SelectedMeters { get; set; } = new();
    public bool ImportHistoricalReadings { get; set; } = false;
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
}

public class HDSMeterPrintItem
{
    public string HdsMeterName { get; set; } = "";
    public string Unit { get; set; } = "";
    public string Type { get; set; } = "main";
    public string ParentMeterId { get; set; } = "";
    public bool Active { get; set; } = true;
    public string LastReading { get; set; } = "";
    public bool IsSelected { get; set; } = true;
}

public class ImportMetersRequest
{
    public List<HDSMeterPrintItem> Meters { get; set; }
    public bool SkipExisting { get; set; }
    public bool UpdateExisting { get; set; }
}
public class ImportVarexpMetersRequest
{
    public List<VarexpMeterImportItem> Meters { get; set; } = new();
    public bool SkipExisting { get; set; }
    public bool UpdateExisting { get; set; }
    public bool CreateMissingParents { get; set; }
}

public class VarexpMeterImportItem
{
    public string MeterName { get; set; } = "";
    public string? Unit { get; set; }
    public string Type { get; set; } = "Main";
    public string? ParentMeterId { get; set; }
    public bool Active { get; set; } = true;
}
public class BrowseVariablesRequest
{
    public string ConnectionId { get; set; } = "";
    public int MaxVariables { get; set; } = 100000;
    public string? BranchFilter { get; set; }
    public string VariableType { get; set; } = "Any";
    public int Depth { get; set; } = 0;
    public bool IncludeSystemVariables { get; set; } = false;
    public string? StartDate { get; set; }
    public string? EndDate { get; set; }
}

public class PrintWebServiceMetersRequest
{
    public string ConnectionId { get; set; } = "";
    public string ConnectionName { get; set; } = "";
    public List<WebServiceVariableItem> SelectedVariables { get; set; } = new();
    public string? StartDate { get; set; }
    public string? EndDate { get; set; }
}

public class WebServiceVariableItem
{
    public string VariableName { get; set; } = "";
    public string Unit { get; set; } = "";
    public string Type { get; set; } = "main";
    public string ParentMeterId { get; set; } = "";
    public bool Active { get; set; } = true;
    public string VariableType { get; set; } = "";
    public bool IsReadOnly { get; set; } = false;
    public bool IsSelected { get; set; } = true;
}

public class ImportWebServiceMetersRequest
{
    public List<WebServiceVariableItem> Variables { get; set; } = new();
    public bool SkipExisting { get; set; }
    public bool UpdateExisting { get; set; }
    public string? StartDate { get; set; }
    public string? EndDate { get; set; }
    public string? ConnectionId { get; set; }
}
public class PrintMetersRequest
{
    public string TableName { get; set; }
    public List<string> SelectedMeterNames { get; set; }
    public List<string> SelectedMeterTypes { get; set; }
    public List<string> SelectedMeterUnits { get; set; }
}