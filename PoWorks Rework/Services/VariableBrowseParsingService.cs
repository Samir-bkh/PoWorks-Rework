using System.Text.Json;

namespace PoWorks_Rework.Services
{
    /// <summary>
    /// Parses PCVue RealtimeData browse responses and applies the import-scope filter.
    /// </summary>
    public class VariableBrowseParsingService
    {
        private readonly ILogger<VariableBrowseParsingService> _logger;

        public VariableBrowseParsingService(ILogger<VariableBrowseParsingService> logger)
        {
            _logger = logger;
        }

        public sealed class ParsedVariable
        {
            public string FullPath { get; set; } = "";
            public List<string> Branches { get; set; } = new();
            public string VariableName { get; set; } = "";
            public string VariableType { get; set; } = "";
            public bool IsReadOnly { get; set; }
            public bool IsLeaf { get; set; }
            public bool IsSystemVariable { get; set; }
        }

        public sealed class ParseResult
        {
            public bool Success { get; set; }
            public List<ParsedVariable> Variables { get; set; } = new();
            public int TotalCount { get; set; }
            public int FilteredSystemVariables { get; set; }
            public string ErrorMessage { get; set; } = "";
        }

        public ParseResult ParseBrowseVariablesResponse(object responseData, bool includeSystemVariables = false)
        {
            var result = new ParseResult();

            try
            {
                using var document = responseData is JsonElement element
                    ? JsonDocument.Parse(element.GetRawText())
                    : JsonDocument.Parse(JsonSerializer.Serialize(responseData));

                var root = document.RootElement;
                if (!TryGetPropertyInsensitive(root, "variableCollections", out var collections) ||
                    collections.ValueKind != JsonValueKind.Array)
                {
                    result.ErrorMessage = "Response missing 'variableCollections' property";
                    return result;
                }

                foreach (var variable in collections.EnumerateArray())
                {
                    var parsed = ParseVariable(variable);
                    if (string.IsNullOrWhiteSpace(parsed.FullPath))
                    {
                        continue;
                    }

                    if (parsed.IsSystemVariable && !includeSystemVariables)
                    {
                        result.FilteredSystemVariables++;
                        continue;
                    }

                    result.Variables.Add(parsed);
                }

                result.TotalCount = result.Variables.Count;
                result.Success = true;

                _logger.LogInformation(
                    "Successfully parsed {Count} PCVue variables; filtered {SystemCount} system variable(s).",
                    result.TotalCount,
                    result.FilteredSystemVariables);
            }
            catch (Exception ex)
            {
                result.ErrorMessage = $"Error parsing response: {ex.Message}";
                _logger.LogError(ex, "Error parsing PCVue browse variables response");
            }

            return result;
        }

        private static ParsedVariable ParseVariable(JsonElement variable)
        {
            var parsed = new ParsedVariable();

            if (TryGetPropertyInsensitive(variable, "branches", out var branches) &&
                branches.ValueKind == JsonValueKind.Array)
            {
                foreach (var branch in branches.EnumerateArray())
                {
                    var value = branch.ValueKind == JsonValueKind.String
                        ? branch.GetString()
                        : branch.ToString();

                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        parsed.Branches.Add(value.Trim());
                    }
                }
            }

            parsed.VariableName = GetString(variable, "VariableName");
            parsed.VariableType = GetString(variable, "variableType");
            parsed.IsReadOnly = GetBoolean(variable, "IsReadOnly");
            parsed.IsLeaf = GetBoolean(variable, "IsLeaf");

            var branchPrefix = string.Join(".", parsed.Branches.Where(b => !string.IsNullOrWhiteSpace(b)));
            if (!string.IsNullOrWhiteSpace(branchPrefix) && !string.IsNullOrWhiteSpace(parsed.VariableName))
            {
                // Some PCVue responses return only the leaf name while others can already
                // contain a fully-qualified name. Avoid duplicating the branch path.
                parsed.FullPath = StartsWithPath(parsed.VariableName, branchPrefix)
                    ? parsed.VariableName.Trim().TrimStart('.')
                    : $"{branchPrefix}.{parsed.VariableName.Trim().TrimStart('.')}";
            }
            else
            {
                parsed.FullPath = parsed.VariableName.Trim().TrimStart('.');
            }

