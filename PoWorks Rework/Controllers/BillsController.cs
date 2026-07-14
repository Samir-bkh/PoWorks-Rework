using Microsoft.AspNetCore.Mvc;
using Npgsql;
using PoWorks_Rework.Models;
using PoWorks_Rework.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace PoWorks_Rework.Controllers
{
    public class BillsController : BaseController
    {
        private readonly ILogger<BillsController> _logger;
        private readonly BillingService _billingService; // 👈 1. Le moteur de calcul est là !

        public BillsController(DatabaseService databaseService, BillingService billingService, ILogger<BillsController> logger)
            : base(databaseService)
        {
            _logger = logger;
            _billingService = billingService;
        }

        public IActionResult Index()
        {
            if (!_databaseService.IsInitialized)
            {
                TempData["ErrorMessage"] = "Database not configured. Please set up database first.";
                return RedirectToAction("General", "Settings");
            }

            try
            {
                var viewModel = new BillsViewModel
                {
                    SearchCriteria = "Tenant",
                    SearchTerm = "",
                    SearchResults = new List<Bill>(),
                    TotalPages = 1,
                    CurrentPage = 1,
                    TotalItems = 0
                };

                viewModel.MeterOptions = GetMeters();
                viewModel.TenantOptions = GetTenants();

                // Load initial real data
                var searchResults = SearchBills("Tenant", "", 1, 10);
                viewModel.SearchResults = searchResults.Items;
                viewModel.TotalItems = searchResults.TotalCount;
                viewModel.TotalPages = searchResults.TotalPages;

                return View(viewModel);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading initial bills data");
                return View(new BillsViewModel());
            }
        }

        [HttpPost]
        public IActionResult Search(string searchCriteria, string searchTerm, int page = 1)
        {
            if (!_databaseService.IsInitialized)
            {
                TempData["ErrorMessage"] = "Database not configured. Please set up database first.";
                return RedirectToAction("General", "Settings");
            }

            var viewModel = new BillsViewModel
            {
                SearchCriteria = searchCriteria,
                SearchTerm = searchTerm,
                CurrentPage = page,
                SearchResults = new List<Bill>(),
                TotalPages = 1,
                TotalItems = 0
            };

            try
            {
                viewModel.MeterOptions = GetMeters();
                viewModel.TenantOptions = GetTenants();

                var searchResults = SearchBills(searchCriteria, searchTerm, page, 10);
                viewModel.SearchResults = searchResults.Items;
                viewModel.TotalItems = searchResults.TotalCount;
                viewModel.TotalPages = searchResults.TotalPages;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error searching bills");
                TempData["ErrorMessage"] = $"Database error: {ex.Message}";
            }

            return View("Index", viewModel);
        }

        // 👈 2. NOUVEAU : Une méthode pour déclencher le calcul !
        [HttpPost]
        public async Task<IActionResult> GenerateBillTest(int tenantId, DateTime startDate, DateTime endDate)
        {
            try
            {
                // 1. Appelle le service pour calculer la facture
                var newBill = await _billingService.CalculateBillAsync(tenantId, startDate, endDate);

                // 2. Sauvegarde la facture dans la base de données
                await _billingService.SaveBillAsync(newBill);

                TempData["SuccessMessage"] = $"SUCCESS! Bill calculated AND SAVED for {newBill.TenantName}. Grand Total: RM {newBill.AmountInclTax}";
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = $"Calculation Error: {ex.Message}";
            }

            return RedirectToAction("Index");
        }

        [HttpGet]
        public IActionResult Details(int id)
        {
            try
            {
                using var connection = GetDatabaseConnection();

                // 1. On récupère les infos principales de la facture
                string billQuery = @"
                    SELECT b.""BillId"", t.""DisplayName"", b.""PeriodStart"", b.""PeriodEnd"", 
                           b.""TotalKWh"", b.""SubTotal"", b.""TaxAmount"", b.""GrandTotal"", b.""Status"", b.""GeneratedAt""
                    FROM ""Bills"" b
                    JOIN ""Tenants"" t ON b.""TenantID"" = t.""TenantID""
                    WHERE b.""BillId"" = @id";

                using var cmdBill = new NpgsqlCommand(billQuery, connection);
                cmdBill.Parameters.AddWithValue("id", id);

                using var reader = cmdBill.ExecuteReader();
                if (!reader.Read())
                {
                    TempData["ErrorMessage"] = "Bill not found.";
                    return RedirectToAction("Index");
                }

                var bill = new BillEntity
                {
                    BillId = reader.GetInt32(0),
                    TenantName = reader.GetString(1),
                    PeriodStart = reader.GetDateTime(2),
                    PeriodEnd = reader.GetDateTime(3),
                    TotalKWh = reader.GetDecimal(4),
                    AmountExclTax = reader.GetDecimal(5),
                    TaxAmount = reader.GetDecimal(6),
                    AmountInclTax = reader.GetDecimal(7),
                    Status = reader.GetString(8),
                    GeneratedAt = reader.GetDateTime(9)
                };

                // Très important : fermer le premier lecteur avant d'en ouvrir un second
                reader.Close();

                // 2. On récupère le détail de chaque compteur lié à cette facture
                string lineQuery = @"
                    SELECT ""MeterName"", ""Consumption"", ""Unit"", ""UnitPrice"", ""LineTotal""
                    FROM ""BillLineItems""
                    WHERE ""BillId"" = @id";

                using var cmdLine = new NpgsqlCommand(lineQuery, connection);
                cmdLine.Parameters.AddWithValue("id", id);

                using var lineReader = cmdLine.ExecuteReader();
                while (lineReader.Read())
                {
                    bill.LineItems.Add(new BillLineItemEntity
                    {
                        MeterName = lineReader.GetString(0),
                        Consumption = lineReader.GetDecimal(1),
                        Unit = lineReader.GetString(2),
                        UnitPrice = lineReader.GetDecimal(3),
                        LineTotalExclTax = lineReader.GetDecimal(4)
                    });
                }

                return View(bill);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading bill details");
                TempData["ErrorMessage"] = "Error loading bill details.";
                return RedirectToAction("Index");
            }
        }

        private List<DropdownOption> GetMeters()
        {
            var options = new List<DropdownOption>();
            try
            {
                using var connection = GetDatabaseConnection();
                var command = new NpgsqlCommand(@"SELECT ""MeterId"", ""Name"" FROM ""Meters"" WHERE ""Active"" = true ORDER BY ""Name""", connection);
                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    options.Add(new DropdownOption { Value = reader.GetInt32(0).ToString(), Text = reader.GetString(1) });
                }
            }
            catch (Exception ex) { _logger.LogError(ex, "Error getting meters"); }
            return options;
        }

        private List<DropdownOption> GetTenants()
        {
            var options = new List<DropdownOption>();
            try
            {
                using var connection = GetDatabaseConnection();
                var command = new NpgsqlCommand(@"
                    SELECT t.""TenantID"", td.""CompanyName"" 
                    FROM ""Tenants"" t
                    JOIN ""TenantDetails"" td ON t.""TenantID"" = td.""TenantID""
                    ORDER BY td.""CompanyName""", connection);

                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    options.Add(new DropdownOption
                    {
                        Value = reader.GetInt32(0).ToString(),
                        Text = !reader.IsDBNull(1) ? reader.GetString(1) : "Unknown"
                    });
                }
            }
            catch (Exception ex) { _logger.LogError(ex, "Error getting tenants"); }
            return options;
        }

        private class SearchResult
        {
            public List<Bill> Items { get; set; } = new List<Bill>();
            public int TotalCount { get; set; }
            public int TotalPages { get; set; }
        }

        // 👈 3. CORRECTION : On lit les vraies données SQL
        private SearchResult SearchBills(string searchCriteria, string searchTerm, int page, int pageSize)
        {
            var result = new SearchResult();
            var bills = new List<Bill>();

            try
            {
                using var connection = GetDatabaseConnection();

                // Requête pour lire la vraie table "Bills"
                string query = @"
                    SELECT b.""BillId"", t.""DisplayName"", b.""PeriodStart"", b.""TotalKWh"", b.""GrandTotal""
                    FROM ""Bills"" b
                    JOIN ""Tenants"" t ON b.""TenantID"" = t.""TenantID""
                    WHERE 1=1 ";

                // Ajout des filtres si besoin
                if (!string.IsNullOrEmpty(searchTerm))
                {
                    if (searchCriteria == "Tenant")
                        query += $" AND t.\"DisplayName\" ILIKE '%{searchTerm}%'";
                }

                query += " ORDER BY b.\"GeneratedAt\" DESC";

                using var command = new NpgsqlCommand(query, connection);
                using var reader = command.ExecuteReader();

                while (reader.Read())
                {
                    bills.Add(new Bill
                    {
                        Id = reader.GetInt32(0),
                        Tenant = reader.GetString(1),
                        Meter = "Multi-Meter", // Une facture regroupe désormais plusieurs compteurs
                        BillDate = reader.GetDateTime(2).ToString("yyyy-MM-dd"),
                        TotalConsumption = reader.IsDBNull(3) ? 0 : reader.GetDecimal(3),
                        NetTotal = reader.IsDBNull(4) ? 0 : reader.GetDecimal(4)
                    });
                }

                result.TotalCount = bills.Count;
                result.TotalPages = (int)Math.Ceiling(result.TotalCount / (double)pageSize);

                int startIndex = (page - 1) * pageSize;
                result.Items = bills.Skip(startIndex).Take(pageSize).ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error searching real bills");
            }

            return result;
        }
    }
}