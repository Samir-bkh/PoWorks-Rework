/**
 * PCVue Web Services variable import.
 *
 * Design goals:
 * - browsing and import are separate operations;
 * - existing PoWorks meters are visible and safely updateable;
 * - engineering units can be read from PCVue or assigned in bulk;
 * - System variables remain excluded unless explicitly enabled;
 * - no ambiguous confirm-based update workflow.
 */

const PCVUE_UNIT_PRESETS = [
    'Wh', 'kWh', 'MWh', 'W', 'kW', 'MW',
    '°C', '°F', 'bar', 'Pa', 'kPa', 'm³', 'm³/h', 'L', 'L/min', '%'
];

document.addEventListener('DOMContentLoaded', () => {
    loadWebServiceConnections();
    setupWebServiceEventListeners();
    setupWebServiceDateRange();
});

function setupWebServiceEventListeners() {
    const connectionSelect = document.getElementById('webServiceConnection');
    connectionSelect?.addEventListener('change', () => {
        const browseButton = document.getElementById('browseVariablesBtn');
        const status = document.getElementById('webServiceConnectionStatus');
        const hasConnection = Boolean(connectionSelect.value);

        if (browseButton) browseButton.disabled = !hasConnection;
        if (status) {
            status.innerHTML = hasConnection
                ? '<i class="bi bi-check-circle text-success"></i> Connection selected - ready to browse variables'
                : '<i class="bi bi-info-circle"></i> Select a web service connection';
        }
    });

    document.getElementById('browseVariablesBtn')?.addEventListener('click', () => {
        const connectionId = connectionSelect?.value || '';
        if (!connectionId) {
            showWebServiceStatus('warning', 'Select a Web Service connection first.');
            return;
        }
        browseVariables(connectionId);
    });

    document.getElementById('maxVariables')?.addEventListener('input', event => {
        const input = event.target;
        const value = Number.parseInt(input.value, 10);
        if (!Number.isFinite(value) || value < 1) input.value = '1';
        if (value > 1000000) input.value = '1000000';
    });

    document.body.addEventListener('click', event => {
        const button = event.target.closest('[data-ws-action]');
        if (!button) return;

        switch (button.dataset.wsAction) {
            case 'fill-blank-units':
                applyBulkUnit(false);
                break;
            case 'apply-unit':
                applyBulkUnit(true);
                break;
            case 'read-units':
                resolveUnitsFromPcVue(false);
                break;
        }
    });
}

async function loadWebServiceConnections() {
    try {
        const response = await fetch('/WebServicesImport/GetWebServiceConnections');
        const data = await response.json();
        const select = document.getElementById('webServiceConnection');
        if (!select) return;

        select.innerHTML = '<option value="">Select a connection...</option>';
        if (!data.success || !Array.isArray(data.connections)) {
            showWebServiceStatus('danger', 'Unable to load Web Service connections.');
            return;
        }

        data.connections.forEach(connection => {
            const option = document.createElement('option');
            option.value = connection.connectionId;
            option.textContent = `${connection.connectionName || 'PCVue'} (${connection.baseUrl || ''})${connection.isDefault ? ' - Default' : ''}`;
            option.dataset.connectionName = connection.connectionName || 'PCVue';
            select.appendChild(option);
        });

        const defaultConnection = data.connections.find(connection => connection.isDefault);
        if (defaultConnection) {
            select.value = defaultConnection.connectionId;
            select.dispatchEvent(new Event('change'));
        }
    } catch (error) {
        showWebServiceStatus('danger', `Unable to load Web Service connections: ${error.message}`);
    }
}

