using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Npgsql;
using PoWorks_Rework.Services;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;

namespace PoWorks_Rework.Controllers
{
    /// <summary>
    /// View model representing a user in the user management list.
    /// </summary>
    public class UserViewModel
    {
        public string Id { get; set; } = "";
        public string UserName { get; set; } = "";
        public string UserType { get; set; } = "Management";
        public string CompanyId { get; set; } = "";
        public string CompanyName { get; set; } = "";
        public string TenantId { get; set; } = "";
        public string TenantName { get; set; } = "";
    }

    /// <summary>
    /// View model for creating a new user.
    /// </summary>
    public class CreateUserViewModel
    {
        [Required(ErrorMessage = "Username is required")]
        public string Username { get; set; } = "";

        [Required(ErrorMessage = "Password is required")]
        public string Password { get; set; } = "";

        [Required]
        public string UserType { get; set; } = "Management";

        public string? CompanyId { get; set; }
        public string? NewCompanyName { get; set; }

        public int? TenantId { get; set; }

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
            var tenantNames = GetTenantNamesDictionary();

            foreach (var user in users)
            {
                var claims = await _userManager.GetClaimsAsync(user);

                var userType = claims.FirstOrDefault(c => c.Type == "UserType")?.Value;
                var companyId = claims.FirstOrDefault(c => c.Type == "CompanyId")?.Value ?? "1";
                var tenantId = claims.FirstOrDefault(c => c.Type == "TenantId")?.Value ?? "";

                if (string.Equals(user.UserName, "Admin", StringComparison.OrdinalIgnoreCase))
                {
                    userType = "Admin";
                }
                else if (string.IsNullOrWhiteSpace(userType))
                {
                    userType = string.IsNullOrWhiteSpace(tenantId) ? "Management" : "Tenant";
                }

                model.Add(new UserViewModel
                {
                    Id = user.Id,
                    UserName = user.UserName ?? "",
                    UserType = userType,
                    CompanyId = companyId,
                    CompanyName = companyNames.TryGetValue(companyId, out var companyName) ? companyName : "Unknown",
                    TenantId = tenantId,
                    TenantName = tenantNames.TryGetValue(tenantId, out var tenantName) ? tenantName : ""
                });
            }

            return View(model);
        }

        [HttpGet]
        public IActionResult Create()
        {
            PopulateCreateLists();
            return View(new CreateUserViewModel());
        }

