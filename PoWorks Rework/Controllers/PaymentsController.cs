// Controllers/PaymentsController.cs
using Microsoft.AspNetCore.Mvc;
using PoWorks_Rework.Models;
using PoWorks_Rework.Services;

namespace PoWorks_Rework.Controllers
{
    public class PaymentsController : BaseController
    {
        public PaymentsController(DatabaseService databaseService) : base(databaseService)
        {
        }

        public IActionResult Index()
        {
            // Create a sample payment view model
            var viewModel = new PaymentViewModel
            {
                BillNumber = "OR20160000007",
                TenantName = "PoWorks",
                BillAmount = 265,
                PaidAmount = 265,
                MeterName = "Meter1000",
                BillDate = "2017-2-7",
                RemainingAmount = 0
            };

            return View(viewModel);
        }

        [HttpPost]
        public IActionResult MakePayment(PaymentViewModel model)
        {
            if (ModelState.IsValid)
            {
                // Process payment logic would go here

                TempData["SuccessMessage"] = "Payment processed successfully.";
                return RedirectToAction("Index");
            }

            return View("Index", model);
        }

        [HttpPost]
        public IActionResult RetrieveBill(string billNumber)
        {
            // This would typically query a database to retrieve the bill details
            // For now, we'll return a sample payment view model
            var viewModel = new PaymentViewModel
            {
                BillNumber = billNumber,
                TenantName = "PoWorks",
                BillAmount = 265,
                PaidAmount = 265,
                MeterName = "Meter1000",
                BillDate = "2017-2-7",
                RemainingAmount = 0
            };

            return View("Index", viewModel);
        }
    }
}