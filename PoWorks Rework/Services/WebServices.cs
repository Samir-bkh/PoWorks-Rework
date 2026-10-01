using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using PoWorks_Rework.Models;

namespace PoWorks_Rework.Services
{
    /// <summary>
    /// Central client for PCVue REST Web Services.
    ///
    /// One OAuth session is reused for one server/client/user identity, even when the
    /// same connection is reached from the settings page, the manual import path and
    /// the background worker with different local connection IDs. This matters because
    /// PCVue licenses Web Services sessions and opening unnecessary password-grant
    /// sessions can exhaust the available connection count.
    /// </summary>
    public class PCVueWebService
    {
        private static readonly TimeSpan TooManyUsersBackoff = TimeSpan.FromSeconds(60);
        private static readonly TimeSpan TransientAuthenticationBackoff = TimeSpan.FromSeconds(5);

        private readonly HttpClient _httpClient;
        private readonly ILogger<PCVueWebService> _logger;
        private readonly TimeProvider _timeProvider;
        private readonly ConcurrentDictionary<string, PCVueSessionState> _sessions = new(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<string, string> _connectionAliases = new(StringComparer.Ordinal);

        public HttpClient HttpClient => _httpClient;

        public PCVueWebService(
            HttpClient httpClient,
            ILogger<PCVueWebService> logger,
            TimeProvider? timeProvider = null)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _timeProvider = timeProvider ?? TimeProvider.System;
        }

        public async Task<string?> GetValidAccessTokenAsync(
            PCVueWebServiceSettings settings,
            bool forceRefresh = false,
            CancellationToken cancellationToken = default)
        {
            var response = await AcquireTokenAsync(settings, forceRefresh, cancellationToken);
            return response.Success ? response.AccessToken : null;
        }

        public Task<OAuthTokenResponse> GetAccessTokenAsync(PCVueWebServiceSettings settings)
            => AcquireTokenAsync(settings, forceRefresh: false);

        public Task<OAuthTokenResponse> RefreshAccessTokenAsync(PCVueWebServiceSettings settings)
            => AcquireTokenAsync(settings, forceRefresh: true);

        private async Task<OAuthTokenResponse> AcquireTokenAsync(
            PCVueWebServiceSettings settings,
            bool forceRefresh,
            CancellationToken cancellationToken = default)
        {
            var validation = ValidateSettings(settings);
            if (!validation.IsValid)
            {
                return Failed(validation.ErrorMessage);
            }

            var sessionKey = BuildSessionKey(settings);
            await RebindConnectionAliasAsync(settings, sessionKey);

            var fingerprint = BuildConfigurationFingerprint(settings);
            var state = _sessions.GetOrAdd(sessionKey, _ => new PCVueSessionState());
            var now = _timeProvider.GetUtcNow();

            if (!forceRefresh &&
                state.ConfigurationFingerprint == fingerprint &&
                IsAccessTokenUsable(state, now))
            {
                return CachedResponse(state);
            }

            await state.Gate.WaitAsync(cancellationToken);
            try
            {
                now = _timeProvider.GetUtcNow();

                if (state.ConfigurationFingerprint != fingerprint)
                {
                    await TryLogoutStateAsync(state, CancellationToken.None);
                    state.ResetForConfiguration(fingerprint, NormalizeBaseUrl(settings.BaseUrl));
                }

                if (!forceRefresh && IsAccessTokenUsable(state, now))
                {
                    return CachedResponse(state);
                }

                if (now < state.RetryAfterUtc)
                {
                    return Failed(state.LastError ??
                        "PCVue authentication is temporarily paused after a recent failure.",
                        state.LastFailureWasTooManyUsers);
                }

                if (!string.IsNullOrWhiteSpace(state.RefreshToken))
                {
                    var refreshAttempt = await RequestRefreshTokenAsync(settings, state, cancellationToken);
                    if (refreshAttempt.Success)
                    {
                        return refreshAttempt.Response;
                    }

                    if (refreshAttempt.IsTooManyUsers)
                    {
                        ApplyAuthenticationBackoff(
                            state,
                            refreshAttempt.Response.ErrorMessage,
                            TooManyUsersBackoff,
                            tooManyUsers: true);
                        return refreshAttempt.Response;
                    }

                    if (!refreshAttempt.CanFallbackToPasswordGrant)
                    {
                        ApplyAuthenticationBackoff(
                            state,
                            refreshAttempt.Response.ErrorMessage,
                            TransientAuthenticationBackoff,
                            tooManyUsers: false);
                        return refreshAttempt.Response;
                    }

                    // Refresh told us that the server-side session is no longer valid.
                    // Only now is a replacement password-grant session justified.
                    state.ClearTokens();
                }

                var passwordAttempt = await RequestPasswordTokenAsync(settings, state, cancellationToken);
                if (!passwordAttempt.Success)
                {
                    ApplyAuthenticationBackoff(
                        state,
                        passwordAttempt.ErrorMessage,
                        passwordAttempt.IsTooManyUsers
                            ? TooManyUsersBackoff
                            : TransientAuthenticationBackoff,
                        passwordAttempt.IsTooManyUsers);
                }

                return passwordAttempt;
            }
            finally
            {
                state.Gate.Release();
            }
        }

