// wwwroot/js/import.js - Clean VAREXP-Only Functionality

// Initialize the page
document.addEventListener('DOMContentLoaded', function () {
    console.log('🟨 DEBUG: DOM Content Loaded');

    // VAREXP Import functions
    const parseVarexpBtn = document.getElementById('parseVarexpBtn');
    const varexpFileInput = document.getElementById('varexpFileInput');
    const recordsContainer = document.getElementById('varexpRecordsContainer');

    console.log('🟨 DEBUG: VAREXP Elements found:', {
        parseVarexpBtn: !!parseVarexpBtn,
        varexpFileInput: !!varexpFileInput,
        recordsContainer: !!recordsContainer
    });

    // ✅ Setup Select All/Deselect All event delegation for VAREXP
    document.body.addEventListener('click', function (event) {
        // Handle VAREXP Select All button click
        if (event.target.id === 'selectAllMetersBtn' || event.target.closest('#selectAllMetersBtn')) {
            console.log('🟨 DEBUG: VAREXP Select All button clicked');
            handleVarexpSelectAll();
        }

        // Handle VAREXP Deselect All button click
        if (event.target.id === 'deselectAllMetersBtn' || event.target.closest('#deselectAllMetersBtn')) {
            console.log('🟨 DEBUG: VAREXP Deselect All button clicked');
            handleVarexpDeselectAll();
        }

        // Handle VAREXP Print Selected button - CLEAN VERSION
        if (event.target.id === 'printSelectedBtn' || event.target.closest('#printSelectedBtn')) {
            console.log('🟨 VAREXP Print Handler - Processing VAREXP meters...');
            handleVarexpPrint();
        }

        // Handle VAREXP Import Selected button
        if (event.target.id === 'importSelectedBtn' || event.target.closest('#importSelectedBtn')) {
            console.log('🟨 DEBUG: VAREXP Import Selected button clicked');
            handleVarexpImport();
        }
    });

    // ✅ Setup checkbox change events for VAREXP
    document.body.addEventListener('change', function (event) {
        if (event.target.classList.contains('meter-checkbox')) {
            handleVarexpCheckboxChange(event.target);
        }
    });

    if (parseVarexpBtn && varexpFileInput && recordsContainer) {
        parseVarexpBtn.addEventListener('click', () => {
            console.log('🟨 DEBUG: Parse VAREXP button clicked');

            const file = varexpFileInput.files[0];
            if (!file) {
                console.log('🟨 DEBUG: No file selected');
                return alert('Please select a VAREXP.DAT file first');
            }

            console.log('🟨 DEBUG: File selected:', file.name);

            const formData = new FormData();
            formData.append('VarexpFile', file);

            console.log('🟨 DEBUG: Sending request to /Import/ParseVarexp');

            fetch('/Import/ParseVarexp', {
                method: 'POST',
                body: formData
            })
                .then(async res => {
                    console.log('🟨 DEBUG: Response status:', res.status);

                    const contentType = res.headers.get('content-type') || '';
                    console.log('🟨 DEBUG: Content type:', contentType);

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
                    console.log('🟨 DEBUG: Server response received:', data);

                    if (data.records) {
                        console.log('🟨 DEBUG: Records found:', data.records.length);

                        if (data.parentOptions) {
                            console.log('🟨 DEBUG: Parent options received:', data.parentOptions.length);
                            window.parentOptions = data.parentOptions;
                        } else {
                            console.log('🟨 DEBUG: No parent options in response, using default');
                            window.parentOptions = [{ value: '', text: 'None' }];
                        }

                        // Convert VAREXP records to meter format
                        convertVarexpToMeterSelection(data.records);
                    } else {
                        console.log('🟨 DEBUG: No records in response');
                        alert(data.error || 'No records returned');
                    }
                })
                .catch(error => {
                    console.error('🟨 ERROR: VAREXP parsing failed:', error);
                    alert(`Error parsing VAREXP file: ${error.message}`);
                });
        });
    } else {
        console.log('🟨 DEBUG: VAREXP elements not found!');
    }
});