            parsed.IsSystemVariable =
                parsed.Branches.Any(IsSystemSegment) ||
                IsSystemVariablePath(parsed.FullPath) ||
                IsSystemVariablePath(parsed.VariableName);

            return parsed;
        }

        /// <summary>
        /// Defensive system-variable classifier used by both browse and import.
        /// PCVue documentation uses the root branch "system"; aliases such as
        /// "$System" and "_System" are also treated as system roots.
        /// </summary>
        public static bool IsSystemVariablePath(string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;

            var segments = path.Split(
                new[] { '.', '/', '\\' },
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            return segments.Any(IsSystemSegment);
        }

        private static bool IsSystemSegment(string? segment)
        {
            if (string.IsNullOrWhiteSpace(segment)) return false;

            var normalized = segment.Trim().TrimStart('$', '@', '_');
            return normalized.Equals("system", StringComparison.OrdinalIgnoreCase);
        }

        private static bool StartsWithPath(string candidate, string branchPrefix)
            => candidate.Equals(branchPrefix, StringComparison.OrdinalIgnoreCase) ||
               candidate.StartsWith(branchPrefix + ".", StringComparison.OrdinalIgnoreCase) ||
               candidate.StartsWith(branchPrefix + "/", StringComparison.OrdinalIgnoreCase);

        private static string GetString(JsonElement element, string propertyName)
        {
            if (!TryGetPropertyInsensitive(element, propertyName, out var value)) return "";
            return value.ValueKind == JsonValueKind.String
                ? value.GetString()?.Trim() ?? ""
                : value.ToString().Trim();
        }

        private static bool GetBoolean(JsonElement element, string propertyName)
        {
            if (!TryGetPropertyInsensitive(element, propertyName, out var value)) return false;
            if (value.ValueKind is JsonValueKind.True or JsonValueKind.False) return value.GetBoolean();
            return bool.TryParse(value.ToString(), out var parsed) && parsed;
        }

        private static bool TryGetPropertyInsensitive(
            JsonElement element,
            string propertyName,
            out JsonElement value)
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                if (element.TryGetProperty(propertyName, out value)) return true;

                foreach (var property in element.EnumerateObject())
                {
                    if (property.Name.Equals(propertyName, StringComparison.OrdinalIgnoreCase))
                    {
                        value = property.Value;
                        return true;
                    }
                }
            }

            value = default;
            return false;
        }

        public void PrintParsedVariablesToConsole(
            ParseResult parseResult,
            string connectionInfo,
            bool includeSystemVariables = false)
        {
            Console.WriteLine("\n=====================================================");
            Console.WriteLine("PCVue VARIABLES BROWSE - PARSED RESULTS");
            Console.WriteLine("=====================================================");
            Console.WriteLine($"Connection: {connectionInfo}");
            Console.WriteLine($"Total Variables Found: {parseResult.TotalCount:N0}");
            Console.WriteLine($"System Variables: {(includeSystemVariables ? "INCLUDED" : "FILTERED OUT")}");
            Console.WriteLine($"System Variables Filtered: {parseResult.FilteredSystemVariables:N0}");
            Console.WriteLine($"Parsing Status: {(parseResult.Success ? "SUCCESS" : "FAILED")}");

            if (!parseResult.Success)
            {
                Console.WriteLine($"Error: {parseResult.ErrorMessage}");
            }
            else
            {
                foreach (var variable in parseResult.Variables)
                {
                    Console.WriteLine(
                        $"- {variable.FullPath} | Type={variable.VariableType} | ReadOnly={variable.IsReadOnly}");
                }
            }

            Console.WriteLine("=====================================================\n");
        }
    }
}
