using System.Text.Json;
using System.Text.Json.Nodes;
using Npgsql;

namespace PoWorks_Rework.Services
{
    public class CredentialMigrationService
    {
        private readonly DatabaseService _databaseService;
        private readonly EncryptionService _encryptionService;
        private readonly ILogger<CredentialMigrationService> _logger;
        private readonly IWebHostEnvironment _env;

        public CredentialMigrationService(
            DatabaseService databaseService,
            EncryptionService encryptionService,
            ILogger<CredentialMigrationService> logger,
            IWebHostEnvironment env)
        {
            _databaseService = databaseService;
            _encryptionService = encryptionService;
            _logger = logger;
            _env = env;
        }

        public async Task MigrateAllCredentialsAsync()
        {
          
            using var connection = _databaseService.CreateNewConnection();
            await connection.OpenAsync();

            using (var checkCmd = new NpgsqlCommand(
                @"SELECT COUNT(*) FROM ""SystemFlags"" WHERE ""FlagName"" = 'CredentialsMigratedV3'", connection))
            {
                var alreadyDone = Convert.ToInt32(await checkCmd.ExecuteScalarAsync()) > 0;
                if (!alreadyDone)
                {
                    _logger.LogInformation("Démarrage de la migration automatique des credentials en base...");
                    int migratedCount = 0;

                    using (var selectCmd = new NpgsqlCommand(
                        @"SELECT ""Id"", ""ClientSecret"", ""Password"" FROM ""WebServiceConnections""", connection))
                    using (var reader = await selectCmd.ExecuteReaderAsync())
                    {
                        var rows = new List<(int Id, string Secret, string Password)>();
                        while (await reader.ReadAsync())
                        {
                            rows.Add((reader.GetInt32(0), reader.IsDBNull(1) ? "" : reader.GetString(1), reader.IsDBNull(2) ? "" : reader.GetString(2)));
                        }
                        reader.Close();

                        foreach (var row in rows)
                        {
                            string decryptedSecret = _encryptionService.Decrypt(row.Secret);
                            string decryptedPassword = _encryptionService.Decrypt(row.Password);

                            string reencryptedSecret = _encryptionService.Encrypt(decryptedSecret);
                            string reencryptedPassword = _encryptionService.Encrypt(decryptedPassword);

                            using var updateCmd = new NpgsqlCommand(
                                @"UPDATE ""WebServiceConnections"" SET ""ClientSecret"" = @secret, ""Password"" = @password WHERE ""Id"" = @id", connection);
                            updateCmd.Parameters.AddWithValue("secret", reencryptedSecret);
                            updateCmd.Parameters.AddWithValue("password", reencryptedPassword);
                            updateCmd.Parameters.AddWithValue("id", row.Id);
                            await updateCmd.ExecuteNonQueryAsync();

                            migratedCount++;
                        }
                    }

                    using (var insertFlagCmd = new NpgsqlCommand(
                        @"INSERT INTO ""SystemFlags"" (""FlagName"", ""SetAt"") VALUES ('CredentialsMigratedV3', NOW())
                          ON CONFLICT (""FlagName"") DO NOTHING", connection))
                    {
                        await insertFlagCmd.ExecuteNonQueryAsync();
                    }

                    _logger.LogInformation("Migration de la base terminée. {Count} connexions ré-encodées.", migratedCount);
                }
            }

         
            try
            {
                var jsonPath = Path.Combine(_env.ContentRootPath, "appsettings.json");
                if (File.Exists(jsonPath))
                {
                    var jsonContent = await File.ReadAllTextAsync(jsonPath);
                    var jsonNode = JsonNode.Parse(jsonContent);

                    if (jsonNode != null)
                    {
                        bool modified = false;

                       
                        var dbSettings = jsonNode["DatabaseSettings"] as JsonObject;
                        if (dbSettings != null && dbSettings["Password"] != null)
                        {
                            string currentVal = dbSettings["Password"]?.ToString() ?? "";
                            string decrypted = _encryptionService.Decrypt(currentVal);
                            string reencrypted = _encryptionService.Encrypt(decrypted);
                            if (currentVal != reencrypted)
                            {
                                dbSettings["Password"] = reencrypted;
                                modified = true;
                            }
                        }

              
                        var sqlServers = jsonNode["SqlServerConnections"] as JsonArray;
                        if (sqlServers != null)
                        {
                            foreach (var server in sqlServers)
                            {
                                if (server is JsonObject obj && obj["Password"] != null)
                                {
                                    string currentVal = obj["Password"]?.ToString() ?? "";
                                    string decrypted = _encryptionService.Decrypt(currentVal);
                                    string reencrypted = _encryptionService.Encrypt(decrypted);
                                    if (currentVal != reencrypted)
                                    {
                                        obj["Password"] = reencrypted;
                                        modified = true;
                                    }
                                }
                            }
                        }

                        if (modified)
                        {
                            var options = new JsonSerializerOptions { WriteIndented = true };
                            await File.WriteAllTextAsync(jsonPath, jsonNode.ToJsonString(options));
                            _logger.LogInformation("appsettings.json mis à jour avec les credentials re-chiffrés.");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur lors de la migration du fichier appsettings.json");
            }
        }
    }
}