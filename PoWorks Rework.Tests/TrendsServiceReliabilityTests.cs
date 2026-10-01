using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using PoWorks_Rework.Models;
using PoWorks_Rework.Services;
using Xunit;

namespace PoWorks_Rework.Tests;

public class TrendsServiceReliabilityTests
{
    [Fact]
    public async Task TrendRequestUsesPcVueRawArchiveLimitAndNoUnsupportedProperties()
    {
        string? payload = null;
        var handler = new TrendRecordingHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/OAuth/token", StringComparison.OrdinalIgnoreCase))
                return TokenResponse();
            if (request.Method == HttpMethod.Post && path.EndsWith("/HistoricalData/v2/Trends"))
            {
                payload = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
                return Text(HttpStatusCode.OK, "\"request-limit\"");
            }
            if (request.Method == HttpMethod.Get && path.Contains("/Trends/request-limit"))
                return Json(HttpStatusCode.OK, TrendJson(false));
            if (request.Method == HttpMethod.Delete && path.EndsWith("/Trends/request-limit"))
                return Json(HttpStatusCode.OK, "{}");
            return Text(HttpStatusCode.NotFound, "not found");
        });

        var result = Assert.Single(await CreateTrendsService(handler).ProcessVariablesTrendsAsync(
            new List<string> { "Building.Power.kW" }, Utc(2024, 6, 11, 0), Utc(2024, 6, 12, 0), Settings()));

        Assert.True(result.Success, result.ErrorMessage);
        Assert.NotNull(payload);
        using var json = JsonDocument.Parse(payload);
        Assert.Equal(4000, json.RootElement.GetProperty("elementMaxNumber").GetInt32());
        Assert.Equal(0, json.RootElement.GetProperty("aggregateFunction").GetInt32());
        Assert.False(json.RootElement.GetProperty("includeEndBound").GetBoolean());
        Assert.False(json.RootElement.TryGetProperty("properties", out _));
    }

    [Fact]
    public async Task SuccessfulTrendImport_AlwaysDeletesPcVueRequest()
    {
        var handler = new TrendRecordingHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/OAuth/token", StringComparison.OrdinalIgnoreCase))
                return TokenResponse();
            if (request.Method == HttpMethod.Post && path.EndsWith("/HistoricalData/v2/Trends"))
                return Text(HttpStatusCode.OK, "\"request-1\"");
            if (request.Method == HttpMethod.Get && path.Contains("/HistoricalData/v2/Trends/request-1"))
                return Json(HttpStatusCode.OK, TrendJson(false, Point("2026-09-29T10:00:00", 42)));
            if (request.Method == HttpMethod.Delete && path.EndsWith("/HistoricalData/v2/Trends/request-1"))
                return Json(HttpStatusCode.OK, "{\"code\":{\"value\":1,\"label\":\"S_Ok\"}}");
            return Text(HttpStatusCode.NotFound, "not found");
        });
        var service = CreateTrendsService(handler);

        var results = await service.ProcessVariablesTrendsAsync(
            new List<string> { "Building.Energy" },
            Utc(2026, 9, 29, 9),
            Utc(2026, 9, 29, 11),
            Settings());

        var result = Assert.Single(results);
        Assert.True(result.Success, result.ErrorMessage);
        Assert.Single(result.TrendData);
        Assert.Equal(1, handler.Count(HttpMethod.Post, "/HistoricalData/v2/Trends"));
        Assert.Equal(1, handler.Count(HttpMethod.Get, "/HistoricalData/v2/Trends/request-1"));
        Assert.Equal(1, handler.Count(HttpMethod.Delete, "/HistoricalData/v2/Trends/request-1"));
    }

    [Fact]
    public async Task FailedTrendRead_StillDeletesPcVueRequest()
    {
        var handler = new TrendRecordingHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/OAuth/token", StringComparison.OrdinalIgnoreCase))
                return TokenResponse();
            if (request.Method == HttpMethod.Post && path.EndsWith("/HistoricalData/v2/Trends"))
                return Text(HttpStatusCode.OK, "\"request-fail\"");
            if (request.Method == HttpMethod.Get && path.Contains("/HistoricalData/v2/Trends/request-fail"))
                return Text(HttpStatusCode.InternalServerError, "{\"code\":{\"label\":\"E_Fail\"}}");
            if (request.Method == HttpMethod.Delete && path.EndsWith("/HistoricalData/v2/Trends/request-fail"))
                return Json(HttpStatusCode.OK, "{}");
            return Text(HttpStatusCode.NotFound, "not found");
        });
        var service = CreateTrendsService(handler);

        var result = Assert.Single(await service.ProcessVariablesTrendsAsync(
            new List<string> { "NotHistorized.Variable" },
            Utc(2026, 9, 29, 9),
            Utc(2026, 9, 29, 11),
            Settings()));

        Assert.False(result.Success);
        Assert.Contains("500", result.ErrorMessage);
        Assert.Equal(1, handler.Count(HttpMethod.Delete, "/HistoricalData/v2/Trends/request-fail"));
    }

    [Fact]
    public async Task PcVueStatusObjectWithHttp200_IsReportedAsAnError()
    {
        var handler = new TrendRecordingHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/OAuth/token", StringComparison.OrdinalIgnoreCase)) return TokenResponse();
            if (request.Method == HttpMethod.Post && path.EndsWith("/HistoricalData/v2/Trends"))
                return Text(HttpStatusCode.OK, "\"request-status\"");
            if (request.Method == HttpMethod.Get && path.Contains("/Trends/request-status"))
                return Json(HttpStatusCode.OK, "{\"code\":{\"label\":\"E_UnknownVariable\"}}");
            return Json(HttpStatusCode.OK, "{}");
        });

        var result = Assert.Single(await CreateTrendsService(handler).ProcessVariablesTrendsAsync(
            new List<string> { "Missing" }, Utc(2024, 1, 15, 0), Utc(2024, 1, 16, 0), Settings()));

        Assert.False(result.Success);
        Assert.Contains("E_UnknownVariable", result.ErrorMessage);
        Assert.Equal(1, handler.Count(HttpMethod.Delete, "/HistoricalData/v2/Trends/request-status"));
    }

    [Fact]
    public async Task PcVueNullValuesWithoutTruncation_AreAnEmptyArchiveNotAnImportFailure()
    {
        var handler = new TrendRecordingHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/OAuth/token", StringComparison.OrdinalIgnoreCase)) return TokenResponse();
            if (request.Method == HttpMethod.Post && path.EndsWith("/HistoricalData/v2/Trends"))
                return Text(HttpStatusCode.OK, "\"empty-archive\"");
            if (request.Method == HttpMethod.Get && path.Contains("/Trends/empty-archive"))
                return Json(HttpStatusCode.OK, "{\"values\":null,\"maxNumberExceeded\":false}");
            return Json(HttpStatusCode.OK, "{}");
        });

        var result = Assert.Single(await CreateTrendsService(handler).ProcessVariablesTrendsAsync(
            new List<string> { "NotHistorized" }, Utc(2024, 1, 1, 0), Utc(2026, 10, 1, 0), Settings()));

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Empty(result.TrendData);
        Assert.Equal(1, handler.Count(HttpMethod.Delete, "/HistoricalData/v2/Trends/empty-archive"));
    }

    [Fact]
    public async Task PcVueNullValuesWithTruncation_IsRejected()
    {
        var handler = new TrendRecordingHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/OAuth/token", StringComparison.OrdinalIgnoreCase)) return TokenResponse();
            if (request.Method == HttpMethod.Post && path.EndsWith("/HistoricalData/v2/Trends"))
                return Text(HttpStatusCode.OK, "\"truncated-empty\"");
            if (request.Method == HttpMethod.Get && path.Contains("/Trends/truncated-empty"))
                return Json(HttpStatusCode.OK, "{\"values\":null,\"maxNumberExceeded\":true}");
            return Json(HttpStatusCode.OK, "{}");
        });

        var result = Assert.Single(await CreateTrendsService(handler).ProcessVariablesTrendsAsync(
            new List<string> { "Truncated" }, Utc(2024, 1, 1, 0), Utc(2026, 10, 1, 0), Settings()));

        Assert.False(result.Success);
        Assert.Contains("truncated", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExactlyFourThousandPoints_SplitsEvenIfPcVueFlagIsFalse()
    {
        var calls = 0;
        var capped = TrendJson(false, Enumerable.Repeat(Point("2024-06-11T10:00:00", 1), 4000).ToArray());
        var handler = new TrendRecordingHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/OAuth/token", StringComparison.OrdinalIgnoreCase)) return TokenResponse();
            if (request.Method == HttpMethod.Post && path.EndsWith("/HistoricalData/v2/Trends"))
                return Text(HttpStatusCode.OK, "\"request-cap\"");
            if (request.Method == HttpMethod.Get && path.Contains("/Trends/request-cap"))
                return Json(HttpStatusCode.OK, Interlocked.Increment(ref calls) == 1 ? capped :
                    TrendJson(false, Point("2024-06-11T10:00:00", 1)));
            return Json(HttpStatusCode.OK, "{}");
        });

        var result = Assert.Single(await CreateTrendsService(handler).ProcessVariablesTrendsAsync(
            new List<string> { "Dense" }, Utc(2024, 6, 11, 0), Utc(2024, 6, 12, 0), Settings()));

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Equal(3, calls);
        Assert.Single(result.TrendData);
    }

    [Fact]
    public async Task MaxNumberExceeded_SplitsRangeAndDeduplicatesBoundaryPoint()
    {
        var trendGets = 0;
        var handler = new TrendRecordingHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/OAuth/token", StringComparison.OrdinalIgnoreCase))
                return TokenResponse();
            if (request.Method == HttpMethod.Post && path.EndsWith("/HistoricalData/v2/Trends"))
                return Text(HttpStatusCode.OK, "\"request-split\"");
            if (request.Method == HttpMethod.Get && path.Contains("/HistoricalData/v2/Trends/request-split"))
            {
                var call = Interlocked.Increment(ref trendGets);
                return call switch
                {
                    1 => Json(HttpStatusCode.OK, TrendJson(true,
                        Point("2026-09-29T00:00:00", 1),
                        Point("2026-09-29T12:00:00", 2))),
                    2 => Json(HttpStatusCode.OK, TrendJson(false,
                        Point("2026-09-29T00:00:00", 1),
                        Point("2026-09-29T12:00:00", 2))),
                    _ => Json(HttpStatusCode.OK, TrendJson(false,
                        Point("2026-09-29T12:00:00", 2),
                        Point("2026-09-29T23:00:00", 3)))
                };
            }
            if (request.Method == HttpMethod.Delete && path.EndsWith("/HistoricalData/v2/Trends/request-split"))
                return Json(HttpStatusCode.OK, "{}");
            return Text(HttpStatusCode.NotFound, "not found");
        });
        var service = CreateTrendsService(handler);

        var result = Assert.Single(await service.ProcessVariablesTrendsAsync(
            new List<string> { "Dense.Variable" },
            Utc(2026, 9, 29, 0),
            Utc(2026, 9, 29, 23),
            Settings()));

        Assert.True(result.Success, result.ErrorMessage);
        Assert.False(result.MaxNumberExceeded);
        Assert.Equal(3, result.TrendData.Count);
        Assert.Equal(new[] { 1d, 2d, 3d }, result.TrendData.Select(point => point.Value).ToArray());
        Assert.Equal(3, trendGets);
        Assert.Equal(1, handler.Count(HttpMethod.Delete, "/HistoricalData/v2/Trends/request-split"));
    }

    [Fact]
    public async Task UnsplittableDenseRange_FailsInsteadOfSilentlyImportingPartialData()
    {
        var handler = new TrendRecordingHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/OAuth/token", StringComparison.OrdinalIgnoreCase))
                return TokenResponse();
            if (request.Method == HttpMethod.Post && path.EndsWith("/HistoricalData/v2/Trends"))
                return Text(HttpStatusCode.OK, "\"request-dense\"");
            if (request.Method == HttpMethod.Get && path.Contains("/HistoricalData/v2/Trends/request-dense"))
                return Json(HttpStatusCode.OK, TrendJson(true, Point("2026-09-29T10:00:00", 1)));
            if (request.Method == HttpMethod.Delete && path.EndsWith("/HistoricalData/v2/Trends/request-dense"))
                return Json(HttpStatusCode.OK, "{}");
            return Text(HttpStatusCode.NotFound, "not found");
        });
        var service = CreateTrendsService(handler);

        var result = Assert.Single(await service.ProcessVariablesTrendsAsync(
            new List<string> { "Very.Dense.Variable" },
            Utc(2026, 9, 29, 10, 0, 0),
            Utc(2026, 9, 29, 10, 0, 1),
            Settings()));

        Assert.False(result.Success);
        Assert.True(result.MaxNumberExceeded);
        Assert.Contains("silent data loss", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, handler.Count(HttpMethod.Delete, "/HistoricalData/v2/Trends/request-dense"));
    }

    [Fact]
    public async Task DeleteNotFound_IsAcceptedAsAlreadyReleased()
    {
        var handler = new TrendRecordingHandler(request =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/OAuth/token", StringComparison.OrdinalIgnoreCase))
                return TokenResponse();
            return Text(HttpStatusCode.NotFound, "already gone");
        });
        var service = CreateTrendsService(handler);

        Assert.True(await service.DeleteTrendRequestAsync("gone-request", Settings()));
    }

    private static TrendsService CreateTrendsService(HttpMessageHandler handler)
    {
        var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(10) };
        var webService = new PCVueWebService(
            client,
            NullLogger<PCVueWebService>.Instance);
        return new TrendsService(webService, NullLogger<TrendsService>.Instance);
    }

    private static PCVueWebServiceSettings Settings() => new()
    {
        ConnectionId = "connection-1",
        ConnectionName = "PCVue test",
        BaseUrl = "https://pcvue.test",
        ClientId = "PoWorks",
        ClientSecret = "secret",
        Username = "enms",
        Password = "password"
    };

    private static DateTime Utc(int year, int month, int day, int hour, int minute = 0, int second = 0)
        => new(year, month, day, hour, minute, second, DateTimeKind.Utc);

    private static string Point(string timestamp, double value)
        => $"{{\"value\":{value.ToString(System.Globalization.CultureInfo.InvariantCulture)},\"timestamp\":\"{timestamp}\",\"quality\":\"Good\",\"qualityValue\":0,\"properties\":null}}";

    private static string TrendJson(bool exceeded, params string[] points)
        => $"{{\"values\":[{string.Join(',', points)}],\"maxNumberExceeded\":{exceeded.ToString().ToLowerInvariant()}}}";

    private static HttpResponseMessage TokenResponse()
        => Json(HttpStatusCode.OK,
            "{\"access_token\":\"access\",\"token_type\":\"bearer\",\"expires_in\":1200,\"refresh_token\":\"refresh\"}");

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

    private sealed class TrendRecordingHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;
        private readonly ConcurrentQueue<RequestRecord> _requests = new();

        public TrendRecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        {
            _responder = responder;
        }

        public int Count(HttpMethod method, string path)
            => _requests.Count(record =>
                record.Method == method &&
                string.Equals(record.Path, path, StringComparison.OrdinalIgnoreCase));

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            _requests.Enqueue(new RequestRecord(request.Method, request.RequestUri!.AbsolutePath));
            return Task.FromResult(_responder(request));
        }

        private sealed record RequestRecord(HttpMethod Method, string Path);
    }
}
