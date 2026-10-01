(() => {
    'use strict';

    let activeHistoryJobId = null;
    async function followHistoricalImport(jobId) {
        if (!jobId) return;
        activeHistoryJobId = jobId;
        try { sessionStorage.setItem('poworksHistoricalJobId', jobId); } catch (_) { /* private browsing */ }
        const status = document.getElementById('historicalImportStatus');
        if (status) status.style.display = 'block';

        try {
            const response = await fetch(`/Import/HistoricalTrendJobStatus/${encodeURIComponent(jobId)}`);
            if (!response.ok) throw new Error('Import status is no longer available (the server may have restarted).');
            const progress = await response.json();
            if (jobId !== activeHistoryJobId) return;

            if (status) {
                status.className = `alert ${progress.complete && (progress.failed || progress.failedVariables || !progress.pointsReturned) ? 'alert-warning' : 'alert-info'}`;
                const prefix = progress.complete ? 'Historical import finished.' : 'Historical import in progress.';
                status.textContent = `${prefix} ${progress.processedVariables}/${progress.totalVariables} variables processed; ` +
                    `${progress.variablesWithData} with archive data; ${progress.pointsReturned} points received; ` +
                    `${progress.insertedReadings} new readings saved; ${progress.failedVariables} failed.` +
                    (progress.errors?.length ? ` PcVue errors: ${progress.errors.join(' | ')}.` : '') +
                    (progress.complete && !progress.pointsReturned ? ' No archived points were returned for this selection and date range.' : '') +
                    (progress.complete && progress.pointsReturned && !progress.insertedReadings ? ' The points may already exist in PoWorks.' : '');
            }

            if (progress.complete) {
                try { sessionStorage.removeItem('poworksHistoricalJobId'); } catch (_) { /* private browsing */ }
            } else {
                setTimeout(() => { if (activeHistoryJobId === jobId) followHistoricalImport(jobId); }, 3000);
            }
        } catch (error) {
            if (status && jobId === activeHistoryJobId) {
                status.className = 'alert alert-warning';
                status.textContent = `Unable to check historical import: ${error.message}`;
            }
        }
    }

    window.PoWorksHistoricalImport = { follow: followHistoricalImport };

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
                <div class="form-check w-100">
                    <input class="form-check-input" type="checkbox" id="webServiceImportTrends" checked>
                    <label class="form-check-label small" for="webServiceImportTrends">
                        Import historical trends (uncheck for a quick meter and unit update)
                    </label>
                </div>
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
        if (data.historyJobId) followHistoricalImport(data.historyJobId);
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
            const selectedConnectionId = document.getElementById('webServiceConnection')?.value || '';

            if (selectedConnectionId && connectionInfo.connectionId !== selectedConnectionId) {
                alert('The PcVue connection changed. Browse variables again before importing them.');
                return;
            }

            const requestData = {
                variables,
                includeSystemVariables,
                importTrendsData: document.getElementById('webServiceImportTrends')?.checked !== false,
                trendsStartDate: dateRange.startDate,
                trendsEndDate: dateRange.endDate,
                connectionId: selectedConnectionId || connectionInfo.connectionId || ''
            };

            if (requestData.importTrendsData &&
                (!requestData.connectionId || !requestData.trendsStartDate || !requestData.trendsEndDate ||
                 !Number.isFinite(Date.parse(requestData.trendsStartDate)) ||
                 !Number.isFinite(Date.parse(requestData.trendsEndDate)) ||
                 Date.parse(requestData.trendsStartDate) >= Date.parse(requestData.trendsEndDate))) {
                alert('Select a PcVue Web Service connection and a valid historical date range first.');
                return;
            }

            if (requestData.importTrendsData) {
                // datetime-local has no timezone. PcVue expects UTC; convert
                // using the user's browser timezone before sending to ASP.NET.
                requestData.trendsStartDate = new Date(requestData.trendsStartDate).toISOString();
                requestData.trendsEndDate = new Date(requestData.trendsEndDate).toISOString();
            }

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

    async function probeHistory() {
        const status = document.getElementById('pcVueHistoryProbeStatus');
        const button = document.getElementById('probePcVueHistoryBtn');
        const connectionId = document.getElementById('webServiceConnection')?.value || '';
        const variableName = document.getElementById('webServiceHistoryProbeVariable')?.value.trim() || '';
        const startDate = document.getElementById('webServiceStartDate')?.value || '';
        const endDate = document.getElementById('webServiceEndDate')?.value || '';
        const duration = Date.parse(endDate) - Date.parse(startDate);

        if (!connectionId || !variableName || !Number.isFinite(duration) ||
            duration <= 0 || duration > 7 * 24 * 60 * 60 * 1000) {
            if (status) status.textContent = 'Select a PcVue connection, a variable, and a valid date range of up to 7 days.';
            return;
        }

        if (button) button.disabled = true;
        if (status) status.textContent = 'Checking PcVue historical data…';
        try {
            const response = await fetch('/Import/ProbePcVueHistory', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({
                    connectionId, variableName,
                    startDate: new Date(startDate).toISOString(),
                    endDate: new Date(endDate).toISOString()
                })
            });
            const result = await response.json();
            if (!response.ok || !result.success) throw new Error(result.error || `HTTP ${response.status}`);
            if (status) {
                status.textContent = result.pointCount > 0
                    ? `PcVue returned ${result.pointCount} original points: ` +
                      `${new Date(result.firstUtc).toLocaleString('fr-FR')} – ${new Date(result.lastUtc).toLocaleString('fr-FR')}. ` +
                      'You can now import this range.'
                    : 'PcVue returned 0 points for this variable and range. PoWorks has nothing to import from this PcVue connection at these dates.';
            }
        } catch (error) {
            if (status) status.textContent = `PcVue history check failed: ${error.message}`;
        } finally {
            if (button) button.disabled = false;
        }
    }

    // The view scripts are rendered before this enhancement script. Install immediately,
    // then once more on DOMContentLoaded for pages where script ordering changes later.
    install();
    document.addEventListener('DOMContentLoaded', () => {
        install();
        document.getElementById('probePcVueHistoryBtn')?.addEventListener('click', probeHistory);
        try {
            const pendingJob = sessionStorage.getItem('poworksHistoricalJobId');
            if (pendingJob) followHistoricalImport(pendingJob);
        } catch (_) { /* private browsing */ }
    });
})();
