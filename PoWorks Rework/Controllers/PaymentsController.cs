using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Npgsql;
using PoWorks_Rework.Models;
using PoWorks_Rework.Services;
using System;
using System.Collections.Generic;

namespace PoWorks_Rework.Controllers
{
    public class PaymentsController : BaseController
    {
        private readonly ILogger<PaymentsController> _logger;
        private readonly ICompanyContext _companyContext;

        public PaymentsController(DatabaseService databaseService, ICompanyContext companyContext, ILogger<PaymentsController> logger)
            : base(databaseService)
        {
            _logger = logger;
            _companyContext = companyContext;
        }

        public IActionResult Index()
        {
            var viewModel = new PaymentDashboardViewModel();
            int currentCompanyId = _companyContext.CurrentCompanyId;

            try
            {
                string connString = _databaseService.GetConnectionString();
                using var connection = new NpgsqlConnection(connString);
                connection.Open();

               
                string queryPayments = @"
                    SELECT p.""PaymentId"", p.""BillId"", p.""PaymentDate"", p.""AmountPaid"", p.""PaymentMethod"",
                           t.""CompanyName"" as ""TenantName""
                    FROM ""Payments"" p
                    JOIN ""Bills"" b ON p.""BillId"" = b.""BillId""
                    JOIN ""Tenants"" tn ON b.""TenantID"" = tn.""TenantID""
                    LEFT JOIN ""TenantDetails"" t ON b.""TenantID"" = t.""TenantID""
                    WHERE tn.""CompanyId"" = @companyId
                    ORDER BY p.""PaymentDate"" DESC
                    LIMIT 100";

                using (var cmd = new NpgsqlCommand(queryPayments, connection))
                {
                    cmd.Parameters.AddWithValue("companyId", currentCompanyId);
                    using var reader = cmd.ExecuteReader();
                    while (reader.Read())
                    {
                        viewModel.RecentPayments.Add(new PaymentEntity
                        {
                            PaymentId = reader.GetInt32(0),
                            BillId = reader.GetInt32(1),
                            PaymentDate = reader.IsDBNull(2) ? null : reader.GetDateTime(2),
                            AmountPaid = reader.GetDecimal(3),
                            PaymentMethod = reader.IsDBNull(4) ? "Transfer" : reader.GetString(4),
                            TenantName = reader.IsDBNull(5) ? "Unknown" : reader.GetString(5)
                        });
                    }
                }


                string queryTotal = @"
                    SELECT COALESCE(SUM(p.""AmountPaid""), 0) 
                    FROM ""Payments"" p
                    JOIN ""Bills"" b ON p.""BillId"" = b.""BillId""
                    JOIN ""Tenants"" tn ON b.""TenantID"" = tn.""TenantID""
                    WHERE tn.""CompanyId"" = @companyId";
                using (var cmdTotal = new NpgsqlCommand(queryTotal, connection))
                {
                    cmdTotal.Parameters.AddWithValue("companyId", currentCompanyId);
                    viewModel.TotalCollectedThisMonth = Convert.ToDecimal(cmdTotal.ExecuteScalar());
                }

              
                string queryPending = @"
                    SELECT COUNT(*) 
                    FROM ""Bills"" b
                    JOIN ""Tenants"" tn ON b.""TenantID"" = tn.""TenantID""
                    WHERE b.""Status"" = 'Validated' AND tn.""CompanyId"" = @companyId";
                using (var cmdPending = new NpgsqlCommand(queryPending, connection))
                {
                    cmdPending.Parameters.AddWithValue("companyId", currentCompanyId);
                    viewModel.PendingBillsCount = Convert.ToInt32(cmdPending.ExecuteScalar());
                }

             
                string queryOverdue = @"
                    SELECT COUNT(*) 
                    FROM ""Bills"" b
                    JOIN ""Tenants"" tn ON b.""TenantID"" = tn.""TenantID""
                    WHERE b.""Status"" = 'Validated' 
                    AND b.""PeriodEnd"" < CURRENT_DATE - INTERVAL '14 days' 
                    AND tn.""CompanyId"" = @companyId";
                using (var cmdOverdue = new NpgsqlCommand(queryOverdue, connection))
                {
                    cmdOverdue.Parameters.AddWithValue("companyId", currentCompanyId);
                    viewModel.OverdueBillsCount = Convert.ToInt32(cmdOverdue.ExecuteScalar());
                }

       
                string queryInvoiceLookup = @"
                    SELECT b.""BillId"", b.""BillNumber"", b.""GrandTotal"", t.""CompanyName"",
                           (b.""GrandTotal"" - COALESCE((SELECT SUM(p.""AmountPaid"") FROM ""Payments"" p WHERE p.""BillId"" = b.""BillId""), 0)) as ""Remaining""
                    FROM ""Bills"" b
                    JOIN ""Tenants"" tn ON b.""TenantID"" = tn.""TenantID""
                    LEFT JOIN ""TenantDetails"" t ON b.""TenantID"" = t.""TenantID""
                    WHERE b.""Status"" = 'Validated' AND tn.""CompanyId"" = @companyId
                    ORDER BY b.""BillNumber""";

                using (var cmdLookup = new NpgsqlCommand(queryInvoiceLookup, connection))
                {
                    cmdLookup.Parameters.AddWithValue("companyId", currentCompanyId);
                    using var reader = cmdLookup.ExecuteReader();
                    while (reader.Read())
                    {
                        decimal remaining = reader.GetDecimal(4);
                        if (remaining > 0)
                        {
                            viewModel.ActiveInvoices.Add(new InvoiceLookupOption
                            {
                                BillId = reader.GetInt32(0),
                                BillNumber = reader.IsDBNull(1) ? $"BILL-{reader.GetInt32(0):D4}" : reader.GetString(1),
                                GrandTotal = reader.GetDecimal(2),
                                TenantName = reader.IsDBNull(3) ? "Unknown" : reader.GetString(3),
                                RemainingAmount = remaining
                            });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading Payments Dashboard");
                TempData["ErrorMessage"] = "Unable to load financial dashboard.";
            }

            return View(viewModel);
        }

        [HttpPost]
        public IActionResult RecordPayment(int billId, decimal amountPaid, string paymentMethod, string reference, string notes)
        {
            try
            {
                if (billId <= 0 || amountPaid <= 0)
                {
                    TempData["ErrorMessage"] = "Invalid invoice selection or amount.";
                    return RedirectToAction("Index");
                }

                string connString = _databaseService.GetConnectionString();
                using var connection = new NpgsqlConnection(connString);
                connection.Open();

                using var transaction = connection.BeginTransaction();
                try
                {
                    int tenantId = 0;
                    decimal grandTotal = 0;

            
                    string getBill = @"
                        SELECT b.""TenantID"", b.""GrandTotal"" 
                        FROM ""Bills"" b 
                        JOIN ""Tenants"" tn ON b.""TenantID"" = tn.""TenantID""
                        WHERE b.""BillId"" = @id AND tn.""CompanyId"" = @companyId";

                    using (var cmdBill = new NpgsqlCommand(getBill, connection, transaction))
                    {
                        cmdBill.Parameters.AddWithValue("id", billId);
                        cmdBill.Parameters.AddWithValue("companyId", _companyContext.CurrentCompanyId);

                        using var reader = cmdBill.ExecuteReader();
                        if (reader.Read())
                        {
                            tenantId = reader.GetInt32(0);
                            grandTotal = reader.GetDecimal(1);
                        }
                        else
                        {
                            TempData["ErrorMessage"] = "Bill not found or access denied.";
                            return RedirectToAction("Index");
                        }
                    }

                    string insertPay = @"
                        INSERT INTO ""Payments"" (""BillId"", ""TenantID"", ""PaymentDate"", ""AmountPaid"", ""PaymentMethod"", ""Reference"", ""Notes"", ""RecordedBy"")
                        VALUES (@billId, @tenantId, CURRENT_TIMESTAMP, @amount, @method, @ref, @notes, 'Admin')";

                    using (var cmdInsert = new NpgsqlCommand(insertPay, connection, transaction))
                    {
                        cmdInsert.Parameters.AddWithValue("billId", billId);
                        cmdInsert.Parameters.AddWithValue("tenantId", tenantId);
                        cmdInsert.Parameters.AddWithValue("amount", amountPaid);
                        cmdInsert.Parameters.AddWithValue("method", paymentMethod);
                        cmdInsert.Parameters.AddWithValue("ref", (object)reference ?? DBNull.Value);
                        cmdInsert.Parameters.AddWithValue("notes", (object)notes ?? DBNull.Value);
                        cmdInsert.ExecuteNonQuery();
                    }

                    string checkTotalPaid = @"SELECT COALESCE(SUM(""AmountPaid""), 0) FROM ""Payments"" WHERE ""BillId"" = @billId";
                    decimal totalPaid = 0;
                    using (var cmdCheck = new NpgsqlCommand(checkTotalPaid, connection, transaction))
                    {
                        cmdCheck.Parameters.AddWithValue("billId", billId);
                        totalPaid = Convert.ToDecimal(cmdCheck.ExecuteScalar());
                    }

                    if (totalPaid >= grandTotal)
                    {
                        using var cmdUpdate = new NpgsqlCommand(@"UPDATE ""Bills"" SET ""Status"" = 'Paid' WHERE ""BillId"" = @id", connection, transaction);
                        cmdUpdate.Parameters.AddWithValue("id", billId);
                        cmdUpdate.ExecuteNonQuery();
                    }

                    transaction.Commit();
                    TempData["SuccessMessage"] = "Payment successfully recorded.";
                }
                catch
                {
                    transaction.Rollback();
                    throw;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error recording manual payment");
                TempData["ErrorMessage"] = "Failed to record payment.";
            }

            return RedirectToAction("Index");
        }
    }
}