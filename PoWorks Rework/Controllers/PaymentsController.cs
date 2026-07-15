using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Npgsql;
using PoWorks_Rework.Models;
using PoWorks_Rework.Services;
using System;

namespace PoWorks_Rework.Controllers
{
    public class PaymentsController : BaseController
    {
        private readonly ILogger<PaymentsController> _logger;

        public PaymentsController(DatabaseService databaseService, ILogger<PaymentsController> logger)
            : base(databaseService)
        {
            _logger = logger;
        }

        public IActionResult Index()
        {
            var viewModel = new PaymentDashboardViewModel();

            try
            {
                string connString = _databaseService.GetConnectionString();
                using var connection = new NpgsqlConnection(connString);
                connection.Open();

                // --- REQUÊTE 1 : Récupérer l'historique des paiements (Le Registre) ---
                string queryPayments = @"
                    SELECT p.""PaymentId"", p.""BillId"", p.""PaymentDate"", p.""AmountPaid"", p.""PaymentMethod"",
                           t.""CompanyName"" as ""TenantName""
                    FROM ""Payments"" p
                    JOIN ""Bills"" b ON p.""BillId"" = b.""BillId""
                    LEFT JOIN ""TenantDetails"" t ON b.""TenantID"" = t.""TenantID""
                    ORDER BY p.""PaymentDate"" DESC
                    LIMIT 100";

                using (var cmd = new NpgsqlCommand(queryPayments, connection))
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        viewModel.RecentPayments.Add(new PaymentEntity
                        {
                            PaymentId = reader.GetInt32(0),
                            BillId = reader.GetInt32(1),
                            PaymentDate = reader.IsDBNull(2) ? null : reader.GetDateTime(2),
                            AmountPaid = reader.GetDecimal(3),
                            PaymentMethod = reader.IsDBNull(4) ? "Transfer" : reader.GetString(4),
                            TenantName = reader.IsDBNull(5) ? "Inconnu" : reader.GetString(5)
                        });
                    }
                }

                // --- REQUÊTE 2 : Calculer le KPI (Total encaissé) ---
                string queryTotal = @"SELECT COALESCE(SUM(""AmountPaid""), 0) FROM ""Payments""";
                using (var cmdTotal = new NpgsqlCommand(queryTotal, connection))
                {
                    viewModel.TotalCollectedThisMonth = Convert.ToDecimal(cmdTotal.ExecuteScalar());
                }

                // --- REQUÊTE 3 : Calculer le KPI (Factures en attente de paiement) ---
                string queryPending = @"SELECT COUNT(*) FROM ""Bills"" WHERE ""Status"" = 'Validated'";
                using (var cmdPending = new NpgsqlCommand(queryPending, connection))
                {
                    viewModel.PendingBillsCount = Convert.ToInt32(cmdPending.ExecuteScalar());
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading the Payments Dashboard");
                TempData["ErrorMessage"] = "Unable to load payment history.";
            }

            return View(viewModel);
        }
    }
}