async function browseVariables(connectionId) {
    const button = document.getElementById('browseVariablesBtn');
    const maxVariables = Number.parseInt(document.getElementById('maxVariables')?.value || '100000', 10);
    const branchFilter = document.getElementById('branchFilter')?.value.trim() || '';
    const includeSystemVariables = Boolean(document.getElementById('includeSystemVariables')?.checked);
    const option = document.querySelector(`#webServiceConnection option[value="${cssEscape(connectionId)}"]`);

    storeWebServiceConnectionInfo({
        connectionId,
        connectionName: option?.dataset.connectionName || option?.textContent || connectionId
    });

    try {
        if (button) {
            button.disabled = true;
            button.innerHTML = '<span class="spinner-border spinner-border-sm me-1"></span> Browsing...';
        }

        showWebServiceStatus(
            'info',
            includeSystemVariables
                ? 'Browsing PCVue variables, including System variables...'
                : 'Browsing PCVue variables. System variables are excluded.'
        );

        const response = await fetch('/WebServicesImport/BrowseVariablesWebService', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({
                connectionId,
                maxVariables: Number.isFinite(maxVariables) ? maxVariables : 100000,
                branchFilter,
                variableType: 'Any',
                depth: 0,
                includeSystemVariables
            })
        });

        const data = await response.json();
        if (!response.ok || !data.success) {
            throw new Error(data.message || data.error || `HTTP ${response.status}`);
        }

        const variables = Array.isArray(data.variables) ? data.variables : [];
        const filteredCount = Number(data.filteredSystemVariables || 0);
        showWebServiceStatus(
            variables.length ? 'success' : 'warning',
            `${variables.length} variable(s) available for import` +
            (!includeSystemVariables && filteredCount > 0
                ? ` · ${filteredCount} System variable(s) filtered`
                : '') + '.'
        );

        storeWebServiceConnectionInfo(data.connectionInfo || getStoredWebServiceConnectionInfo());
        showWebServiceMeterSelection(variables, data.parentOptions || [], data.connectionInfo || {});

        if (variables.length > 0) {
            // Browse does not expose the Unit property. Resolve missing units through
            // one batched BulkRead after the table is visible.
            await resolveUnitsFromPcVue(true);
        }
    } catch (error) {
        showWebServiceStatus('danger', `PCVue browse failed: ${error.message}`);
    } finally {
        if (button) {
            button.disabled = false;
            button.innerHTML = '<i class="bi bi-search"></i> Browse Variables';
        }
    }
}

function showWebServiceStatus(type, message) {
    const target = document.getElementById('webServiceStatus');
    if (!target) return;
    target.className = `alert alert-${type} mt-3`;
    target.textContent = message;
    target.style.display = 'block';
}

window.showWebServiceMeterSelection = function (variables, parentOptions, connectionInfo) {
    const section = document.getElementById('meterSelectionSection');
    if (!section) return;

    storeWebServiceConnectionInfo(connectionInfo);
    window.currentMeterDataType = 'WebService';
    window.currentWebServiceContext = connectionInfo;
    window.currentWebServiceVariables = Array.isArray(variables) ? variables : [];

    section.classList.remove('d-none');
    const oldContainer = section.querySelector('.web-service-meter-selection-container, .table-responsive');
    if (oldContainer) oldContainer.outerHTML = createWebServiceMeterSelectionTable(variables, parentOptions, connectionInfo);

    const header = section.querySelector('.card-header h5');
    if (header) header.innerHTML = '<i class="bi bi-cloud"></i> PCVue variables';

    const importReadings = document.getElementById('importReadings')?.closest('.form-check');
    if (importReadings) importReadings.style.display = 'none';

    if (typeof updatePrintButtonForDataType === 'function') updatePrintButtonForDataType('WebService');
    if (typeof updateMeterCounter === 'function') updateMeterCounter();
};

