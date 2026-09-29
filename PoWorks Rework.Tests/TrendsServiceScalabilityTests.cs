using System.Collections.Concurrent;
using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using PoWorks_Rework.Models;
using PoWorks_Rework.Services;
using Xunit;

namespace PoWorks_Rework.Tests;

public class TrendsServiceScalabilityTests
{
    [Fact]
    public async Task LargeBatch_KeepsHistoricalNetworkConcurrencyBounded()
    {
        const int variableCount = 250;
        const int concurrency = 8;
        var nextRequestId = 0;
        var handler = new AsyncRecordingHandler(async request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/OAuth/token", StringComparison.OrdinalIgnoreCase))
                return TokenResponse("token-1", "refresh-1");

            if (path.StartsWith("/HistoricalData/v2/Trends", StringComparison.OrdinalIgnoreCase))
            {
                await Task.Delay(2);
                if (request.Method == HttpMethod.Post && path.EndsWith("/HistoricalData/v2/Trends"))
                {
                    var id = Interlocked.Increment(ref nextRequestId);
                    return Text(HttpStatusCode.OK, $"\"request-{id}\"");
                }

                if (request.Method == HttpMethod.Get)
                    return Json(HttpStatusCode.OK, "{\"values\":[],\"maxNumberExceeded\":false}");

                if (request.Method == HttpMethod.Delete)
                    return Json(HttpStatusCode.OK, "{}");
            }

            return Text(HttpStatusCode.NotFound, "not found");
        });
        var service = CreateTrendsService(handler, concurrency);
        var variables = Enumerable.Range(0, variableCount)
            .Select(index => $"Building.Variable.{index:D5}")
            .ToList();

        var results = await service.ProcessVariablesTrendsAsync(
            variables,
            Utc(2026, 9, 29, 10),
            Utc(2026, 9, 29, 11),
            Settings());

