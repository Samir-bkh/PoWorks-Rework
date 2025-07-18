/**
 * varexp_import.js
 * Handles:
 * - Parsing VAREXP.DAT files
 * - Converting records to meter format
 * - Displaying VAREXP meters in unified meter table
 * - Importing VAREXP meters into database
 * - Printing VAREXP meters
 */

// =====================================================
// INITIALIZATION
// =====================================================

/**
 * Initializes VAREXP import on DOM ready.
 */
document.addEventListener('DOMContentLoaded', function () {
    initializeVarexpElements();
});

/**
 * Checks required elements for VAREXP import
 * and wires the parse button click event.
 */
function initializeVarexpElements() {
    const parseVarexpBtn = document.getElementById('parseVarexpBtn');
    const varexpFileInput = document.getElementById('varexpFileInput');
    const recordsContainer = document.getElementById('varexpRecordsContainer');

    if (parseVarexpBtn && varexpFileInput && recordsContainer) {
        parseVarexpBtn.addEventListener('click', handleVarexpFileParse);
    } 
}

// =====================================================
// VAREXP FILE PARSING
// =====================================================

/**
 * Handles user clicking the "Parse VAREXP" button
 * and sends the DAT file to the backend.
 */
function handleVarexpFileParse() {

    const varexpFileInput = document.getElementById('varexpFileInput');
    const file = varexpFileInput.files[0];

    if (!file) {
        return alert('Please select a VAREXP.DAT file first');
    }

    const formData = new FormData();
    formData.append('VarexpFile', file);

    fetch('/VarexpImport/ParseVarexp', {
        method: 'POST',
        body: formData
    })
        .then(async res => {
            const contentType = res.headers.get('content-type') || '';

            if (res.ok) {
                if (contentType.includes('application/json')) {
                    return res.json();
                }
                throw new Error('Invalid JSON response');
            }

            let errMsg = '';
            if (contentType.includes('application/json')) {
                const errData = await res.json();
                errMsg = errData.error || JSON.stringify(errData);
            } else {
                errMsg = await res.text();
            }
            throw new Error(errMsg || `Error ${res.status} ${res.statusText}`);
        })
        .then(data => {

            if (data.records) {
                window.parentOptions = data.parentOptions || [{ value: '', text: 'None' }];
                convertVarexpToMeterSelection(data.records);
            } else {
                alert(data.error || 'No records returned');
            }
        })
        .catch(error => {
            alert(`Error parsing VAREXP file: ${error.message}`);
        });
}

// =====================================================
// VAREXP TO METER CONVERSION
// =====================================================

/**
 * Converts raw VAREXP records into simplified meter objects.
 * @param {Array<Array<string>>} records
 */
function convertVarexpToMeterSelection(records) {

    const meterRecords = records.filter(record => {
        if (!record || record.length < 2) return false;

        const recordType = record[0].trim();
        const combinedName = record[1].trim();

        if (combinedName === 'CombinedName' || recordType === 'Class') return false;
        if (combinedName.toLowerCase().startsWith('system')) return false;

        const validTypes = ['CHR', 'CMD', 'REG', 'TXT'];
        return validTypes.includes(recordType.toUpperCase()) && combinedName;
    });

    if (meterRecords.length === 0) {
        alert('No valid meter records found in VAREXP.DAT file.');
        return;
    }

    const meters = meterRecords.map(record => ({
        hdsMeterName: record[1].trim(),
        unit: '',
        type: 'Main',
        active: true,
        isSelected: true,
        lastReading: '0',
        recordType: record[0].trim()
    }));

    showMeterSelectionForVarexp(meters);
}

// =====================================================
// METER SELECTION DISPLAY
// =====================================================

/**
 * Displays VAREXP meters in the unified meter selection table.
 * @param {Array<Object>} meters
 */
function showMeterSelectionForVarexp(meters) {

    const container = document.getElementById('varexpRecordsContainer');
    if (container) {
        container.innerHTML = '';
    }

    const meterSelectionSection = document.getElementById('meterSelectionSection');
    if (!meterSelectionSection) {
        return;
    }

    meterSelectionSection.classList.remove('d-none');
    meterSelectionSection.querySelector('.card-header h5').textContent = 'VAREXP Meter Selection';

    window.currentMeterDataType = 'VAREXP';
    window.currentHDSContext = null;

    const importReadingsCheckbox = document.getElementById('importReadings');
    importReadingsCheckbox?.closest('.form-check')?.style?.setProperty('display', 'none');

    renderVarexpMetersTable(meters);

    if (typeof updatePrintButtonForDataType === 'function') {
        updatePrintButtonForDataType('VAREXP');
    }

    const statusElement = document.getElementById('meterFilterStatus');
    if (statusElement) {
        statusElement.textContent = `Loaded ${meters.length} meters from VAREXP.DAT`;
    }
}

