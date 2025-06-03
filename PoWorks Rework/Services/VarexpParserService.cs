using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.VisualBasic.FileIO;

namespace PoWorks_Rework.Services
{
    /// <summary>
    /// Exception thrown when a VAREXP.DAT parsing error occurs.
    /// </summary>
    public class VarexpParseException : Exception
    {
        public int LineNumber { get; }

        public VarexpParseException(string message, int lineNumber, Exception innerException = null)
            : base($"Error parsing VAREXP.DAT at line {lineNumber}: {message}", innerException)
        {
            LineNumber = lineNumber;
        }
    }

    /// <summary>
    /// Configuration enum to represent the two possible VAREXP configurations
    /// </summary>
    public enum VarexpConfiguration
    {
        /// <summary>
        /// Configuration with n1-n6 for names + n7 for 6 variables (Source at index != 22)
        /// </summary>
        SixPlusOne,

        /// <summary>
        /// Configuration with n1-n12 for names (Source at index 22)
        /// </summary>
        Twelve
    }

    /// <summary>
    /// Service to parse PCVue VAREXP.DAT configuration files (comma-delimited ASCII with optional quoted fields).
    /// </summary>
    public class VarexpParserService
    {
        private readonly ILogger<VarexpParserService> _logger;

        public VarexpParserService(ILogger<VarexpParserService> logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// Parses a VAREXP.DAT file uploaded via an ASP.NET Core IFormFile.
        /// Uses TextFieldParser to handle quoted fields correctly.
        /// Combines name columns (n1-n6 or n1-n12) into a single dotted name.
        /// </summary>
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

                    // Skip empty or comment lines
                    if (fields == null || fields.Length == 0)
                        continue;

                    // Check if this is the "Class" row to find the Source column and determine configuration
                    if (fields.Length > 0 && fields[0]?.Trim().Equals("Class", StringComparison.OrdinalIgnoreCase) == true)
                    {
                        sourceColumnIndex = FindSourceColumn(fields);
                        configuration = DetermineConfiguration(sourceColumnIndex);

                        LogConfigurationInfo(sourceColumnIndex, configuration, fields);

                        // Process the Class row with combined name column
                        var processedFields = ProcessClassRow(fields, configuration.Value);
                        records.Add(processedFields);
                    }
                    else
                    {
                        // Process regular data rows (combine name columns if configuration is known)
                        if (configuration.HasValue)
                        {
                            var processedFields = ProcessDataRow(fields, configuration.Value);

                            // Apply filtering to processed fields
                            if (!ShouldFilterRow(processedFields))
                            {
                                records.Add(processedFields);
                            }
                            else
                            {
                                // Log filtered rows for debugging
                                _logger.LogDebug($"Filtered out row: [{string.Join(", ", processedFields)}]");
                            }
                        }
                        else
                        {
                            // Configuration not determined yet, add as-is
                            records.Add(fields);
                        }
                    }
                }

                // Log final summary
                LogFinalSummary(sourceColumnIndex, configuration, records.Count);
            }
            catch (VarexpParseException)
            {
                throw; // already logged and wrapped
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error parsing VAREXP.DAT at line {LineNumber}", lineNumber);
                throw new VarexpParseException(ex.Message, lineNumber, ex);
            }

            return records;
        }

        /// <summary>
        /// Find the column index that contains "Source" in the given fields array.
        /// </summary>
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
        /// Determine the VAREXP configuration based on Source column position
        /// </summary>
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
        /// Log configuration information to both logger and console
        /// </summary>
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
        /// Process the Class row by combining name columns and adjusting headers
        /// </summary>
        private string[] ProcessClassRow(string[] fields, VarexpConfiguration configuration)
        {
            var result = new List<string>();

            // Add the first field (Class)
            result.Add(fields[0]);

            // Add "CombinedName" instead of individual n1, n2, etc.
            result.Add("CombinedName");

            if (configuration == VarexpConfiguration.Twelve)
            {
                // Skip n1-n12 (indices 2-13), add everything after n12
                for (int i = 14; i < fields.Length; i++)
                {
                    result.Add(fields[i]);
                }
            }
            else // SixPlusOne
            {
                // Skip n1-n7 (indices 2-8), add everything after n7
                for (int i = 9; i < fields.Length; i++)
                {
                    result.Add(fields[i]);
                }
            }

            return result.ToArray();
        }

        /// <summary>
        /// Process data rows by combining name columns into a single dotted name
        /// </summary>
        private string[] ProcessDataRow(string[] fields, VarexpConfiguration configuration)
        {
            var result = new List<string>();

            // Add the first field (typically the record type like CMD, CHR, etc.)
            result.Add(fields[0]);

            // Combine name columns based on configuration
            string combinedName = CombineNameColumns(fields, configuration);
            result.Add(combinedName);

            if (configuration == VarexpConfiguration.Twelve)
            {
                // Skip n1-n12 (indices 2-13), add everything after n12
                for (int i = 14; i < fields.Length; i++)
                {
                    result.Add(fields[i]);
                }
            }
            else // SixPlusOne
            {
                // Skip n1-n7 (indices 2-8), add everything after n7
                for (int i = 9; i < fields.Length; i++)
                {
                    result.Add(fields[i]);
                }
            }

            return result.ToArray();
        }

        /// <summary>
        /// Combine name columns into a single dotted string based on configuration
        /// </summary>
        private string CombineNameColumns(string[] fields, VarexpConfiguration configuration)
        {
            var nameParts = new List<string>();

            if (configuration == VarexpConfiguration.Twelve)
            {
                // Combine n1-n12 (indices 2-13)
                for (int i = 2; i <= 13; i++)
                {
                    if (i < fields.Length && !string.IsNullOrWhiteSpace(fields[i]))
                    {
                        nameParts.Add(fields[i].Trim());
                    }
                }
            }
            else // SixPlusOne
            {
                // Combine n1-n6 (indices 2-7)
                for (int i = 2; i <= 7; i++)
                {
                    if (i < fields.Length && !string.IsNullOrWhiteSpace(fields[i]))
                    {
                        nameParts.Add(fields[i].Trim());
                    }
                }

                // Add n7 content if it exists and contains variables (index 8)
                if (fields.Length > 8 && !string.IsNullOrWhiteSpace(fields[8]))
                {
                    nameParts.Add(fields[8].Trim());
                }
            }

            return string.Join(".", nameParts);
        }

        /// <summary>
        /// Check if a processed row should be filtered out (system rows, etc.)
        /// </summary>
        private bool ShouldFilterRow(string[] processedFields)
        {
            if (processedFields == null || processedFields.Length < 2)
                return false;

            var firstField = processedFields[0]?.Trim() ?? "";
            var combinedName = processedFields[1]?.Trim().ToLowerInvariant() ?? "";

            // Filter out system rows (combined name starts with "system")
            if (combinedName.StartsWith("system", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // Filter out rows with excluded prefixes in the first field
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
        /// Log final parsing summary
        /// </summary>
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
        /// Convenience method to parse VAREXP.DAT from a file path.
        /// </summary>
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

                    // Check if this is the "Class" row to find the Source column
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