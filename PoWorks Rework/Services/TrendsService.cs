using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using PoWorks_Rework.Models;

namespace PoWorks_Rework.Services
{
    /// <summary>
    /// Retrieves PCVue historical trends and performs single-flight OAuth recovery.
    /// A server-side session may expire before the OAuth access token lifetime; when
    /// concurrent requests receive 401, exactly one request refreshes the session.
    /// </summary>
    public class TrendsService
    {
        private static readonly ConcurrentDictionary<string, SemaphoreSlim> SessionRecoveryGates =
            new(StringComparer.OrdinalIgnoreCase);

        private readonly PCVueWebService _pcvueWebService;
        private readonly ILogger<TrendsService> _logger;

        public TrendsService(PCVueWebService pcvueWebService, ILogger<TrendsService> logger)
        {
            _pcvueWebService = pcvueWebService;
            _logger = logger;
        }

        public async Task<TrendRequestResult> CreateTrendRequestAsync(
            string variableName,
            PCVueWebServiceSettings settings)
        {
            if (string.IsNullOrWhiteSpace(variableName))
            {
                return new TrendRequestResult
                {
                    Success = false,
                    VariableName = variableName,
                    ErrorMessage = "Variable name is required."
                };
            }

            try
            {
                var token = await _pcvueWebService.GetValidAccessTokenAsync(settings);
                if (string.IsNullOrWhiteSpace(token))
                {
                    return FailureRequest(variableName, "Failed to obtain a valid PCVue access token.");
                }

                var endpoint = $"{settings.BaseUrl.TrimEnd('/')}/HistoricalData/v2/Trends";
                var payload = JsonSerializer.Serialize(new
                {
                    VariableName = variableName,
                    elementMaxNumber = 100000,
                    properties = new[] { "VariableName", "Description", "StandardLabel" }
                });

                var result = await SendTrendCreateAsync(endpoint, payload, token);
                if (result.StatusCode == HttpStatusCode.Unauthorized)
                {
                    var recoveredToken = await RecoverSessionAfterUnauthorizedAsync(settings, token);
                    if (string.IsNullOrWhiteSpace(recoveredToken))
                    {
                        return FailureRequest(variableName, "PCVue session expired and could not be refreshed.");
                    }

                    result = await SendTrendCreateAsync(endpoint, payload, recoveredToken);
                }

                if (IsSuccess(result.StatusCode))
                {
                    var requestId = result.Content.Trim().Trim('"');
                    if (string.IsNullOrWhiteSpace(requestId))
                    {
                        return FailureRequest(variableName, "PCVue returned an empty trend request id.");
                    }

                    return new TrendRequestResult
                    {
                        Success = true,
                        RequestId = requestId,
                        VariableName = variableName
                    };
                }

                return FailureRequest(variableName, FormatApiError(result.StatusCode, result.Content));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Trend request failed for {VariableName}.", variableName);
                return FailureRequest(variableName, ex.Message);
            }
        }

