using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using PoWorks_Rework.Models;
using PoWorks_Rework.Services;

namespace PoWorks_Rework.Controllers
{
    [Authorize]
    public class PcVueConfigController : Controller
    {
        private readonly DatabaseService _databaseService;
        private readonly EncryptionService _encryptionService;

        public PcVueConfigController(DatabaseService databaseService, EncryptionService encryptionService)
        {
            _databaseService = databaseService;
            _encryptionService = encryptionService;
        }

        private int GetCompanyId()
        {
            var claim = User.FindFirst("CompanyId");
            return claim != null ? int.Parse(claim.Value) : 1;
        }

        [HttpGet]
        public async Task<IActionResult> Index()
        {
            var model = new PcVueSettingsViewModel();
            int companyId = GetCompanyId();

            using var conn = _databaseService.CreateNewConnection();
            await conn.OpenAsync();

            // 1. AJOUT DE "ProjectName" DANS LA LECTURE (SELECT)
            string sql = @"SELECT ""Id"", ""BaseUrl"", ""ClientId"", ""ClientSecret"", ""Username"", ""Password"", ""IsActive"", ""ProjectName"" 
                           FROM ""WebServiceConnections"" 
                           WHERE ""CompanyId"" = @companyId LIMIT 1";

            using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("companyId", companyId);

            using var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                model.Id = reader.GetInt32(0);
                model.BaseUrl = reader.GetString(1);
                model.ClientId = reader.GetString(2);

                string encryptedSecret = reader.GetString(3);
                model.ClientSecret = _encryptionService.Decrypt(encryptedSecret);

                if (!reader.IsDBNull(4)) model.Username = reader.GetString(4);
                if (!reader.IsDBNull(5))
                {
                    string encryptedPassword = reader.GetString(5);
                    model.Password = _encryptionService.Decrypt(encryptedPassword);
                }

                model.IsActive = reader.GetBoolean(6);

                // 2. RÉCUPÉRATION DE LA VALEUR POUR L'AFFICHER
                if (!reader.IsDBNull(7)) model.ProjectName = reader.GetString(7);
            }

            return View(model);
        }

        [HttpPost]
        public async Task<IActionResult> Save(PcVueSettingsViewModel model)
        {
            if (!ModelState.IsValid)
            {
                TempData["ErrorMessage"] = "Please correct the errors in the form.";
                return View("Index", model);
            }

            int companyId = GetCompanyId();
            string encryptedSecret = _encryptionService.Encrypt(model.ClientSecret);
            string? encryptedPassword = !string.IsNullOrEmpty(model.Password) ? _encryptionService.Encrypt(model.Password) : null;

            using var conn = _databaseService.CreateNewConnection();
            await conn.OpenAsync();

            string checkSql = @"SELECT COUNT(*) FROM ""WebServiceConnections"" WHERE ""CompanyId"" = @companyId";
            using var checkCmd = new NpgsqlCommand(checkSql, conn);
            checkCmd.Parameters.AddWithValue("companyId", companyId);
            long count = (long)await checkCmd.ExecuteScalarAsync();

            if (count > 0)
            {
                // 3. AJOUT DE "ProjectName" DANS LA MISE À JOUR (UPDATE)
                string updateSql = @"UPDATE ""WebServiceConnections"" 
                                     SET ""BaseUrl"" = @url, ""ClientId"" = @clientId, ""ClientSecret"" = @secret, 
                                         ""Username"" = @username, ""Password"" = @password, ""IsActive"" = @isActive,
                                         ""ProjectName"" = @projectName
                                     WHERE ""CompanyId"" = @companyId";

                using var cmd = new NpgsqlCommand(updateSql, conn);
                cmd.Parameters.AddWithValue("url", model.BaseUrl);
                cmd.Parameters.AddWithValue("clientId", model.ClientId);
                cmd.Parameters.AddWithValue("secret", encryptedSecret);
                cmd.Parameters.AddWithValue("username", model.Username ?? (object)DBNull.Value);
                cmd.Parameters.AddWithValue("password", encryptedPassword ?? (object)DBNull.Value);
                cmd.Parameters.AddWithValue("isActive", model.IsActive);
                cmd.Parameters.AddWithValue("projectName", model.ProjectName ?? (object)DBNull.Value); // Le paramètre est bien passé ici
                cmd.Parameters.AddWithValue("companyId", companyId);

                await cmd.ExecuteNonQueryAsync();
            }
            else
            {
                // 4. AJOUT DE "ProjectName" DANS LA CRÉATION (INSERT)
                string insertSql = @"INSERT INTO ""WebServiceConnections"" 
                                     (""CompanyId"", ""BaseUrl"", ""ClientId"", ""ClientSecret"", ""Username"", ""Password"", ""IsActive"", ""ProjectName"")
                                     VALUES (@companyId, @url, @clientId, @secret, @username, @password, @isActive, @projectName)";

                using var cmd = new NpgsqlCommand(insertSql, conn);
                cmd.Parameters.AddWithValue("companyId", companyId);
                cmd.Parameters.AddWithValue("url", model.BaseUrl);
                cmd.Parameters.AddWithValue("clientId", model.ClientId);
                cmd.Parameters.AddWithValue("secret", encryptedSecret);
                cmd.Parameters.AddWithValue("username", model.Username ?? (object)DBNull.Value);
                cmd.Parameters.AddWithValue("password", encryptedPassword ?? (object)DBNull.Value);
                cmd.Parameters.AddWithValue("isActive", model.IsActive);
                cmd.Parameters.AddWithValue("projectName", model.ProjectName ?? (object)DBNull.Value); // Et ici aussi

                await cmd.ExecuteNonQueryAsync();
            }

            TempData["SuccessMessage"] = "PcVue configuration saved successfully!";
            return RedirectToAction("Index");
        }
    }
}