using System.ComponentModel.DataAnnotations;

namespace PoWorks_Rework.Models
{
    public class CompanyListViewModel
    {
        public int CompanyId { get; set; }
        public string Name { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public int UserCount { get; set; }
    }

    public class CreateCompanyViewModel
    {
        [Required(ErrorMessage = "Le nom de l'entreprise est requis.")]
        public string CompanyName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Le nom d'utilisateur est requis.")]
        public string AdminUsername { get; set; } = string.Empty;

        [Required(ErrorMessage = "Le mot de passe est requis.")]
        [MinLength(6, ErrorMessage = "Le mot de passe doit faire au moins 6 caractères.")]
        public string AdminPassword { get; set; } = string.Empty;
    }
}