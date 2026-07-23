using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Npgsql;
using PoWorks_Rework.Services;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;

namespace PoWorks_Rework.Controllers
{
    public class UserViewModel
    {
        public string Id { get; set; }
        public string UserName { get; set; }
        public string CompanyId { get; set; }
        public string CompanyName { get; set; }
    }

    
    public class CreateUserViewModel
    {
        [Required(ErrorMessage = "Username is required")]
        public string Username { get; set; }

        [Required(ErrorMessage = "Password is required")]
        public string Password { get; set; }

        public string? CompanyId { get; set; }
        public string? NewCompanyName { get; set; }

      
        public bool CanViewPcVueConfig { get; set; }
        public bool CanViewImportExport { get; set; }
        public bool CanViewGeneralSettings { get; set; }
    }

    [Authorize(Policy = "AdminOnly")]
    public class UserManagementController : Controller
    {
        private readonly UserManager<IdentityUser> _userManager;
        private readonly DatabaseService _databaseService;

        public UserManagementController(UserManager<IdentityUser> userManager, DatabaseService databaseService)
        {
            _userManager = userManager;
            _databaseService = databaseService;
        }

        public async Task<IActionResult> Index()
        {
            var users = _userManager.Users.ToList();
            var model = new List<UserViewModel>();
            var companyNames = GetCompanyNamesDictionary();

            foreach (var user in users)
            {
                var claims = await _userManager.GetClaimsAsync(user);
                var companyClaim = claims.FirstOrDefault(c => c.Type == "CompanyId");
                string companyId = companyClaim?.Value ?? "1";

                model.Add(new UserViewModel
                {
                    Id = user.Id,
                    UserName = user.UserName,
                    CompanyId = companyId,
                    CompanyName = companyNames.ContainsKey(companyId) ? companyNames[companyId] : "Inconnu"
                });
            }

            return View(model);
        }

        [HttpGet]
        public IActionResult Create()
        {
            ViewBag.Companies = GetCompaniesSelectList();
            return View(new CreateUserViewModel());
        }

        [HttpPost]
        public async Task<IActionResult> Create(CreateUserViewModel model)
        {
            if (model.CompanyId != "NEW")
            {
                ModelState.Remove("NewCompanyName");
            }

            if (ModelState.IsValid)
            {
                string companyId = model.CompanyId;
                if (companyId == "NEW" && !string.IsNullOrWhiteSpace(model.NewCompanyName))
                {
                    companyId = CreateNewCompany(model.NewCompanyName.Trim()).ToString();
                }

                var user = new IdentityUser { UserName = model.Username };
                var result = await _userManager.CreateAsync(user, model.Password);

                if (result.Succeeded)
                {
                    string assignedCompanyId = string.IsNullOrWhiteSpace(companyId) ? "1" : companyId.Trim();

                 
                    await _userManager.AddClaimAsync(user, new Claim("CompanyId", assignedCompanyId));

                    if (model.CanViewPcVueConfig)
                        await _userManager.AddClaimAsync(user, new Claim("Permission", "ViewPcVueConfig"));

                    if (model.CanViewImportExport)
                        await _userManager.AddClaimAsync(user, new Claim("Permission", "ViewImportExport"));

                    if (model.CanViewGeneralSettings)
                        await _userManager.AddClaimAsync(user, new Claim("Permission", "ViewGeneralSettings"));

                    return RedirectToAction(nameof(Index));
                }

                foreach (var error in result.Errors)
                {
                    ModelState.AddModelError(string.Empty, error.Description);
                }
            }

            ViewBag.Companies = GetCompaniesSelectList();
            return View(model);
        }

        [HttpPost]
        public async Task<IActionResult> Delete(string id)
        {
            var user = await _userManager.FindByIdAsync(id);
            if (user != null && user.UserName.ToLower() != "admin")
            {
                await _userManager.DeleteAsync(user);
            }
            return RedirectToAction(nameof(Index));
        }

        private List<SelectListItem> GetCompaniesSelectList()
        {
            var items = new List<SelectListItem>();
            using var connection = _databaseService.CreateNewConnection();
            connection.Open();

            using var cmd = new NpgsqlCommand(@"SELECT ""CompanyId"", ""Name"" FROM ""Companies"" ORDER BY ""Name""", connection);
            using var reader = cmd.ExecuteReader();

            while (reader.Read())
            {
                items.Add(new SelectListItem
                {
                    Value = reader.GetInt32(0).ToString(),
                    Text = reader.GetString(1)
                });
            }

            items.Add(new SelectListItem { Value = "NEW", Text = "+ Créer une nouvelle Company" });
            return items;
        }

        private Dictionary<string, string> GetCompanyNamesDictionary()
        {
            var dict = new Dictionary<string, string>();
            using var connection = _databaseService.CreateNewConnection();
            connection.Open();

            using var cmd = new NpgsqlCommand(@"SELECT ""CompanyId"", ""Name"" FROM ""Companies""", connection);
            using var reader = cmd.ExecuteReader();

            while (reader.Read())
            {
                dict[reader.GetInt32(0).ToString()] = reader.GetString(1);
            }
            return dict;
        }

        private int CreateNewCompany(string companyName)
        {
            using var connection = _databaseService.CreateNewConnection();
            connection.Open();

            using var cmd = new NpgsqlCommand(
                @"INSERT INTO ""Companies"" (""Name"") VALUES (@name) RETURNING ""CompanyId""",
                connection);
            cmd.Parameters.AddWithValue("name", companyName);

            return (int)cmd.ExecuteScalar();
        }
    }
}