(() => {
    'use strict';

    function isSystemVariable(variable) {
        const branches = variable?.branches || variable?.Branches || [];
        if (Array.isArray(branches) && branches.some(b => String(b).toLowerCase() === 'system')) {
            return true;
        }

        const path = String(
            variable?.fullPath ||
            variable?.FullPath ||
            variable?.variableName ||
            variable?.VariableName ||
            ''
        ).trim();

        return /^system(?:[.\\/]|$)/i.test(path);
    }

    function createBulkUnitToolbar() {
        const wrapper = document.createElement('div');
        wrapper.id = 'webServiceBulkUnitToolbar';
        wrapper.className = 'border rounded bg-light p-2 mb-2';
        wrapper.innerHTML = `
            <div class="d-flex flex-wrap align-items-end gap-2">
                <div>
                    <label for="webServiceBulkUnit" class="form-label small fw-semibold mb-1">Bulk unit</label>
                    <input id="webServiceBulkUnit"
                           class="form-control form-control-sm"
                           list="webServiceUnitPresets"
                           placeholder="kWh, °C, bar, m³..."
                           autocomplete="off"
                           style="width: 150px;" />
                    <datalist id="webServiceUnitPresets">
                        <option value="Wh"></option>
                        <option value="kWh"></option>
                        <option value="MWh"></option>
                        <option value="W"></option>
                        <option value="kW"></option>
                        <option value="MW"></option>
                        <option value="°C"></option>
                        <option value="bar"></option>
                        <option value="Pa"></option>
                        <option value="kPa"></option>
                        <option value="m³"></option>
                        <option value="L"></option>
                        <option value="m³/h"></option>
                        <option value="L/min"></option>
                        <option value="%"></option>
                        <option value="ppm"></option>
                    </datalist>
                </div>
                <button type="button" class="btn btn-sm btn-outline-primary" id="applyUnitToSelected">
                    Apply to selected
                </button>
                <button type="button" class="btn btn-sm btn-outline-secondary" id="fillMissingUnits">
                    Fill empty selected
                </button>
                <small class="text-muted ms-auto" id="bulkUnitStatus">
                    Existing meter units are updated only when you explicitly provide a non-empty unit.
                </small>
            </div>`;
        return wrapper;
    }

    function applyBulkUnit(fillOnlyEmpty) {
        const unit = (document.getElementById('webServiceBulkUnit')?.value || '').trim();
        if (!unit) {
            const status = document.getElementById('bulkUnitStatus');
            if (status) status.textContent = 'Enter a unit first.';
            return;
        }

        let changed = 0;
        document.querySelectorAll('.web-service-variable-checkbox:checked').forEach(checkbox => {
            const index = checkbox.getAttribute('data-variable-index');
            const input = document.querySelector(`.web-service-unit-input[data-variable-index="${index}"]`);
            if (!input) return;
            if (fillOnlyEmpty && input.value.trim()) return;
            input.value = unit;
            input.dispatchEvent(new Event('change', { bubbles: true }));
            changed++;
        });

        const status = document.getElementById('bulkUnitStatus');
        if (status) {
            status.textContent = changed > 0
                ? `${unit} applied to ${changed} selected variable${changed === 1 ? '' : 's'}.`
                : 'No selected variable needed an update.';
        }
    }

    function enhanceRenderedWebServiceTable(variables) {
        const table = document.querySelector('#meterSelectionSection .table-responsive');
        if (!table) return;

        document.getElementById('webServiceBulkUnitToolbar')?.remove();
        table.parentNode.insertBefore(createBulkUnitToolbar(), table);

        document.getElementById('applyUnitToSelected')?.addEventListener('click', () => applyBulkUnit(false));
        document.getElementById('fillMissingUnits')?.addEventListener('click', () => applyBulkUnit(true));

        // Preserve a future unit supplied by the API/parser automatically.
        variables.forEach((variable, index) => {
            const unit = String(variable?.unit || variable?.Unit || '').trim();
            if (!unit) return;
            const input = document.querySelector(`.web-service-unit-input[data-variable-index="${index}"]`);
            if (input) input.value = unit;
        });
    }

    function installTableWrapper() {
        if (typeof window.showWebServiceMeterSelection !== 'function' || window.__poworksWsTableV2Installed) return;

        const original = window.showWebServiceMeterSelection;
        window.showWebServiceMeterSelection = function (variables, parentOptions, connectionInfo) {
            const includeSystem = document.getElementById('includeSystemVariables')?.checked === true;
            const inputVariables = Array.isArray(variables) ? variables : [];
            const filtered = includeSystem
                ? inputVariables
                : inputVariables.filter(v => !isSystemVariable(v));

            original(filtered, parentOptions, connectionInfo);
            enhanceRenderedWebServiceTable(filtered);

            if (!includeSystem && filtered.length !== inputVariables.length) {
                const removed = inputVariables.length - filtered.length;
                const status = document.getElementById('webServiceStatus');
                if (status) {
                    const note = document.createElement('div');
                    note.className = 'small text-muted mt-1';
                    note.textContent = `${removed} System variable${removed === 1 ? '' : 's'} filtered out.`;
                    status.appendChild(note);
                }
            }
        };

        window.__poworksWsTableV2Installed = true;
    }

    function showImportResult(data) {
        let message = 'Web Service import completed.\n';
        message += `Created: ${data.importedCount || 0}\n`;
        message += `Existing metadata updated: ${data.updatedCount || 0}\n`;
        message += `Existing unchanged: ${data.unchangedCount || 0}\n`;
        if ((data.filteredSystemCount || 0) > 0) {
            message += `System variables filtered: ${data.filteredSystemCount}\n`;
        }
        message += `Errors: ${data.errorCount || 0}`;
        if (data.trendsQueued) message += '\n\nHistorical trends import has started in the background.';
        alert(message);
    }

    function installImportOverride() {
        if (window.__poworksWsImportV2Installed) return;

        window.importWebServiceVariables = function () {
            if (typeof collectSelectedWebServiceVariables !== 'function') {
                alert('Web Service import table is not ready.');
                return;
            }

            const selectedVariables = collectSelectedWebServiceVariables();
            if (!selectedVariables.length) {
                alert('Please select at least one variable to import.');
                return;
            }

            const includeSystemVariables = document.getElementById('includeSystemVariables')?.checked === true;
            const systemSelected = selectedVariables.filter(v => isSystemVariable(v));
            const variables = includeSystemVariables
                ? selectedVariables
                : selectedVariables.filter(v => !isSystemVariable(v));

            if (!variables.length) {
                alert('Only System variables are selected, but Include System Variables is disabled.');
                return;
            }

            const withoutUnits = variables.filter(v => !String(v.unit || '').trim());
            if (withoutUnits.length > 0) {
                const proceed = confirm(
                    `${withoutUnits.length} selected variable${withoutUnits.length === 1 ? ' has' : 's have'} no unit. ` +
                    'They can still be imported, and an existing PoWorks unit will never be erased. Continue?'
                );
                if (!proceed) return;
            }

            const dateRange = typeof getSelectedDateRange === 'function'
                ? getSelectedDateRange()
                : { startDate: null, endDate: null };
            const connectionInfo = typeof getStoredWebServiceConnectionInfo === 'function'
                ? getStoredWebServiceConnectionInfo()
                : {};

            const requestData = {
                variables,
                includeSystemVariables,
                importTrendsData: true,
                trendsStartDate: dateRange.startDate,
                trendsEndDate: dateRange.endDate,
                connectionId: connectionInfo.connectionId || ''
            };

            const importBtn = document.getElementById('importSelectedBtn');
            const previousHtml = importBtn?.innerHTML || 'Import Selected Variables';
            if (importBtn) {
                importBtn.disabled = true;
                importBtn.innerHTML = '<i class="bi bi-hourglass-split"></i> Saving meters...';
            }

            fetch('/Import/UpsertWebServiceVariablesWithTrends', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(requestData)
            })
                .then(response => {
                    if (!response.ok) throw new Error(`HTTP ${response.status}: ${response.statusText}`);
                    return response.json();
                })
                .then(data => {
                    if (!data.success) {
                        throw new Error(data.error || data.errorMessage || 'Unknown import error');
                    }

                    showImportResult(data);

                    if (!includeSystemVariables && systemSelected.length > 0) {
                        console.info(`${systemSelected.length} System variables were removed before import.`);
                    }
                })
                .catch(error => {
                    console.error('Web Service upsert import failed:', error);
                    alert(`Web Service import failed: ${error.message}`);
                })
                .finally(() => {
                    if (importBtn) {
                        importBtn.disabled = false;
                        importBtn.innerHTML = previousHtml;
                        if (typeof updateMeterCounter === 'function') updateMeterCounter();
                    }
                });
        };

        window.__poworksWsImportV2Installed = true;
    }

    function install() {
        installTableWrapper();
        installImportOverride();
    }

    // The view scripts are rendered before this enhancement script. Install immediately,
    // then once more on DOMContentLoaded for pages where script ordering changes later.
    install();
    document.addEventListener('DOMContentLoaded', install);
})();
