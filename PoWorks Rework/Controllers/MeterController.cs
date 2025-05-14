// Controllers/MeterController.cs
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Npgsql;
using PoWorks_Rework.Models;
using PoWorks_Rework.Repositories;
using PoWorks_Rework.Services;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace PoWorks_Rework.Controllers
{
    public class MeterController : BaseController
    {
        private readonly MeterRepository _meterRepository;

        public MeterController(DatabaseService databaseService, MeterRepository meterRepository)
            : base(databaseService)
        {
            _meterRepository = meterRepository;
        }

        // Controllers/MeterController.cs - Update the Management method
        // Update the Management method in MeterController
        public async Task<IActionResult> Management(int? id = null, int page = 1, int pageSize = 10)
        {
            // Check if database is initialized
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
                TenantOptions = GetTenantOptions() // Add this line to load tenant options
            };

            try
            {
                // Get search results
                viewModel.SearchResults = await _meterRepository.GetMetersAsync(searchCriteria, page, pageSize);
                viewModel.TotalItems = await _meterRepository.GetTotalMetersCountAsync(searchCriteria);
                viewModel.TotalPages = (viewModel.TotalItems + pageSize - 1) / pageSize;

                // Load selected meter if ID is provided
                if (id.HasValue)
                {
                    Console.WriteLine($"Selected meter ID: {id.Value}");
                    viewModel.SelectedMeter = await _meterRepository.GetMeterByIdAsync(id.Value);

                    if (viewModel.SelectedMeter != null)
                    {
                        Console.WriteLine($"Selected meter: {viewModel.SelectedMeter.Name}, ID: {viewModel.SelectedMeter.Id}");

                        // Get sub meters
                        viewModel.SubMeters = await _meterRepository.GetSubMetersAsync(id.Value);
                        Console.WriteLine($"Retrieved {viewModel.SubMeters.Count} sub meters for meter ID {id.Value}");
                    }
                }
                else if (viewModel.SearchResults.Count > 0)
                {
                    // Find a main meter to display by default
                    var mainMeter = viewModel.SearchResults.FirstOrDefault(m => m.Type.ToLower() == "main");

                    if (mainMeter != null)
                    {
                        viewModel.SelectedMeter = mainMeter;
                        Console.WriteLine($"Selected main meter: {viewModel.SelectedMeter.Name}, ID: {viewModel.SelectedMeter.Id}");
                    }
                    else
                    {
                        // If no main meter found, select the first one
                        viewModel.SelectedMeter = viewModel.SearchResults[0];
                        Console.WriteLine($"No main meter found, selected first meter: {viewModel.SelectedMeter.Name}, ID: {viewModel.SelectedMeter.Id}");
                    }

                    // Get sub meters
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
            // Check if database is initialized
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
                    TenantOptions = GetTenantOptions() // Add this line to load tenant options
                };

                // Get search results
                viewModel.SearchResults = await _meterRepository.GetMetersAsync(searchCriteria, page, pageSize);
                viewModel.TotalItems = await _meterRepository.GetTotalMetersCountAsync(searchCriteria);
                viewModel.TotalPages = (viewModel.TotalItems + pageSize - 1) / pageSize;

                // Select first meter if available
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
            // Check if database is initialized
            if (!_databaseService.IsInitialized)
            {
                logger.LogError("Database not initialized when attempting to create meter");
                TempData["ErrorMessage"] = "Database not configured. Please set up database first.";
                return RedirectToAction("General", "Settings");
            }

            try
            {
                // When saving as new from the details form, the meter will have an Id
                // We need to reset it to ensure a new record is created
                meter.Id = 0;

                // Log the incoming meter data
                logger.LogInformation("Creating meter: Name={Name}, Type={Type}, Unit={Unit}, LastReading={LastReading}, Active={Active}, ParentId={ParentId}, TenantId={TenantId}",
                    meter.Name, meter.Type, meter.Unit, meter.LastReading, meter.Active, meter.ParentMeterId, meter.TenantId);

                // Ensure name is not empty (a critical field)
                if (string.IsNullOrWhiteSpace(meter.Name))
                {
                    meter.Name = "Unnamed Meter " + DateTime.Now.ToString("yyyyMMddHHmmss");
                    logger.LogWarning("Empty meter name was auto-filled with a timestamp");
                }

                // Ensure Unit is not null (critical fix for NOT NULL constraint)
                if (string.IsNullOrWhiteSpace(meter.Unit))
                {
                    meter.Unit = "";  // Set to empty string instead of null
                    logger.LogWarning("Empty Unit field was set to empty string to satisfy NOT NULL constraint");
                }

                // Create a brand new connection directly
                using (var connection = new NpgsqlConnection(_databaseService.GetConnectionString()))
                {
                    await connection.OpenAsync();
                    logger.LogInformation("New database connection opened successfully");

                    // Insert using a transaction
                    using var transaction = await connection.BeginTransactionAsync();
                    try
                    {
                        string sql = @"
                    INSERT INTO ""Meters"" (""Name"", ""Unit"", ""ParentId"", ""LastReading"", ""Type"", ""Active"", ""TenantID"")
                    VALUES (@Name, @Unit, @ParentId, @LastReading, @Type, @Active, @TenantId)
                    RETURNING ""MeterId""";

                        using var cmd = new NpgsqlCommand(sql, connection, transaction);

                        cmd.Parameters.AddWithValue("@Name", meter.Name);
                        // Never pass NULL for Unit, use empty string instead
                        cmd.Parameters.AddWithValue("@Unit", meter.Unit ?? "");

                        // Parse parent meter ID if provided
                        int? parentId = null;
                        if (!string.IsNullOrEmpty(meter.ParentMeterId) && int.TryParse(meter.ParentMeterId, out int pid))
                        {
                            parentId = pid;
                        }
                        cmd.Parameters.AddWithValue("@ParentId", parentId.HasValue ? parentId.Value : DBNull.Value);

                        // Parse last reading if provided
                        int lastReading = 0;
                        if (!string.IsNullOrEmpty(meter.LastReading) && int.TryParse(meter.LastReading, out int reading))
                        {
                            lastReading = reading;
                        }
                        cmd.Parameters.AddWithValue("@LastReading", lastReading);

                        // Ensure valid type
                        string type = "main";
                        if (!string.IsNullOrWhiteSpace(meter.Type) &&
                            (meter.Type.ToLower() == "main" || meter.Type.ToLower() == "sub"))
                        {
                            type = meter.Type.ToLower();
                        }
                        cmd.Parameters.AddWithValue("@Type", type);

                        cmd.Parameters.AddWithValue("@Active", meter.Active);

                        // Parse tenant ID if provided
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

            // If we get here, there was an error - create a view model for the form
            var viewModel = new MeterManagementViewModel
            {
                SelectedMeter = meter,
                SearchCriteria = new MeterSearchCriteria()
            };

            try
            {
                // Get search results with empty criteria - using a new connection
                using (var connection = new NpgsqlConnection(_databaseService.GetConnectionString()))
                {
                    await connection.OpenAsync();

                    // Get meters
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
                            Unit = reader.GetString(reader.GetOrdinal("Unit")), // Unit will never be NULL now
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

                // Get count for pagination
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
        public async Task<IActionResult> Update(Meter meter)
        {
            // Check if database is initialized
            if (!_databaseService.IsInitialized)
            {
                TempData["ErrorMessage"] = "Database not configured. Please set up database first.";
                return RedirectToAction("General", "Settings");
            }

            if (ModelState.IsValid)
            {
                try
                {
                    // Create a brand new connection for this operation
                    using (var connection = new NpgsqlConnection(_databaseService.GetConnectionString()))
                    {
                        await connection.OpenAsync();

                        // Begin a transaction
                        using var transaction = await connection.BeginTransactionAsync();

                        try
                        {
                            string sql = @"
                        UPDATE ""Meters""
                        SET ""Name"" = @Name,
                            ""Unit"" = @Unit,
                            ""ParentId"" = @ParentId,
                            ""LastReading"" = @LastReading,
                            ""Type"" = @Type,
                            ""Active"" = @Active,
                            ""TenantID"" = @TenantId
                        WHERE ""MeterId"" = @MeterId";

                            using var cmd = new NpgsqlCommand(sql, connection, transaction);

                            cmd.Parameters.AddWithValue("@MeterId", meter.Id);
                            cmd.Parameters.AddWithValue("@Name", meter.Name);
                            cmd.Parameters.AddWithValue("@Unit", string.IsNullOrEmpty(meter.Unit) ? "" : meter.Unit);

                            // Parse parent meter ID if provided
                            int? parentId = null;
                            if (!string.IsNullOrEmpty(meter.ParentMeterId) && int.TryParse(meter.ParentMeterId, out int pid))
                            {
                                parentId = pid;
                            }
                            cmd.Parameters.AddWithValue("@ParentId", parentId.HasValue ? parentId.Value : DBNull.Value);

                            // Parse last reading if provided
                            int lastReading = 0;
                            if (!string.IsNullOrEmpty(meter.LastReading) && int.TryParse(meter.LastReading, out int reading))
                            {
                                lastReading = reading;
                            }
                            cmd.Parameters.AddWithValue("@LastReading", lastReading);

                            cmd.Parameters.AddWithValue("@Type", meter.Type.ToLower());
                            cmd.Parameters.AddWithValue("@Active", meter.Active);

                            // Parse tenant ID if provided
                            int? tenantId = null;
                            if (!string.IsNullOrEmpty(meter.TenantId) && int.TryParse(meter.TenantId, out int tid))
                            {
                                tenantId = tid;
                            }
                            cmd.Parameters.AddWithValue("@TenantId", tenantId.HasValue ? tenantId.Value : DBNull.Value);

                            await cmd.ExecuteNonQueryAsync();
                            await transaction.CommitAsync();

                            TempData["SuccessMessage"] = "Meter updated successfully.";
                        }
                        catch (Exception ex)
                        {
                            await transaction.RollbackAsync();
                            throw new Exception($"Failed to update meter: {ex.Message}", ex);
                        }
                    }
                }
                catch (Exception ex)
                {
                    TempData["ErrorMessage"] = $"Error updating meter: {ex.Message}";
                }
            }
            else
            {
                TempData["ErrorMessage"] = "Invalid meter data";
            }

            // Redirect back to the management page with the meter ID
            return RedirectToAction("Management", new { id = meter.Id });
        }



        public async Task<IActionResult> Readings(int page = 1, int pageSize = 10)
        {
            // Check if database is initialized
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
                    Meters = meters,
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
    

    // Add this method to the MeterController class
private List<SelectListItem> GetTenantOptions()
        {
            var options = new List<SelectListItem>
    {
        new SelectListItem { Value = "", Text = "None" }
    };

            try
            {
                // Create a brand new connection for this operation
                using (var connection = new NpgsqlConnection(_databaseService.GetConnectionString()))
                {
                    connection.Open();

                    string sql = @"
                SELECT t.""TenantID"", td.""CompanyName""
                FROM ""Tenants"" t
                LEFT JOIN ""TenantDetails"" td ON t.""TenantID"" = td.""TenantID""
                ORDER BY td.""CompanyName""";

                    using var cmd = new NpgsqlCommand(sql, connection);
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