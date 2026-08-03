using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Npgsql;
using PoWorks_Rework.Models;
using PoWorks_Rework.Services;
using QuestPDF.Fluent;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace PoWorks_Rework.Controllers
{
    /// <summary>
    /// Controller for bill and invoice management.
    /// Handles bill generation, search, filtering, PDF export, and payment tracking.
    /// </summary>
    public class BillsController : BaseController
    {
        private readonly ILogger<BillsController> _logger;
        private readonly BillingService _billingService;
        private readonly ICompanyContext _companyContext;

        /// <summary>
        /// Initializes the bills controller with database, billing service, company context, and logging dependencies.
        /// </summary>
        public BillsController(DatabaseService databaseService, BillingService billingService, ICompanyContext companyContext, ILogger<BillsController> logger)
            : base(databaseService)
        {
            _logger = logger;
            _billingService = billingService;
            _companyContext = companyContext;
        }

        /// <summary>
        /// Displays the bills management page with search results for available bills.
        /// Filters bills by tenant or meter and supports pagination.
        /// </summary>
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

        /// <summary>
        /// Searches bills by the given criteria and term with pagination.
        /// </summary>
        /// <param name="searchCriteria">The search field to filter by (e.g. Tenant).</param>
        /// <param name="searchTerm">The term to look for in the selected search field.</param>
        /// <param name="page">The page number to display (1-based).</param>
        /// <returns>The bills index view with the filtered results.</returns>
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

        /// <summary>
        /// Calculates and saves a bill for the given tenant and billing period.
        /// </summary>
        /// <param name="tenantId">The ID of the tenant to bill.</param>
        /// <param name="startDate">The start of the billing period.</param>
        /// <param name="endDate">The end of the billing period.</param>
        /// <returns>A redirect to the bills index with a success or error message.</returns>
        [HttpPost]
        public async Task<IActionResult> GenerateBillTest(int tenantId, DateTime startDate, DateTime endDate)
        {
            try
            {
                var newBill = await _billingService.CalculateBillAsync(tenantId, startDate, endDate);
                await _billingService.SaveBillAsync(newBill);

                TempData["SuccessMessage"] = $"SUCCESS! Bill calculated AND SAVED for {newBill.TenantName}. Grand Total: RM {newBill.AmountInclTax}";
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = $"Calculation Error: {ex.Message}";
            }

            return RedirectToAction("Index");
        }

        /// <summary>
        /// Displays the details of a specific bill, including its line items.
        /// </summary>
        /// <param name="id">The bill ID to display.</param>
        /// <returns>The bill details view, or a redirect if not found or access is denied.</returns>
        [HttpGet]
        public IActionResult Details(int id)
        {
            try
            {
                string connString = _databaseService.GetConnectionString();
                using var connection = new NpgsqlConnection(connString);
                connection.Open();


                string billQuery = @"
    SELECT b.""BillId"", t.""DisplayName"", b.""PeriodStart"", b.""PeriodEnd"", 
           b.""TotalKWh"", b.""MontantHT"", b.""MontantTVA"", b.""GrandTotal"", b.""Status"", b.""GeneratedAt""
    FROM ""Bills"" b
    JOIN ""Tenants"" t ON b.""TenantID"" = t.""TenantID""
    WHERE b.""BillId"" = @id AND t.""CompanyId"" = @companyId";

                using var cmdBill = new NpgsqlCommand(billQuery, connection);
                cmdBill.Parameters.AddWithValue("id", id);
                cmdBill.Parameters.AddWithValue("companyId", _companyContext.CurrentCompanyId);

                using var reader = cmdBill.ExecuteReader();
                if (!reader.Read())
                {
                    TempData["ErrorMessage"] = "Bill not found or access denied.";
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
                reader.Close();

                string lineQuery = @"
    SELECT ""MeterName"", ""Consumption"", ""Unit"", ""UnitPrice"", ""LineTotalHT""
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

        /// <summary>
        /// Retrieves the active meters belonging to the current company for dropdown options.
        /// </summary>
        /// <returns>A list of dropdown options containing meter IDs and names.</returns>
        private List<DropdownOption> GetMeters()
        {
            var options = new List<DropdownOption>();
            try
            {
                string connString = _databaseService.GetConnectionString();
                using var connection = new NpgsqlConnection(connString);
                connection.Open();

                var command = new NpgsqlCommand(@"SELECT ""MeterId"", ""Name"" FROM ""Meters"" WHERE ""Active"" = true AND ""CompanyId"" = @companyId ORDER BY ""Name""", connection);
                command.Parameters.AddWithValue("companyId", _companyContext.CurrentCompanyId);

                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    options.Add(new DropdownOption { Value = reader.GetInt32(0).ToString(), Text = reader.GetString(1) });
                }
            }
            catch (Exception ex) { _logger.LogError(ex, "Error getting meters"); }
            return options;
        }

        /// <summary>
        /// Retrieves the tenants belonging to the current company for dropdown options.
        /// </summary>
        /// <returns>A list of dropdown options containing tenant IDs and company names.</returns>
        private List<DropdownOption> GetTenants()
        {
            var options = new List<DropdownOption>();
            try
            {
                string connString = _databaseService.GetConnectionString();
                using var connection = new NpgsqlConnection(connString);
                connection.Open();

                var command = new NpgsqlCommand(@"
                    SELECT t.""TenantID"", td.""CompanyName"" 
                    FROM ""Tenants"" t
                    JOIN ""TenantDetails"" td ON t.""TenantID"" = td.""TenantID""
                    WHERE t.""CompanyId"" = @companyId
                    ORDER BY td.""CompanyName""", connection);

                command.Parameters.AddWithValue("companyId", _companyContext.CurrentCompanyId);

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

        /// <summary>
        /// Holds the paginated results of a bill search.
        /// </summary>
        private class SearchResult
        {
            /// <summary>
            /// The list of bills on the current page.
            /// </summary>
            public List<Bill> Items { get; set; } = new List<Bill>();

            /// <summary>
            /// The total number of bills matching the search criteria.
            /// </summary>
            public int TotalCount { get; set; }

            /// <summary>
            /// The total number of pages available.
            /// </summary>
            public int TotalPages { get; set; }
        }

        /// <summary>
        /// Searches the database for bills matching the given criteria, with pagination.
        /// </summary>
        /// <param name="searchCriteria">The search field to filter by.</param>
        /// <param name="searchTerm">The term to look for in the selected search field.</param>
        /// <param name="page">The page number to retrieve.</param>
        /// <param name="pageSize">The number of results per page.</param>
        /// <returns>A SearchResult containing the matching bills and pagination information.</returns>
        private SearchResult SearchBills(string searchCriteria, string searchTerm, int page, int pageSize)
        {
            var result = new SearchResult();
            var bills = new List<Bill>();

            try
            {
                string connString = _databaseService.GetConnectionString();
                using var connection = new NpgsqlConnection(connString);
                connection.Open();

                string query = @"
                    SELECT b.""BillId"", t.""DisplayName"", b.""PeriodStart"", b.""TotalKWh"", b.""GrandTotal""
                    FROM ""Bills"" b
                    JOIN ""Tenants"" t ON b.""TenantID"" = t.""TenantID""
                    WHERE t.""CompanyId"" = @companyId ";

                if (!string.IsNullOrEmpty(searchTerm))
                {
                    if (searchCriteria == "Tenant")
                        query += $" AND t.\"DisplayName\" ILIKE '%{searchTerm}%'";
                }

                query += " ORDER BY b.\"GeneratedAt\" DESC";

                using var command = new NpgsqlCommand(query, connection);
                command.Parameters.AddWithValue("companyId", _companyContext.CurrentCompanyId);

                using var reader = command.ExecuteReader();

                while (reader.Read())
                {
                    bills.Add(new Bill
                    {
                        Id = reader.GetInt32(0),
                        Tenant = reader.GetString(1),
                        Meter = "Multi-Meter",
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

        /// <summary>
        /// Deletes a draft invoice along with its line items.
        /// Bills that are not in Draft status cannot be deleted.
        /// </summary>
        /// <param name="id">The ID of the invoice to delete.</param>
        /// <returns>A redirect to the bills index or details view with a result message.</returns>
        [HttpPost]
        public IActionResult Delete(int id)
        {
            try
            {
                string connString = _databaseService.GetConnectionString();
                using var connection = new NpgsqlConnection(connString);
                connection.Open();

                string checkQuery = @"
                    SELECT b.""Status"" 
                    FROM ""Bills"" b
                    JOIN ""Tenants"" t ON b.""TenantID"" = t.""TenantID""
                    WHERE b.""BillId"" = @id AND t.""CompanyId"" = @companyId";

                using var checkCmd = new NpgsqlCommand(checkQuery, connection);
                checkCmd.Parameters.AddWithValue("id", id);
                checkCmd.Parameters.AddWithValue("companyId", _companyContext.CurrentCompanyId);

                var status = checkCmd.ExecuteScalar()?.ToString();

                if (status == null)
                {
                    TempData["ErrorMessage"] = "Bill not found or access denied.";
                    return RedirectToAction("Index");
                }

                if (status != "Draft")
                {
                    TempData["ErrorMessage"] = "Only draft invoices can be deleted.";
                    return RedirectToAction("Details", new { id = id });
                }

                using var transaction = connection.BeginTransaction();
                try
                {
                    using var deleteLinesCmd = new NpgsqlCommand(@"DELETE FROM ""BillLineItems"" WHERE ""BillId"" = @id", connection, transaction);
                    deleteLinesCmd.Parameters.AddWithValue("id", id);
                    deleteLinesCmd.ExecuteNonQuery();

                    using var deleteBillCmd = new NpgsqlCommand(@"DELETE FROM ""Bills"" WHERE ""BillId"" = @id", connection, transaction);
                    deleteBillCmd.Parameters.AddWithValue("id", id);
                    deleteBillCmd.ExecuteNonQuery();

                    transaction.Commit();
                    TempData["SuccessMessage"] = "The draft invoice has been successfully deleted.";
                    return RedirectToAction("Index");
                }
                catch
                {
                    transaction.Rollback();
                    throw;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error while deleting the invoice {id}");
                TempData["ErrorMessage"] = "Error while deleting the invoice.";
                return RedirectToAction("Index");
            }
        }

        /// <summary>
        /// Updates the status of a bill (Validated or Paid) and optionally records a payment.
        /// </summary>
        /// <param name="id">The ID of the bill to update.</param>
        /// <param name="newStatus">The new status to apply (Validated or Paid).</param>
        /// <returns>A redirect to the bill details with a result message.</returns>
        [HttpPost]
        public IActionResult UpdateStatus(int id, string newStatus)
        {
            try
            {
                if (newStatus != "Validated" && newStatus != "Paid")
                {
                    TempData["ErrorMessage"] = "Statut invalide.";
                    return RedirectToAction("Details", new { id = id });
                }
                string connString = _databaseService.GetConnectionString();
                using var connection = new NpgsqlConnection(connString);
                connection.Open();

        
                string checkQuery = @"
                    SELECT b.""BillId"" 
                    FROM ""Bills"" b
                    JOIN ""Tenants"" t ON b.""TenantID"" = t.""TenantID""
                    WHERE b.""BillId"" = @id AND t.""CompanyId"" = @companyId";

                using (var checkCmd = new NpgsqlCommand(checkQuery, connection))
                {
                    checkCmd.Parameters.AddWithValue("id", id);
                    checkCmd.Parameters.AddWithValue("companyId", _companyContext.CurrentCompanyId);
                    if (checkCmd.ExecuteScalar() == null)
                    {
                        TempData["ErrorMessage"] = "Bill not found or access denied.";
                        return RedirectToAction("Index");
                    }
                }

                using var transaction = connection.BeginTransaction();

                try
                {
                    string updateQuery = @"UPDATE ""Bills"" SET ""Status"" = @status WHERE ""BillId"" = @id";
                    using (var cmd = new NpgsqlCommand(updateQuery, connection, transaction))
                    {
                        cmd.Parameters.AddWithValue("status", newStatus);
                        cmd.Parameters.AddWithValue("id", id);
                        cmd.ExecuteNonQuery();
                    }

                    if (newStatus == "Paid")
                    {
                        decimal amount = 0;
                        int tenantId = 0;
                        string getBillInfo = @"SELECT ""GrandTotal"", ""TenantID"" FROM ""Bills"" WHERE ""BillId"" = @id";
                        using (var cmdInfo = new NpgsqlCommand(getBillInfo, connection, transaction))
                        {
                            cmdInfo.Parameters.AddWithValue("id", id);
                            using var reader = cmdInfo.ExecuteReader();
                            if (reader.Read())
                            {
                                amount = reader.GetDecimal(0);
                                tenantId = reader.GetInt32(1);
                            }
                        }

                        string insertPayment = @"
                            INSERT INTO ""Payments"" (""BillId"", ""TenantID"", ""PaymentDate"", ""AmountPaid"", ""PaymentMethod"") 
                            VALUES (@billId, @tenantId, CURRENT_TIMESTAMP, @amount, 'Virement')";

                        using (var cmdInsert = new NpgsqlCommand(insertPayment, connection, transaction))
                        {
                            cmdInsert.Parameters.AddWithValue("billId", id);
                            cmdInsert.Parameters.AddWithValue("tenantId", tenantId);
                            cmdInsert.Parameters.AddWithValue("amount", amount);
                            cmdInsert.ExecuteNonQuery();
                        }
                    }

                    transaction.Commit();
                    TempData["SuccessMessage"] = $"The status has been updated ({newStatus}).";
                }
                catch
                {
                    transaction.Rollback();
                    throw;
                }

                return RedirectToAction("Details", new { id = id });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error updating the status of the invoice {id}");
                TempData["ErrorMessage"] = "Error while updating the invoice.";
                return RedirectToAction("Details", new { id = id });
            }
        }

        /// <summary>
        /// Generates and downloads a PDF document for the specified bill.
        /// </summary>
        /// <param name="id">The ID of the bill to export as PDF.</param>
        /// <returns>The PDF file, or a redirect with an error message on failure.</returns>
        [HttpGet]
        public IActionResult DownloadPdf(int id)
        {
            try
            {
                using var connection = GetDatabaseConnection();

                string billQuery = @"
                    SELECT b.""BillId"", t.""DisplayName"", b.""PeriodStart"", b.""PeriodEnd"", 
                           b.""TotalKWh"", b.""MontantHT"", b.""MontantTVA"", b.""GrandTotal"", b.""Status"", b.""GeneratedAt""
                    FROM ""Bills"" b
                    JOIN ""Tenants"" t ON b.""TenantID"" = t.""TenantID""
                    WHERE b.""BillId"" = @id AND t.""CompanyId"" = @companyId";

                using var cmdBill = new NpgsqlCommand(billQuery, connection);
                cmdBill.Parameters.AddWithValue("id", id);
                cmdBill.Parameters.AddWithValue("companyId", _companyContext.CurrentCompanyId);

                var bill = new BillEntity();

                using (var reader = cmdBill.ExecuteReader())
                {
                    if (!reader.Read())
                    {
                        TempData["ErrorMessage"] = "Bill not found or access denied.";
                        return RedirectToAction("Index");
                    }

                    bill.BillId = reader.GetInt32(0);
                    bill.BillNumber = $"BILL-{bill.BillId:D4}";
                    bill.TenantName = reader.GetString(1);
                    bill.PeriodStart = reader.GetDateTime(2);
                    bill.PeriodEnd = reader.GetDateTime(3);
                    bill.TotalKWh = reader.GetDecimal(4);
                    bill.AmountExclTax = reader.GetDecimal(5);
                    bill.TaxAmount = reader.GetDecimal(6);
                    bill.AmountInclTax = reader.GetDecimal(7);
                    bill.Status = reader.GetString(8);
                    bill.GeneratedAt = reader.GetDateTime(9);
                }

                string lineQuery = @"
                    SELECT ""MeterName"", ""Consumption"", ""Unit"", ""UnitPrice"", ""LineTotalHT""
                    FROM ""BillLineItems""
                    WHERE ""BillId"" = @id";

                using var cmdLine = new NpgsqlCommand(lineQuery, connection);
                cmdLine.Parameters.AddWithValue("id", id);

                using (var lineReader = cmdLine.ExecuteReader())
                {
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
                }

                var document = new InvoiceDocument(bill);
                byte[] pdfBytes = document.GeneratePdf();
                return File(pdfBytes, "application/pdf", $"{bill.BillNumber}.pdf");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating PDF for Bill {BillId}", id);
                TempData["ErrorMessage"] = "Error generating PDF document.";
                return RedirectToAction("Details", new { id = id });
            }
        }
    }
}