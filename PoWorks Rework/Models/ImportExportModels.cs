using static PoWorks_Rework.Controllers.ImportController;
namespace PoWorks_Rework.Models
{
    /// <summary>
    /// View model for the import/export page.
    /// </summary>
    public class ImportExportViewModel
    {
        /// <summary>
        /// The list of available HDS tables.
        /// </summary>
        public List<string> HdsTables { get; set; } = new List<string>();

        /// <summary>
        /// The currently selected HDS table.
        /// </summary>
        public string SelectedTable { get; set; }

        /// <summary>
        /// The start date filter for imports.
        /// </summary>
        public string StartDate { get; set; }

        /// <summary>
        /// The end date filter for imports.
        /// </summary>
        public string EndDate { get; set; }

        /// <summary>
        /// The maximum number of records to import.
        /// </summary>
        public int Limit { get; set; } = 1000;

        /// <summary>
        /// The uploaded VAREXP.DAT file.
        /// </summary>
        public IFormFile VarexpFile { get; set; }

        /// <summary>
        /// The records parsed from the VAREXP file.
        /// </summary>
        public List<string[]> VarexpRecords { get; set; } = new List<string[]>();
    }
}


namespace PoWorks_Rework.Models
{
    /// <summary>
    /// Request model for processing trends data from a web service connection.
    /// </summary>
    public class ProcessTrendsRequest
    {
        /// <summary>
        /// The ID of the web service connection to use.
        /// </summary>
        public string ConnectionId { get; set; } = "";

        /// <summary>
        /// The list of variable names to retrieve trends for.
        /// </summary>
        public List<string> VariableNames { get; set; } = new();

        /// <summary>
        /// The start of the trends data range.
        /// </summary>
        public DateTime StartDate { get; set; }

        /// <summary>
        /// The end of the trends data range.
        /// </summary>
        public DateTime EndDate { get; set; }

        /// <summary>
        /// The options for the trends request.
        /// </summary>
        public TrendsOptions Options { get; set; } = new();
    }

    /// <summary>
    /// Options that configure a trends data request.
    /// </summary>
    public class TrendsOptions
    {
        /// <summary>
        /// The maximum number of data elements to return.
        /// </summary>
        public int ElementMaxNumber { get; set; } = 100000;

        /// <summary>
        /// The aggregate function to apply to the data.
        /// </summary>
        public int AggregateFunction { get; set; } = 0; 

        /// <summary>
        /// The first aggregate parameter.
        /// </summary>
        public int AggregateParam1 { get; set; } = 0;

        /// <summary>
        /// The properties to include in the response.
        /// </summary>
        public List<string> Properties { get; set; } = new() { "VariableName", "Description", "StandardLabel" };

        /// <summary>
        /// Whether to include the start bound in the results.
        /// </summary>
        public bool IncludeStartBound { get; set; } = false; 

        /// <summary>
        /// Whether to include the end bound in the results.
        /// </summary>
        public bool IncludeEndBound { get; set; } = false;   
    }

    /// <summary>
    /// Request model for creating a trends data request in PCVue.
    /// </summary>
    public class TrendCreateRequest
    {
        /// <summary>
        /// The variable name to request trends for.
        /// </summary>
        public string VariableName { get; set; } = "";

        /// <summary>
        /// The maximum number of data elements to return.
        /// </summary>
        public int ElementMaxNumber { get; set; } = 100000;

        /// <summary>
        /// The aggregate function to apply to the data.
        /// </summary>
        public int AggregateFunction { get; set; } = 0; 

        /// <summary>
        /// The first aggregate parameter.
        /// </summary>
        public int AggregateParam1 { get; set; } = 0;

        /// <summary>
        /// The properties to include in the response.
        /// </summary>
        public List<string> Properties { get; set; } = new() { "VariableName", "Description", "StandardLabel" };

        /// <summary>
        /// The context for the request.
        /// </summary>
        public string Context { get; set; } = "";

        /// <summary>
        /// Whether to include the start bound in the results.
        /// </summary>
        public bool IncludeStartBound { get; set; } = false; 

