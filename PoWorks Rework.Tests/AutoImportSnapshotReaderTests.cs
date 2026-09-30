using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using PoWorks_Rework.Models;
using PoWorks_Rework.Services;
using Xunit;

namespace PoWorks_Rework.Tests;

public class AutoImportSnapshotReaderTests
{
    [Fact]
    public async Task UnchangedPcVueValue_ProducesOneNewReadingAtEachTwoMinutePoll()
    {
        var clock = new ManualClock(new DateTimeOffset(2026, 9, 30, 10, 0, 0, TimeSpan.Zero));
        var bulkCalls = 0;
        using var handler = new PcVueHandler(request =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/OAuth/token"))
                return TokenResponse();
            Assert.Equal("/RealTimeData/v2/BulkRead", request.RequestUri.AbsolutePath);
            bulkCalls++;
            return Json("""
                {"Building.Water":{
                    "result":{"code":{"value":1,"label":"S_Ok"}},
                    "value":VALUE,"Timestamp":"2026-09-30T09:00:00",
                    "quality":"Good","QualityValue":192}}
                """.Replace("VALUE", bulkCalls < 3 ? "600" : "601"));
        });
        using var client = new HttpClient(handler);
        var reader = new AutoImportSnapshotReader(
            new PCVueWebService(client, NullLogger<PCVueWebService>.Instance, clock), clock);
        var meters = Meters("Building.Water");

        var first = Assert.Single(await reader.ReadAsync(Settings(), meters));
        clock.Advance(TimeSpan.FromMinutes(2));
        var second = Assert.Single(await reader.ReadAsync(Settings(), meters));
        clock.Advance(TimeSpan.FromMinutes(2));
        var third = Assert.Single(await reader.ReadAsync(Settings(), meters));

