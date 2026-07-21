using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Npgsql;
using PoWorks_Rework.Models;
using PoWorks_Rework.Repositories;
using PoWorks_Rework.Services;

namespace PoWorks_Rework.Controllers
{
    public class MeterController : BaseController
    {
        private readonly MeterRepository _meterRepository;
        private readonly ICompanyContext _companyContext; 

        public MeterController(DatabaseService databaseService, MeterRepository meterRepository, ICompanyContext companyContext)
            : base(databaseService)
        {
            _meterRepository = meterRepository;
            _companyContext = companyContext; 
        }
        public async Task<IActionResult> Management(int? id = null, int page = 1, int pageSize = 10)
        {
            if (!_databaseService.IsInitialized)
            {
                TempData["ErrorMessage"] = "Database not configured. Please set up database first.";
                return RedirectToAction("General", "Settings");
            }

            var searchCriteria = new MeterSearchCriteria();
            var viewModel = new MeterManagementViewModel
            {
                SearchCriteria = searchCriteria,
                CurrentPage = page,
                TenantOptions = GetTenantOptions() 
            };

            try
            {
                viewModel.SearchResults = await _meterRepository.GetMetersAsync(searchCriteria, page, pageSize);
                viewModel.TotalItems = await _meterRepository.GetTotalMetersCountAsync(searchCriteria);
                viewModel.TotalPages = (viewModel.TotalItems + pageSize - 1) / pageSize;
                if (id.HasValue)
                {
                    Console.WriteLine($"Selected meter ID: {id.Value}");
                    viewModel.SelectedMeter = await _meterRepository.GetMeterByIdAsync(id.Value);

                    if (viewModel.SelectedMeter != null)
                    {
                        Console.WriteLine($"Selected meter: {viewModel.SelectedMeter.Name}, ID: {viewModel.SelectedMeter.Id}");
                        viewModel.SubMeters = await _meterRepository.GetSubMetersAsync(id.Value);
                        Console.WriteLine($"Retrieved {viewModel.SubMeters.Count} sub meters for meter ID {id.Value}");
                    }
                }
                else if (viewModel.SearchResults.Count > 0)
                {
                    var mainMeter = viewModel.SearchResults.FirstOrDefault(m => m.Type.ToLower() == "main");

                    if (mainMeter != null)
                    {
                        viewModel.SelectedMeter = mainMeter;
                        Console.WriteLine($"Selected main meter: {viewModel.SelectedMeter.Name}, ID: {viewModel.SelectedMeter.Id}");
                    }
                    else
                    {
                        viewModel.SelectedMeter = viewModel.SearchResults[0];
                        Console.WriteLine($"No main meter found, selected first meter: {viewModel.SelectedMeter.Name}, ID: {viewModel.SelectedMeter.Id}");
                    }
                    viewModel.SubMeters = await _meterRepository.GetSubMetersAsync(viewModel.SelectedMeter.Id);
                    Console.WriteLine($"Retrieved {viewModel.SubMeters.Count} sub meters for meter ID {viewModel.SelectedMeter.Id}");
                }

                return View(viewModel);
            }
            catch (System.Exception ex)
            {
                Console.WriteLine($"Error in Management method: {ex.Message}");
                TempData["ErrorMessage"] = $"Database error: {ex.Message}";
                return View(viewModel);
            }
        }
        [HttpPost]
        public async Task<IActionResult> Search(MeterSearchCriteria searchCriteria, int page = 1, int pageSize = 10)
        {
            if (!_databaseService.IsInitialized)
            {
                TempData["ErrorMessage"] = "Database not configured. Please set up database first.";
                return RedirectToAction("General", "Settings");
            }

            try
            {
                var viewModel = new MeterManagementViewModel
                {
                    SearchCriteria = searchCriteria,
                    CurrentPage = page,
                    TenantOptions = GetTenantOptions() 
                };
                viewModel.SearchResults = await _meterRepository.GetMetersAsync(searchCriteria, page, pageSize);
                viewModel.TotalItems = await _meterRepository.GetTotalMetersCountAsync(searchCriteria);
                viewModel.TotalPages = (viewModel.TotalItems + pageSize - 1) / pageSize;
                if (viewModel.SearchResults.Count > 0)
                {
                    viewModel.SelectedMeter = viewModel.SearchResults[0];
                    viewModel.SubMeters = await _meterRepository.GetSubMetersAsync(viewModel.SelectedMeter.Id);
                }

                return View("Management", viewModel);
            }
            catch (System.Exception ex)
            {
                TempData["ErrorMessage"] = $"Database error: {ex.Message}";
                return RedirectToAction("Management");
            }
        }

