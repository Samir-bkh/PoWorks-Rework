using System.Collections.Concurrent;
using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using PoWorks_Rework.Models;
using PoWorks_Rework.Services;
using Xunit;

namespace PoWorks_Rework.Tests;

public class TrendsServiceConcurrencyTests
{
    [Fact]
    public async Task ConcurrentUnauthorizedTrendRequests_RefreshOAuthSessionOnlyOnce()
    {
        const int callers = 5;
        var unauthorizedArrivals = 0;
        var allInitialRequestsArrived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var handler = new RecordingHandler(async (request, cancellationToken) =>
        {
            var path = request.RequestUri?.AbsolutePath ?? string.Empty;

            if (request.Method == HttpMethod.Post && path.Equals("/OAuth/token", StringComparison.OrdinalIgnoreCase))
            {
                var body = await ReadBody(request);
                return body.Contains("grant_type=refresh_token", StringComparison.Ordinal)
                    ? TokenResponse("access-2", "refresh-2")
                    : TokenResponse("access-1", "refresh-1");
            }

            if (request.Method == HttpMethod.Post && path.Equals("/HistoricalData/v2/Trends", StringComparison.OrdinalIgnoreCase))
            {
                var token = request.Headers.Authorization?.Parameter;
                if (token == "access-1")
                {
                    if (Interlocked.Increment(ref unauthorizedArrivals) == callers)
                    {
                        allInitialRequestsArrived.TrySetResult();
                    }

                    await allInitialRequestsArrived.Task.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
                    return new HttpResponseMessage(HttpStatusCode.Unauthorized)
                    {
                        Content = new StringContent("{\"error\":\"E_InvalidSessionId\"}", Encoding.UTF8, "application/json")
                    };
                }

                Assert.Equal("access-2", token);
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("\"request-ok\"", Encoding.UTF8, "application/json")
                };
            }

            throw new InvalidOperationException($"Unexpected request: {request.Method} {path}");
        });

        var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(10) };
        var pcVue = new PCVueWebService(client, NullLogger<PCVueWebService>.Instance);
        var trends = new TrendsService(pcVue, NullLogger<TrendsService>.Instance);
        var settings = Settings();

        var results = await Task.WhenAll(
            Enumerable.Range(0, callers)
                .Select(index => trends.CreateTrendRequestAsync($"Building.Meter{index}", settings)));

        Assert.All(results, result =>
        {
            Assert.True(result.Success, result.ErrorMessage);
            Assert.Equal("request-ok", result.RequestId);
        });

        Assert.Equal(1, handler.CountFormValue("grant_type", "password"));
        Assert.Equal(1, handler.CountFormValue("grant_type", "refresh_token"));
        Assert.Equal(callers * 2, handler.CountRequests(HttpMethod.Post, "/HistoricalData/v2/Trends"));
    }

    [Fact]
    public async Task InternalServerError_PreservesPcVueResponseDetailForDiagnostics()
    {
        var handler = new RecordingHandler(async (request, _) =>
        {
            var path = request.RequestUri?.AbsolutePath ?? string.Empty;
            if (path.Equals("/OAuth/token", StringComparison.OrdinalIgnoreCase))
            {
                _ = await ReadBody(request);
                return TokenResponse("access-1", "refresh-1");
            }

            return new HttpResponseMessage(HttpStatusCode.InternalServerError)
            {
                Content = new StringContent("{\"error\":\"E_UnknownVariable\",\"description\":\"Archive unavailable\"}", Encoding.UTF8, "application/json")
            };
        });

        var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };
        var pcVue = new PCVueWebService(client, NullLogger<PCVueWebService>.Instance);
        var trends = new TrendsService(pcVue, NullLogger<TrendsService>.Instance);

        var result = await trends.CreateTrendRequestAsync("BacNet01.Light1", Settings());

        Assert.False(result.Success);
        Assert.Contains("InternalServerError", result.ErrorMessage);
        Assert.Contains("E_UnknownVariable", result.ErrorMessage);
        Assert.Contains("Archive unavailable", result.ErrorMessage);
    }

    private static PCVueWebServiceSettings Settings() => new()
    {
        ConnectionId = "connection-1",
        ConnectionName = "Connection1",
        BaseUrl = "https://pcvue.test",
        ClientId = "PoWorks",
        ClientSecret = "secret",
        Username = "enms",
        Password = "password"
    };

    private static HttpResponseMessage TokenResponse(string access, string refresh)
        => new(HttpStatusCode.OK)
        {
            Content = new StringContent(
                $"{{\"access_token\":\"{access}\",\"token_type\":\"bearer\",\"expires_in\":1199,\"refresh_token\":\"{refresh}\"}}",
                Encoding.UTF8,
                "application/json")
        };

    private static async Task<string> ReadBody(HttpRequestMessage request)
        => request.Content == null ? string.Empty : await request.Content.ReadAsStringAsync();

    private static Dictionary<string, string> ParseForm(string body)
        => body.Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Split('=', 2))
            .ToDictionary(
                pair => Uri.UnescapeDataString(pair[0].Replace('+', ' ')),
                pair => Uri.UnescapeDataString((pair.Length > 1 ? pair[1] : string.Empty).Replace('+', ' ')),
                StringComparer.OrdinalIgnoreCase);

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _responder;
        private readonly ConcurrentQueue<RequestRecord> _requests = new();

        public RecordingHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder)
        {
            _responder = responder;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = await ReadBody(request);
            _requests.Enqueue(new RequestRecord(
                request.Method,
                request.RequestUri?.AbsolutePath ?? string.Empty,
                body));
            return await _responder(request, cancellationToken);
        }

        public int CountRequests(HttpMethod method, string path)
            => _requests.Count(request => request.Method == method && request.Path.Equals(path, StringComparison.OrdinalIgnoreCase));

        public int CountFormValue(string name, string value)
            => _requests.Count(request =>
            {
                if (string.IsNullOrWhiteSpace(request.Body)) return false;
                var form = ParseForm(request.Body);
                return form.TryGetValue(name, out var actual) &&
                       string.Equals(actual, value, StringComparison.OrdinalIgnoreCase);
            });
    }

    private sealed record RequestRecord(HttpMethod Method, string Path, string Body);
}
