// Controllers/TenantController.cs
using Microsoft.AspNetCore.Mvc;
using PoWorks_Rework.Models;
using System.Collections.Generic;

namespace PoWorks_Rework.Controllers
{
    public class TenantController : Controller
    {
        // Update in Controllers/TenantController.cs
        public IActionResult Management()
        {
            // Create a sample view model with tenant data
            var viewModel = new TenantViewModel
            {
                SearchCriteria = "Company Name",
                SearchTerm = "",
                SearchResults = new List<Tenant>
        {
            new Tenant
            {
                Id = 1,
                CompanyName = "PoWorks",
                Contact = "Abdul",
                Email = "ww@cs.com",
                Phone = "3333333333",
                Outstanding = 0,
                Overdue = 0,
                Active = true
            }
        },
                SelectedTenant = new Tenant(),
                ConsumptionData = new TenantConsumptionData
                {
                    Overdue = 0,
                    TotalBilledOutstanding = 0,
                    TotalMonthUnbilled = 0,
                    // Sample data for meters
                    Meters = new List<MeterData>
            {
                new MeterData { Name = "Meter1000", Unit = "66666", LastReading = "579652", Active = true },
                new MeterData { Name = "Meter0001", Unit = "1155", LastReading = "576967", Active = true },
                new MeterData { Name = "Meter0002", Unit = "812", LastReading = "0", Active = true }
            }
                    // In a real implementation, you would also populate YearlyData and WeeklyData
                },
                TotalPages = 1,
                CurrentPage = 1,
                TotalItems = 1
            };

            return View(viewModel);
        }

        [HttpPost]
        public IActionResult Search(string searchCriteria, string searchTerm)
        {
            // This would typically query a database and return results
            // For now, we'll just redirect back to the management page
            return RedirectToAction("Management");
        }

        [HttpPost]
        public IActionResult SaveTenant(Tenant tenant)
        {
            if (ModelState.IsValid)
            {
                // Save tenant logic would go here

                TempData["SuccessMessage"] = "Tenant saved successfully.";
                return RedirectToAction("Management");
            }

            return View("Management", new TenantViewModel { SelectedTenant = tenant });
        }
    }
}