/**
 * Populates the meters table with VAREXP meter rows.
 * @param {Array<Object>} meters
 */
function renderVarexpMetersTable(meters) {

    const tbody = document.getElementById('metersTableBody');
    if (!tbody) {
        return;
    }

    tbody.innerHTML = '';

    if (!meters.length) {
        tbody.innerHTML = '<tr><td colspan="6" class="text-center">No meters found in VAREXP.DAT</td></tr>';
        return;
    }

    // Add info header
    const headerRow = document.createElement('tr');
    headerRow.className = 'table-info';
    headerRow.innerHTML = `
        <td colspan="6" class="text-center">
            <small><strong>VAREXP Import:</strong> Showing ${meters.length} meters from VAREXP.DAT file. 
            Only meter names are extracted. Fill in units and other details as needed.</small>
        </td>
    `;
    tbody.appendChild(headerRow);

    meters.forEach((meter, index) => {
        const row = document.createElement('tr');

        row.innerHTML = `
            <td class="text-center">
                <div class="form-check d-flex justify-content-center">
                    <input class="form-check-input meter-checkbox" type="checkbox"
                        id="meter_${index}" checked>
                </div>
            </td>
            <td>
                <div>
                    <span class="badge bg-secondary me-2">${meter.recordType}</span>
                    <span title="VAREXP Meter ${index + 1} of ${meters.length}">${meter.hdsMeterName}</span>
                </div>
            </td>
            <td>
                <input type="text" class="form-control form-control-sm meter-unit"
                       placeholder="Enter unit (e.g., kWh, bar, °C)">
            </td>
            <td>${renderParentDropdown()}</td>
            <td>
                <select class="form-select form-select-sm meter-type">
                    <option value="main" selected>Main</option>
                    <option value="sub">Sub</option>
                </select>
            </td>
            <td class="text-center">
                <div class="form-check d-flex justify-content-center">
                    <input class="form-check-input meter-active" type="checkbox"
                           id="active_${index}" checked>
                </div>
            </td>
        `;

        tbody.appendChild(row);
    });

    if (typeof updateMeterCounter === 'function') {
        updateMeterCounter();
    }
}

/**
 * Renders the dropdown HTML for parent meters.
 */
function renderParentDropdown() {
    if (window.parentOptions?.length > 0) {
        return `
            <select class="form-select form-select-sm meter-parent">
                ${window.parentOptions.map(opt =>
            `<option value="${opt.value || opt.Value}">${opt.text || opt.Text}</option>`
        ).join('')}
            </select>
        `;
    }
    return `
        <select class="form-select form-select-sm meter-parent">
            <option value="">No parent meters found</option>
        </select>
        <small class="text-muted">No existing meters in database</small>
    `;
}

// =====================================================
// VAREXP PRINT FUNCTIONALITY
// =====================================================

/**
 * Handles the unified print button for VAREXP meters.
 */
function handleVarexpPrint() {

    const selectedCheckboxes = document.querySelectorAll('.meter-checkbox:checked');
    if (!selectedCheckboxes.length) {
        alert('Please select at least one meter to print.');
        return;
    }

    const selectedMeterNames = [];
    const selectedMeterTypes = [];
    const selectedMeterUnits = [];

    selectedCheckboxes.forEach(checkbox => {
        const row = checkbox.closest('tr');
        if (!row || row.classList.contains('table-info')) return;

        const nameCell = row.querySelector('td:nth-child(2)');
        const meterNameSpan = nameCell.querySelector('span:not(.badge)');
        const meterName = meterNameSpan?.textContent.trim() || '';

        if (!meterName) return;

        const unit = row.querySelector('.meter-unit')?.value || '';
        const type = row.querySelector('.meter-type')?.value || 'main';

        selectedMeterNames.push(meterName);
        selectedMeterTypes.push(type);
        selectedMeterUnits.push(unit);
    });

    if (!selectedMeterNames.length) {
        alert('No valid VAREXP meters found in selection.');
        return;
    }

    const requestData = {
        tableName: 'VAREXP.DAT',
        selectedMeterNames,
        selectedMeterTypes,
        selectedMeterUnits
    };

    fetch('/Import/PrintSelectedMeters', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(requestData)
    })
        .then(response => {
            if (!response.ok) {
                throw new Error(`HTTP ${response.status}: ${response.statusText}`);
            }
            return response.json();
        })
        .then(data => {
            if (data.success) {
                alert(`✅ Successfully printed ${data.count} VAREXP meters.`);
            } else {
                alert('❌ VAREXP Print failed: ' + (data.error || 'Unknown error'));
            }
        })
        .catch(error => {
            alert(`❌ VAREXP Print failed: ${error.message}`);
        });
}

