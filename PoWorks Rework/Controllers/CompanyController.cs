// Controllers/CompanyController.cs
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using PoWorks_Rework.Models;
using PoWorks_Rework.Services;
using System;
using System.Data;

namespace PoWorks_Rework.Controllers
{
    public class CompanyController : BaseController
    {
        private readonly ILogger<CompanyController> _logger;

        public CompanyController(DatabaseService databaseService, ILogger<CompanyController> logger)
            : base(databaseService)
        {
            _logger = logger;
        }

        public IActionResult Info()
        {
            // Check if database is initialized
            if (!_databaseService.IsInitialized)
            {
                TempData["ErrorMessage"] = "Database not configured. Please set up database first.";
                return RedirectToAction("General", "Settings");
            }

            try
            {
                // Get company info from database
                var companyInfo = GetCompanyInfo();
                return View(companyInfo);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading company information");

                // Return a default company info object if there's an error
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

                TempData["ErrorMessage"] = $"Error loading company information: {ex.Message}";
                return View(companyInfo);
            }
        }

        [HttpPost]
        public IActionResult SaveInfo(CompanyInfo companyInfo)
        {
            // Check if database is initialized
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

        private CompanyInfo GetCompanyInfo()
        {
            using (var connection = GetDatabaseConnection())
            {
                // We'll always use the first record in the table
                var sql = @"SELECT 
                    ""CompanyName"", ""RegistrationNumber"", ""Address1"", ""Address2"", 
                    ""PostCode"", ""Country"", ""City"", ""GstId"", ""GstPercentage"", 
                    ""Phone"", ""Fax"", ""Email"", ""LogoPath"" 
                FROM ""CompanyInfo"" 
                LIMIT 1";

                using (var cmd = new NpgsqlCommand(sql, connection))
                {
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

                // If no record exists, create a default one
                var defaultCompanyInfo = new CompanyInfo
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

                // Insert the default record
                SaveCompanyInfo(defaultCompanyInfo);
                return defaultCompanyInfo;
            }
        }

        private void SaveCompanyInfo(CompanyInfo companyInfo)
        {
            using (var connection = GetDatabaseConnection())
            {
                // Check if any records exist
                bool recordExists = false;
                using (var checkCmd = new NpgsqlCommand("SELECT COUNT(*) FROM \"CompanyInfo\"", connection))
                {
                    recordExists = Convert.ToInt32(checkCmd.ExecuteScalar()) > 0;
                }

                string sql;
                if (recordExists)
                {
                    // Update the first record
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
                        WHERE ""CompanyInfoId"" = (SELECT MIN(""CompanyInfoId"") FROM ""CompanyInfo"")";
                }
                else
                {
                    // Insert a new record
                    sql = @"
                        INSERT INTO ""CompanyInfo"" (
                            ""CompanyName"", ""RegistrationNumber"", ""Address1"", ""Address2"", 
                            ""PostCode"", ""Country"", ""City"", ""GstId"", ""GstPercentage"", 
                            ""Phone"", ""Fax"", ""Email"")
                        VALUES (
                            @CompanyName, @RegistrationNumber, @Address1, @Address2, 
                            @PostCode, @Country, @City, @GstId, @GstPercentage, 
                            @Phone, @Fax, @Email)";
                }

                using (var cmd = new NpgsqlCommand(sql, connection))
                {
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