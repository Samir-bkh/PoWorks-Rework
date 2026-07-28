using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using System.Threading.Tasks;

namespace PoWorks_Rework.Controllers
{
    public class AuthController : Controller
    {
        private readonly UserManager<IdentityUser> _userManager;
        private readonly SignInManager<IdentityUser> _signInManager;

        public AuthController(UserManager<IdentityUser> userManager, SignInManager<IdentityUser> signInManager)
        {
            _userManager = userManager;
            _signInManager = signInManager;
        }

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
            Console.WriteLine($"User saisi : '{username}'");
            Console.WriteLine($"Password saisi : '{password}'");

            if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
            {
                Console.WriteLine("ERREUR : User ou Password vide !");
                return View();
            }

      
            var userCheck = await _userManager.FindByNameAsync(username);
            if (userCheck == null)
            {
                Console.WriteLine($"ERREUR : L'utilisateur '{username}' N'EXISTE PAS dans la base de données !");
            }
            else
            {
                Console.WriteLine($"SUCCÈS : Utilisateur trouvé en base (ID: {userCheck.Id})");
            }

 
            var result = await _signInManager.PasswordSignInAsync(username, password, rememberMe, lockoutOnFailure: false);

            if (result.Succeeded)
            {
                var user = await _userManager.FindByNameAsync(username);
                if (user != null)
                {
                    var claims = await _userManager.GetClaimsAsync(user);
                    await _signInManager.SignInWithClaimsAsync(user, rememberMe, claims);
                }
                Console.WriteLine("CONNEXION RÉUSSIE ! Redirection vers Home...");
                return RedirectToAction("Index", "Home");
            }

            if (result.IsLockedOut)
            {
                Console.WriteLine("ERREUR : Le compte est BLOQUÉ (Lockout) !");
            }
            else if (result.IsNotAllowed)
            {
                Console.WriteLine("ERREUR : Connexion NON AUTORISÉE (ex: Email non confirmé) !");
            }
            else
            {
                Console.WriteLine("ERREUR : Mot de passe INCORRECT ou échec de PasswordSignInAsync !");
            }

            ModelState.AddModelError(string.Empty, "Invalid login attempt.");
            return View();
        }

        public async Task<IActionResult> Logout()
        {
            await _signInManager.SignOutAsync();
        
            return RedirectToAction("Login", "Auth");
        }

        [HttpGet]
        public IActionResult AccessDenied()
        {
            return View();
        }
    }
}