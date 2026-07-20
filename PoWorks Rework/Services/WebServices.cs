using System.Text.Json;
using System.Text.Json.Serialization;
using PoWorks_Rework.Models;

namespace PoWorks_Rework.Services
{
    public class PCVueWebService
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<PCVueWebService> _logger;

        public HttpClient HttpClient => _httpClient;

        // Token storage
        private string? _accessToken;
        private string? _refreshToken;
        private DateTime _tokenExpiry;

        public PCVueWebService(HttpClient httpClient, ILogger<PCVueWebService> logger)
        {
            _httpClient = httpClient;
            _logger = logger;
        }

        public async Task<string?> GetValidAccessTokenAsync(PCVueWebServiceSettings settings, bool forceRefresh = false)
        {
            if (forceRefresh)
            {
                _logger.LogInformation("🔄 Force refresh requested. Clearing old tokens.");
                ClearTokens();
            }

            if (!string.IsNullOrEmpty(_accessToken) && DateTime.UtcNow < _tokenExpiry)
            {
                return _accessToken;
            }

            if (!string.IsNullOrEmpty(_refreshToken))
            {
                _logger.LogDebug("Attempting token refresh");
                var refreshedToken = await RefreshTokenAsync(settings);
                if (!string.IsNullOrEmpty(refreshedToken))
                {
                    return refreshedToken;
                }
            }

            _logger.LogInformation("Getting new access token...");
            var tokenResponse = await RequestNewTokenAsync(settings);
            return tokenResponse.Success ? tokenResponse.AccessToken : null;
        }

        public async Task<OAuthTokenResponse> GetAccessTokenAsync(PCVueWebServiceSettings settings)
        {
            return await RequestNewTokenAsync(settings);
        }

        private async Task<OAuthTokenResponse> RequestNewTokenAsync(PCVueWebServiceSettings settings)
        {
            try
            {
                _logger.LogInformation("Requesting new OAuth token for PCVue Web Services");

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

                            _logger.LogInformation("✅ OAuth token acquired successfully. Cached for {CacheSeconds} seconds.", actualLifespan);

                            tokenResponse.Success = true;
                            return tokenResponse;
                        }
                        else
                        {
                            // C'est ici que la magie de la lecture d'erreur se passe
                            var errorContent = await response.Content.ReadAsStringAsync();
                            _logger.LogError("OAuth token request failed: {StatusCode}. Message retourné par PcVue : {ErrorDetails}",
                                response.StatusCode, errorContent);

                            return new OAuthTokenResponse { Success = false, ErrorMessage = $"Token request failed: {response.StatusCode}" };
                        }
                    }
                    catch (JsonException ex)
                    {
                        return new OAuthTokenResponse { Success = false, ErrorMessage = $"Error parsing token response: {ex.Message}" };
                    }
                }
                else
                {
                    _logger.LogError("OAuth token request failed: {StatusCode}", response.StatusCode);
                    return new OAuthTokenResponse { Success = false, ErrorMessage = $"Token request failed: {response.StatusCode}" };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error during OAuth token request");
                return new OAuthTokenResponse { Success = false, ErrorMessage = $"Unexpected error: {ex.Message}" };
            }
        }

        private async Task<string?> RefreshTokenAsync(PCVueWebServiceSettings settings)
        {
            try
            {
                _logger.LogInformation("Refreshing OAuth token");
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

        private static ValidationResult ValidateSettings(PCVueWebServiceSettings settings)
        {
            if (string.IsNullOrWhiteSpace(settings.BaseUrl)) return new ValidationResult(false, "Base URL is required");
            return new ValidationResult(true, "Settings are valid");
        }

        public void ClearTokens()
        {
            _accessToken = null;
            _refreshToken = null;
            _tokenExpiry = DateTime.MinValue;
            _logger.LogDebug("All tokens cleared");
        }

        public void ClearToken()
        {
            ClearTokens();
        }

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
    public class OAuthTokenResponse
    {
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        [JsonPropertyName("access_token")] public string AccessToken { get; set; } = "";
        [JsonPropertyName("token_type")] public string TokenType { get; set; } = "Bearer";
        [JsonPropertyName("expires_in")] public int ExpiresIn { get; set; }
        [JsonPropertyName("refresh_token")] public string? RefreshToken { get; set; }
        [JsonPropertyName("scope")] public string? Scope { get; set; }
    }

    public class OAuthErrorResponse
    {
        public string Error { get; set; } = "";
        public string ErrorDescription { get; set; } = "";
        public string? ErrorUri { get; set; }
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