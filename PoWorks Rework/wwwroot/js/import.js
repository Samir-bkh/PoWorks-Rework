// wwwroot/js/import.js - With VAREXP Print Functionality

// Initialize the page
document.addEventListener('DOMContentLoaded', function () {
    console.log('🔍 DEBUG: DOM Content Loaded');

    // VAREXP Import functions
    const parseVarexpBtn = document.getElementById('parseVarexpBtn');
    const varexpFileInput = document.getElementById('varexpFileInput');
    const recordsContainer = document.getElementById('varexpRecordsContainer');

    console.log('🔍 DEBUG: VAREXP Elements found:', {
        parseVarexpBtn: !!parseVarexpBtn,
        varexpFileInput: !!varexpFileInput,
        recordsContainer: !!recordsContainer
    });

    if (parseVarexpBtn && varexpFileInput && recordsContainer) {
        parseVarexpBtn.addEventListener('click', () => {
            console.log('🔍 DEBUG: Parse VAREXP button clicked');

            const file = varexpFileInput.files[0];
            if (!file) {
                console.log('🔍 DEBUG: No file selected');
                return alert('Please select a VAREXP.DAT file first');
            }

            console.log('🔍 DEBUG: File selected:', file.name);

            const formData = new FormData();
            formData.append('VarexpFile', file);

            console.log('🔍 DEBUG: Sending request to /Import/ParseVarexp');

            fetch('/Import/ParseVarexp', {
                method: 'POST',
                body: formData
            })
                .then(async res => {
                    console.log('🔍 DEBUG: Response status:', res.status);

                    const contentType = res.headers.get('content-type') || '';
                    console.log('🔍 DEBUG: Content type:', contentType);

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
                    console.log('🔍 DEBUG: Server response received:', data);
                    console.log('🔍 DEBUG: Full response structure:', JSON.stringify(data, null, 2)); // ✅ ADD THIS LINE

                    if (data.records) {
                        console.log('🔍 DEBUG: Records found:', data.records.length);
                        console.log('🔍 DEBUG: First 3 records:', data.records.slice(0, 3));

                        // ✅ ADD MORE DETAILED LOGGING
                        console.log('🔍 DEBUG: data.parentOptions exists?', !!data.parentOptions);
                        console.log('🔍 DEBUG: data.parentOptions type:', typeof data.parentOptions);
                        console.log('🔍 DEBUG: data.parentOptions content:', data.parentOptions);

                        if (data.parentOptions) {
                            console.log('🔍 DEBUG: Parent options received:', data.parentOptions.length);
                            window.parentOptions = data.parentOptions;
                        } else {
                            console.log('🔍 DEBUG: No parent options in response, using default');
                            window.parentOptions = [{ value: '', text: 'None' }];
                        }

                        // ✅ VERIFY WHAT'S STORED
                        console.log('🔍 DEBUG: window.parentOptions after setting:', window.parentOptions);

                        // Convert VAREXP records to meter format
                        debugConvertVarexpToMeterSelection(data.records);
                    } else {
                        console.log('🔍 DEBUG: No records in response');
                        alert(data.error || 'No records returned');
                    }
                })
        });
    } else {
        console.log('🔍 DEBUG: VAREXP elements not found!');
    }

    // Setup print button handler using event delegation
    document.body.addEventListener('click', function (event) {
        if (event.target.id === 'printSelectedBtn' || event.target.closest('#printSelectedBtn')) {
            console.log('🔍 DEBUG: Print Selected button clicked');
            handleVarexpPrint();
        }
    });
});

