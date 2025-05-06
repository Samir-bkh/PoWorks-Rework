// Controllers/MeterController.cs
using Microsoft.AspNetCore.Mvc;
using PoWorks_Rework.Models;
using System.Collections.Generic;

namespace PoWorks_Rework.Controllers
{
    public class MeterController : Controller
    {
        public IActionResult Management()
        {
            // Create a sample view model with some data
            var viewModel = new MeterManagementViewModel
            {
                SearchCriteria = new MeterSearchCriteria
                {
                    SearchField = "Name",
                    SearchTerm = "Meter0111"
                },
                SearchResults = new List<Meter>
                {
                    new Meter
                    {
                        Id = 1,
                        Name = "Meter0111",
                        Type = "Main",
                        LastReading = "581029",
                        Active = true
                    }
                },
                SelectedMeter = new Meter
                {
                    Id = 1,
                    Name = "Meter0111",
                    Type = "Main",
                    LastReading = "581029",
                    Active = true
                },
                SubMeters = new List<Meter>(),
                TotalPages = 1,
                CurrentPage = 1,
                TotalItems = 1
            };

            return View(viewModel);
        }

        [HttpPost]
        public IActionResult Search(MeterSearchCriteria searchCriteria)
        {
            // This would typically query a database and return results
            // For now, we'll just redirect back to the management page
            return RedirectToAction("Management");
        }

        [HttpPost]
        public IActionResult UpdateMeter(Meter meter)
        {
            // This would typically update the meter in the database
            // For now, we'll just redirect back to the management page
            return RedirectToAction("Management");
        }

        // Update in Controllers/MeterController.cs
        public IActionResult Readings()
        {
            // Create a sample view model with meter readings data
            var viewModel = new MeterReadingsViewModel
            {
                Meters = new List<Meter>
        {
            new Meter { Name = "Meter1000", Type = "Main", Unit = "66666", LastReading = "579652", Active = true },
            new Meter { Name = "Meter0001", ParentMeterName = "Meter1000", Type = "Main", Unit = "1155", LastReading = "576967", Active = true },
            new Meter { Name = "Meter0002", Type = "Main", LastReading = "579705", Active = true },
            new Meter { Name = "Meter0003", Type = "Main", LastReading = "584253", Active = true },
            new Meter { Name = "Meter0004", ParentMeterName = "Meter1000", Type = "Main", Unit = "8888", LastReading = "580946", Active = true },
            new Meter { Name = "Meter0005", Type = "Main", LastReading = "585210", Active = true },
            new Meter { Name = "Meter0006", Type = "Main", LastReading = "581283", Active = true },
            new Meter { Name = "Meter0007", Type = "Main", LastReading = "588900", Active = true },
            new Meter { Name = "Meter0008", Type = "Main", LastReading = "583696", Active = true },
            new Meter { Name = "Meter0009", Type = "Main", LastReading = "578641", Active = true },
            new Meter { Name = "Meter0010", Type = "Main", LastReading = "587118", Active = true },
            new Meter { Name = "Meter0011", Type = "Main", LastReading = "572079", Active = true }
        },
                TotalPages = 85,
                CurrentPage = 1,
                TotalItems = 1011
            };

            return View(viewModel);
        }
    }
}