// Handle VAREXP print functionality - CLEAN VERSION
function handleVarexpPrint() {
    console.log('🟨 Starting VAREXP print process');

    const selectedCheckboxes = document.querySelectorAll('.meter-checkbox:checked');

    if (selectedCheckboxes.length === 0) {
        alert('Please select at least one meter to print.');
        return;
    }

    console.log(`🟨 Found ${selectedCheckboxes.length} selected VAREXP meters`);

    // Gather VAREXP meter data
    const selectedMeterNames = [];
    const selectedMeterTypes = [];
    const selectedMeterUnits = [];

    selectedCheckboxes.forEach((checkbox, index) => {
        const row = checkbox.closest('tr');
        if (!row) return;

        // Skip the info header row
        if (row.classList.contains('table-info')) return;

        const nameCell = row.cells[1];
        if (!nameCell) return;

        // Extract meter name from span (VAREXP has .badge elements)
        const nameSpan = nameCell.querySelector('span:not(.badge)');
        const meterName = nameSpan ? nameSpan.textContent.trim() : '';

        if (!meterName) return;

        const unitInput = row.querySelector('.meter-unit');
        const unit = unitInput ? unitInput.value.trim() : '';

        const typeSelect = row.querySelector('.meter-type');
        const type = typeSelect ? typeSelect.value : 'main';

        selectedMeterNames.push(meterName);
        selectedMeterTypes.push(type);
        selectedMeterUnits.push(unit);
    });

    if (selectedMeterNames.length === 0) {
        alert('No valid VAREXP meters found in selection.');
        return;
    }

    console.log('🟨 Collected VAREXP meter data:', {
        names: selectedMeterNames,
        types: selectedMeterTypes,
        units: selectedMeterUnits
    });

    // Prepare VAREXP request data
    const requestData = {
        tableName: 'VAREXP.DAT',
        selectedMeterNames: selectedMeterNames,
        selectedMeterTypes: selectedMeterTypes,
        selectedMeterUnits: selectedMeterUnits
    };

    console.log('🟨 Sending VAREXP print request:', requestData);

    // Send to VAREXP endpoint
    fetch('/Import/PrintSelectedMeters', {
        method: 'POST',
        headers: {
            'Content-Type': 'application/json'
        },
        body: JSON.stringify(requestData)
    })
        .then(response => {
            if (!response.ok) {
                throw new Error(`HTTP ${response.status}: ${response.statusText}`);
            }
            return response.json();
        })
        .then(data => {
            console.log('🟨 VAREXP print response:', data);

            if (data.success) {
                alert(`✅ Successfully printed ${data.count} VAREXP meters to console!\nCheck the server console for details.`);
            } else {
                alert('❌ VAREXP Print failed: ' + (data.error || 'Unknown error'));
            }
        })
        .catch(error => {
            console.error('🟨 VAREXP print request failed:', error);
            alert('❌ VAREXP Print request failed: ' + error.message);
        });
}

/**
 * Handle VAREXP Select All button click
 */
function handleVarexpSelectAll() {
    const checkboxes = document.querySelectorAll('.meter-checkbox');
    console.log(`🟨 Selecting all ${checkboxes.length} VAREXP checkboxes`);

    checkboxes.forEach(function (checkbox) {
        checkbox.checked = true;

        const hiddenInput = checkbox.nextElementSibling;
        if (hiddenInput && hiddenInput.type === 'hidden') {
            hiddenInput.disabled = true;
        }
    });

    updateVarexpCounter();
}

/**
 * Handle VAREXP Deselect All button click
 */
function handleVarexpDeselectAll() {
    const checkboxes = document.querySelectorAll('.meter-checkbox');
    console.log(`🟨 Deselecting all ${checkboxes.length} VAREXP checkboxes`);

    checkboxes.forEach(function (checkbox) {
        checkbox.checked = false;

        const hiddenInput = checkbox.nextElementSibling;
        if (hiddenInput && hiddenInput.type === 'hidden') {
            hiddenInput.disabled = false;
        }
    });

    updateVarexpCounter();
}

/**
 * Handle individual VAREXP checkbox changes
 */
function handleVarexpCheckboxChange(checkbox) {
    console.log(`🟨 VAREXP checkbox changed: ${checkbox.id}, Checked: ${checkbox.checked}`);

    const hiddenInput = checkbox.nextElementSibling;
    if (hiddenInput && hiddenInput.type === 'hidden') {
        hiddenInput.disabled = checkbox.checked;
        console.log(`🟨 Updated hidden input disabled: ${hiddenInput.disabled}`);
    }

    updateVarexpCounter();
}

