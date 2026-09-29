using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using PoWorks_Rework.Models;
using PoWorks_Rework.Services;
using Xunit;

namespace PoWorks_Rework.Tests;

public class PCVueOAuthSessionTests
{
    [Fact]
    public async Task ConcurrentCallers_OpenOnlyOnePasswordSession()
    {
        var handler = new RecordingHandler(async request =>
        {
            var body = await ReadBody(request);
            Assert.Contains("grant_type=password", body);
            return TokenResponse("access-1", "refresh-1", 1200);
        });
        var service = CreateService(handler);
        var settings = Settings();

        var tokens = await Task.WhenAll(
            Enumerable.Range(0, 20)
                .Select(_ => service.GetValidAccessTokenAsync(settings)));

        Assert.All(tokens, token => Assert.Equal("access-1", token));
        Assert.Equal(1, handler.CountRequests(HttpMethod.Post, "/OAuth/token"));
    }

    [Fact]
    public async Task TokenLifetime_UsesPcVueExpiresInInsteadOfFourMinuteClamp()
    {
        var clock = new ManualTimeProvider(new DateTimeOffset(2026, 9, 29, 8, 0, 0, TimeSpan.Zero));
        var handler = new RecordingHandler(async request =>
        {
            var body = await ReadBody(request);
            if (body.Contains("grant_type=refresh_token", StringComparison.Ordinal))
            {
                return TokenResponse("access-2", "refresh-2", 1200);
            }
            return TokenResponse("access-1", "refresh-1", 1200);
        });
        var service = CreateService(handler, clock);
        var settings = Settings();

        Assert.Equal("access-1", await service.GetValidAccessTokenAsync(settings));

        clock.Advance(TimeSpan.FromMinutes(5));
        Assert.Equal("access-1", await service.GetValidAccessTokenAsync(settings));
        Assert.Equal(1, handler.CountRequests(HttpMethod.Post, "/OAuth/token"));

        clock.Advance(TimeSpan.FromMinutes(15));
        Assert.Equal("access-2", await service.GetValidAccessTokenAsync(settings));
        Assert.Equal(2, handler.CountRequests(HttpMethod.Post, "/OAuth/token"));
        Assert.Equal(1, handler.CountFormValue("grant_type", "refresh_token"));
    }

    [Fact]
    public async Task ForceRefresh_UsesRefreshTokenAndDoesNotOpenSecondPasswordSession()
    {
        var handler = new RecordingHandler(async request =>
        {
            var body = await ReadBody(request);
            if (body.Contains("grant_type=refresh_token", StringComparison.Ordinal))
            {
                Assert.Contains("refresh_token=refresh-1", body);
                return TokenResponse("access-2", "refresh-2", 1200);
            }

            Assert.Contains("grant_type=password", body);
            return TokenResponse("access-1", "refresh-1", 1200);
        });
        var service = CreateService(handler);
        var settings = Settings();

        Assert.Equal("access-1", await service.GetValidAccessTokenAsync(settings));
        Assert.Equal("access-2", await service.GetValidAccessTokenAsync(settings, forceRefresh: true));

        Assert.Equal(1, handler.CountFormValue("grant_type", "password"));
        Assert.Equal(1, handler.CountFormValue("grant_type", "refresh_token"));
    }