function createWebServiceMeterSelectionTable(variables, parentOptions, connectionInfo) {
    const rows = Array.isArray(variables) ? variables : [];
    if (rows.length === 0) {
        return '<div class="web-service-meter-selection-container"><div class="alert alert-warning mb-0">No variables found.</div></div>';
    }

    const existingCount = rows.filter(variable => variable.existingMeterId != null).length;
    const unitOptions = PCVUE_UNIT_PRESETS.map(unit => `<option value="${escapeHtml(unit)}"></option>`).join('');

    const tableRows = rows.map((variable, index) => {
        const fullPath = normalizeVariablePath(variable.fullPath || variable.variableName || '');
        const existing = variable.existingMeterId != null;
        const unit = variable.existingUnit || '';
        const meterType = normalizeMeterType(variable.existingType || 'main');
        const active = existing ? variable.existingActive !== false : true;
        const parentId = variable.existingParentId == null ? '' : String(variable.existingParentId);
        const pcVueType = variable.variableType || 'Unknown';
        const badge = existing
            ? '<span class="badge bg-light text-dark border ms-2">Existing</span>'
            : '<span class="badge bg-success-subtle text-success-emphasis border border-success-subtle ms-2">New</span>';

        return `
            <tr class="web-service-variable-row" data-variable-index="${index}" data-variable-name="${escapeAttribute(fullPath)}" data-is-system="${variable.isSystemVariable ? 'true' : 'false'}">
                <td><input type="checkbox" class="form-check-input meter-checkbox web-service-variable-checkbox" checked data-variable-index="${index}"></td>
                <td><span class="badge bg-secondary">${escapeHtml(shortType(pcVueType))}</span></td>
                <td class="text-break"><small class="fw-semibold">${escapeHtml(fullPath)}</small>${badge}</td>
                <td>
                    <input type="text" class="form-control form-control-sm web-service-unit-input"
                           value="${escapeAttribute(unit)}" list="pcvueUnitPresets"
                           placeholder="Unit" data-variable-index="${index}">
                </td>
                <td>
                    <select class="form-select form-select-sm web-service-type-select" data-variable-index="${index}">
                        <option value="main" ${meterType === 'main' ? 'selected' : ''}>Main</option>
                        <option value="sub" ${meterType === 'sub' ? 'selected' : ''}>Sub</option>
                    </select>
                </td>
                <td>${createParentSelect(index, parentOptions, parentId, variable.existingMeterId)}</td>
                <td class="text-center"><input type="checkbox" class="form-check-input web-service-active-checkbox" ${active ? 'checked' : ''} data-variable-index="${index}"></td>
                <td><small class="text-muted">${escapeHtml(pcVueType)}</small></td>
                <td><small class="text-muted">${variable.isReadOnly ? 'Yes' : 'No'}</small></td>
            </tr>`;
    }).join('');

    return `
        <div class="web-service-meter-selection-container">
            <datalist id="pcvueUnitPresets">${unitOptions}</datalist>
            <div class="border rounded bg-light p-2 mb-2">
                <div class="row g-2 align-items-end">
                    <div class="col-lg-4 col-md-6">
                        <label class="form-label small fw-semibold mb-1">Bulk unit</label>
                        <div class="input-group input-group-sm">
                            <input id="webServiceBulkUnit" class="form-control" list="pcvueUnitPresets" placeholder="e.g. kWh, °C, bar">
                            <button class="btn btn-outline-secondary" type="button" data-ws-action="fill-blank-units" title="Fill only selected variables that have no unit">Fill blanks</button>
                            <button class="btn btn-outline-primary" type="button" data-ws-action="apply-unit" title="Apply to every selected variable">Apply selected</button>
                        </div>
                    </div>
                    <div class="col-lg-3 col-md-6">
                        <label class="form-label small fw-semibold mb-1">Existing meters</label>
                        <select id="webServiceExistingMode" class="form-select form-select-sm">
                            <option value="update" selected>Update metadata</option>
                            <option value="skip">Keep existing unchanged</option>
                        </select>
                    </div>
                    <div class="col-lg-3 col-md-6">
                        <button type="button" class="btn btn-outline-secondary btn-sm w-100" data-ws-action="read-units">
                            <i class="bi bi-arrow-repeat me-1"></i>Read missing units from PCVue
                        </button>
                    </div>
                    <div class="col-lg-2 col-md-6 text-lg-end">
                        <small class="text-muted d-block">${rows.length} variables</small>
                        <small class="text-muted d-block">${existingCount} already in PoWorks</small>
                    </div>
                </div>
                <div class="form-text mt-1">Empty incoming units never erase an existing meter unit. Bulk actions affect selected rows only.</div>
            </div>
            <div class="table-responsive" style="max-height: 500px; overflow-y: auto;">
                <table class="table table-sm table-hover align-middle mb-0">
                    <thead class="table-light sticky-top">
                        <tr>
                            <th>Import</th><th>Type</th><th>Variable Name</th><th style="min-width:130px">Unit</th>
                            <th>Meter Type</th><th style="min-width:180px">Parent Meter</th><th>Active</th><th>PCVue Type</th><th>Read Only</th>
                        </tr>
                    </thead>
                    <tbody id="metersTableBody">${tableRows}</tbody>
                </table>
            </div>
        </div>`;
}