/**
 * Update the VAREXP selection counter display
 */
function updateVarexpCounter() {
    const checkboxes = document.querySelectorAll('.meter-checkbox');
    const checkedBoxes = document.querySelectorAll('.meter-checkbox:checked');
    console.log(`🟨 VAREXP selection count: ${checkedBoxes.length} of ${checkboxes.length}`);

    const statusElement = document.getElementById('meterFilterStatus');
    if (statusElement) {
        const totalMeters = checkboxes.length;
        const selectedMeters = checkedBoxes.length;
        statusElement.textContent = `Selected ${selectedMeters} of ${totalMeters} meters`;
    }

    const importBtn = document.getElementById('importSelectedBtn');
    if (importBtn) {
        importBtn.textContent = checkedBoxes.length > 0 ?
            `Import Selected (${checkedBoxes.length})` :
            'Import Selected';
    }

    const printBtn = document.getElementById('printSelectedBtn');
    if (printBtn) {
        printBtn.textContent = checkedBoxes.length > 0 ?
            `Print Selected (${checkedBoxes.length})` :
            'Print Selected';
    }
}

// Convert VAREXP records to meter selection format
function convertVarexpToMeterSelection(records) {
    console.log('🟨 Converting VAREXP records to meter selection');
    console.log('🟨 Total records received:', records.length);

    // Filter out header rows and system records
    const meterRecords = records.filter((record, index) => {
        if (!record || record.length < 2) {
            return false;
        }

        const recordType = record[0]?.trim() || '';
        const combinedName = record[1]?.trim() || '';

        // Skip header row
        if (combinedName === 'CombinedName' || recordType === 'Class') {
            return false;
        }

        // Skip system records
        if (combinedName.toLowerCase().startsWith('system')) {
            return false;
        }

        // Only include actual meter record types
        const validTypes = ['CHR', 'CMD', 'REG', 'TXT'];
        const isValidType = validTypes.includes(recordType.toUpperCase());

        if (!isValidType || !combinedName) {
            return false;
        }

        return true;
    });

    console.log(`🟨 Filtered ${meterRecords.length} valid meter records from ${records.length} total records`);

    if (meterRecords.length === 0) {
        console.log('🟨 No valid meter records found!');
        alert('No valid meter records found in VAREXP.DAT file. Check console for details.');
        return;
    }

    // Convert to simplified meter format
    const meters = meterRecords.map((record, index) => {
        const recordType = record[0]?.trim() || '';
        const combinedName = record[1]?.trim() || '';

        return {
            hdsMeterName: combinedName,
            unit: '',
            type: 'Main',
            active: true,
            isSelected: true,
            lastReading: '0',
            recordType: recordType
        };
    });

    console.log('🟨 Converted meters:', meters.length);

    // Show the meter selection section and populate with VAREXP data
    showMeterSelectionForVarexp(meters);
}

function showMeterSelectionForVarexp(meters) {
    console.log('🟨 Showing meter selection for VAREXP data');

    // Hide the VAREXP records container
    const recordsContainer = document.getElementById('varexpRecordsContainer');
    if (recordsContainer) {
        recordsContainer.innerHTML = '';
    }

    // Show meter selection section
    const meterSelectionSection = document.getElementById('meterSelectionSection');
    if (meterSelectionSection) {
        meterSelectionSection.classList.remove('d-none');
    } else {
        console.log('🟨 Meter selection section not found!');
        return;
    }

    // Update the section title to indicate VAREXP source
    const sectionHeader = meterSelectionSection.querySelector('.card-header h5');
    if (sectionHeader) {
        sectionHeader.textContent = 'VAREXP Meter Selection';
    }

    // 🎯 Show VAREXP print button, hide HDS print button
    const hdsBtn = document.getElementById('printSelectedHDSBtn');
    const varexpBtn = document.getElementById('printSelectedBtn');

    if (hdsBtn) hdsBtn.classList.add('d-none');
    if (varexpBtn) varexpBtn.classList.remove('d-none');

    console.log('🟨 VAREXP print button shown, HDS button hidden');

    // Hide the "Import historical readings" checkbox for VAREXP
    const importReadingsCheckbox = document.getElementById('importReadings');
    if (importReadingsCheckbox) {
        const checkboxContainer = importReadingsCheckbox.closest('.form-check');
        if (checkboxContainer) {
            checkboxContainer.style.display = 'none';
        }
    }

    // Render the table with VAREXP data
    renderVarexpMetersTable(meters);

    // Update status
    const statusElement = document.getElementById('meterFilterStatus');
    if (statusElement) {
        statusElement.textContent = `Loaded ${meters.length} meters from VAREXP.DAT`;
    }

    console.log('🟨 VAREXP meter selection setup complete');
}

