using System.Text.Json;
using System.Text;
using PoWorks_Rework.Models;

namespace PoWorks_Rework.Services
{
    public class TrendsService
    {
        private readonly PCVueWebService _pcvueWebService;
        private readonly ILogger<TrendsService> _logger;

        public TrendsService(PCVueWebService pcvueWebService, ILogger<TrendsService> logger)
        {
            _pcvueWebService = pcvueWebService;
            _logger = logger;
        }

        /// <summary>
        /// Creates a trend request for a single variable
        /// </summary>
        public async Task<TrendRequestResult> CreateTrendRequestAsync(string variableName, PCVueWebServiceSettings settings)
        {
            try
            {
                _logger.LogInformation("Creating trend request for variable: {VariableName}", variableName);

                // Get valid token (handles refresh if needed)
                var token = await _pcvueWebService.GetValidAccessTokenAsync(settings);
                if (string.IsNullOrEmpty(token))
                {
                    return new TrendRequestResult
                    {
                        Success = false,
                        ErrorMessage = "Failed to obtain valid access token"
                    };
                }

                var endpoint = $"{settings.BaseUrl.TrimEnd('/')}/HistoricalData/v2/Trends";

                // Prepare request payload
                var payload = new
                {
                    VariableName = variableName,
                    elementMaxNumber = 100000, // Large default to get all available data
                    properties = new[] { "VariableName", "Description", "StandardLabel" }
                };

                var jsonContent = JsonSerializer.Serialize(payload);
                var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

                // CREATE HTTPCLIENT WITH SSL BYPASS (for development environments)
                var handler = new HttpClientHandler();
                handler.ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true;

                using var httpClient = new HttpClient(handler);
                httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");

                _logger.LogInformation("Sending POST request to: {Endpoint}", endpoint);
                var response = await httpClient.PostAsync(endpoint, content);

                if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                {
                    _logger.LogWarning("Received 401 Unauthorized. Attempting to get fresh token and retry...");

                    // Get fresh token and retry once
                    token = await _pcvueWebService.GetValidAccessTokenAsync(settings);
                    if (string.IsNullOrEmpty(token))
                    {
                        return new TrendRequestResult
                        {
                            Success = false,
                            ErrorMessage = "Failed to get fresh token after 401 error"
                        };
                    }

                    httpClient.DefaultRequestHeaders.Clear();
                    httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");
                    response = await httpClient.PostAsync(endpoint, content);
                }

                var responseContent = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    // Response is plain text containing the RequestID
                    var requestId = responseContent.Trim();
                    _logger.LogInformation("Trend request created successfully. RequestID: {RequestId}", requestId);

                    return new TrendRequestResult
                    {
                        Success = true,
                        RequestId = requestId,
                        VariableName = variableName
                    };
                }
                else
                {
                    _logger.LogError("Failed to create trend request. Status: {StatusCode}, Response: {Response}",
                        response.StatusCode, responseContent);

                    return new TrendRequestResult
                    {
                        Success = false,
                        ErrorMessage = $"API Error: {response.StatusCode} - {responseContent}",
                        VariableName = variableName
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception occurred while creating trend request for variable: {VariableName}", variableName);
                return new TrendRequestResult
                {
                    Success = false,
                    ErrorMessage = $"Exception: {ex.Message}",
                    VariableName = variableName
                };
            }
        }

        /// <summary>
        /// Gets trend data for a specific request ID
        /// </summary>
        public async Task<TrendDataResult> GetTrendDataAsync(string requestId, DateTime startDate, DateTime endDate, PCVueWebServiceSettings settings)
        {
            try
            {
                _logger.LogInformation("Getting trend data for RequestID: {RequestId}", requestId);

                // Get valid token (handles refresh if needed)
                var token = await _pcvueWebService.GetValidAccessTokenAsync(settings);
                if (string.IsNullOrEmpty(token))
                {
                    return new TrendDataResult
                    {
                        Success = false,
                        ErrorMessage = "Failed to obtain valid access token",
                        RequestId = requestId
                    };
                }

                // Format dates as required by API
                var startStr = startDate.ToString("yyyy-MM-dd HH:mm:ss");
                var endStr = endDate.ToString("yyyy-MM-dd HH:mm:ss");

                var endpoint = $"{settings.BaseUrl.TrimEnd('/')}/HistoricalData/v2/Trends/{requestId}?Start={Uri.EscapeDataString(startStr)}&End={Uri.EscapeDataString(endStr)}";

                // CREATE HTTPCLIENT WITH SSL BYPASS (for development environments)
                var handler = new HttpClientHandler();
                handler.ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true;

                using var httpClient = new HttpClient(handler);
                httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");

                _logger.LogInformation("Sending GET request to: {Endpoint}", endpoint);
                var response = await httpClient.GetAsync(endpoint);

                if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                {
                    _logger.LogWarning("Received 401 Unauthorized. Attempting to get fresh token and retry...");

                    // Get fresh token and retry once
                    token = await _pcvueWebService.GetValidAccessTokenAsync(settings);
                    if (string.IsNullOrEmpty(token))
                    {
                        return new TrendDataResult
                        {
                            Success = false,
                            ErrorMessage = "Failed to get fresh token after 401 error",
                            RequestId = requestId
                        };
                    }

                    httpClient.DefaultRequestHeaders.Clear();
                    httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");
                    response = await httpClient.GetAsync(endpoint);
                }

                var responseContent = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    _logger.LogInformation("Trend data retrieved successfully for RequestID: {RequestId}", requestId);

                    // Parse JSON response
                    var trendData = JsonSerializer.Deserialize<TrendApiResponse>(responseContent, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });

                    return new TrendDataResult
                    {
                        Success = true,
                        RequestId = requestId,
                        Values = trendData?.Values ?? new List<TrendDataPoint>(),
                        MaxNumberExceeded = trendData?.MaxNumberExceeded ?? false
                    };
                }
                else
                {
                    _logger.LogError("Failed to get trend data. Status: {StatusCode}, Response: {Response}",
                        response.StatusCode, responseContent);

                    return new TrendDataResult
                    {
                        Success = false,
                        ErrorMessage = $"API Error: {response.StatusCode} - {responseContent}",
                        RequestId = requestId
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception occurred while getting trend data for RequestID: {RequestId}", requestId);
                return new TrendDataResult
                {
                    Success = false,
                    ErrorMessage = $"Exception: {ex.Message}",
                    RequestId = requestId
                };
            }
        }

        /// <summary>
        /// Process multiple variables to get trend data (one by one as required by API)
        /// </summary>
        public async Task<List<VariableTrendResult>> ProcessVariablesTrendsAsync(
            List<string> variableNames,
            DateTime startDate,
            DateTime endDate,
            PCVueWebServiceSettings settings)
        {
            var results = new List<VariableTrendResult>();

            _logger.LogInformation("Processing {Count} variables for trend data", variableNames.Count);

            foreach (var variableName in variableNames)
            {
                try
                {
                    _logger.LogInformation("Processing variable: {VariableName}", variableName);

                    // Step 1: Create trend request
                    var requestResult = await CreateTrendRequestAsync(variableName, settings);
                    if (!requestResult.Success)
                    {
                        results.Add(new VariableTrendResult
                        {
                            VariableName = variableName,
                            Success = false,
                            ErrorMessage = $"Failed to create trend request: {requestResult.ErrorMessage}"
                        });
                        continue;
                    }

                    // Step 2: Get trend data
                    var dataResult = await GetTrendDataAsync(requestResult.RequestId!, startDate, endDate, settings);
                    if (!dataResult.Success)
                    {
                        results.Add(new VariableTrendResult
                        {
                            VariableName = variableName,
                            Success = false,
                            ErrorMessage = $"Failed to get trend data: {dataResult.ErrorMessage}",
                            RequestId = requestResult.RequestId
                        });
                        continue;
                    }

                    // Success - add to results
                    results.Add(new VariableTrendResult
                    {
                        VariableName = variableName,
                        Success = true,
                        RequestId = requestResult.RequestId,
                        TrendData = dataResult.Values,
                        MaxNumberExceeded = dataResult.MaxNumberExceeded
                    });

                    _logger.LogInformation("Successfully processed variable: {VariableName} with {Count} data points",
                        variableName, dataResult.Values.Count);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Exception processing variable: {VariableName}", variableName);
                    results.Add(new VariableTrendResult
                    {
                        VariableName = variableName,
                        Success = false,
                        ErrorMessage = $"Exception: {ex.Message}"
                    });
                }

                // Small delay between requests to be API-friendly
                await Task.Delay(100);
            }

            _logger.LogInformation("Completed processing {Total} variables. Success: {Success}, Failed: {Failed}",
                results.Count, results.Count(r => r.Success), results.Count(r => !r.Success));

            return results;
        }
    }
}