function createParentSelect(index, parentOptions, selectedParentId, meterId) {
    const options = ['<option value="">None / keep current</option>'];
    (parentOptions || []).forEach(option => {
        const value = String(option.value ?? '');
        if (!value || String(meterId ?? '') === value) return;
        const selected = selectedParentId === value ? 'selected' : '';
        options.push(`<option value="${escapeAttribute(value)}" ${selected}>${escapeHtml(option.text || value)}</option>`);
    });

    return `<select class="form-select form-select-sm web-service-parent-select" data-variable-index="${index}">${options.join('')}</select>`;
}

function applyBulkUnit(overwrite) {
    const unit = document.getElementById('webServiceBulkUnit')?.value.trim() || '';
    if (!unit) {
        showWebServiceStatus('warning', 'Enter a unit before applying it.');
        return;
    }

    let changed = 0;
    selectedWebServiceRows().forEach(row => {
        const input = row.querySelector('.web-service-unit-input');
        if (!input) return;
        if (overwrite || !input.value.trim()) {
            input.value = unit;
            changed++;
        }
    });

    showWebServiceStatus('success', `${unit} applied to ${changed} selected variable(s).`);
}

async function resolveUnitsFromPcVue(silent = false) {
    const connection = getStoredWebServiceConnectionInfo();
    const rows = selectedWebServiceRows().filter(row => {
        const input = row.querySelector('.web-service-unit-input');
        return input && !input.value.trim();
    });

    if (!connection.connectionId || rows.length === 0) return;

    const variableNames = rows.map(row => row.dataset.variableName).filter(Boolean);
    try {
        if (!silent) showWebServiceStatus('info', `Reading units from PCVue for ${variableNames.length} variable(s)...`);

        const response = await fetch('/WebServicesImport/ResolveVariableUnits', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ connectionId: connection.connectionId, variableNames })
        });
        const data = await response.json();
        if (!response.ok || !data.success) throw new Error(data.error || `HTTP ${response.status}`);

        const units = data.units || {};
        let applied = 0;
        rows.forEach(row => {
            const unit = lookupCaseInsensitive(units, row.dataset.variableName);
            const input = row.querySelector('.web-service-unit-input');
            if (input && typeof unit === 'string' && unit.trim() && !input.value.trim()) {
                input.value = unit.trim();
                applied++;
            }
        });

        if (!silent) {
            showWebServiceStatus(
                applied > 0 ? 'success' : 'info',
                applied > 0
                    ? `${applied} unit(s) read from PCVue.`
                    : 'PCVue did not provide an engineering unit for the selected blank variables.'
            );
        }
    } catch (error) {
        if (!silent) showWebServiceStatus('warning', `Unable to read PCVue units: ${error.message}`);
    }
}

function handleWebServicePrint() {
    const selectedVariables = collectSelectedWebServiceVariables();
    if (selectedVariables.length === 0) {
        alert('Select at least one Web Service variable.');
        return;
    }

    const connection = getStoredWebServiceConnectionInfo();
    fetch('/WebServicesImport/PrintWebServiceMeters', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
            connectionId: connection.connectionId || '',
            connectionName: connection.connectionName || '',
            selectedVariables
        })
    }).catch(() => {});
}