// Render VAREXP meters in the table
function renderVarexpMetersTable(meters) {
    console.log('🟨 Rendering VAREXP meters table');

    const tbody = document.getElementById('metersTableBody');
    if (!tbody) {
        console.log('🟨 Table body not found!');
        return;
    }

    tbody.innerHTML = '';

    if (!meters || meters.length === 0) {
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

        // Import checkbox cell
        const checkboxCell = document.createElement('td');
        checkboxCell.className = 'text-center';
        checkboxCell.innerHTML = `
            <div class="form-check d-flex justify-content-center">
                <input class="form-check-input meter-checkbox" type="checkbox"
                       id="meter_${index}" checked>
            </div>
        `;

        // Meter name cell - VAREXP format with badge
        const nameCell = document.createElement('td');
        nameCell.innerHTML = `
            <div>
                <span class="badge bg-secondary me-2">${meter.recordType}</span>
                <span title="VAREXP Meter ${index + 1} of ${meters.length}">${meter.hdsMeterName}</span>
            </div>
        `;

        // Unit cell - empty text field for user input
        const unitCell = document.createElement('td');
        unitCell.innerHTML = `
            <input type="text" class="form-control form-control-sm meter-unit"
                   value="" placeholder="Enter unit (e.g., kWh, bar, °C)">
        `;

        // Parent meter cell
        const parentCell = document.createElement('td');
        if (window.parentOptions && window.parentOptions.length > 0) {
            let parentSelectHtml = `<select class="form-select form-select-sm meter-parent">`;
            window.parentOptions.forEach(option => {
                parentSelectHtml += `<option value="${option.value || option.Value}">${option.text || option.Text}</option>`;
            });
            parentSelectHtml += `</select>`;
            parentCell.innerHTML = parentSelectHtml;
        } else {
            parentCell.innerHTML = `
                <select class="form-select form-select-sm meter-parent">
                    <option value="">No parent meters found</option>
                </select>
                <small class="text-muted">No existing meters in database</small>
            `;
        }

        // Type cell
        const typeCell = document.createElement('td');
        typeCell.innerHTML = `
            <select class="form-select form-select-sm meter-type">
                <option value="main" selected>Main</option>
                <option value="sub">Sub</option>
            </select>
        `;

        // Active cell
        const activeCell = document.createElement('td');
        activeCell.className = 'text-center';
        activeCell.innerHTML = `
            <div class="form-check d-flex justify-content-center">
                <input class="form-check-input meter-active" type="checkbox"
                       id="active_${index}" checked>
            </div>
        `;

        // Append cells to row
        row.appendChild(checkboxCell);
        row.appendChild(nameCell);
        row.appendChild(unitCell);
        row.appendChild(parentCell);
        row.appendChild(typeCell);
        row.appendChild(activeCell);

        // Append row to table
        tbody.appendChild(row);
    });

    console.log(`🟨 Table rendered with ${meters.length} VAREXP meters`);
}

/**
 * Handle VAREXP import functionality
 */