        /// <summary>
        /// Whether to include the end bound in the results.
        /// </summary>
        public bool IncludeEndBound { get; set; } = false;   
    }

    /// <summary>
    /// Response model for processed trends data.
    /// </summary>
    public class ProcessTrendsResponse
    {
        /// <summary>
        /// Whether the processing succeeded.
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// The error message if processing failed.
        /// </summary>
        public string? ErrorMessage { get; set; }

        /// <summary>
        /// The per-variable trends results.
        /// </summary>
        public List<VariableTrendsResult> Results { get; set; } = new();

        /// <summary>
        /// A summary of the trends processing.
        /// </summary>
        public TrendsSummary Summary { get; set; } = new();

        /// <summary>
        /// The timestamp when processing completed.
        /// </summary>
        public DateTime ProcessedAt { get; set; } = DateTime.UtcNow;
    }

    /// <summary>
    /// Represents the trends result for a single variable.
    /// </summary>
    public class VariableTrendsResult
    {
        /// <summary>
        /// The variable name.
        /// </summary>
        public string VariableName { get; set; } = "";

        /// <summary>
        /// Whether the retrieval succeeded.
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// The error message if retrieval failed.
        /// </summary>
        public string? ErrorMessage { get; set; }

        /// <summary>
        /// The request ID used for the trends query.
        /// </summary>
        public string? RequestId { get; set; }

        /// <summary>
        /// The retrieved trend data points.
        /// </summary>
        public List<TrendDataPoint> TrendData { get; set; } = new();

        /// <summary>
        /// Whether the maximum element count was exceeded.
        /// </summary>
        public bool MaxNumberExceeded { get; set; }

        /// <summary>
        /// The number of data points retrieved.
        /// </summary>
        public int DataPointsCount { get; set; }

        /// <summary>
        /// The timestamp of the first data point.
        /// </summary>
        public DateTime? FirstTimestamp { get; set; }

        /// <summary>
        /// The timestamp of the last data point.
        /// </summary>
        public DateTime? LastTimestamp { get; set; }
    }

    /// <summary>
    /// Represents a single trend data point.
    /// </summary>
    public class TrendDataPoint
    {
        /// <summary>
        /// The value of the data point.
        /// </summary>
        public double Value { get; set; }

        /// <summary>
        /// The timestamp of the data point as a string.
        /// </summary>
        public string Timestamp { get; set; } = "";

        /// <summary>
        /// The quality of the data point.
        /// </summary>
        public string Quality { get; set; } = "";

        /// <summary>
        /// The numeric quality value.
        /// </summary>
        public int QualityValue { get; set; }

        /// <summary>
        /// Additional properties associated with the data point.
        /// </summary>
        public object? Properties { get; set; }

        /// <summary>
        /// The variable name associated with the data point.
        /// </summary>
        public string? Variable { get; set; }

        /// <summary>
        /// The parsed timestamp value, or null if parsing fails.
        /// </summary>
        public DateTime? TimestampParsed
        {
            get
            {
                if (DateTime.TryParse(Timestamp, out var result))
                    return result;
                return null;
            }
        }