        /// <summary>
        /// When a saved connection changes endpoint or user, release the session that
        /// was previously associated with that local connection ID before binding the
        /// ID to the new OAuth identity.
        /// </summary>
        private async Task RebindConnectionAliasAsync(
            PCVueWebServiceSettings settings,
            string sessionKey)
        {
            // SettingsController's temporary token-test object historically did not
            // copy ConnectionName, so don't create aliases for those ephemeral objects.
            // They still reuse the same identity-based sessionKey.
            if (string.IsNullOrWhiteSpace(settings.ConnectionId) ||
                string.IsNullOrWhiteSpace(settings.ConnectionName))
            {
                return;
            }

            var alias = "id:" + settings.ConnectionId.Trim();
            if (_connectionAliases.TryGetValue(alias, out var previousKey) &&
                !string.Equals(previousKey, sessionKey, StringComparison.Ordinal))
            {
                await LogoutBySessionKeyAsync(previousKey);
            }

            _connectionAliases[alias] = sessionKey;
        }

        private async Task<OAuthTokenResponse> RequestPasswordTokenAsync(
            PCVueWebServiceSettings settings,
            PCVueSessionState state,
            CancellationToken cancellationToken)
        {
            var endpoint = $"{NormalizeBaseUrl(settings.BaseUrl)}/OAuth/token";
            _logger.LogInformation(
                "Opening PCVue OAuth session for {Connection} at {Endpoint}",
                SafeConnectionLabel(settings),
                endpoint);

            var form = new Dictionary<string, string>
            {
                ["username"] = settings.Username,
                ["password"] = settings.Password,
                ["grant_type"] = "password",
                ["client_id"] = settings.ClientId,
                ["client_secret"] = settings.ClientSecret,
                ["scope"] = "RealtimeData RealtimeAlarm HistoricalData GraphicalData"
            };

            var attempt = await SendTokenRequestAsync(endpoint, form, cancellationToken);
            if (!attempt.Success)
            {
                LogAuthenticationFailure(settings, attempt, isRefresh: false);
                return attempt.Response;
            }

            ApplySuccessfulToken(state, attempt.Response, preserveExistingRefreshToken: false);
            _logger.LogInformation(
                "PCVue OAuth session ready for {Connection}; access token lifetime {ExpiresIn}s.",
                SafeConnectionLabel(settings),
                attempt.Response.ExpiresIn);
            return attempt.Response;
        }

        private async Task<TokenAttempt> RequestRefreshTokenAsync(
            PCVueWebServiceSettings settings,
            PCVueSessionState state,
            CancellationToken cancellationToken)
        {
            var endpoint = $"{NormalizeBaseUrl(settings.BaseUrl)}/OAuth/token";
            var form = new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = state.RefreshToken!,
                ["client_id"] = settings.ClientId,
                ["client_secret"] = settings.ClientSecret
            };

