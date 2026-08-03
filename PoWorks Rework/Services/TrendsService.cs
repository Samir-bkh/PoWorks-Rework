using System.Text.Json;
using System.Text;
using System.Threading;
using PoWorks_Rework.Models;

namespace PoWorks_Rework.Services
{
    /// <summary>
    /// Service for retrieving and processing historical trends data from PCVue web service.
    /// Manages trend requests, data retrieval, and handles OAuth token management with retry logic.
    /// </summary>
    public class TrendsService
    {
        private readonly PCVueWebService _pcvueWebService;
        private readonly ILogger<TrendsService> _logger;

        /// <summary>
        /// Initializes the trends service with web service and logging dependencies.
        /// </summary>
        public TrendsService(PCVueWebService pcvueWebService, ILogger<TrendsService> logger)
        {
            _pcvueWebService = pcvueWebService;
            _logger = logger;
        }

        /// <summary>
        /// Creates a trends data request for a specific variable in PCVue.
        /// Returns a request ID used to retrieve the actual data.
        /// Handles authentication and retries if token expires.
        /// </summary>
        public async Task<TrendRequestResult> CreateTrendRequestAsync(string variableName, PCVueWebServiceSettings settings)
        {
            try
            {
                _logger.LogDebug("Creating trend request for variable: {VariableName}", variableName);

                var token = await _pcvueWebService.GetValidAccessTokenAsync(settings);
                if (string.IsNullOrEmpty(token))
                {
                    return new TrendRequestResult { Success = false, ErrorMessage = "Failed to obtain valid access token", VariableName = variableName };
                }

                var endpoint = $"{settings.BaseUrl.TrimEnd('/')}/HistoricalData/v2/Trends";
                var payload = new { VariableName = variableName, elementMaxNumber = 100000, properties = new[] { "VariableName", "Description", "StandardLabel" } };
                var jsonContent = JsonSerializer.Serialize(payload);

                var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
                request.Content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

                var response = await _pcvueWebService.HttpClient.SendAsync(request);
                var responseContent = await response.Content.ReadAsStringAsync();

                if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                {
                    _logger.LogWarning("Received 401 Unauthorized. Retrying with FORCE REFRESH...");

                    token = await _pcvueWebService.GetValidAccessTokenAsync(settings, true);

                    if (string.IsNullOrEmpty(token))
                        return new TrendRequestResult { Success = false, ErrorMessage = "Failed to refresh token", VariableName = variableName };

                    var retryRequest = new HttpRequestMessage(HttpMethod.Post, endpoint);
                    retryRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
                    retryRequest.Content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

                    response = await _pcvueWebService.HttpClient.SendAsync(retryRequest);
                    responseContent = await response.Content.ReadAsStringAsync();
                }

                if (response.IsSuccessStatusCode)
                {
                    var requestId = responseContent.Trim().Trim('"');
                    return new TrendRequestResult { Success = true, RequestId = requestId, VariableName = variableName };
                }

                return new TrendRequestResult { Success = false, ErrorMessage = $"API Error: {response.StatusCode}", VariableName = variableName };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception variable: {VariableName}", variableName);
                return new TrendRequestResult { Success = false, ErrorMessage = ex.Message, VariableName = variableName };
            }
        }

        /// <summary>
        /// Retrieves trend data for a previously created trend request.
        /// Handles authentication retries if the token expires.
        /// </summary>
        /// <param name="requestId">The request ID returned from CreateTrendRequestAsync.</param>
        /// <param name="startDate">The start of the data range.</param>
        /// <param name="endDate">The end of the data range.</param>
        /// <param name="settings">The web service connection settings to use.</param>
        /// <returns>A TrendDataResult with the retrieved data points.</returns>
        public async Task<TrendDataResult> GetTrendDataAsync(string requestId, DateTime startDate, DateTime endDate, PCVueWebServiceSettings settings)
        {
            if (string.IsNullOrEmpty(requestId))
                return new TrendDataResult { Success = false, ErrorMessage = "RequestId is null" };

            try
            {
                var token = await _pcvueWebService.GetValidAccessTokenAsync(settings);
                var endpoint = $"{settings.BaseUrl.TrimEnd('/')}/HistoricalData/v2/Trends/{requestId.Trim('"')}?Start={Uri.EscapeDataString(startDate.ToString("yyyy-MM-dd HH:mm:ss"))}&End={Uri.EscapeDataString(endDate.ToString("yyyy-MM-dd HH:mm:ss"))}";

                var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

                var response = await _pcvueWebService.HttpClient.SendAsync(request);
                var responseContent = await response.Content.ReadAsStringAsync();

                if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                {
                    _logger.LogWarning("Received 401 Unauthorized. Retrying with FORCE REFRESH...");

                    token = await _pcvueWebService.GetValidAccessTokenAsync(settings, true);

                    if (string.IsNullOrEmpty(token))
                        return new TrendDataResult { Success = false, ErrorMessage = "Failed to refresh token", RequestId = requestId };

                    var retryRequest = new HttpRequestMessage(HttpMethod.Get, endpoint);
                    retryRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

                    response = await _pcvueWebService.HttpClient.SendAsync(retryRequest);
                    responseContent = await response.Content.ReadAsStringAsync();
                }

                if (response.IsSuccessStatusCode)
                {
                    var trendData = JsonSerializer.Deserialize<TrendApiResponse>(responseContent, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    return new TrendDataResult { Success = true, RequestId = requestId, Values = trendData?.Values ?? new List<TrendDataPoint>() };
                }

                return new TrendDataResult { Success = false, ErrorMessage = $"API Error: {response.StatusCode}", RequestId = requestId };
            }
            catch (Exception ex)
            {
                return new TrendDataResult { Success = false, ErrorMessage = ex.Message, RequestId = requestId };
            }
        }

        /// <summary>
        /// Processes trends data for multiple variables concurrently with a throttling limit.
        /// </summary>
        /// <param name="variableNames">The list of variable names to process.</param>
        /// <param name="startDate">The start of the data range.</param>
        /// <param name="endDate">The end of the data range.</param>
        /// <param name="settings">The web service connection settings to use.</param>
        /// <returns>A list of per-variable trend results.</returns>
        public async Task<List<VariableTrendResult>> ProcessVariablesTrendsAsync(List<string> variableNames, DateTime startDate, DateTime endDate, PCVueWebServiceSettings settings)
        {
            var throttler = new SemaphoreSlim(15);

            var tasks = variableNames.Select(async variableName =>
            {
                await throttler.WaitAsync();
                try
                {
                    var requestResult = await CreateTrendRequestAsync(variableName, settings);
                    Console.WriteLine($"[TRENDS] {variableName} -> request {(requestResult.Success ? "OK" : "FAIL: " + requestResult.ErrorMessage)}");
                    if (requestResult.Success)
                    {
                        var dataResult = await GetTrendDataAsync(requestResult.RequestId!, startDate, endDate, settings);
                        return new VariableTrendResult
                        {
                            VariableName = variableName,
                            Success = dataResult.Success,
                            TrendData = dataResult.Values
                        };
                    }

                    return new VariableTrendResult
                    {
                        VariableName = variableName,
                        Success = false,
                        ErrorMessage = requestResult.ErrorMessage
                    };
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Unexpected error processing variable: {VariableName}", variableName);
                    return new VariableTrendResult
                    {
                        VariableName = variableName,
                        Success = false,
                        ErrorMessage = ex.Message
                    };
                }
                finally
                {
                    throttler.Release();
                }
            });

            var results = await Task.WhenAll(tasks);
            return results.ToList();
        }
    }
}