    [Fact]
    public async Task TooManyUsers_IsBackedOffAcrossConcurrentCallers()
    {
        var clock = new ManualTimeProvider(new DateTimeOffset(2026, 9, 29, 8, 0, 0, TimeSpan.Zero));
        var handler = new RecordingHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent(
                "{\"error\":\"invalid_grant\",\"error_description\":\"E_TooManyUsers\"}",
                Encoding.UTF8,
                "application/json")
        }));
        var service = CreateService(handler, clock);
        var settings = Settings();

        var firstWave = await Task.WhenAll(
            Enumerable.Range(0, 12)
                .Select(_ => service.GetValidAccessTokenAsync(settings)));

        Assert.All(firstWave, Assert.Null);
        Assert.Equal(1, handler.CountRequests(HttpMethod.Post, "/OAuth/token"));

        Assert.Null(await service.GetValidAccessTokenAsync(settings));
        Assert.Equal(1, handler.CountRequests(HttpMethod.Post, "/OAuth/token"));

        clock.Advance(TimeSpan.FromSeconds(61));
        Assert.Null(await service.GetValidAccessTokenAsync(settings));
        Assert.Equal(2, handler.CountRequests(HttpMethod.Post, "/OAuth/token"));
    }

    [Fact]
    public async Task DistinctPcVueIdentities_DoNotShareTokens()
    {
        var handler = new RecordingHandler(async request =>
        {
            var body = await ReadBody(request);
            var username = ParseForm(body)["username"];
            return username == "user-a"
                ? TokenResponse("token-a", "refresh-a", 1200)
                : TokenResponse("token-b", "refresh-b", 1200);
        });
        var service = CreateService(handler);
        var a = Settings("connection-a", "user-a");
        var b = Settings("connection-b", "user-b");

        Assert.Equal("token-a", await service.GetValidAccessTokenAsync(a));
        Assert.Equal("token-b", await service.GetValidAccessTokenAsync(b));
        Assert.Equal("token-a", await service.GetValidAccessTokenAsync(a));
        Assert.Equal("token-b", await service.GetValidAccessTokenAsync(b));

        Assert.Equal(2, handler.CountRequests(HttpMethod.Post, "/OAuth/token"));
    }

    [Fact]
    public async Task SamePcVueIdentity_ReusesSessionEvenWhenConnectionIdDiffers()
    {
        var handler = new RecordingHandler(_ => Task.FromResult(TokenResponse("shared", "refresh", 1200)));
        var service = CreateService(handler);
        var first = Settings("temporary-ui-id", "enms");
        var second = Settings("database-id", "enms");

        Assert.Equal("shared", await service.GetValidAccessTokenAsync(first));
        Assert.Equal("shared", await service.GetValidAccessTokenAsync(second));

        Assert.Equal(1, handler.CountRequests(HttpMethod.Post, "/OAuth/token"));
    }

    [Fact]
    public async Task ChangedCredentials_CloseOldSessionBeforeOpeningReplacement()
    {
        var handler = new RecordingHandler(async request =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/OAuth/Account/logout", StringComparison.OrdinalIgnoreCase))
            {
                Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
                Assert.Equal("old-access", request.Headers.Authorization?.Parameter);
                return new HttpResponseMessage(HttpStatusCode.OK);
            }

            var body = await ReadBody(request);
            var username = ParseForm(body)["username"];
            return username == "old-user"
                ? TokenResponse("old-access", "old-refresh", 1200)
                : TokenResponse("new-access", "new-refresh", 1200);
        });
        var service = CreateService(handler);
        var oldSettings = Settings("connection-1", "old-user");
        var newSettings = Settings("connection-1", "new-user");

        Assert.Equal("old-access", await service.GetValidAccessTokenAsync(oldSettings));
        Assert.Equal("new-access", await service.GetValidAccessTokenAsync(newSettings));

        Assert.Equal(1, handler.CountRequests(HttpMethod.Get, "/OAuth/Account/logout"));
        Assert.Equal(2, handler.CountFormValue("grant_type", "password"));
    }

    [Fact]
    public async Task TestConnection_ReusesOAuthSessionAndChecksHistoricalDataStatus()
    {
        var handler = new RecordingHandler(async request =>
        {
            if (request.Method == HttpMethod.Get &&
                request.RequestUri!.AbsolutePath.EndsWith("/HistoricalData/v2/Status", StringComparison.OrdinalIgnoreCase))
            {
                Assert.Equal("access-1", request.Headers.Authorization?.Parameter);
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("[]", Encoding.UTF8, "application/json")
                };
            }

            _ = await ReadBody(request);
            return TokenResponse("access-1", "refresh-1", 1200);
        });
        var service = CreateService(handler);
        var settings = Settings();

        Assert.True((await service.TestConnectionAsync(settings)).Success);
        Assert.True((await service.TestConnectionAsync(settings)).Success);

        Assert.Equal(1, handler.CountRequests(HttpMethod.Post, "/OAuth/token"));
        Assert.Equal(2, handler.CountRequests(HttpMethod.Get, "/HistoricalData/v2/Status"));
    }

    [Fact]
    public async Task Logout_ReleasesServerSessionAndNextUseAuthenticatesAgain()
    {
        var tokenNumber = 0;
        var handler = new RecordingHandler(request =>
        {
            if (request.Method == HttpMethod.Get &&
                request.RequestUri!.AbsolutePath.EndsWith("/OAuth/Account/logout", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
            }

            tokenNumber++;
            return Task.FromResult(TokenResponse("access-" + tokenNumber, "refresh-" + tokenNumber, 1200));
        });
        var service = CreateService(handler);
        var settings = Settings();

        Assert.Equal("access-1", await service.GetValidAccessTokenAsync(settings));
        Assert.True(await service.LogoutAsync(settings));
        Assert.Equal("access-2", await service.GetValidAccessTokenAsync(settings));

        Assert.Equal(1, handler.CountRequests(HttpMethod.Get, "/OAuth/Account/logout"));
        Assert.Equal(2, handler.CountRequests(HttpMethod.Post, "/OAuth/token"));
    }

    private static PCVueWebService CreateService(
        HttpMessageHandler handler,
        TimeProvider? timeProvider = null)
    {
        var client = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(5)
        };
        return new PCVueWebService(
            client,
            NullLogger<PCVueWebService>.Instance,
            timeProvider);
    }

    private static PCVueWebServiceSettings Settings(
        string connectionId = "connection-1",
        string username = "enms") => new()
        {
            ConnectionId = connectionId,
            ConnectionName = "PCVue test",
            BaseUrl = "https://pcvue.test",
            ClientId = "PoWorks",
            ClientSecret = "secret",
            Username = username,
            Password = "password"
        };

    private static HttpResponseMessage TokenResponse(
        string access,
        string refresh,
        int expiresIn) => new(HttpStatusCode.OK)
        {
            Content = new StringContent(
                $"{{\"access_token\":\"{access}\",\"token_type\":\"bearer\",\"expires_in\":{expiresIn},\"refresh_token\":\"{refresh}\"}}",
                Encoding.UTF8,
                "application/json")
        };

    private static async Task<string> ReadBody(HttpRequestMessage request)
        => request.Content == null
            ? string.Empty
            : await request.Content.ReadAsStringAsync();

    private static Dictionary<string, string> ParseForm(string body)
        => body.Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Split('=', 2))
            .ToDictionary(
                pair => Uri.UnescapeDataString(pair[0].Replace('+', ' ')),
                pair => Uri.UnescapeDataString((pair.Length > 1 ? pair[1] : string.Empty).Replace('+', ' ')),
                StringComparer.OrdinalIgnoreCase);

    private sealed class ManualTimeProvider : TimeProvider
    {
        private DateTimeOffset _utcNow;

        public ManualTimeProvider(DateTimeOffset utcNow)
        {
            _utcNow = utcNow;
        }

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void Advance(TimeSpan value)
        {
            _utcNow = _utcNow.Add(value);
        }
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, Task<HttpResponseMessage>> _responder;
        private readonly object _sync = new();
        private readonly List<RequestRecord> _requests = new();

        public RecordingHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> responder)
        {
            _responder = responder;
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var body = await ReadBody(request);
            lock (_sync)
            {
                _requests.Add(new RequestRecord(
                    request.Method,
                    request.RequestUri?.AbsolutePath ?? string.Empty,
                    body));
            }

            return await _responder(request);
        }

        public int CountRequests(HttpMethod method, string path)
        {
            lock (_sync)
            {
                return _requests.Count(r =>
                    r.Method == method &&
                    r.Path.Equals(path, StringComparison.OrdinalIgnoreCase));
            }
        }

        public int CountFormValue(string name, string value)
        {
            lock (_sync)
            {
                return _requests.Count(r =>
                {
                    if (string.IsNullOrWhiteSpace(r.Body)) return false;
                    var form = ParseForm(r.Body);
                    return form.TryGetValue(name, out var actual) &&
                           string.Equals(actual, value, StringComparison.OrdinalIgnoreCase);
                });
            }
        }
    }

    private sealed record RequestRecord(HttpMethod Method, string Path, string Body);
}
