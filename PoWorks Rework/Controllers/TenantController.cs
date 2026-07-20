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
    public class TenantController : BaseController
    {
        private readonly ILogger<TenantController> _logger;

        public TenantController(DatabaseService databaseService, ILogger<TenantController> logger)
            : base(databaseService)
        {
            _logger = logger;
        }

        private int GetCurrentUserId()
        {
            // On récupère l'ID de l'utilisateur connecté (par défaut 1 si on est en test)
            var claim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier);
            return claim != null && int.TryParse(claim.Value, out int userId) ? userId : 1;
        }

        public IActionResult Management(int? id = null)
        {
            if (!_databaseService.IsInitialized)
            {
                TempData["ErrorMessage"] = "Database not configured. Please set up database first.";
                return RedirectToAction("General", "Settings");
            }

            var viewModel = new TenantViewModel
            {
                SearchCriteria = "Company Name",
                SearchTerm = "",
                ConsumptionData = new TenantConsumptionData(),
                TotalPages = 1,
                CurrentPage = 1,
                TotalItems = 0
            };

            try
            {
                var results = GetTenants("Company Name", "", 1, 10);
                viewModel.SearchResults = results.Items;
                viewModel.TotalItems = results.TotalCount;
                viewModel.TotalPages = results.TotalPages;

                if (id.HasValue && id.Value > 0)
                {
                    viewModel.SelectedTenant = GetTenantDetailsById(id.Value);
                }
                else if (viewModel.SearchResults.Count > 0)
                {
                    viewModel.SelectedTenant = GetTenantDetailsById(viewModel.SearchResults[0].Id);
                }
                else
                {
                    viewModel.SelectedTenant = new Tenant
                    {
                        StartDate = DateTime.Now.ToString("yyyy-MM-dd"),
                        Period = "Monthly",
                        TariffType = "Company",
                        BaseRate = 0.5m,
                        Threshold1 = 100m,
                        Threshold1Rate = 0.6m,
                        Threshold2 = 200m,
                        Threshold2Rate = 0.8m,
                        Deposit = 0m,
                        Active = true,
                        EmailAlert = true,
                        PrintBill = true,
                        EmailBill = true
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading tenant data");
                TempData["ErrorMessage"] = $"Database error: {ex.Message}";
            }

            return View(viewModel);
        }

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
                var results = GetTenants(searchCriteria, searchTerm, page, 10);
                viewModel.SearchResults = results.Items;
                viewModel.TotalItems = results.TotalCount;
                viewModel.TotalPages = results.TotalPages;

                if (viewModel.SearchResults.Count > 0)
                {
                    viewModel.SelectedTenant = GetTenantDetailsById(viewModel.SearchResults[0].Id);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error searching tenants");
                TempData["ErrorMessage"] = $"Database error: {ex.Message}";
            }

            return View("Management", viewModel);
        }

        private class SearchResult
        {
            public List<Tenant> Items { get; set; } = new List<Tenant>();
            public int TotalCount { get; set; }
            public int TotalPages { get; set; }
        }

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

                // LA SOLUTION ULTIME : On récupère la vraie chaîne avec le mot de passe, et on crée une connexion toute propre !
                string connString = _databaseService.GetConnectionString();
                using var connection = new NpgsqlConnection(connString);
                connection.Open();

                int currentUserId = GetCurrentUserId();

                // Ajout du filtre UserId dans la clause WHERE existante
                if (string.IsNullOrEmpty(whereClause))
                {
                    whereClause = @"WHERE ""t"".""UserId"" = @currentUserId";
                }
                else
                {
                    whereClause += @" AND ""t"".""UserId"" = @currentUserId";
                }

                string countSql = @"
    SELECT COUNT(*) 
    FROM ""Tenants"" ""t""
    LEFT JOIN ""TenantDetails"" ""td"" ON ""t"".""TenantID"" = ""td"".""TenantID""
    " + whereClause;

                using (var countCommand = new NpgsqlCommand(countSql, connection))
                {
                  
                    countCommand.Parameters.AddWithValue("@currentUserId", currentUserId);

                    if (!string.IsNullOrEmpty(searchTerm))
                    {
                        countCommand.Parameters.AddWithValue("@searchTerm", $"%{searchTerm}%");
                    }

                    result.TotalCount = Convert.ToInt32(countCommand.ExecuteScalar());
                    result.TotalPages = (int)Math.Ceiling(result.TotalCount / (double)pageSize);
                }

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
                    // AJOUT DE LA LIGNE CI-DESSOUS
                    searchCommand.Parameters.AddWithValue("@currentUserId", currentUserId);

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
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting tenants");
                throw;
            }

            return result;
        }

        private Tenant GetTenantDetailsById(int id)
        {
            var tenant = new Tenant();

            try
            {
                // LA SOLUTION ULTIME ICI AUSSI
                string connString = _databaseService.GetConnectionString();
                using var connection = new NpgsqlConnection(connString);
                connection.Open();

                // Dans la méthode GetTenantDetailsById()
                int currentUserId = GetCurrentUserId();

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
    WHERE ""t"".""TenantID"" = @tenantId AND ""t"".""UserId"" = @currentUserId", connection);

                command.Parameters.AddWithValue("@tenantId", id);
                command.Parameters.AddWithValue("@currentUserId", currentUserId);

                using (var reader = command.ExecuteReader())
                {
                    if (reader.Read())
                    {
                        tenant.Id = reader.GetInt32(0);
                        tenant.CompanyName = !reader.IsDBNull(1) ? reader.GetString(1) : "";
                        tenant.Contact = !reader.IsDBNull(2) ? reader.GetString(2) : "";
                        tenant.Email = !reader.IsDBNull(3) ? reader.GetString(3) : "";
                        tenant.Phone = !reader.IsDBNull(4) ? reader.GetString(4) : "";

                        string address = !reader.IsDBNull(5) ? reader.GetString(5) : "";
                        string[] addressParts = address.Split(new[] { ", " }, StringSplitOptions.RemoveEmptyEntries);
                        tenant.Address1 = addressParts.Length > 0 ? addressParts[0] : "";
                        tenant.Address2 = addressParts.Length > 1 ? addressParts[1] : "";

                        string location = !reader.IsDBNull(6) ? reader.GetString(6) : "";
                        var locationParts = location.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                        tenant.City = locationParts.Length > 0 ? locationParts[0] : "";
                        tenant.PostCode = locationParts.Length > 1 ? locationParts[1] : "";

                        tenant.Unit = !reader.IsDBNull(7) ? reader.GetString(7) : "";
                        tenant.BaseRate = reader.GetDecimal(8);
                        tenant.Threshold1Rate = reader.GetDecimal(9);
                        tenant.Threshold2Rate = reader.GetDecimal(10);

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
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error getting tenant ID {id}");
            }

            return tenant;
        }
    }
}