async function importWebServiceVariables() {
    const selectedVariables = collectSelectedWebServiceVariables();
    if (selectedVariables.length === 0) {
        alert('Select at least one Web Service variable to import.');
        return;
    }

    const missingUnitCount = selectedVariables.filter(variable => !variable.unit.trim()).length;
    if (missingUnitCount > 0 && !confirm(`${missingUnitCount} selected variable(s) still have no unit. Import them without a unit?`)) {
        return;
    }

    const connection = getStoredWebServiceConnectionInfo();
    const dateRange = getSelectedDateRange();
    const existingMode = document.getElementById('webServiceExistingMode')?.value || 'update';
    const includeSystemVariables = Boolean(document.getElementById('includeSystemVariables')?.checked);
    const button = document.getElementById('importSelectedBtn');
    const oldHtml = button?.innerHTML || '';

    try {
        if (button) {
            button.disabled = true;
            button.innerHTML = '<span class="spinner-border spinner-border-sm me-1"></span> Importing...';
        }

        const response = await fetch('/Import/ImportWebServiceVariablesWithTrends', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({
                variables: selectedVariables,
                skipExisting: existingMode === 'skip',
                updateExisting: existingMode !== 'skip',
                includeSystemVariables,
                importTrendsData: true,
                trendsStartDate: dateRange.startDate,
                trendsEndDate: dateRange.endDate,
                connectionId: connection.connectionId || ''
            })
        });
        const data = await response.json();
        if (!response.ok || !data.success) {
            throw new Error(data.error || data.errorMessage || `HTTP ${response.status}`);
        }

        const parts = [
            `Imported: ${data.importedCount || 0}`,
            `Updated: ${data.updatedCount || 0}`,
            `Skipped: ${data.skippedCount || 0}`,
            `Errors: ${data.errorCount || 0}`
        ];
        if (data.filteredSystemCount) parts.push(`System variables filtered: ${data.filteredSystemCount}`);
        if (data.trendsStarted) parts.push('Historical trends started in background');

        showWebServiceStatus(data.errorCount ? 'warning' : 'success', parts.join(' · '));

        // Update the visual state immediately. The next browse will re-read the DB.
        document.querySelectorAll('.web-service-variable-row').forEach(row => {
            if (!row.querySelector('.web-service-variable-checkbox')?.checked) return;
            if (!row.querySelector('.badge.bg-light')) {
                const nameCell = row.cells[2];
                nameCell?.insertAdjacentHTML('beforeend', '<span class="badge bg-light text-dark border ms-2">Existing</span>');
            }
        });
    } catch (error) {
        showWebServiceStatus('danger', `Web Service import failed: ${error.message}`);
    } finally {
        if (button) {
            button.disabled = false;
            button.innerHTML = oldHtml || '<i class="bi bi-cloud-upload"></i> Import Selected Variables';
        }
        if (typeof updateMeterCounter === 'function') updateMeterCounter();
    }
}

function collectSelectedWebServiceVariables() {
    const includeSystemVariables = Boolean(document.getElementById('includeSystemVariables')?.checked);
    const result = [];

    selectedWebServiceRows().forEach(row => {
        const variableName = row.dataset.variableName || '';
        if (!variableName) return;
        if (!includeSystemVariables && isSystemVariablePath(variableName)) return;

        const index = row.dataset.variableIndex;
        result.push({
            variableName,
            unit: row.querySelector('.web-service-unit-input')?.value.trim() || '',
            type: row.querySelector('.web-service-type-select')?.value || 'main',
            parentMeterId: row.querySelector('.web-service-parent-select')?.value || '',
            active: Boolean(row.querySelector('.web-service-active-checkbox')?.checked),
            variableType: row.cells[7]?.textContent.trim() || '',
            isReadOnly: row.cells[8]?.textContent.trim().toLowerCase() === 'yes',
            isSelected: true
        });
    });

    return result;
}

function selectedWebServiceRows() {
    return Array.from(document.querySelectorAll('.web-service-variable-row')).filter(row =>
        row.querySelector('.web-service-variable-checkbox')?.checked
    );
}

function isSystemVariablePath(path) {
    if (!path) return false;
    return path
        .split(/[./\\]+/)
        .filter(Boolean)
        .some(segment => segment.replace(/^[$@_]+/, '').toLowerCase() === 'system');
}

function storeWebServiceConnectionInfo(connectionInfo) {
    if (!connectionInfo?.connectionId) return;
    window.webServiceConnectionInfo = connectionInfo;
    try {
        sessionStorage.setItem('webServiceConnectionInfo', JSON.stringify(connectionInfo));
    } catch (_) { }
}

function getStoredWebServiceConnectionInfo() {
    if (window.webServiceConnectionInfo?.connectionId) return window.webServiceConnectionInfo;
    try {
        const value = sessionStorage.getItem('webServiceConnectionInfo');
        if (value) {
            const parsed = JSON.parse(value);
            if (parsed?.connectionId) {
                window.webServiceConnectionInfo = parsed;
                return parsed;
            }
        }
    } catch (_) { }

    const select = document.getElementById('webServiceConnection');
    const option = select?.selectedOptions?.[0];
    return select?.value
        ? { connectionId: select.value, connectionName: option?.dataset.connectionName || option?.textContent || select.value }
        : {};
}

