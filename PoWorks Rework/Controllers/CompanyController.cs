// Controllers/CompanyController.cs
using Microsoft.AspNetCore.Mvc;
using PoWorks_Rework.Models;

namespace PoWorks_Rework.Controllers
{
    public class CompanyController : Controller
    {
        public IActionResult Info()
        {
            // Create a sample company info object
            var companyInfo = new CompanyInfo
            {
                CompanyName = "PoWorks",
                RegistrationNumber = "PO123",
                Address1 = "Damansara",
                Address2 = "Kuala Lumpur",
                PostCode = "8888",
                Country = "Malaysia",
                City = "KL",
                GstId = "32184",
                GstPercentage = 6.00m,
                Phone = "123456",
                Fax = "123456",
                Email = "info@abc.com"
            };

            return View(companyInfo);
        }

        [HttpPost]
        public IActionResult SaveInfo(CompanyInfo companyInfo)
        {
            if (ModelState.IsValid)
            {
                // Save the company info to database logic goes here

                TempData["SuccessMessage"] = "Company information saved successfully.";
                return RedirectToAction("Info");
            }

            return View("Info", companyInfo);
        }

        public IActionResult Settings()
        {
            // Create a sample company settings object
            var companySettings = new CompanySettings
            {
                DateFormat = "20-12-2016",
                TimeFormat = "16:01:01",
                ReadingInterval = 60,
                OutputFolder = "C:/Output",
                Prefix = "INV",
                Suffix = "",
                NumberOfDigits = 5,
                Format = "{PREFIX}{NUMBER}{SUFFIX}",
                EmailServer = "smtp.example.com",
                EmailUsername = "user@example.com",
                EmailPassword = "••••••••",
                SmsLink = "https://sms-api.example.com",
                SmsUsername = "smsuser",
                SmsPassword = "••••••••"
            };

            return View(companySettings);
        }

        [HttpPost]
        public IActionResult SaveSettings(CompanySettings companySettings)
        {
            if (ModelState.IsValid)
            {
                // Save the company settings to database logic goes here

                TempData["SuccessMessage"] = "Company settings saved successfully.";
                return RedirectToAction("Settings");
            }

            return View("Settings", companySettings);
        }
    }
}