        Assert.Equal(3, bulkCalls);
        Assert.Equal(600m, first.Value);
        Assert.Equal(600m, second.Value);
        Assert.Equal(601m, third.Value);
        Assert.Equal(192, second.Quality);
        Assert.Equal(new DateTime(2026, 9, 30, 10, 0, 0), first.Timestamp);
        Assert.Equal(first.Timestamp.AddMinutes(2), second.Timestamp);
        Assert.Equal(second.Timestamp.AddMinutes(2), third.Timestamp);
    }

    [Fact]
    public async Task FailedBadAndNonNumericValues_DoNotBecomeMeterReadings()
    {
        using var handler = new PcVueHandler(request =>
            request.RequestUri!.AbsolutePath.EndsWith("/OAuth/token")
                ? TokenResponse()
                : Json("""
                    {
                      "GoodZero":{"result":{"code":{"value":1}},"value":0,"quality":"Good","QualityValue":192},
                      "GoodString":{"result":{"code":{"value":1}},"value":"12.5","quality":"Good","QualityValue":192},
                      "Bad":{"result":{"code":{"value":1}},"value":900,"quality":"Bad","QualityValue":0},
                      "Failed":{"result":{"code":{"value":0}},"value":900,"quality":"Good","QualityValue":192},
                      "Text":{"result":{"code":{"value":1}},"value":"not-a-number","quality":"Good","QualityValue":192},
                      "Null":{"result":null,"value":100,"quality":"Good","QualityValue":192}
                    }
                    """));
        using var client = new HttpClient(handler);
        var reader = new AutoImportSnapshotReader(
            new PCVueWebService(client, NullLogger<PCVueWebService>.Instance));

        var snapshots = await reader.ReadAsync(Settings(), Meters(
            "GoodZero", "GoodString", "Bad", "Failed", "Text", "Null", "Missing"));

        Assert.Equal(2, snapshots.Count);
        Assert.Equal((1, 0m), (snapshots[0].MeterId, snapshots[0].Value));
        Assert.Equal((2, 12.5m), (snapshots[1].MeterId, snapshots[1].Value));
    }

    [Fact]
    public async Task LargeMeterSet_UsesBoundedBulkReadBatchesWithOneValuePerMeter()
    {
        const int meterCount = 2_001;
        var bulkCalls = 0;
        var tokenCalls = 0;
        using var handler = new PcVueHandler(async request =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/OAuth/token"))
            {
                tokenCalls++;
                return TokenResponse();
            }

            Assert.Equal("/RealTimeData/v2/BulkRead", request.RequestUri.AbsolutePath);
            using var payload = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            var names = payload.RootElement.GetProperty("Variables").EnumerateArray()
                .Select(v => v.GetString()!).ToArray();
            Assert.InRange(names.Length, 1, AutoImportSnapshotReader.BatchSize);
            bulkCalls++;
            var values = names.ToDictionary(name => name, _ => new
            {
                result = new { code = new { value = 1 } },
                value = 42,
                quality = "Good",
                QualityValue = 192
            });
            return Json(JsonSerializer.Serialize(values));
        });
        using var client = new HttpClient(handler);
        var reader = new AutoImportSnapshotReader(
            new PCVueWebService(client, NullLogger<PCVueWebService>.Instance));
        var meters = Enumerable.Range(1, meterCount)
            .Select(id => new MeterForTrendsAnalysis
            {
                MeterId = id,
                OriginalVariableName = $"Building.Variable.{id:D5}"
            }).ToArray();

        var snapshots = await reader.ReadAsync(Settings(), meters);

        Assert.Equal(9, bulkCalls);
        Assert.Equal(1, tokenCalls);
        Assert.Equal(meterCount, snapshots.Count);
        Assert.Equal(meterCount, snapshots.Select(s => s.MeterId).Distinct().Count());
        Assert.All(snapshots, reading => Assert.Equal(42m, reading.Value));
    }

    [Fact]
    public async Task FailedBatch_DoesNotReturnPartialOrInventedReadings()
    {
        var calls = 0;
        using var handler = new PcVueHandler(async request =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/OAuth/token"))
                return TokenResponse();

            calls++;
            if (calls == 2)
                return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                {
                    Content = new StringContent("PCVue unavailable")
                };

            using var payload = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            var values = payload.RootElement.GetProperty("Variables").EnumerateArray()
                .Select(v => v.GetString()!)
                .ToDictionary(name => name, _ => new
                {
                    result = new { code = new { value = 1 } },
                    value = 5,
                    quality = "Good",
                    QualityValue = 192
                });
            return Json(JsonSerializer.Serialize(values));
        });
        using var client = new HttpClient(handler);
        var reader = new AutoImportSnapshotReader(
            new PCVueWebService(client, NullLogger<PCVueWebService>.Instance));
        var meters = Enumerable.Range(1, AutoImportSnapshotReader.BatchSize + 1)
            .Select(id => new MeterForTrendsAnalysis
            {
                MeterId = id,
                OriginalVariableName = $"Variable.{id}"
            }).ToArray();

        await Assert.ThrowsAsync<InvalidOperationException>(() => reader.ReadAsync(Settings(), meters));
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task ExpiredToken_RefreshesOnceAndReadsCurrentValue()
    {
        var tokenCalls = 0;
        var bulkCalls = 0;
        using var handler = new PcVueHandler(async request =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/OAuth/token"))
            {
                tokenCalls++;
                var body = await request.Content!.ReadAsStringAsync();
                return TokenResponse(body.Contains("grant_type=refresh_token", StringComparison.Ordinal)
                    ? "refreshed" : "expired");
            }

            bulkCalls++;
            if (request.Headers.Authorization?.Parameter == "expired")
                return new HttpResponseMessage(HttpStatusCode.Unauthorized);

            Assert.Equal("refreshed", request.Headers.Authorization?.Parameter);
            return Json("""
                {"Building.Water":{"result":{"code":{"value":1}},
                "value":600,"quality":"Good","QualityValue":192}}
                """);
        });
        using var client = new HttpClient(handler);
        var reader = new AutoImportSnapshotReader(
            new PCVueWebService(client, NullLogger<PCVueWebService>.Instance));

        var reading = Assert.Single(await reader.ReadAsync(Settings(), Meters("Building.Water")));

        Assert.Equal(600m, reading.Value);
        Assert.Equal(2, tokenCalls);
        Assert.Equal(2, bulkCalls);
    }

    private static MeterForTrendsAnalysis[] Meters(params string[] names) =>
        names.Select((name, index) => new MeterForTrendsAnalysis
        {
            MeterId = index + 1,
            OriginalVariableName = name
        }).ToArray();

    private static PCVueWebServiceSettings Settings() => new()
    {
        ConnectionId = "pcvue-1",
        BaseUrl = "https://pcvue.test",
        ClientId = "PoWorks",
        ClientSecret = "secret",
        Username = "enms",
        Password = "password"
    };

    private static HttpResponseMessage TokenResponse(string access = "access") => Json(
        $$"""{"access_token":"{{access}}","refresh_token":"refresh","expires_in":1200,"token_type":"bearer"}""");

    private static HttpResponseMessage Json(string content) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(content, Encoding.UTF8, "application/json")
    };

    private sealed class PcVueHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, Task<HttpResponseMessage>> _respond;

        public PcVueHandler(Func<HttpRequestMessage, HttpResponseMessage> respond)
            : this(request => Task.FromResult(respond(request))) { }

        public PcVueHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond)
            => _respond = respond;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) => _respond(request);
    }

    private sealed class ManualClock : TimeProvider
    {
        private DateTimeOffset _now;
        public ManualClock(DateTimeOffset now) => _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
        public void Advance(TimeSpan duration) => _now = _now.Add(duration);
    }
}
