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

        public async Task<TrendRequestResult> CreateTrendRequestAsync(string variableName, PCVueWebServiceSettings settings)
        {
            try
            {
                _logger.LogInformation("Creating trend request for variable: {VariableName}", variableName);

                var token = await _pcvueWebService.GetValidAccessTokenAsync(settings);
                if (string.IsNullOrEmpty(token))
                {
                    return new TrendRequestResult { Success = false, ErrorMessage = "Failed to obtain valid access token" };
                }

                var endpoint = $"{settings.BaseUrl.TrimEnd('/')}/HistoricalData/v2/Trends";
                var payload = new { VariableName = variableName, elementMaxNumber = 100000, properties = new[] { "VariableName", "Description", "StandardLabel" } };
                var jsonContent = JsonSerializer.Serialize(payload);
                var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

                var handler = new HttpClientHandler { ServerCertificateCustomValidationCallback = (_, _, _, _) => true };
                using var httpClient = new HttpClient(handler);
                httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");

                var response = await httpClient.PostAsync(endpoint, content);
                var responseContent = await response.Content.ReadAsStringAsync();

                if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                {
                    _logger.LogWarning("Received 401 Unauthorized. Retrying with FORCE REFRESH...");

                    token = await _pcvueWebService.GetValidAccessTokenAsync(settings, true);

                    if (string.IsNullOrEmpty(token))
                        return new TrendRequestResult { Success = false, ErrorMessage = "Failed to refresh token" };

                    httpClient.DefaultRequestHeaders.Clear();
                    httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");

                    httpClient.DefaultRequestHeaders.Clear();
                    httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");
                    response = await httpClient.PostAsync(endpoint, content);
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

        public async Task<TrendDataResult> GetTrendDataAsync(string requestId, DateTime startDate, DateTime endDate, PCVueWebServiceSettings settings)
        {
            if (string.IsNullOrEmpty(requestId))
                return new TrendDataResult { Success = false, ErrorMessage = "RequestId is null" };

            try
            {
                var token = await _pcvueWebService.GetValidAccessTokenAsync(settings);
                var endpoint = $"{settings.BaseUrl.TrimEnd('/')}/HistoricalData/v2/Trends/{requestId.Trim('"')}?Start={Uri.EscapeDataString(startDate.ToString("yyyy-MM-dd HH:mm:ss"))}&End={Uri.EscapeDataString(endDate.ToString("yyyy-MM-dd HH:mm:ss"))}";

                var handler = new HttpClientHandler { ServerCertificateCustomValidationCallback = (_, _, _, _) => true };
                using var httpClient = new HttpClient(handler);
                httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");

                var response = await httpClient.GetAsync(endpoint);
                var responseContent = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    var trendData = JsonSerializer.Deserialize<TrendApiResponse>(responseContent, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    return new TrendDataResult { Success = true, RequestId = requestId, Values = trendData?.Values ?? new List<TrendDataPoint>() };
                }
                return new TrendDataResult { Success = false, ErrorMessage = "API Error", RequestId = requestId };
            }
            catch (Exception ex)
            {
                return new TrendDataResult { Success = false, ErrorMessage = ex.Message, RequestId = requestId };
            }
        }

        public async Task<List<VariableTrendResult>> ProcessVariablesTrendsAsync(List<string> variableNames, DateTime startDate, DateTime endDate, PCVueWebServiceSettings settings)
        {
            var results = new List<VariableTrendResult>();
            foreach (var variableName in variableNames)
            {
                var requestResult = await CreateTrendRequestAsync(variableName, settings);
                if (requestResult.Success)
                {
                    var dataResult = await GetTrendDataAsync(requestResult.RequestId!, startDate, endDate, settings);
                    results.Add(new VariableTrendResult { VariableName = variableName, Success = dataResult.Success, TrendData = dataResult.Values });
                }
                else
                {
                    results.Add(new VariableTrendResult { VariableName = variableName, Success = false, ErrorMessage = requestResult.ErrorMessage });
                }
                await Task.Delay(200);
            }
            return results;
        }
    }
}