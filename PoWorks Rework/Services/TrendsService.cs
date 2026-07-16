using System.Text.Json;
using System.Text;
using PoWorks_Rework.Models;
using Microsoft.AspNetCore.Http.HttpResults;

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
                    var requestId = responseContent.Trim().Trim('"');
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

                var endpoint = $"{settings.BaseUrl.TrimEnd('/')}/HistoricalData/v2/Trends/{requestId.Trim('"')}?Start={Uri.EscapeDataString(startStr)}&End={Uri.EscapeDataString(endStr)}";

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

            _logger.LogInformation("Traitement de {Count} variables de manière séquentielle.", variableNames.Count);

            foreach (var variableName in variableNames)
            {
                _logger.LogInformation("Demande de trends pour la variable: {VariableName}", variableName);

                try
                {
                    // 1 requête = 1 compteur (Pas besoin de trier les données, elles sont toutes à lui !)
                    var requestResult = await CreateTrendRequestAsync(variableName, settings);

                    if (requestResult.Success)
                    {
                        var dataResult = await GetTrendDataAsync(requestResult.RequestId, startDate, endDate, settings);

                        results.Add(new VariableTrendResult
                        {
                            VariableName = variableName,
                            Success = dataResult.Success,
                            TrendData = dataResult.Values // On prend TOUT directement
                        });
                    }
                    else
                    {
                        results.Add(new VariableTrendResult
                        {
                            VariableName = variableName,
                            Success = false,
                            ErrorMessage = requestResult.ErrorMessage
                        });
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Erreur lors du traitement de la variable {VariableName}", variableName);
                    results.Add(new VariableTrendResult { VariableName = variableName, Success = false, ErrorMessage = ex.Message });
                }

                // Petite pause de sécurité pour ne pas étouffer PcVue
                await Task.Delay(200);
            }

            return results;
        }

        public async Task<bool> ProcessTrendsForVariable(string variableName, PCVueWebServiceSettings settings, DateTime startDate, DateTime endDate)
        {
            var requestResult = await CreateTrendRequestAsync(variableName, settings);
            if (!requestResult.Success) return false;

            var dataResult = await GetTrendDataAsync(requestResult.RequestId!, startDate, endDate, settings);
            return dataResult.Success && dataResult.Values.Any();
        }
    }
}