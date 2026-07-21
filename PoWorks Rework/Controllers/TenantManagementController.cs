using Microsoft.AspNetCore.Mvc;
using Npgsql;
using PoWorks_Rework.Models;
using PoWorks_Rework.Services;


namespace PoWorks_Rework.Controllers
{
    public class TenantManagementController : BaseController
    {
        private readonly ILogger<TenantManagementController> _logger;

        public TenantManagementController(DatabaseService databaseService, ILogger<TenantManagementController> logger)
            : base(databaseService)
        {
            _logger = logger;
        }

        private int GetCurrentUserId()
        {
            var claim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier);
            return claim != null && int.TryParse(claim.Value, out int userId) ? userId : 1;
        }
        [HttpGet]
        public IActionResult Create()
        {
            var viewModel = new TenantViewModel
            {
                SelectedTenant = new Tenant
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
                },
                SearchResults = new List<Tenant>(),
                ConsumptionData = new TenantConsumptionData()
            };

            return View("~/Views/Tenant/Management.cshtml", viewModel);
        }
        [HttpPost]
        public IActionResult SaveTenant(Tenant tenant, IFormCollection form)
        {
            _logger.LogInformation($"Tenant save attempt - ID: {tenant.Id}, Company: {tenant.CompanyName}, BaseRate: {tenant.BaseRate}");
            tenant.Active = form["Active"].ToString() == "on";
            tenant.EmailAlert = form["EmailAlert"].ToString() == "on";
            tenant.PrintBill = form["PrintBill"].ToString() == "on";
            tenant.EmailBill = form["EmailBill"].ToString() == "on";

            if (!_databaseService.IsInitialized)
            {
                _logger.LogError("Database not initialized when trying to save tenant");
                TempData["ErrorMessage"] = "Database not configured";
                return RedirectToAction("Management", "Tenant", new { errorMessage = "Database not configured" });
            }

            if (!ModelState.IsValid)
            {
                _logger.LogWarning("Model state is invalid");
                foreach (var error in ModelState.Values)
                {
                    foreach (var item in error.Errors)
                    {
                        _logger.LogWarning($"Validation error: {item.ErrorMessage}");
                    }
                }

                var viewModel = new TenantViewModel
                {
                    SelectedTenant = tenant,
                    SearchResults = new List<Tenant>()
                };
                return View("~/Views/Tenant/Management.cshtml", viewModel);
            }

            try
            {
                _logger.LogInformation("Attempting to open database connection");
                using (var connection = GetDatabaseConnection())
                {
                    _logger.LogInformation("Database connection opened successfully");
                    using var transaction = connection.BeginTransaction();
                    _logger.LogInformation("Transaction started");

                    try
                    {
                        int tenantId;

                        if (tenant.Id <= 0)
                        {
                            tenantId = CreateNewTenant(tenant, connection, transaction);
                        }
                        else
                        {
                            tenantId = UpdateExistingTenant(tenant, connection, transaction);
                        }

                        _logger.LogInformation($"All database operations completed successfully for tenant ID: {tenantId}");
                        transaction.Commit();
                        _logger.LogInformation("Transaction committed");

                        TempData["SuccessMessage"] = "Tenant saved successfully.";
                        return RedirectToAction("Management", "Tenant", new { id = tenantId });
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, $"Database operation failed with error: {ex.Message}");
                        if (ex.InnerException != null)
                        {
                            _logger.LogError($"Inner exception: {ex.InnerException.Message}");
                        }

                        transaction.Rollback();
                        _logger.LogInformation("Transaction rolled back");
                        throw new Exception($"Failed to save tenant: {ex.Message}", ex);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving tenant");
                TempData["ErrorMessage"] = $"Database error: {ex.Message}";

                var viewModel = new TenantViewModel
                {
                    SelectedTenant = tenant,
                    SearchResults = new List<Tenant>()
                };
                return View("~/Views/Tenant/Management.cshtml", viewModel);
            }
        }
        private int CreateNewTenant(Tenant tenant, NpgsqlConnection connection, NpgsqlTransaction transaction)
        {
            _logger.LogInformation("Creating new tenant");
            int currentUserId = GetCurrentUserId(); 

            
            var insertTenantCommand = new NpgsqlCommand(
                @"INSERT INTO ""Tenants"" (""DisplayName"", ""Misc"", ""UserId"", ""CompanyId"") 
          VALUES (@displayName, @misc, @userId, 1) 
          RETURNING ""TenantID""",
                connection, transaction);

            insertTenantCommand.Parameters.AddWithValue("@displayName", tenant.CompanyName);
            insertTenantCommand.Parameters.AddWithValue("@misc", tenant.Unit ?? (object)DBNull.Value);
            insertTenantCommand.Parameters.AddWithValue("@userId", currentUserId);

            _logger.LogInformation("Executing tenant insert command");
            int tenantId = (int)insertTenantCommand.ExecuteScalar();
            _logger.LogInformation($"New tenant created with ID: {tenantId}");
            var insertDetailsSql = @"
                INSERT INTO ""TenantDetails"" 
                   (""TenantID"", ""ContactName"", ""ContactPhone"", ""ContactEmail"",
                    ""CompanyName"", ""CompanyAddress"", ""CompanyLocation"", ""CompanyMisc"",
                    ""Tarif_1"", ""Tarif_2"", ""Tarif_3"")
                VALUES 
                   (@tenantId, @contactName, @contactPhone, @contactEmail,
                    @companyName, @address, @location, @misc,
                    @tarif1::money, @tarif2::money, @tarif3::money)";

            var insertDetailsCommand = new NpgsqlCommand(insertDetailsSql, connection, transaction);
            SetTenantDetailsParameters(insertDetailsCommand, tenant, tenantId);

            _logger.LogInformation("Executing tenant details insert command");
            insertDetailsCommand.ExecuteNonQuery();
            _logger.LogInformation("Tenant details inserted successfully");

            return tenantId;
        }
        private int UpdateExistingTenant(Tenant tenant, NpgsqlConnection connection, NpgsqlTransaction transaction)
        {
            _logger.LogInformation($"Updating existing tenant with ID: {tenant.Id}");
            int tenantId = tenant.Id;
            var updateTenantCommand = new NpgsqlCommand(
                @"UPDATE ""Tenants"" 
                  SET ""DisplayName"" = @displayName, ""Misc"" = @misc 
                  WHERE ""TenantID"" = @tenantId",
                connection, transaction);

            updateTenantCommand.Parameters.AddWithValue("@displayName", tenant.CompanyName);
            updateTenantCommand.Parameters.AddWithValue("@misc", tenant.Unit ?? (object)DBNull.Value);
            updateTenantCommand.Parameters.AddWithValue("@tenantId", tenantId);

            _logger.LogInformation("Executing tenant update command");
            updateTenantCommand.ExecuteNonQuery();
            _logger.LogInformation("Tenant updated successfully");
            var checkCommand = new NpgsqlCommand(
                @"SELECT COUNT(*) FROM ""TenantDetails"" WHERE ""TenantID"" = @tenantId",
                connection, transaction);
            checkCommand.Parameters.AddWithValue("@tenantId", tenantId);

            _logger.LogInformation("Checking if tenant details exist");
            int detailsCount = Convert.ToInt32(checkCommand.ExecuteScalar());
            _logger.LogInformation($"Tenant details exist: {detailsCount > 0}");
            string detailsSql = detailsCount > 0
                ? @"UPDATE ""TenantDetails"" SET
                    ""ContactName"" = @contactName, 
                    ""ContactPhone"" = @contactPhone,
                    ""ContactEmail"" = @contactEmail,
                    ""CompanyName"" = @companyName,
                    ""CompanyAddress"" = @address,
                    ""CompanyLocation"" = @location,
                    ""CompanyMisc"" = @misc,
                    ""Tarif_1"" = @tarif1::money,
                    ""Tarif_2"" = @tarif2::money,
                    ""Tarif_3"" = @tarif3::money
                  WHERE ""TenantID"" = @tenantId"
                : @"INSERT INTO ""TenantDetails"" 
                   (""TenantID"", ""ContactName"", ""ContactPhone"", ""ContactEmail"",
                    ""CompanyName"", ""CompanyAddress"", ""CompanyLocation"", ""CompanyMisc"",
                    ""Tarif_1"", ""Tarif_2"", ""Tarif_3"")
                  VALUES 
                   (@tenantId, @contactName, @contactPhone, @contactEmail,
                    @companyName, @address, @location, @misc,
                    @tarif1::money, @tarif2::money, @tarif3::money)";

            var detailsCommand = new NpgsqlCommand(detailsSql, connection, transaction);
            SetTenantDetailsParameters(detailsCommand, tenant, tenantId);

            _logger.LogInformation("Executing tenant details update/insert command");
            detailsCommand.ExecuteNonQuery();
            _logger.LogInformation("Tenant details updated successfully");

            return tenantId;
        }
        private void SetTenantDetailsParameters(NpgsqlCommand command, Tenant tenant, int tenantId)
        {
            command.Parameters.AddWithValue("@tenantId", tenantId);
            command.Parameters.AddWithValue("@contactName", tenant.Contact ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@contactPhone", tenant.Phone ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@contactEmail", tenant.Email ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@companyName", tenant.CompanyName);

            string address = tenant.Address1;
            if (!string.IsNullOrEmpty(tenant.Address2))
                address += ", " + tenant.Address2;

            command.Parameters.AddWithValue("@address", address);
            command.Parameters.AddWithValue("@location", tenant.City + " " + tenant.PostCode);
            command.Parameters.AddWithValue("@misc", tenant.Unit ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@tarif1", tenant.BaseRate.ToString());
            command.Parameters.AddWithValue("@tarif2", tenant.Threshold1Rate.ToString());
            command.Parameters.AddWithValue("@tarif3", tenant.Threshold2Rate.ToString());
        }
    }
}