// =====================================================
// VAREXP IMPORT FUNCTIONALITY
// =====================================================

/**
 * Handles importing selected VAREXP meters into the database.
 */
function handleVarexpImport() {

    const selectedCheckboxes = document.querySelectorAll('.meter-checkbox:checked');
    if (!selectedCheckboxes.length) {
        alert('Please select at least one meter to import.');
        return;
    }

    if (!confirm(`Import ${selectedCheckboxes.length} meters from VAREXP?`)) {
        return;
    }

    const importBtn = document.getElementById('importSelectedBtn');
    const originalText = importBtn.textContent;
    importBtn.disabled = true;
    importBtn.innerHTML = '<span class="spinner-border spinner-border-sm"></span> Importing...';

    const skipExisting = document.getElementById('skipExisting')?.checked ?? true;
    const updateExisting = document.getElementById('updateExisting')?.checked ?? false;
    const createMissingParents = document.getElementById('createMissingParents')?.checked ?? false;

    const meters = [];

    selectedCheckboxes.forEach(checkbox => {
        const row = checkbox.closest('tr');
        if (!row || row.classList.contains('table-info')) return;

        const nameCell = row.querySelector('td:nth-child(2)');
        const meterNameSpan = nameCell.querySelector('span:not(.badge)');
        const meterName = meterNameSpan?.textContent.trim() || '';

        if (!meterName) return;

        const unit = row.querySelector('.meter-unit')?.value || '';
        const type = row.querySelector('.meter-type')?.value || 'Main';
        const parent = row.querySelector('.meter-parent')?.value || '';
        const active = row.querySelector('.meter-active')?.checked ?? true;

        meters.push({
            meterName,
            unit,
            type,
            parentMeterId: parent,
            active
        });
    });

    if (!meters.length) {
        alert('No valid meters found to import.');
        importBtn.disabled = false;
        importBtn.textContent = originalText;
        return;
    }

    fetch('/VarexpImport/ImportVarexpMeters', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
            meters,
            skipExisting,
            updateExisting,
            createMissingParents
        })
    })
        .then(response => {
            if (!response.ok) {
                throw new Error(`HTTP error! status: ${response.status}`);
            }
            return response.json();
        })
        .then(data => {

            importBtn.disabled = false;
            importBtn.textContent = originalText;

            showVarexpImportResults(data);

            if (data.success && (data.importedCount > 0 || data.updatedCount > 0)) {
                setTimeout(() => {
                    if (confirm('Import completed successfully! Reload page to see the new meters?')) {
                        window.location.href = '/Meter/Management';
                    }
                }, 2000);
            }
        })
        .catch(error => {
            importBtn.disabled = false;
            importBtn.textContent = originalText;
            alert(`Error importing meters: ${error.message}`);
        });
}

/**
 * Displays the result summary after VAREXP import.
 * @param {Object} data
 */
function showVarexpImportResults(data) {
    let message = '';

    if (data.success) {
        message = `✅ VAREXP Import Successful!\n\n` +
            `• Imported: ${data.importedCount}\n` +
            `• Updated: ${data.updatedCount}\n` +
            `• Skipped: ${data.skippedCount}\n` +
            `• Total processed: ${data.totalProcessed}`;
    } else {
        message = `❌ VAREXP Import Failed:\n` +
            `${data.error || 'Unknown error'}\n` +
            `• Imported: ${data.importedCount || 0}\n` +
            `• Updated: ${data.updatedCount || 0}\n` +
            `• Skipped: ${data.skippedCount || 0}\n` +
            `• Errors: ${data.errorCount || 0}`;

        if (data.detailedErrors) {
            for (const [meterName, err] of Object.entries(data.detailedErrors)) {
                message += `\n• ${meterName}: ${err}`;
            }
        }
    }

    alert(message);
}

// =====================================================
// GLOBAL EXPORTS
// =====================================================

/**
 * Export key VAREXP functions for global use.
 */
window.VarexpImport = {
    handleVarexpPrint,
    handleVarexpImport,
    convertVarexpToMeterSelection,
    showMeterSelectionForVarexp
};
