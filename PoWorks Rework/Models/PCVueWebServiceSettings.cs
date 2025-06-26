// Models/PCVueWebServiceSettings.cs
using System.ComponentModel.DataAnnotations;

namespace PoWorks_Rework.Models
{
    public class PCVueWebServiceSettings
    {
        [Display(Name = "Connection ID")]
        public string ConnectionId { get; set; } = Guid.NewGuid().ToString();

        [Display(Name = "Connection Name")]
        public string ConnectionName { get; set; } = "";

        [Display(Name = "Base URL")]
        public string BaseUrl { get; set; } = ""; // e.g., https://pcvue-server:8080/api

        [Display(Name = "Client ID")]
        public string ClientId { get; set; } = "";

        [Display(Name = "Client Secret")]
        public string ClientSecret { get; set; } = "";

        [Display(Name = "API Key")]
        public string ApiKey { get; set; } = ""; // Alternative to OAuth

        // NEW: Basic Auth fields
        [Display(Name = "Username")]
        public string Username { get; set; } = "";

        [Display(Name = "Password")]
        public string Password { get; set; } = "";

        [Display(Name = "Authentication Type")]
        public AuthenticationType AuthType { get; set; } = AuthenticationType.OAuth;

        [Display(Name = "Timeout (seconds)")]
        public int TimeoutSeconds { get; set; } = 30;

        [Display(Name = "Is Default Connection")]
        public bool IsDefault { get; set; } = false;

        [Display(Name = "PCVue Project Name")]
        public string ProjectName { get; set; } = "";

        // Helper method to get authorization header value
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

    public enum AuthenticationType
    {
        [Display(Name = "OAuth 2.0")]
        OAuth = 0,

        [Display(Name = "API Key")]
        ApiKey = 1,

        [Display(Name = "Basic Auth")]
        Basic = 2
    }
}