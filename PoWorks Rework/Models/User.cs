namespace PoWorks_Rework.Models
{
    /// <summary>
    /// Represents a user entity in the PoWorks system.
    /// This model stores user authentication and role information, linking users to companies
    /// and managing their active status within the platform.
    /// </summary>
    public class User
    {
        /// <summary>
        /// Unique identifier for the user
        /// </summary>
        public int UserId { get; set; }

        /// <summary>
        /// The username used for user login
        /// </summary>
        public string Username { get; set; } = string.Empty;

        /// <summary>
        /// Hashed password for secure authentication
        /// </summary>
        public string PasswordHash { get; set; } = string.Empty;

        /// <summary>
        /// User role for authorization (e.g., Admin, User, Manager)
        /// </summary>
        public string Role { get; set; } = string.Empty;

        /// <summary>
        /// Foreign key linking user to their company
        /// </summary>
        public int CompanyId { get; set; }

        /// <summary>
        /// Indicates whether the user account is active or deactivated
        /// </summary>
        public bool IsActive { get; set; }
    }
}