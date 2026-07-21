using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;

namespace PoWorks_Rework.Controllers
{
   
    public class UserViewModel
    {
        public string Id { get; set; }
        public string UserName { get; set; }
        public string TenantId { get; set; }
    }

    [Authorize(Policy = "AdminOnly")]
    public class UserManagementController : Controller
    {
        private readonly UserManager<IdentityUser> _userManager;

        public UserManagementController(UserManager<IdentityUser> userManager)
        {
            _userManager = userManager;
        }

        public async Task<IActionResult> Index()
        {
            var users = _userManager.Users.ToList();
            var model = new List<UserViewModel>();

            foreach (var user in users)
            {
                var claims = await _userManager.GetClaimsAsync(user);
                var tenantClaim = claims.FirstOrDefault(c => c.Type == "TenantId");

                model.Add(new UserViewModel
                {
                    Id = user.Id,
                    UserName = user.UserName,
                    TenantId = tenantClaim?.Value ?? "N/A"
                });
            }

            return View(model);
        }

        [HttpGet]
        public IActionResult Create()
        {
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> Create(string username, string password, string tenantId)
        {
            if (ModelState.IsValid)
            {
                var user = new IdentityUser { UserName = username };
                var result = await _userManager.CreateAsync(user, password);

                if (result.Succeeded)
                {
                   
                    if (!string.IsNullOrWhiteSpace(tenantId))
                    {
                        await _userManager.AddClaimAsync(user, new Claim("TenantId", tenantId.Trim()));
                    }

                    return RedirectToAction(nameof(Index));
                }

                foreach (var error in result.Errors)
                {
                    ModelState.AddModelError(string.Empty, error.Description);
                }
            }
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> Delete(string id)
        {
            var user = await _userManager.FindByIdAsync(id);
            if (user != null)
            {
                if (user.UserName.ToLower() != "admin")
                {
                    await _userManager.DeleteAsync(user);
                }
            }
            return RedirectToAction(nameof(Index));
        }
    }
}