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
    /// <summary>
    /// View model representing a user in the user management list.
    /// </summary>
    public class UserViewModel
    {
        /// <summary>
        /// The user's unique identifier.
        /// </summary>
        public string Id { get; set; }

        /// <summary>
        /// The user's username.
        /// </summary>
        public string UserName { get; set; }

        /// <summary>
        /// The company ID assigned to the user.
        /// </summary>
        public string CompanyId { get; set; }

        /// <summary>
        /// The display name of the assigned company.
        /// </summary>
        public string CompanyName { get; set; }
    }

    /// <summary>
    /// View model for creating a new user.
    /// </summary>
    public class CreateUserViewModel
    {
        /// <summary>
        /// The username for the new user.
        /// </summary>
        [Required(ErrorMessage = "Username is required")]
        public string Username { get; set; }

        /// <summary>
        /// The password for the new user.
        /// </summary>
        [Required(ErrorMessage = "Password is required")]
        public string Password { get; set; }

        /// <summary>
        /// The company ID to assign, or "NEW" to create a new company.
        /// </summary>
        public string? CompanyId { get; set; }

        /// <summary>
        /// The name of the new company when CompanyId is "NEW".
        /// </summary>
        public string? NewCompanyName { get; set; }

        /// <summary>
        /// Whether the user can view the PCVue configuration page.
        /// </summary>
        public bool CanViewPcVueConfig { get; set; }

        /// <summary>
        /// Whether the user can view the import/export page.
        /// </summary>
        public bool CanViewImportExport { get; set; }

        /// <summary>
        /// Whether the user can view the general settings page.
        /// </summary>
        public bool CanViewGeneralSettings { get; set; }
    }

    /// <summary>
    /// Controller for managing application users and their company assignments.
    /// Restricted to administrators only.
    /// </summary>
    [Authorize(Policy = "AdminOnly")]
    public class UserManagementController : Controller
    {
        private readonly UserManager<IdentityUser> _userManager;
        private readonly DatabaseService _databaseService;

        /// <summary>
        /// Initializes the user management controller with user manager and database service dependencies.
        /// </summary>
        public UserManagementController(UserManager<IdentityUser> userManager, DatabaseService databaseService)
        {
            _userManager = userManager;
            _databaseService = databaseService;
        }

        /// <summary>
        /// Displays the list of all users with their assigned companies.
        /// </summary>
        /// <returns>The user management index view.</returns>
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

        /// <summary>
        /// Displays the user creation form.
        /// </summary>
        /// <returns>The user creation view.</returns>
        [HttpGet]
        public IActionResult Create()
        {
            ViewBag.Companies = GetCompaniesSelectList();
            return View(new CreateUserViewModel());
        }

        /// <summary>
        /// Creates a new user with the selected company assignment and permission claims.
        /// </summary>
        /// <param name="model">The create user view model containing the user data.</param>
        /// <returns>A redirect to the user list, or the creation view on validation errors.</returns>
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

        /// <summary>
        /// Deletes a user, except for the built-in admin account.
        /// </summary>
        /// <param name="id">The ID of the user to delete.</param>
        /// <returns>A redirect to the user list.</returns>
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

        /// <summary>
        /// Retrieves the list of companies as dropdown options, plus an option to create a new company.
        /// </summary>
        /// <returns>A list of select list items for company selection.</returns>
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

        /// <summary>
        /// Builds a dictionary mapping company IDs to company names.
        /// </summary>
        /// <returns>A dictionary of company ID to company name.</returns>
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

        /// <summary>
        /// Creates a new company record and returns its ID.
        /// </summary>
        /// <param name="companyName">The name of the company to create.</param>
        /// <returns>The newly created company ID.</returns>
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