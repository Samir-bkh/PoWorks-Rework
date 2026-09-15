using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Npgsql;

namespace PoWorks_Rework.Services
{
    /// <summary>
    /// Migrates stored credentials to the current protected format without
    /// ever wrapping ciphertext that cannot be decrypted with the configured key.
    /// </summary>
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
                @"SELECT COUNT(*) FROM ""SystemFlags"" WHERE ""FlagName"" = 'CredentialsMigratedV4'", connection))
            {
                var alreadyDone = Convert.ToInt32(await checkCmd.ExecuteScalarAsync()) > 0;
                if (!alreadyDone)
                {
                    _logger.LogInformation("Starting safe credential migration to ENC:v1 format...");
                    int migratedCount = 0;
                    bool migrationFailed = false;

                    using (var selectCmd = new NpgsqlCommand(
                        @"SELECT ""Id"", ""ClientSecret"", ""Password"" FROM ""WebServiceConnections""", connection))
                    using (var reader = await selectCmd.ExecuteReaderAsync())
                    {
                        var rows = new List<(int Id, string Secret, string Password)>();
                        while (await reader.ReadAsync())
                        {
                            rows.Add((
                                reader.GetInt32(0),
                                reader.IsDBNull(1) ? "" : reader.GetString(1),
                                reader.IsDBNull(2) ? "" : reader.GetString(2)));
                        }

                        reader.Close();

                        foreach (var row in rows)
                        {
                            if (!TryNormalize(row.Secret, $"WebServiceConnections[{row.Id}].ClientSecret", out var normalizedSecret) ||
                                !TryNormalize(row.Password, $"WebServiceConnections[{row.Id}].Password", out var normalizedPassword))
                            {
                                migrationFailed = true;
                                continue;
                            }

                            if (normalizedSecret == row.Secret && normalizedPassword == row.Password)
                                continue;

                            using var updateCmd = new NpgsqlCommand(
                                @"UPDATE ""WebServiceConnections""
                                  SET ""ClientSecret"" = @secret, ""Password"" = @password
                                  WHERE ""Id"" = @id", connection);
                            updateCmd.Parameters.AddWithValue("secret", normalizedSecret);
                            updateCmd.Parameters.AddWithValue("password", normalizedPassword);
                            updateCmd.Parameters.AddWithValue("id", row.Id);
                            await updateCmd.ExecuteNonQueryAsync();
                            migratedCount++;
                        }
                    }

                    if (!migrationFailed)
                    {
                        using var insertFlagCmd = new NpgsqlCommand(
                            @"INSERT INTO ""SystemFlags"" (""FlagName"", ""SetAt"")
                              VALUES ('CredentialsMigratedV4', NOW())
                              ON CONFLICT (""FlagName"") DO NOTHING", connection);
                        await insertFlagCmd.ExecuteNonQueryAsync();

                        _logger.LogInformation(
                            "Credential migration completed safely. {Count} connection(s) updated.",
                            migratedCount);
                    }
                    else
                    {
                        _logger.LogError(
                            "Credential migration was not marked complete because at least one stored secret could not be decrypted. Check EncryptionKey configuration.");
                    }
                }
            }

            await MigrateAppSettingsAsync();
        }

        private async Task MigrateAppSettingsAsync()
        {
            try
            {
                var jsonPath = Path.Combine(_env.ContentRootPath, "appsettings.json");
                if (!File.Exists(jsonPath))
                    return;

                var jsonContent = await File.ReadAllTextAsync(jsonPath);
                var jsonNode = JsonNode.Parse(jsonContent);
                if (jsonNode == null)
                    return;

                bool modified = false;

                var dbSettings = jsonNode["DatabaseSettings"] as JsonObject;
                if (dbSettings?["Password"] != null)
                {
                    string currentValue = dbSettings["Password"]?.ToString() ?? "";
                    if (TryNormalize(currentValue, "DatabaseSettings.Password", out var normalized) &&
                        currentValue != normalized)
                    {
                        dbSettings["Password"] = normalized;
                        modified = true;
                    }
                }

                var sqlServers = jsonNode["SqlServerConnections"] as JsonArray;
                if (sqlServers != null)
                {
                    var index = 0;
                    foreach (var server in sqlServers)
                    {
                        if (server is JsonObject obj && obj["Password"] != null)
                        {
                            string currentValue = obj["Password"]?.ToString() ?? "";
                            if (TryNormalize(currentValue, $"SqlServerConnections[{index}].Password", out var normalized) &&
                                currentValue != normalized)
                            {
                                obj["Password"] = normalized;
                                modified = true;
                            }
                        }

                        index++;
                    }
                }

                if (!modified)
                    return;

                var options = new JsonSerializerOptions { WriteIndented = true };
                await File.WriteAllTextAsync(jsonPath, jsonNode.ToJsonString(options));
                _logger.LogInformation("appsettings.json credentials migrated to ENC:v1 format.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while safely migrating appsettings.json credentials.");
            }
        }

        private bool TryNormalize(string currentValue, string location, out string normalized)
        {
            normalized = currentValue;

            try
            {
                normalized = _encryptionService.NormalizeForStorage(currentValue);
                return true;
            }
            catch (CryptographicException ex)
            {
                _logger.LogError(
                    "Credential at {Location} was left unchanged: {Reason}",
                    location,
                    ex.Message);
                return false;
            }
        }
    }
}