        [HttpPost]
        public async Task<IActionResult> Create(Meter meter, [FromServices] ILogger<MeterController> logger)
        {
            if (!_databaseService.IsInitialized)
            {
                logger.LogError("Database not initialized when attempting to create meter");
                TempData["ErrorMessage"] = "Database not configured. Please set up database first.";
                return RedirectToAction("General", "Settings");
            }

            try
            {
                meter.Id = 0;
                logger.LogInformation("Creating meter: Name={Name}, Type={Type}, Unit={Unit}, LastReading={LastReading}, Active={Active}, ParentId={ParentId}, TenantId={TenantId}",
                    meter.Name, meter.Type, meter.Unit, meter.LastReading, meter.Active, meter.ParentMeterId, meter.TenantId);
                if (string.IsNullOrWhiteSpace(meter.Name))
                {
                    meter.Name = "Unnamed Meter " + DateTime.Now.ToString("yyyyMMddHHmmss");
                    logger.LogWarning("Empty meter name was auto-filled with a timestamp");
                }
                if (string.IsNullOrWhiteSpace(meter.Unit))
                {
                    meter.Unit = "";  
                    logger.LogWarning("Empty Unit field was set to empty string to satisfy NOT NULL constraint");
                }
                using (var connection = new NpgsqlConnection(_databaseService.GetConnectionString()))
                {
                    await connection.OpenAsync();
                    logger.LogInformation("New database connection opened successfully");
                    using var transaction = await connection.BeginTransactionAsync();
                    try
                    {
                        string sql = @"
    UPDATE ""Meters""
    SET ""Name"" = @Name,
        ""Label"" = @Label,
        ""Unit"" = @Unit,
        ""ParentId"" = @ParentId,
        ""LastReading"" = @LastReading,
        ""Type"" = @Type,
        ""Active"" = @Active,
        ""TenantID"" = @TenantId
    WHERE ""MeterId"" = @MeterId AND ""CompanyId"" = @CompanyId"; 

                        using var cmd = new NpgsqlCommand(sql, connection, transaction);
                        cmd.Parameters.AddWithValue("@CompanyId", _companyContext.CurrentCompanyId);

                        cmd.Parameters.AddWithValue("@Name", meter.Name);
                        cmd.Parameters.AddWithValue("@Unit", meter.Unit ?? "");
                        int? parentId = null;
                        if (!string.IsNullOrEmpty(meter.ParentMeterId) && int.TryParse(meter.ParentMeterId, out int pid))
                        {
                            parentId = pid;
                        }
                        cmd.Parameters.AddWithValue("@ParentId", parentId.HasValue ? parentId.Value : DBNull.Value);
                        int lastReading = 0;
                        if (!string.IsNullOrEmpty(meter.LastReading) && int.TryParse(meter.LastReading, out int reading))
                        {
                            lastReading = reading;
                        }
                        cmd.Parameters.AddWithValue("@LastReading", lastReading);
                        string type = "main";
                        if (!string.IsNullOrWhiteSpace(meter.Type) &&
                            (meter.Type.ToLower() == "main" || meter.Type.ToLower() == "sub"))
                        {
                            type = meter.Type.ToLower();
                        }
                        cmd.Parameters.AddWithValue("@Type", type);

                        cmd.Parameters.AddWithValue("@Active", meter.Active);
                        int? tenantId = null;
                        if (!string.IsNullOrEmpty(meter.TenantId) && int.TryParse(meter.TenantId, out int tid))
                        {
                            tenantId = tid;
                        }
                        cmd.Parameters.AddWithValue("@TenantId", tenantId.HasValue ? tenantId.Value : DBNull.Value);

                        var result = await cmd.ExecuteScalarAsync();
                        if (result == null)
                        {
                            throw new Exception("Database returned null after insert operation");
                        }

                        int meterId = Convert.ToInt32(result);
                        logger.LogInformation("New meter created with ID: {MeterId}", meterId);

                        await transaction.CommitAsync();
                        logger.LogInformation("Transaction committed successfully");

                        TempData["SuccessMessage"] = "Meter created successfully.";
                        return RedirectToAction("Management", new { id = meterId });
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "Error executing SQL command. Rolling back transaction.");
                        await transaction.RollbackAsync();
                        throw;
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Unexpected error creating meter");
                TempData["ErrorMessage"] = $"Error creating meter: {ex.Message}";
            }
            var viewModel = new MeterManagementViewModel
            {
                SelectedMeter = meter,
                SearchCriteria = new MeterSearchCriteria()
            };

            try
            {
                using (var connection = new NpgsqlConnection(_databaseService.GetConnectionString()))
                {
                    await connection.OpenAsync();
                    string sql = @"
                SELECT m.""MeterId"", m.""Name"", m.""Unit"", m.""ParentId"", p.""Name"" AS ""ParentName"",
                       m.""LastReading"", m.""Type"", m.""Active"", m.""TenantID"", t.""DisplayName"" AS ""TenantName""
                FROM ""Meters"" m
                LEFT JOIN ""Meters"" p ON m.""ParentId"" = p.""MeterId""
                LEFT JOIN ""Tenants"" t ON m.""TenantID"" = t.""TenantID""
                ORDER BY m.""Name""
                LIMIT 10";

                    using var cmd = new NpgsqlCommand(sql, connection);
                    using var reader = await cmd.ExecuteReaderAsync();

                    while (await reader.ReadAsync())
                    {
                        viewModel.SearchResults.Add(new Meter
                        {
                            Id = reader.GetInt32(reader.GetOrdinal("MeterId")),
                            Name = reader.GetString(reader.GetOrdinal("Name")),
                            Unit = reader.GetString(reader.GetOrdinal("Unit")), 
                            ParentMeterId = reader.IsDBNull(reader.GetOrdinal("ParentId")) ? null : reader.GetInt32(reader.GetOrdinal("ParentId")).ToString(),
                            ParentMeterName = reader.IsDBNull(reader.GetOrdinal("ParentName")) ? null : reader.GetString(reader.GetOrdinal("ParentName")),
                            LastReading = reader.GetInt32(reader.GetOrdinal("LastReading")).ToString(),
                            Type = reader.GetString(reader.GetOrdinal("Type")).First().ToString().ToUpper() + reader.GetString(reader.GetOrdinal("Type")).Substring(1),
                            TenantId = reader.IsDBNull(reader.GetOrdinal("TenantID")) ? null : reader.GetInt32(reader.GetOrdinal("TenantID")).ToString(),
                            TenantName = reader.IsDBNull(reader.GetOrdinal("TenantName")) ? null : reader.GetString(reader.GetOrdinal("TenantName")),
                            Active = reader.GetBoolean(reader.GetOrdinal("Active"))
                        });
                    }
                }
                using (var connection = new NpgsqlConnection(_databaseService.GetConnectionString()))
                {
                    await connection.OpenAsync();
                    using var cmd = new NpgsqlCommand("SELECT COUNT(*) FROM \"Meters\"", connection);
                    viewModel.TotalItems = Convert.ToInt32(await cmd.ExecuteScalarAsync());
                    viewModel.TotalPages = (viewModel.TotalItems + 9) / 10;
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error loading search results");
                TempData["ErrorMessage"] = (TempData["ErrorMessage"] as string ?? "") +
                                          $" Error loading search results: {ex.Message}";
            }

            return View("Management", viewModel);
        }
        [HttpPost]
        public async Task<IActionResult> Debug()
        {
            Console.WriteLine("=== DEBUG: ANY POST REQUEST RECEIVED ===");
            foreach (var key in Request.Form.Keys)
            {
                Console.WriteLine($"Form Field: {key} = {Request.Form[key]}");
            }
            Console.WriteLine("=======================================");

            return RedirectToAction("Management");
        }

        [HttpPost]
        public async Task<IActionResult> Update(Meter meter)
        {
            Console.WriteLine("=== UPDATE METHOD CALLED ===");
            Console.WriteLine($"Method reached at: {DateTime.Now}");
            Console.WriteLine("=== FORM DATA RECEIVED ===");
            foreach (var key in Request.Form.Keys)
            {
                Console.WriteLine($"Form Field: {key} = {Request.Form[key]}");
            }
            Console.WriteLine("=========================");
            if (!_databaseService.IsInitialized)
            {
                Console.WriteLine("Database not initialized");
                TempData["ErrorMessage"] = "Database not configured. Please set up database first.";
                return RedirectToAction("General", "Settings");
            }
            Console.WriteLine($"=== METER OBJECT DATA ===");
            Console.WriteLine($"Meter ID: {meter.Id}");
            Console.WriteLine($"Meter Name: '{meter.Name}'");
            Console.WriteLine($"Meter Label: '{meter.Label}'");
            Console.WriteLine($"Meter Unit: '{meter.Unit}'");
            Console.WriteLine($"Meter Type: '{meter.Type}'");
            Console.WriteLine($"Meter ParentMeterId: '{meter.ParentMeterId}'");
            Console.WriteLine($"Meter LastReading: '{meter.LastReading}'");
            Console.WriteLine($"Meter TenantId: '{meter.TenantId}'");
            Console.WriteLine($"Meter Active: {meter.Active}");
            Console.WriteLine($"=========================");
            Console.WriteLine($"ModelState.IsValid: {ModelState.IsValid}");

            if (!ModelState.IsValid)
            {
                Console.WriteLine("=== VALIDATION ERRORS ===");
                foreach (var modelError in ModelState)
                {
                    var key = modelError.Key;
                    var errors = modelError.Value.Errors;
                    if (errors.Count > 0)
                    {
                        Console.WriteLine($"Field: {key}");
                        foreach (var error in errors)
                        {
                            Console.WriteLine($"  Error: {error.ErrorMessage}");
                            if (error.Exception != null)
                            {
                                Console.WriteLine($"  Exception: {error.Exception.Message}");
                            }
                        }
                    }
                }
                Console.WriteLine("========================");
                var errorMessages = ModelState
                    .Where(x => x.Value.Errors.Count > 0)
                    .Select(x => $"{x.Key}: {string.Join(", ", x.Value.Errors.Select(e => e.ErrorMessage))}")
                    .ToList();

                TempData["ErrorMessage"] = $"Invalid meter data. Errors: {string.Join("; ", errorMessages)}";
                return RedirectToAction("Management", new { id = meter.Id });
            }

            Console.WriteLine("ModelState is valid, proceeding with update...");

            try
            {
                using (var connection = new NpgsqlConnection(_databaseService.GetConnectionString()))
                {
                    await connection.OpenAsync();
                    Console.WriteLine("Database connection opened for update");
                    using var transaction = await connection.BeginTransactionAsync();
                    Console.WriteLine("Transaction started");

                    try
                    {
                        string sql = @"
                    UPDATE ""Meters""
                    SET ""Name"" = @Name,
                        ""Label"" = @Label,
                        ""Unit"" = @Unit,
                        ""ParentId"" = @ParentId,
                        ""LastReading"" = @LastReading,
                        ""Type"" = @Type,
                        ""Active"" = @Active,
                        ""TenantID"" = @TenantId
                    WHERE ""MeterId"" = @MeterId";

                        using var cmd = new NpgsqlCommand(sql, connection, transaction);

                        cmd.Parameters.AddWithValue("@MeterId", meter.Id);
                        cmd.Parameters.AddWithValue("@Name", meter.Name ?? "");
                        cmd.Parameters.AddWithValue("@Label", meter.Label ?? (object)DBNull.Value);
                        cmd.Parameters.AddWithValue("@Unit", string.IsNullOrEmpty(meter.Unit) ? "" : meter.Unit);
                        int? parentId = null;
                        if (!string.IsNullOrEmpty(meter.ParentMeterId) && int.TryParse(meter.ParentMeterId, out int pid))
                        {
                            parentId = pid;
                        }
                        cmd.Parameters.AddWithValue("@ParentId", parentId.HasValue ? parentId.Value : DBNull.Value);
                        int lastReading = 0;
                        if (!string.IsNullOrEmpty(meter.LastReading) && int.TryParse(meter.LastReading, out int reading))
                        {
                            lastReading = reading;
                        }
                        cmd.Parameters.AddWithValue("@LastReading", lastReading);

                        cmd.Parameters.AddWithValue("@Type", meter.Type.ToLower());
                        cmd.Parameters.AddWithValue("@Active", meter.Active);
                        int? tenantId = null;
                        if (!string.IsNullOrEmpty(meter.TenantId) && int.TryParse(meter.TenantId, out int tid))
                        {
                            tenantId = tid;
                        }
                        cmd.Parameters.AddWithValue("@TenantId", tenantId.HasValue ? tenantId.Value : DBNull.Value);

                        Console.WriteLine($"Executing SQL update for meter ID: {meter.Id}");
                        var rowsAffected = await cmd.ExecuteNonQueryAsync();
                        Console.WriteLine($"Rows affected: {rowsAffected}");

                        await transaction.CommitAsync();
                        Console.WriteLine("Transaction committed successfully");

                        TempData["SuccessMessage"] = "Meter updated successfully.";
                    }
                    catch (Exception ex)
                    {
                        await transaction.RollbackAsync();
                        Console.WriteLine($"Database error, transaction rolled back: {ex.Message}");
                        Console.WriteLine($"Stack trace: {ex.StackTrace}");
                        throw new Exception($"Failed to update meter: {ex.Message}", ex);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Update error: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
                TempData["ErrorMessage"] = $"Error updating meter: {ex.Message}";
            }

            Console.WriteLine("Redirecting back to Management page");
            return RedirectToAction("Management", new { id = meter.Id });
        }


        public async Task<IActionResult> Readings(int page = 1, int pageSize = 10)
        {
            if (!_databaseService.IsInitialized)
            {
                TempData["ErrorMessage"] = "Database not configured. Please set up database first.";
                return RedirectToAction("General", "Settings");
            }

            try
            {
                var searchCriteria = new MeterSearchCriteria();
                var meters = await _meterRepository.GetMetersAsync(searchCriteria, page, pageSize);
                var totalCount = await _meterRepository.GetTotalMetersCountAsync(searchCriteria);
                var viewModel = new MeterReadingsViewModel
                {
                    Readings = new List<MeterReading>(), 
                    AvailableMeters = meters.Select(m => new MeterOption
                    {
                        MeterId = m.Id,
                        Name = m.Name,
                        Unit = m.Unit,
                        Type = m.Type
                    }).ToList(),
                    TotalItems = totalCount,
                    CurrentPage = page,
                    TotalPages = (totalCount + pageSize - 1) / pageSize
                };

                return View(viewModel);
            }
            catch (System.Exception ex)
            {
                TempData["ErrorMessage"] = $"Database error: {ex.Message}";
                return View(new MeterReadingsViewModel());
            }
        }
        private List<SelectListItem> GetTenantOptions()
        {
            var options = new List<SelectListItem>
    {
        new SelectListItem { Value = "", Text = "None" }
    };

            try
            {
                using (var connection = new NpgsqlConnection(_databaseService.GetConnectionString()))
                {
                    connection.Open();

                    string sql = @"
SELECT t.""TenantID"", td.""CompanyName""
FROM ""Tenants"" t
LEFT JOIN ""TenantDetails"" td ON t.""TenantID"" = td.""TenantID""
WHERE t.""CompanyId"" = @CompanyId
ORDER BY td.""CompanyName""";

                    using var cmd = new NpgsqlCommand(sql, connection);
                    cmd.Parameters.AddWithValue("@CompanyId", _companyContext.CurrentCompanyId); 
                    using var reader = cmd.ExecuteReader();

                    while (reader.Read())
                    {
                        int tenantId = reader.GetInt32(0);
                        string companyName = !reader.IsDBNull(1) ? reader.GetString(1) : $"Tenant ID: {tenantId}";

                        options.Add(new SelectListItem
                        {
                            Value = tenantId.ToString(),
                            Text = companyName
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error getting tenant options: {ex.Message}");
            }

            return options;
        }

    }
}