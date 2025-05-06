// Controllers/BillsController.cs
using Microsoft.AspNetCore.Mvc;
using PoWorks_Rework.Models;
using System.Collections.Generic;

namespace PoWorks_Rework.Controllers
{
    public class BillsController : Controller
    {
        public IActionResult Index()
        {
            // Create a sample view model with bill data
            var viewModel = new BillsViewModel
            {
                SearchCriteria = "Meter Name",
                SearchTerm = "Meter1000",
                SearchResults = new List<Bill>
                {
                    new Bill
                    {
                        Id = 1,
                        Tenant = "PoWorks",
                        Meter = "Meter1000",
                        BillDate = "07/02/2017",
                        TotalConsumption = 250,
                        NetTotal = 265
                    }
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
            // For now, we'll just redirect back to the index page
            return RedirectToAction("Index");
        }
    }
}