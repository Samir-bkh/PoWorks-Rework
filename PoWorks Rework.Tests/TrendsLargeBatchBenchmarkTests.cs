using System.Diagnostics;
using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using PoWorks_Rework.Models;
using PoWorks_Rework.Services;
using Xunit;
using Xunit.Abstractions;

namespace PoWorks_Rework.Tests;

/// <summary>
/// This exercises the real legacy TrendsService over deterministic, simulated
/// PCVue HTTP responses. It measures scheduling and HTTP pipeline overhead;
/// it is NOT a benchmark of an actual PCVue server or PostgreSQL ingestion.
/// CI always runs 2,000 variables. Set POWORKS_STRESS_TRENDS_50K=1 to run
/// the same test with 50,000 variables in a dedicated stress-test environment.
/// </summary>
public sealed class TrendsLargeBatchBenchmarkTests
{
    private readonly ITestOutputHelper _output;

    public TrendsLargeBatchBenchmarkTests(ITestOutputHelper output)
        => _output = output;

    [Fact]
    public async Task ThousandsOfVariables_PreserveOrderingConcurrencyAndRequestCleanup()
    {
        var variableCount = Environment.GetEnvironmentVariable("POWORKS_STRESS_TRENDS_50K") == "1"
            ? 50_000
            : 2_000;
        const int concurrency = 8;
        using var handler = new FastPcVueHandler();
        using var client = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(30)
        };
        var pcvue = new PCVueWebService(client, NullLogger<PCVueWebService>.Instance);
        var trends = new TrendsService(
            pcvue,
            NullLogger<TrendsService>.Instance,
            concurrency);
        var variables = Enumerable.Range(0, variableCount)
            .Select(i => $"Building.Level01.Energy.{i:D5}")
            .ToList();
        var start = new DateTime(2026, 9, 29, 9, 0, 0, DateTimeKind.Utc);
        var end = start.AddHours(1);
        var watch = Stopwatch.StartNew();

        var results = await trends.ProcessVariablesTrendsAsync(
            variables,
            start,
            end,
            new PCVueWebServiceSettings
            {
                ConnectionId = "benchmark-connection",
                ConnectionName = "Deterministic PCVue simulator",
                BaseUrl = "https://pcvue.test",
                ClientId = "PoWorks",
                ClientSecret = "fake-secret",
                Username = "enms",
                Password = "fake-password"
            });

        watch.Stop();
        _output.WriteLine(
            "Simulated PCVue legacy Trends: {0:N0} variables in {1:F3}s ({2:F1} variables/s), concurrency peak {3}.",
            variableCount,
            watch.Elapsed.TotalSeconds,
            variableCount / Math.Max(watch.Elapsed.TotalSeconds, 0.001),
            handler.PeakHistoricalRequests);

        Assert.Equal(variableCount, results.Count);
        Assert.All(results, result =>
        {
            Assert.True(result.Success, result.ErrorMessage);
            Assert.Empty(result.TrendData);
            Assert.False(result.MaxNumberExceeded);
        });
        for (var i = 0; i < results.Count; i++)
            Assert.Equal(variables[i], results[i].VariableName);

        Assert.InRange(handler.PeakHistoricalRequests, 2, concurrency);
        Assert.Equal(variableCount, handler.CreatedCount);
        Assert.Equal(variableCount, handler.ReadCount);
        Assert.Equal(variableCount, handler.DeletedCount);
        Assert.Equal(1, handler.PasswordGrantCount);
        Assert.Equal(0, handler.RefreshGrantCount);
    }

    private sealed class FastPcVueHandler : HttpMessageHandler
    {
        private int _nextId;
        private int _active;
        private int _peak;
        private int _created;
        private int _read;
        private int _deleted;
        private int _password;
        private int _refresh;

        public int PeakHistoricalRequests => Volatile.Read(ref _peak);
        public int CreatedCount => Volatile.Read(ref _created);
        public int ReadCount => Volatile.Read(ref _read);
        public int DeletedCount => Volatile.Read(ref _deleted);
        public int PasswordGrantCount => Volatile.Read(ref _password);
        public int RefreshGrantCount => Volatile.Read(ref _refresh);

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/OAuth/token", StringComparison.OrdinalIgnoreCase))
            {
                var body = request.Content == null
                    ? string.Empty
                    : await request.Content.ReadAsStringAsync(cancellationToken);
                if (body.Contains("grant_type=refresh_token", StringComparison.Ordinal))
                    Interlocked.Increment(ref _refresh);
                else if (body.Contains("grant_type=password", StringComparison.Ordinal))
                    Interlocked.Increment(ref _password);

                return Response(HttpStatusCode.OK,
                    "{\"access_token\":\"simulated-token\",\"token_type\":\"bearer\",\"expires_in\":1200,\"refresh_token\":\"simulated-refresh\"}");
            }

            if (!path.StartsWith("/HistoricalData/v2/Trends", StringComparison.OrdinalIgnoreCase))
                return Response(HttpStatusCode.NotFound, "not found");

            var active = Interlocked.Increment(ref _active);
            RecordPeak(active);
            try
            {
                // A small asynchronous response delay creates overlapping requests
                // so this detects accidental unbounded concurrency.
                await Task.Delay(1, cancellationToken);
                if (request.Method == HttpMethod.Post &&
                    path.Equals("/HistoricalData/v2/Trends", StringComparison.OrdinalIgnoreCase))
                {
                    Interlocked.Increment(ref _created);
                    return Response(HttpStatusCode.OK,
                        $"\"request-{Interlocked.Increment(ref _nextId)}\"");
                }

                if (request.Method == HttpMethod.Get)
                {
                    Interlocked.Increment(ref _read);
                    return Response(HttpStatusCode.OK,
                        "{\"values\":[],\"maxNumberExceeded\":false}");
                }

                if (request.Method == HttpMethod.Delete)
                {
                    Interlocked.Increment(ref _deleted);
                    return Response(HttpStatusCode.OK, "{}");
                }

                return Response(HttpStatusCode.MethodNotAllowed, "unexpected method");
            }
            finally
            {
                Interlocked.Decrement(ref _active);
            }
        }

        private void RecordPeak(int candidate)
        {
            while (true)
            {
                var current = Volatile.Read(ref _peak);
                if (candidate <= current) return;
                if (Interlocked.CompareExchange(ref _peak, candidate, current) == current)
                    return;
            }
        }

        private static HttpResponseMessage Response(HttpStatusCode status, string body)
            => new(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };
    }
}
