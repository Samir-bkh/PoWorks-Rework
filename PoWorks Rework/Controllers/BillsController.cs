// Controllers/BillsController.cs
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using PoWorks_Rework.Models;
using PoWorks_Rework.Services;

namespace PoWorks_Rework.Controllers
{
    public class BillsController : BaseController
    {
        private readonly ILogger<BillsController> _logger;

        public BillsController(DatabaseService databaseService, ILogger<BillsController> logger)
            : base(databaseService)
        {
            _logger = logger;
        }

        public IActionResult Index()
        {
            // Check if database is initialized
            if (!_databaseService.IsInitialized)
            {
                TempData["ErrorMessage"] = "Database not configured. Please set up database first.";
                return RedirectToAction("General", "Settings");
            }

            try
            {
                // Create view model with initial data
                var viewModel = new BillsViewModel
                {
                    SearchCriteria = "Meter Name",
                    SearchTerm = "",
                    SearchResults = new List<Bill>(),
                    TotalPages = 1,
                    CurrentPage = 1,
                    TotalItems = 0
                };

                // Load meters for dropdown
                viewModel.MeterOptions = GetMeters();

                // Load tenants for dropdown
                viewModel.TenantOptions = GetTenants();

                return View(viewModel);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading initial bills data");

                // Return basic view model if error occurs
                return View(new BillsViewModel
                {
                    SearchCriteria = "Meter Name",
                    SearchTerm = "",
                    SearchResults = new List<Bill>(),
                    MeterOptions = new List<DropdownOption>(),
                    TenantOptions = new List<DropdownOption>()
                });
            }
        }

        [HttpPost]
        public IActionResult Search(string searchCriteria, string searchTerm, int page = 1)
        {
            // Check if database is initialized
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
                // Load meters and tenants for dropdowns
                viewModel.MeterOptions = GetMeters();
                viewModel.TenantOptions = GetTenants();

                // Perform search for bills
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

        // Helper method to get meters for dropdown
        private List<DropdownOption> GetMeters()
        {
            var options = new List<DropdownOption>();

            try
            {
                using (var connection = GetDatabaseConnection())
                {
                    var command = new NpgsqlCommand(@"
                        SELECT ""MeterId"", ""Name"" 
                        FROM ""Meters"" 
                        WHERE ""Active"" = true 
                        ORDER BY ""Name""", connection);

                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            options.Add(new DropdownOption
                            {
                                Value = reader.GetInt32(0).ToString(),
                                Text = reader.GetString(1)
                            });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting meters for dropdown");
            }

            return options;
        }

        // Helper method to get tenants for dropdown
        private List<DropdownOption> GetTenants()
        {
            var options = new List<DropdownOption>();

            try
            {
                using (var connection = GetDatabaseConnection())
                {
                    var command = new NpgsqlCommand(@"
                        SELECT t.""TenantID"", td.""CompanyName"" 
                        FROM ""Tenants"" t
                        JOIN ""TenantDetails"" td ON t.""TenantID"" = td.""TenantID""
                        ORDER BY td.""CompanyName""", connection);

                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            options.Add(new DropdownOption
                            {
                                Value = reader.GetInt32(0).ToString(),
                                Text = !reader.IsDBNull(1) ? reader.GetString(1) : "Unknown"
                            });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting tenants for dropdown");
            }

            return options;
        }

        // Helper class for search results pagination
        private class SearchResult
        {
            public List<Bill> Items { get; set; } = new List<Bill>();
            public int TotalCount { get; set; }
            public int TotalPages { get; set; }
        }

        // Helper method to search for bills
        private SearchResult SearchBills(string searchCriteria, string searchTerm, int page, int pageSize)
        {
            var result = new SearchResult();

            try
            {
                // For demo/prototype, we'll just create some sample bills
                // In a real application, this would query a Bills table in the database

                // Create sample bills (replace this with actual database query in production)
                var bills = new List<Bill>();
                bills.Add(new Bill
                {
                    Id = 1,
                    Tenant = "PoWorks",
                    Meter = "Meter1000",
                    BillDate = "07/02/2022",
                    TotalConsumption = 250,
                    NetTotal = 265
                });

                // If search term isn't empty, filter the results
                if (!string.IsNullOrEmpty(searchTerm))
                {
                    if (searchCriteria == "Meter Name")
                    {
                        bills = bills.FindAll(b => b.Meter.Contains(searchTerm, StringComparison.OrdinalIgnoreCase));
                    }
                    else if (searchCriteria == "Tenant")
                    {
                        bills = bills.FindAll(b => b.Tenant.Contains(searchTerm, StringComparison.OrdinalIgnoreCase));
                    }
                    else if (searchCriteria == "Bill Date")
                    {
                        bills = bills.FindAll(b => b.BillDate.Contains(searchTerm, StringComparison.OrdinalIgnoreCase));
                    }
                }

                // Calculate pagination
                result.TotalCount = bills.Count;
                result.TotalPages = (int)Math.Ceiling(result.TotalCount / (double)pageSize);

                // Apply pagination
                int startIndex = (page - 1) * pageSize;
                result.Items = bills.Skip(startIndex).Take(pageSize).ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error searching bills");
                throw;
            }

            return result;
        }
    }
}