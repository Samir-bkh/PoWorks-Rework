using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
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

        /// <summary>
        /// Initializes the auth controller with user and sign-in manager dependencies.
        /// </summary>
        public AuthController(UserManager<IdentityUser> userManager, SignInManager<IdentityUser> signInManager)
        {
            _userManager = userManager;
            _signInManager = signInManager;
        }

        /// <summary>
        /// Displays the login page. Allows anonymous access.
        /// </summary>
        [AllowAnonymous]
        [HttpGet]
        public IActionResult Login(string returnUrl = null)
        {
            ViewData["ReturnUrl"] = returnUrl;
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
            Console.WriteLine($"\n--- LOGIN ATTEMPT ---");

            if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
            {
                ModelState.AddModelError(string.Empty, "Invalid username or password.");
                return View();
            }

            var result = await _signInManager.PasswordSignInAsync(username, password, rememberMe, lockoutOnFailure: false);

            if (result.Succeeded)
            {
                var user = await _userManager.FindByNameAsync(username);
                if (user != null)
                {
              
                    var claims = (await _userManager.GetClaimsAsync(user)).ToList();

               
                    if (!claims.Any(c => c.Type == "CompanyId"))
                    {
                        Console.WriteLine("No CompanyId found, assigning to Company 1 by default.");
                        claims.Add(new Claim("CompanyId", "1"));

                
                        await _userManager.AddClaimAsync(user, new Claim("CompanyId", "1"));
                    }

               
                    await _signInManager.SignInWithClaimsAsync(user, rememberMe, claims);
                }

                Console.WriteLine("LOGIN SUCCESSFUL!");

                if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
                {
                    return LocalRedirect(returnUrl ?? "~/");
                }
                return LocalRedirect("~/");
            }

            ModelState.AddModelError(string.Empty, "Invalid login attempt.");
            return View();
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