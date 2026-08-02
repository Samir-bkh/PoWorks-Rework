using System.ComponentModel.DataAnnotations;

namespace PoWorks_Rework.Models
{
    /// <summary>
    /// View model for displaying company information in admin list.
    /// Shows company details with user count and creation date.
    /// </summary>
    public class CompanyListViewModel
    {
        /// <summary>
        /// Unique identifier for the company
        /// </summary>
        public int CompanyId { get; set; }

        /// <summary>
        /// Name of the company
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Timestamp when the company was created
        /// </summary>
        public DateTime CreatedAt { get; set; }

        /// <summary>
        /// Number of active users in the company
        /// </summary>
        public int UserCount { get; set; }
    }

    /// <summary>
    /// View model for creating a new company with initial admin user.
    /// Used in admin onboarding and company registration.
    /// </summary>
    public class CreateCompanyViewModel
    {
        /// <summary>
        /// Legal name of the company to create
        /// </summary>
        [Required(ErrorMessage = "Le nom de l'entreprise est requis.")]
        public string CompanyName { get; set; } = string.Empty;

        /// <summary>
        /// Username for the initial admin user
        /// </summary>
        [Required(ErrorMessage = "Le nom d'utilisateur est requis.")]
        public string AdminUsername { get; set; } = string.Empty;

        /// <summary>
        /// Password for the initial admin user (minimum 6 characters)
        /// </summary>
        [Required(ErrorMessage = "Le mot de passe est requis.")]
        [MinLength(6, ErrorMessage = "Le mot de passe doit faire au moins 6 caractères.")]
        public string AdminPassword { get; set; } = string.Empty;
    }
}