            _logger.LogDebug(
                "Refreshing PCVue OAuth session for {Connection}.",
                SafeConnectionLabel(settings));

            var attempt = await SendTokenRequestAsync(endpoint, form, cancellationToken);
            if (attempt.Success)
            {
                ApplySuccessfulToken(state, attempt.Response, preserveExistingRefreshToken: true);
                return attempt with { Response = CachedResponse(state) };
            }

            LogAuthenticationFailure(settings, attempt, isRefresh: true);
            return attempt;
        }

        private async Task<TokenAttempt> SendTokenRequestAsync(
            string endpoint,
            Dictionary<string, string> form,
            CancellationToken cancellationToken)
        {
            try
            {
                using var content = new FormUrlEncodedContent(form);
                using var response = await _httpClient.PostAsync(endpoint, content, cancellationToken);
                var raw = await response.Content.ReadAsStringAsync(cancellationToken);

                if (response.IsSuccessStatusCode)
                {
                    if (string.IsNullOrWhiteSpace(raw))
                    {
                        return TokenAttempt.Failure(
                            response.StatusCode,
                            "PCVue returned an empty OAuth response.");
                    }

                    try
                    {
                        var token = JsonSerializer.Deserialize<OAuthTokenResponse>(
                            raw,
                            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                        if (token == null || string.IsNullOrWhiteSpace(token.AccessToken))
                        {
                            return TokenAttempt.Failure(
                                response.StatusCode,
                                "PCVue OAuth response does not contain access_token.");
                        }

                        token.Success = true;
                        return TokenAttempt.Successful(response.StatusCode, token);
                    }
                    catch (JsonException ex)
                    {
                        _logger.LogWarning(ex, "Unable to parse PCVue OAuth response.");
                        return TokenAttempt.Failure(
                            response.StatusCode,
                            "Unable to parse PCVue OAuth response.");
                    }
                }

                var oauthError = ParseOAuthError(raw);
                var serverCode = oauthError?.ErrorDescription;
                var tooManyUsers = string.Equals(
                    serverCode,
                    "E_TooManyUsers",
                    StringComparison.OrdinalIgnoreCase);

                var mayOpenReplacementSession =
                    !tooManyUsers &&
                    (response.StatusCode == HttpStatusCode.Unauthorized ||
                     string.Equals(serverCode, "E_InvalidSessionId", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(oauthError?.Error, "invalid_grant", StringComparison.OrdinalIgnoreCase));

                var message = tooManyUsers
                    ? "PCVue Web Services connection limit reached (E_TooManyUsers). " +
                      "PoWorks paused new authentication attempts for 60 seconds."
                    : BuildOAuthErrorMessage(response.StatusCode, oauthError);

                return TokenAttempt.Failure(
                    response.StatusCode,
                    message,
                    tooManyUsers,
                    mayOpenReplacementSession);
            }
            catch (HttpRequestException ex)
            {
                _logger.LogWarning(ex, "Network error while contacting PCVue OAuth endpoint {Endpoint}.", endpoint);
                return TokenAttempt.Failure(
                    null,
                    $"Network error while contacting PCVue OAuth: {ex.Message}");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (TaskCanceledException ex)
            {
                _logger.LogWarning(ex, "Timeout while contacting PCVue OAuth endpoint {Endpoint}.", endpoint);
                return TokenAttempt.Failure(null, "Timeout while contacting PCVue OAuth.");
            }
        }

        private void ApplySuccessfulToken(
            PCVueSessionState state,
            OAuthTokenResponse response,
            bool preserveExistingRefreshToken)
        {
            var expiresIn = response.ExpiresIn > 0 ? response.ExpiresIn : 1200;
            var safetyMarginSeconds = Math.Clamp(expiresIn / 10, 5, 30);
            var usableLifetimeSeconds = Math.Max(1, expiresIn - safetyMarginSeconds);

            state.AccessToken = response.AccessToken;

            if (!preserveExistingRefreshToken || !string.IsNullOrWhiteSpace(response.RefreshToken))
            {
                if (!string.IsNullOrWhiteSpace(response.RefreshToken))
                {
                    state.RefreshToken = response.RefreshToken;
                }
            }

            state.TokenType = string.IsNullOrWhiteSpace(response.TokenType)
                ? "Bearer"
                : response.TokenType;
            state.OriginalExpiresIn = expiresIn;
            state.AccessTokenExpiresAtUtc = _timeProvider.GetUtcNow().AddSeconds(usableLifetimeSeconds);
            state.RetryAfterUtc = DateTimeOffset.MinValue;
            state.LastError = null;
            state.LastFailureWasTooManyUsers = false;

            response.ExpiresIn = expiresIn;
            response.RefreshToken = state.RefreshToken;
            response.Success = true;
        }

        private static bool IsAccessTokenUsable(
            PCVueSessionState state,
            DateTimeOffset now)
            => !string.IsNullOrWhiteSpace(state.AccessToken) &&
               now < state.AccessTokenExpiresAtUtc;

        private static OAuthTokenResponse CachedResponse(PCVueSessionState state)
            => new()
            {
                Success = true,
                AccessToken = state.AccessToken ?? string.Empty,
                RefreshToken = state.RefreshToken,
                TokenType = state.TokenType,
                ExpiresIn = state.OriginalExpiresIn
            };

        private void ApplyAuthenticationBackoff(
            PCVueSessionState state,
            string? message,
            TimeSpan delay,
            bool tooManyUsers)
        {
            state.LastError = message;
            state.LastFailureWasTooManyUsers = tooManyUsers;
            state.RetryAfterUtc = _timeProvider.GetUtcNow().Add(delay);
        }

        private void LogAuthenticationFailure(
            PCVueWebServiceSettings settings,
            TokenAttempt attempt,
            bool isRefresh)
        {
            if (attempt.IsTooManyUsers)
            {
                _logger.LogWarning(
                    "PCVue rejected {Grant} authentication for {Connection}: E_TooManyUsers. " +
                    "Authentication storm protection is active.",
                    isRefresh ? "refresh-token" : "password",
                    SafeConnectionLabel(settings));
                return;
            }

            _logger.LogWarning(
                "PCVue rejected {Grant} authentication for {Connection}. HTTP {Status}: {Error}",
                isRefresh ? "refresh-token" : "password",
                SafeConnectionLabel(settings),
                attempt.StatusCode,
                attempt.Response.ErrorMessage);
        }

        /// <summary>
        /// Tests both the OAuth session and the HistoricalData service. Repeated tests
        /// reuse the existing OAuth session instead of consuming another PCVue slot.
        /// </summary>
        public async Task<WebServiceTestResult> TestConnectionAsync(PCVueWebServiceSettings settings)
        {
            var token = await AcquireTokenAsync(settings, forceRefresh: false);
            if (!token.Success)
            {
                return new WebServiceTestResult
                {
                    Success = false,
                    ErrorMessage = token.ErrorMessage,
                    TokenInfo = token.IsTooManyUsers ? "E_TooManyUsers" : null
                };
            }

            var endpoint = $"{NormalizeBaseUrl(settings.BaseUrl)}/HistoricalData/v2/Status";
            var statusCode = await GetBearerStatusCodeAsync(endpoint, token.AccessToken);

            if (statusCode == HttpStatusCode.Unauthorized)
            {
                var refreshed = await AcquireTokenAsync(settings, forceRefresh: true);
                if (!refreshed.Success)
                {
                    return new WebServiceTestResult
                    {
                        Success = false,
                        ErrorMessage = refreshed.ErrorMessage
                    };
                }

                statusCode = await GetBearerStatusCodeAsync(endpoint, refreshed.AccessToken);
            }

            if ((int)statusCode < 200 || (int)statusCode > 299)
            {
                return new WebServiceTestResult
                {
                    Success = false,
                    ErrorMessage = $"PCVue HistoricalData status failed: HTTP {(int)statusCode} ({statusCode})."
                };
            }

            return new WebServiceTestResult
            {
                Success = true,
                Message = "PCVue OAuth session and HistoricalData service are available.",
                TokenInfo = $"expires_in={token.ExpiresIn}"
            };
        }

        private async Task<HttpStatusCode> GetBearerStatusCodeAsync(string endpoint, string token)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var response = await _httpClient.SendAsync(request);
            return response.StatusCode;
        }

        public async Task<bool> LogoutAsync(PCVueWebServiceSettings settings)
            => await LogoutBySessionKeyAsync(BuildSessionKey(settings));

        public async Task<bool> LogoutConnectionAsync(string connectionId)
        {
            if (string.IsNullOrWhiteSpace(connectionId)) return true;

            var alias = "id:" + connectionId.Trim();
            if (!_connectionAliases.TryRemove(alias, out var sessionKey))
            {
                return true;
            }

            return await LogoutBySessionKeyAsync(sessionKey);
        }

        private async Task<bool> LogoutBySessionKeyAsync(string sessionKey)
        {
            if (!_sessions.TryGetValue(sessionKey, out var state)) return true;

            await state.Gate.WaitAsync();
            try
            {
                var success = await TryLogoutStateAsync(state, CancellationToken.None);
                state.ClearTokens();
                _sessions.TryRemove(sessionKey, out _);
                return success;
            }
            finally
            {
                state.Gate.Release();
            }
        }

        private async Task<bool> TryLogoutStateAsync(
            PCVueSessionState state,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(state.AccessToken) ||
                string.IsNullOrWhiteSpace(state.BaseUrl))
            {
                return true;
            }

            var endpoint = $"{state.BaseUrl}/OAuth/Account/logout";
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", state.AccessToken);
                using var response = await _httpClient.SendAsync(request, cancellationToken);

                // If the session is already invalid, the intended end state is reached.
                return response.IsSuccessStatusCode ||
                       response.StatusCode == HttpStatusCode.Unauthorized;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                _logger.LogDebug(ex, "Unable to close PCVue OAuth session at {Endpoint}.", endpoint);
                return false;
            }
        }

