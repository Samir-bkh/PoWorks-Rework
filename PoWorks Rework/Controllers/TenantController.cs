using Microsoft.AspNetCore.Mvc;
using Npgsql;
using PoWorks_Rework.Models;
using PoWorks_Rework.Services;

namespace PoWorks_Rework.Controllers
{
    /// <summary>
    /// Read/search side of tenant management. Every query is scoped to the
    /// currently selected workspace.
    /// </summary>
    public class TenantController : BaseController
    {
        private readonly ILogger<TenantController> _logger;
        private readonly ICompanyContext _companyContext;

        public TenantController(
            DatabaseService databaseService,
            ICompanyContext companyContext,
            ILogger<TenantController> logger)
            : base(databaseService)
        {
            _logger = logger;
            _companyContext = companyContext;
        }

        [HttpGet]
        public IActionResult Management(int? id = null)
        {
            if (!_databaseService.IsInitialized)
            {
                TempData["ErrorMessage"] = "Database not configured. Please set up database first.";
                return RedirectToAction("General", "Settings");
            }

            var model = BuildListModel("Company Name", "", 1);

            if (id.HasValue && id.Value > 0)
            {
                if (!LoadSelection(model, id.Value))
                    TempData["ErrorMessage"] = "Tenant not found in the current workspace.";
            }
            else if (model.SearchResults.Count > 0)
            {
                LoadSelection(model, model.SearchResults[0].Id);
            }
            else
            {
                model.SelectedTenant = CreateDefaultTenant();
            }

            return View(model);
        }

        [HttpGet]
        public IActionResult Search(string searchCriteria = "Company Name", string searchTerm = "", int page = 1)
        {
            if (!_databaseService.IsInitialized)
            {
                TempData["ErrorMessage"] = "Database not configured.";
                return RedirectToAction("General", "Settings");
            }

            var model = BuildListModel(searchCriteria, searchTerm, Math.Max(1, page));
            if (model.SearchResults.Count > 0)
                LoadSelection(model, model.SearchResults[0].Id);
            else
                model.SelectedTenant = CreateDefaultTenant();

            return View("Management", model);
        }

        private TenantViewModel BuildListModel(string searchCriteria, string searchTerm, int page)
        {
            var model = new TenantViewModel
            {
                SearchCriteria = searchCriteria,
                SearchTerm = searchTerm,
                CurrentPage = page
            };

            try
            {
                var results = GetTenants(searchCriteria, searchTerm, page, 10);
                model.SearchResults = results.Items;
                model.TotalItems = results.TotalCount;
                model.TotalPages = Math.Max(1, results.TotalPages);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading tenant list for workspace {CompanyId}", _companyContext.CurrentCompanyId);
                TempData["ErrorMessage"] = "Unable to load tenants.";
            }

            return model;
        }

        private bool LoadSelection(TenantViewModel model, int tenantId)
        {
            try
            {
                var tenant = GetTenantDetailsById(tenantId);
                if (tenant == null)
                    return false;

                model.SelectedTenant = tenant;
                model.Dependencies = GetDependencies(tenantId);
                model.ConsumptionData = GetConsumptionData(tenantId);
                model.SelectedTenant.Outstanding = model.ConsumptionData.TotalBilledOutstanding;
                model.SelectedTenant.Overdue = model.ConsumptionData.Overdue;
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading tenant {TenantId} in workspace {CompanyId}", tenantId, _companyContext.CurrentCompanyId);
                TempData["ErrorMessage"] = "Unable to load tenant details.";
                return false;
            }
        }

        private sealed class SearchResult
        {
            public List<Tenant> Items { get; set; } = new();
            public int TotalCount { get; set; }
            public int TotalPages { get; set; }
        }