function handleVarexpImport() {
    console.log('🟨 VAREXP Import button clicked');

    const selectedCheckboxes = document.querySelectorAll('.meter-checkbox:checked');
    if (selectedCheckboxes.length === 0) {
        alert('Please select at least one meter to import.');
        return;
    }

    const confirmMessage = `Are you sure you want to import ${selectedCheckboxes.length} meters from VAREXP into the database?`;
    if (!confirm(confirmMessage)) {
        return;
    }

    const importBtn = document.getElementById('importSelectedBtn');
    const originalBtnText = importBtn.textContent;
    importBtn.disabled = true;
    importBtn.innerHTML = '<span class="spinner-border spinner-border-sm" role="status" aria-hidden="true"></span> Importing...';

    const skipExisting = document.getElementById('skipExisting')?.checked || true;
    const updateExisting = document.getElementById('updateExisting')?.checked || false;
    const createMissingParents = document.getElementById('createMissingParents')?.checked || false;

    const meters = [];
    selectedCheckboxes.forEach((checkbox, index) => {
        const row = checkbox.closest('tr');

        if (row.classList.contains('table-info')) {
            return;
        }

        const nameCell = row.querySelector('td:nth-child(2)');
        const meterNameSpan = nameCell.querySelector('span:not(.badge)');
        const meterName = meterNameSpan ? meterNameSpan.textContent.trim() : '';

        if (!meterName) {
            return;
        }

        const meterUnit = row.querySelector('.meter-unit')?.value || '';
        const meterType = row.querySelector('.meter-type')?.value || 'Main';
        const meterParent = row.querySelector('.meter-parent')?.value || '';
        const meterActive = row.querySelector('.meter-active')?.checked || true;

        meters.push({
            meterName: meterName,
            unit: meterUnit,
            type: meterType,
            parentMeterId: meterParent,
            active: meterActive
        });
    });

    if (meters.length === 0) {
        alert('No valid meters found to import.');
        importBtn.disabled = false;
        importBtn.textContent = originalBtnText;
        return;
    }

    console.log(`🟨 Prepared ${meters.length} meters for import`);

    fetch('/Import/ImportVarexpMeters', {
        method: 'POST',
        headers: {
            'Content-Type': 'application/json',
        },
        body: JSON.stringify({
            meters: meters,
            skipExisting: skipExisting,
            updateExisting: updateExisting,
            createMissingParents: createMissingParents
        })
    })
        .then(response => {
            if (!response.ok) {
                throw new Error(`HTTP error! status: ${response.status}`);
            }
            return response.json();
        })
        .then(data => {
            console.log('🟨 Import response:', data);

            importBtn.disabled = false;
            importBtn.textContent = originalBtnText;

            showVarexpImportResults(data);

            if (data.success && (data.importedCount > 0 || data.updatedCount > 0)) {
                setTimeout(() => {
                    if (confirm('Import completed successfully! Would you like to reload the page to see the imported meters in the meter management section?')) {
                        window.location.href = '/Meter/Management';
                    }
                }, 2000);
            }
        })
        .catch(error => {
            console.error('🟨 Import error:', error);

            importBtn.disabled = false;
            importBtn.textContent = originalBtnText;

            alert(`Error importing meters: ${error.message}`);
        });
}

/**
 * Show detailed import results
 */
function showVarexpImportResults(data) {
    let message = '';

    if (data.success) {
        message = `✅ VAREXP Import Successful!\n\n`;
        message += `📊 Summary:\n`;
        message += `• ${data.importedCount} meters imported\n`;
        message += `• ${data.updatedCount} meters updated\n`;
        if (data.skippedCount > 0) {
            message += `• ${data.skippedCount} meters skipped\n`;
        }
        message += `• Total processed: ${data.totalProcessed}\n\n`;

        if (data.importedCount > 0) {
            message += `New meters have been added to your database and can be viewed in the Meter Management section.`;
        }
    } else {
        message = `❌ VAREXP Import Completed with Errors\n\n`;
        message += `📊 Summary:\n`;
        message += `• ${data.importedCount || 0} meters imported\n`;
        message += `• ${data.updatedCount || 0} meters updated\n`;
        if (data.skippedCount > 0) {
            message += `• ${data.skippedCount} meters skipped\n`;
        }
        message += `• ${data.errorCount} errors encountered\n\n`;

        if (data.detailedErrors && Object.keys(data.detailedErrors).length > 0) {
            message += `🔍 Detailed Errors:\n`;
            for (const [meterName, errorMsg] of Object.entries(data.detailedErrors)) {
                message += `• ${meterName}: ${errorMsg}\n`;
            }
        }
    }

    alert(message);
}