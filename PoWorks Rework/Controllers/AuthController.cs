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

        [AllowAnonymous]
        [HttpPost]
        public async Task<IActionResult> Login(string username, string password, bool rememberMe, string returnUrl = null)
        {
            Console.WriteLine($"\n--- TENTATIVE DE CONNEXION ---");

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
                        Console.WriteLine("Aucun CompanyId trouvé, assignation à la Company 1 par défaut.");
                        claims.Add(new Claim("CompanyId", "1"));

                
                        await _userManager.AddClaimAsync(user, new Claim("CompanyId", "1"));
                    }

               
                    await _signInManager.SignInWithClaimsAsync(user, rememberMe, claims);
                }

                Console.WriteLine("CONNEXION RÉUSSIE !");

                if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
                {
                    return LocalRedirect(returnUrl ?? "~/");
                }
                return LocalRedirect("~/");
            }

            ModelState.AddModelError(string.Empty, "Invalid login attempt.");
            return View();
        }

        public async Task<IActionResult> Logout()
        {
            await _signInManager.SignOutAsync();
            return LocalRedirect("~/");
        }

        [HttpGet]
        public IActionResult AccessDenied()
        {
            return View();
        }
    }
}