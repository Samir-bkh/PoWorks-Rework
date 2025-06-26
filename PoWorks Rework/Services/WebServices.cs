// Services/PCVueWebService.cs
using System.Text;
using System.Text.Json;
using PoWorks_Rework.Models;

namespace PoWorks_Rework.Services
{
    public class PCVueWebService
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<PCVueWebService> _logger;
        private string? _accessToken;
        private DateTime _tokenExpiry;

        public PCVueWebService(HttpClient httpClient, ILogger<PCVueWebService> logger)
        {
            _httpClient = httpClient;
            _logger = logger;
        }

        /// <summary>
        /// Get OAuth access token from PCVue Web Services
        /// </summary>
        /// <param name="settings">PCVue connection settings</param>
        /// <returns>OAuth token response</returns>
        public async Task<OAuthTokenResponse> GetAccessTokenAsync(PCVueWebServiceSettings settings)
        {
            try
            {
                _logger.LogInformation("Attempting to get OAuth token from PCVue Web Services");

                // Configure HttpClient
                _httpClient.Timeout = TimeSpan.FromSeconds(settings.TimeoutSeconds);

                // Prepare token endpoint URL
                var tokenEndpoint = $"{settings.BaseUrl.TrimEnd('/')}/OAuth/Token";
                _logger.LogDebug("Token endpoint: {TokenEndpoint}", tokenEndpoint);

                // Prepare form data for OAuth Password Grant Flow
                var formParams = new Dictionary<string, string>
                {
                    {"username", settings.Username},
                    {"password", settings.Password},
                    {"grant_type", "password"},
                    {"client_id", settings.ClientId},
                    {"client_secret", settings.ClientSecret}
                };

                // Create form-encoded content
                var formContent = new FormUrlEncodedContent(formParams);

                // Make the token request
                _logger.LogDebug("Sending OAuth token request...");
                var response = await _httpClient.PostAsync(tokenEndpoint, formContent);

                // Read response content
                var responseContent = await response.Content.ReadAsStringAsync();
                _logger.LogDebug("Token response status: {StatusCode}", response.StatusCode);

                if (response.IsSuccessStatusCode)
                {
                    // Parse successful token response
                    var tokenResponse = JsonSerializer.Deserialize<OAuthTokenResponse>(responseContent, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });

                    if (tokenResponse != null && !string.IsNullOrEmpty(tokenResponse.AccessToken))
                    {
                        // Store token and calculate expiry
                        _accessToken = tokenResponse.AccessToken;
                        _tokenExpiry = DateTime.UtcNow.AddSeconds(tokenResponse.ExpiresIn - 60); // 60 second buffer

                        _logger.LogInformation("OAuth token acquired successfully. Expires in {ExpiresIn} seconds", tokenResponse.ExpiresIn);

                        tokenResponse.Success = true;
                        return tokenResponse;
                    }
                    else
                    {
                        _logger.LogError("Invalid token response: missing access_token");
                        return new OAuthTokenResponse
                        {
                            Success = false,
                            ErrorMessage = "Invalid token response: missing access_token"
                        };
                    }
                }
                else
                {
                    // Handle error response
                    _logger.LogError("OAuth token request failed. Status: {StatusCode}, Response: {Response}",
                        response.StatusCode, responseContent);

                    // Try to parse error response
                    OAuthErrorResponse? errorResponse = null;
                    try
                    {
                        errorResponse = JsonSerializer.Deserialize<OAuthErrorResponse>(responseContent, new JsonSerializerOptions
                        {
                            PropertyNameCaseInsensitive = true
                        });
                    }
                    catch (JsonException)
                    {
                        // If JSON parsing fails, use raw response
                    }

                    var errorMessage = errorResponse?.ErrorDescription ??
                                     errorResponse?.Error ??
                                     $"HTTP {response.StatusCode}: {response.ReasonPhrase}";

                    return new OAuthTokenResponse
                    {
                        Success = false,
                        ErrorMessage = errorMessage
                    };
                }
            }
            catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
            {
                _logger.LogError(ex, "OAuth token request timed out");
                return new OAuthTokenResponse
                {
                    Success = false,
                    ErrorMessage = "Request timed out. Check your network connection and try again."
                };
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "Network error during OAuth token request");
                return new OAuthTokenResponse
                {
                    Success = false,
                    ErrorMessage = $"Network error: {ex.Message}"
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error during OAuth token request");
                return new OAuthTokenResponse
                {
                    Success = false,
                    ErrorMessage = $"Unexpected error: {ex.Message}"
                };
            }
        }

        /// <summary>
        /// Get current access token (refresh if needed)
        /// </summary>
        /// <param name="settings">PCVue connection settings</param>
        /// <returns>Valid access token</returns>
        public async Task<string?> GetValidAccessTokenAsync(PCVueWebServiceSettings settings)
        {
            // Check if we have a valid token
            if (!string.IsNullOrEmpty(_accessToken) && DateTime.UtcNow < _tokenExpiry)
            {
                return _accessToken;
            }

            // Token is expired or missing, get a new one
            var tokenResponse = await GetAccessTokenAsync(settings);

            return tokenResponse.Success ? tokenResponse.AccessToken : null;
        }

        /// <summary>
        /// Test PCVue Web Services connection by attempting to get a token
        /// </summary>
        /// <param name="settings">PCVue connection settings</param>
        /// <returns>Connection test result</returns>
        public async Task<WebServiceTestResult> TestConnectionAsync(PCVueWebServiceSettings settings)
        {
            try
            {
                _logger.LogInformation("Testing PCVue Web Services connection");

                // Validate settings
                var validationResult = ValidateSettings(settings);
                if (!validationResult.IsValid)
                {
                    return new WebServiceTestResult
                    {
                        Success = false,
                        ErrorMessage = validationResult.ErrorMessage
                    };
                }

                // Attempt to get access token
                var tokenResponse = await GetAccessTokenAsync(settings);

                if (tokenResponse.Success)
                {
                    return new WebServiceTestResult
                    {
                        Success = true,
                        Message = "PCVue Web Services connection successful! OAuth token acquired.",
                        TokenInfo = $"Token Type: {tokenResponse.TokenType}, Expires in: {tokenResponse.ExpiresIn} seconds"
                    };
                }
                else
                {
                    return new WebServiceTestResult
                    {
                        Success = false,
                        ErrorMessage = tokenResponse.ErrorMessage
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error testing PCVue Web Services connection");
                return new WebServiceTestResult
                {
                    Success = false,
                    ErrorMessage = $"Connection test failed: {ex.Message}"
                };
            }
        }

        /// <summary>
        /// Validate PCVue connection settings
        /// </summary>
        /// <param name="settings">Settings to validate</param>
        /// <returns>Validation result</returns>
        private static ValidationResult ValidateSettings(PCVueWebServiceSettings settings)
        {
            if (string.IsNullOrWhiteSpace(settings.BaseUrl))
            {
                return new ValidationResult(false, "Base URL is required");
            }

            if (string.IsNullOrWhiteSpace(settings.Username))
            {
                return new ValidationResult(false, "Username is required");
            }

            if (string.IsNullOrWhiteSpace(settings.Password))
            {
                return new ValidationResult(false, "Password is required");
            }

            if (string.IsNullOrWhiteSpace(settings.ClientId))
            {
                return new ValidationResult(false, "Client ID is required");
            }

            if (string.IsNullOrWhiteSpace(settings.ClientSecret))
            {
                return new ValidationResult(false, "Client Secret is required");
            }

            // Validate URL format
            if (!Uri.TryCreate(settings.BaseUrl, UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                return new ValidationResult(false, "Base URL must be a valid HTTP or HTTPS URL");
            }

            return new ValidationResult(true, "Settings are valid");
        }

        /// <summary>
        /// Clear stored token (for logout or connection changes)
        /// </summary>
        public void ClearToken()
        {
            _accessToken = null;
            _tokenExpiry = DateTime.MinValue;
            _logger.LogDebug("Access token cleared");
        }
    }

    #region Response Models

    /// <summary>
    /// OAuth token response from PCVue Web Services
    /// </summary>
    public class OAuthTokenResponse
    {
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }

        public string AccessToken { get; set; } = "";
        public string TokenType { get; set; } = "Bearer";
        public int ExpiresIn { get; set; }
        public string? RefreshToken { get; set; }
        public string? Scope { get; set; }
    }

    /// <summary>
    /// OAuth error response
    /// </summary>
    public class OAuthErrorResponse
    {
        public string Error { get; set; } = "";
        public string ErrorDescription { get; set; } = "";
        public string? ErrorUri { get; set; }
    }

    /// <summary>
    /// Web service connection test result
    /// </summary>
    public class WebServiceTestResult
    {
        public bool Success { get; set; }
        public string? Message { get; set; }
        public string? ErrorMessage { get; set; }
        public string? TokenInfo { get; set; }
    }

    /// <summary>
    /// Settings validation result
    /// </summary>
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