using Microsoft.VisualBasic.FileIO;

namespace PoWorks_Rework.Services
{
    /// <summary>
    /// Exception thrown when an error occurs while parsing a VAREXP.DAT file.
    /// Includes the line number where the parsing error occurred.
    /// </summary>
    public class VarexpParseException : Exception
    {
        /// <summary>
        /// The line number in the file where the parsing error occurred.
        /// </summary>
        public int LineNumber { get; }

        /// <summary>
        /// Initializes the parsing exception with a message and line number.
        /// </summary>
        public VarexpParseException(string message, int lineNumber, Exception innerException = null)
            : base($"Error parsing VAREXP.DAT at line {lineNumber}: {message}", innerException)
        {
            LineNumber = lineNumber;
        }
    }

    /// <summary>
    /// Defines the supported VAREXP.DAT column configurations.
    /// </summary>
    public enum VarexpConfiguration
    {
        /// <summary>
        /// Configuration with 6 name columns plus 1 additional column (n1-n6 + n7).
        /// </summary>
        SixPlusOne,

        /// <summary>
        /// Configuration with 12 name columns (n1-n12).
        /// </summary>
        Twelve
    }

    /// <summary>
    /// Service for parsing VAREXP.DAT files containing meter configuration data.
    /// Detects the column configuration, combines name columns, and filters system rows.
    /// </summary>
    public class VarexpParserService
    {
        private readonly ILogger<VarexpParserService> _logger;

