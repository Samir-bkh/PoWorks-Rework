using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Data.SqlClient;
using Npgsql;
using PoWorks_Rework.Models;
using PoWorks_Rework.Services;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace PoWorks_Rework.Controllers
{
    public class HdsImportController : Controller
    {
        private readonly ILogger<HdsImportController> _logger;
        private readonly SqlServerService _sqlServerService;
        private readonly DatabaseService _databaseService;

        // MeterReading class needs to be public so it can be accessed
        public class MeterReading
        {
            public DateTime Timestamp { get; set; }
            public decimal Value { get; set; }
            public int Quality { get; set; } = 100; // Default good quality
        }

        public HdsImportController(
            ILogger<HdsImportController> logger,
            SqlServerService sqlServerService,
            DatabaseService databaseService)
        {
            _logger = logger;
            _sqlServerService = sqlServerService;
            _databaseService = databaseService;
        }

        [HttpGet]
        public async Task<IActionResult> GetTables()
        {
            if (!_sqlServerService.IsInitialized)
            {
                return BadRequest("SQL Server connection is not configured. Please set up the SQL Server connection in General Settings.");
            }

            try
            {
                var tables = await _sqlServerService.GetAvailableTables();
                return Json(tables);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting tables from PCVue HDS");
                return StatusCode(500, $"Error: {ex.Message}");
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetMetersFromTable(string tableName, string startDate = null, string endDate = null, int limit = 1000)
        {
            if (!_sqlServerService.IsInitialized)
            {
                return BadRequest("SQL Server connection is not configured.");
            }

            try
            {
                // Validate the table name to prevent SQL injection
                if (string.IsNullOrWhiteSpace(tableName) || !IsValidTableName(tableName))
                {
                    return BadRequest("Invalid table name provided.");
                }

                // Get distinct meter names from the SQL Server table
                var hdsMeters = await GetDistinctMeterNames(tableName, startDate, endDate, limit);

                // Get parent meter options from PostgreSQL database
                var parentOptions = await GetParentMeterOptions();

                // Create view model
                var viewModel = new HDSMeterSelectionViewModel
                {
                    HdsMeters = hdsMeters,
                    ParentMeterOptions = parentOptions,
                    TableName = tableName,
                    SkipExisting = true,
                    UpdateExisting = false
                };

                return PartialView("~/Views/Shared/_HDSMeterSelectionModal.cshtml", viewModel);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error getting meters from HDS table {tableName}");
                return StatusCode(500, $"Error: {ex.Message}");
            }
        }

        // Add these modifications to your HdsImportController.cs
        // Enhanced ImportMeters method with detailed terminal logging

        [HttpPost]
        public async Task<IActionResult> ImportMeters([FromBody] ImportMetersRequest request)
        {
            // Log directly to terminal at start of method
            Console.WriteLine("=====================================================");
            Console.WriteLine($"IMPORT METERS STARTED: Request received with {request?.Meters?.Count ?? 0} meters");
            Console.WriteLine("=====================================================");

            if (!_databaseService.IsInitialized)
            {
                Console.WriteLine("ERROR: PostgreSQL database is not configured.");
                return BadRequest("PostgreSQL database is not configured.");
            }

            try
            {
                // Print information about the database connection
                Console.WriteLine($"Database connection string (masked): {MaskConnectionString(_databaseService.GetConnectionString())}");

                // Check if request data is valid
                if (request == null)
                {
                    Console.WriteLine("ERROR: Request object is null");
                    return BadRequest("Request cannot be null");
                }

                if (request.Meters == null || request.Meters.Count == 0)
                {
                    Console.WriteLine("ERROR: No meters provided in request");
                    return BadRequest("No meters provided");
                }

                // Print import options
                Console.WriteLine($"Import options: SkipExisting={request.Options?.SkipExisting}, UpdateExisting={request.Options?.UpdateExisting}");

                // Prepare response object with detailed error information
                var response = new ImportMetersResponse
                {
                    Success = true,
                    ImportedCount = 0,
                    ErrorCount = 0,
                    ImportedMeters = new List<string>(),
                    ErrorMeters = new List<string>(),
                    DetailedErrors = new Dictionary<string, string>()
                };

                // Process each meter with detailed terminal logging
                Console.WriteLine("\nProcessing meters one by one:");
                Console.WriteLine("-----------------------------------------------------");

                foreach (var meter in request.Meters)
                {
                    try
                    {
                        if (meter == null || string.IsNullOrEmpty(meter.HdsMeterName))
                        {
                            Console.WriteLine("ERROR: Invalid meter data - Meter is null or has no name");
                            response.ErrorCount++;
                            response.ErrorMeters.Add("Unknown meter");
                            response.DetailedErrors.Add("Unknown meter", "Meter data is null or missing name");
                            continue;
                        }

                        Console.WriteLine($"\nProcessing meter: {meter.HdsMeterName}");
                        Console.WriteLine($"  Type: {meter.Type}, Active: {meter.Active}, Unit: {meter.Unit ?? "None"}, ParentId: {meter.ParentMeterId ?? "None"}");

                        // Step 1: Check if meter exists (if SkipExisting is true)
                        int? existingMeterId = null;
                        if (request.Options.SkipExisting || request.Options.UpdateExisting)
                        {
                            existingMeterId = await CheckIfMeterExistsByName(meter.HdsMeterName);
                            Console.WriteLine($"  Meter existence check result: {(existingMeterId.HasValue ? $"Found with ID {existingMeterId}" : "Not found")}");

                            if (existingMeterId.HasValue && request.Options.SkipExisting && !request.Options.UpdateExisting)
                            {
                                Console.WriteLine($"  Skipping existing meter (ID: {existingMeterId})");
                                continue;
                            }
                        }

                        // Step 2: Create or update meter
                        int meterId;
                        if (existingMeterId.HasValue && request.Options.UpdateExisting)
                        {
                            // Update existing meter
                            try
                            {
                                Console.WriteLine($"  Updating existing meter with ID {existingMeterId}");
                                meterId = await UpdateExistingMeterWithHDSItem(existingMeterId.Value, meter);
                                Console.WriteLine($"  Successfully updated meter with ID: {meterId}");
                            }
                            catch (Exception ex)
                            {
                                string errorMessage = $"Error updating meter {meter.HdsMeterName}: {ex.Message}";
                                if (ex.InnerException != null)
                                {
                                    errorMessage += $" - {ex.InnerException.Message}";
                                }

                                Console.WriteLine($"  ERROR updating meter: {errorMessage}");
                                Console.WriteLine($"  Exception stack trace: {ex.StackTrace}");

                                response.ErrorCount++;
                                response.ErrorMeters.Add(meter.HdsMeterName);
                                response.DetailedErrors.Add(meter.HdsMeterName, errorMessage);
                                continue;
                            }
                        }
                        else
                        {
                            // Create new meter
                            try
                            {
                                Console.WriteLine($"  Creating new meter");
                                meterId = await CreateNewMeterFromHDSItem(meter);
                                Console.WriteLine($"  Successfully created new meter with ID: {meterId}");
                            }
                            catch (Exception ex)
                            {
                                string errorMessage = $"Error creating meter {meter.HdsMeterName}: {ex.Message}";
                                if (ex.InnerException != null)
                                {
                                    errorMessage += $" - {ex.InnerException.Message}";
                                }

                                Console.WriteLine($"  ERROR creating meter: {errorMessage}");
                                Console.WriteLine($"  Exception stack trace: {ex.StackTrace}");

                                response.ErrorCount++;
                                response.ErrorMeters.Add(meter.HdsMeterName);
                                response.DetailedErrors.Add(meter.HdsMeterName, errorMessage);
                                continue;
                            }
                        }

                        // Mark as successfully imported
                        response.ImportedCount++;
                        response.ImportedMeters.Add(meter.HdsMeterName);
                        Console.WriteLine($"  Meter {meter.HdsMeterName} successfully imported");
                    }
                    catch (Exception ex)
                    {
                        string meterName = meter?.HdsMeterName ?? "Unknown meter";
                        string errorMessage = $"Unexpected error processing meter {meterName}: {ex.Message}";
                        if (ex.InnerException != null)
                        {
                            errorMessage += $" - {ex.InnerException.Message}";
                        }

                        Console.WriteLine($"  CRITICAL ERROR: {errorMessage}");
                        Console.WriteLine($"  Exception stack trace: {ex.StackTrace}");

                        response.ErrorCount++;
                        response.ErrorMeters.Add(meterName);
                        response.DetailedErrors.Add(meterName, errorMessage);
                    }
                }

                // Update the success flag based on errors
                response.Success = response.ErrorCount == 0;

                // If we had errors, add an overall error message
                if (response.ErrorCount > 0)
                {
                    response.ErrorMessage = $"Import completed with {response.ErrorCount} errors. See detailed error messages.";
                }

                Console.WriteLine("\n-----------------------------------------------------");
                Console.WriteLine($"IMPORT COMPLETED: {response.ImportedCount} meters imported, {response.ErrorCount} errors");
                if (response.ErrorCount > 0)
                {
                    Console.WriteLine("\nERROR SUMMARY:");
                    foreach (var error in response.DetailedErrors)
                    {
                        Console.WriteLine($"- {error.Key}: {error.Value}");
                    }
                }
                Console.WriteLine("=====================================================");

                return Json(response);
            }
            catch (Exception ex)
            {
                string errorMessage = $"Fatal error during meter import: {ex.Message}";
                if (ex.InnerException != null)
                {
                    errorMessage += $" - {ex.InnerException.Message}";
                }

                Console.WriteLine("\n=====================================================");
                Console.WriteLine($"CRITICAL IMPORT ERROR: {errorMessage}");
                Console.WriteLine($"Exception stack trace: {ex.StackTrace}");
                Console.WriteLine("=====================================================");

                return StatusCode(500, new ImportMetersResponse
                {
                    Success = false,
                    ErrorMessage = errorMessage,
                    DetailedErrors = new Dictionary<string, string> { { "Global error", errorMessage } }
                });
            }
        }

        // Add this helper method to mask sensitive connection string information
        private string MaskConnectionString(string connectionString)
        {
            if (string.IsNullOrEmpty(connectionString))
                return "null";

            try
            {
                // Mask password
                return System.Text.RegularExpressions.Regex.Replace(
                    connectionString,
                    @"Password=([^;]*)",
                    "Password=*****",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            }
            catch
            {
                return "Error masking connection string";
            }
        }

        // Add detailed console logging to CreateNewMeterFromHDSItem method
        private async Task<int> CreateNewMeterFromHDSItem(HDSMeterItem meter)
        {
            Console.WriteLine($"  CreateNewMeterFromHDSItem started for {meter?.HdsMeterName ?? "null"}");

            if (meter == null)
            {
                Console.WriteLine("  ERROR: Meter data is null");
                throw new ArgumentNullException(nameof(meter), "Meter data cannot be null");
            }

            if (string.IsNullOrEmpty(meter.HdsMeterName))
            {
                Console.WriteLine("  ERROR: Meter name is required");
                throw new ArgumentException("Meter name is required", nameof(meter));
            }

            Console.WriteLine($"  Creating meter: {meter.HdsMeterName}");

            using (var connection = new NpgsqlConnection(_databaseService.GetConnectionString()))
            {
                try
                {
                    Console.WriteLine("  Opening database connection");
                    await connection.OpenAsync();
                    Console.WriteLine("  Database connection opened successfully");

                    string sql = @"
                INSERT INTO ""Meters"" (""Name"", ""Unit"", ""ParentId"", ""LastReading"", ""Type"", ""Active"", ""TenantID"")
                VALUES (@Name, @Unit, @ParentId, @LastReading, @Type, @Active, @TenantId)
                RETURNING ""MeterId""";

                    Console.WriteLine("  SQL Insert statement prepared");
                    Console.WriteLine($"  SQL: {sql}");

                    using (var command = new NpgsqlCommand(sql, connection))
                    {
                        // Set parameters with extensive logging
                        command.Parameters.AddWithValue("@Name", meter.HdsMeterName);
                        Console.WriteLine($"  Parameter @Name set to: {meter.HdsMeterName}");

                        command.Parameters.AddWithValue("@Unit", meter.Unit ?? "");
                        Console.WriteLine($"  Parameter @Unit set to: {meter.Unit ?? ""}");

                        // Handle parent meter ID
                        if (!string.IsNullOrEmpty(meter.ParentMeterId) && int.TryParse(meter.ParentMeterId, out int parentId))
                        {
                            command.Parameters.AddWithValue("@ParentId", parentId);
                            Console.WriteLine($"  Parameter @ParentId set to: {parentId}");
                        }
                        else
                        {
                            command.Parameters.AddWithValue("@ParentId", DBNull.Value);
                            Console.WriteLine("  Parameter @ParentId set to NULL");
                        }

                        // Set default last reading to 0
                        command.Parameters.AddWithValue("@LastReading", 0);
                        Console.WriteLine("  Parameter @LastReading set to: 0");

                        // Ensure type is lowercase for DB consistency
                        string meterType = (meter.Type ?? "main").ToLower();
                        command.Parameters.AddWithValue("@Type", meterType);
                        Console.WriteLine($"  Parameter @Type set to: {meterType}");

                        // Ensure active is a boolean value
                        command.Parameters.AddWithValue("@Active", meter.Active);
                        Console.WriteLine($"  Parameter @Active set to: {meter.Active} (type: {meter.Active.GetType().Name})");

                        // Handle tenant ID if provided
                        if (!string.IsNullOrEmpty(meter.TenantId) && int.TryParse(meter.TenantId, out int tenantId))
                        {
                            command.Parameters.AddWithValue("@TenantId", tenantId);
                            Console.WriteLine($"  Parameter @TenantId set to: {tenantId}");
                        }
                        else
                        {
                            command.Parameters.AddWithValue("@TenantId", DBNull.Value);
                            Console.WriteLine("  Parameter @TenantId set to NULL");
                        }

                        // Execute and get the new meter ID
                        Console.WriteLine("  Executing SQL INSERT command");

                        var result = await command.ExecuteScalarAsync();
                        Console.WriteLine($"  Execute result: {result}");

                        if (result == null || result == DBNull.Value)
                        {
                            Console.WriteLine("  ERROR: Insert operation did not return a meter ID");
                            throw new Exception("Insert operation did not return a meter ID");
                        }

                        int newMeterId = Convert.ToInt32(result);
                        Console.WriteLine($"  New meter created with ID: {newMeterId}");
                        return newMeterId;
                    }
                }
                catch (NpgsqlException npgEx)
                {
                    Console.WriteLine($"  POSTGRES ERROR: {npgEx.Message}");
                    if (npgEx.InnerException != null)
                        Console.WriteLine($"  INNER ERROR: {npgEx.InnerException.Message}");

                    Console.WriteLine($"  PostgreSQL error code: {npgEx.SqlState}");
                    Console.WriteLine($"  Stack trace: {npgEx.StackTrace}");

                    throw new Exception($"Database error creating meter: {npgEx.Message}", npgEx);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"  GENERAL ERROR: {ex.Message}");
                    if (ex.InnerException != null)
                        Console.WriteLine($"  INNER ERROR: {ex.InnerException.Message}");

                    Console.WriteLine($"  Stack trace: {ex.StackTrace}");
                    throw;
                }
            }
        }

        // Enhanced CreateNewMeter method with additional validation and logging
        private async Task<int> CreateNewMeter(HDSMeterItem meter)
        {
            if (meter == null)
            {
                throw new ArgumentNullException(nameof(meter), "Meter data cannot be null");
            }

            if (string.IsNullOrEmpty(meter.HdsMeterName))
            {
                throw new ArgumentException("Meter name is required", nameof(meter));
            }

            _logger.LogInformation($"Creating meter: {meter.HdsMeterName}, Type: {meter.Type}, Active: {meter.Active}");

            using (var connection = new NpgsqlConnection(_databaseService.GetConnectionString()))
            {
                try
                {
                    await connection.OpenAsync();
                    _logger.LogInformation($"Database connection opened successfully for creating meter {meter.HdsMeterName}");

                    string sql = @"
                INSERT INTO ""Meters"" (""Name"", ""Unit"", ""ParentId"", ""LastReading"", ""Type"", ""Active"", ""TenantID"")
                VALUES (@Name, @Unit, @ParentId, @LastReading, @Type, @Active, @TenantId)
                RETURNING ""MeterId""";

                    using (var command = new NpgsqlCommand(sql, connection))
                    {
                        // Set parameters with logging
                        command.Parameters.AddWithValue("@Name", meter.HdsMeterName);
                        _logger.LogDebug($"Parameter @Name set to: {meter.HdsMeterName}");

                        command.Parameters.AddWithValue("@Unit", meter.Unit ?? "");
                        _logger.LogDebug($"Parameter @Unit set to: {meter.Unit ?? ""}");

                        // Handle parent meter ID
                        if (!string.IsNullOrEmpty(meter.ParentMeterId) && int.TryParse(meter.ParentMeterId, out int parentId))
                        {
                            command.Parameters.AddWithValue("@ParentId", parentId);
                            _logger.LogDebug($"Parameter @ParentId set to: {parentId}");
                        }
                        else
                        {
                            command.Parameters.AddWithValue("@ParentId", DBNull.Value);
                            _logger.LogDebug("Parameter @ParentId set to NULL");
                        }

                        // Set default last reading to 0
                        command.Parameters.AddWithValue("@LastReading", 0);
                        _logger.LogDebug("Parameter @LastReading set to: 0");

                        // Ensure type is lowercase for DB consistency
                        string meterType = (meter.Type ?? "main").ToLower();
                        command.Parameters.AddWithValue("@Type", meterType);
                        _logger.LogDebug($"Parameter @Type set to: {meterType}");

                        // Ensure active is a boolean value
                        command.Parameters.AddWithValue("@Active", meter.Active);
                        _logger.LogDebug($"Parameter @Active set to: {meter.Active} (type: {meter.Active.GetType().Name})");

                        // Handle tenant ID if provided
                        if (!string.IsNullOrEmpty(meter.TenantId) && int.TryParse(meter.TenantId, out int tenantId))
                        {
                            command.Parameters.AddWithValue("@TenantId", tenantId);
                            _logger.LogDebug($"Parameter @TenantId set to: {tenantId}");
                        }
                        else
                        {
                            command.Parameters.AddWithValue("@TenantId", DBNull.Value);
                            _logger.LogDebug("Parameter @TenantId set to NULL");
                        }

                        // Execute and get the new meter ID
                        _logger.LogInformation($"Executing INSERT SQL for meter {meter.HdsMeterName}");

                        var result = await command.ExecuteScalarAsync();

                        if (result == null || result == DBNull.Value)
                        {
                            throw new Exception("Insert operation did not return a meter ID");
                        }

                        int newMeterId = Convert.ToInt32(result);
                        _logger.LogInformation($"New meter created with ID: {newMeterId}");
                        return newMeterId;
                    }
                }
                catch (NpgsqlException npgEx)
                {
                    _logger.LogError(npgEx, $"PostgreSQL error creating meter {meter.HdsMeterName}: {npgEx.Message}");
                    throw new Exception($"Database error creating meter: {npgEx.Message}", npgEx);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Unexpected error creating meter {meter.HdsMeterName}: {ex.Message}");
                    throw;
                }
            }
        }

        // Enhanced UpdateExistingMeter method with improved error handling
        private async Task<int> UpdateExistingMeter(int meterId, HDSMeterItem meter)
        {
            if (meter == null)
            {
                throw new ArgumentNullException(nameof(meter), "Meter data cannot be null");
            }

            if (meterId <= 0)
            {
                throw new ArgumentException("Invalid meter ID", nameof(meterId));
            }

            _logger.LogInformation($"Updating meter: ID={meterId}, Name={meter.HdsMeterName}, Type={meter.Type}, Active={meter.Active}");

            using (var connection = new NpgsqlConnection(_databaseService.GetConnectionString()))
            {
                try
                {
                    await connection.OpenAsync();
                    _logger.LogInformation($"Database connection opened successfully for updating meter ID {meterId}");

                    // First verify meter exists
                    string checkSql = @"SELECT COUNT(*) FROM ""Meters"" WHERE ""MeterId"" = @MeterId";
                    using (var checkCommand = new NpgsqlCommand(checkSql, connection))
                    {
                        checkCommand.Parameters.AddWithValue("@MeterId", meterId);
                        int count = Convert.ToInt32(await checkCommand.ExecuteScalarAsync());

                        if (count == 0)
                        {
                            throw new Exception($"Meter with ID {meterId} not found");
                        }
                    }

                    string sql = @"
                UPDATE ""Meters""
                SET ""Unit"" = @Unit,
                    ""ParentId"" = @ParentId,
                    ""Type"" = @Type,
                    ""Active"" = @Active,
                    ""TenantID"" = @TenantId
                WHERE ""MeterId"" = @MeterId";

                    using (var command = new NpgsqlCommand(sql, connection))
                    {
                        // Set parameters with logging
                        command.Parameters.AddWithValue("@MeterId", meterId);
                        _logger.LogDebug($"Parameter @MeterId set to: {meterId}");

                        command.Parameters.AddWithValue("@Unit", meter.Unit ?? "");
                        _logger.LogDebug($"Parameter @Unit set to: {meter.Unit ?? ""}");

                        // Handle parent meter ID
                        if (!string.IsNullOrEmpty(meter.ParentMeterId) && int.TryParse(meter.ParentMeterId, out int parentId))
                        {
                            command.Parameters.AddWithValue("@ParentId", parentId);
                            _logger.LogDebug($"Parameter @ParentId set to: {parentId}");
                        }
                        else
                        {
                            command.Parameters.AddWithValue("@ParentId", DBNull.Value);
                            _logger.LogDebug("Parameter @ParentId set to NULL");
                        }

                        // Ensure type is lowercase for DB consistency
                        string meterType = (meter.Type ?? "main").ToLower();
                        command.Parameters.AddWithValue("@Type", meterType);
                        _logger.LogDebug($"Parameter @Type set to: {meterType}");

                        // Ensure active is a boolean value
                        command.Parameters.AddWithValue("@Active", meter.Active);
                        _logger.LogDebug($"Parameter @Active set to: {meter.Active} (type: {meter.Active.GetType().Name})");

                        // Handle tenant ID if provided
                        if (!string.IsNullOrEmpty(meter.TenantId) && int.TryParse(meter.TenantId, out int tenantId))
                        {
                            command.Parameters.AddWithValue("@TenantId", tenantId);
                            _logger.LogDebug($"Parameter @TenantId set to: {tenantId}");
                        }
                        else
                        {
                            command.Parameters.AddWithValue("@TenantId", DBNull.Value);
                            _logger.LogDebug("Parameter @TenantId set to NULL");
                        }

                        // Execute the update
                        _logger.LogInformation($"Executing UPDATE SQL for meter ID {meterId}");
                        int rowsAffected = await command.ExecuteNonQueryAsync();

                        if (rowsAffected == 0)
                        {
                            throw new Exception($"Update operation did not affect any rows for meter ID {meterId}");
                        }

                        _logger.LogInformation($"Meter ID {meterId} updated successfully, {rowsAffected} rows affected");
                        return meterId;
                    }
                }
                catch (NpgsqlException npgEx)
                {
                    _logger.LogError(npgEx, $"PostgreSQL error updating meter ID {meterId}: {npgEx.Message}");
                    throw new Exception($"Database error updating meter: {npgEx.Message}", npgEx);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Unexpected error updating meter ID {meterId}: {ex.Message}");
                    throw;
                }
            }
        }

        // Renamed to avoid ambiguity
        private async Task<int?> CheckIfMeterExistsByName(string meterName)
        {
            try
            {
                using (var connection = new NpgsqlConnection(_databaseService.GetConnectionString()))
                {
                    await connection.OpenAsync();

                    string sql = @"
                SELECT ""MeterId"" 
                FROM ""Meters"" 
                WHERE ""Name"" = @Name
                LIMIT 1";

                    using (var command = new NpgsqlCommand(sql, connection))
                    {
                        command.Parameters.AddWithValue("@Name", meterName);
                        var result = await command.ExecuteScalarAsync();
                        if (result != null && result != DBNull.Value)
                        {
                            return Convert.ToInt32(result);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error checking if meter exists: {meterName}");
                // Don't throw here, just return null
            }

            return null;
        }

       

        private async Task<int> UpdateExistingMeterWithHDSItem(int meterId, HDSMeterItem meter)
        {
            if (meter == null)
            {
                throw new ArgumentNullException(nameof(meter), "Meter data cannot be null");
            }

            if (meterId <= 0)
            {
                throw new ArgumentException("Invalid meter ID", nameof(meterId));
            }

            _logger.LogInformation($"Updating meter: ID={meterId}, Name={meter.HdsMeterName}, Type={meter.Type}, Active={meter.Active}");

            using (var connection = new NpgsqlConnection(_databaseService.GetConnectionString()))
            {
                try
                {
                    await connection.OpenAsync();
                    _logger.LogInformation($"Database connection opened successfully for updating meter ID {meterId}");

                    // First verify meter exists
                    string checkSql = @"SELECT COUNT(*) FROM ""Meters"" WHERE ""MeterId"" = @MeterId";
                    using (var checkCommand = new NpgsqlCommand(checkSql, connection))
                    {
                        checkCommand.Parameters.AddWithValue("@MeterId", meterId);
                        int count = Convert.ToInt32(await checkCommand.ExecuteScalarAsync());

                        if (count == 0)
                        {
                            throw new Exception($"Meter with ID {meterId} not found");
                        }
                    }

                    string sql = @"
                UPDATE ""Meters""
                SET ""Unit"" = @Unit,
                    ""ParentId"" = @ParentId,
                    ""Type"" = @Type,
                    ""Active"" = @Active,
                    ""TenantID"" = @TenantId
                WHERE ""MeterId"" = @MeterId";

                    using (var command = new NpgsqlCommand(sql, connection))
                    {
                        // Set parameters with logging
                        command.Parameters.AddWithValue("@MeterId", meterId);
                        _logger.LogDebug($"Parameter @MeterId set to: {meterId}");

                        command.Parameters.AddWithValue("@Unit", meter.Unit ?? "");
                        _logger.LogDebug($"Parameter @Unit set to: {meter.Unit ?? ""}");

                        // Handle parent meter ID
                        if (!string.IsNullOrEmpty(meter.ParentMeterId) && int.TryParse(meter.ParentMeterId, out int parentId))
                        {
                            command.Parameters.AddWithValue("@ParentId", parentId);
                            _logger.LogDebug($"Parameter @ParentId set to: {parentId}");
                        }
                        else
                        {
                            command.Parameters.AddWithValue("@ParentId", DBNull.Value);
                            _logger.LogDebug("Parameter @ParentId set to NULL");
                        }

                        // Ensure type is lowercase for DB consistency
                        string meterType = (meter.Type ?? "main").ToLower();
                        command.Parameters.AddWithValue("@Type", meterType);
                        _logger.LogDebug($"Parameter @Type set to: {meterType}");

                        // Ensure active is a boolean value
                        command.Parameters.AddWithValue("@Active", meter.Active);
                        _logger.LogDebug($"Parameter @Active set to: {meter.Active} (type: {meter.Active.GetType().Name})");

                        // Handle tenant ID if provided
                        if (!string.IsNullOrEmpty(meter.TenantId) && int.TryParse(meter.TenantId, out int tenantId))
                        {
                            command.Parameters.AddWithValue("@TenantId", tenantId);
                            _logger.LogDebug($"Parameter @TenantId set to: {tenantId}");
                        }
                        else
                        {
                            command.Parameters.AddWithValue("@TenantId", DBNull.Value);
                            _logger.LogDebug("Parameter @TenantId set to NULL");
                        }

                        // Execute the update
                        _logger.LogInformation($"Executing UPDATE SQL for meter ID {meterId}");
                        int rowsAffected = await command.ExecuteNonQueryAsync();

                        if (rowsAffected == 0)
                        {
                            throw new Exception($"Update operation did not affect any rows for meter ID {meterId}");
                        }

                        _logger.LogInformation($"Meter ID {meterId} updated successfully, {rowsAffected} rows affected");
                        return meterId;
                    }
                }
                catch (NpgsqlException npgEx)
                {
                    _logger.LogError(npgEx, $"PostgreSQL error updating meter ID {meterId}: {npgEx.Message}");
                    throw new Exception($"Database error updating meter: {npgEx.Message}", npgEx);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Unexpected error updating meter ID {meterId}: {ex.Message}");
                    throw;
                }
            }
        }


        #region Helper Methods

        private bool IsValidTableName(string tableName)
        {
            // Simple validation to prevent SQL injection
            // Only allow alphanumeric characters, underscores, and optional square brackets
            return System.Text.RegularExpressions.Regex.IsMatch(
                tableName, @"^(\[?[a-zA-Z0-9_]+\]?)|(\[?[a-zA-Z0-9_]+\]?\.\[?[a-zA-Z0-9_]+\]?)$");
        }

        private async Task<bool> CheckIfColumnExists(SqlConnection connection, string tableName, string columnName)
        {
            try
            {
                string sql = @"
                    SELECT COUNT(*) 
                    FROM INFORMATION_SCHEMA.COLUMNS 
                    WHERE TABLE_NAME = @TableName 
                    AND COLUMN_NAME = @ColumnName";

                using (var command = new SqlCommand(sql, connection))
                {
                    command.Parameters.AddWithValue("@TableName", tableName);
                    command.Parameters.AddWithValue("@ColumnName", columnName);

                    var result = await command.ExecuteScalarAsync();
                    return Convert.ToInt32(result) > 0;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error checking if column {columnName} exists in table {tableName}");
                return false;
            }
        }

        private async Task<List<HDSMeterItem>> GetDistinctMeterNames(string tableName, string startDate, string endDate, int limit)
        {
            var meters = new List<HDSMeterItem>();

            try
            {
                using (var connection = _sqlServerService.GetConnection())
                {
                    await connection.OpenAsync();

                    // Build query based on provided parameters
                    string whereClause = "";

                    // First check if both NAME exists in the table
                    bool hasNameColumn = await CheckIfColumnExists(connection, tableName, "NAME");
                    bool hasTimestampColumn = await CheckIfColumnExists(connection, tableName, "TIMESTAMP");

                    if (!hasNameColumn)
                    {
                        _logger.LogWarning($"Table {tableName} does not have a NAME column. Using sample data.");
                        return GenerateSampleMeters();
                    }

                    if (hasTimestampColumn && (!string.IsNullOrEmpty(startDate) || !string.IsNullOrEmpty(endDate)))
                    {
                        whereClause = " WHERE NAME IS NOT NULL";

                        if (!string.IsNullOrEmpty(startDate))
                        {
                            whereClause += " AND TIMESTAMP >= @StartDate";
                        }

                        if (!string.IsNullOrEmpty(endDate))
                        {
                            whereClause += " AND TIMESTAMP <= @EndDate";
                        }
                    }
                    else
                    {
                        // If no timestamp constraints or no timestamp column
                        whereClause = " WHERE NAME IS NOT NULL";
                    }

                    // Assume HDS tables have NAME column
                    string sql = $@"
                        SELECT DISTINCT TOP {limit} NAME 
                        FROM {tableName}
                        {whereClause}
                        ORDER BY NAME";

                    using (var command = new SqlCommand(sql, connection))
                    {
                        // Add parameters if date range is specified
                        if (hasTimestampColumn)
                        {
                            if (!string.IsNullOrEmpty(startDate))
                            {
                                command.Parameters.AddWithValue("@StartDate", DateTime.Parse(startDate));
                            }

                            if (!string.IsNullOrEmpty(endDate))
                            {
                                command.Parameters.AddWithValue("@EndDate", DateTime.Parse(endDate));
                            }
                        }

                        using (var reader = await command.ExecuteReaderAsync())
                        {
                            while (await reader.ReadAsync())
                            {
                                string meterName = reader.GetString(0);
                                // Try to determine unit based on name patterns or other table data
                                string unit = DetermineUnitFromName(meterName);

                                meters.Add(new HDSMeterItem
                                {
                                    HdsMeterName = meterName,
                                    Unit = unit,
                                    Type = "Main", // Default to Main
                                    Active = true,
                                    IsSelected = true
                                });
                            }
                        }
                    }
                }

                _logger.LogInformation($"Found {meters.Count} distinct meter names in table {tableName}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error getting distinct meter names from table {tableName}");

                // Generate sample meters in case of error
                return GenerateSampleMeters();
            }

            // If no meters found, create sample meters for development/testing
            if (meters.Count == 0)
            {
                _logger.LogWarning($"No meters found in table {tableName}, creating sample meters for development");
                return GenerateSampleMeters();
            }

            return meters;
        }

        private List<HDSMeterItem> GenerateSampleMeters()
        {
            var meters = new List<HDSMeterItem>();

            for (int i = 1; i <= 15; i++)
            {
                var prefix = i % 3 == 0 ? "FLOW_" : (i % 3 == 1 ? "PRESSURE_" : "TEMP_");
                meters.Add(new HDSMeterItem
                {
                    HdsMeterName = $"{prefix}{i:D2}",
                    Unit = i % 3 == 0 ? "m³/h" : (i % 3 == 1 ? "bar" : "°C"),
                    Type = "Main",
                    Active = true,
                    IsSelected = true
                });
            }

            return meters;
        }

        private string DetermineUnitFromName(string meterName)
        {
            // Try to determine unit based on meter name patterns
            // This is a simplified example - in a real system, you might have more logic here
            // or retrieve this information from metadata in the HDS database
            meterName = meterName.ToLower();

            if (meterName.Contains("flow") || meterName.Contains("discharge"))
                return "m³/h";
            else if (meterName.Contains("temp") || meterName.Contains("temperature"))
                return "°C";
            else if (meterName.Contains("press") || meterName.Contains("pressure"))
                return "bar";
            else if (meterName.Contains("level"))
                return "m";
            else if (meterName.Contains("power") || meterName.Contains("energy"))
                return "kWh";
            else if (meterName.Contains("current"))
                return "A";
            else if (meterName.Contains("voltage"))
                return "V";

            return ""; // Default empty unit
        }

        private async Task<List<SelectListItem>> GetParentMeterOptions()
        {
            var options = new List<SelectListItem>
            {
                new SelectListItem { Value = "", Text = "None" }
            };

            try
            {
                using (var connection = new NpgsqlConnection(_databaseService.GetConnectionString()))
                {
                    await connection.OpenAsync();

                    string sql = @"
                        SELECT ""MeterId"", ""Name"" 
                        FROM ""Meters"" 
                        WHERE ""Type"" = 'main' AND ""Active"" = true
                        ORDER BY ""Name""";

                    using (var command = new NpgsqlCommand(sql, connection))
                    {
                        using (var reader = await command.ExecuteReaderAsync())
                        {
                            while (await reader.ReadAsync())
                            {
                                options.Add(new SelectListItem
                                {
                                    Value = reader.GetInt32(0).ToString(),
                                    Text = reader.GetString(1)
                                });
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting parent meter options");
                // Don't throw here, just return what we have
            }

            return options;
        }

        // Remove these duplicate methods since we now have renamed versions above
        // private async Task<int?> CheckIfMeterExists(string meterName) - Removed duplicate
        // private async Task<int> CreateMeter(HDSMeterItem meter) - Removed duplicate
        // private async Task<int> UpdateMeter(int meterId, HDSMeterItem meter) - Removed duplicate

        // Import readings functionality can be added back later if needed
        /* 
        private async Task<int> ImportMeterReadings(...)
        private async Task<List<MeterReading>> GetMeterReadingsFromHDS(...)
        private List<MeterReading> GenerateSampleReadings(...)
        */

        #endregion
    }
}