        private SearchResult GetTenants(string searchCriteria, string searchTerm, int page, int pageSize)
        {
            var result = new SearchResult();
            var companyId = _companyContext.CurrentCompanyId;
            var filter = string.IsNullOrWhiteSpace(searchTerm)
                ? ""
                : searchCriteria switch
                {
                    "Contact" => @" AND COALESCE(td.""ContactName"", '') ILIKE @searchTerm",
                    "Email" => @" AND COALESCE(td.""ContactEmail"", '') ILIKE @searchTerm",
                    "Phone" => @" AND COALESCE(td.""ContactPhone"", '') ILIKE @searchTerm",
                    _ => @" AND COALESCE(td.""CompanyName"", t.""DisplayName"", '') ILIKE @searchTerm"
                };

            using var connection = _databaseService.CreateNewConnection();
            connection.Open();

            var countSql = @"
                SELECT COUNT(*)
                FROM ""Tenants"" t
                LEFT JOIN ""TenantDetails"" td
                  ON td.""TenantID"" = t.""TenantID""
                 AND td.""CompanyId"" = t.""CompanyId""
                WHERE t.""CompanyId"" = @companyId" + filter;

            using (var count = new NpgsqlCommand(countSql, connection))
            {
                count.Parameters.AddWithValue("companyId", companyId);
                if (!string.IsNullOrWhiteSpace(searchTerm))
                    count.Parameters.AddWithValue("searchTerm", $"%{searchTerm.Trim()}%");

                result.TotalCount = Convert.ToInt32(count.ExecuteScalar());
                result.TotalPages = (int)Math.Ceiling(result.TotalCount / (double)pageSize);
            }

            var sql = @"
                SELECT
                    t.""TenantID"",
                    COALESCE(td.""CompanyName"", t.""DisplayName"", ''),
                    COALESCE(td.""ContactName"", ''),
                    COALESCE(td.""ContactEmail"", ''),
                    COALESCE(td.""ContactPhone"", ''),
                    COALESCE(td.""Active"", TRUE),
                    (SELECT COUNT(*) FROM ""Meters"" m
                     WHERE m.""TenantID"" = t.""TenantID"" AND m.""CompanyId"" = @companyId) AS MeterCount,
                    (SELECT COUNT(*) FROM ""Bills"" b
                     WHERE b.""TenantID"" = t.""TenantID"" AND b.""CompanyId"" = @companyId) AS BillCount,
                    (SELECT COUNT(DISTINCT tc.""UserId"")
                     FROM ""AspNetUserClaims"" tc
                     INNER JOIN ""AspNetUserClaims"" cc
                       ON cc.""UserId"" = tc.""UserId""
                      AND cc.""ClaimType"" = 'CompanyId'
                      AND cc.""ClaimValue"" = CAST(@companyId AS text)
                     WHERE tc.""ClaimType"" = 'TenantId'
                       AND tc.""ClaimValue"" = CAST(t.""TenantID"" AS text)) AS UserCount
                FROM ""Tenants"" t
                LEFT JOIN ""TenantDetails"" td
                  ON td.""TenantID"" = t.""TenantID""
                 AND td.""CompanyId"" = t.""CompanyId""
                WHERE t.""CompanyId"" = @companyId" + filter + @"
                ORDER BY COALESCE(td.""CompanyName"", t.""DisplayName"")
                LIMIT @pageSize OFFSET @offset";

            using var cmd = new NpgsqlCommand(sql, connection);
            cmd.Parameters.AddWithValue("companyId", companyId);
            cmd.Parameters.AddWithValue("pageSize", pageSize);
            cmd.Parameters.AddWithValue("offset", (page - 1) * pageSize);
            if (!string.IsNullOrWhiteSpace(searchTerm))
                cmd.Parameters.AddWithValue("searchTerm", $"%{searchTerm.Trim()}%");

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                result.Items.Add(new Tenant
                {
                    Id = reader.GetInt32(0),
                    CompanyName = reader.GetString(1),
                    Contact = reader.GetString(2),
                    Email = reader.GetString(3),
                    Phone = reader.GetString(4),
                    Active = reader.GetBoolean(5),
                    AssignedMeterCount = Convert.ToInt32(reader.GetInt64(6)),
                    BillCount = Convert.ToInt32(reader.GetInt64(7)),
                    UserCount = Convert.ToInt32(reader.GetInt64(8))
                });
            }

            return result;
        }