// Handle VAREXP print functionality
function handleVarexpPrint() {
    console.log('🔍 DEBUG: Starting VAREXP print process');

    // Get all selected checkboxes
    const selectedCheckboxes = document.querySelectorAll('.meter-checkbox:checked');

    if (selectedCheckboxes.length === 0) {
        alert('Please select at least one meter to print.');
        return;
    }

    console.log(`🔍 DEBUG: Found ${selectedCheckboxes.length} selected meters`);

    // Gather selected meter data
    const selectedMeterNames = [];
    const selectedMeterTypes = [];
    const selectedMeterUnits = [];

    selectedCheckboxes.forEach((checkbox, index) => {
        const row = checkbox.closest('tr');
        if (!row) return;

        // Get meter name from the name cell (skip the info header row)
        const nameCell = row.cells[1];
        if (!nameCell) return;

        // Extract meter name (skip badge, get the span text)
        const nameSpan = nameCell.querySelector('span:not(.badge)');
        const meterName = nameSpan ? nameSpan.textContent.trim() : '';

        if (!meterName) return;

        // Get unit from input field
        const unitInput = row.querySelector('.meter-unit');
        const unit = unitInput ? unitInput.value.trim() : '';

        // Get type from select
        const typeSelect = row.querySelector('.meter-type');
        const type = typeSelect ? typeSelect.value : 'main';

        console.log(`🔍 DEBUG: Meter ${index + 1}: Name="${meterName}", Type="${type}", Unit="${unit}"`);

        selectedMeterNames.push(meterName);
        selectedMeterTypes.push(type);
        selectedMeterUnits.push(unit);
    });

    if (selectedMeterNames.length === 0) {
        alert('No valid meters found in selection.');
        return;
    }

    console.log('🔍 DEBUG: Collected meter data:', {
        names: selectedMeterNames,
        types: selectedMeterTypes,
        units: selectedMeterUnits
    });

    // Prepare request data
    const requestData = {
        tableName: 'VAREXP.DAT',
        selectedMeterNames: selectedMeterNames,
        selectedMeterTypes: selectedMeterTypes,
        selectedMeterUnits: selectedMeterUnits
    };

    console.log('🔍 DEBUG: Sending print request:', requestData);

    // Send to server
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
            console.log('🔍 DEBUG: Print response:', data);

            if (data.success) {
                alert(`Successfully printed ${data.count} meters to console. Check the server console for details.`);
            } else {
                alert('Print failed: ' + (data.error || 'Unknown error'));
            }
        })
        .catch(error => {
            console.error('🔍 DEBUG: Print request failed:', error);
            alert('Print request failed: ' + error.message);
        });
}

// DEBUG: Convert VAREXP records to meter selection format
function debugConvertVarexpToMeterSelection(records) {
    console.log('🔍 DEBUG: Converting VAREXP records to meter selection');
    console.log('🔍 DEBUG: Total records received:', records.length);

    // Show first few records to understand structure
    console.log('🔍 DEBUG: First 5 records structure:');
    records.slice(0, 5).forEach((record, index) => {
        console.log(`Record ${index}:`, record);
    });

    // Filter out header rows and system records
    const meterRecords = records.filter((record, index) => {
        if (!record || record.length < 2) {
            console.log(`🔍 DEBUG: Filtered out record ${index}: insufficient fields`);
            return false;
        }

        const recordType = record[0]?.trim() || '';
        const combinedName = record[1]?.trim() || '';

        console.log(`🔍 DEBUG: Record ${index}: Type="${recordType}", Name="${combinedName}"`);

        // Skip header row
        if (combinedName === 'CombinedName' || recordType === 'Class') {
            console.log(`🔍 DEBUG: Filtered out record ${index}: header row`);
            return false;
        }

        // Skip system records
        if (combinedName.toLowerCase().startsWith('system')) {
            console.log(`🔍 DEBUG: Filtered out record ${index}: system record`);
            return false;
        }

        // Only include actual meter record types
        const validTypes = ['CHR', 'CMD', 'REG', 'TXT'];
        const isValidType = validTypes.includes(recordType.toUpperCase());

        if (!isValidType) {
            console.log(`🔍 DEBUG: Filtered out record ${index}: invalid type "${recordType}"`);
            return false;
        }

        if (!combinedName) {
            console.log(`🔍 DEBUG: Filtered out record ${index}: empty name`);
            return false;
        }

        console.log(`🔍 DEBUG: ✅ Keeping record ${index}: Type="${recordType}", Name="${combinedName}"`);
        return true;
    });

    console.log(`🔍 DEBUG: Filtered ${meterRecords.length} valid meter records from ${records.length} total records`);

    if (meterRecords.length === 0) {
        console.log('🔍 DEBUG: ❌ No valid meter records found!');
        alert('No valid meter records found in VAREXP.DAT file. Check console for details.');
        return;
    }

    // Convert to simplified meter format - ONLY extract the combined name
    const meters = meterRecords.map((record, index) => {
        const recordType = record[0]?.trim() || '';
        const combinedName = record[1]?.trim() || '';

        const meter = {
            hdsMeterName: combinedName,  // Only the combined name matters
            unit: '',                    // Empty, user can fill
            type: 'Main',               // Default to Main type
            active: true,               // Default to active
            isSelected: true,           // Default selected
            lastReading: '0',           // Default reading
            recordType: recordType      // Keep for display purposes
        };

        console.log(`🔍 DEBUG: Converted meter ${index}:`, meter);
        return meter;
    });

    console.log('🔍 DEBUG: ✅ Converted meters:', meters.length);
    console.log('🔍 DEBUG: Sample meter names:', meters.slice(0, 5).map(m => m.hdsMeterName));

    // Show the meter selection section and populate with VAREXP data
    debugShowMeterSelectionForVarexp(meters);
}

