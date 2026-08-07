using System.ComponentModel.DataAnnotations;

namespace PoWorks_Rework.Models
{
    /// <summary>
    /// View model for PCVue web service connection configuration.
    /// Handles authentication credentials and connection settings for PCVue SCADA system integration.
    /// </summary>
    public class PcVueSettingsViewModel
    {
        /// <summary>
        /// Unique identifier for this connection configuration
        /// </summary>
        public int Id { get; set; }

        /// <summary>
        /// Base URL of the PCVue web service
        /// </summary>
        [Required(ErrorMessage = "The base URL is required.")]
        [Url(ErrorMessage = "The URL is not valid.")]
        public string BaseUrl { get; set; } = string.Empty;

        /// <summary>
        /// OAuth Client ID for authentication
        /// </summary>
        [Required(ErrorMessage = "The Client ID is required.")]
        public string ClientId { get; set; } = string.Empty;

        /// <summary>
        /// OAuth Client Secret for authentication
        /// </summary>
        [Required(ErrorMessage = "The Client Secret is required.")]
        public string ClientSecret { get; set; } = string.Empty;

        /// <summary>
        /// Optional username for basic authentication
        /// </summary>
        public string? Username { get; set; }

        /// <summary>
        /// Optional password for basic authentication
        /// </summary>
        public string? Password { get; set; }

        /// <summary>
        /// Whether this connection is currently active
        /// </summary>
        public bool IsActive { get; set; } = true;

        /// <summary>
        /// Name of the PCVue project to connect to
        /// </summary>
        public string? ProjectName { get; set; }
    }
}