        /// <summary>
        /// Whether the data point has good quality.
        /// </summary>
        public bool IsGoodQuality => Quality.Equals("Good", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Summary statistics for a trends processing operation.
    /// </summary>
    public class TrendsSummary
    {
        /// <summary>
        /// The total number of variables processed.
        /// </summary>
        public int TotalVariables { get; set; }

        /// <summary>
        /// The number of variables successfully processed.
        /// </summary>
        public int SuccessfulVariables { get; set; }

        /// <summary>
        /// The number of variables that failed.
        /// </summary>
        public int FailedVariables { get; set; }

        /// <summary>
        /// The total number of data points retrieved.
        /// </summary>
        public int TotalDataPoints { get; set; }

        /// <summary>
        /// The overall start time of the processed data.
        /// </summary>
        public DateTime? OverallStartTime { get; set; }

        /// <summary>
        /// The overall end time of the processed data.
        /// </summary>
        public DateTime? OverallEndTime { get; set; }

        /// <summary>
        /// The total processing duration.
        /// </summary>
        public TimeSpan ProcessingDuration { get; set; }

        /// <summary>
        /// The success rate as a percentage.
        /// </summary>
        public double SuccessRate => TotalVariables > 0 ? (double)SuccessfulVariables / TotalVariables * 100 : 0;
    }

    /// <summary>
    /// Request model for importing web service variables with their trends data.
    /// </summary>
    public class ImportWebServiceVariablesWithTrendsRequest
    {
        /// <summary>
        /// The variables to import.
        /// </summary>
        public List<WebServiceVariableWithTrends> Variables { get; set; } = new();

        /// <summary>
        /// Whether to skip variables that already exist.
        /// </summary>
        public bool SkipExisting { get; set; }

        /// <summary>
        /// Whether to update variables that already exist.
        /// </summary>
        public bool UpdateExisting { get; set; }

        /// <summary>
        /// Whether trends data should be imported.
        /// </summary>
        public bool ImportTrendsData { get; set; } = true;

        /// <summary>
        /// The start date for trends data retrieval.
        /// </summary>
        public DateTime? TrendsStartDate { get; set; }

        /// <summary>
        /// The end date for trends data retrieval.
        /// </summary>
        public DateTime? TrendsEndDate { get; set; }

        /// <summary>
        /// The web service connection ID to use.
        /// </summary>
        public string ConnectionId { get; set; } = "";
    }

    /// <summary>
    /// Represents a web service variable with optional trends data.
    /// </summary>
    public class WebServiceVariableWithTrends : WebServiceVariableItem
    {
        /// <summary>
        /// The trends data for the variable.
        /// </summary>
        public List<TrendDataPoint> TrendsData { get; set; } = new();

        /// <summary>
        /// Whether trends data is available for this variable.
        /// </summary>
        public bool TrendsDataAvailable { get; set; }

        /// <summary>
        /// The error message from trends retrieval, if any.
        /// </summary>
        public string? TrendsErrorMessage { get; set; }

        /// <summary>
        /// The number of trends data points.
        /// </summary>
        public int TrendsDataPointsCount { get; set; }

        /// <summary>
        /// The start date of the retrieved trends data.
        /// </summary>
        public DateTime? TrendsStartDate { get; set; }

        /// <summary>
        /// The end date of the retrieved trends data.
        /// </summary>
        public DateTime? TrendsEndDate { get; set; }
    }

    /// <summary>
    /// Response model for importing variables with trends data.
    /// </summary>
    public class ImportVariablesWithTrendsResponse
    {
        /// <summary>
        /// Whether the import succeeded.
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// The error message if the import failed.
        /// </summary>
        public string? ErrorMessage { get; set; }

        /// <summary>
        /// Summary of the variable import.
        /// </summary>
        public ImportSummary ImportSummary { get; set; } = new();

        /// <summary>
        /// Summary of the trends import.
        /// </summary>
        public TrendsSummary TrendsSummary { get; set; } = new();

        /// <summary>
        /// The per-variable import results.
        /// </summary>
        public List<VariableImportResult> Results { get; set; } = new();
    }

    /// <summary>
    /// Summary of a variable import operation.
    /// </summary>
    public class ImportSummary
    {
        /// <summary>
        /// The total number of variables processed.
        /// </summary>
        public int TotalVariables { get; set; }

        /// <summary>
        /// The number of variables imported.
        /// </summary>
        public int ImportedVariables { get; set; }

        /// <summary>
        /// The number of variables skipped.
        /// </summary>
        public int SkippedVariables { get; set; }

        /// <summary>
        /// The number of variables updated.
        /// </summary>
        public int UpdatedVariables { get; set; }

        /// <summary>
        /// The number of variables that failed.
        /// </summary>
        public int FailedVariables { get; set; }

        /// <summary>
        /// The list of error messages.
        /// </summary>
        public List<string> Errors { get; set; } = new();
    }

    /// <summary>
    /// Represents the import result for a single variable.
    /// </summary>
    public class VariableImportResult
    {
        /// <summary>
        /// The variable name.
        /// </summary>
        public string VariableName { get; set; } = "";

        /// <summary>
        /// Whether the variable was imported successfully.
        /// </summary>
        public bool ImportSuccess { get; set; }

        /// <summary>
        /// Whether the trends data was imported successfully.
        /// </summary>
        public bool TrendsSuccess { get; set; }

        /// <summary>
        /// The error message from the variable import, if any.
        /// </summary>
        public string? ImportErrorMessage { get; set; }

        /// <summary>
        /// The error message from the trends import, if any.
        /// </summary>
        public string? TrendsErrorMessage { get; set; }

        /// <summary>
        /// The action taken (e.g. imported, skipped, updated).
        /// </summary>
        public string Action { get; set; } = ""; 

        /// <summary>
        /// The ID of the created or updated meter.
        /// </summary>
        public int? MeterId { get; set; }

        /// <summary>
        /// The number of trends data points imported.
        /// </summary>
        public int TrendsDataPointsImported { get; set; }
    }

    /// <summary>
    /// Result of creating a trends data request.
    /// </summary>
    public class TrendRequestResult
    {
        /// <summary>
        /// Whether the request creation succeeded.
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// The request ID for retrieving the data.
        /// </summary>
        public string? RequestId { get; set; }

        /// <summary>
        /// The variable name the request was for.
        /// </summary>
        public string? VariableName { get; set; }

        /// <summary>
        /// The error message if the request failed.
        /// </summary>
        public string? ErrorMessage { get; set; }
    }

    /// <summary>
    /// Result of retrieving trends data for a request.
    /// </summary>
    public class TrendDataResult
    {
        /// <summary>
        /// Whether the retrieval succeeded.
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// The request ID the data was retrieved for.
        /// </summary>
        public string? RequestId { get; set; }

        /// <summary>
        /// The retrieved trend data values.
        /// </summary>
        public List<TrendDataPoint> Values { get; set; } = new();

        /// <summary>
        /// Whether the maximum element count was exceeded.
        /// </summary>
        public bool MaxNumberExceeded { get; set; }

        /// <summary>
        /// The error message if the retrieval failed.
        /// </summary>
        public string? ErrorMessage { get; set; }
    }

    /// <summary>
    /// Represents the trends result for a single variable.
    /// </summary>
    public class VariableTrendResult
    {
        /// <summary>
        /// The variable name.
        /// </summary>
        public string VariableName { get; set; } = "";

        /// <summary>
        /// Whether the processing succeeded.
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// The request ID used for the query.
        /// </summary>
        public string? RequestId { get; set; }

        /// <summary>
        /// The retrieved trend data.
        /// </summary>
        public List<TrendDataPoint> TrendData { get; set; } = new();

        /// <summary>
        /// Whether the maximum element count was exceeded.
        /// </summary>
        public bool MaxNumberExceeded { get; set; }

        /// <summary>
        /// The error message if processing failed.
        /// </summary>
        public string? ErrorMessage { get; set; }
    }

    /// <summary>
    /// Represents the API response from a trends data query.
    /// </summary>
    public class TrendApiResponse
    {
        /// <summary>
        /// The retrieved trend data values.
        /// </summary>
        public List<TrendDataPoint> Values { get; set; } = new();

        /// <summary>
        /// Whether the maximum element count was exceeded.
        /// </summary>
        public bool MaxNumberExceeded { get; set; }
    }
}

/// <summary>
/// Request model for importing meter readings from a SQL Server table.
/// </summary>
public class ImportReadingsRequest
{
    /// <summary>
    /// The SQL Server table name to import readings from.
    /// </summary>
    public string TableName { get; set; }

