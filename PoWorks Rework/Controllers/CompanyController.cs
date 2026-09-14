using Microsoft.AspNetCore.Mvc;
using Npgsql;
using PoWorks_Rework.Models;
using PoWorks_Rework.Services;
using System;
using System.Data;

namespace PoWorks_Rework.Controllers
{
    public class CompanyAdminViewModel
    {
        public List<CompanyAdminItem> Companies { get; set; } = new();
    }

    public class CompanyAdminItem
    {
        public int CompanyId { get; set; }
        public string Name { get; set; } = "";
        public bool Active { get; set; }
        public DateTime CreatedAt { get; set; }
        public int UserCount { get; set; }
        public int TenantCount { get; set; }
        public int MeterCount { get; set; }
        public int BillCount { get; set; }
        public bool CanDelete => CompanyId != 1 && UserCount == 0 && TenantCount == 0 && MeterCount == 0 && BillCount == 0;
    }

    /// <summary>
    /// Controller for managing company information and settings.
    /// Handles company profile data, configuration settings, and company switching for multi-tenancy.
    /// </summary>
    public class CompanyController : BaseController
    {
        private readonly ILogger<CompanyController> _logger;
        private readonly ICompanyContext _companyContext; 

        /// <summary>
        /// Initializes the company controller with database, company context, and logging dependencies.
        /// </summary>
        public CompanyController(DatabaseService databaseService, ICompanyContext companyContext, ILogger<CompanyController> logger)
            : base(databaseService)
        {
            _logger = logger;
            _companyContext = companyContext; 
        }

        /// <summary>
        /// Displays the company information page for the current company.
        /// </summary>
        /// <returns>The company info view with the loaded company details.</returns>
        public IActionResult Info()
        {
            if (!_databaseService.IsInitialized)
            {
                TempData["ErrorMessage"] = "Database not configured. Please set up database first.";
                return RedirectToAction("General", "Settings");
            }

            try
            {
                var companyInfo = GetCompanyInfo();
                return View(companyInfo);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading company information");
                var companyInfo = new CompanyInfo
                {
                    CompanyName = "Company Name",
                    RegistrationNumber = "",
                    Address1 = "",
                    Address2 = "",
                    PostCode = "",
                    Country = "",
                    City = "",
                    GstId = "",
                    GstPercentage = 0.00m,
                    Phone = "",
                    Fax = "",
                    Email = ""
                };

                TempData["ErrorMessage"] = $"Error loading company information: {ex.Message}";
                return View(companyInfo);
            }
        }

        /// <summary>
        /// Saves the company information submitted from the info form.
        /// </summary>
        /// <param name="companyInfo">The company information model containing the data to save.</param>
        /// <returns>A redirect to the company info page with a success or error message.</returns>
        [HttpPost]
        public IActionResult SaveInfo(CompanyInfo companyInfo)
        {
            if (!_databaseService.IsInitialized)
            {
                TempData["ErrorMessage"] = "Database not configured. Please set up database first.";
                return RedirectToAction("General", "Settings");
            }

            if (ModelState.IsValid)
            {
                try
                {
                    SaveCompanyInfo(companyInfo);
                    TempData["SuccessMessage"] = "Company information saved successfully.";
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error saving company information");
                    TempData["ErrorMessage"] = $"Error saving company information: {ex.Message}";
                }
            }
            else
            {
                TempData["ErrorMessage"] = "Please correct the errors in the form.";
            }

            return RedirectToAction("Info");
        }