        public void ClearTokens()
        {
            foreach (var state in _sessions.Values)
            {
                state.ClearTokens();
            }
        }

        public void ClearToken() => ClearTokens();

        public async Task<string> BulkReadVariablesAsync(
            PCVueWebServiceSettings settings,
            string[] variables,
            string[]? properties = null,
            CancellationToken cancellationToken = default)
        {
            var endpoint = $"{NormalizeBaseUrl(settings.BaseUrl)}/RealtimeData/v2/BulkRead";
            properties ??= new[] { "VariableName", "Description", "Unit" };

            var payload = JsonSerializer.Serialize(new
            {
                Variables = variables,
                Properties = properties
            });

            for (var attempt = 0; attempt < 2; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var token = await GetValidAccessTokenAsync(settings,
                    forceRefresh: attempt == 1, cancellationToken: cancellationToken);
                if (string.IsNullOrWhiteSpace(token))
                    throw new InvalidOperationException("Failed to get a valid PCVue access token.");

                using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                request.Content = new StringContent(payload, Encoding.UTF8, "application/json");

                using var response = await _httpClient.SendAsync(request, cancellationToken);
                var raw = await response.Content.ReadAsStringAsync(cancellationToken);
                if (response.IsSuccessStatusCode) return raw;
                if (response.StatusCode == HttpStatusCode.Unauthorized && attempt == 0)
                    continue;

                // IIS may return a full diagnostic page (including machine paths). Never log it.
                _logger.LogWarning("PCVue BulkRead failed. HTTP {Status}; response URI {Path}",
                    response.StatusCode, response.RequestMessage?.RequestUri?.AbsolutePath);
                throw new InvalidOperationException(
                    $"PCVue BulkRead failed: HTTP {(int)response.StatusCode} ({response.StatusCode}).");
            }

            throw new InvalidOperationException("PCVue BulkRead failed after refreshing the access token.");
        }

