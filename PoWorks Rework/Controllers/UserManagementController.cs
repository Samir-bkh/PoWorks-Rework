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
    public class UserViewModel
    {
        public string Id { get; set; } = "";
        public string UserName { get; set; } = "";
        public string UserType { get; set; } = "Management";
        public string CompanyId { get; set; } = "";
        public string CompanyName { get; set; } = "";
        public string TenantId { get; set; } = "";
        public string TenantName { get; set; } = "";
        public bool IsEnabled { get; set; } = true;
    }

    public class CreateUserViewModel
    {
        [Required(ErrorMessage = "Username is required")]
        public string Username { get; set; } = "";

        [Required(ErrorMessage = "Password is required")]
        public string Password { get; set; } = "";

        [Required]
        public string UserType { get; set; } = "Management";

        public string? CompanyId { get; set; }
        public int? TenantId { get; set; }

        public bool CanViewPcVueConfig { get; set; }
        public bool CanViewImportExport { get; set; }
        public bool CanViewGeneralSettings { get; set; }
    }

    public class EditUserViewModel
    {
        public string Id { get; set; } = "";
        public string Username { get; set; } = "";

        [Required]
        public string UserType { get; set; } = "Management";

        public string? CompanyId { get; set; }
        public int? TenantId { get; set; }

        public bool CanViewPcVueConfig { get; set; }
        public bool CanViewImportExport { get; set; }
        public bool CanViewGeneralSettings { get; set; }

        public bool IsEnabled { get; set; } = true;

        [DataType(DataType.Password)]
        public string? NewPassword { get; set; }
    }

    [Authorize(Policy = "AdminOnly")]
    public class UserManagementController : Controller
    {
        private readonly UserManager<IdentityUser> _userManager;
        private readonly DatabaseService _databaseService;
        private readonly ICompanyContext _companyContext;

        public UserManagementController(UserManager<IdentityUser> userManager, DatabaseService databaseService, ICompanyContext companyContext)
        {
            _userManager = userManager;
            _databaseService = databaseService;
            _companyContext = companyContext;
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
                var tenantId = claims.FirstOrDefault(c => c.Type == "TenantId")?.Value ?? "";
                var companyId = claims.FirstOrDefault(c => c.Type == "CompanyId")?.Value ?? "";
                var userType = claims.FirstOrDefault(c => c.Type == "UserType")?.Value;

                if (string.Equals(user.UserName, "Admin", StringComparison.OrdinalIgnoreCase))
                    userType = "Admin";
                else if (string.IsNullOrWhiteSpace(userType))
                    userType = string.IsNullOrWhiteSpace(tenantId) ? "Management" : "Tenant";

                model.Add(new UserViewModel
                {
                    Id = user.Id,
                    UserName = user.UserName ?? "",
                    UserType = userType ?? "Management",
                    CompanyId = companyId,
                    CompanyName = companyNames.TryGetValue(companyId, out var companyName) ? companyName : (string.IsNullOrWhiteSpace(companyId) ? "Not assigned" : "Unknown"),
                    TenantId = tenantId,
                    TenantName = tenantNames.TryGetValue(tenantId, out var tenantName) ? tenantName : "",
                    IsEnabled = AccessRules.IsUserEnabled(user, DateTimeOffset.UtcNow)
                });
            }

            return View(model.OrderByDescending(x => x.UserType == "Admin").ThenBy(x => x.UserName).ToList());
        }

        [HttpGet]
        public IActionResult Create()
        {
            PopulateLists();
            var currentCompanyId = _companyContext.CurrentCompanyId;
            ViewBag.CurrentWorkspaceName = GetCompanyName(currentCompanyId);
            return View(new CreateUserViewModel
            {
                CompanyId = currentCompanyId.ToString()
            });
        }

        [HttpPost]
        public async Task<IActionResult> Create(CreateUserViewModel model)
        {
            var isTenant = string.Equals(model.UserType, "Tenant", StringComparison.OrdinalIgnoreCase);
            var currentCompanyId = _companyContext.CurrentCompanyId;

            var assignment = isTenant
                ? ValidateAndResolveTenantAssignment(model.TenantId, currentCompanyId)
                : IsCompanyActive(currentCompanyId)
                    ? (true, "", currentCompanyId, (int?)null)
                    : (false, "The active workspace is disabled or unavailable.", 0, (int?)null);

            if (!assignment.Item1)
                ModelState.AddModelError(string.Empty, assignment.Item2);

            if (!ModelState.IsValid)
            {
                PopulateLists();
                ViewBag.CurrentWorkspaceName = GetCompanyName(currentCompanyId);
                model.CompanyId = currentCompanyId.ToString();
                return View(model);
            }

            var user = new IdentityUser
            {
                UserName = model.Username.Trim(),
                LockoutEnabled = true
            };

            var result = await _userManager.CreateAsync(user, model.Password);
            if (!result.Succeeded)
            {
                foreach (var error in result.Errors)
                    ModelState.AddModelError(string.Empty, error.Description);

                PopulateLists();
                ViewBag.CurrentWorkspaceName = GetCompanyName(currentCompanyId);
                model.CompanyId = currentCompanyId.ToString();
                return View(model);
            }

            await ApplyAccessClaimsAsync(
                user,
                model.UserType,
                assignment.Item3,
                assignment.Item4,
                model.CanViewPcVueConfig,
                model.CanViewImportExport,
                model.CanViewGeneralSettings);

            await AuditTrail.LogAsync(_databaseService, HttpContext, new PoWorks_Rework.Models.AuditEvent
            {
                Action = "CREATE",
                EntityType = "User",
                EntityId = user.Id,
                CompanyId = assignment.Item3,
                Summary = $"User '{user.UserName}' created.",
                After = await BuildUserAuditSnapshotAsync(user)
            });

            TempData["SuccessMessage"] = $"User '{user.UserName}' created.";
            return RedirectToAction(nameof(Index));
        }

        [HttpGet]
        public async Task<IActionResult> Edit(string id)
        {
            var user = await _userManager.FindByIdAsync(id);
            if (user == null || string.Equals(user.UserName, "Admin", StringComparison.OrdinalIgnoreCase))
                return RedirectToAction(nameof(Index));

            var claims = await _userManager.GetClaimsAsync(user);
            var model = new EditUserViewModel
            {
                Id = user.Id,
                Username = user.UserName ?? "",
                UserType = claims.FirstOrDefault(x => x.Type == "UserType")?.Value ?? "Management",
                CompanyId = claims.FirstOrDefault(x => x.Type == "CompanyId")?.Value,
                TenantId = int.TryParse(claims.FirstOrDefault(x => x.Type == "TenantId")?.Value, out var tenantId) ? tenantId : null,
                CanViewPcVueConfig = claims.Any(x => x.Type == "Permission" && x.Value == "ViewPcVueConfig"),
                CanViewImportExport = claims.Any(x => x.Type == "Permission" && x.Value == "ViewImportExport"),
                CanViewGeneralSettings = claims.Any(x => x.Type == "Permission" && x.Value == "ViewGeneralSettings"),
                IsEnabled = !(user.LockoutEnabled && user.LockoutEnd.HasValue && user.LockoutEnd.Value > DateTimeOffset.UtcNow)
            };

            PopulateLists();
            return View(model);
        }

        [HttpPost]
        public async Task<IActionResult> Edit(EditUserViewModel model)
        {
            var user = await _userManager.FindByIdAsync(model.Id);
            if (user == null || string.Equals(user.UserName, "Admin", StringComparison.OrdinalIgnoreCase))
                return RedirectToAction(nameof(Index));

            var beforeAudit = await BuildUserAuditSnapshotAsync(user);

            var assignment = ValidateAndResolveAssignment(model.UserType, model.CompanyId, model.TenantId);
            if (!assignment.IsValid)
                ModelState.AddModelError(string.Empty, assignment.ErrorMessage);

            if (!ModelState.IsValid)
            {
                model.Username = user.UserName ?? "";
                PopulateLists();
                return View(model);
            }

            await ApplyAccessClaimsAsync(
                user,
                model.UserType,
                assignment.CompanyId,
                assignment.TenantId,
                model.CanViewPcVueConfig,
                model.CanViewImportExport,
                model.CanViewGeneralSettings);

            await _userManager.SetLockoutEnabledAsync(user, true);
            await _userManager.SetLockoutEndDateAsync(
                user,
                model.IsEnabled ? DateTimeOffset.UtcNow.AddSeconds(-1) : DateTimeOffset.UtcNow.AddYears(100));

            if (!string.IsNullOrWhiteSpace(model.NewPassword))
            {
                var token = await _userManager.GeneratePasswordResetTokenAsync(user);
                var passwordResult = await _userManager.ResetPasswordAsync(user, token, model.NewPassword);
                if (!passwordResult.Succeeded)
                {
                    foreach (var error in passwordResult.Errors)
                        ModelState.AddModelError(string.Empty, error.Description);

                    model.Username = user.UserName ?? "";
                    PopulateLists();
                    return View(model);
                }
            }

            await _userManager.UpdateSecurityStampAsync(user);

            await AuditTrail.LogAsync(_databaseService, HttpContext, new PoWorks_Rework.Models.AuditEvent
            {
                Action = "UPDATE",
                EntityType = "User",
                EntityId = user.Id,
                CompanyId = assignment.CompanyId,
                Summary = $"User '{user.UserName}' updated.",
                Before = beforeAudit,
                After = new
                {
                    Snapshot = await BuildUserAuditSnapshotAsync(user),
                    PasswordChanged = !string.IsNullOrWhiteSpace(model.NewPassword)
                }
            });

            TempData["SuccessMessage"] = $"User '{user.UserName}' updated.";
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        public async Task<IActionResult> EnableUser(string id)
        {
            var user = await _userManager.FindByIdAsync(id);
            if (user == null || string.Equals(user.UserName, "Admin", StringComparison.OrdinalIgnoreCase))
                return RedirectToAction(nameof(Index));

            var beforeAudit = await BuildUserAuditSnapshotAsync(user);

            user.LockoutEnabled = true;
            user.LockoutEnd = DateTimeOffset.UtcNow.AddSeconds(-1);
            user.AccessFailedCount = 0;

            var updateResult = await _userManager.UpdateAsync(user);
            if (!updateResult.Succeeded)
            {
                TempData["ErrorMessage"] = string.Join(" ", updateResult.Errors.Select(e => e.Description));
                return RedirectToAction(nameof(Index));
            }

            await _userManager.UpdateSecurityStampAsync(user);

            var enabledClaims = await _userManager.GetClaimsAsync(user);
            var enabledCompanyId = int.TryParse(enabledClaims.FirstOrDefault(x => x.Type == "CompanyId")?.Value, out var parsedEnabledCompanyId)
                ? parsedEnabledCompanyId
                : (int?)null;

            await AuditTrail.LogAsync(_databaseService, HttpContext, new PoWorks_Rework.Models.AuditEvent
            {
                Action = "ENABLE",
                EntityType = "User",
                EntityId = user.Id,
                CompanyId = enabledCompanyId,
                Summary = $"User '{user.UserName}' enabled.",
                Before = beforeAudit,
                After = await BuildUserAuditSnapshotAsync(user)
            });

            TempData["SuccessMessage"] = $"User '{user.UserName}' enabled.";
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        public async Task<IActionResult> DisableUser(string id)
        {
            var user = await _userManager.FindByIdAsync(id);
            if (user == null || string.Equals(user.UserName, "Admin", StringComparison.OrdinalIgnoreCase))
                return RedirectToAction(nameof(Index));

            var beforeAudit = await BuildUserAuditSnapshotAsync(user);

            user.LockoutEnabled = true;
            user.LockoutEnd = DateTimeOffset.UtcNow.AddYears(100);

            var updateResult = await _userManager.UpdateAsync(user);
            if (!updateResult.Succeeded)
            {
                TempData["ErrorMessage"] = string.Join(" ", updateResult.Errors.Select(e => e.Description));
                return RedirectToAction(nameof(Index));
            }

            await _userManager.UpdateSecurityStampAsync(user);

            var disabledClaims = await _userManager.GetClaimsAsync(user);
            var disabledCompanyId = int.TryParse(disabledClaims.FirstOrDefault(x => x.Type == "CompanyId")?.Value, out var parsedDisabledCompanyId)
                ? parsedDisabledCompanyId
                : (int?)null;

            await AuditTrail.LogAsync(_databaseService, HttpContext, new PoWorks_Rework.Models.AuditEvent
            {
                Action = "DISABLE",
                EntityType = "User",
                EntityId = user.Id,
                CompanyId = disabledCompanyId,
                Summary = $"User '{user.UserName}' disabled.",
                Before = beforeAudit,
                After = await BuildUserAuditSnapshotAsync(user)
            });

            TempData["SuccessMessage"] = $"User '{user.UserName}' disabled.";
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        public async Task<IActionResult> Delete(string id)
        {
            var user = await _userManager.FindByIdAsync(id);

            if (user != null && !string.Equals(user.UserName, "Admin", StringComparison.OrdinalIgnoreCase))
            {
                var beforeAudit = await BuildUserAuditSnapshotAsync(user);
                var claims = await _userManager.GetClaimsAsync(user);
                var companyId = int.TryParse(claims.FirstOrDefault(x => x.Type == "CompanyId")?.Value, out var parsedCompanyId)
                    ? parsedCompanyId
                    : (int?)null;

                var deleteResult = await _userManager.DeleteAsync(user);
                if (deleteResult.Succeeded)
                {
                    await AuditTrail.LogAsync(_databaseService, HttpContext, new PoWorks_Rework.Models.AuditEvent
                    {
                        Action = "DELETE",
                        EntityType = "User",
                        EntityId = user.Id,
                        CompanyId = companyId,
                        Summary = $"User '{user.UserName}' deleted. Business data preserved.",
                        Before = beforeAudit
                    });

                    TempData["SuccessMessage"] = $"User '{user.UserName}' deleted. Company, tenant and business data were preserved.";
                }
                else
                {
                    await AuditTrail.LogAsync(_databaseService, HttpContext, new PoWorks_Rework.Models.AuditEvent
                    {
                        Action = "DELETE",
                        EntityType = "User",
                        EntityId = user.Id,
                        CompanyId = companyId,
                        Summary = $"User '{user.UserName}' deletion failed.",
                        Before = beforeAudit,
                        Success = false
                    });

                    TempData["ErrorMessage"] = string.Join(" ", deleteResult.Errors.Select(e => e.Description));
                }
            }

            return RedirectToAction(nameof(Index));
        }

        private async Task<object> BuildUserAuditSnapshotAsync(IdentityUser user)
        {
            var claims = await _userManager.GetClaimsAsync(user);
            return new
            {
                user.Id,
                user.UserName,
                UserType = claims.FirstOrDefault(x => x.Type == "UserType")?.Value,
                CompanyId = claims.FirstOrDefault(x => x.Type == "CompanyId")?.Value,
                TenantId = claims.FirstOrDefault(x => x.Type == "TenantId")?.Value,
                Permissions = claims.Where(x => x.Type == "Permission").Select(x => x.Value).OrderBy(x => x).ToArray(),
                Enabled = AccessRules.IsUserEnabled(user, DateTimeOffset.UtcNow)
            };
        }

        private async Task ApplyAccessClaimsAsync(
            IdentityUser user,
            string userType,
            int companyId,
            int? tenantId,
            bool canViewPcVueConfig,
            bool canViewImportExport,
            bool canViewGeneralSettings)
        {
            var existingClaims = await _userManager.GetClaimsAsync(user);
            var managedClaims = existingClaims
                .Where(x => x.Type == "UserType" || x.Type == "CompanyId" || x.Type == "TenantId" || x.Type == "Permission")
                .ToList();

            if (managedClaims.Count > 0)
                await _userManager.RemoveClaimsAsync(user, managedClaims);

            var claims = AccessRules.BuildAccessClaims(
                userType,
                companyId,
                tenantId,
                canViewPcVueConfig,
                canViewImportExport,
                canViewGeneralSettings);

            await _userManager.AddClaimsAsync(user, claims);
        }

        private (bool, string, int, int?) ValidateAndResolveTenantAssignment(int? tenantId, int currentCompanyId)
        {
            if (!tenantId.HasValue)
                return (false, "A tenant must be selected for a Tenant user.", 0, null);

            var tenant = GetTenantAssignment(tenantId.Value);
            if (tenant == null || tenant.Value.CompanyId != currentCompanyId)
                return (false, "The selected tenant does not belong to the active workspace, is disabled, or no longer exists.", 0, null);

            return (true, "", tenant.Value.CompanyId, tenant.Value.TenantId);
        }

        private (bool IsValid, string ErrorMessage, int CompanyId, int? TenantId) ValidateAndResolveAssignment(
            string userType,
            string? companyId,
            int? tenantId)
        {
            var isTenant = string.Equals(userType, "Tenant", StringComparison.OrdinalIgnoreCase);

            if (isTenant)
            {
                if (!tenantId.HasValue)
                    return (false, "A tenant must be selected for a Tenant user.", 0, null);

                var tenant = GetTenantAssignment(tenantId.Value);
                if (tenant == null)
                    return (false, "The selected tenant is disabled, its company is disabled, or it no longer exists.", 0, null);

                return (true, "", tenant.Value.CompanyId, tenant.Value.TenantId);
            }

            if (!int.TryParse(companyId, out var parsedCompanyId) || !IsCompanyActive(parsedCompanyId))
                return (false, "An active company must be selected for a Management user.", 0, null);

            return (true, "", parsedCompanyId, null);
        }

        private void PopulateLists()
        {
            ViewBag.Companies = GetCompaniesSelectList();
            ViewBag.Tenants = GetTenantsSelectList();
        }

        private List<SelectListItem> GetCompaniesSelectList()
        {
            var items = new List<SelectListItem>
            {
                new SelectListItem { Value = "", Text = "-- Select a Company --" }
            };

            using var connection = _databaseService.CreateNewConnection();
            connection.Open();

            using var cmd = new NpgsqlCommand(@"
                SELECT ""CompanyId"", ""Name""
                FROM ""Companies""
                WHERE ""Active"" = TRUE
                ORDER BY ""Name""", connection);

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
                       COALESCE(td.""CompanyName"", t.""DisplayName"") AS ""TenantName"",
                       c.""Name"" AS ""CompanyName""
                FROM ""Tenants"" t
                INNER JOIN ""Companies"" c ON c.""CompanyId"" = t.""CompanyId"" AND c.""Active"" = TRUE
                INNER JOIN ""TenantDetails"" td ON td.""TenantID"" = t.""TenantID"" AND td.""Active"" = TRUE
                ORDER BY c.""Name"", COALESCE(td.""CompanyName"", t.""DisplayName"")", connection);

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                items.Add(new SelectListItem
                {
                    Value = reader.GetInt32(0).ToString(),
                    Text = $"{reader.GetString(2)} — {reader.GetString(1)}"
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
                dict[reader.GetInt32(0).ToString()] = reader.GetString(1);

            return dict;
        }

        private Dictionary<string, string> GetTenantNamesDictionary()
        {
            var dict = new Dictionary<string, string>();
            using var connection = _databaseService.CreateNewConnection();
            connection.Open();

            using var cmd = new NpgsqlCommand(@"
                SELECT t.""TenantID"", COALESCE(td.""CompanyName"", t.""DisplayName"")
                FROM ""Tenants"" t
                LEFT JOIN ""TenantDetails"" td ON td.""TenantID"" = t.""TenantID""", connection);

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                dict[reader.GetInt32(0).ToString()] = reader.GetString(1);

            return dict;
        }

        private string GetCompanyName(int companyId)
        {
            using var connection = _databaseService.CreateNewConnection();
            connection.Open();
            using var cmd = new NpgsqlCommand(@"SELECT ""Name"" FROM ""Companies"" WHERE ""CompanyId"" = @companyId", connection);
            cmd.Parameters.AddWithValue("companyId", companyId);
            return cmd.ExecuteScalar()?.ToString() ?? $"Workspace #{companyId}";
        }

        private bool IsCompanyActive(int companyId)
        {
            using var connection = _databaseService.CreateNewConnection();
            connection.Open();
            using var cmd = new NpgsqlCommand(@"SELECT ""Active"" FROM ""Companies"" WHERE ""CompanyId"" = @companyId", connection);
            cmd.Parameters.AddWithValue("companyId", companyId);
            return cmd.ExecuteScalar() is bool active && active;
        }

        private (int TenantId, int CompanyId)? GetTenantAssignment(int tenantId)
        {
            using var connection = _databaseService.CreateNewConnection();
            connection.Open();

            using var cmd = new NpgsqlCommand(@"
                SELECT t.""TenantID"", t.""CompanyId""
                FROM ""Tenants"" t
                INNER JOIN ""TenantDetails"" td ON td.""TenantID"" = t.""TenantID"" AND td.""Active"" = TRUE
                INNER JOIN ""Companies"" c ON c.""CompanyId"" = t.""CompanyId"" AND c.""Active"" = TRUE
                WHERE t.""TenantID"" = @tenantId", connection);

            cmd.Parameters.AddWithValue("tenantId", tenantId);
            using var reader = cmd.ExecuteReader();

            if (!reader.Read())
                return null;

            return (reader.GetInt32(0), reader.GetInt32(1));
        }
    }
}
