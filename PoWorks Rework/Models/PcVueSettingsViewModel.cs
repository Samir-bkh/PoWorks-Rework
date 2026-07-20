using System.ComponentModel.DataAnnotations;

namespace PoWorks_Rework.Models
{
    public class PcVueSettingsViewModel
    {
        public int Id { get; set; }

        [Required(ErrorMessage = "L'URL de base est requise.")]
        [Url(ErrorMessage = "L'URL n'est pas valide.")]
        public string BaseUrl { get; set; } = string.Empty;

        [Required(ErrorMessage = "Le Client ID est requis.")]
        public string ClientId { get; set; } = string.Empty;

        [Required(ErrorMessage = "Le Client Secret est requis.")]
        public string ClientSecret { get; set; } = string.Empty;

        public string? Username { get; set; }

        public string? Password { get; set; }

        public bool IsActive { get; set; } = true;
        public string? ProjectName { get; set; }
    }
}