    /// <summary>
    /// The list of meter names to import readings for.
    /// </summary>
    public List<string> MeterNames { get; set; }

    /// <summary>
    /// Optional start date filter for readings.
    /// </summary>
    public DateTime? StartDate { get; set; }

    /// <summary>
    /// Optional end date filter for readings.
    /// </summary>
    public DateTime? EndDate { get; set; }

    /// <summary>
    /// Optional limit on the number of readings per meter.
    /// </summary>
    public int? Limit { get; set; }
}

/// <summary>
/// Request model for printing selected HDS meters.
/// </summary>
public class PrintHDSMetersRequest
{
    /// <summary>
    /// The HDS table name.
    /// </summary>
    public string TableName { get; set; } = "";

    /// <summary>
    /// The SQL Server connection ID.
    /// </summary>
    public string ConnectionId { get; set; } = "";

    /// <summary>
    /// The list of meters to print.
    /// </summary>
    public List<HDSMeterPrintItem> SelectedMeters { get; set; } = new();

    /// <summary>
    /// Whether historical readings should be imported along with the meters.
    /// </summary>
    public bool ImportHistoricalReadings { get; set; } = false;

    /// <summary>
    /// Optional start date for historical reading imports.
    /// </summary>
    public DateTime? StartDate { get; set; }

