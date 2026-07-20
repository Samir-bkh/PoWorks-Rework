using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using PoWorks_Rework.Models;
using PoWorks_Rework.Services;
using System.Security.Claims;

namespace PoWorks_Rework.Controllers
{
    public class AuthController : Controller
    {
        private readonly DatabaseService _databaseService;

        public AuthController(DatabaseService databaseService)
        {
            _databaseService = databaseService;
        }

        [HttpGet]
        public async Task<IActionResult> Login()
        {
            // Vérifie et crée l'Admin par défaut si la table est vide
            await EnsureAdminUserExistsAsync();

            // Si déjà connecté, on redirige vers l'accueil
            if (User.Identity != null && User.Identity.IsAuthenticated)
            {
                return Redirect("/");
            }
            return View();
        }

        // On fusionne HttpGet et HttpPost pour régler le problème de doublon
        [HttpGet, HttpPost]
        public async Task<IActionResult> Logout()
        {
            // Détruit le cookie de connexion
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return RedirectToAction("Login");
        }

        [HttpPost]
        public async Task<IActionResult> Login(string username, string password)
        {
            if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
            {
                ViewBag.Error = "Veuillez entrer un nom d'utilisateur et un mot de passe.";
                return View();
            }

            User? user = null;
            try
            {
                // Pas de RLS ici car on a besoin de chercher dans tout le système
                using var connection = _databaseService.CreateNewConnection();
                await connection.OpenAsync();

                string sql = @"SELECT ""UserId"", ""Username"", ""PasswordHash"", ""Role"", ""CompanyId"", ""IsActive"" 
                               FROM ""Users"" WHERE ""Username"" = @username";

                using var cmd = new NpgsqlCommand(sql, connection);
                cmd.Parameters.AddWithValue("username", username);

                using var reader = await cmd.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    user = new User
                    {
                        UserId = reader.GetInt32(0),
                        Username = reader.GetString(1),
                        PasswordHash = reader.GetString(2),
                        Role = reader.GetString(3),
                        CompanyId = reader.GetInt32(4),
                        IsActive = reader.GetBoolean(5)
                    };
                }
            }
            catch (Exception ex)
            {
                ViewBag.Error = "Erreur de connexion à la base de données : " + ex.Message;
                return View();
            }

            // --- DÉBUT MIGRATION AUTOMATIQUE BCRYPT ---
            // Si l'utilisateur existe et que son mot de passe en BDD n'est pas un hash BCrypt (qui commence toujours par $2)
            if (user != null && !user.PasswordHash.StartsWith("$2"))
            {
                // On vérifie si ce qu'il a tapé correspond à l'ancien mot de passe en clair
                if (password == user.PasswordHash)
                {
                    // Si oui, on génère le nouveau hash sécurisé et on met à jour la base de données !
                    string newHash = BCrypt.Net.BCrypt.HashPassword(password);
                    using var updateConn = _databaseService.CreateNewConnection();
                    await updateConn.OpenAsync();
                    using var updateCmd = new NpgsqlCommand("UPDATE \"Users\" SET \"PasswordHash\" = @hash WHERE \"UserId\" = @id", updateConn);
                    updateCmd.Parameters.AddWithValue("hash", newHash);
                    updateCmd.Parameters.AddWithValue("id", user.UserId);
                    await updateCmd.ExecuteNonQueryAsync();

                    // On met à jour l'objet en mémoire pour que la vérification suivante réussisse
                    user.PasswordHash = newHash;
                }
            }
            // --- FIN MIGRATION AUTOMATIQUE BCRYPT ---

            // Vérification BCrypt ultra-sécurisée
            if (user == null || !user.IsActive || !BCrypt.Net.BCrypt.Verify(password, user.PasswordHash))
            {
        
                ViewBag.Error = "Nom d'utilisateur ou mot de passe incorrect.";
                return View();
            }

            // --- C'EST ICI LA MAGIE DU MULTI-CLIENTS ---
            // On stocke le CompanyId dans le cookie de session de l'utilisateur
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.Name, user.Username),
                new Claim(ClaimTypes.Role, user.Role),
                new Claim("CompanyId", user.CompanyId.ToString())
            };

            var claimsIdentity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);

            await HttpContext.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                new ClaimsPrincipal(claimsIdentity),
                new AuthenticationProperties { IsPersistent = true });

            return Redirect("/");
        }

        // Fonction magique qui crée le compte admin la première fois
        private async Task EnsureAdminUserExistsAsync()
        {
            try
            {
                using var connection = _databaseService.CreateNewConnection();
                await connection.OpenAsync();
                using var cmdCount = new NpgsqlCommand("SELECT COUNT(*) FROM \"Users\"", connection);
                var count = Convert.ToInt32(await cmdCount.ExecuteScalarAsync());

                if (count == 0)
                {
                    string hash = BCrypt.Net.BCrypt.HashPassword("admin");
                    using var cmdInsert = new NpgsqlCommand(
                        "INSERT INTO \"Users\" (\"Username\", \"PasswordHash\", \"Role\", \"CompanyId\") VALUES ('admin', @hash, 'SuperAdmin', 1)", connection);
                    cmdInsert.Parameters.AddWithValue("hash", hash);
                    await cmdInsert.ExecuteNonQueryAsync();
                }
            }
            catch { /* La base n'est peut-être pas prête, on ignore */ }
        }
    }
}