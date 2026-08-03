using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using PoWorks_Rework.Models;

namespace PoWorks_Rework.Services
{
    /// <summary>
    /// Service for communicating with PCVue web service API.
    /// Handles OAuth token management, trends data requests, and variable browsing.
    /// Implements automatic token refresh with caching and retry logic.
    /// </summary>
    public class PCVueWebService
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<PCVueWebService> _logger;
        private readonly SemaphoreSlim _tokenLock = new SemaphoreSlim(1, 1);

        /// <summary>
        /// Gets the underlying HTTP client for requests
        /// </summary>
        public HttpClient HttpClient => _httpClient;

        private string? _accessToken;
        private string? _refreshToken;
        private DateTime _tokenExpiry;
        private DateTime _lastTokenRefreshTime = DateTime.MinValue;

        /// <summary>
        /// Initializes the PCVue web service with HTTP client and logging.
        /// Configures SSL validation to accept self-signed certificates.
        /// </summary>
        public PCVueWebService(HttpClient httpClient, ILogger<PCVueWebService> logger)
        {
            _logger = logger;

            var handler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true
            };

            _httpClient = new HttpClient(handler)
            {
                Timeout = httpClient.Timeout
            };
        }

        /// <summary>
        /// Gets a valid OAuth access token for API requests.
        /// Caches tokens and automatically refreshes when expired.
        /// Can force refresh to handle token invalidation.
        /// </summary>
        public async Task<string?> GetValidAccessTokenAsync(PCVueWebServiceSettings settings, bool forceRefresh = false)
        {
            if (!forceRefresh && !string.IsNullOrEmpty(_accessToken) && DateTime.UtcNow < _tokenExpiry)
            {
                return _accessToken;
            }

            await _tokenLock.WaitAsync();
            try
            {
                if (forceRefresh)
                {
                    if ((DateTime.UtcNow - _lastTokenRefreshTime).TotalSeconds < 5)
                    {
                        return _accessToken;
                    }

                    ClearTokens();
                }
                else if (!string.IsNullOrEmpty(_accessToken) && DateTime.UtcNow < _tokenExpiry)
                {
                    return _accessToken;
                }

                if (!string.IsNullOrEmpty(_refreshToken))
                {
                    var refreshedToken = await RefreshTokenAsync(settings);
                    if (!string.IsNullOrEmpty(refreshedToken))
                    {
                        return refreshedToken;
                    }
                }

                var tokenResponse = await RequestNewTokenAsync(settings);
                return tokenResponse.Success ? tokenResponse.AccessToken : null;
            }
            finally
            {
                _tokenLock.Release();
            }
        }

        /// <summary>
        /// Requests a new OAuth access token from the PCVue web service.
        /// </summary>
        /// <param name="settings">The web service connection settings.</param>
        /// <returns>An OAuth token response with the access token.</returns>
        public async Task<OAuthTokenResponse> GetAccessTokenAsync(PCVueWebServiceSettings settings)
        {
            return await RequestNewTokenAsync(settings);
        }

        /// <summary>
        /// Requests a new token from the PCVue OAuth endpoint with password grant.
        /// </summary>
        /// <param name="settings">The web service connection settings.</param>
        /// <returns>An OAuth token response with success status and token data.</returns>
        private async Task<OAuthTokenResponse> RequestNewTokenAsync(PCVueWebServiceSettings settings)
        {
            try
            {
                var tokenEndpoint = $"{settings.BaseUrl.TrimEnd('/')}/OAuth/token";

                var formParams = new Dictionary<string, string>
                {
                    {"username", settings.Username},
                    {"password", settings.Password},
                    {"grant_type", "password"},
                    {"client_id", settings.ClientId},
                    {"client_secret", settings.ClientSecret},
                    {"scope", "RealtimeData RealtimeAlarm HistoricalData GraphicalData"}
                };

                var formContent = new FormUrlEncodedContent(formParams);
                var response = await _httpClient.PostAsync(tokenEndpoint, formContent);
                var responseContent = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    if (string.IsNullOrWhiteSpace(responseContent))
                    {
                        return new OAuthTokenResponse { Success = false, ErrorMessage = "Empty response from server" };
                    }

                    try
                    {
                        var tokenResponse = JsonSerializer.Deserialize<OAuthTokenResponse>(responseContent, new JsonSerializerOptions
                        {
                            PropertyNameCaseInsensitive = true
                        });

                        if (tokenResponse != null && !string.IsNullOrEmpty(tokenResponse.AccessToken))
                        {
                            _accessToken = tokenResponse.AccessToken;
                            _refreshToken = tokenResponse.RefreshToken;

                            int actualLifespan = Math.Min(tokenResponse.ExpiresIn - 60, 240);
                            _tokenExpiry = DateTime.UtcNow.AddSeconds(actualLifespan);
                            _lastTokenRefreshTime = DateTime.UtcNow;

                            tokenResponse.Success = true;
                            return tokenResponse;
                        }

                        return new OAuthTokenResponse { Success = false, ErrorMessage = $"Token request failed: {response.StatusCode}" };
                    }
                    catch (JsonException ex)
                    {
                        return new OAuthTokenResponse { Success = false, ErrorMessage = $"Error parsing token response: {ex.Message}" };
                    }
                }

                var errorContent = await response.Content.ReadAsStringAsync();
                return new OAuthTokenResponse { Success = false, ErrorMessage = $"PCVue a refusé l'accès : {errorContent}" };
            }
            catch (Exception ex)
            {
               
                return new OAuthTokenResponse { Success = false, ErrorMessage = $"Unexpected error: {ex.Message}" };
            }
        }

        /// <summary>
        /// Refreshes the OAuth access token using the stored refresh token.
        /// </summary>
        /// <param name="settings">The web service connection settings.</param>
        /// <returns>The new access token, or null if refresh failed.</returns>
        private async Task<string?> RefreshTokenAsync(PCVueWebServiceSettings settings)
        {
            try
            {
                var tokenEndpoint = $"{settings.BaseUrl.TrimEnd('/')}/OAuth/Token";

                var formParams = new Dictionary<string, string>
                {
                    {"grant_type", "refresh_token"},
                    {"refresh_token", _refreshToken!},
                    {"client_id", settings.ClientId},
                    {"client_secret", settings.ClientSecret}
                };

                var formContent = new FormUrlEncodedContent(formParams);
                var response = await _httpClient.PostAsync(tokenEndpoint, formContent);

                if (response.IsSuccessStatusCode)
                {
                    var responseContent = await response.Content.ReadAsStringAsync();
                    var tokenData = JsonSerializer.Deserialize<JsonElement>(responseContent);

                    if (tokenData.TryGetProperty("access_token", out var accessElement))
                    {
                        _accessToken = accessElement.GetString();

                        if (tokenData.TryGetProperty("refresh_token", out var refreshElement))
                        {
                            _refreshToken = refreshElement.GetString();
                        }

                        var expiresIn = tokenData.TryGetProperty("expires_in", out var expiresElement) ? expiresElement.GetInt32() : 3600;
                        int actualLifespan = Math.Min(expiresIn - 60, 240);
                        _tokenExpiry = DateTime.UtcNow.AddSeconds(actualLifespan);
                        _lastTokenRefreshTime = DateTime.UtcNow;

                        return _accessToken;
                    }
                }
                return null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// Tests the connection to a PCVue web service by validating settings and requesting a token.
        /// </summary>
        /// <param name="settings">The web service connection settings to test.</param>
        /// <returns>A WebServiceTestResult indicating success or failure.</returns>
        public async Task<WebServiceTestResult> TestConnectionAsync(PCVueWebServiceSettings settings)
        {
            try
            {
                var validationResult = ValidateSettings(settings);
                if (!validationResult.IsValid) return new WebServiceTestResult { Success = false, ErrorMessage = validationResult.ErrorMessage };

                var tokenResponse = await RequestNewTokenAsync(settings);
                if (tokenResponse.Success)
                {
                    return new WebServiceTestResult { Success = true, Message = "Connection successful!" };
                }
                return new WebServiceTestResult { Success = false, ErrorMessage = tokenResponse.ErrorMessage };
            }
            catch (Exception ex)
            {
                return new WebServiceTestResult { Success = false, ErrorMessage = ex.Message };
            }
        }

        /// <summary>
        /// Validates the required web service connection settings.
        /// </summary>
        /// <param name="settings">The settings to validate.</param>
        /// <returns>A ValidationResult indicating validity.</returns>
        private static ValidationResult ValidateSettings(PCVueWebServiceSettings settings)
        {
            if (string.IsNullOrWhiteSpace(settings.BaseUrl)) return new ValidationResult(false, "Base URL is required");
            return new ValidationResult(true, "Settings are valid");
        }

        /// <summary>
        /// Clears all cached OAuth tokens and resets token state.
        /// </summary>
        public void ClearTokens()
        {
            _accessToken = null;
            _refreshToken = null;
            _tokenExpiry = DateTime.MinValue;
        }

        /// <summary>
        /// Clears the cached OAuth tokens. Alias for ClearTokens.
        /// </summary>
        public void ClearToken()
        {
            ClearTokens();
        }

        /// <summary>
        /// Performs a bulk read of multiple variables from the PCVue real-time data API.
        /// </summary>
        /// <param name="settings">The web service connection settings.</param>
        /// <param name="variables">The variable names to read.</param>
        /// <param name="properties">The properties to retrieve for each variable.</param>
        /// <returns>The raw JSON response from the API.</returns>
        public async Task<string> BulkReadVariablesAsync(PCVueWebServiceSettings settings, string[] variables, string[] properties = null)
        {
            var token = await GetValidAccessTokenAsync(settings);
            if (string.IsNullOrEmpty(token)) throw new Exception("Failed to get valid access token");

            var bulkReadEndpoint = $"{settings.BaseUrl.TrimEnd('/')}/RealTimeData/v2/BulkRead";
            properties ??= new[] { "VariableName", "Description", "Unit" };

            var requestPayload = new { Variables = variables, Properties = properties };
            var jsonContent = JsonSerializer.Serialize(requestPayload);

            var httpRequest = new HttpRequestMessage(HttpMethod.Post, bulkReadEndpoint);
            httpRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            httpRequest.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
            httpRequest.Content = new StringContent(jsonContent, System.Text.Encoding.UTF8, "application/json");

            var response = await _httpClient.SendAsync(httpRequest);
            var responseContent = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode) return responseContent;
            throw new Exception($"API call failed: {response.StatusCode}");
        }
    }

    #region Response Models
    /// <summary>
    /// Represents the OAuth token response from the PCVue web service.
    /// </summary>
    public class OAuthTokenResponse
    {
        /// <summary>
        /// Whether the token request succeeded.
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// The error message if the request failed.
        /// </summary>
        public string? ErrorMessage { get; set; }

        /// <summary>
        /// The OAuth access token.
        /// </summary>
        [JsonPropertyName("access_token")] public string AccessToken { get; set; } = "";

        /// <summary>
        /// The token type (usually Bearer).
        /// </summary>
        [JsonPropertyName("token_type")] public string TokenType { get; set; } = "Bearer";

        /// <summary>
        /// The token lifetime in seconds.
        /// </summary>
        [JsonPropertyName("expires_in")] public int ExpiresIn { get; set; }

        /// <summary>
        /// The refresh token for obtaining new access tokens.
        /// </summary>
        [JsonPropertyName("refresh_token")] public string? RefreshToken { get; set; }

        /// <summary>
        /// The scope of the issued token.
        /// </summary>
        [JsonPropertyName("scope")] public string? Scope { get; set; }
    }

    /// <summary>
    /// Represents an OAuth error response from the PCVue web service.
    /// </summary>
    public class OAuthErrorResponse
    {
        /// <summary>
        /// The OAuth error code.
        /// </summary>
        public string Error { get; set; } = "";

        /// <summary>
        /// A human-readable error description.
        /// </summary>
        public string ErrorDescription { get; set; } = "";

        /// <summary>
        /// An optional URI with more information about the error.
        /// </summary>
        public string? ErrorUri { get; set; }
    }

    /// <summary>
    /// Represents the result of a web service connection test.
    /// </summary>
    public class WebServiceTestResult
    {
        /// <summary>
        /// Whether the test succeeded.
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// A success message.
        /// </summary>
        public string? Message { get; set; }

        /// <summary>
        /// An error message if the test failed.
        /// </summary>
        public string? ErrorMessage { get; set; }

        /// <summary>
        /// Optional token information from the test.
        /// </summary>
        public string? TokenInfo { get; set; }
    }

    /// <summary>
    /// Represents the result of validating web service settings.
    /// </summary>
    public class ValidationResult
    {
        /// <summary>
        /// Whether the settings are valid.
        /// </summary>
        public bool IsValid { get; }

        /// <summary>
        /// The validation error message.
        /// </summary>
        public string ErrorMessage { get; }

        /// <summary>
        /// Initializes the validation result.
        /// </summary>
        public ValidationResult(bool isValid, string errorMessage)
        {
            IsValid = isValid;
            ErrorMessage = errorMessage;
        }
    }
    #endregion
}
