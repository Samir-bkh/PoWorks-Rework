// Controllers/TenantController.cs
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Npgsql;
using PoWorks_Rework.Models;
using PoWorks_Rework.Services;
using System;
using System.Collections.Generic;

namespace PoWorks_Rework.Controllers
{
    /// <summary>
    /// Main controller for tenant management operations
    /// </summary>
    public class TenantController : BaseController
    {
        private readonly ILogger<TenantController> _logger;

        public TenantController(DatabaseService databaseService, ILogger<TenantController> logger)
            : base(databaseService)
        {
            _logger = logger;
        }

        /// <summary>
        /// Main tenant management page
        /// </summary>
        public IActionResult Management(int? id = null)
        {
            // Check if database is initialized
            if (!_databaseService.IsInitialized)
            {
                TempData["ErrorMessage"] = "Database not configured. Please set up database first.";
                return RedirectToAction("General", "Settings");
            }

            var viewModel = new TenantViewModel
            {
                SearchCriteria = "Company Name",
                SearchTerm = "",
                SearchResults = new List<Tenant>(),
                SelectedTenant = new Tenant(), // Start with an empty tenant by default
                ConsumptionData = new TenantConsumptionData(),
                TotalPages = 1,
                CurrentPage = 1,
                TotalItems = 0
            };

            try
            {
                // If id is provided, load that specific tenant
                if (id.HasValue && id.Value > 0)
                {
                    viewModel.SelectedTenant = GetTenantDetailsById(id.Value);
                    // Load search results to show the current tenant in the list
                    viewModel.SearchResults = GetTenants("", "", 1, 10).Items;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading tenant data");
                TempData["ErrorMessage"] = $"Database error: {ex.Message}";
            }

            return View(viewModel);
        }

        /// <summary>
        /// Search for tenants based on criteria
        /// </summary>
        [HttpPost]
        public IActionResult Search(string searchCriteria, string searchTerm, int page = 1)
        {
            if (!_databaseService.IsInitialized)
            {
                TempData["ErrorMessage"] = "Database not configured";
                return RedirectToAction("General", "Settings");
            }

            var viewModel = new TenantViewModel
            {
                SearchCriteria = searchCriteria,
                SearchTerm = searchTerm,
                CurrentPage = page,
                SelectedTenant = new Tenant(),
                ConsumptionData = new TenantConsumptionData()
            };

            try
            {
                // Get search results
                var results = GetTenants(searchCriteria, searchTerm, page, 10);
                viewModel.SearchResults = results.Items;
                viewModel.TotalItems = results.TotalCount;
                viewModel.TotalPages = results.TotalPages;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error searching tenants");
                TempData["ErrorMessage"] = $"Database error: {ex.Message}";
            }

            return View("Management", viewModel);
        }

        /// <summary>
        /// Helper class for search results pagination
        /// </summary>
        private class SearchResult
        {
            public List<Tenant> Items { get; set; } = new List<Tenant>();
            public int TotalCount { get; set; }
            public int TotalPages { get; set; }
        }

        /// <summary>
        /// Get list of tenants based on search criteria
        /// </summary>
        private SearchResult GetTenants(string searchCriteria, string searchTerm, int page, int pageSize)
        {
            var result = new SearchResult();

            try
            {
                string whereClause = string.IsNullOrEmpty(searchTerm) ? "" :
                    searchCriteria switch
                    {
                        "Company Name" => @"WHERE ""td"".""CompanyName"" ILIKE @searchTerm",
                        "Contact" => @"WHERE ""td"".""ContactName"" ILIKE @searchTerm",
                        "Email" => @"WHERE ""td"".""ContactEmail"" ILIKE @searchTerm",
                        "Phone" => @"WHERE ""td"".""ContactPhone"" ILIKE @searchTerm",
                        _ => @"WHERE ""td"".""CompanyName"" ILIKE @searchTerm"
                    };

                using (var connection = GetDatabaseConnection())
                {
                    // Get count for pagination
                    string countSql = @"
                        SELECT COUNT(*) 
                        FROM ""Tenants"" ""t""
                        LEFT JOIN ""TenantDetails"" ""td"" ON ""t"".""TenantID"" = ""td"".""TenantID""
                        " + whereClause;

                    using (var countCommand = new NpgsqlCommand(countSql, connection))
                    {
                        if (!string.IsNullOrEmpty(searchTerm))
                        {
                            countCommand.Parameters.AddWithValue("@searchTerm", $"%{searchTerm}%");
                        }

                        result.TotalCount = Convert.ToInt32(countCommand.ExecuteScalar());
                        result.TotalPages = (int)Math.Ceiling(result.TotalCount / (double)pageSize);
                    }

                    // Get tenant data with pagination
                    int offset = (page - 1) * pageSize;
                    string searchSql = @"
                        SELECT 
                            ""t"".""TenantID"",
                            ""td"".""CompanyName"",
                            ""td"".""ContactName"",
                            ""td"".""ContactEmail"",
                            ""td"".""ContactPhone"",
                            0 AS Outstanding,
                            0 AS Overdue,
                            TRUE AS Active
                        FROM ""Tenants"" ""t""
                        LEFT JOIN ""TenantDetails"" ""td"" ON ""t"".""TenantID"" = ""td"".""TenantID""
                        " + whereClause + @"
                        ORDER BY ""td"".""CompanyName""
                        LIMIT @pageSize OFFSET @offset";

                    using (var searchCommand = new NpgsqlCommand(searchSql, connection))
                    {
                        if (!string.IsNullOrEmpty(searchTerm))
                        {
                            searchCommand.Parameters.AddWithValue("@searchTerm", $"%{searchTerm}%");
                        }

                        searchCommand.Parameters.AddWithValue("@pageSize", pageSize);
                        searchCommand.Parameters.AddWithValue("@offset", offset);

                        using (var reader = searchCommand.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                result.Items.Add(new Tenant
                                {
                                    Id = reader.GetInt32(0),
                                    CompanyName = !reader.IsDBNull(1) ? reader.GetString(1) : "",
                                    Contact = !reader.IsDBNull(2) ? reader.GetString(2) : "",
                                    Email = !reader.IsDBNull(3) ? reader.GetString(3) : "",
                                    Phone = !reader.IsDBNull(4) ? reader.GetString(4) : "",
                                    Outstanding = reader.GetDecimal(5),
                                    Overdue = reader.GetDecimal(6),
                                    Active = reader.GetBoolean(7)
                                });
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting tenants");
                throw;
            }

            return result;
        }

        /// <summary>
        /// Get detailed information for a specific tenant
        /// </summary>
        private Tenant GetTenantDetailsById(int id)
        {
            var tenant = new Tenant();

            try
            {
                using (var connection = GetDatabaseConnection())
                {
                    var command = new NpgsqlCommand(@"
                        SELECT 
                            ""t"".""TenantID"", 
                            ""td"".""CompanyName"", 
                            ""td"".""ContactName"", 
                            ""td"".""ContactEmail"", 
                            ""td"".""ContactPhone"",
                            ""td"".""CompanyAddress"",
                            ""td"".""CompanyLocation"",
                            ""td"".""CompanyMisc"",
                            COALESCE(""td"".""Tarif_1""::numeric, 0.0),
                            COALESCE(""td"".""Tarif_2""::numeric, 0.0),
                            COALESCE(""td"".""Tarif_3""::numeric, 0.0)
                        FROM ""Tenants"" ""t""
                        LEFT JOIN ""TenantDetails"" ""td"" ON ""t"".""TenantID"" = ""td"".""TenantID""
                        WHERE ""t"".""TenantID"" = @tenantId", connection);

                    command.Parameters.AddWithValue("@tenantId", id);

                    using (var reader = command.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            tenant.Id = reader.GetInt32(0);

                            tenant.CompanyName = !reader.IsDBNull(1) ? reader.GetString(1) : "";
                            tenant.Contact = !reader.IsDBNull(2) ? reader.GetString(2) : "";
                            tenant.Email = !reader.IsDBNull(3) ? reader.GetString(3) : "";
                            tenant.Phone = !reader.IsDBNull(4) ? reader.GetString(4) : "";

                            // Parse address fields
                            string address = !reader.IsDBNull(5) ? reader.GetString(5) : "";
                            string[] addressParts = address.Split(new[] { ", " }, StringSplitOptions.RemoveEmptyEntries);
                            tenant.Address1 = addressParts.Length > 0 ? addressParts[0] : "";
                            tenant.Address2 = addressParts.Length > 1 ? addressParts[1] : "";

                            // Parse location
                            string location = !reader.IsDBNull(6) ? reader.GetString(6) : "";
                            var locationParts = location.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                            tenant.City = locationParts.Length > 0 ? locationParts[0] : "";
                            tenant.PostCode = locationParts.Length > 1 ? locationParts[1] : "";

                            tenant.Unit = !reader.IsDBNull(7) ? reader.GetString(7) : "";
                            tenant.BaseRate = reader.GetDecimal(8);
                            tenant.Threshold1Rate = reader.GetDecimal(9);
                            tenant.Threshold2Rate = reader.GetDecimal(10);

                            // Set default values for fields not in the database yet
                            tenant.StartDate = DateTime.Now.ToString("yyyy-MM-dd");
                            tenant.Period = "Monthly";
                            tenant.Threshold1 = 100;
                            tenant.Threshold2 = 200;
                            tenant.Active = true;
                            tenant.EmailAlert = true;
                            tenant.PrintBill = true;
                            tenant.EmailBill = true;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error getting tenant ID {id}");
            }

            return tenant;
        }
    }
}