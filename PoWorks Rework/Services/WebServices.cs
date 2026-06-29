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
        private string? _refreshToken;  // FIXED: Added missing refresh token storage
        private DateTime _tokenExpiry;

        public PCVueWebService(HttpClient httpClient, ILogger<PCVueWebService> logger)
        {
            _httpClient = httpClient;
            _logger = logger;
        }

        /// <summary>
        /// Get a valid access token (refreshes automatically if needed)
        /// This is the MAIN function you should call for all API requests
        /// </summary>
        /// <param name="settings">PCVue connection settings</param>
        /// <returns>Valid access token or null if authentication fails</returns>
        public async Task<string?> GetValidAccessTokenAsync(PCVueWebServiceSettings settings)
        {
            // If we have a valid token, return it
            if (!string.IsNullOrEmpty(_accessToken) && DateTime.UtcNow < _tokenExpiry)
            {
                return _accessToken;
            }

            // If we have a refresh token, try to refresh first
            if (!string.IsNullOrEmpty(_refreshToken))
            {
                _logger.LogDebug("Attempting token refresh");
                var refreshedToken = await RefreshTokenAsync(settings);
                if (!string.IsNullOrEmpty(refreshedToken))
                {
                    return refreshedToken;
                }
            }

            // No valid token and refresh failed, get a new token
            _logger.LogDebug("Getting new access token");
            var tokenResponse = await RequestNewTokenAsync(settings);
            return tokenResponse.Success ? tokenResponse.AccessToken : null;
        }

        /// <summary>
        /// Get OAuth access token from PCVue Web Services (for backward compatibility)
        /// Prefer using GetValidAccessTokenAsync instead
        /// </summary>
        /// <param name="settings">PCVue connection settings</param>
        /// <returns>OAuth token response</returns>
        public async Task<OAuthTokenResponse> GetAccessTokenAsync(PCVueWebServiceSettings settings)
        {
            return await RequestNewTokenAsync(settings);
        }

        // UPDATE YOUR RequestNewTokenAsync method in WebServices.cs with this version that includes detailed logging:

        /// <summary>
        /// Request a new OAuth token (initial login or when refresh fails)
        /// </summary>
        /// <summary>
        /// Request a new OAuth token (initial login or when refresh fails)
        /// </summary>
        private async Task<OAuthTokenResponse> RequestNewTokenAsync(PCVueWebServiceSettings settings)
        {
            try
            {
                _logger.LogInformation("Requesting new OAuth token for PCVue Web Services");

         

                // Prepare token endpoint URL
                var tokenEndpoint = $"{settings.BaseUrl.TrimEnd('/')}/OAuth/token";
                _logger.LogInformation("=== API CALL DEBUG ===");
                _logger.LogInformation("Token endpoint: {TokenEndpoint}", tokenEndpoint);

                // Prepare form data for OAuth Password Grant Flow
                var formParams = new Dictionary<string, string>
        {
            {"username", settings.Username},
            {"password", settings.Password},
            {"grant_type", "password"},
            {"client_id", settings.ClientId},
            {"client_secret", settings.ClientSecret},
            {"scope", "RealtimeData RealtimeAlarm HistoricalData GraphicalData"} // ✅ ADDED SCOPE
        };

                // LOG THE REQUEST PARAMETERS (without sensitive data)
                _logger.LogInformation("Request parameters:");
                _logger.LogInformation("  username: {Username}", settings.Username);
                _logger.LogInformation("  password: [HIDDEN]");
                _logger.LogInformation("  grant_type: password");
                _logger.LogInformation("  client_id: {ClientId}", settings.ClientId);
                _logger.LogInformation("  client_secret: [HIDDEN]");
                _logger.LogInformation("  scope: RealtimeData RealtimeAlarm HistoricalData GraphicalData"); // ✅ LOG SCOPE

                // Create form-encoded content
                var formContent = new FormUrlEncodedContent(formParams);

                // LOG THE ACTUAL FORM CONTENT
                var formContentString = await formContent.ReadAsStringAsync();
                _logger.LogInformation("Form content: {FormContent}", formContentString);

                // Make the token request
                _logger.LogInformation("Sending OAuth token request...");
                var response = await _httpClient.PostAsync(tokenEndpoint, formContent);

                // Read response content
                var responseContent = await response.Content.ReadAsStringAsync();

                // LOG THE RAW RESPONSE
                _logger.LogInformation("=== RAW RESPONSE ===");
                _logger.LogInformation("Status Code: {StatusCode}", response.StatusCode);
                _logger.LogInformation("Response Headers:");
                foreach (var header in response.Headers)
                {
                    _logger.LogInformation("  {HeaderName}: {HeaderValue}", header.Key, string.Join(", ", header.Value));
                }
                foreach (var header in response.Content.Headers)
                {
                    _logger.LogInformation("  {HeaderName}: {HeaderValue}", header.Key, string.Join(", ", header.Value));
                }
                _logger.LogInformation("Raw Response Body: {ResponseContent}", responseContent);
                _logger.LogInformation("Response Length: {Length} characters", responseContent?.Length ?? 0);
                _logger.LogInformation("====================");

                if (response.IsSuccessStatusCode)
                {
                    // Check if response is empty or null
                    if (string.IsNullOrWhiteSpace(responseContent))
                    {
                        _logger.LogError("Response content is empty or null");
                        return new OAuthTokenResponse
                        {
                            Success = false,
                            ErrorMessage = "Empty response from server"
                        };
                    }

                    try
                    {
                        // Parse successful token response
                        _logger.LogInformation("Attempting to parse JSON response...");
                        var tokenResponse = JsonSerializer.Deserialize<OAuthTokenResponse>(responseContent, new JsonSerializerOptions
                        {
                            PropertyNameCaseInsensitive = true
                        });

                        _logger.LogInformation("JSON parsing completed. TokenResponse is null: {IsNull}", tokenResponse == null);

                        if (tokenResponse != null)
                        {
                            _logger.LogInformation("Parsed token response:");
                            _logger.LogInformation("  AccessToken: {HasToken}", !string.IsNullOrEmpty(tokenResponse.AccessToken) ? "PRESENT" : "MISSING");
                            _logger.LogInformation("  RefreshToken: {HasRefreshToken}", !string.IsNullOrEmpty(tokenResponse.RefreshToken) ? "PRESENT" : "MISSING");
                            _logger.LogInformation("  TokenType: {TokenType}", tokenResponse.TokenType);
                            _logger.LogInformation("  ExpiresIn: {ExpiresIn}", tokenResponse.ExpiresIn);
                        }

                        if (tokenResponse != null && !string.IsNullOrEmpty(tokenResponse.AccessToken))
                        {
                            // Store ALL token information
                            _accessToken = tokenResponse.AccessToken;
                            _refreshToken = tokenResponse.RefreshToken;  // IMPORTANT: Store refresh token
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
                    catch (JsonException ex)
                    {
                        _logger.LogError(ex, "Error parsing token response JSON. Raw response: {ResponseContent}", responseContent);
                        return new OAuthTokenResponse
                        {
                            Success = false,
                            ErrorMessage = $"Error parsing token response: {ex.Message}"
                        };
                    }
                }
                else
                {
                    _logger.LogError("OAuth token request failed: {StatusCode} - {ResponseContent}", response.StatusCode, responseContent);

                    // Try to parse error response
                    try
                    {
                        var errorResponse = JsonSerializer.Deserialize<OAuthErrorResponse>(responseContent, new JsonSerializerOptions
                        {
                            PropertyNameCaseInsensitive = true
                        });

                        return new OAuthTokenResponse
                        {
                            Success = false,
                            ErrorMessage = $"OAuth error: {errorResponse?.Error} - {errorResponse?.ErrorDescription}"
                        };
                    }
                    catch
                    {
                        return new OAuthTokenResponse
                        {
                            Success = false,
                            ErrorMessage = $"Token request failed: {response.StatusCode} - {responseContent}"
                        };
                    }
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
        /// Refresh the access token using the refresh token
        /// </summary>
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

                        // Update refresh token if provided (some servers return new refresh token)
                        if (tokenData.TryGetProperty("refresh_token", out var refreshElement))
                        {
                            _refreshToken = refreshElement.GetString();
                        }

                        // Update expiry
                        var expiresIn = tokenData.TryGetProperty("expires_in", out var expiresElement) ?
                            expiresElement.GetInt32() : 3600;
                        _tokenExpiry = DateTime.UtcNow.AddSeconds(expiresIn - 60);

                        _logger.LogInformation("Token refreshed successfully. Expires in {ExpiresIn} seconds", expiresIn);
                        return _accessToken;
                    }
                }

                _logger.LogWarning("Token refresh failed: {StatusCode}", response.StatusCode);
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error refreshing token");
                return null;
            }
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
                var tokenResponse = await RequestNewTokenAsync(settings);

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
        /// Clear stored tokens (for logout or connection changes)
        /// </summary>
        public void ClearTokens()
        {
            _accessToken = null;
            _refreshToken = null;
            _tokenExpiry = DateTime.MinValue;
            _logger.LogDebug("All tokens cleared");
        }

        /// <summary>
        /// Clear stored token (for backward compatibility)
        /// </summary>
        public void ClearToken()
        {
            ClearTokens();
        }

        public async Task<string> BulkReadVariablesAsync(PCVueWebServiceSettings settings, string[] variables, string[] properties = null)
        {
            try
            {
                _logger.LogInformation("Starting BulkRead operation for {VariableCount} variables", variables.Length);

                // Get valid access token
                var token = await GetValidAccessTokenAsync(settings);
                if (string.IsNullOrEmpty(token))
                {
                    throw new Exception("Failed to get valid access token for BulkRead operation");
                }

                // Build BulkRead endpoint URL
                var bulkReadEndpoint = $"{settings.BaseUrl.TrimEnd('/')}/RealTimeData/v2/BulkRead";
                _logger.LogInformation("BulkRead endpoint: {Endpoint}", bulkReadEndpoint);

                // Set default properties if none provided
                if (properties == null || properties.Length == 0)
                {
                    properties = new[] { "VariableName", "Description", "Unit" };
                }

                // Create request payload
                var requestPayload = new
                {
                    Variables = variables,
                    Properties = properties
                };

                // Serialize to JSON
                var jsonContent = JsonSerializer.Serialize(requestPayload, new JsonSerializerOptions
                {
                    WriteIndented = true
                });

                _logger.LogInformation("BulkRead request payload:");
                _logger.LogInformation("{JsonPayload}", jsonContent);

                // Create HTTP request
                var httpRequest = new HttpRequestMessage(HttpMethod.Post, bulkReadEndpoint);
                httpRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
                httpRequest.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
                httpRequest.Content = new StringContent(jsonContent, System.Text.Encoding.UTF8, "application/json");

                // Send request
                var response = await _httpClient.SendAsync(httpRequest);
                var responseContent = await response.Content.ReadAsStringAsync();

                _logger.LogInformation("BulkRead response status: {StatusCode}", response.StatusCode);
                _logger.LogInformation("BulkRead response length: {Length} characters", responseContent?.Length ?? 0);

                if (response.IsSuccessStatusCode)
                {
                    _logger.LogInformation("BulkRead operation completed successfully");
                    return responseContent;
                }
                else
                {
                    throw new Exception($"BulkRead API call failed with status: {response.StatusCode}. Response: {responseContent}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during BulkRead operation");
                throw;
            }
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

        [JsonPropertyName("access_token")]
        public string AccessToken { get; set; } = "";

        [JsonPropertyName("token_type")]
        public string TokenType { get; set; } = "Bearer";

        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; set; }

        [JsonPropertyName("refresh_token")]
        public string? RefreshToken { get; set; }

        [JsonPropertyName("scope")]
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