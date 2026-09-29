using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using PoWorks_Rework.Models;

namespace PoWorks_Rework.Services
{
    /// <summary>
    /// Retrieves PCVue historical trend data through the legacy two-step REST API.
    /// A PCVue Trend request allocates server-side resources, so every successful POST
    /// is paired with a DELETE in a finally block. Large ranges are split when PCVue
    /// reports MaxNumberExceeded instead of silently importing truncated history.
    /// </summary>
    public class TrendsService
    {
        private const int MaxSplitDepth = 24;
        private static readonly TimeSpan MinimumSplitWindow = TimeSpan.FromSeconds(2);

        private readonly PCVueWebService _pcvueWebService;
        private readonly ILogger<TrendsService> _logger;

        public TrendsService(
            PCVueWebService pcvueWebService,
            ILogger<TrendsService> logger)
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
                var endpoint = $"{settings.BaseUrl.TrimEnd('/')}/HistoricalData/v2/Trends";
                var payload = new
                {
                    VariableName = variableName,
                    elementMaxNumber = 100000,
                    properties = new[] { "VariableName", "Description", "StandardLabel" }
                };
                var json = JsonSerializer.Serialize(payload);

                var response = await SendAuthorizedWithSingleRetryAsync(
                    settings,
                    token =>
                    {
                        var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
                        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                        request.Content = new StringContent(json, Encoding.UTF8, "application/json");
                        return request;
                    },
                    $"create trend request for {variableName}");

                if (!response.Success)
                {
                    return new TrendRequestResult
                    {
                        Success = false,
                        VariableName = variableName,
                        ErrorMessage = response.ErrorMessage
                    };
                }

                var requestId = response.Content.Trim().Trim('"');
                if (string.IsNullOrWhiteSpace(requestId))
                {
                    return new TrendRequestResult
                    {
                        Success = false,
                        VariableName = variableName,
                        ErrorMessage = "PCVue returned an empty trend RequestId."
                    };
                }

                return new TrendRequestResult
                {
                    Success = true,
                    RequestId = requestId,
                    VariableName = variableName
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unable to create PCVue trend request for {VariableName}.", variableName);
                return new TrendRequestResult
                {
                    Success = false,
                    VariableName = variableName,
                    ErrorMessage = ex.Message
                };
            }
        }