        public async Task<TrendDataResult> GetTrendDataAsync(
            string requestId,
            DateTime startDate,
            DateTime endDate,
            PCVueWebServiceSettings settings)
        {
            if (string.IsNullOrWhiteSpace(requestId))
            {
                return new TrendDataResult { Success = false, ErrorMessage = "RequestId is required." };
            }

            try
            {
                var token = await _pcvueWebService.GetValidAccessTokenAsync(settings);
                if (string.IsNullOrWhiteSpace(token))
                {
                    return FailureData(requestId, "Failed to obtain a valid PCVue access token.");
                }

                var endpoint = BuildTrendDataUrl(settings.BaseUrl, requestId, startDate, endDate);
                var result = await SendTrendReadAsync(endpoint, token);

                if (result.StatusCode == HttpStatusCode.Unauthorized)
                {
                    var recoveredToken = await RecoverSessionAfterUnauthorizedAsync(settings, token);
                    if (string.IsNullOrWhiteSpace(recoveredToken))
                    {
                        return FailureData(requestId, "PCVue session expired and could not be refreshed.");
                    }

                    result = await SendTrendReadAsync(endpoint, recoveredToken);
                }

                if (!IsSuccess(result.StatusCode))
                {
                    return FailureData(requestId, FormatApiError(result.StatusCode, result.Content));
                }

                var trendData = JsonSerializer.Deserialize<TrendApiResponse>(
                    result.Content,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                return new TrendDataResult
                {
                    Success = true,
                    RequestId = requestId,
                    Values = trendData?.Values ?? new List<TrendDataPoint>(),
                    MaxNumberExceeded = trendData?.MaxNumberExceeded ?? false
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Trend data read failed for request {RequestId}.", requestId);
                return FailureData(requestId, ex.Message);
            }
        }

        public async Task<List<VariableTrendResult>> ProcessVariablesTrendsAsync(
            List<string> variableNames,
            DateTime startDate,
            DateTime endDate,
            PCVueWebServiceSettings settings,
            string? logContext = null)
        {
            if (variableNames == null || variableNames.Count == 0)
            {
                return new List<VariableTrendResult>();
            }

            using var throttler = new SemaphoreSlim(15);
            var tasks = variableNames
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(async variableName =>
                {
                    await throttler.WaitAsync();
                    try
                    {
                        var request = await CreateTrendRequestAsync(variableName, settings);
                        var contextPrefix = string.IsNullOrWhiteSpace(logContext) ? "" : $"[{logContext}]";
                        Console.WriteLine(
                            $"[TRENDS]{contextPrefix} {variableName} -> request " +
                            (request.Success ? "OK" : "FAIL: " + request.ErrorMessage));

                        if (!request.Success || string.IsNullOrWhiteSpace(request.RequestId))
                        {
                            return new VariableTrendResult
                            {
                                VariableName = variableName,
                                Success = false,
                                ErrorMessage = request.ErrorMessage
                            };
                        }

                        var data = await GetTrendDataAsync(
                            request.RequestId,
                            startDate,
                            endDate,
                            settings);

                        return new VariableTrendResult
                        {
                            VariableName = variableName,
                            RequestId = request.RequestId,
                            Success = data.Success,
                            TrendData = data.Values,
                            MaxNumberExceeded = data.MaxNumberExceeded,
                            ErrorMessage = data.ErrorMessage
                        };
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Unexpected trend error for {VariableName}.", variableName);
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

            return (await Task.WhenAll(tasks)).ToList();
        }

        /// <summary>
        /// Coordinates 401 recovery for a PCVue OAuth identity. Followers entering the
        /// gate after the first refresh observe the new token and reuse it without
        /// issuing another refresh-token request.
        /// </summary>
        private async Task<string?> RecoverSessionAfterUnauthorizedAsync(
            PCVueWebServiceSettings settings,
            string rejectedToken)
        {
            var key = BuildRecoveryKey(settings);
            var gate = SessionRecoveryGates.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
            await gate.WaitAsync();
            try
            {
                var currentToken = await _pcvueWebService.GetValidAccessTokenAsync(settings);
                if (!string.IsNullOrWhiteSpace(currentToken) &&
                    !string.Equals(currentToken, rejectedToken, StringComparison.Ordinal))
                {
                    return currentToken;
                }

                _logger.LogWarning(
                    "PCVue HistoricalData session expired for {Connection}; refreshing the shared OAuth session once.",
                    string.IsNullOrWhiteSpace(settings.ConnectionName)
                        ? settings.ConnectionId
                        : settings.ConnectionName);

                return await _pcvueWebService.GetValidAccessTokenAsync(settings, forceRefresh: true);
            }
            finally
            {
                gate.Release();
            }
        }

        private async Task<HttpResult> SendTrendCreateAsync(
            string endpoint,
            string payload,
            string token)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Content = new StringContent(payload, Encoding.UTF8, "application/json");
            using var response = await _pcvueWebService.HttpClient.SendAsync(request);
            return new HttpResult(response.StatusCode, await response.Content.ReadAsStringAsync());
        }

        private async Task<HttpResult> SendTrendReadAsync(string endpoint, string token)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var response = await _pcvueWebService.HttpClient.SendAsync(request);
            return new HttpResult(response.StatusCode, await response.Content.ReadAsStringAsync());
        }

        private static string BuildTrendDataUrl(
            string baseUrl,
            string requestId,
            DateTime startDate,
            DateTime endDate)
        {
            var startUtc = NormalizeUtc(startDate);
            var endUtc = NormalizeUtc(endDate);
            return $"{baseUrl.TrimEnd('/')}/HistoricalData/v2/Trends/{Uri.EscapeDataString(requestId.Trim('"'))}" +
                   $"?Start={Uri.EscapeDataString(startUtc.ToString("yyyy-MM-dd HH:mm:ss"))}" +
                   $"&End={Uri.EscapeDataString(endUtc.ToString("yyyy-MM-dd HH:mm:ss"))}";
        }

        private static DateTime NormalizeUtc(DateTime value)
            => value.Kind switch
            {
                DateTimeKind.Utc => value,
                DateTimeKind.Local => value.ToUniversalTime(),
                _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
            };

        private static string BuildRecoveryKey(PCVueWebServiceSettings settings)
            => string.Join("|",
                settings.BaseUrl.Trim().TrimEnd('/').ToUpperInvariant(),
                settings.ClientId.Trim(),
                settings.Username.Trim().ToUpperInvariant());

        private static bool IsSuccess(HttpStatusCode statusCode)
            => (int)statusCode is >= 200 and <= 299;

        private static string FormatApiError(HttpStatusCode statusCode, string? content)
        {
            var detail = string.IsNullOrWhiteSpace(content)
                ? string.Empty
                : " - " + Truncate(content.Trim(), 500);
            return $"API Error: {statusCode}{detail}";
        }

        private static string Truncate(string value, int maxLength)
            => value.Length <= maxLength ? value : value[..maxLength] + "…";

        private static TrendRequestResult FailureRequest(string variableName, string message)
            => new()
            {
                Success = false,
                VariableName = variableName,
                ErrorMessage = message
            };

        private static TrendDataResult FailureData(string requestId, string message)
            => new()
            {
                Success = false,
                RequestId = requestId,
                ErrorMessage = message
            };

        private sealed record HttpResult(HttpStatusCode StatusCode, string Content);
    }
}
