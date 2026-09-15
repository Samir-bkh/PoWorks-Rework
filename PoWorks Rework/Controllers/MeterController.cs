using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Npgsql;
using PoWorks_Rework.Models;
using PoWorks_Rework.Repositories;
using PoWorks_Rework.Services;

namespace PoWorks_Rework.Controllers
{
    /// <summary>
    /// Management-facing meter configuration. Tenant users are intentionally
    /// excluded: their experience is the dashboard/analytics view of meters
    /// already assigned to them.
    /// </summary>
    [Authorize(Policy = "ManagementAccess")]
    public class MeterController : BaseController
    {
        private readonly MeterRepository _meterRepository;
        private readonly ICompanyContext _companyContext;
        private readonly ILogger<MeterController> _logger;

        public MeterController(
            DatabaseService databaseService,
            MeterRepository meterRepository,
            ICompanyContext companyContext,
            ILogger<MeterController> logger)
            : base(databaseService)
        {
            _meterRepository = meterRepository;
            _companyContext = companyContext;
            _logger = logger;
        }

        [HttpGet]
        public async Task<IActionResult> Management(
            int? id = null,
            string searchField = "Name",
            string? searchTerm = null,
            string statusFilter = "All",
            string assignmentFilter = "All",
            int page = 1,
            int pageSize = 20)
        {
            if (!_databaseService.IsInitialized)
            {
                TempData["ErrorMessage"] = "Database not configured. Please set up database first.";
                return RedirectToAction("General", "Settings");
            }

            var criteria = new MeterSearchCriteria
            {
                SearchField = NormalizeSearchField(searchField),
                SearchTerm = searchTerm,
                StatusFilter = MeterLifecycleRules.NormalizeStatus(statusFilter),
                AssignmentFilter = MeterLifecycleRules.NormalizeAssignment(assignmentFilter)
            };

            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 10, 100);

            try
            {
                var model = await BuildManagementModelAsync(criteria, id, page, pageSize);
                return View(model);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Unable to load meter management for workspace {CompanyId}",
                    _companyContext.CurrentCompanyId);