        public async Task<string> ReadVariablesAsync(
            PCVueWebServiceSettings settings,
            string[] variables,
            CancellationToken cancellationToken = default)
        {
            // PCVue also exposes a GET for multiple values. Keep batches small enough
            // for web servers with conservative URL length limits.
            var query = string.Join("&", variables.Select((name, index) =>
                $"Variables[{index}]={Uri.EscapeDataString(name)}"));
            var endpoint = $"{NormalizeBaseUrl(settings.BaseUrl)}/RealtimeData/v2/Values/?{query}";

            for (var attempt = 0; attempt < 2; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var token = await GetValidAccessTokenAsync(settings,
                    forceRefresh: attempt == 1, cancellationToken: cancellationToken);
                if (string.IsNullOrWhiteSpace(token))
                    throw new InvalidOperationException("Failed to get a valid PCVue access token.");

                using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                using var response = await _httpClient.SendAsync(request, cancellationToken);
                if (response.IsSuccessStatusCode)
                    return await response.Content.ReadAsStringAsync(cancellationToken);
                if (response.StatusCode == HttpStatusCode.Unauthorized && attempt == 0)
                    continue;
                throw new InvalidOperationException(
                    $"PCVue Values failed: HTTP {(int)response.StatusCode} ({response.StatusCode}).");
            }

            throw new InvalidOperationException("PCVue Values failed after refreshing the access token.");
        }