        /// <summary>
        /// Initializes the VAREXP parser service with a logger.
        /// </summary>
        public VarexpParserService(ILogger<VarexpParserService> logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// Parses an uploaded VAREXP.DAT file and returns the processed records.
        /// </summary>
        /// <param name="file">The uploaded VAREXP.DAT file.</param>
        /// <returns>A list of string arrays representing the parsed records.</returns>
        public async Task<List<string[]>> ParseVarexpAsync(IFormFile file)
        {
            var records = new List<string[]>();
            int lineNumber = 0;
            int? sourceColumnIndex = null;
            VarexpConfiguration? configuration = null;

            try
            {
                using var stream = file.OpenReadStream();
                using var reader = new StreamReader(stream);

                using var parser = new TextFieldParser(reader)
                {
                    TextFieldType = FieldType.Delimited,
                    Delimiters = new[] { "," },
                    HasFieldsEnclosedInQuotes = true
                };

                while (!parser.EndOfData)
                {
                    lineNumber++;
                    string[] fields;
                    try
                    {
                        fields = parser.ReadFields();
                    }
                    catch (MalformedLineException ex)
                    {
                        _logger.LogError(ex, "Malformed line {LineNumber} in VAREXP.DAT", lineNumber);
                        throw new VarexpParseException(ex.Message, lineNumber, ex);
                    }
                    if (fields == null || fields.Length == 0)
                        continue;
                    if (fields.Length > 0 && fields[0]?.Trim().Equals("Class", StringComparison.OrdinalIgnoreCase) == true)
                    {
                        sourceColumnIndex = FindSourceColumn(fields);
                        configuration = DetermineConfiguration(sourceColumnIndex);

                        LogConfigurationInfo(sourceColumnIndex, configuration, fields);
                        var processedFields = ProcessClassRow(fields, configuration.Value);
                        records.Add(processedFields);
                    }
                    else
                    {
                        if (configuration.HasValue)
                        {
                            var processedFields = ProcessDataRow(fields, configuration.Value);
                            if (!ShouldFilterRow(processedFields))
                            {
                                records.Add(processedFields);
                            }
                            else
                            {
                                _logger.LogDebug($"Filtered out row: [{string.Join(", ", processedFields)}]");
                            }
                        }
                        else
                        {
                            records.Add(fields);
                        }
                    }
                }
                LogFinalSummary(sourceColumnIndex, configuration, records.Count);
            }
            catch (VarexpParseException)
            {
                throw; 
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error parsing VAREXP.DAT at line {LineNumber}", lineNumber);
                throw new VarexpParseException(ex.Message, lineNumber, ex);
            }

            return records;
        }
        /// <summary>
        /// Finds the index of the 'Source' column in the class header row.
        /// </summary>
        /// <param name="fields">The header row fields.</param>
        /// <returns>The column index, or null if not found.</returns>
        private int? FindSourceColumn(string[] fields)
        {
            for (int i = 0; i < fields.Length; i++)
            {
                if (fields[i]?.Trim().Equals("Source", StringComparison.OrdinalIgnoreCase) == true)
                {
                    return i;
                }
            }
            return null;
        }
        /// <summary>
        /// Determines the VAREXP configuration based on the source column index.
        /// </summary>
        /// <param name="sourceColumnIndex">The index of the source column.</param>
        /// <returns>The detected configuration.</returns>
        private VarexpConfiguration DetermineConfiguration(int? sourceColumnIndex)
        {
            if (sourceColumnIndex.HasValue && sourceColumnIndex.Value == 22)
            {
                return VarexpConfiguration.Twelve;
            }
            else
            {
                return VarexpConfiguration.SixPlusOne;
            }
        }
        /// <summary>
        /// Logs the detected VAREXP configuration details for debugging.
        /// </summary>
        /// <param name="sourceColumnIndex">The detected source column index.</param>
        /// <param name="configuration">The detected configuration.</param>
        /// <param name="fields">The class header row fields.</param>
        private void LogConfigurationInfo(int? sourceColumnIndex, VarexpConfiguration? configuration, string[] fields)
        {
            string configName = configuration == VarexpConfiguration.Twelve ? "12-column (n1-n12)" : "6+1-column (n1-n6 + n7)";

            _logger.LogInformation("=== VAREXP CONFIGURATION DETECTION ===");
            _logger.LogInformation($"Source column index: {sourceColumnIndex?.ToString() ?? "NOT FOUND"}");
            _logger.LogInformation($"Configuration: {configName}");
            _logger.LogInformation($"Class row content: [{string.Join(", ", fields)}]");
            _logger.LogInformation("======================================");

            Console.WriteLine("\n=== VAREXP CONFIGURATION DETECTION ===");
            Console.WriteLine($"Source column index: {sourceColumnIndex?.ToString() ?? "NOT FOUND"}");
            Console.WriteLine($"Configuration: {configName}");
            Console.WriteLine($"Class row content: [{string.Join(", ", fields)}]");
            Console.WriteLine("======================================\n");
        }
        /// <summary>
        /// Processes a class header row, keeping the class name and a CombinedName placeholder.
        /// </summary>
        /// <param name="fields">The class header row fields.</param>
        /// <param name="configuration">The detected configuration.</param>
        /// <returns>The processed row.</returns>
        private string[] ProcessClassRow(string[] fields, VarexpConfiguration configuration)
        {
            var result = new List<string>();
            result.Add(fields[0]);
            result.Add("CombinedName");

            if (configuration == VarexpConfiguration.Twelve)
            {
                for (int i = 14; i < fields.Length; i++)
                {
                    result.Add(fields[i]);
                }
            }
            else 
            {
                for (int i = 9; i < fields.Length; i++)
                {
                    result.Add(fields[i]);
                }
            }

            return result.ToArray();
        }
        /// <summary>
        /// Processes a data row by combining name columns and keeping relevant fields.
        /// </summary>
        /// <param name="fields">The data row fields.</param>
        /// <param name="configuration">The detected configuration.</param>
        /// <returns>The processed row.</returns>
        private string[] ProcessDataRow(string[] fields, VarexpConfiguration configuration)
        {
            var result = new List<string>();
            result.Add(fields[0]);
            string combinedName = CombineNameColumns(fields, configuration);
            result.Add(combinedName);

            if (configuration == VarexpConfiguration.Twelve)
            {
                for (int i = 14; i < fields.Length; i++)
                {
                    result.Add(fields[i]);
                }
            }
            else 
            {
                for (int i = 9; i < fields.Length; i++)
                {
                    result.Add(fields[i]);
                }
            }

            return result.ToArray();
        }
        /// <summary>
        /// Combines the name columns into a single dotted name.
        /// </summary>
        /// <param name="fields">The data row fields.</param>
        /// <param name="configuration">The detected configuration.</param>
        /// <returns>The combined dotted name.</returns>
        private string CombineNameColumns(string[] fields, VarexpConfiguration configuration)
        {
            var nameParts = new List<string>();

            if (configuration == VarexpConfiguration.Twelve)
            {
                for (int i = 2; i <= 13; i++)
                {
                    if (i < fields.Length && !string.IsNullOrWhiteSpace(fields[i]))
                    {
                        nameParts.Add(fields[i].Trim());
                    }
                }
            }
            else 
            {
                for (int i = 2; i <= 7; i++)
                {
                    if (i < fields.Length && !string.IsNullOrWhiteSpace(fields[i]))
                    {
                        nameParts.Add(fields[i].Trim());
                    }
                }
                if (fields.Length > 8 && !string.IsNullOrWhiteSpace(fields[8]))
                {
                    nameParts.Add(fields[8].Trim());
                }
            }

            return string.Join(".", nameParts);
        }
        /// <summary>
        /// Determines whether a processed row should be filtered out.
        /// Filters system rows and rows with excluded class prefixes.
        /// </summary>
        /// <param name="processedFields">The processed row fields.</param>
        /// <returns>True if the row should be filtered out.</returns>
        private bool ShouldFilterRow(string[] processedFields)
        {
            if (processedFields == null || processedFields.Length < 2)
                return false;

            var firstField = processedFields[0]?.Trim() ?? "";
            var combinedName = processedFields[1]?.Trim().ToLowerInvariant() ?? "";
            if (combinedName.StartsWith("system", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
            var excludedPrefixes = new[]
            {
                "DEFACTALABEL", "DEFALMALABEL", "DEFBITALABEL", "LNSLCA",
                "OPCCONF", "BACCONF", "M104CONF", "M61850CONF", "MDNP3CONF", "SNMPCONF"
            };

            if (excludedPrefixes.Any(prefix => firstField.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }

            return false;
        }
        /// <summary>
        /// Logs the final parsing summary with configuration and record count.
        /// </summary>
        /// <param name="sourceColumnIndex">The detected source column index.</param>
        /// <param name="configuration">The detected configuration.</param>
        /// <param name="recordCount">The total number of records parsed.</param>
        private void LogFinalSummary(int? sourceColumnIndex, VarexpConfiguration? configuration, int recordCount)
        {
            string configName = configuration == VarexpConfiguration.Twelve ? "12-column" : "6+1-column";

            if (sourceColumnIndex.HasValue && configuration.HasValue)
            {
                _logger.LogInformation($"VAREXP parsing completed. Configuration: {configName}, Source column: {sourceColumnIndex.Value}, Total records: {recordCount}");
                Console.WriteLine($"\n=== VAREXP PARSING COMPLETED ===");
                Console.WriteLine($"Configuration: {configName}");
                Console.WriteLine($"Source column: {sourceColumnIndex.Value}");
                Console.WriteLine($"Total records: {recordCount} (after filtering)");
                Console.WriteLine($"Name columns combined with dots");
                Console.WriteLine($"System rows and excluded prefixes filtered out");
                Console.WriteLine($"===============================\n");
            }
            else
            {
                _logger.LogWarning($"VAREXP parsing completed but configuration could not be determined. Total records: {recordCount}");
                Console.WriteLine($"\n=== VAREXP PARSING COMPLETED (WITH WARNINGS) ===");
                Console.WriteLine($"WARNING: Configuration could not be determined");
                Console.WriteLine($"Total records: {recordCount}");
                Console.WriteLine($"===============================================\n");
            }
        }
        /// <summary>
        /// Parses a VAREXP.DAT file from a file path and returns the raw records.
        /// </summary>
        /// <param name="filePath">The path to the VAREXP.DAT file.</param>
        /// <returns>A list of string arrays representing the parsed records.</returns>
        public List<string[]> ParseVarexpFromPath(string filePath)
        {
            var records = new List<string[]>();
            int lineNumber = 0;
            int? sourceColumnIndex = null;

            try
            {
                using var parser = new TextFieldParser(filePath)
                {
                    TextFieldType = FieldType.Delimited,
                    Delimiters = new[] { "," },
                    HasFieldsEnclosedInQuotes = true
                };

                while (!parser.EndOfData)
                {
                    lineNumber++;
                    string[] fields;
                    try
                    {
                        fields = parser.ReadFields();
                    }
                    catch (MalformedLineException ex)
                    {
                        _logger.LogError(ex, "Malformed line {LineNumber} in VAREXP.DAT at {FilePath}", lineNumber, filePath);
                        throw new VarexpParseException(ex.Message, lineNumber, ex);
                    }

                    if (fields == null || fields.Length == 0)
                        continue;
                    if (fields.Length > 0 && fields[0]?.Trim().Equals("Class", StringComparison.OrdinalIgnoreCase) == true)
                    {
                        sourceColumnIndex = FindSourceColumn(fields);
                        if (sourceColumnIndex.HasValue)
                        {
                            _logger.LogInformation($"Found 'Source' column at index: {sourceColumnIndex.Value} in file: {filePath}");
                            Console.WriteLine($"\n=== Source column found at index: {sourceColumnIndex.Value} in file: {filePath} ===\n");
                        }
                    }

                    records.Add(fields);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error parsing VAREXP.DAT from path {FilePath} at line {LineNumber}", filePath, lineNumber);
                throw new VarexpParseException(ex.Message, lineNumber, ex);
            }

            return records;
        }
    }
}