        Assert.Equal(variableCount, results.Count);
        Assert.All(results, result => Assert.True(result.Success, result.ErrorMessage));
        Assert.Equal(variables[0], results[0].VariableName);
        Assert.Equal(variables[^1], results[^1].VariableName);
        Assert.InRange(handler.MaxHistoricalConcurrency, 2, concurrency);
        Assert.Equal(variableCount, handler.Count(HttpMethod.Post, "/HistoricalData/v2/Trends"));
        Assert.Equal(variableCount, handler.CountPrefix(HttpMethod.Get, "/HistoricalData/v2/Trends/request-"));
        Assert.Equal(variableCount, handler.CountPrefix(HttpMethod.Delete, "/HistoricalData/v2/Trends/request-"));
        Assert.Equal(1, handler.PasswordGrantCount);
    }

    [Fact]
    public async Task Concurrent401Wave_PerformsOnlyOneRefreshTokenGrant()
    {
        var requestId = 0;
        var handler = new AsyncRecordingHandler(async request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/OAuth/token", StringComparison.OrdinalIgnoreCase))
            {
                var body = request.Content == null
                    ? string.Empty
                    : await request.Content.ReadAsStringAsync();
                return body.Contains("grant_type=refresh_token", StringComparison.Ordinal)
                    ? TokenResponse("new-token", "refresh-2")
                    : TokenResponse("old-token", "refresh-1");
            }

            if (request.Method == HttpMethod.Post && path.EndsWith("/HistoricalData/v2/Trends"))
            {
                await Task.Delay(5);
                if (request.Headers.Authorization?.Parameter == "old-token")
                    return Text(HttpStatusCode.Unauthorized, "expired");

                var id = Interlocked.Increment(ref requestId);
                return Text(HttpStatusCode.OK, $"\"request-{id}\"");
            }

            if (request.Method == HttpMethod.Get && path.StartsWith("/HistoricalData/v2/Trends/request-"))
                return Json(HttpStatusCode.OK, "{\"values\":[],\"maxNumberExceeded\":false}");

            if (request.Method == HttpMethod.Delete && path.StartsWith("/HistoricalData/v2/Trends/request-"))
                return Json(HttpStatusCode.OK, "{}");

            return Text(HttpStatusCode.NotFound, "not found");
        });
        var service = CreateTrendsService(handler, 15);
        var variables = Enumerable.Range(0, 40)
            .Select(index => $"401.Variable.{index}")
            .ToList();

        var results = await service.ProcessVariablesTrendsAsync(
            variables,
            Utc(2026, 9, 29, 10),
            Utc(2026, 9, 29, 11),
            Settings());

        Assert.All(results, result => Assert.True(result.Success, result.ErrorMessage));
        Assert.Equal(1, handler.PasswordGrantCount);
        Assert.Equal(1, handler.RefreshGrantCount);
    }

    [Fact]
    public async Task EmptyVariableBatch_DoesNotAuthenticateOrCallPcVue()
    {
        var handler = new AsyncRecordingHandler(_ =>
            Task.FromResult(Text(HttpStatusCode.InternalServerError, "should not be called")));
        var service = CreateTrendsService(handler, 8);

        var results = await service.ProcessVariablesTrendsAsync(
            new List<string>(),
            Utc(2026, 9, 29, 10),
            Utc(2026, 9, 29, 11),
            Settings());

        Assert.Empty(results);
        Assert.Equal(0, handler.TotalRequests);
    }

    private static TrendsService CreateTrendsService(
        HttpMessageHandler handler,
        int concurrency)
    {
        var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(10) };
        var webService = new PCVueWebService(
            client,
            NullLogger<PCVueWebService>.Instance);
        return new TrendsService(
            webService,
            NullLogger<TrendsService>.Instance,
            concurrency);
    }

    private static PCVueWebServiceSettings Settings() => new()
    {
        ConnectionId = "connection-1",
        ConnectionName = "PCVue benchmark",
        BaseUrl = "https://pcvue.test",
        ClientId = "PoWorks",
        ClientSecret = "secret",
        Username = "enms",
        Password = "password"
    };

    private static DateTime Utc(int year, int month, int day, int hour)
        => new(year, month, day, hour, 0, 0, DateTimeKind.Utc);

    private static HttpResponseMessage TokenResponse(string access, string refresh)
        => Json(
            HttpStatusCode.OK,
            $"{{\"access_token\":\"{access}\",\"token_type\":\"bearer\",\"expires_in\":1200,\"refresh_token\":\"{refresh}\"}}");

    private static HttpResponseMessage Json(HttpStatusCode status, string content)
        => new(status)
        {
            Content = new StringContent(content, Encoding.UTF8, "application/json")
        };

    private static HttpResponseMessage Text(HttpStatusCode status, string content)
        => new(status)
        {
            Content = new StringContent(content, Encoding.UTF8, "text/plain")
        };

    private sealed class AsyncRecordingHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, Task<HttpResponseMessage>> _responder;
        private readonly ConcurrentQueue<RequestRecord> _requests = new();
        private int _activeHistorical;
        private int _maxHistoricalConcurrency;
        private int _passwordGrantCount;
        private int _refreshGrantCount;

        public AsyncRecordingHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> responder)
        {
            _responder = responder;
        }

        public int TotalRequests => _requests.Count;
        public int MaxHistoricalConcurrency => Volatile.Read(ref _maxHistoricalConcurrency);
        public int PasswordGrantCount => Volatile.Read(ref _passwordGrantCount);
        public int RefreshGrantCount => Volatile.Read(ref _refreshGrantCount);

        public int Count(HttpMethod method, string path)
            => _requests.Count(record => record.Method == method && record.Path == path);

        public int CountPrefix(HttpMethod method, string pathPrefix)
            => _requests.Count(record =>
                record.Method == method &&
                record.Path.StartsWith(pathPrefix, StringComparison.OrdinalIgnoreCase));

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            var body = request.Content == null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);
            _requests.Enqueue(new RequestRecord(request.Method, path));

            if (path.EndsWith("/OAuth/token", StringComparison.OrdinalIgnoreCase))
            {
                if (body.Contains("grant_type=refresh_token", StringComparison.Ordinal))
                    Interlocked.Increment(ref _refreshGrantCount);
                else if (body.Contains("grant_type=password", StringComparison.Ordinal))
                    Interlocked.Increment(ref _passwordGrantCount);
            }

            var isHistorical = path.StartsWith(
                "/HistoricalData/v2/Trends",
                StringComparison.OrdinalIgnoreCase);
            if (!isHistorical)
                return await _responder(request);

            var current = Interlocked.Increment(ref _activeHistorical);
            UpdateMaximum(current);
            try
            {
                return await _responder(request);
            }
            finally
            {
                Interlocked.Decrement(ref _activeHistorical);
            }
        }

        private void UpdateMaximum(int candidate)
        {
            while (true)
            {
                var current = Volatile.Read(ref _maxHistoricalConcurrency);
                if (candidate <= current) return;
                if (Interlocked.CompareExchange(
                        ref _maxHistoricalConcurrency,
                        candidate,
                        current) == current)
                    return;
            }
        }

        private sealed record RequestRecord(HttpMethod Method, string Path);
    }
}