        /// <summary>
        /// Retrieves the company information for the current company from the database.
        /// Returns a default placeholder object if no record exists.
        /// </summary>
        /// <returns>The company information for the current company.</returns>
        private CompanyInfo GetCompanyInfo()
        {
            int currentCompanyId = _companyContext.CurrentCompanyId;

            using (var connection = GetDatabaseConnection())
            {
                var sql = @"SELECT 
                    ""CompanyName"", ""RegistrationNumber"", ""Address1"", ""Address2"", 
                    ""PostCode"", ""Country"", ""City"", ""GstId"", ""GstPercentage"", 
                    ""Phone"", ""Fax"", ""Email"", ""LogoPath"" 
                FROM ""CompanyInfo"" 
                WHERE ""CompanyInfoId"" = @companyId LIMIT 1";

                using (var cmd = new NpgsqlCommand(sql, connection))
                {
                    cmd.Parameters.AddWithValue("companyId", currentCompanyId);

                    using (var reader = cmd.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            return new CompanyInfo
                            {
                                CompanyName = reader.IsDBNull(0) ? string.Empty : reader.GetString(0),
                                RegistrationNumber = reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
                                Address1 = reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                                Address2 = reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
                                PostCode = reader.IsDBNull(4) ? string.Empty : reader.GetString(4),
                                Country = reader.IsDBNull(5) ? string.Empty : reader.GetString(5),
                                City = reader.IsDBNull(6) ? string.Empty : reader.GetString(6),
                                GstId = reader.IsDBNull(7) ? string.Empty : reader.GetString(7),
                                GstPercentage = reader.IsDBNull(8) ? 0 : reader.GetDecimal(8),
                                Phone = reader.IsDBNull(9) ? string.Empty : reader.GetString(9),
                                Fax = reader.IsDBNull(10) ? string.Empty : reader.GetString(10),
                                Email = reader.IsDBNull(11) ? string.Empty : reader.GetString(11),
                                LogoPath = reader.IsDBNull(12) ? string.Empty : reader.GetString(12)
                            };
                        }
                    }
                }

                var defaultCompanyInfo = new CompanyInfo
                {
                    CompanyName = $"New Company {currentCompanyId}",
                    GstPercentage = 0.00m
                };
                return defaultCompanyInfo;
            }
        }

        /// <summary>
        /// Inserts or updates the company information record for the current company.
        /// </summary>
        /// <param name="companyInfo">The company information model containing the data to persist.</param>
        private void SaveCompanyInfo(CompanyInfo companyInfo)
        {
            int currentCompanyId = _companyContext.CurrentCompanyId;

            using (var connection = GetDatabaseConnection())
            {
                bool recordExists = false;

                using (var checkCmd = new NpgsqlCommand("SELECT COUNT(*) FROM \"CompanyInfo\" WHERE \"CompanyInfoId\" = @companyId", connection))
                {
                    checkCmd.Parameters.AddWithValue("companyId", currentCompanyId);
                    recordExists = Convert.ToInt32(checkCmd.ExecuteScalar()) > 0;
                }

                string sql;
                if (recordExists)
                {
                    sql = @"
                        UPDATE ""CompanyInfo"" 
                        SET 
                            ""CompanyName"" = @CompanyName, 
                            ""RegistrationNumber"" = @RegistrationNumber, 
                            ""Address1"" = @Address1, 
                            ""Address2"" = @Address2, 
                            ""PostCode"" = @PostCode, 
                            ""Country"" = @Country, 
                            ""City"" = @City, 
                            ""GstId"" = @GstId, 
                            ""GstPercentage"" = @GstPercentage, 
                            ""Phone"" = @Phone, 
                            ""Fax"" = @Fax, 
                            ""Email"" = @Email
                        WHERE ""CompanyInfoId"" = @companyId";
                }
                else
                {
                    sql = @"
                        INSERT INTO ""CompanyInfo"" (
                            ""CompanyInfoId"", ""CompanyName"", ""RegistrationNumber"", ""Address1"", ""Address2"", 
                            ""PostCode"", ""Country"", ""City"", ""GstId"", ""GstPercentage"", 
                            ""Phone"", ""Fax"", ""Email"")
                        VALUES (
                            @companyId, @CompanyName, @RegistrationNumber, @Address1, @Address2, 
                            @PostCode, @Country, @City, @GstId, @GstPercentage, 
                            @Phone, @Fax, @Email)";
                }

                using (var cmd = new NpgsqlCommand(sql, connection))
                {
                    cmd.Parameters.AddWithValue("@companyId", currentCompanyId);
                    cmd.Parameters.AddWithValue("@CompanyName", companyInfo.CompanyName ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@RegistrationNumber", companyInfo.RegistrationNumber ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@Address1", companyInfo.Address1 ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@Address2", companyInfo.Address2 ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@PostCode", companyInfo.PostCode ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@Country", companyInfo.Country ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@City", companyInfo.City ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@GstId", companyInfo.GstId ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@GstPercentage", companyInfo.GstPercentage);
                    cmd.Parameters.AddWithValue("@Phone", companyInfo.Phone ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@Fax", companyInfo.Fax ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@Email", companyInfo.Email ?? (object)DBNull.Value);

                    cmd.ExecuteNonQuery();
                }
            }
        }

