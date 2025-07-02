using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace PoWorks_Rework.Services
{
    public class VariableBrowseParsingService
    {
        private readonly ILogger<VariableBrowseParsingService> _logger;

        public VariableBrowseParsingService(ILogger<VariableBrowseParsingService> logger)
        {
            _logger = logger;
        }

        public class ParsedVariable
        {
            public string FullPath { get; set; } = "";
            public List<string> Branches { get; set; } = new List<string>();
            public string VariableName { get; set; } = "";
            public string VariableType { get; set; } = "";
            public bool IsReadOnly { get; set; }
            public bool IsLeaf { get; set; }
        }

        public class ParseResult
        {
            public bool Success { get; set; }
            public List<ParsedVariable> Variables { get; set; } = new List<ParsedVariable>();
            public int TotalCount { get; set; }
            public string ErrorMessage { get; set; } = "";
        }

        /// <summary>
        /// Parse PCVue browse variables response and extract variable paths
        /// </summary>
        public ParseResult ParseBrowseVariablesResponse(object responseData)
        {
            var result = new ParseResult();

            try
            {
                // Convert response to JsonElement for parsing
                var jsonString = JsonSerializer.Serialize(responseData);
                var jsonDoc = JsonDocument.Parse(jsonString);
                var root = jsonDoc.RootElement;

                // Check if variableCollections exists
                if (!root.TryGetProperty("variableCollections", out var collectionsElement))
                {
                    result.ErrorMessage = "Response missing 'variableCollections' property";
                    return result;
                }

                // Parse each variable in the collection
                foreach (var variable in collectionsElement.EnumerateArray())
                {
                    var parsedVar = new ParsedVariable();

                    // Extract branches array
                    if (variable.TryGetProperty("branches", out var branchesElement))
                    {
                        foreach (var branch in branchesElement.EnumerateArray())
                        {
                            parsedVar.Branches.Add(branch.GetString() ?? "");
                        }
                    }

                    // Extract variable name
                    if (variable.TryGetProperty("VariableName", out var varNameElement))
                    {
                        parsedVar.VariableName = varNameElement.GetString() ?? "";
                    }

                    // Extract other properties
                    if (variable.TryGetProperty("variableType", out var typeElement))
                    {
                        parsedVar.VariableType = typeElement.GetString() ?? "";
                    }

                    if (variable.TryGetProperty("IsReadOnly", out var readOnlyElement))
                    {
                        parsedVar.IsReadOnly = readOnlyElement.GetBoolean();
                    }

                    if (variable.TryGetProperty("IsLeaf", out var leafElement))
                    {
                        parsedVar.IsLeaf = leafElement.GetBoolean();
                    }

                    // Build full path: branches joined with dots + variable name
                    if (parsedVar.Branches.Any() && !string.IsNullOrEmpty(parsedVar.VariableName))
                    {
                        parsedVar.FullPath = string.Join(".", parsedVar.Branches) + "." + parsedVar.VariableName;
                    }
                    else if (!string.IsNullOrEmpty(parsedVar.VariableName))
                    {
                        parsedVar.FullPath = parsedVar.VariableName;
                    }

                    // Only add variables with valid paths
                    if (!string.IsNullOrEmpty(parsedVar.FullPath))
                    {
                        result.Variables.Add(parsedVar);
                    }
                }

                result.TotalCount = result.Variables.Count;
                result.Success = true;

                _logger.LogInformation($"Successfully parsed {result.TotalCount} variables from PCVue response");
            }
            catch (Exception ex)
            {
                result.ErrorMessage = $"Error parsing response: {ex.Message}";
                _logger.LogError(ex, "Error parsing PCVue browse variables response");
            }

            return result;
        }

        /// <summary>
        /// Print parsed variables to console in structured format
        /// </summary>
        public void PrintParsedVariablesToConsole(ParseResult parseResult, string connectionInfo)
        {
            Console.WriteLine("\n=====================================================");
            Console.WriteLine("PCVue VARIABLES BROWSE - PARSED RESULTS");
            Console.WriteLine("=====================================================");
            Console.WriteLine($"Connection: {connectionInfo}");
            Console.WriteLine($"Total Variables Found: {parseResult.TotalCount:N0}");
            Console.WriteLine($"Parsing Status: {(parseResult.Success ? "SUCCESS" : "FAILED")}");

            if (!parseResult.Success)
            {
                Console.WriteLine($"Error: {parseResult.ErrorMessage}");
                Console.WriteLine("=====================================================\n");
                return;
            }

            if (parseResult.Variables.Any())
            {
                Console.WriteLine("\nPARSED VARIABLE PATHS:");
                Console.WriteLine("----------------------");

                for (int i = 0; i < parseResult.Variables.Count; i++)
                {
                    var variable = parseResult.Variables[i];
                    Console.WriteLine($"{i + 1}. {variable.FullPath}");

                    if (variable.Branches.Any())
                    {
                        Console.WriteLine($"   ├─ Branches: {string.Join(" → ", variable.Branches)}");
                    }
                    Console.WriteLine($"   ├─ Variable: {variable.VariableName}");
                    Console.WriteLine($"   └─ Type: {variable.VariableType}, ReadOnly: {variable.IsReadOnly}");

                    // Add spacing every 5 items for readability
                    if ((i + 1) % 5 == 0 && i < parseResult.Variables.Count - 1)
                    {
                        Console.WriteLine();
                    }
                }

                // Summary by type
                var typeSummary = parseResult.Variables
                    .GroupBy(v => v.VariableType)
                    .OrderByDescending(g => g.Count())
                    .ToDictionary(g => g.Key, g => g.Count());

                Console.WriteLine("\nSUMMARY BY TYPE:");
                foreach (var type in typeSummary)
                {
                    Console.WriteLine($"- {type.Key} variables: {type.Value:N0}");
                }
            }
            else
            {
                Console.WriteLine("\nNo variables found in response.");
            }

            Console.WriteLine("=====================================================\n");
        }
    }
}