function setupWebServiceDateRange() {
    const start = document.getElementById('webServiceStartDate');
    const end = document.getElementById('webServiceEndDate');
    if (!start || !end) return;

    if (!start.value || !end.value) setWebServiceQuickRange(24, 'hours');

    document.getElementById('wsQuickRange24h')?.addEventListener('click', event => {
        event.preventDefault(); setWebServiceQuickRange(24, 'hours'); highlightQuickRange('wsQuickRange24h');
    });
    document.getElementById('wsQuickRange7d')?.addEventListener('click', event => {
        event.preventDefault(); setWebServiceQuickRange(7, 'days'); highlightQuickRange('wsQuickRange7d');
    });
    document.getElementById('wsQuickRange30d')?.addEventListener('click', event => {
        event.preventDefault(); setWebServiceQuickRange(30, 'days'); highlightQuickRange('wsQuickRange30d');
    });

    start.addEventListener('change', validateWebServiceDateRange);
    end.addEventListener('change', validateWebServiceDateRange);
}

function setWebServiceQuickRange(amount, unit) {
    const endDate = new Date();
    const startDate = new Date(endDate);
    if (unit === 'hours') startDate.setHours(startDate.getHours() - amount);
    else startDate.setDate(startDate.getDate() - amount);

    const start = document.getElementById('webServiceStartDate');
    const end = document.getElementById('webServiceEndDate');
    if (start) start.value = formatDateTimeLocal(startDate);
    if (end) end.value = formatDateTimeLocal(endDate);
}

function highlightQuickRange(activeId) {
    ['wsQuickRange24h', 'wsQuickRange7d', 'wsQuickRange30d'].forEach(id => {
        const button = document.getElementById(id);
        if (!button) return;
        button.classList.toggle('btn-secondary', id === activeId);
        button.classList.toggle('btn-outline-secondary', id !== activeId);
    });
}

function validateWebServiceDateRange() {
    const range = getSelectedDateRange();
    if (!range.startDate || !range.endDate) return true;
    const valid = new Date(range.startDate) < new Date(range.endDate);
    if (!valid) showWebServiceStatus('warning', 'Trends start date must be before the end date.');
    return valid;
}

function getSelectedDateRange() {
    const start = document.getElementById('webServiceStartDate')?.value || null;
    const end = document.getElementById('webServiceEndDate')?.value || null;
    return {
        startDate: start ? new Date(start).toISOString() : null,
        endDate: end ? new Date(end).toISOString() : null
    };
}

function formatDateTimeLocal(date) {
    const pad = value => String(value).padStart(2, '0');
    return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}T${pad(date.getHours())}:${pad(date.getMinutes())}`;
}

function normalizeVariablePath(value) {
    return String(value || '').trim().replace(/^\.+/, '');
}

function normalizeMeterType(value) {
    return String(value || '').toLowerCase() === 'sub' ? 'sub' : 'main';
}

function shortType(value) {
    const normalized = String(value || '').toLowerCase();
    if (['numeric', 'real', 'double', 'register'].includes(normalized)) return 'NUM';
    if (['boolean', 'bool'].includes(normalized)) return 'BOOL';
    if (['string', 'text'].includes(normalized)) return 'TXT';
    return String(value || 'VAR').slice(0, 4).toUpperCase();
}

function lookupCaseInsensitive(object, key) {
    if (!object || !key) return undefined;
    if (Object.prototype.hasOwnProperty.call(object, key)) return object[key];
    const found = Object.keys(object).find(candidate => candidate.toLowerCase() === key.toLowerCase());
    return found ? object[found] : undefined;
}

function cssEscape(value) {
    if (window.CSS?.escape) return window.CSS.escape(String(value));
    return String(value).replace(/["\\]/g, '\\$&');
}

function escapeHtml(value) {
    return String(value ?? '')
        .replaceAll('&', '&amp;')
        .replaceAll('<', '&lt;')
        .replaceAll('>', '&gt;')
        .replaceAll('"', '&quot;')
        .replaceAll("'", '&#039;');
}

function escapeAttribute(value) {
    return escapeHtml(value).replaceAll('`', '&#096;');
}
