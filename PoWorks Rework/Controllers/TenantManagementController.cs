using Microsoft.AspNetCore.Mvc;
using Npgsql;
using PoWorks_Rework.Models;
using PoWorks_Rework.Services;
using System.Security.Claims;

namespace PoWorks_Rework.Controllers
{
    /// <summary>
    /// Mutating side of tenant management. Creation, updates and lifecycle actions
    /// are always restricted to the active workspace and produce rich audit events.
    /// </summary>
    public class TenantManagementController : BaseController
    {
        private readonly ILogger<TenantManagementController> _logger;
        private readonly ICompanyContext _companyContext;

        public TenantManagementController(
            DatabaseService databaseService,
            ICompanyContext companyContext,
            ILogger<TenantManagementController> logger)
            : base(databaseService)
        {
            _logger = logger;
            _companyContext = companyContext;
        }

        [HttpGet]
        public IActionResult Create()
        {
            return View("~/Views/Tenant/Management.cshtml", new TenantViewModel
            {
                SelectedTenant = CreateDefaultTenant()
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult SaveTenant(Tenant tenant, IFormCollection form)
        {
            tenant.Active = ReadCheckbox(form, "Active", tenant.Active);
            tenant.EmailAlert = ReadCheckbox(form, "EmailAlert", tenant.EmailAlert);
            tenant.PrintBill = ReadCheckbox(form, "PrintBill", tenant.PrintBill);
            tenant.EmailBill = ReadCheckbox(form, "EmailBill", tenant.EmailBill);

            foreach (var error in TenantLifecycleRules.Validate(tenant))
                ModelState.AddModelError(string.Empty, error);

            if (!_databaseService.IsInitialized)
            {
                TempData["ErrorMessage"] = "Database not configured.";
                return RedirectToAction("General", "Settings");
            }

            if (!ModelState.IsValid)
            {
                return View("~/Views/Tenant/Management.cshtml", new TenantViewModel
                {
                    SelectedTenant = tenant
                });
            }

            var companyId = _companyContext.CurrentCompanyId;
            var isCreate = tenant.Id <= 0;
            object? before = null;
            object? after = null;
            int tenantId = tenant.Id;

            try
            {
                using var connection = _databaseService.CreateNewConnection();
                connection.Open();
                using var transaction = connection.BeginTransaction();

                if (isCreate)
                {
                    tenantId = CreateTenant(tenant, companyId, connection, transaction);
                }
                else
                {
                    if (!TenantExistsInWorkspace(tenant.Id, companyId, connection, transaction))
                    {
                        transaction.Rollback();
                        TempData["ErrorMessage"] = "Tenant not found in the current workspace.";
                        return RedirectToAction("Management", "Tenant");
                    }

                    before = GetTenantAuditSnapshot(tenant.Id, companyId, connection, transaction);
                    UpdateTenant(tenant, companyId, connection, transaction);
                    tenantId = tenant.Id;
                }

                after = GetTenantAuditSnapshot(tenantId, companyId, connection, transaction);
                transaction.Commit();

                AuditTrail.LogAsync(_databaseService, HttpContext, new AuditEvent
                {
                    Action = isCreate ? "CREATE" : "UPDATE",
                    EntityType = "Tenant",
                    EntityId = tenantId.ToString(),
                    CompanyId = companyId,
                    Summary = isCreate
                        ? $"Tenant '{tenant.CompanyName}' created."
                        : $"Tenant '{tenant.CompanyName}' updated.",
                    Before = before,
                    After = after
                }).GetAwaiter().GetResult();

                TempData["SuccessMessage"] = isCreate
                    ? "Tenant created successfully."
                    : "Tenant updated successfully.";

                return RedirectToAction("Management", "Tenant", new { id = tenantId });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save tenant {TenantId} in workspace {CompanyId}", tenant.Id, companyId);

                AuditTrail.LogAsync(_databaseService, HttpContext, new AuditEvent
                {
                    Action = isCreate ? "CREATE_FAILED" : "UPDATE_FAILED",
                    EntityType = "Tenant",
                    EntityId = tenant.Id > 0 ? tenant.Id.ToString() : null,
                    CompanyId = companyId,
                    Summary = isCreate ? "Tenant creation failed." : "Tenant update failed.",
                    Before = before,
                    Success = false
                }).GetAwaiter().GetResult();

                TempData["ErrorMessage"] = "Tenant could not be saved. Check the application logs for details.";
                return View("~/Views/Tenant/Management.cshtml", new TenantViewModel
                {
                    SelectedTenant = tenant
                });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult EnableTenant(int tenantId) => SetTenantActiveState(tenantId, true);

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult DisableTenant(int tenantId) => SetTenantActiveState(tenantId, false);

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult DeleteTenant(int tenantId)
        {
            var companyId = _companyContext.CurrentCompanyId;

            using var connection = _databaseService.CreateNewConnection();
            connection.Open();
            using var transaction = connection.BeginTransaction();

            if (!TenantExistsInWorkspace(tenantId, companyId, connection, transaction))
            {
                transaction.Rollback();
                TempData["ErrorMessage"] = "Tenant not found in the current workspace.";
                return RedirectToAction("Management", "Tenant");
            }

            var before = GetTenantAuditSnapshot(tenantId, companyId, connection, transaction);
            var dependencies = GetDependencies(tenantId, companyId, connection, transaction);

            if (!TenantLifecycleRules.CanPermanentlyDelete(dependencies))
            {
                transaction.Rollback();

                AuditTrail.LogAsync(_databaseService, HttpContext, new AuditEvent
                {
                    Action = "DELETE_BLOCKED",
                    EntityType = "Tenant",
                    EntityId = tenantId.ToString(),
                    CompanyId = companyId,
                    Summary = $"Tenant deletion blocked: {dependencies.UserCount} user(s), {dependencies.MeterCount} meter(s), {dependencies.BillCount} bill(s), {dependencies.PaymentCount} payment(s).",
                    Before = before,
                    Success = false
                }).GetAwaiter().GetResult();

                TempData["ErrorMessage"] =
                    $"Tenant cannot be permanently deleted while it has linked data ({dependencies.UserCount} users, {dependencies.MeterCount} meters, {dependencies.BillCount} bills, {dependencies.PaymentCount} payments). Disable it instead.";

                return RedirectToAction("Management", "Tenant", new { id = tenantId });
            }

            try
            {
                using (var details = new NpgsqlCommand(
                    @"DELETE FROM ""TenantDetails""
                      WHERE ""TenantID"" = @tenantId AND ""CompanyId"" = @companyId",
                    connection, transaction))
                {
                    details.Parameters.AddWithValue("tenantId", tenantId);
                    details.Parameters.AddWithValue("companyId", companyId);
                    details.ExecuteNonQuery();
                }

                using (var tenant = new NpgsqlCommand(
                    @"DELETE FROM ""Tenants""
                      WHERE ""TenantID"" = @tenantId AND ""CompanyId"" = @companyId",
                    connection, transaction))
                {
                    tenant.Parameters.AddWithValue("tenantId", tenantId);
                    tenant.Parameters.AddWithValue("companyId", companyId);
                    tenant.ExecuteNonQuery();
                }

                transaction.Commit();

                AuditTrail.LogAsync(_databaseService, HttpContext, new AuditEvent
                {
                    Action = "DELETE",
                    EntityType = "Tenant",
                    EntityId = tenantId.ToString(),
                    CompanyId = companyId,
                    Summary = "Empty tenant permanently deleted.",
                    Before = before
                }).GetAwaiter().GetResult();

                TempData["SuccessMessage"] = "Empty tenant permanently deleted.";
            }
            catch (Exception ex)
            {
                transaction.Rollback();
                _logger.LogError(ex, "Failed to delete tenant {TenantId}", tenantId);
                TempData["ErrorMessage"] = "Tenant deletion failed.";
            }

            return RedirectToAction("Management", "Tenant");
        }

        private IActionResult SetTenantActiveState(int tenantId, bool active)
        {
            var companyId = _companyContext.CurrentCompanyId;
            using var connection = _databaseService.CreateNewConnection();
            connection.Open();
            using var transaction = connection.BeginTransaction();

            if (!TenantExistsInWorkspace(tenantId, companyId, connection, transaction))
            {
                transaction.Rollback();
                TempData["ErrorMessage"] = "Tenant not found in the current workspace.";
                return RedirectToAction("Management", "Tenant");
            }

            var before = GetTenantAuditSnapshot(tenantId, companyId, connection, transaction);

            using var cmd = new NpgsqlCommand(@"
                UPDATE ""TenantDetails""
                SET ""Active"" = @active
                WHERE ""TenantID"" = @tenantId
                  AND ""CompanyId"" = @companyId", connection, transaction);
            cmd.Parameters.AddWithValue("active", active);
            cmd.Parameters.AddWithValue("tenantId", tenantId);
            cmd.Parameters.AddWithValue("companyId", companyId);

            if (cmd.ExecuteNonQuery() == 0)
            {
                transaction.Rollback();
                TempData["ErrorMessage"] = "Tenant details not found.";
                return RedirectToAction("Management", "Tenant", new { id = tenantId });
            }

            var after = GetTenantAuditSnapshot(tenantId, companyId, connection, transaction);
            transaction.Commit();

            AuditTrail.LogAsync(_databaseService, HttpContext, new AuditEvent
            {
                Action = active ? "ENABLE" : "DISABLE",
                EntityType = "Tenant",
                EntityId = tenantId.ToString(),
                CompanyId = companyId,
                Summary = active ? "Tenant enabled." : "Tenant disabled. Tenant user access is now blocked.",
                Before = before,
                After = after
            }).GetAwaiter().GetResult();

            TempData["SuccessMessage"] = active ? "Tenant enabled." : "Tenant disabled.";
            return RedirectToAction("Management", "Tenant", new { id = tenantId });
        }

        private int CreateTenant(
            Tenant tenant,
            int companyId,
            NpgsqlConnection connection,
            NpgsqlTransaction transaction)
        {
            using var cmd = new NpgsqlCommand(@"
                INSERT INTO ""Tenants"" (""DisplayName"", ""Misc"", ""UserId"", ""CompanyId"")
                VALUES (@displayName, @misc, @userId, @companyId)
                RETURNING ""TenantID""", connection, transaction);

            cmd.Parameters.AddWithValue("displayName", tenant.CompanyName.Trim());
            cmd.Parameters.AddWithValue("misc", (object?)tenant.Unit ?? DBNull.Value);
            cmd.Parameters.AddWithValue("userId", (object?)User.FindFirstValue(ClaimTypes.NameIdentifier) ?? DBNull.Value);
            cmd.Parameters.AddWithValue("companyId", companyId);

            var tenantId = Convert.ToInt32(cmd.ExecuteScalar());
            WriteTenantDetails(tenantId, tenant, companyId, connection, transaction, insert: true);
            return tenantId;
        }

        private void UpdateTenant(
            Tenant tenant,
            int companyId,
            NpgsqlConnection connection,
            NpgsqlTransaction transaction)
        {
            using (var cmd = new NpgsqlCommand(@"
                UPDATE ""Tenants""
                SET ""DisplayName"" = @displayName,
                    ""Misc"" = @misc
                WHERE ""TenantID"" = @tenantId
                  AND ""CompanyId"" = @companyId", connection, transaction))
            {
                cmd.Parameters.AddWithValue("displayName", tenant.CompanyName.Trim());
                cmd.Parameters.AddWithValue("misc", (object?)tenant.Unit ?? DBNull.Value);
                cmd.Parameters.AddWithValue("tenantId", tenant.Id);
                cmd.Parameters.AddWithValue("companyId", companyId);

                if (cmd.ExecuteNonQuery() != 1)
                    throw new InvalidOperationException("Tenant update was not scoped to exactly one tenant.");
            }

            WriteTenantDetails(tenant.Id, tenant, companyId, connection, transaction, insert: false);
        }

        private static void WriteTenantDetails(
            int tenantId,
            Tenant tenant,
            int companyId,
            NpgsqlConnection connection,
            NpgsqlTransaction transaction,
            bool insert)
        {
            var updateSql = @"
                UPDATE ""TenantDetails"" SET
                    ""ContactName"" = @contactName,
                    ""ContactPhone"" = @contactPhone,
                    ""ContactEmail"" = @contactEmail,
                    ""CompanyName"" = @companyName,
                    ""CompanyAddress"" = @legacyAddress,
                    ""CompanyLocation"" = @legacyLocation,
                    ""CompanyMisc"" = @unit,
                    ""Address1"" = @address1,
                    ""Address2"" = @address2,
                    ""PostCode"" = @postCode,
                    ""City"" = @city,
                    ""Unit"" = @unit,
                    ""TariffType"" = @tariffType,
                    ""BaseRate"" = @baseRate,
                    ""Threshold1"" = @threshold1,
                    ""Threshold1Rate"" = @threshold1Rate,
                    ""Threshold2"" = @threshold2,
                    ""Threshold2Rate"" = @threshold2Rate,
                    ""Tarif_1"" = CAST(@baseRate AS numeric)::money,
                    ""Tarif_2"" = CAST(@threshold1Rate AS numeric)::money,
                    ""Tarif_3"" = CAST(@threshold2Rate AS numeric)::money,
                    ""StartDate"" = @startDate,
                    ""Period"" = @period,
                    ""Deposit"" = CAST(@deposit AS numeric)::money,
                    ""AbonnementMensuel"" = @monthlyFee,
                    ""Active"" = @active,
                    ""EmailAlert"" = @emailAlert,
                    ""PrintBill"" = @printBill,
                    ""EmailBill"" = @emailBill
                WHERE ""TenantID"" = @tenantId
                  AND ""CompanyId"" = @companyId";

            var insertSql = @"
                INSERT INTO ""TenantDetails"" (
                    ""TenantID"", ""CompanyId"", ""ContactName"", ""ContactPhone"", ""ContactEmail"",
                    ""CompanyName"", ""CompanyAddress"", ""CompanyLocation"", ""CompanyMisc"",
                    ""Address1"", ""Address2"", ""PostCode"", ""City"", ""Unit"",
                    ""TariffType"", ""BaseRate"", ""Threshold1"", ""Threshold1Rate"", ""Threshold2"", ""Threshold2Rate"",
                    ""Tarif_1"", ""Tarif_2"", ""Tarif_3"", ""StartDate"", ""Period"", ""Deposit"", ""AbonnementMensuel"",
                    ""Active"", ""EmailAlert"", ""PrintBill"", ""EmailBill"")
                VALUES (
                    @tenantId, @companyId, @contactName, @contactPhone, @contactEmail,
                    @companyName, @legacyAddress, @legacyLocation, @unit,
                    @address1, @address2, @postCode, @city, @unit,
                    @tariffType, @baseRate, @threshold1, @threshold1Rate, @threshold2, @threshold2Rate,
                    CAST(@baseRate AS numeric)::money, CAST(@threshold1Rate AS numeric)::money, CAST(@threshold2Rate AS numeric)::money,
                    @startDate, @period, CAST(@deposit AS numeric)::money, @monthlyFee,
                    @active, @emailAlert, @printBill, @emailBill)";

            using var update = new NpgsqlCommand(updateSql, connection, transaction);
            AddTenantParameters(update, tenantId, tenant, companyId);
            var rows = insert ? 0 : update.ExecuteNonQuery();

            if (rows == 0)
            {
                using var create = new NpgsqlCommand(insertSql, connection, transaction);
                AddTenantParameters(create, tenantId, tenant, companyId);
                create.ExecuteNonQuery();
            }
        }

        private static void AddTenantParameters(NpgsqlCommand command, int tenantId, Tenant tenant, int companyId)
        {
            DateTime.TryParse(tenant.StartDate, out var startDate);
            if (startDate == default)
                startDate = DateTime.Today;

            command.Parameters.AddWithValue("tenantId", tenantId);
            command.Parameters.AddWithValue("companyId", companyId);
            command.Parameters.AddWithValue("contactName", (object?)tenant.Contact ?? DBNull.Value);
            command.Parameters.AddWithValue("contactPhone", (object?)tenant.Phone ?? DBNull.Value);
            command.Parameters.AddWithValue("contactEmail", (object?)tenant.Email ?? DBNull.Value);
            command.Parameters.AddWithValue("companyName", tenant.CompanyName.Trim());
            command.Parameters.AddWithValue("address1", (object?)tenant.Address1 ?? DBNull.Value);
            command.Parameters.AddWithValue("address2", (object?)tenant.Address2 ?? DBNull.Value);
            command.Parameters.AddWithValue("postCode", (object?)tenant.PostCode ?? DBNull.Value);
            command.Parameters.AddWithValue("city", (object?)tenant.City ?? DBNull.Value);
            command.Parameters.AddWithValue("unit", (object?)tenant.Unit ?? DBNull.Value);
            command.Parameters.AddWithValue("legacyAddress", string.Join(", ", new[] { tenant.Address1, tenant.Address2 }.Where(x => !string.IsNullOrWhiteSpace(x))));
            command.Parameters.AddWithValue("legacyLocation", string.Join(" ", new[] { tenant.PostCode, tenant.City }.Where(x => !string.IsNullOrWhiteSpace(x))));
            command.Parameters.AddWithValue("tariffType", tenant.TariffType);
            command.Parameters.AddWithValue("baseRate", tenant.BaseRate);
            command.Parameters.AddWithValue("threshold1", tenant.Threshold1);
            command.Parameters.AddWithValue("threshold1Rate", tenant.Threshold1Rate);
            command.Parameters.AddWithValue("threshold2", tenant.Threshold2);
            command.Parameters.AddWithValue("threshold2Rate", tenant.Threshold2Rate);
            command.Parameters.AddWithValue("startDate", startDate.Date);
            command.Parameters.AddWithValue("period", tenant.Period);
            command.Parameters.AddWithValue("deposit", tenant.Deposit);
            command.Parameters.AddWithValue("monthlyFee", tenant.MonthlyFee);
            command.Parameters.AddWithValue("active", tenant.Active);
            command.Parameters.AddWithValue("emailAlert", tenant.EmailAlert);
            command.Parameters.AddWithValue("printBill", tenant.PrintBill);
            command.Parameters.AddWithValue("emailBill", tenant.EmailBill);
        }

        private static bool TenantExistsInWorkspace(
            int tenantId,
            int companyId,
            NpgsqlConnection connection,
            NpgsqlTransaction transaction)
        {
            using var cmd = new NpgsqlCommand(@"
                SELECT COUNT(*)
                FROM ""Tenants""
                WHERE ""TenantID"" = @tenantId
                  AND ""CompanyId"" = @companyId", connection, transaction);
            cmd.Parameters.AddWithValue("tenantId", tenantId);
            cmd.Parameters.AddWithValue("companyId", companyId);
            return Convert.ToInt32(cmd.ExecuteScalar()) == 1;
        }

        private static TenantDependencySummary GetDependencies(
            int tenantId,
            int companyId,
            NpgsqlConnection connection,
            NpgsqlTransaction transaction)
        {
            const string sql = @"
                SELECT
                    (SELECT COUNT(DISTINCT tc.""UserId"")
                     FROM ""AspNetUserClaims"" tc
                     INNER JOIN ""AspNetUserClaims"" cc
                       ON cc.""UserId"" = tc.""UserId""
                      AND cc.""ClaimType"" = 'CompanyId'
                      AND cc.""ClaimValue"" = CAST(@companyId AS text)
                     WHERE tc.""ClaimType"" = 'TenantId'
                       AND tc.""ClaimValue"" = CAST(@tenantId AS text)),
                    (SELECT COUNT(*) FROM ""Meters""
                     WHERE ""TenantID"" = @tenantId AND ""CompanyId"" = @companyId),
                    (SELECT COUNT(*) FROM ""Bills""
                     WHERE ""TenantID"" = @tenantId AND ""CompanyId"" = @companyId),
                    (SELECT COUNT(*) FROM ""Payments""
                     WHERE ""TenantID"" = @tenantId AND ""CompanyId"" = @companyId)";

            using var cmd = new NpgsqlCommand(sql, connection, transaction);
            cmd.Parameters.AddWithValue("tenantId", tenantId);
            cmd.Parameters.AddWithValue("companyId", companyId);
            using var reader = cmd.ExecuteReader();
            reader.Read();

            return new TenantDependencySummary
            {
                UserCount = Convert.ToInt32(reader.GetInt64(0)),
                MeterCount = Convert.ToInt32(reader.GetInt64(1)),
                BillCount = Convert.ToInt32(reader.GetInt64(2)),
                PaymentCount = Convert.ToInt32(reader.GetInt64(3))
            };
        }

        private static object? GetTenantAuditSnapshot(
            int tenantId,
            int companyId,
            NpgsqlConnection connection,
            NpgsqlTransaction transaction)
        {
            const string sql = @"
                SELECT
                    t.""TenantID"", t.""DisplayName"",
                    td.""ContactName"", td.""ContactEmail"", td.""ContactPhone"",
                    td.""Address1"", td.""Address2"", td.""PostCode"", td.""City"", td.""Unit"",
                    td.""TariffType"", td.""BaseRate"", td.""Threshold1"", td.""Threshold1Rate"",
                    td.""Threshold2"", td.""Threshold2Rate"", td.""StartDate"", td.""Period"",
                    td.""Deposit""::numeric, td.""AbonnementMensuel"", td.""Active"", td.""EmailAlert"", td.""PrintBill"", td.""EmailBill""
                FROM ""Tenants"" t
                LEFT JOIN ""TenantDetails"" td
                  ON td.""TenantID"" = t.""TenantID""
                 AND td.""CompanyId"" = t.""CompanyId""
                WHERE t.""TenantID"" = @tenantId
                  AND t.""CompanyId"" = @companyId";

            using var cmd = new NpgsqlCommand(sql, connection, transaction);
            cmd.Parameters.AddWithValue("tenantId", tenantId);
            cmd.Parameters.AddWithValue("companyId", companyId);
            using var reader = cmd.ExecuteReader();

            if (!reader.Read())
                return null;

            return new
            {
                TenantId = reader.GetInt32(0),
                CompanyName = reader.GetString(1),
                Contact = reader.IsDBNull(2) ? null : reader.GetString(2),
                Email = reader.IsDBNull(3) ? null : reader.GetString(3),
                Phone = reader.IsDBNull(4) ? null : reader.GetString(4),
                Address1 = reader.IsDBNull(5) ? null : reader.GetString(5),
                Address2 = reader.IsDBNull(6) ? null : reader.GetString(6),
                PostCode = reader.IsDBNull(7) ? null : reader.GetString(7),
                City = reader.IsDBNull(8) ? null : reader.GetString(8),
                Unit = reader.IsDBNull(9) ? null : reader.GetString(9),
                TariffType = reader.IsDBNull(10) ? null : reader.GetString(10),
                BaseRate = reader.IsDBNull(11) ? (decimal?)null : reader.GetDecimal(11),
                Threshold1 = reader.IsDBNull(12) ? (decimal?)null : reader.GetDecimal(12),
                Threshold1Rate = reader.IsDBNull(13) ? (decimal?)null : reader.GetDecimal(13),
                Threshold2 = reader.IsDBNull(14) ? (decimal?)null : reader.GetDecimal(14),
                Threshold2Rate = reader.IsDBNull(15) ? (decimal?)null : reader.GetDecimal(15),
                StartDate = reader.IsDBNull(16) ? null : reader.GetDateTime(16).ToString("yyyy-MM-dd"),
                Period = reader.IsDBNull(17) ? null : reader.GetString(17),
                Deposit = reader.IsDBNull(18) ? (decimal?)null : reader.GetDecimal(18),
                MonthlyFee = reader.IsDBNull(19) ? (decimal?)null : reader.GetDecimal(19),
                Active = reader.IsDBNull(20) || reader.GetBoolean(20),
                EmailAlert = reader.IsDBNull(21) || reader.GetBoolean(21),
                PrintBill = reader.IsDBNull(22) || reader.GetBoolean(22),
                EmailBill = reader.IsDBNull(23) || reader.GetBoolean(23)
            };
        }

        private static bool ReadCheckbox(IFormCollection form, string name, bool defaultValue)
        {
            if (!form.TryGetValue(name, out var values))
                return defaultValue;

            return values.Any(value =>
                string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "on", StringComparison.OrdinalIgnoreCase));
        }

        private static Tenant CreateDefaultTenant() => new()
        {
            StartDate = DateTime.Now.ToString("yyyy-MM-dd"),
            Period = "Monthly",
            TariffType = "Company",
            BaseRate = 0.5m,
            Threshold1 = 100m,
            Threshold1Rate = 0.6m,
            Threshold2 = 200m,
            Threshold2Rate = 0.8m,
            Active = true,
            EmailAlert = true,
            PrintBill = true,
            EmailBill = true
        };
    }
}