function debugShowMeterSelectionForVarexp(meters) {
    console.log('🔍 DEBUG: Showing meter selection for VAREXP data');
    console.log('🔍 DEBUG: Available parent options:', window.parentOptions?.length || 0);

    // Hide the VAREXP records container
    const recordsContainer = document.getElementById('varexpRecordsContainer');
    if (recordsContainer) {
        recordsContainer.innerHTML = '';
        console.log('🔍 DEBUG: Cleared VAREXP records container');
    }

    // Show meter selection section
    const meterSelectionSection = document.getElementById('meterSelectionSection');
    if (meterSelectionSection) {
        meterSelectionSection.classList.remove('d-none');
        console.log('🔍 DEBUG: Meter selection section shown');
    } else {
        console.log('🔍 DEBUG: ❌ Meter selection section not found!');
        return;
    }

    // Update the section title to indicate VAREXP source
    const sectionHeader = meterSelectionSection.querySelector('.card-header h5');
    if (sectionHeader) {
        sectionHeader.textContent = 'VAREXP Meter Selection';
        console.log('🔍 DEBUG: Updated section header');
    }

    // Hide the "Import historical readings" checkbox for VAREXP
    const importReadingsCheckbox = document.getElementById('importReadings');
    if (importReadingsCheckbox) {
        const checkboxContainer = importReadingsCheckbox.closest('.form-check');
        if (checkboxContainer) {
            checkboxContainer.style.display = 'none';
            console.log('🔍 DEBUG: Hidden import readings checkbox');
        }
    }

    // Render the table with parent options already available
    debugRenderVarexpMetersTable(meters);

    // Update status
    const statusElement = document.getElementById('meterFilterStatus');
    if (statusElement) {
        statusElement.textContent = `Loaded ${meters.length} meters from VAREXP.DAT`;
        console.log('🔍 DEBUG: Updated filter status');
    }

    console.log('🔍 DEBUG: ✅ Meter selection setup complete');
}

// DEBUG: Render VAREXP meters in the table
function debugRenderVarexpMetersTable(meters) {
    console.log('🔍 DEBUG: Rendering VAREXP meters table');

    const tbody = document.getElementById('metersTableBody');
    if (!tbody) {
        console.log('🔍 DEBUG: ❌ Table body not found!');
        return;
    }

    tbody.innerHTML = '';
    console.log('🔍 DEBUG: Cleared table body');

    if (!meters || meters.length === 0) {
        tbody.innerHTML = '<tr><td colspan="6" class="text-center">No meters found in VAREXP.DAT</td></tr>';
        console.log('🔍 DEBUG: No meters to display');
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
    console.log('🔍 DEBUG: Added header row');

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

        // Meter name cell - ONLY show the combined name
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

        // Parent meter cell - NOW ENABLED with options from database
        const parentCell = document.createElement('td');

        // ✅ ADD DETAILED LOGGING
        console.log(`🔍 DEBUG: Creating parent cell for meter ${index}`);
        console.log('🔍 DEBUG: window.parentOptions available?', !!window.parentOptions);
        console.log('🔍 DEBUG: window.parentOptions length:', window.parentOptions?.length || 0);

        // Check if we have parent options available
        if (window.parentOptions && window.parentOptions.length > 0) {
            let parentSelectHtml = `<select class="form-select form-select-sm meter-parent">`;

            // Add options from the stored parent options
            window.parentOptions.forEach(option => {
                parentSelectHtml += `<option value="${option.value || option.Value}">${option.text || option.Text}</option>`;
            });

            parentSelectHtml += `</select>`;
            parentCell.innerHTML = parentSelectHtml;

            console.log(`🔍 DEBUG: Created parent dropdown with ${window.parentOptions.length} options for meter ${index}`);
        } else {
            // ✅ BETTER FALLBACK MESSAGE
            console.log(`🔍 DEBUG: No parent options available for meter ${index}, using fallback`);
            parentCell.innerHTML = `
        <select class="form-select form-select-sm meter-parent">
            <option value="">No parent meters found</option>
        </select>
        <small class="text-muted">No existing meters in database</small>
    `;
        }

        // Type cell - allow user selection
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

        if (index < 3) {
            console.log(`🔍 DEBUG: Added row ${index} for meter: ${meter.hdsMeterName}`);
        }
    });

    console.log(`🔍 DEBUG: ✅ Table rendered with ${meters.length} meters`);
}