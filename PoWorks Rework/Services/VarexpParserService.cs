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
        /// </summary>
        public async Task<List<string[]>> ParseVarexpAsync(IFormFile file)
        {
            var records = new List<string[]>();
            int lineNumber = 0;

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

                    records.Add(fields);
                }
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
        /// Convenience method to parse VAREXP.DAT from a file path.
        /// </summary>
        public List<string[]> ParseVarexpFromPath(string filePath)
        {
            var records = new List<string[]>();
            int lineNumber = 0;

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