        private Tenant? GetTenantDetailsById(int tenantId)
        {
            var companyId = _companyContext.CurrentCompanyId;
            using var connection = _databaseService.CreateNewConnection();
            connection.Open();

            const string sql = @"
                SELECT
                    t.""TenantID"",
                    COALESCE(td.""CompanyName"", t.""DisplayName"", ''),
                    COALESCE(td.""ContactName"", ''),
                    COALESCE(td.""ContactEmail"", ''),
                    COALESCE(td.""ContactPhone"", ''),
                    COALESCE(td.""Address1"", ''),
                    COALESCE(td.""Address2"", ''),
                    COALESCE(td.""PostCode"", ''),
                    COALESCE(td.""City"", ''),
                    COALESCE(td.""Unit"", t.""Misc"", ''),
                    COALESCE(td.""TariffType"", 'Company'),
                    COALESCE(td.""BaseRate"", td.""Tarif_1""::numeric, 0.5),
                    COALESCE(td.""Threshold1"", 100),
                    COALESCE(td.""Threshold1Rate"", td.""Tarif_2""::numeric, 0.6),
                    COALESCE(td.""Threshold2"", 200),
                    COALESCE(td.""Threshold2Rate"", td.""Tarif_3""::numeric, 0.8),
                    COALESCE(td.""StartDate"", CURRENT_DATE),
                    COALESCE(td.""Period"", 'Monthly'),
                    COALESCE(td.""Deposit""::numeric, 0),
                    COALESCE(td.""AbonnementMensuel"", 0),
                    COALESCE(td.""Active"", TRUE),
                    COALESCE(td.""EmailAlert"", TRUE),
                    COALESCE(td.""PrintBill"", TRUE),
                    COALESCE(td.""EmailBill"", TRUE)
                FROM ""Tenants"" t
                LEFT JOIN ""TenantDetails"" td
                  ON td.""TenantID"" = t.""TenantID""
                 AND td.""CompanyId"" = t.""CompanyId""
                WHERE t.""TenantID"" = @tenantId
                  AND t.""CompanyId"" = @companyId";

            using var cmd = new NpgsqlCommand(sql, connection);
            cmd.Parameters.AddWithValue("tenantId", tenantId);
            cmd.Parameters.AddWithValue("companyId", companyId);

            using var reader = cmd.ExecuteReader();
            if (!reader.Read())
                return null;

            return new Tenant
            {
                Id = reader.GetInt32(0),
                CompanyName = reader.GetString(1),
                Contact = reader.GetString(2),
                Email = reader.GetString(3),
                Phone = reader.GetString(4),
                Address1 = reader.GetString(5),
                Address2 = reader.GetString(6),
                PostCode = reader.GetString(7),
                City = reader.GetString(8),
                Unit = reader.GetString(9),
                TariffType = reader.GetString(10),
                BaseRate = reader.GetDecimal(11),
                Threshold1 = reader.GetDecimal(12),
                Threshold1Rate = reader.GetDecimal(13),
                Threshold2 = reader.GetDecimal(14),
                Threshold2Rate = reader.GetDecimal(15),
                StartDate = reader.GetDateTime(16).ToString("yyyy-MM-dd"),
                Period = reader.GetString(17),
                Deposit = reader.GetDecimal(18),
                MonthlyFee = reader.GetDecimal(19),
                Active = reader.GetBoolean(20),
                EmailAlert = reader.GetBoolean(21),
                PrintBill = reader.GetBoolean(22),
                EmailBill = reader.GetBoolean(23)
            };
        }

        private TenantDependencySummary GetDependencies(int tenantId)
        {
            var companyId = _companyContext.CurrentCompanyId;
            using var connection = _databaseService.CreateNewConnection();
            connection.Open();

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

            using var cmd = new NpgsqlCommand(sql, connection);
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

        private TenantConsumptionData GetConsumptionData(int tenantId)
        {
            var companyId = _companyContext.CurrentCompanyId;
            var data = new TenantConsumptionData();

            using var connection = _databaseService.CreateNewConnection();
            connection.Open();

            const string meterSql = @"
                SELECT ""MeterId"", ""Name"", ""Unit"", COALESCE(""LastReading"", 0), COALESCE(""Active"", TRUE)
                FROM ""Meters""
                WHERE ""TenantID"" = @tenantId
                  AND ""CompanyId"" = @companyId
                ORDER BY ""Name""";

            using (var cmd = new NpgsqlCommand(meterSql, connection))
            {
                cmd.Parameters.AddWithValue("tenantId", tenantId);
                cmd.Parameters.AddWithValue("companyId", companyId);
                using var reader = cmd.ExecuteReader();

                while (reader.Read())
                {
                    data.Meters.Add(new MeterData
                    {
                        Id = reader.GetInt32(0),
                        Name = reader.GetString(1),
                        Unit = reader.IsDBNull(2) ? "" : reader.GetString(2),
                        LastReading = reader.GetInt32(3).ToString(),
                        Active = reader.GetBoolean(4)
                    });
                }
            }

            const string outstandingSql = @"
                SELECT COALESCE(SUM(
                    CASE WHEN COALESCE(""Status"", 'Draft') <> 'Paid'
                         THEN COALESCE(NULLIF(""GrandTotal"", 0), ""MontantTTC"", 0)
                         ELSE 0 END), 0)
                FROM ""Bills""
                WHERE ""TenantID"" = @tenantId
                  AND ""CompanyId"" = @companyId";

            using (var cmd = new NpgsqlCommand(outstandingSql, connection))
            {
                cmd.Parameters.AddWithValue("tenantId", tenantId);
                cmd.Parameters.AddWithValue("companyId", companyId);
                data.TotalBilledOutstanding = Convert.ToDecimal(cmd.ExecuteScalar());
            }

            return data;
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