    /// <summary>
    /// Optional end date for historical reading imports.
    /// </summary>
    public DateTime? EndDate { get; set; }
}

/// <summary>
/// Represents a meter item from an HDS table for import.
/// </summary>
public class HDSMeterPrintItem
{
    /// <summary>
    /// The meter name in the HDS table.
    /// </summary>
    public string HdsMeterName { get; set; } = "";

    /// <summary>
    /// The meter's unit of measurement.
    /// </summary>
    public string Unit { get; set; } = "";

    /// <summary>
    /// The meter type (main or sub).
    /// </summary>
    public string Type { get; set; } = "main";

    /// <summary>
    /// The parent meter ID if this is a sub meter.
    /// </summary>
    public string ParentMeterId { get; set; } = "";

    /// <summary>
    /// Whether the meter is active.
    /// </summary>
    public bool Active { get; set; } = true;

    /// <summary>
    /// The last known reading value.
    /// </summary>
    public string LastReading { get; set; } = "";

    /// <summary>
    /// Whether the meter is selected for import.
    /// </summary>
    public bool IsSelected { get; set; } = true;
}

/// <summary>
/// Request model for importing meters.
/// </summary>
public class ImportMetersRequest
{
    /// <summary>
    /// The meters to import.
    /// </summary>
    public List<HDSMeterPrintItem> Meters { get; set; }

    /// <summary>
    /// Whether to skip meters that already exist.
    /// </summary>
    public bool SkipExisting { get; set; }

    /// <summary>
    /// Whether to update meters that already exist.
    /// </summary>
    public bool UpdateExisting { get; set; }
}

/// <summary>
/// Request model for importing meters from a VAREXP file.
/// </summary>
public class ImportVarexpMetersRequest
{
    /// <summary>
    /// The meters to import.
    /// </summary>
    public List<VarexpMeterImportItem> Meters { get; set; } = new();

    /// <summary>
    /// Whether to skip meters that already exist.
    /// </summary>
    public bool SkipExisting { get; set; }

    /// <summary>
    /// Whether to update meters that already exist.
    /// </summary>
    public bool UpdateExisting { get; set; }

    /// <summary>
    /// Whether to allow creating meters without a valid parent.
    /// </summary>
    public bool CreateMissingParents { get; set; }
}

/// <summary>
/// Represents a meter item parsed from a VAREXP.DAT file.
/// </summary>
public class VarexpMeterImportItem
{
    /// <summary>
    /// The combined dotted meter name.
    /// </summary>
    public string MeterName { get; set; } = "";

    /// <summary>
    /// The meter's unit of measurement.
    /// </summary>
    public string? Unit { get; set; }

    /// <summary>
    /// The meter type (Main or Sub).
    /// </summary>
    public string Type { get; set; } = "Main";

    /// <summary>
    /// The parent meter ID if this is a sub meter.
    /// </summary>
    public string? ParentMeterId { get; set; }

    /// <summary>
    /// Whether the meter is active.
    /// </summary>
    public bool Active { get; set; } = true;
}

/// <summary>
/// Request model for browsing variables from a PCVue web service.
/// </summary>
public class BrowseVariablesRequest
{
    /// <summary>
    /// The web service connection ID.
    /// </summary>
    public string ConnectionId { get; set; } = "";

