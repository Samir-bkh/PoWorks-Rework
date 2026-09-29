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
    /// Central PCVue Web Services client.
    ///
    /// OAuth sessions are cached per configured PCVue connection. A password grant
    /// opens a session only when no reusable session exists; normal renewal uses the
    /// refresh token supplied by PCVue. Authentication failures are briefly cached to
    /// prevent concurrent callers from creating an authentication storm against a
    /// license-limited Web Services Toolkit server.
    /// </summary>
    public class PCVueWebService
    {
        private static readonly TimeSpan TooManyUsersBackoff = TimeSpan.FromSeconds(60);
        private static readonly TimeSpan TransientAuthenticationBackoff = TimeSpan.FromSeconds(5);

        private readonly HttpClient _httpClient;
        private readonly ILogger<PCVueWebService> _logger;
        private readonly TimeProvider _timeProvider;
        private readonly ConcurrentDictionary<string, PCVueSessionState> _sessions = new(StringComparer.Ordinal);

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

        /// <summary>
        /// Returns a reusable access token for the selected PCVue connection.
        /// On forceRefresh, the existing refresh token is tried first; it is never
        /// discarded before the refresh attempt.
        /// </summary>
        public async Task<string?> GetValidAccessTokenAsync(
            PCVueWebServiceSettings settings,
            bool forceRefresh = false)
        {
            var response = await AcquireTokenAsync(settings, forceRefresh);
            return response.Success ? response.AccessToken : null;
        }

        /// <summary>
        /// Legacy/public token API used by the settings page. It is session-aware and
        /// therefore no longer opens a fresh PCVue session on every invocation.
        /// </summary>
        public Task<OAuthTokenResponse> GetAccessTokenAsync(PCVueWebServiceSettings settings)
            => AcquireTokenAsync(settings, forceRefresh: false);

        /// <summary>
        /// Explicitly renews the access token using the refresh token when available.
        /// A password grant is only used when the previous session is no longer valid.
        /// </summary>
        public Task<OAuthTokenResponse> RefreshAccessTokenAsync(PCVueWebServiceSettings settings)
            => AcquireTokenAsync(settings, forceRefresh: true);

        private async Task<OAuthTokenResponse> AcquireTokenAsync(
            PCVueWebServiceSettings settings,
            bool forceRefresh)
        {
            var validation = ValidateSettings(settings);
            if (!validation.IsValid)
            {
                return Failed(validation.ErrorMessage);
            }

            var key = BuildSessionKey(settings);
            var fingerprint = BuildConfigurationFingerprint(settings);
            var state = _sessions.GetOrAdd(key, _ => new PCVueSessionState());
            var now = _timeProvider.GetUtcNow();

            if (!forceRefresh &&
                state.ConfigurationFingerprint == fingerprint &&
                IsAccessTokenUsable(state, now))
            {
                return CachedResponse(state);
            }

            await state.Gate.WaitAsync();
            try
            {
                now = _timeProvider.GetUtcNow();

                if (state.ConfigurationFingerprint != fingerprint)
                {
                    // Same logical connection ID but changed server/credentials.
                    // Release the previous server-side session before replacing it.
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
                        "PCVue authentication is temporarily paused after a recent failure.");
                }

                if (!string.IsNullOrWhiteSpace(state.RefreshToken))
                {
                    var refreshAttempt = await RequestRefreshTokenAsync(settings, state);
                    if (refreshAttempt.Success)
                    {
                        return refreshAttempt.Response;
                    }

                    if (refreshAttempt.IsTooManyUsers)
                    {
                        ApplyAuthenticationBackoff(state, refreshAttempt.Response.ErrorMessage, TooManyUsersBackoff);
                        return refreshAttempt.Response;
                    }

                    if (!refreshAttempt.CanFallbackToPasswordGrant)
                    {
                        ApplyAuthenticationBackoff(
                            state,
                            refreshAttempt.Response.ErrorMessage,
                            TransientAuthenticationBackoff);
                        return refreshAttempt.Response;
                    }

                    // The previous PCVue session is invalid/expired. At this point it is
                    // safe to open one replacement session with the password grant.
                    state.ClearTokens();
                }

                var passwordAttempt = await RequestPasswordTokenAsync(settings, state);
                if (!passwordAttempt.Success)
                {
                    ApplyAuthenticationBackoff(
                        state,
                        passwordAttempt.ErrorMessage,
                        passwordAttempt.IsTooManyUsers
                            ? TooManyUsersBackoff
                            : TransientAuthenticationBackoff);
                }

                return passwordAttempt;
            }
            finally
            {
                state.Gate.Release();
            }
        }

        private async Task<OAuthTokenResponse> RequestPasswordTokenAsync(
            PCVueWebServiceSettings settings,
            PCVueSessionState state)
        {
            var tokenEndpoint = $"{NormalizeBaseUrl(settings.BaseUrl)}/OAuth/token";

            _logger.LogInformation(
                "PCVue OAuth password grant for connection {ConnectionId} at {Endpoint}",
                SafeConnectionLabel(settings),
                tokenEndpoint);

            var formParams = new Dictionary<string, string>
            {
                ["username"] = settings.Username,
                ["password"] = settings.Password,
                ["grant_type"] = "password",
                ["client_id"] = settings.ClientId,
                ["client_secret"] = settings.ClientSecret,
                ["scope"] = "RealtimeData RealtimeAlarm HistoricalData GraphicalData"
            };

            var attempt = await SendTokenRequestAsync(tokenEndpoint, formParams);
            if (!attempt.Response.Success)
            {
                LogAuthenticationFailure(settings, attempt.Response, attempt.StatusCode);
                return attempt.Response;
            }

            ApplySuccessfulToken(state, attempt.Response, preserveRefreshToken: false);
            _logger.LogInformation(
                "PCVue OAuth session opened for connection {ConnectionId}; token lifetime {ExpiresIn}s.",
                SafeConnectionLabel(settings),
                attempt.Response.ExpiresIn);
            return attempt.Response;
        }

        private async Task<TokenAttempt> RequestRefreshTokenAsync(
            PCVueWebServiceSettings settings,
            PCVueSessionState state)
        {
            var tokenEndpoint = $"{NormalizeBaseUrl(settings.BaseUrl)}/OAuth/token";
            var existingRefreshToken = state.RefreshToken;

            var formParams = new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = existingRefreshToken!,
                ["client_id"] = settings.ClientId,
                ["client_secret"] = settings.ClientSecret
            };

            _logger.LogDebug(
                "Refreshing PCVue OAuth session for connection {ConnectionId}.",
                SafeConnectionLabel(settings));

            var attempt = await SendTokenRequestAsync(tokenEndpoint, formParams);
            if (attempt.Response.Success)
            {
                // Some OAuth implementations rotate refresh tokens while others do not.
                // Keep the previous refresh token if PcVue omits it in the response.
                ApplySuccessfulToken(state, attempt.Response, preserveRefreshToken: true);
                return attempt with { Response = CachedResponse(state) };
            }

            LogAuthenticationFailure(settings, attempt.Response, attempt.StatusCode, isRefresh: true);
            return attempt;
        }

        private async Task<TokenAttempt> SendTokenRequestAsync(
            string tokenEndpoint,
            Dictionary<string, string> formParams)
        {
            try
            {
                using var formContent = new FormUrlEncodedContent(formParams);
                using var response = await _httpClient.PostAsync(tokenEndpoint, formContent);
                var responseContent = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    if (string.IsNullOrWhiteSpace(responseContent))
                    {
                        return TokenAttempt.Failure(
                            response.StatusCode,
                            "PCVue returned an empty OAuth response.");
                    }

                    try
                    {
                        var token = JsonSerializer.Deserialize<OAuthTokenResponse>(
                            responseContent,
                            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                        if (token == null || string.IsNullOrWhiteSpace(token.AccessToken))
                        {
                            return TokenAttempt.Failure(
                                response.StatusCode,
                                "PCVue OAuth response does not contain an access_token.");
                        }

                        token.Success = true;
                        return TokenAttempt.Successful(response.StatusCode, token);
                    }
                    catch (JsonException ex)
                    {
                        _logger.LogWarning(ex, "Unable to parse the PCVue OAuth response.");
                        return TokenAttempt.Failure(
                            response.StatusCode,
                            "Unable to parse the PCVue OAuth response.");
                    }
                }

                var oauthError = ParseOAuthError(responseContent);
                var errorCode = oauthError?.ErrorDescription;
                var isTooManyUsers = string.Equals(
                    errorCode,
                    "E_TooManyUsers",
                    StringComparison.OrdinalIgnoreCase);

                var canFallback =
                    !isTooManyUsers &&
                    (response.StatusCode == HttpStatusCode.Unauthorized ||
                     string.Equals(oauthError?.Error, "invalid_grant", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(errorCode, "E_InvalidSessionId", StringComparison.OrdinalIgnoreCase));

                var userMessage = isTooManyUsers
                    ? "PCVue Web Services connection limit reached (E_TooManyUsers). " +
                      "PoWorks paused authentication retries for 60 seconds to avoid opening more sessions."
                    : BuildOAuthErrorMessage(response.StatusCode, oauthError, responseContent);

                return TokenAttempt.Failure(
                    response.StatusCode,
                    userMessage,
                    isTooManyUsers,
                    canFallback);
            }
            catch (HttpRequestException ex)
            {
                _logger.LogWarning(ex, "Network error while contacting PCVue OAuth endpoint {Endpoint}.", tokenEndpoint);
                return TokenAttempt.Failure(
                    null,
                    $"Network error while contacting PCVue OAuth: {ex.Message}");
            }
            catch (TaskCanceledException ex)
            {
                _logger.LogWarning(ex, "Timeout while contacting PCVue OAuth endpoint {Endpoint}.", tokenEndpoint);
                return TokenAttempt.Failure(null, "Timeout while contacting PCVue OAuth.");
            }
        }

        private void ApplySuccessfulToken(
            PCVueSessionState state,
            OAuthTokenResponse response,
            bool preserveRefreshToken)
        {
            var now = _timeProvider.GetUtcNow();
            var expiresIn = response.ExpiresIn > 0 ? response.ExpiresIn : 1200;

            // PCVue tells the client the real token lifetime with expires_in. Keep a
            // small safety margin rather than forcing a 4-minute lifetime.
            var safetyMarginSeconds = Math.Clamp(expiresIn / 10, 5, 30);
            var usableLifetimeSeconds = Math.Max(1, expiresIn - safetyMarginSeconds);

            state.AccessToken = response.AccessToken;
            if (!preserveRefreshToken || !string.IsNullOrWhiteSpace(response.RefreshToken))
            {
                state.RefreshToken = string.IsNullOrWhiteSpace(response.RefreshToken)
                    ? state.RefreshToken
                    : response.RefreshToken;
            }

            state.TokenType = string.IsNullOrWhiteSpace(response.TokenType) ? "Bearer" : response.TokenType;
            state.OriginalExpiresIn = expiresIn;
            state.AccessTokenExpiresAtUtc = now.AddSeconds(usableLifetimeSeconds);
            state.RetryAfterUtc = DateTimeOffset.MinValue;
            state.LastError = null;

            response.ExpiresIn = expiresIn;
            response.RefreshToken = state.RefreshToken;
            response.Success = true;
        }

        private static bool IsAccessTokenUsable(PCVueSessionState state, DateTimeOffset now)
            => !string.IsNullOrWhiteSpace(state.AccessToken) && now < state.AccessTokenExpiresAtUtc;

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
            string? error,
            TimeSpan delay)
        {
            state.LastError = error;
            state.RetryAfterUtc = _timeProvider.GetUtcNow().Add(delay);
        }

        private void LogAuthenticationFailure(
            PCVueWebServiceSettings settings,
            OAuthTokenResponse response,
            HttpStatusCode? statusCode,
            bool isRefresh = false)
        {
            if (response.IsTooManyUsers)
            {
                _logger.LogWarning(
                    "PCVue rejected {GrantType} authentication for connection {ConnectionId}: E_TooManyUsers. " +
                    "Further authentication attempts are temporarily throttled.",
                    isRefresh ? "refresh-token" : "password",
                    SafeConnectionLabel(settings));
                return;
            }

            _logger.LogWarning(
                "PCVue rejected {GrantType} authentication for connection {ConnectionId}. HTTP {StatusCode}: {Error}",
                isRefresh ? "refresh-token" : "password",
                SafeConnectionLabel(settings),
                statusCode,
                response.ErrorMessage);
        }

        /// <summary>
        /// Tests authentication plus HistoricalData service availability without opening
        /// another session when a reusable session already exists.
        /// </summary>
        public async Task<WebServiceTestResult> TestConnectionAsync(PCVueWebServiceSettings settings)
        {
            var tokenResponse = await AcquireTokenAsync(settings, forceRefresh: false);
            if (!tokenResponse.Success)
            {
                return new WebServiceTestResult
                {
                    Success = false,
                    ErrorMessage = tokenResponse.ErrorMessage,
                    TokenInfo = tokenResponse.IsTooManyUsers ? "E_TooManyUsers" : null
                };
            }

            var endpoint = $"{NormalizeBaseUrl(settings.BaseUrl)}/HistoricalData/v2/Status";
            var status = await SendBearerStatusRequestAsync(endpoint, tokenResponse.AccessToken);

            if (status.StatusCode == HttpStatusCode.Unauthorized)
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

                status = await SendBearerStatusRequestAsync(endpoint, refreshed.AccessToken);
            }

            if (!status.IsSuccessStatusCode)
            {
                return new WebServiceTestResult
                {
                    Success = false,
                    ErrorMessage = $"PCVue HistoricalData status failed: HTTP {(int)status.StatusCode} ({status.StatusCode})."
                };
            }

            return new WebServiceTestResult
            {
                Success = true,
                Message = "PCVue OAuth session and HistoricalData service are available.",
                TokenInfo = $"expires_in={tokenResponse.ExpiresIn}"
            };
        }

        private async Task<HttpResponseMessage> SendBearerStatusRequestAsync(string endpoint, string token)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            return await _httpClient.SendAsync(request);
        }

        /// <summary>
        /// Ends the cached PCVue server-side session for a configured connection.
        /// </summary>
        public async Task<bool> LogoutAsync(PCVueWebServiceSettings settings)
        {
            var key = BuildSessionKey(settings);
            if (!_sessions.TryGetValue(key, out var state))
            {
                return true;
            }

            await state.Gate.WaitAsync();
            try
            {
                var success = await TryLogoutStateAsync(state, CancellationToken.None);
                state.ClearTokens();
                _sessions.TryRemove(key, out _);
                return success;
            }
            finally
            {
                state.Gate.Release();
            }
        }

        /// <summary>
        /// Ends a cached session by connection ID, used when deleting a configured
        /// Web Service connection.
        /// </summary>
        public async Task<bool> LogoutConnectionAsync(string connectionId)
        {
            if (string.IsNullOrWhiteSpace(connectionId)) return true;

            var key = "id:" + connectionId.Trim();
            if (!_sessions.TryGetValue(key, out var state)) return true;

            await state.Gate.WaitAsync();
            try
            {
                var success = await TryLogoutStateAsync(state, CancellationToken.None);
                state.ClearTokens();
                _sessions.TryRemove(key, out _);
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
            if (string.IsNullOrWhiteSpace(state.AccessToken) || string.IsNullOrWhiteSpace(state.BaseUrl))
            {
                return true;
            }

            var endpoint = $"{state.BaseUrl.TrimEnd('/')}/OAuth/Account/logout";
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", state.AccessToken);
                using var response = await _httpClient.SendAsync(request, cancellationToken);

                // 401 means the server already considers the session invalid/closed.
                return response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.Unauthorized;
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
            string[]? properties = null)
        {
            var token = await GetValidAccessTokenAsync(settings);
            if (string.IsNullOrEmpty(token))
            {
                throw new InvalidOperationException("Failed to get a valid PCVue access token.");
            }

            var endpoint = $"{NormalizeBaseUrl(settings.BaseUrl)}/RealTimeData/v2/BulkRead";
            properties ??= new[] { "VariableName", "Description", "Unit" };

            var requestPayload = new { Variables = variables, Properties = properties };
            var jsonContent = JsonSerializer.Serialize(requestPayload);

            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            request.Content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

            using var response = await _httpClient.SendAsync(request);
            var responseContent = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode) return responseContent;

            _logger.LogError(
                "PCVue BulkRead failed. HTTP {StatusCode}: {Response}",
                response.StatusCode,
                responseContent);
            throw new InvalidOperationException(
                $"PCVue BulkRead failed: HTTP {(int)response.StatusCode} ({response.StatusCode}).");
        }

        private static ValidationResult ValidateSettings(PCVueWebServiceSettings settings)
        {
            if (settings == null) return new ValidationResult(false, "PCVue settings are required.");
            if (string.IsNullOrWhiteSpace(settings.BaseUrl)) return new ValidationResult(false, "Base URL is required.");
            if (!Uri.TryCreate(settings.BaseUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            {
                return new ValidationResult(false, "PCVue Base URL must be an absolute HTTPS URL.");
            }
            if (string.IsNullOrWhiteSpace(settings.ClientId)) return new ValidationResult(false, "Client ID is required.");
            if (string.IsNullOrWhiteSpace(settings.ClientSecret)) return new ValidationResult(false, "Client Secret is required.");
            if (string.IsNullOrWhiteSpace(settings.Username)) return new ValidationResult(false, "Username is required.");
            if (string.IsNullOrWhiteSpace(settings.Password)) return new ValidationResult(false, "Password is required.");
            return new ValidationResult(true, "Settings are valid");
        }

        private static string NormalizeBaseUrl(string baseUrl) => baseUrl.Trim().TrimEnd('/');

        private static string BuildSessionKey(PCVueWebServiceSettings settings)
        {
            if (!string.IsNullOrWhiteSpace(settings.ConnectionId))
            {
                return "id:" + settings.ConnectionId.Trim();
            }

            var identity = $"{NormalizeBaseUrl(settings.BaseUrl).ToUpperInvariant()}|{settings.ClientId}|{settings.Username}";
            return "cfg:" + Hash(identity);
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

        private static string Hash(string value)
            => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

        private static string SafeConnectionLabel(PCVueWebServiceSettings settings)
            => !string.IsNullOrWhiteSpace(settings.ConnectionId)
                ? settings.ConnectionId
                : (!string.IsNullOrWhiteSpace(settings.ConnectionName)
                    ? settings.ConnectionName
                    : settings.BaseUrl);

        private static OAuthErrorResponse? ParseOAuthError(string responseContent)
        {
            if (string.IsNullOrWhiteSpace(responseContent)) return null;
            try
            {
                return JsonSerializer.Deserialize<OAuthErrorResponse>(
                    responseContent,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            catch (JsonException)
            {
                return null;
            }
        }

        private static string BuildOAuthErrorMessage(
            HttpStatusCode statusCode,
            OAuthErrorResponse? error,
            string rawContent)
        {
            if (!string.IsNullOrWhiteSpace(error?.ErrorDescription))
            {
                return $"PCVue OAuth failed: {error.ErrorDescription}";
            }
            if (!string.IsNullOrWhiteSpace(error?.Error))
            {
                return $"PCVue OAuth failed: {error.Error}";
            }
            return string.IsNullOrWhiteSpace(rawContent)
                ? $"PCVue OAuth failed: HTTP {(int)statusCode} ({statusCode})."
                : $"PCVue OAuth failed: HTTP {(int)statusCode} ({statusCode}).";
        }

        private static OAuthTokenResponse Failed(string message, bool tooManyUsers = false)
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
            }
        }

        private sealed record TokenAttempt(
            OAuthTokenResponse Response,
            HttpStatusCode? StatusCode,
            bool IsTooManyUsers,
            bool CanFallbackToPasswordGrant)
        {
            public bool Success => Response.Success;

            public static TokenAttempt Successful(HttpStatusCode statusCode, OAuthTokenResponse response)
                => new(response, statusCode, false, false);

            public static TokenAttempt Failure(
                HttpStatusCode? statusCode,
                string error,
                bool tooManyUsers = false,
                bool canFallbackToPasswordGrant = false)
                => new(
                    new OAuthTokenResponse
                    {
                        Success = false,
                        ErrorMessage = error,
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
        [JsonPropertyName("access_token")] public string AccessToken { get; set; } = "";
        [JsonPropertyName("token_type")] public string TokenType { get; set; } = "Bearer";
        [JsonPropertyName("expires_in")] public int ExpiresIn { get; set; }
        [JsonPropertyName("refresh_token")] public string? RefreshToken { get; set; }
        [JsonPropertyName("scope")] public string? Scope { get; set; }
    }

    public class OAuthErrorResponse
    {
        [JsonPropertyName("error")] public string Error { get; set; } = "";
        [JsonPropertyName("error_description")] public string ErrorDescription { get; set; } = "";
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