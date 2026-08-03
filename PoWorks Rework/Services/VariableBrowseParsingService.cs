using System.Text.Json;

namespace PoWorks_Rework.Services
{
    /// <summary>
    /// Service for parsing PCVue web service variable browse responses.
    /// Extracts variable paths, branches, types, and filters system variables.
    /// </summary>
    public class VariableBrowseParsingService
    {
        private readonly ILogger<VariableBrowseParsingService> _logger;

        /// <summary>
        /// Initializes the variable browse parsing service with a logger.
        /// </summary>
        public VariableBrowseParsingService(ILogger<VariableBrowseParsingService> logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// Represents a parsed variable from a PCVue browse response.
        /// </summary>
        public class ParsedVariable
        {
            /// <summary>
            /// The full dotted path of the variable including branches.
            /// </summary>
            public string FullPath { get; set; } = "";

            /// <summary>
            /// The list of branch names in the variable's path.
            /// </summary>
            public List<string> Branches { get; set; } = new List<string>();

            /// <summary>
            /// The variable name.
            /// </summary>
            public string VariableName { get; set; } = "";

            /// <summary>
            /// The variable type as reported by PCVue.
            /// </summary>
            public string VariableType { get; set; } = "";

            /// <summary>
            /// Whether the variable is read-only.
            /// </summary>
            public bool IsReadOnly { get; set; }

            /// <summary>
            /// Whether the variable is a leaf node.
            /// </summary>
            public bool IsLeaf { get; set; }
        }

        /// <summary>
        /// Represents the result of parsing a PCVue browse response.
        /// </summary>
        public class ParseResult
        {
            /// <summary>
            /// Whether the parsing succeeded.
            /// </summary>
            public bool Success { get; set; }

            /// <summary>
            /// The list of parsed variables.
            /// </summary>
            public List<ParsedVariable> Variables { get; set; } = new List<ParsedVariable>();

            /// <summary>
            /// The total number of parsed variables.
            /// </summary>
            public int TotalCount { get; set; }

            /// <summary>
            /// The error message if parsing failed.
            /// </summary>
            public string ErrorMessage { get; set; } = "";
        }

        /// <summary>
        /// Parses a PCVue browse variables response into a structured result.
        /// </summary>
        /// <param name="responseData">The raw response data to parse.</param>
        /// <param name="includeSystemVariables">Whether to include variables under the System branch.</param>
        /// <returns>A ParseResult with the parsed variables.</returns>
        public ParseResult ParseBrowseVariablesResponse(object responseData, bool includeSystemVariables = false)
        {
            var result = new ParseResult();

            try
            {
                var jsonString = JsonSerializer.Serialize(responseData);
                var jsonDoc = JsonDocument.Parse(jsonString);
                var root = jsonDoc.RootElement;
                if (!root.TryGetProperty("variableCollections", out var collectionsElement))
                {
                    result.ErrorMessage = "Response missing 'variableCollections' property";
                    return result;
                }
                foreach (var variable in collectionsElement.EnumerateArray())
                {
                    var parsedVar = new ParsedVariable();
                    if (variable.TryGetProperty("branches", out var branchesElement))
                    {
                        foreach (var branch in branchesElement.EnumerateArray())
                        {
                            parsedVar.Branches.Add(branch.GetString() ?? "");
                        }
                    }
                    if (variable.TryGetProperty("VariableName", out var varNameElement))
                    {
                        parsedVar.VariableName = varNameElement.GetString() ?? "";
                    }
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
                    if (parsedVar.Branches.Any() && !string.IsNullOrEmpty(parsedVar.VariableName))
                    {
                        parsedVar.FullPath = string.Join(".", parsedVar.Branches) + "." + parsedVar.VariableName;
                    }
                    else if (!string.IsNullOrEmpty(parsedVar.VariableName))
                    {
                        parsedVar.FullPath = parsedVar.VariableName;
                    }
                    bool hasSystemBranch = parsedVar.Branches.Any(branch =>
                        branch.Equals("System", StringComparison.OrdinalIgnoreCase));

                    if (hasSystemBranch && !includeSystemVariables)
                    {
                        continue;
                    }
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
        /// Prints the parsed variables to the console for debugging purposes.
        /// </summary>
        /// <param name="parseResult">The parsing result to print.</param>
        /// <param name="connectionInfo">The connection information to display.</param>
        /// <param name="includeSystemVariables">Whether system variables were included.</param>
        public void PrintParsedVariablesToConsole(ParseResult parseResult, string connectionInfo, bool includeSystemVariables = false)
        {
            Console.WriteLine("\n=====================================================");
            Console.WriteLine("PCVue VARIABLES BROWSE - PARSED RESULTS");
            Console.WriteLine("=====================================================");
            Console.WriteLine($"Connection: {connectionInfo}");
            Console.WriteLine($"Total Variables Found: {parseResult.TotalCount:N0}");
            Console.WriteLine($"System Variables: {(includeSystemVariables ? "INCLUDED" : "FILTERED OUT")}");
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
                        Console.WriteLine($"   |- Branches: {string.Join(" -> ", variable.Branches)}");
                    }
                    Console.WriteLine($"   |- Variable: {variable.VariableName}");
                    Console.WriteLine($"   |- Type: {variable.VariableType}, ReadOnly: {variable.IsReadOnly}");
                    if ((i + 1) % 5 == 0 && i < parseResult.Variables.Count - 1)
                    {
                        Console.WriteLine();
                    }
                }
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