                TempData["ErrorMessage"] = "Unable to load meters. Check the application logs for details.";
                return View(new MeterManagementViewModel
                {
                    SearchCriteria = criteria,
                    CurrentPage = page,
                    PageSize = pageSize
                });
            }
        }

        /// <summary>
        /// Compatibility endpoint for old links/forms. Search is read-only and
        /// therefore intentionally GET-only.
        /// </summary>
        [HttpGet]
        public IActionResult Search(
            string searchField = "Name",
            string? searchTerm = null,
            string statusFilter = "All",
            string assignmentFilter = "All",
            int page = 1,
            int pageSize = 20)
        {
            return RedirectToAction(nameof(Management), new
            {
                searchField,
                searchTerm,
                statusFilter,
                assignmentFilter,
                page,
                pageSize
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(Meter meter)
        {
            if (!_databaseService.IsInitialized)
                return RedirectDatabaseNotConfigured();

            var companyId = _companyContext.CurrentCompanyId;
            meter.Id = 0;

            var validationErrors = MeterLifecycleRules.Validate(meter).ToList();
            var tenantId = ParseOptionalId(meter.TenantId, "Tenant", validationErrors);
            var parentId = ParseOptionalId(meter.ParentMeterId, "Parent meter", validationErrors);
            var normalizedType = MeterLifecycleRules.NormalizeType(meter.Type);

            if (normalizedType == "main")
                parentId = null;

            if (validationErrors.Count > 0)
            {
                TempData["ErrorMessage"] = string.Join(" ", validationErrors);
                return RedirectToAction(nameof(Management));
            }

            try
            {
                await using var connection = _databaseService.CreateNewConnection();
                await connection.OpenAsync();
                await using var tx = await connection.BeginTransactionAsync();

                if (!await ValidateTenantAssignmentAsync(connection, tx, tenantId))
                {
                    await tx.RollbackAsync();
                    TempData["ErrorMessage"] = "The selected tenant is disabled or does not belong to the current workspace.";
                    return RedirectToAction(nameof(Management));
                }

                var parentError = await ValidateParentAssignmentAsync(
                    connection,
                    tx,
                    meterId: null,
                    parentId);

                if (parentError != null)
                {
                    await tx.RollbackAsync();
                    TempData["ErrorMessage"] = parentError;
                    return RedirectToAction(nameof(Management));
                }

                const string sql = @"
                    INSERT INTO ""Meters"" (
                        ""Name"", ""Label"", ""Unit"", ""ParentId"",
                        ""LastReading"", ""Type"", ""Active"", ""TenantID"", ""CompanyId"")
                    VALUES (
                        @Name, @Label, @Unit, @ParentId,
                        0, @Type, @Active, @TenantId, @CompanyId)
                    RETURNING ""MeterId""";

                await using var cmd = new NpgsqlCommand(sql, connection, tx);
                AddMeterConfigurationParameters(
                    cmd,
                    meter,
                    normalizedType,
                    parentId,
                    tenantId,
                    companyId);

                var result = await cmd.ExecuteScalarAsync();
                if (result == null)
                    throw new InvalidOperationException("The database did not return the created meter id.");

                var meterId = Convert.ToInt32(result);
                var after = await GetMeterSnapshotAsync(connection, tx, meterId, companyId);

                await tx.CommitAsync();

                await AuditTrail.LogAsync(_databaseService, HttpContext, new AuditEvent
                {
                    Action = "CREATE",
                    EntityType = "Meter",
                    EntityId = meterId.ToString(),
                    CompanyId = companyId,
                    Summary = $"Meter '{meter.Name.Trim()}' created.",
                    After = after
                });

                TempData["SuccessMessage"] = "Meter created successfully.";
                return RedirectToAction(nameof(Management), new { id = meterId });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create meter in workspace {CompanyId}", companyId);

                await AuditTrail.LogAsync(_databaseService, HttpContext, new AuditEvent
                {
                    Action = "CREATE_FAILED",
                    EntityType = "Meter",
                    CompanyId = companyId,
                    Summary = "Meter creation failed.",
                    Success = false
                });

                TempData["ErrorMessage"] = "Meter could not be created. Check the application logs for details.";
                return RedirectToAction(nameof(Management));
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Update(Meter meter)
        {
            if (!_databaseService.IsInitialized)
                return RedirectDatabaseNotConfigured();

            var companyId = _companyContext.CurrentCompanyId;
            var validationErrors = MeterLifecycleRules.Validate(meter).ToList();
            var tenantId = ParseOptionalId(meter.TenantId, "Tenant", validationErrors);
            var parentId = ParseOptionalId(meter.ParentMeterId, "Parent meter", validationErrors);
            var normalizedType = MeterLifecycleRules.NormalizeType(meter.Type);

            if (meter.Id <= 0)
                validationErrors.Add("A valid meter id is required.");

            if (normalizedType == "main")
                parentId = null;

            if (validationErrors.Count > 0)
            {
                TempData["ErrorMessage"] = string.Join(" ", validationErrors);
                return RedirectToAction(nameof(Management), new { id = meter.Id });
            }

            try
            {
                await using var connection = _databaseService.CreateNewConnection();
                await connection.OpenAsync();
                await using var tx = await connection.BeginTransactionAsync();

                var before = await GetMeterSnapshotAsync(connection, tx, meter.Id, companyId);
                if (before == null)
                {
                    await tx.RollbackAsync();
                    TempData["ErrorMessage"] = "Meter not found in the current workspace.";
                    return RedirectToAction(nameof(Management));
                }

                if (!await ValidateTenantAssignmentAsync(connection, tx, tenantId, before.TenantId))
                {
                    await tx.RollbackAsync();
                    TempData["ErrorMessage"] = "The selected tenant is disabled or does not belong to the current workspace.";
                    return RedirectToAction(nameof(Management), new { id = meter.Id });
                }

                var parentError = await ValidateParentAssignmentAsync(
                    connection,
                    tx,
                    meter.Id,
                    parentId);

                if (parentError != null)
                {
                    await tx.RollbackAsync();
                    TempData["ErrorMessage"] = parentError;
                    return RedirectToAction(nameof(Management), new { id = meter.Id });
                }

                const string sql = @"
                    UPDATE ""Meters""
                    SET ""Name"" = @Name,
                        ""Label"" = @Label,
                        ""Unit"" = @Unit,
                        ""ParentId"" = @ParentId,
                        ""Type"" = @Type,
                        ""Active"" = @Active,
                        ""TenantID"" = @TenantId
                    WHERE ""MeterId"" = @MeterId
                      AND ""CompanyId"" = @CompanyId";

                await using var cmd = new NpgsqlCommand(sql, connection, tx);
                AddMeterConfigurationParameters(
                    cmd,
                    meter,
                    normalizedType,
                    parentId,
                    tenantId,
                    companyId);
                cmd.Parameters.AddWithValue("@MeterId", meter.Id);

                var affected = await cmd.ExecuteNonQueryAsync();
                if (affected != 1)
                    throw new InvalidOperationException("Meter update did not affect exactly one workspace-scoped row.");

                var after = await GetMeterSnapshotAsync(connection, tx, meter.Id, companyId);
                await tx.CommitAsync();

                var (action, summary) = DescribeSingleMeterChange(before, after!);

                await AuditTrail.LogAsync(_databaseService, HttpContext, new AuditEvent
                {
                    Action = action,
                    EntityType = "Meter",
                    EntityId = meter.Id.ToString(),
                    CompanyId = companyId,
                    Summary = summary,
                    Before = before,
                    After = after
                });

                TempData["SuccessMessage"] = "Meter updated successfully.";
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Failed to update meter {MeterId} in workspace {CompanyId}",
                    meter.Id,
                    companyId);

                await AuditTrail.LogAsync(_databaseService, HttpContext, new AuditEvent
                {
                    Action = "UPDATE_FAILED",
                    EntityType = "Meter",
                    EntityId = meter.Id.ToString(),
                    CompanyId = companyId,
                    Summary = "Meter update failed.",
                    Success = false
                });

                TempData["ErrorMessage"] = "Meter could not be updated. Check the application logs for details.";
            }

            return RedirectToAction(nameof(Management), new { id = meter.Id });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public Task<IActionResult> EnableMeter(int meterId) =>
            SetMeterActiveState(meterId, true);

        [HttpPost]
        [ValidateAntiForgeryToken]
        public Task<IActionResult> DisableMeter(int meterId) =>
            SetMeterActiveState(meterId, false);

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteMeter(int meterId)
        {
            var companyId = _companyContext.CurrentCompanyId;

            try
            {
                await using var connection = _databaseService.CreateNewConnection();
                await connection.OpenAsync();
                await using var tx = await connection.BeginTransactionAsync();

                var before = await GetMeterSnapshotAsync(connection, tx, meterId, companyId);
                if (before == null)
                {
                    await tx.RollbackAsync();
                    TempData["ErrorMessage"] = "Meter not found in the current workspace.";
                    return RedirectToAction(nameof(Management));
                }

                var dependencies = await GetMeterDependenciesAsync(connection, tx, meterId, companyId);
                if (!MeterLifecycleRules.CanPermanentlyDelete(dependencies))
                {
                    await tx.RollbackAsync();

                    await AuditTrail.LogAsync(_databaseService, HttpContext, new AuditEvent
                    {
                        Action = "DELETE_BLOCKED",
                        EntityType = "Meter",
                        EntityId = meterId.ToString(),
                        CompanyId = companyId,
                        Summary = BuildDeleteBlockedSummary(before.Name, dependencies),
                        Before = new { Meter = before, Dependencies = dependencies },
                        Success = false
                    });

                    TempData["ErrorMessage"] =
                        "This meter has historical data, invoice references, or child meters. Disable it instead of deleting it.";
                    return RedirectToAction(nameof(Management), new { id = meterId });
                }

                await using var delete = new NpgsqlCommand(@"
                    DELETE FROM ""Meters""
                    WHERE ""MeterId"" = @MeterId
                      AND ""CompanyId"" = @CompanyId", connection, tx);
                delete.Parameters.AddWithValue("@MeterId", meterId);
                delete.Parameters.AddWithValue("@CompanyId", companyId);

                if (await delete.ExecuteNonQueryAsync() != 1)
                    throw new InvalidOperationException("Meter delete did not affect exactly one row.");

                await tx.CommitAsync();

                await AuditTrail.LogAsync(_databaseService, HttpContext, new AuditEvent
                {
                    Action = "DELETE",
                    EntityType = "Meter",
                    EntityId = meterId.ToString(),
                    CompanyId = companyId,
                    Summary = $"Empty meter '{before.Name}' permanently deleted.",
                    Before = before
                });

                TempData["SuccessMessage"] = "Empty meter permanently deleted.";
                return RedirectToAction(nameof(Management));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to delete meter {MeterId}", meterId);

                await AuditTrail.LogAsync(_databaseService, HttpContext, new AuditEvent
                {
                    Action = "DELETE_FAILED",
                    EntityType = "Meter",
                    EntityId = meterId.ToString(),
                    CompanyId = companyId,
                    Summary = "Meter deletion failed.",
                    Success = false
                });

                TempData["ErrorMessage"] = "Meter deletion failed. Check the application logs for details.";
                return RedirectToAction(nameof(Management), new { id = meterId });
            }
        }

        public async Task<IActionResult> Readings()
        {
            if (!_databaseService.IsInitialized)
                return RedirectDatabaseNotConfigured();

            try
            {
                var meters = await _meterRepository.GetMetersAsync(
                    new MeterSearchCriteria(),
                    1,
                    5000);

                return View(new MeterReadingsViewModel
                {
                    Readings = new List<MeterReading>(),
                    AvailableMeters = meters.Select(m => new MeterOption
                    {
                        MeterId = m.Id,
                        Name = m.Name,
                        Unit = m.Unit,
                        Type = m.Type
                    }).ToList(),
                    TotalItems = meters.Count,
                    CurrentPage = 1,
                    TotalPages = 1
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unable to load meter readings page.");
                TempData["ErrorMessage"] = "Unable to load meter readings.";
                return View(new MeterReadingsViewModel());
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> BulkDeleteMeters([FromBody] List<int> meterIds)
        {
            var ids = (meterIds ?? new List<int>())
                .Where(id => id > 0)
                .Distinct()
                .ToList();

            if (ids.Count == 0)
                return Json(new { success = false, message = "No meters selected." });

            var companyId = _companyContext.CurrentCompanyId;

            try
            {
                await using var connection = _databaseService.CreateNewConnection();
                await connection.OpenAsync();
                await using var tx = await connection.BeginTransactionAsync();

                var scopedIds = await GetScopedMeterIdsAsync(connection, tx, ids, companyId);
                if (scopedIds.Count != ids.Count)
                {
                    await tx.RollbackAsync();
                    return Json(new
                    {
                        success = false,
                        message = "One or more selected meters do not belong to the current workspace."
                    });
                }

                var blocked = new List<object>();
                foreach (var id in scopedIds)
                {
                    var dependencies = await GetMeterDependenciesAsync(connection, tx, id, companyId);
                    if (dependencies.HasDependencies)
                    {
                        blocked.Add(new
                        {
                            MeterId = id,
                            Dependencies = dependencies
                        });
                    }
                }

                if (blocked.Count > 0)
                {
                    await tx.RollbackAsync();

                    await AuditTrail.LogAsync(_databaseService, HttpContext, new AuditEvent
                    {
                        Action = "DELETE_BLOCKED",
                        EntityType = "Meter",
                        CompanyId = companyId,
                        Summary = $"Permanent deletion blocked for {blocked.Count} selected meter(s). Disable them instead.",
                        Before = new { BlockedMeters = blocked.Take(25).ToList() },
                        Success = false
                    });

                    return Json(new
                    {
                        success = false,
                        message = "Permanent deletion was blocked because at least one selected meter has readings, invoice references, or child meters. Disable those meters instead."
                    });
                }

                var before = await GetMeterSnapshotsAsync(connection, tx, scopedIds, companyId);

                await using var delete = new NpgsqlCommand(@"
                    DELETE FROM ""Meters""
                    WHERE ""MeterId"" = ANY(@MeterIds)
                      AND ""CompanyId"" = @CompanyId", connection, tx);
                delete.Parameters.AddWithValue("@MeterIds", scopedIds.ToArray());
                delete.Parameters.AddWithValue("@CompanyId", companyId);

                var rows = await delete.ExecuteNonQueryAsync();
                await tx.CommitAsync();

                await AuditTrail.LogAsync(_databaseService, HttpContext, new AuditEvent
                {
                    Action = rows == 1 ? "DELETE" : "BULK_DELETE",
                    EntityType = "Meter",
                    CompanyId = companyId,
                    Summary = $"{rows} empty meter(s) permanently deleted.",
                    Before = new { Count = before.Count, Sample = before.Take(25).ToList() }
                });

                return Json(new
                {
                    success = true,
                    message = $"{rows} empty meter(s) permanently deleted."
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Bulk meter deletion failed in workspace {CompanyId}", companyId);

                await AuditTrail.LogAsync(_databaseService, HttpContext, new AuditEvent
                {
                    Action = "BULK_DELETE_FAILED",
                    EntityType = "Meter",
                    CompanyId = companyId,
                    Summary = "Bulk meter deletion failed.",
                    Success = false
                });

                return Json(new
                {
                    success = false,
                    message = "Meter deletion failed. Check the application logs for details."
                });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> BulkEditMeters([FromBody] BulkEditMetersRequest request)
        {
            if (!_databaseService.IsInitialized)
                return Json(new { success = false, message = "Database not configured." });

            if (!request.UpdateTenant &&
                !request.UpdateUnit &&
                !request.UpdateType &&
                !request.UpdateParent &&
                !request.UpdateActive)
            {
                return Json(new { success = false, message = "No fields selected for update." });
            }

            var companyId = _companyContext.CurrentCompanyId;

            try
            {
                List<int> idsToUpdate;
                if (request.SelectAllMatching)
                {
                    idsToUpdate = await _meterRepository.GetMeterIdsAsync(new MeterSearchCriteria
                    {
                        SearchField = NormalizeSearchField(request.SearchField),
                        SearchTerm = request.SearchTerm,
                        StatusFilter = MeterLifecycleRules.NormalizeStatus(request.StatusFilter),
                        AssignmentFilter = MeterLifecycleRules.NormalizeAssignment(request.AssignmentFilter)
                    });
                }
                else
                {
                    idsToUpdate = (request.MeterIds ?? new List<int>())
                        .Where(id => id > 0)
                        .Distinct()
                        .ToList();
                }

                if (idsToUpdate.Count == 0)
                    return Json(new { success = false, message = "No meters matched the selection." });

                var normalizedType = request.UpdateType
                    ? MeterLifecycleRules.NormalizeType(request.Type)
                    : null;

                if (request.UpdateType &&
                    !string.Equals(request.Type, "main", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(request.Type, "sub", StringComparison.OrdinalIgnoreCase))
                {
                    return Json(new { success = false, message = "Meter type must be Main or Sub." });
                }

                if (request.UpdateUnit && (request.Unit?.Length ?? 0) > 20)
                    return Json(new { success = false, message = "Meter unit cannot exceed 20 characters." });

                await using var connection = _databaseService.CreateNewConnection();
                await connection.OpenAsync();
                await using var tx = await connection.BeginTransactionAsync();

                var scopedIds = await GetScopedMeterIdsAsync(connection, tx, idsToUpdate, companyId);
                if (scopedIds.Count != idsToUpdate.Count)
                {
                    await tx.RollbackAsync();
                    return Json(new
                    {
                        success = false,
                        message = "One or more selected meters do not belong to the current workspace."
                    });
                }

                if (request.UpdateTenant &&
                    !await ValidateTenantAssignmentAsync(connection, tx, request.TenantId))
                {
                    await tx.RollbackAsync();
                    return Json(new
                    {
                        success = false,
                        message = "The selected tenant is disabled or does not belong to the current workspace."
                    });
                }

                if (request.UpdateParent && request.ParentId.HasValue)
                {
                    if (scopedIds.Contains(request.ParentId.Value))
                    {
                        await tx.RollbackAsync();
                        return Json(new
                        {
                            success = false,
                            message = "A selected meter cannot also be the parent used for that same bulk operation."
                        });
                    }

                    foreach (var meterId in scopedIds)
                    {
                        var parentError = await ValidateParentAssignmentAsync(
                            connection,
                            tx,
                            meterId,
                            request.ParentId);

                        if (parentError != null)
                        {
                            await tx.RollbackAsync();
                            return Json(new { success = false, message = parentError });
                        }
                    }
                }

                if (request.UpdateType &&
                    normalizedType == "main" &&
                    request.UpdateParent &&
                    request.ParentId.HasValue)
                {
                    await tx.RollbackAsync();
                    return Json(new
                    {
                        success = false,
                        message = "Main meters cannot be assigned to a parent meter."
                    });
                }

                var before = await GetMeterSnapshotsAsync(connection, tx, scopedIds, companyId);
                var setClauses = new List<string>();

                if (request.UpdateTenant)
                    setClauses.Add(@"""TenantID"" = @TenantId");

                if (request.UpdateUnit)
                    setClauses.Add(@"""Unit"" = @Unit");

                if (request.UpdateType)
                {
                    setClauses.Add(@"""Type"" = @Type");
                    if (normalizedType == "main" && !request.UpdateParent)
                        setClauses.Add(@"""ParentId"" = NULL");
                }

                if (request.UpdateParent)
                    setClauses.Add(@"""ParentId"" = @ParentId");

                if (request.UpdateActive)
                    setClauses.Add(@"""Active"" = @Active");

                var sql = $@"
                    UPDATE ""Meters""
                    SET {string.Join(", ", setClauses)}
                    WHERE ""MeterId"" = ANY(@MeterIds)
                      AND ""CompanyId"" = @CompanyId";

                await using var cmd = new NpgsqlCommand(sql, connection, tx);

                if (request.UpdateTenant)
                    cmd.Parameters.AddWithValue("@TenantId", (object?)request.TenantId ?? DBNull.Value);

                if (request.UpdateUnit)
                    cmd.Parameters.AddWithValue("@Unit", request.Unit?.Trim() ?? "");

                if (request.UpdateType)
                    cmd.Parameters.AddWithValue("@Type", normalizedType!);

                if (request.UpdateParent)
                    cmd.Parameters.AddWithValue("@ParentId", (object?)request.ParentId ?? DBNull.Value);

                if (request.UpdateActive)
                    cmd.Parameters.AddWithValue("@Active", request.Active);

                cmd.Parameters.AddWithValue("@MeterIds", scopedIds.ToArray());
                cmd.Parameters.AddWithValue("@CompanyId", companyId);

                var rows = await cmd.ExecuteNonQueryAsync();
                var after = await GetMeterSnapshotsAsync(connection, tx, scopedIds, companyId);
                await tx.CommitAsync();

                var action = DescribeBulkAction(request);
                var tenantName = request.UpdateTenant && request.TenantId.HasValue
                    ? await GetTenantNameAsync(request.TenantId.Value, companyId)
                    : null;

                var summary = BuildBulkSummary(action, rows, tenantName);

                await AuditTrail.LogAsync(_databaseService, HttpContext, new AuditEvent
                {
                    Action = action,
                    EntityType = "Meter",
                    CompanyId = companyId,
                    Summary = summary,
                    Before = new { Count = before.Count, Sample = before.Take(25).ToList() },
                    After = new { Count = after.Count, Sample = after.Take(25).ToList() }
                });

                return Json(new { success = true, message = summary });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Bulk meter update failed in workspace {CompanyId}", companyId);

                await AuditTrail.LogAsync(_databaseService, HttpContext, new AuditEvent
                {
                    Action = "BULK_UPDATE_FAILED",
                    EntityType = "Meter",
                    CompanyId = companyId,
                    Summary = "Bulk meter update failed.",
                    Success = false
                });

                return Json(new
                {
                    success = false,
                    message = "Meter update failed. Check the application logs for details."
                });
            }
        }

        private async Task<MeterManagementViewModel> BuildManagementModelAsync(
            MeterSearchCriteria criteria,
            int? selectedId,
            int page,
            int pageSize)
        {
            var model = new MeterManagementViewModel
            {
                SearchCriteria = criteria,
                CurrentPage = page,
                PageSize = pageSize,
                TenantOptions = await GetTenantOptionsAsync(),
                Summary = await _meterRepository.GetSummaryAsync()
            };

            model.TotalItems = await _meterRepository.GetTotalMetersCountAsync(criteria);
            model.TotalPages = Math.Max(1, (int)Math.Ceiling(model.TotalItems / (double)pageSize));

            if (model.CurrentPage > model.TotalPages)
                model.CurrentPage = model.TotalPages;

            model.SearchResults = await _meterRepository.GetMetersAsync(
                criteria,
                model.CurrentPage,
                pageSize);

            var meterId = selectedId;
            if (!meterId.HasValue && model.SearchResults.Count > 0)
                meterId = model.SearchResults[0].Id;

            if (meterId.HasValue)
            {
                model.SelectedMeter = await _meterRepository.GetMeterByIdAsync(meterId.Value);
                if (model.SelectedMeter == null)
                {
                    TempData["ErrorMessage"] = "Meter not found in the current workspace.";
                }
                else
                {
                    model.SubMeters = await _meterRepository.GetSubMetersAsync(meterId.Value);
                    model.ParentMeterOptions = BuildParentOptions(
                        await _meterRepository.GetParentMetersAsync());

                    await using var connection = _databaseService.CreateNewConnection();
                    await connection.OpenAsync();
                    await using var tx = await connection.BeginTransactionAsync();
                    model.SelectedMeterDependencies = await GetMeterDependenciesAsync(
                        connection,
                        tx,
                        meterId.Value,
                        _companyContext.CurrentCompanyId);
                    await tx.CommitAsync();
                }
            }
            else
            {
                model.ParentMeterOptions = BuildParentOptions(
                    await _meterRepository.GetParentMetersAsync());
            }

            return model;
        }

        private async Task<IActionResult> SetMeterActiveState(int meterId, bool active)
        {
            var companyId = _companyContext.CurrentCompanyId;

            try
            {
                await using var connection = _databaseService.CreateNewConnection();
                await connection.OpenAsync();
                await using var tx = await connection.BeginTransactionAsync();

                var before = await GetMeterSnapshotAsync(connection, tx, meterId, companyId);
                if (before == null)
                {
                    await tx.RollbackAsync();
                    TempData["ErrorMessage"] = "Meter not found in the current workspace.";
                    return RedirectToAction(nameof(Management));
                }

                await using var cmd = new NpgsqlCommand(@"
                    UPDATE ""Meters""
                    SET ""Active"" = @Active
                    WHERE ""MeterId"" = @MeterId
                      AND ""CompanyId"" = @CompanyId", connection, tx);
                cmd.Parameters.AddWithValue("@Active", active);
                cmd.Parameters.AddWithValue("@MeterId", meterId);
                cmd.Parameters.AddWithValue("@CompanyId", companyId);

                if (await cmd.ExecuteNonQueryAsync() != 1)
                    throw new InvalidOperationException("Meter state update did not affect exactly one row.");

                var after = await GetMeterSnapshotAsync(connection, tx, meterId, companyId);
                await tx.CommitAsync();

                await AuditTrail.LogAsync(_databaseService, HttpContext, new AuditEvent
                {
                    Action = active ? "ENABLE" : "DISABLE",
                    EntityType = "Meter",
                    EntityId = meterId.ToString(),
                    CompanyId = companyId,
                    Summary = active
                        ? $"Meter '{before.Name}' enabled."
                        : $"Meter '{before.Name}' disabled. Historical readings were preserved.",
                    Before = before,
                    After = after
                });

                TempData["SuccessMessage"] = active ? "Meter enabled." : "Meter disabled.";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to change meter state for {MeterId}", meterId);
                TempData["ErrorMessage"] = "Meter state could not be changed.";
            }

            return RedirectToAction(nameof(Management), new { id = meterId });
        }

        private async Task<List<SelectListItem>> GetTenantOptionsAsync()
        {
            var options = new List<SelectListItem>
            {
                new() { Value = "", Text = "Unassigned" }
            };

            await using var connection = _databaseService.CreateNewConnection();
            await connection.OpenAsync();

            const string sql = @"
                SELECT
                    t.""TenantID"",
                    COALESCE(NULLIF(td.""CompanyName"", ''), t.""DisplayName""),
                    COALESCE(td.""Active"", TRUE)
                FROM ""Tenants"" t
                LEFT JOIN ""TenantDetails"" td
                  ON td.""TenantID"" = t.""TenantID""
                 AND td.""CompanyId"" = t.""CompanyId""
                WHERE t.""CompanyId"" = @CompanyId
                ORDER BY COALESCE(NULLIF(td.""CompanyName"", ''), t.""DisplayName"")";

            await using var cmd = new NpgsqlCommand(sql, connection);
            cmd.Parameters.AddWithValue("@CompanyId", _companyContext.CurrentCompanyId);

            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var active = reader.GetBoolean(2);
                options.Add(new SelectListItem
                {
                    Value = reader.GetInt32(0).ToString(),
                    Text = active ? reader.GetString(1) : $"{reader.GetString(1)} (disabled)",
                    Disabled = !active
                });
            }

            return options;
        }

        private static List<SelectListItem> BuildParentOptions(IEnumerable<Meter> meters)
        {
            var options = new List<SelectListItem>
            {
                new() { Value = "", Text = "None (top level)" }
            };

            options.AddRange(meters.Select(m => new SelectListItem
            {
                Value = m.Id.ToString(),
                Text = string.IsNullOrWhiteSpace(m.Label)
                    ? m.Name
                    : $"{m.Label} ({m.Name})",
                Disabled = !m.Active
            }));

            return options;
        }

        private static void AddMeterConfigurationParameters(
            NpgsqlCommand cmd,
            Meter meter,
            string normalizedType,
            int? parentId,
            int? tenantId,
            int companyId)
        {
            cmd.Parameters.AddWithValue("@Name", meter.Name.Trim());
            cmd.Parameters.AddWithValue("@Label", (object?)meter.Label?.Trim() ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Unit", meter.Unit?.Trim() ?? "");
            cmd.Parameters.AddWithValue("@ParentId", (object?)parentId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Type", normalizedType);
            cmd.Parameters.AddWithValue("@Active", meter.Active);
            cmd.Parameters.AddWithValue("@TenantId", (object?)tenantId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@CompanyId", companyId);
        }

        private async Task<bool> ValidateTenantAssignmentAsync(
            NpgsqlConnection connection,
            NpgsqlTransaction tx,
            int? tenantId,
            int? existingTenantId = null)
        {
            if (!tenantId.HasValue)
                return true;

            const string sql = @"
                SELECT COALESCE(td.""Active"", TRUE)
                FROM ""Tenants"" t
                LEFT JOIN ""TenantDetails"" td
                  ON td.""TenantID"" = t.""TenantID""
                 AND td.""CompanyId"" = t.""CompanyId""
                WHERE t.""TenantID"" = @TenantId
                  AND t.""CompanyId"" = @CompanyId";

            await using var cmd = new NpgsqlCommand(sql, connection, tx);
            cmd.Parameters.AddWithValue("@TenantId", tenantId.Value);
            cmd.Parameters.AddWithValue("@CompanyId", _companyContext.CurrentCompanyId);

            var result = await cmd.ExecuteScalarAsync();
            if (result is not bool active)
                return false;

            return active || existingTenantId == tenantId;
        }

        private async Task<string?> ValidateParentAssignmentAsync(
            NpgsqlConnection connection,
            NpgsqlTransaction tx,
            int? meterId,
            int? parentId)
        {
            if (!parentId.HasValue)
                return null;

            if (meterId.HasValue && meterId.Value == parentId.Value)
                return "A meter cannot be its own parent.";

            const string parentSql = @"
                SELECT LOWER(""Type"")
                FROM ""Meters""
                WHERE ""MeterId"" = @ParentId
                  AND ""CompanyId"" = @CompanyId";

            await using (var parent = new NpgsqlCommand(parentSql, connection, tx))
            {
                parent.Parameters.AddWithValue("@ParentId", parentId.Value);
                parent.Parameters.AddWithValue("@CompanyId", _companyContext.CurrentCompanyId);
                var result = await parent.ExecuteScalarAsync();

                if (result == null)
                    return "The selected parent meter does not belong to the current workspace.";

                if (!string.Equals(Convert.ToString(result), "main", StringComparison.OrdinalIgnoreCase))
                    return "Only a Main meter can be used as a parent.";
            }

            if (!meterId.HasValue)
                return null;

            const string cycleSql = @"
                WITH RECURSIVE ancestors AS (
                    SELECT ""MeterId"", ""ParentId""
                    FROM ""Meters""
                    WHERE ""MeterId"" = @ParentId
                      AND ""CompanyId"" = @CompanyId

                    UNION ALL

                    SELECT m.""MeterId"", m.""ParentId""
                    FROM ""Meters"" m
                    INNER JOIN ancestors a ON m.""MeterId"" = a.""ParentId""
                    WHERE m.""CompanyId"" = @CompanyId
                )
                SELECT COUNT(*)
                FROM ancestors
                WHERE ""MeterId"" = @MeterId";

            await using var cycle = new NpgsqlCommand(cycleSql, connection, tx);
            cycle.Parameters.AddWithValue("@ParentId", parentId.Value);
            cycle.Parameters.AddWithValue("@MeterId", meterId.Value);
            cycle.Parameters.AddWithValue("@CompanyId", _companyContext.CurrentCompanyId);

            return Convert.ToInt32(await cycle.ExecuteScalarAsync()) > 0
                ? "This parent assignment would create a meter hierarchy cycle."
                : null;
        }

        private static async Task<List<int>> GetScopedMeterIdsAsync(
            NpgsqlConnection connection,
            NpgsqlTransaction tx,
            IReadOnlyCollection<int> ids,
            int companyId)
        {
            var result = new List<int>();
            await using var cmd = new NpgsqlCommand(@"
                SELECT ""MeterId""
                FROM ""Meters""
                WHERE ""MeterId"" = ANY(@MeterIds)
                  AND ""CompanyId"" = @CompanyId
                ORDER BY ""MeterId""", connection, tx);
            cmd.Parameters.AddWithValue("@MeterIds", ids.ToArray());
            cmd.Parameters.AddWithValue("@CompanyId", companyId);

            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                result.Add(reader.GetInt32(0));

            return result;
        }

        private static async Task<MeterDependencySummary> GetMeterDependenciesAsync(
            NpgsqlConnection connection,
            NpgsqlTransaction tx,
            int meterId,
            int companyId)
        {
            const string sql = @"
                SELECT
                    (SELECT COUNT(*) FROM ""MeterReadings""
                     WHERE ""MeterId"" = @MeterId AND ""CompanyId"" = @CompanyId),
                    (SELECT COUNT(*) FROM ""MeterReadingsDaily""
                     WHERE ""MeterId"" = @MeterId AND ""CompanyId"" = @CompanyId),
                    (SELECT COUNT(*) FROM ""MeterReadingsMonthly""
                     WHERE ""MeterId"" = @MeterId AND ""CompanyId"" = @CompanyId),
                    (SELECT COUNT(*) FROM ""MeterReadingsYearly""
                     WHERE ""MeterId"" = @MeterId AND ""CompanyId"" = @CompanyId),
                    (SELECT COUNT(*) FROM ""BillLineItems""
                     WHERE ""MeterId"" = @MeterId),
                    (SELECT COUNT(*) FROM ""Meters""
                     WHERE ""ParentId"" = @MeterId AND ""CompanyId"" = @CompanyId)";

            await using var cmd = new NpgsqlCommand(sql, connection, tx);
            cmd.Parameters.AddWithValue("@MeterId", meterId);
            cmd.Parameters.AddWithValue("@CompanyId", companyId);

            await using var reader = await cmd.ExecuteReaderAsync();
            await reader.ReadAsync();

            return new MeterDependencySummary
            {
                RawReadingCount = reader.GetInt64(0),
                DailyReadingCount = reader.GetInt64(1),
                MonthlyReadingCount = reader.GetInt64(2),
                YearlyReadingCount = reader.GetInt64(3),
                BillLineCount = reader.GetInt64(4),
                ChildMeterCount = reader.GetInt64(5)
            };
        }

        private static async Task<MeterAuditSnapshot?> GetMeterSnapshotAsync(
            NpgsqlConnection connection,
            NpgsqlTransaction tx,
            int meterId,
            int companyId)
        {
            const string sql = @"
                SELECT
                    m.""MeterId"", m.""Name"", m.""Label"", m.""Unit"",
                    m.""ParentId"", m.""Type"", m.""Active"", m.""TenantID"",
                    t.""DisplayName""
                FROM ""Meters"" m
                LEFT JOIN ""Tenants"" t
                  ON t.""TenantID"" = m.""TenantID""
                 AND t.""CompanyId"" = m.""CompanyId""
                WHERE m.""MeterId"" = @MeterId
                  AND m.""CompanyId"" = @CompanyId";

            await using var cmd = new NpgsqlCommand(sql, connection, tx);
            cmd.Parameters.AddWithValue("@MeterId", meterId);
            cmd.Parameters.AddWithValue("@CompanyId", companyId);

            await using var reader = await cmd.ExecuteReaderAsync();
            if (!await reader.ReadAsync())
                return null;

            return ReadAuditSnapshot(reader);
        }

        private static async Task<List<MeterAuditSnapshot>> GetMeterSnapshotsAsync(
            NpgsqlConnection connection,
            NpgsqlTransaction tx,
            IReadOnlyCollection<int> meterIds,
            int companyId)
        {
            var result = new List<MeterAuditSnapshot>();
            await using var cmd = new NpgsqlCommand(@"
                SELECT
                    m.""MeterId"", m.""Name"", m.""Label"", m.""Unit"",
                    m.""ParentId"", m.""Type"", m.""Active"", m.""TenantID"",
                    t.""DisplayName""
                FROM ""Meters"" m
                LEFT JOIN ""Tenants"" t
                  ON t.""TenantID"" = m.""TenantID""
                 AND t.""CompanyId"" = m.""CompanyId""
                WHERE m.""MeterId"" = ANY(@MeterIds)
                  AND m.""CompanyId"" = @CompanyId
                ORDER BY m.""MeterId""", connection, tx);
            cmd.Parameters.AddWithValue("@MeterIds", meterIds.ToArray());
            cmd.Parameters.AddWithValue("@CompanyId", companyId);

            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                result.Add(ReadAuditSnapshot(reader));

            return result;
        }

        private static MeterAuditSnapshot ReadAuditSnapshot(NpgsqlDataReader reader) => new()
        {
            Id = reader.GetInt32(0),
            Name = reader.GetString(1),
            Label = reader.IsDBNull(2) ? null : reader.GetString(2),
            Unit = reader.IsDBNull(3) ? "" : reader.GetString(3),
            ParentId = reader.IsDBNull(4) ? null : reader.GetInt32(4),
            Type = reader.GetString(5),
            Active = reader.GetBoolean(6),
            TenantId = reader.IsDBNull(7) ? null : reader.GetInt32(7),
            TenantName = reader.IsDBNull(8) ? null : reader.GetString(8)
        };

        private async Task<string?> GetTenantNameAsync(int tenantId, int companyId)
        {
            await using var connection = _databaseService.CreateNewConnection();
            await connection.OpenAsync();

            await using var cmd = new NpgsqlCommand(@"
                SELECT COALESCE(NULLIF(td.""CompanyName"", ''), t.""DisplayName"")
                FROM ""Tenants"" t
                LEFT JOIN ""TenantDetails"" td
                  ON td.""TenantID"" = t.""TenantID""
                 AND td.""CompanyId"" = t.""CompanyId""
                WHERE t.""TenantID"" = @TenantId
                  AND t.""CompanyId"" = @CompanyId", connection);
            cmd.Parameters.AddWithValue("@TenantId", tenantId);
            cmd.Parameters.AddWithValue("@CompanyId", companyId);

            return Convert.ToString(await cmd.ExecuteScalarAsync());
        }

        private static (string Action, string Summary) DescribeSingleMeterChange(
            MeterAuditSnapshot before,
            MeterAuditSnapshot after)
        {
            var changed = new List<string>();

            if (!string.Equals(before.Name, after.Name, StringComparison.Ordinal))
                changed.Add("name");
            if (!string.Equals(before.Label, after.Label, StringComparison.Ordinal))
                changed.Add("label");
            if (!string.Equals(before.Unit, after.Unit, StringComparison.Ordinal))
                changed.Add("unit");
            if (before.ParentId != after.ParentId)
                changed.Add("parent");
            if (!string.Equals(before.Type, after.Type, StringComparison.OrdinalIgnoreCase))
                changed.Add("type");
            if (before.Active != after.Active)
                changed.Add("active");
            if (before.TenantId != after.TenantId)
                changed.Add("tenant");

            if (changed.Count == 1 && changed[0] == "tenant")
            {
                return after.TenantId.HasValue
                    ? ("ASSIGN", $"Meter '{after.Name}' assigned to tenant '{after.TenantName ?? after.TenantId.ToString()}'.")
                    : ("UNASSIGN", $"Meter '{after.Name}' unassigned from tenant.");
            }

            if (changed.Count == 1 && changed[0] == "active")
            {
                return after.Active
                    ? ("ENABLE", $"Meter '{after.Name}' enabled.")
                    : ("DISABLE", $"Meter '{after.Name}' disabled. Historical readings were preserved.");
            }

            var fieldSummary = changed.Count == 0 ? "no configuration fields" : string.Join(", ", changed);
            return ("UPDATE", $"Meter '{after.Name}' updated ({fieldSummary}).");
        }

        private static string DescribeBulkAction(BulkEditMetersRequest request)
        {
            var selectedOperations =
                (request.UpdateTenant ? 1 : 0) +
                (request.UpdateUnit ? 1 : 0) +
                (request.UpdateType ? 1 : 0) +
                (request.UpdateParent ? 1 : 0) +
                (request.UpdateActive ? 1 : 0);

            if (selectedOperations == 1 && request.UpdateTenant)
                return request.TenantId.HasValue ? "BULK_ASSIGN" : "BULK_UNASSIGN";

            if (selectedOperations == 1 && request.UpdateActive)
                return request.Active ? "BULK_ENABLE" : "BULK_DISABLE";

            return "BULK_UPDATE";
        }

        private static string BuildBulkSummary(string action, int rows, string? tenantName)
        {
            return action switch
            {
                "BULK_ASSIGN" => $"{rows} meter(s) assigned to tenant '{tenantName ?? "selected tenant"}'.",
                "BULK_UNASSIGN" => $"{rows} meter(s) unassigned from tenant.",
                "BULK_ENABLE" => $"{rows} meter(s) enabled.",
                "BULK_DISABLE" => $"{rows} meter(s) disabled. Historical readings were preserved.",
                _ => $"{rows} meter(s) updated in bulk."
            };
        }

        private static string BuildDeleteBlockedSummary(
            string meterName,
            MeterDependencySummary dependencies)
        {
            return $"Meter '{meterName}' cannot be permanently deleted: " +
                   $"{dependencies.HistoricalReadingCount} historical/aggregate reading row(s), " +
                   $"{dependencies.BillLineCount} invoice line reference(s), " +
                   $"{dependencies.ChildMeterCount} child meter(s).";
        }

        private static int? ParseOptionalId(
            string? value,
            string fieldName,
            ICollection<string> errors)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            if (int.TryParse(value, out var id) && id > 0)
                return id;

            errors.Add($"{fieldName} is invalid.");
            return null;
        }

        private static string NormalizeSearchField(string? field) =>
            field is "Type" or "Tenant" ? field : "Name";

        private IActionResult RedirectDatabaseNotConfigured()
        {
            TempData["ErrorMessage"] = "Database not configured. Please set up database first.";
            return RedirectToAction("General", "Settings");
        }

        private sealed class MeterAuditSnapshot
        {
            public int Id { get; init; }
            public string Name { get; init; } = "";
            public string? Label { get; init; }
            public string Unit { get; init; } = "";
            public int? ParentId { get; init; }
            public string Type { get; init; } = "";
            public bool Active { get; init; }
            public int? TenantId { get; init; }
            public string? TenantName { get; init; }
        }
    }
}
