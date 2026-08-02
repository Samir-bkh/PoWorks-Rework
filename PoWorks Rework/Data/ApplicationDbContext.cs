using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace PoWorks_Rework.Data
{
    /// <summary>
    /// Entity Framework Core database context for ASP.NET Identity.
    /// Manages user authentication, roles, and identity-related data persistence.
    /// </summary>
    public class ApplicationDbContext : IdentityDbContext<IdentityUser>
    {
        /// <summary>
        /// Initializes the ApplicationDbContext with Entity Framework configuration options.
        /// </summary>
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }
    }
}