        [HttpPost]
        public async Task<IActionResult> Create(CreateUserViewModel model)
        {
            var isTenant = string.Equals(model.UserType, "Tenant", StringComparison.OrdinalIgnoreCase);

            if (isTenant)
            {
                ModelState.Remove(nameof(model.CompanyId));
                ModelState.Remove(nameof(model.NewCompanyName));

                if (!model.TenantId.HasValue)
                {
                    ModelState.AddModelError(nameof(model.TenantId), "A tenant must be selected for a tenant user.");
                }
            }
            else
            {
                ModelState.Remove(nameof(model.TenantId));

                if (model.CompanyId != "NEW")
                {
                    ModelState.Remove(nameof(model.NewCompanyName));
                }
                else if (string.IsNullOrWhiteSpace(model.NewCompanyName))
                {
                    ModelState.AddModelError(nameof(model.NewCompanyName), "Company name is required.");
                }
            }

            if (!ModelState.IsValid)
            {
                PopulateCreateLists();
                return View(model);
            }

            string assignedCompanyId;
            int? assignedTenantId = null;

            if (isTenant)
            {
                var tenantInfo = GetTenantAssignment(model.TenantId!.Value);
                if (tenantInfo == null)
                {
                    ModelState.AddModelError(nameof(model.TenantId), "The selected tenant no longer exists.");
                    PopulateCreateLists();
                    return View(model);
                }

                assignedTenantId = tenantInfo.Value.TenantId;
                assignedCompanyId = tenantInfo.Value.CompanyId.ToString();
            }
            else
            {
                var companyId = model.CompanyId;

                if (companyId == "NEW")
                {
                    companyId = CreateNewCompany(model.NewCompanyName!.Trim()).ToString();
                }

                assignedCompanyId = string.IsNullOrWhiteSpace(companyId) ? "1" : companyId.Trim();
            }

            var user = new IdentityUser { UserName = model.Username };
            var result = await _userManager.CreateAsync(user, model.Password);

            if (!result.Succeeded)
            {
                foreach (var error in result.Errors)
                {
                    ModelState.AddModelError(string.Empty, error.Description);
                }

                PopulateCreateLists();
                return View(model);
            }

            await _userManager.AddClaimAsync(user, new Claim("UserType", isTenant ? "Tenant" : "Management"));
            await _userManager.AddClaimAsync(user, new Claim("CompanyId", assignedCompanyId));

            if (isTenant)
            {
                await _userManager.AddClaimAsync(user, new Claim("TenantId", assignedTenantId!.Value.ToString()));
            }
            else
            {
                if (model.CanViewPcVueConfig)
                    await _userManager.AddClaimAsync(user, new Claim("Permission", "ViewPcVueConfig"));

                if (model.CanViewImportExport)
                    await _userManager.AddClaimAsync(user, new Claim("Permission", "ViewImportExport"));

                if (model.CanViewGeneralSettings)
                    await _userManager.AddClaimAsync(user, new Claim("Permission", "ViewGeneralSettings"));
            }

            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        public async Task<IActionResult> Delete(string id)
        {
            var user = await _userManager.FindByIdAsync(id);

            if (user != null && !string.Equals(user.UserName, "Admin", StringComparison.OrdinalIgnoreCase))
            {
                await _userManager.DeleteAsync(user);
            }

            return RedirectToAction(nameof(Index));
        }

        private void PopulateCreateLists()
        {
            ViewBag.Companies = GetCompaniesSelectList();
            ViewBag.Tenants = GetTenantsSelectList();
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

            items.Add(new SelectListItem { Value = "NEW", Text = "+ Create a New Company" });
            return items;
        }

        private List<SelectListItem> GetTenantsSelectList()
        {
            var items = new List<SelectListItem>
            {
                new SelectListItem { Value = "", Text = "-- Select a Tenant --" }
            };

            using var connection = _databaseService.CreateNewConnection();
            connection.Open();

            using var cmd = new NpgsqlCommand(@"
                SELECT t.""TenantID"",
                       COALESCE(td.""CompanyName"", t.""DisplayName"") AS ""TenantName""
                FROM ""Tenants"" t
                LEFT JOIN ""TenantDetails"" td ON td.""TenantID"" = t.""TenantID""
                ORDER BY COALESCE(td.""CompanyName"", t.""DisplayName"")", connection);

            using var reader = cmd.ExecuteReader();

            while (reader.Read())
            {
                items.Add(new SelectListItem
                {
                    Value = reader.GetInt32(0).ToString(),
                    Text = reader.GetString(1)
                });
            }

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

        private Dictionary<string, string> GetTenantNamesDictionary()
        {
            var dict = new Dictionary<string, string>();

            using var connection = _databaseService.CreateNewConnection();
            connection.Open();

            using var cmd = new NpgsqlCommand(@"
                SELECT t.""TenantID"",
                       COALESCE(td.""CompanyName"", t.""DisplayName"") AS ""TenantName""
                FROM ""Tenants"" t
                LEFT JOIN ""TenantDetails"" td ON td.""TenantID"" = t.""TenantID""", connection);

            using var reader = cmd.ExecuteReader();

            while (reader.Read())
            {
                dict[reader.GetInt32(0).ToString()] = reader.GetString(1);
            }

            return dict;
        }

        private (int TenantId, int CompanyId)? GetTenantAssignment(int tenantId)
        {
            using var connection = _databaseService.CreateNewConnection();
            connection.Open();

            using var cmd = new NpgsqlCommand(@"
                SELECT ""TenantID"", COALESCE(""CompanyId"", 1)
                FROM ""Tenants""
                WHERE ""TenantID"" = @tenantId", connection);

            cmd.Parameters.AddWithValue("tenantId", tenantId);

            using var reader = cmd.ExecuteReader();

            if (!reader.Read())
            {
                return null;
            }

            return (reader.GetInt32(0), reader.GetInt32(1));
        }

        private int CreateNewCompany(string companyName)
        {
            using var connection = _databaseService.CreateNewConnection();
            connection.Open();

            using var cmd = new NpgsqlCommand(
                @"INSERT INTO ""Companies"" (""Name"") VALUES (@name) RETURNING ""CompanyId""",
                connection);

            cmd.Parameters.AddWithValue("name", companyName);

            return (int)cmd.ExecuteScalar()!;
        }
    }
}