        [Microsoft.AspNetCore.Authorization.Authorize(Policy = "AdminOnly")]
        public IActionResult Management()
        {
            var model = new CompanyAdminViewModel();

            using var connection = GetDatabaseConnection();
            using var cmd = new NpgsqlCommand(@"
                SELECT c.""CompanyId"",
                       c.""Name"",
                       c.""Active"",
                       c.""CreatedAt"",
                       (SELECT COUNT(DISTINCT uc.""UserId"")
                          FROM ""AspNetUserClaims"" uc
                         WHERE uc.""ClaimType"" = 'CompanyId'
                           AND uc.""ClaimValue"" = c.""CompanyId""::text) AS ""UserCount"",
                       (SELECT COUNT(*) FROM ""Tenants"" t WHERE t.""CompanyId"" = c.""CompanyId"") AS ""TenantCount"",
                       (SELECT COUNT(*) FROM ""Meters"" m WHERE m.""CompanyId"" = c.""CompanyId"") AS ""MeterCount"",
                       (SELECT COUNT(*) FROM ""Bills"" b
                          JOIN ""Tenants"" t ON t.""TenantID"" = b.""TenantID""
                         WHERE t.""CompanyId"" = c.""CompanyId"") AS ""BillCount""
                  FROM ""Companies"" c
                 ORDER BY c.""CompanyId""", connection);

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                model.Companies.Add(new CompanyAdminItem
                {
                    CompanyId = reader.GetInt32(0),
                    Name = reader.GetString(1),
                    Active = reader.GetBoolean(2),
                    CreatedAt = reader.GetDateTime(3),
                    UserCount = Convert.ToInt32(reader.GetInt64(4)),
                    TenantCount = Convert.ToInt32(reader.GetInt64(5)),
                    MeterCount = Convert.ToInt32(reader.GetInt64(6)),
                    BillCount = Convert.ToInt32(reader.GetInt64(7))
                });
            }

            return View(model);
        }

        [HttpPost]
        [Microsoft.AspNetCore.Authorization.Authorize(Policy = "AdminOnly")]
        public IActionResult CreateCompany(string name)
        {
            name = (name ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                TempData["ErrorMessage"] = "Company name is required.";
                return RedirectToAction(nameof(Management));
            }

            using var connection = GetDatabaseConnection();

            using (var existsCmd = new NpgsqlCommand(@"SELECT COUNT(*) FROM ""Companies"" WHERE LOWER(""Name"") = LOWER(@name)", connection))
            {
                existsCmd.Parameters.AddWithValue("name", name);
                if (Convert.ToInt32(existsCmd.ExecuteScalar()) > 0)
                {
                    TempData["ErrorMessage"] = "A company with this name already exists.";
                    return RedirectToAction(nameof(Management));
                }
            }

            using var cmd = new NpgsqlCommand(@"INSERT INTO ""Companies"" (""Name"", ""Active"") VALUES (@name, TRUE) RETURNING ""CompanyId""", connection);
            cmd.Parameters.AddWithValue("name", name);
            var newCompanyId = Convert.ToInt32(cmd.ExecuteScalar());

            TempData["SuccessMessage"] = $"Company '{name}' created (ID {newCompanyId}).";
            return RedirectToAction(nameof(Management));
        }

        [HttpPost]
        [Microsoft.AspNetCore.Authorization.Authorize(Policy = "AdminOnly")]
        public IActionResult SetCompanyActive(int companyId, bool active)
        {
            if (companyId == 1 && !active)
            {
                TempData["ErrorMessage"] = "The default company cannot be disabled.";
                return RedirectToAction(nameof(Management));
            }

            using var connection = GetDatabaseConnection();
            using var cmd = new NpgsqlCommand(@"UPDATE ""Companies"" SET ""Active"" = @active WHERE ""CompanyId"" = @companyId", connection);
            cmd.Parameters.AddWithValue("active", active);
            cmd.Parameters.AddWithValue("companyId", companyId);
            var rows = cmd.ExecuteNonQuery();

            if (rows == 0)
                TempData["ErrorMessage"] = "Company not found.";
            else
                TempData["SuccessMessage"] = active
                    ? "Company enabled. Users and automatic imports can use it again."
                    : "Company disabled. Login access and automatic imports are now blocked.";

            return RedirectToAction(nameof(Management));
        }

