using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using PoWorks_Rework.Services;
using System.Threading.Tasks;
using System;
using System.Linq;
using System.Security.Claims;

namespace PoWorks_Rework.Controllers
{
    /// <summary>
    /// Controller for user authentication and authorization.
    /// Handles login, logout, registration, and user claim management for multi-tenancy.
    /// </summary>
    public class AuthController : Controller
    {
        private readonly UserManager<IdentityUser> _userManager;
        private readonly SignInManager<IdentityUser> _signInManager;
        private readonly DatabaseService _databaseService;

        /// <summary>
        /// Initializes the auth controller with user and sign-in manager dependencies.
        /// </summary>
        public AuthController(UserManager<IdentityUser> userManager, SignInManager<IdentityUser> signInManager, DatabaseService databaseService)
        {
            _userManager = userManager;
            _signInManager = signInManager;
            _databaseService = databaseService;
        }

        /// <summary>
        /// Displays the login page. Allows anonymous access.
        /// </summary>
        [AllowAnonymous]
        [HttpGet]
        public IActionResult Login(string returnUrl = null, int? accessDisabled = null)
        {
            ViewData["ReturnUrl"] = returnUrl;
            if (accessDisabled == 1)
                ModelState.AddModelError(string.Empty, "Your company or tenant access has been disabled. Contact an administrator.");
            return View();
        }

        /// <summary>
        /// Handles the login form submission. Authenticates the user credentials,
        /// ensures a CompanyId claim exists for multi-tenancy, and redirects
        /// the user to the return URL or the home page upon success.
        /// </summary>
        /// <param name="username">The username entered by the user.</param>
        /// <param name="password">The password entered by the user.</param>
        /// <param name="rememberMe">Whether the authentication cookie should persist across browser sessions.</param>
        /// <param name="returnUrl">Optional local URL to redirect to after a successful login.</param>
        /// <returns>The login view on failure, or a redirect result on success.</returns>
        [AllowAnonymous]
        [HttpPost]
        public async Task<IActionResult> Login(string username, string password, bool rememberMe, string returnUrl = null)
        {
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            {
                ModelState.AddModelError(string.Empty, "Invalid username or password.");
                return View();
            }

            var user = await _userManager.FindByNameAsync(username);
            if (user == null)
            {
                ModelState.AddModelError(string.Empty, "Invalid username or password.");
                return View();
            }

            if (user.LockoutEnabled && user.LockoutEnd.HasValue && user.LockoutEnd.Value > DateTimeOffset.UtcNow)
            {
                ModelState.AddModelError(string.Empty, "This account is disabled. Contact an administrator.");
                return View();
            }

            var claims = (await _userManager.GetClaimsAsync(user)).ToList();
            var isAdmin = string.Equals(user.UserName, "Admin", StringComparison.OrdinalIgnoreCase);

            if (!isAdmin)
            {
                var companyClaim = claims.FirstOrDefault(x => x.Type == "CompanyId")?.Value;
                if (!int.TryParse(companyClaim, out var companyId) || !IsCompanyActive(companyId))
                {
                    ModelState.AddModelError(string.Empty, "This account's company is disabled or unavailable. Contact an administrator.");
                    return View();
                }

                var userType = claims.FirstOrDefault(x => x.Type == "UserType")?.Value;
                if (string.Equals(userType, "Tenant", StringComparison.OrdinalIgnoreCase))
                {
                    var tenantClaim = claims.FirstOrDefault(x => x.Type == "TenantId")?.Value;
                    if (!int.TryParse(tenantClaim, out var tenantId) || !IsTenantActiveForCompany(tenantId, companyId))
                    {
                        ModelState.AddModelError(string.Empty, "This tenant account is disabled or unavailable. Contact an administrator.");
                        return View();
                    }
                }
            }

            var result = await _signInManager.PasswordSignInAsync(user, password, rememberMe, lockoutOnFailure: false);

            if (!result.Succeeded)
            {
                ModelState.AddModelError(string.Empty, "Invalid login attempt.");
                return View();
            }

            await _signInManager.SignInWithClaimsAsync(user, rememberMe, claims);

            if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
                return LocalRedirect(returnUrl);

            return LocalRedirect("~/");
        }

        private bool IsCompanyActive(int companyId)
        {
            try
            {
                using var connection = _databaseService.CreateNewConnection();
                connection.Open();
                using var cmd = new NpgsqlCommand(@"SELECT ""Active"" FROM ""Companies"" WHERE ""CompanyId"" = @companyId", connection);
                cmd.Parameters.AddWithValue("companyId", companyId);
                return cmd.ExecuteScalar() is bool active && active;
            }
            catch
            {
                return false;
            }
        }

        private bool IsTenantActiveForCompany(int tenantId, int companyId)
        {
            try
            {
                using var connection = _databaseService.CreateNewConnection();
                connection.Open();
                using var cmd = new NpgsqlCommand(@"
                    SELECT COALESCE(td.""Active"", FALSE)
                    FROM ""Tenants"" t
                    LEFT JOIN ""TenantDetails"" td ON td.""TenantID"" = t.""TenantID""
                    WHERE t.""TenantID"" = @tenantId
                      AND t.""CompanyId"" = @companyId", connection);
                cmd.Parameters.AddWithValue("tenantId", tenantId);
                cmd.Parameters.AddWithValue("companyId", companyId);
                return cmd.ExecuteScalar() is bool active && active;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Signs out the current user and redirects to the home page.
        /// </summary>
        /// <returns>A redirect result to the home page.</returns>
        public async Task<IActionResult> Logout()
        {
            await _signInManager.SignOutAsync();
            return LocalRedirect("~/");
        }

        /// <summary>
        /// Displays the access denied page when a user lacks the required permissions.
        /// </summary>
        /// <returns>The access denied view.</returns>
        [HttpGet]
        public IActionResult AccessDenied()
        {
            return View();
        }
    }
}