        private static ValidationResult ValidateSettings(PCVueWebServiceSettings settings)
        {
            if (settings == null) return new ValidationResult(false, "PCVue settings are required.");
            if (string.IsNullOrWhiteSpace(settings.BaseUrl)) return new ValidationResult(false, "Base URL is required.");
            if (!Uri.TryCreate(settings.BaseUrl, UriKind.Absolute, out var uri) ||
                uri.Scheme != Uri.UriSchemeHttps)
            {
                return new ValidationResult(false, "PCVue Base URL must be an absolute HTTPS URL.");
            }
            if (string.IsNullOrWhiteSpace(settings.ClientId)) return new ValidationResult(false, "Client ID is required.");
            if (string.IsNullOrWhiteSpace(settings.ClientSecret)) return new ValidationResult(false, "Client Secret is required.");
            if (string.IsNullOrWhiteSpace(settings.Username)) return new ValidationResult(false, "Username is required.");
            if (string.IsNullOrWhiteSpace(settings.Password)) return new ValidationResult(false, "Password is required.");
            return new ValidationResult(true, "Settings are valid");
        }

        private static string BuildSessionKey(PCVueWebServiceSettings settings)
        {
            // A PCVue OAuth session is a server/client/user concept, not a local DB-row
            // concept. Reusing this identity prevents the settings UI and AutoImportWorker
            // from opening duplicate sessions for the same PCVue user.
            var identity = string.Join("|",
                NormalizeBaseUrl(settings.BaseUrl).ToUpperInvariant(),
                settings.ClientId.Trim(),
                settings.Username.Trim().ToUpperInvariant());
            return "oauth:" + Hash(identity);
        }

        private static string BuildConfigurationFingerprint(PCVueWebServiceSettings settings)
        {
            var value = string.Join("|",
                NormalizeBaseUrl(settings.BaseUrl).ToUpperInvariant(),
                settings.ClientId,
                settings.ClientSecret,
                settings.Username,
                settings.Password);
            return Hash(value);
        }

        private static string NormalizeBaseUrl(string baseUrl)
            => baseUrl.Trim().TrimEnd('/');

        private static string Hash(string value)
            => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

        private static string SafeConnectionLabel(PCVueWebServiceSettings settings)
            => !string.IsNullOrWhiteSpace(settings.ConnectionName)
                ? settings.ConnectionName
                : (!string.IsNullOrWhiteSpace(settings.ConnectionId)
                    ? settings.ConnectionId
                    : settings.BaseUrl);