        [HttpPost]
        [Microsoft.AspNetCore.Authorization.Authorize(Policy = "AdminOnly")]
        public IActionResult DeleteCompany(int companyId)
        {
            if (companyId == 1)
            {
                TempData["ErrorMessage"] = "The default company cannot be deleted.";
                return RedirectToAction(nameof(Management));
            }

            using var connection = GetDatabaseConnection();
            using var tx = connection.BeginTransaction();

            try
            {
                using var dependencyCmd = new NpgsqlCommand(@"
                    SELECT
                        (SELECT COUNT(*) FROM ""AspNetUserClaims"" WHERE ""ClaimType"" = 'CompanyId' AND ""ClaimValue"" = @companyIdText),
                        (SELECT COUNT(*) FROM ""Tenants"" WHERE ""CompanyId"" = @companyId),
                        (SELECT COUNT(*) FROM ""Meters"" WHERE ""CompanyId"" = @companyId)", connection, tx);
                dependencyCmd.Parameters.AddWithValue("companyId", companyId);
                dependencyCmd.Parameters.AddWithValue("companyIdText", companyId.ToString());

                using var reader = dependencyCmd.ExecuteReader();
                reader.Read();
                var userCount = Convert.ToInt32(reader.GetInt64(0));
                var tenantCount = Convert.ToInt32(reader.GetInt64(1));
                var meterCount = Convert.ToInt32(reader.GetInt64(2));
                reader.Close();

                if (userCount > 0 || tenantCount > 0 || meterCount > 0)
                {
                    tx.Rollback();
                    TempData["ErrorMessage"] =
                        $"Company cannot be permanently deleted while it still contains data ({userCount} users, {tenantCount} tenants, {meterCount} meters). Disable it instead.";
                    return RedirectToAction(nameof(Management));
                }

                foreach (var table in new[] { "WebServiceConnections", "SqlServerConnections" })
                {
                    using var cleanup = new NpgsqlCommand($@"DELETE FROM ""{table}"" WHERE ""CompanyId"" = @companyId", connection, tx);
                    cleanup.Parameters.AddWithValue("companyId", companyId);
                    cleanup.ExecuteNonQuery();
                }

                using (var infoCleanup = new NpgsqlCommand(@"DELETE FROM ""CompanyInfo"" WHERE ""CompanyInfoId"" = @companyId", connection, tx))
                {
                    infoCleanup.Parameters.AddWithValue("companyId", companyId);
                    infoCleanup.ExecuteNonQuery();
                }

                using (var delete = new NpgsqlCommand(@"DELETE FROM ""Companies"" WHERE ""CompanyId"" = @companyId", connection, tx))
                {
                    delete.Parameters.AddWithValue("companyId", companyId);
                    delete.ExecuteNonQuery();
                }

                tx.Commit();
                TempData["SuccessMessage"] = "Empty company permanently deleted.";
            }
            catch (Exception ex)
            {
                tx.Rollback();
                _logger.LogError(ex, "Error deleting company {CompanyId}", companyId);
                TempData["ErrorMessage"] = "Company deletion failed.";
            }

            return RedirectToAction(nameof(Management));
        }

        /// <summary>
        /// Displays the company settings page with the current configuration values.
        /// </summary>
        /// <returns>The company settings view.</returns>
        public IActionResult Settings()
        {
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


        /// <summary>
        /// Switches the active company context. Admins can select a company via a cookie that persists for one day.
        /// </summary>
        /// <param name="companyId">The ID of the company to switch to.</param>
        /// <param name="returnUrl">The local URL to redirect to after switching.</param>
        /// <returns>A redirect to the return URL or the home page.</returns>
        [HttpPost]
        public IActionResult SwitchCompany(int companyId, string returnUrl)
        {
            if (User.Identity?.Name?.ToLower() == "admin")
            {
                using var connection = GetDatabaseConnection();
                using var cmd = new NpgsqlCommand(@"SELECT ""Active"" FROM ""Companies"" WHERE ""CompanyId"" = @companyId", connection);
                cmd.Parameters.AddWithValue("companyId", companyId);
                var result = cmd.ExecuteScalar();

                if (result is bool active && active)
                {
                    Response.Cookies.Append("AdminSelectedCompanyId", companyId.ToString(), new CookieOptions
                    {
                        Expires = DateTimeOffset.UtcNow.AddDays(1),
                        HttpOnly = true,
                        SameSite = SameSiteMode.Lax
                    });
                }
                else
                {
                    TempData["ErrorMessage"] = "Only active companies can be selected.";
                }
            }

            return LocalRedirect(string.IsNullOrEmpty(returnUrl) ? "/" : returnUrl);
        }

        /// <summary>
        /// Saves the company settings submitted from the settings form.
        /// </summary>
        /// <param name="companySettings">The company settings model containing the data to save.</param>
        /// <returns>A redirect to the settings page, or the settings view if validation fails.</returns>
        [HttpPost]
        public IActionResult SaveSettings(CompanySettings companySettings)
        {
            if (ModelState.IsValid)
            {
                TempData["SuccessMessage"] = "Company settings saved successfully.";
                return RedirectToAction("Settings");
            }
            return View("Settings", companySettings);
        }
    }
}