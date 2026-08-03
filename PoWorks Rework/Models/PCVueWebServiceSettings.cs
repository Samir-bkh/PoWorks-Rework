using System.ComponentModel.DataAnnotations;

namespace PoWorks_Rework.Models
{
    /// <summary>
    /// Stores configuration for PCVue web service connections.
    /// Supports multiple authentication methods and connection management.
    /// </summary>
    public class PCVueWebServiceSettings
    {
        /// <summary>
        /// Unique identifier for this connection
        /// </summary>
        [Display(Name = "Connection ID")]
        public string ConnectionId { get; set; } = Guid.NewGuid().ToString();

        /// <summary>
        /// User-friendly name for this connection
        /// </summary>
        [Display(Name = "Connection Name")]
        public string ConnectionName { get; set; } = "";

        /// <summary>
        /// Base URL of the PCVue web service endpoint
        /// </summary>
        [Display(Name = "Base URL")]
        public string BaseUrl { get; set; } = ""; 

        /// <summary>
        /// Client ID for OAuth authentication
        /// </summary>
        [Display(Name = "Client ID")]
        public string ClientId { get; set; } = "";

        /// <summary>
        /// Client Secret for OAuth authentication
        /// </summary>
        [Display(Name = "Client Secret")]
        public string ClientSecret { get; set; } = "";

        /// <summary>
        /// API Key for API Key authentication method
        /// </summary>
        [Display(Name = "API Key")]
        public string ApiKey { get; set; } = ""; 

        /// <summary>
        /// Username for Basic or other authentication methods
        /// </summary>
        [Display(Name = "Username")]
        public string Username { get; set; } = "";

        /// <summary>
        /// Password for Basic or other authentication methods
        /// </summary>
        [Display(Name = "Password")]
        public string Password { get; set; } = "";

        /// <summary>
        /// Authentication method to use (OAuth, ApiKey, or Basic)
        /// </summary>
        [Display(Name = "Authentication Type")]
        public AuthenticationType AuthType { get; set; } = AuthenticationType.OAuth;

        /// <summary>
        /// Request timeout in seconds
        /// </summary>
        [Display(Name = "Timeout (seconds)")]
        public int TimeoutSeconds { get; set; } = 30;

        /// <summary>
        /// Whether this is the default connection to use
        /// </summary>
        [Display(Name = "Is Default Connection")]
        public bool IsDefault { get; set; } = false;

        /// <summary>
        /// Name of the PCVue project to access
        /// </summary>
        [Display(Name = "PCVue Project Name")]
        public string ProjectName { get; set; } = "";

        /// <summary>
        /// Whether to automatically import data from this source
        /// </summary>
        [Display(Name = "Enable Automatic Data Import")]
        public bool EnableAutomaticImport { get; set; } = false;

        /// <summary>
        /// Interval in minutes between automatic import cycles
        /// </summary>
        [Display(Name = "Auto Import Interval (minutes)")]
        [Range(1, 1440)]
        public int AutoImportIntervalMinutes { get; set; } = 1;

        /// <summary>
        /// Generates the appropriate HTTP Authorization header value based on auth type.
        /// Returns formatted header suitable for HTTP requests.
        /// </summary>
        public string GetAuthHeaderValue()
        {
            return AuthType switch
            {
                AuthenticationType.OAuth => $"Bearer {ClientId}:{ClientSecret}",
                AuthenticationType.ApiKey => $"ApiKey {ApiKey}",
                AuthenticationType.Basic => $"Basic {Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"{Username}:{Password}"))}",
                _ => ""
            };
        }
    }

    /// <summary>
    /// Enum for supported authentication types for web service connections.
    /// </summary>
    public enum AuthenticationType
    {
        /// <summary>
        /// OAuth 2.0 token-based authentication
        /// </summary>
        [Display(Name = "OAuth 2.0")]
        OAuth = 0,

        /// <summary>
        /// API key-based authentication
        /// </summary>
        [Display(Name = "API Key")]
        ApiKey = 1,

        /// <summary>
        /// HTTP Basic authentication with username and password
        /// </summary>
        [Display(Name = "Basic Auth")]
        Basic = 2
    }
}