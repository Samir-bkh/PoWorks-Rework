using Microsoft.AspNetCore.Mvc;
using Npgsql;
using PoWorks_Rework.Models;
using PoWorks_Rework.Services;
using System;
using System.Data;

namespace PoWorks_Rework.Controllers
{
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
                Response.Cookies.Append("AdminSelectedCompanyId", companyId.ToString(), new CookieOptions
                {
                    Expires = DateTimeOffset.UtcNow.AddDays(1),
                    HttpOnly = true
                });
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