    /// <summary>
    /// The maximum number of variables to return.
    /// </summary>
    public int MaxVariables { get; set; } = 100000;

    /// <summary>
    /// An optional branch filter for the variable tree.
    /// </summary>
    public string? BranchFilter { get; set; }

    /// <summary>
    /// The variable type filter (e.g. Any).
    /// </summary>
    public string VariableType { get; set; } = "Any";

    /// <summary>
    /// The tree depth to browse.
    /// </summary>
    public int Depth { get; set; } = 0;

    /// <summary>
    /// Whether to include system variables.
    /// </summary>
    public bool IncludeSystemVariables { get; set; } = false;

    /// <summary>
    /// Optional start date filter.
    /// </summary>
    public string? StartDate { get; set; }

    /// <summary>
    /// Optional end date filter.
    /// </summary>
    public string? EndDate { get; set; }
}

/// <summary>
/// Request model for printing selected web service meters.
/// </summary>
public class PrintWebServiceMetersRequest
{
    /// <summary>
    /// The web service connection ID.
    /// </summary>
    public string ConnectionId { get; set; } = "";

    /// <summary>
    /// The display name of the web service connection.
    /// </summary>
    public string ConnectionName { get; set; } = "";

    /// <summary>
    /// The selected variables to print.
    /// </summary>
    public List<WebServiceVariableItem> SelectedVariables { get; set; } = new();

    /// <summary>
    /// Optional start date filter.
    /// </summary>
    public string? StartDate { get; set; }

    /// <summary>
    /// Optional end date filter.
    /// </summary>
    public string? EndDate { get; set; }
}

/// <summary>
/// Represents a variable from a PCVue web service for import.
/// </summary>
public class WebServiceVariableItem
{
    /// <summary>
    /// The variable name.
    /// </summary>
    public string VariableName { get; set; } = "";

    /// <summary>
    /// The unit of measurement.
    /// </summary>
    public string Unit { get; set; } = "";

    /// <summary>
    /// The meter type (main or sub).
    /// </summary>
    public string Type { get; set; } = "main";

    /// <summary>
    /// The parent meter ID if this is a sub meter.
    /// </summary>
    public string ParentMeterId { get; set; } = "";

    /// <summary>
    /// Whether the meter is active.
    /// </summary>
    public bool Active { get; set; } = true;

    /// <summary>
    /// The variable type as reported by PCVue.
    /// </summary>
    public string VariableType { get; set; } = "";

    /// <summary>
    /// Whether the variable is read-only.
    /// </summary>
    public bool IsReadOnly { get; set; } = false;

    /// <summary>
    /// Whether the variable is selected for import.
    /// </summary>
    public bool IsSelected { get; set; } = true;
}

/// <summary>
/// Request model for importing web service meters.
/// </summary>
public class ImportWebServiceMetersRequest
{
    /// <summary>
    /// The variables to import.
    /// </summary>
    public List<WebServiceVariableItem> Variables { get; set; } = new();

    /// <summary>
    /// Whether to skip variables that already exist.
    /// </summary>
    public bool SkipExisting { get; set; }

    /// <summary>
    /// Whether to update variables that already exist.
    /// </summary>
    public bool UpdateExisting { get; set; }

    /// <summary>
    /// Optional start date filter.
    /// </summary>
    public string? StartDate { get; set; }

    /// <summary>
    /// Optional end date filter.
    /// </summary>
    public string? EndDate { get; set; }

    /// <summary>
    /// The web service connection ID to use.
    /// </summary>
    public string? ConnectionId { get; set; }
}

/// <summary>
/// Request model for printing selected meters.
/// </summary>
public class PrintMetersRequest
{
    /// <summary>
    /// The table name.
    /// </summary>
    public string TableName { get; set; }

    /// <summary>
    /// The selected meter names.
    /// </summary>
    public List<string> SelectedMeterNames { get; set; }

    /// <summary>
    /// The selected meter types.
    /// </summary>
    public List<string> SelectedMeterTypes { get; set; }

    /// <summary>
    /// The selected meter units.
    /// </summary>
    public List<string> SelectedMeterUnits { get; set; }
}