        private static OAuthErrorResponse? ParseOAuthError(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;
            try
            {
                return JsonSerializer.Deserialize<OAuthErrorResponse>(
                    raw,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            catch (JsonException)
            {
                return null;
            }
        }

        private static string BuildOAuthErrorMessage(
            HttpStatusCode statusCode,
            OAuthErrorResponse? error)
        {
            if (!string.IsNullOrWhiteSpace(error?.ErrorDescription))
            {
                return $"PCVue OAuth failed: {error.ErrorDescription}";
            }
            if (!string.IsNullOrWhiteSpace(error?.Error))
            {
                return $"PCVue OAuth failed: {error.Error}";
            }
            return $"PCVue OAuth failed: HTTP {(int)statusCode} ({statusCode}).";
        }

        private static OAuthTokenResponse Failed(
            string message,
            bool tooManyUsers = false)
            => new()
            {
                Success = false,
                ErrorMessage = message,
                IsTooManyUsers = tooManyUsers
            };

        private sealed class PCVueSessionState
        {
            public SemaphoreSlim Gate { get; } = new(1, 1);
            public string ConfigurationFingerprint { get; private set; } = string.Empty;
            public string BaseUrl { get; private set; } = string.Empty;
            public string? AccessToken { get; set; }
            public string? RefreshToken { get; set; }
            public string TokenType { get; set; } = "Bearer";
            public int OriginalExpiresIn { get; set; }
            public DateTimeOffset AccessTokenExpiresAtUtc { get; set; } = DateTimeOffset.MinValue;
            public DateTimeOffset RetryAfterUtc { get; set; } = DateTimeOffset.MinValue;
            public string? LastError { get; set; }
            public bool LastFailureWasTooManyUsers { get; set; }

            public void ResetForConfiguration(string fingerprint, string baseUrl)
            {
                ClearTokens();
                ConfigurationFingerprint = fingerprint;
                BaseUrl = baseUrl;
            }

            public void ClearTokens()
            {
                AccessToken = null;
                RefreshToken = null;
                TokenType = "Bearer";
                OriginalExpiresIn = 0;
                AccessTokenExpiresAtUtc = DateTimeOffset.MinValue;
                RetryAfterUtc = DateTimeOffset.MinValue;
                LastError = null;
                LastFailureWasTooManyUsers = false;
            }
        }

        private sealed record TokenAttempt(
            OAuthTokenResponse Response,
            HttpStatusCode? StatusCode,
            bool IsTooManyUsers,
            bool CanFallbackToPasswordGrant)
        {
            public bool Success => Response.Success;

            public static TokenAttempt Successful(
                HttpStatusCode statusCode,
                OAuthTokenResponse response)
                => new(response, statusCode, false, false);

            public static TokenAttempt Failure(
                HttpStatusCode? statusCode,
                string message,
                bool tooManyUsers = false,
                bool canFallbackToPasswordGrant = false)
                => new(
                    new OAuthTokenResponse
                    {
                        Success = false,
                        ErrorMessage = message,
                        IsTooManyUsers = tooManyUsers
                    },
                    statusCode,
                    tooManyUsers,
                    canFallbackToPasswordGrant);
        }
    }

    #region Response Models
    public class OAuthTokenResponse
    {
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public bool IsTooManyUsers { get; set; }
        [JsonPropertyName("access_token")] public string AccessToken { get; set; } = string.Empty;
        [JsonPropertyName("token_type")] public string TokenType { get; set; } = "Bearer";
        [JsonPropertyName("expires_in")] public int ExpiresIn { get; set; }
        [JsonPropertyName("refresh_token")] public string? RefreshToken { get; set; }
        [JsonPropertyName("scope")] public string? Scope { get; set; }
    }

    public class OAuthErrorResponse
    {
        [JsonPropertyName("error")] public string Error { get; set; } = string.Empty;
        [JsonPropertyName("error_description")] public string ErrorDescription { get; set; } = string.Empty;
        [JsonPropertyName("error_uri")] public string? ErrorUri { get; set; }
    }

    public class WebServiceTestResult
    {
        public bool Success { get; set; }
        public string? Message { get; set; }
        public string? ErrorMessage { get; set; }
        public string? TokenInfo { get; set; }
    }

    public class ValidationResult
    {
        public bool IsValid { get; }
        public string ErrorMessage { get; }

        public ValidationResult(bool isValid, string errorMessage)
        {
            IsValid = isValid;
            ErrorMessage = errorMessage;
        }
    }
    #endregion
}
