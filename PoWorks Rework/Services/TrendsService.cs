using System.Collections.Concurrent;
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
        private static readonly TimeSpan HistoricalWindowSize = TimeSpan.FromDays(7);
        private static readonly TimeSpan HistoricalWindowOverlap = TimeSpan.FromSeconds(2);
        // PcVue's HistoricalData service caps a single trend reply at 4,000
        // points, even if a larger ElementMaxNumber is requested.
        private const int MaxTrendPointsPerRequest = 4000;
        private const int DefaultMaxConcurrentTrendRequests = 15;
        private static readonly TimeSpan MinimumSplitWindow = TimeSpan.FromSeconds(2);

        private readonly PCVueWebService _pcvueWebService;
        private readonly ILogger<TrendsService> _logger;
        private readonly int _maxConcurrentTrendRequests;
        private readonly ConcurrentDictionary<string, SemaphoreSlim> _unauthorizedRefreshGates =
            new(StringComparer.Ordinal);

        public TrendsService(
            PCVueWebService pcvueWebService,
            ILogger<TrendsService> logger)
            : this(pcvueWebService, logger, DefaultMaxConcurrentTrendRequests)
        {
        }

        /// <summary>
        /// Explicit concurrency overload used by benchmarks and tests. Production uses
        /// the conservative default of 15 concurrent variables until a real PCVue
        /// benchmark proves another value is safer/faster.
        /// </summary>
        public TrendsService(
            PCVueWebService pcvueWebService,
            ILogger<TrendsService> logger,
            int maxConcurrentTrendRequests)
        {
            _pcvueWebService = pcvueWebService;
            _logger = logger;
            _maxConcurrentTrendRequests = Math.Clamp(maxConcurrentTrendRequests, 1, 64);
        }

        public async Task<TrendRequestResult> CreateTrendRequestAsync(
            string variableName,
            PCVueWebServiceSettings settings,
            CancellationToken cancellationToken = default)
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
                    elementMaxNumber = MaxTrendPointsPerRequest,
                    aggregateFunction = 0, // Raw values, not graph decimation.
                    aggregateParam1 = 0,
                    includeStartBound = false,
                    includeEndBound = false
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
                    $"create trend request for {variableName}",
                    cancellationToken: cancellationToken);

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
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
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
            PCVueWebServiceSettings settings,
            CancellationToken cancellationToken = default)
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
                    $"read trend request {cleanRequestId}",
                    cancellationToken: cancellationToken);

                if (!response.Success)
                {
                    return new TrendDataResult
                    {
                        Success = false,
                        RequestId = requestId,
                        ErrorMessage = response.ErrorMessage
                    };
                }

                using var document = JsonDocument.Parse(response.Content);
                if (document.RootElement.ValueKind != JsonValueKind.Object ||
                    document.RootElement.TryGetProperty("code", out _))
                {
                    return new TrendDataResult
                    {
                        Success = false,
                        RequestId = requestId,
                        ErrorMessage = $"PCVue rejected the trend query: {response.Content}"
                    };
                }

                if (!document.RootElement.TryGetProperty("values", out var values) ||
                    (values.ValueKind != JsonValueKind.Array &&
                     values.ValueKind != JsonValueKind.Null))
                {
                    return new TrendDataResult
                    {
                        Success = false,
                        RequestId = requestId,
                        ErrorMessage = $"PCVue returned no trend values array: {response.Content}"
                    };
                }

                if (values.ValueKind == JsonValueKind.Null &&
                    document.RootElement.TryGetProperty("maxNumberExceeded", out var exceeded) &&
                    exceeded.ValueKind == JsonValueKind.True)
                {
                    return new TrendDataResult
                    {
                        Success = false,
                        RequestId = requestId,
                        ErrorMessage = "PCVue reported truncated history without returning any trend points."
                    };
                }

                var trendData = document.RootElement.Deserialize<TrendApiResponse>(
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
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
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
            PCVueWebServiceSettings settings,
            CancellationToken cancellationToken = default)
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
                    treatNotFoundAsSuccess: true,
                    cancellationToken: cancellationToken);

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

        /// <summary>
        /// Processes arbitrarily large variable lists with bounded active work. Unlike
        /// Select(async ...)+Task.WhenAll, Parallel.ForEachAsync does not create one
        /// suspended Task per variable, which keeps memory stable for large imports.
        /// Result order remains identical to the input order.
        /// </summary>
        public async Task<List<VariableTrendResult>> ProcessVariablesTrendsAsync(
            List<string> variableNames,
            DateTime startDate,
            DateTime endDate,
            PCVueWebServiceSettings settings,
            string? logContext = null,
            CancellationToken cancellationToken = default)
        {
            if (variableNames == null || variableNames.Count == 0)
            {
                return new List<VariableTrendResult>();
            }

            var results = new VariableTrendResult[variableNames.Count];
            var options = new ParallelOptions
            {
                MaxDegreeOfParallelism = _maxConcurrentTrendRequests,
                CancellationToken = cancellationToken
            };

            await Parallel.ForEachAsync(
                Enumerable.Range(0, variableNames.Count),
                options,
                async (index, _) =>
                {
                    results[index] = await ProcessSingleVariableAsync(
                        variableNames[index],
                        startDate,
                        endDate,
                        settings,
                        logContext,
                        cancellationToken);
                });

            return results.ToList();
        }

        /// <summary>
        /// A broad PcVue query can return an empty or incomplete result even when a
        /// short query within the same range has archived points. Query bounded
        /// windows with one PcVue request per variable and let the caller persist
        /// each window before retrieving the next one.
        /// </summary>
        public static IReadOnlyList<HistoricalTrendWindow> PlanHistoricalWindows(
            DateTime startUtc, DateTime endUtc)
        {
            if (endUtc <= startUtc)
                throw new ArgumentException("The historical end must be after the start.");

            var windows = new List<HistoricalTrendWindow>();
            var cursor = startUtc;
            while (cursor < endUtc)
            {
                var remaining = endUtc - cursor;
                var windowEnd = remaining <= HistoricalWindowSize
                    ? endUtc
                    : cursor.Add(HistoricalWindowSize);
                windows.Add(new HistoricalTrendWindow(cursor, windowEnd));
                if (windowEnd == endUtc) break;

                // Include real readings on both sides of a split boundary. The
                // database's (MeterId, Timestamp) constraint removes duplicates.
                cursor = windowEnd.Subtract(HistoricalWindowOverlap);
            }

            return windows;
        }

        public async Task ProcessVariableTrendWindowsAsync(
            string variableName,
            IReadOnlyList<HistoricalTrendWindow> windows,
            PCVueWebServiceSettings settings,
            Func<HistoricalTrendWindow, VariableTrendResult, Task> onWindow,
            CancellationToken cancellationToken = default)
        {
            if (windows.Count == 0) return;

            var request = await CreateTrendRequestAsync(variableName, settings, cancellationToken);
            if (!request.Success || string.IsNullOrWhiteSpace(request.RequestId))
            {
                await onWindow(windows[0], new VariableTrendResult
                {
                    VariableName = variableName,
                    Success = false,
                    ErrorMessage = request.ErrorMessage ?? "PcVue did not create a trend request."
                });
                return;
            }

            try
            {
                foreach (var window in windows)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var result = await GetCompleteTrendDataAsync(
                        request.RequestId, window.StartUtc, window.EndUtc,
                        settings, depth: 0, cancellationToken: cancellationToken);
                    await onWindow(window, new VariableTrendResult
                    {
                        VariableName = variableName,
                        RequestId = request.RequestId,
                        Success = result.Success,
                        TrendData = result.Values,
                        MaxNumberExceeded = result.MaxNumberExceeded,
                        ErrorMessage = result.ErrorMessage
                    });
                }
            }
            finally
            {
                using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(8));
                await DeleteTrendRequestAsync(request.RequestId, settings, cleanup.Token);
            }
        }

        private async Task<VariableTrendResult> ProcessSingleVariableAsync(
            string variableName,
            DateTime startDate,
            DateTime endDate,
            PCVueWebServiceSettings settings,
            string? logContext,
            CancellationToken cancellationToken)
        {
            TrendRequestResult? requestResult = null;
            try
            {
                requestResult = await CreateTrendRequestAsync(variableName, settings, cancellationToken);
                var contextPrefix = string.IsNullOrWhiteSpace(logContext)
                    ? string.Empty
                    : $"[{logContext}]";

                if (!string.Equals(logContext, "Auto-import recent snapshot", StringComparison.Ordinal))
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
                    depth: 0,
                    cancellationToken: cancellationToken);

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
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
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
                    using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(8));
                    await DeleteTrendRequestAsync(requestResult.RequestId, settings, cleanup.Token);
                }
            }
        }

        private async Task<TrendDataResult> GetCompleteTrendDataAsync(
            string requestId,
            DateTime startDate,
            DateTime endDate,
            PCVueWebServiceSettings settings,
            int depth,
            CancellationToken cancellationToken)
        {
            var page = await GetTrendDataAsync(requestId, startDate, endDate, settings, cancellationToken);
            if (!page.Success ||
                (!page.MaxNumberExceeded && page.Values.Count < MaxTrendPointsPerRequest))
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
                depth + 1,
                cancellationToken);
            if (!left.Success)
            {
                return left;
            }

            var right = await GetCompleteTrendDataAsync(
                requestId,
                midpoint,
                endDate,
                settings,
                depth + 1,
                cancellationToken);
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
            bool treatNotFoundAsSuccess = false,
            CancellationToken cancellationToken = default)
        {
            var token = await _pcvueWebService.GetValidAccessTokenAsync(
                settings, cancellationToken: cancellationToken);
            if (string.IsNullOrWhiteSpace(token))
            {
                return AuthorizedResponse.Failed("Failed to obtain valid PCVue access token.");
            }

            var response = await SendAsync(requestFactory(token), cancellationToken);
            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                _logger.LogWarning(
                    "PCVue returned 401 while attempting to {Operation}; refreshing OAuth session once.",
                    operation);

                token = await RefreshAfterUnauthorizedAsync(settings, token, cancellationToken);
                if (string.IsNullOrWhiteSpace(token))
                {
                    return AuthorizedResponse.Failed("Failed to refresh PCVue access token.");
                }

                response = await SendAsync(requestFactory(token), cancellationToken);
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

        /// <summary>
        /// Collapses a wave of simultaneous 401 responses into one OAuth refresh for
        /// the same PCVue identity. Callers waiting behind the first refresh reuse the
        /// newly cached token instead of refreshing it again.
        /// </summary>
        private async Task<string?> RefreshAfterUnauthorizedAsync(
            PCVueWebServiceSettings settings,
            string rejectedToken,
            CancellationToken cancellationToken)
        {
            var identity = BuildRefreshIdentity(settings);
            var gate = _unauthorizedRefreshGates.GetOrAdd(
                identity,
                _ => new SemaphoreSlim(1, 1));

            await gate.WaitAsync(cancellationToken);
            try
            {
                var currentToken = await _pcvueWebService.GetValidAccessTokenAsync(
                    settings, cancellationToken: cancellationToken);
                if (!string.IsNullOrWhiteSpace(currentToken) &&
                    !string.Equals(currentToken, rejectedToken, StringComparison.Ordinal))
                {
                    return currentToken;
                }

                return await _pcvueWebService.GetValidAccessTokenAsync(
                    settings,
                    forceRefresh: true,
                    cancellationToken: cancellationToken);
            }
            finally
            {
                gate.Release();
            }
        }

        private static string BuildRefreshIdentity(PCVueWebServiceSettings settings)
            => string.Join(
                "|",
                (settings.BaseUrl ?? string.Empty).Trim().TrimEnd('/').ToLowerInvariant(),
                (settings.ClientId ?? string.Empty).Trim().ToLowerInvariant(),
                (settings.Username ?? string.Empty).Trim().ToLowerInvariant());

        private async Task<HttpPayload> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            using (request)
            using (var response = await _pcvueWebService.HttpClient.SendAsync(request, cancellationToken))
            {
                var content = await response.Content.ReadAsStringAsync(cancellationToken);
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

    public sealed record HistoricalTrendWindow(DateTime StartUtc, DateTime EndUtc);
}