        /// <summary>
        /// Retrieves one range from an existing PCVue trend request.
        /// This method intentionally returns MaxNumberExceeded to its caller so the
        /// complete-history layer can split the range when needed.
        /// </summary>
        public async Task<TrendDataResult> GetTrendDataAsync(
            string requestId,
            DateTime startDate,
            DateTime endDate,
            PCVueWebServiceSettings settings)
        {
            if (string.IsNullOrWhiteSpace(requestId))
            {
                return new TrendDataResult
                {
                    Success = false,
                    ErrorMessage = "RequestId is null or empty."
                };
            }

            if (endDate < startDate)
            {
                return new TrendDataResult
                {
                    Success = false,
                    RequestId = requestId,
                    ErrorMessage = "Trend end date must be on or after start date."
                };
            }

            try
            {
                var cleanRequestId = requestId.Trim('"');
                var endpoint =
                    $"{settings.BaseUrl.TrimEnd('/')}/HistoricalData/v2/Trends/{cleanRequestId}" +
                    $"?Start={Uri.EscapeDataString(FormatPcVueDate(startDate))}" +
                    $"&End={Uri.EscapeDataString(FormatPcVueDate(endDate))}";

                var response = await SendAuthorizedWithSingleRetryAsync(
                    settings,
                    token =>
                    {
                        var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
                        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                        return request;
                    },
                    $"read trend request {cleanRequestId}");

                if (!response.Success)
                {
                    return new TrendDataResult
                    {
                        Success = false,
                        RequestId = requestId,
                        ErrorMessage = response.ErrorMessage
                    };
                }

                var trendData = JsonSerializer.Deserialize<TrendApiResponse>(
                    response.Content,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (trendData == null)
                {
                    return new TrendDataResult
                    {
                        Success = false,
                        RequestId = requestId,
                        ErrorMessage = "PCVue returned an empty or invalid trend response."
                    };
                }

                return new TrendDataResult
                {
                    Success = true,
                    RequestId = requestId,
                    Values = trendData.Values ?? new List<TrendDataPoint>(),
                    MaxNumberExceeded = trendData.MaxNumberExceeded
                };
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Unable to parse PCVue trend response for request {RequestId}.", requestId);
                return new TrendDataResult
                {
                    Success = false,
                    RequestId = requestId,
                    ErrorMessage = "Unable to parse PCVue trend response."
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unable to retrieve PCVue trend request {RequestId}.", requestId);
                return new TrendDataResult
                {
                    Success = false,
                    RequestId = requestId,
                    ErrorMessage = ex.Message
                };
            }
        }

        /// <summary>
        /// Releases the server-side resources allocated by a PCVue trend request.
        /// Cleanup failure is logged but does not turn a successfully retrieved history
        /// into a failed import.
        /// </summary>
        public async Task<bool> DeleteTrendRequestAsync(
            string requestId,
            PCVueWebServiceSettings settings)
        {
            if (string.IsNullOrWhiteSpace(requestId)) return true;

            try
            {
                var cleanRequestId = requestId.Trim('"');
                var endpoint = $"{settings.BaseUrl.TrimEnd('/')}/HistoricalData/v2/Trends/{cleanRequestId}";
                var response = await SendAuthorizedWithSingleRetryAsync(
                    settings,
                    token =>
                    {
                        var request = new HttpRequestMessage(HttpMethod.Delete, endpoint);
                        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                        return request;
                    },
                    $"delete trend request {cleanRequestId}",
                    treatNotFoundAsSuccess: true);

                if (!response.Success)
                {
                    _logger.LogWarning(
                        "Unable to release PCVue trend request {RequestId}: {Error}",
                        cleanRequestId,
                        response.ErrorMessage);
                }

                return response.Success;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Unable to release PCVue trend request {RequestId}.", requestId);
                return false;
            }
        }

        public async Task<List<VariableTrendResult>> ProcessVariablesTrendsAsync(
            List<string> variableNames,
            DateTime startDate,
            DateTime endDate,
            PCVueWebServiceSettings settings,
            string? logContext = null)
        {
            var throttler = new SemaphoreSlim(15);

            var tasks = variableNames.Select(async variableName =>
            {
                await throttler.WaitAsync();
                try
                {
                    return await ProcessSingleVariableAsync(
                        variableName,
                        startDate,
                        endDate,
                        settings,
                        logContext);
                }
                finally
                {
                    throttler.Release();
                }
            });

            var results = await Task.WhenAll(tasks);
            return results.ToList();
        }

        private async Task<VariableTrendResult> ProcessSingleVariableAsync(
            string variableName,
            DateTime startDate,
            DateTime endDate,
            PCVueWebServiceSettings settings,
            string? logContext)
        {
            TrendRequestResult? requestResult = null;
            try
            {
                requestResult = await CreateTrendRequestAsync(variableName, settings);
                var contextPrefix = string.IsNullOrWhiteSpace(logContext)
                    ? string.Empty
                    : $"[{logContext}]";

                Console.WriteLine(
                    $"[TRENDS]{contextPrefix} {variableName} -> request " +
                    (requestResult.Success ? "OK" : "FAIL: " + requestResult.ErrorMessage));

                if (!requestResult.Success || string.IsNullOrWhiteSpace(requestResult.RequestId))
                {
                    return new VariableTrendResult
                    {
                        VariableName = variableName,
                        Success = false,
                        ErrorMessage = requestResult.ErrorMessage
                    };
                }

                var complete = await GetCompleteTrendDataAsync(
                    requestResult.RequestId,
                    startDate,
                    endDate,
                    settings,
                    depth: 0);

                return new VariableTrendResult
                {
                    VariableName = variableName,
                    RequestId = requestResult.RequestId,
                    Success = complete.Success,
                    TrendData = complete.Values,
                    MaxNumberExceeded = complete.MaxNumberExceeded,
                    ErrorMessage = complete.ErrorMessage
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error processing variable {VariableName}.", variableName);
                return new VariableTrendResult
                {
                    VariableName = variableName,
                    RequestId = requestResult?.RequestId,
                    Success = false,
                    ErrorMessage = ex.Message
                };
            }
            finally
            {
                if (!string.IsNullOrWhiteSpace(requestResult?.RequestId))
                {
                    await DeleteTrendRequestAsync(requestResult.RequestId, settings);
                }
            }
        }

        private async Task<TrendDataResult> GetCompleteTrendDataAsync(
            string requestId,
            DateTime startDate,
            DateTime endDate,
            PCVueWebServiceSettings settings,
            int depth)
        {
            var page = await GetTrendDataAsync(requestId, startDate, endDate, settings);
            if (!page.Success || !page.MaxNumberExceeded)
            {
                return page;
            }

            var span = endDate - startDate;
            if (depth >= MaxSplitDepth || span <= MinimumSplitWindow)
            {
                _logger.LogError(
                    "PCVue still reports MaxNumberExceeded for request {RequestId} in the smallest safe window {Start:o} - {End:o}. " +
                    "PoWorks refuses to mark the history complete because data would be truncated.",
                    requestId,
                    startDate,
                    endDate);

                return new TrendDataResult
                {
                    Success = false,
                    RequestId = requestId,
                    Values = page.Values,
                    MaxNumberExceeded = true,
                    ErrorMessage = "PCVue trend history exceeds the retrievable point limit even after range splitting; import stopped to avoid silent data loss."
                };
            }

            var midpoint = startDate.AddTicks(span.Ticks / 2);
            if (midpoint <= startDate || midpoint >= endDate)
            {
                return new TrendDataResult
                {
                    Success = false,
                    RequestId = requestId,
                    Values = page.Values,
                    MaxNumberExceeded = true,
                    ErrorMessage = "Unable to split the PCVue trend range safely."
                };
            }

            _logger.LogDebug(
                "PCVue trend request {RequestId} exceeded the point limit; splitting {Start:o} - {End:o} at {Mid:o}.",
                requestId,
                startDate,
                endDate,
                midpoint);

            // The two ranges deliberately overlap at the midpoint. PCVue boundary
            // semantics can vary with archive type; deduplication below is safer than
            // risking a missing point exactly on the split boundary.
            var left = await GetCompleteTrendDataAsync(
                requestId,
                startDate,
                midpoint,
                settings,
                depth + 1);
            if (!left.Success)
            {
                return left;
            }

            var right = await GetCompleteTrendDataAsync(
                requestId,
                midpoint,
                endDate,
                settings,
                depth + 1);
            if (!right.Success)
            {
                return right;
            }

            return new TrendDataResult
            {
                Success = true,
                RequestId = requestId,
                Values = MergeTrendPoints(left.Values, right.Values),
                MaxNumberExceeded = false
            };
        }

        private static List<TrendDataPoint> MergeTrendPoints(
            IEnumerable<TrendDataPoint> first,
            IEnumerable<TrendDataPoint> second)
        {
            var unique = new Dictionary<string, TrendDataPoint>(StringComparer.Ordinal);

            foreach (var point in first.Concat(second))
            {
                var key = BuildPointIdentity(point);
                if (!unique.ContainsKey(key))
                {
                    unique[key] = point;
                }
            }

            return unique.Values
                .OrderBy(point => point.TimestampParsed ?? DateTime.MaxValue)
                .ThenBy(point => point.Timestamp, StringComparer.Ordinal)
                .ToList();
        }

        private static string BuildPointIdentity(TrendDataPoint point)
            => string.Join(
                "|",
                point.Timestamp ?? string.Empty,
                point.Value.ToString("R", CultureInfo.InvariantCulture),
                point.Quality ?? string.Empty,
                point.QualityValue.ToString(CultureInfo.InvariantCulture));

        private async Task<AuthorizedResponse> SendAuthorizedWithSingleRetryAsync(
            PCVueWebServiceSettings settings,
            Func<string, HttpRequestMessage> requestFactory,
            string operation,
            bool treatNotFoundAsSuccess = false)
        {
            var token = await _pcvueWebService.GetValidAccessTokenAsync(settings);
            if (string.IsNullOrWhiteSpace(token))
            {
                return AuthorizedResponse.Failed("Failed to obtain valid PCVue access token.");
            }

            var response = await SendAsync(requestFactory(token));
            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                _logger.LogWarning(
                    "PCVue returned 401 while attempting to {Operation}; refreshing OAuth session once.",
                    operation);

                token = await _pcvueWebService.GetValidAccessTokenAsync(settings, forceRefresh: true);
                if (string.IsNullOrWhiteSpace(token))
                {
                    return AuthorizedResponse.Failed("Failed to refresh PCVue access token.");
                }

                response = await SendAsync(requestFactory(token));
            }

            var success = response.IsSuccessStatusCode ||
                          (treatNotFoundAsSuccess && response.StatusCode == HttpStatusCode.NotFound);

            if (!success)
            {
                var detail = string.IsNullOrWhiteSpace(response.Content)
                    ? string.Empty
                    : $" - {response.Content}";
                return AuthorizedResponse.Failed(
                    $"API Error: {(int)response.StatusCode} ({response.StatusCode}){detail}");
            }

            return AuthorizedResponse.Ok(response.Content);
        }

        private async Task<HttpPayload> SendAsync(HttpRequestMessage request)
        {
            using (request)
            using (var response = await _pcvueWebService.HttpClient.SendAsync(request))
            {
                var content = await response.Content.ReadAsStringAsync();
                return new HttpPayload(response.StatusCode, response.IsSuccessStatusCode, content);
            }
        }

        private static string FormatPcVueDate(DateTime value)
            => value.ToUniversalTime().ToString(
                "yyyy-MM-dd HH:mm:ss",
                CultureInfo.InvariantCulture);

        private readonly record struct HttpPayload(
            HttpStatusCode StatusCode,
            bool IsSuccessStatusCode,
            string Content);

        private readonly record struct AuthorizedResponse(
            bool Success,
            string Content,
            string? ErrorMessage)
        {
            public static AuthorizedResponse Ok(string content)
                => new(true, content, null);

            public static AuthorizedResponse Failed(string error)
                => new(false, string.Empty, error);
        }
    }
}
