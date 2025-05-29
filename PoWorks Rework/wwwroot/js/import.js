// wwwroot/js/import.js

// Initialize the page
document.addEventListener('DOMContentLoaded', function () {
    // Load HDS tables
    loadHdsTables();

    // Set default dates (last 30 days)
    const today = new Date();
    const thirtyDaysAgo = new Date();
    thirtyDaysAgo.setDate(today.getDate() - 30);
    document.getElementById('startDate').valueAsDate = thirtyDaysAgo;
    document.getElementById('endDate').valueAsDate = today;

    // Attach event listeners for HDS & CSV
    document.getElementById('loadMetersBtn').addEventListener('click', loadMetersFromHds);
    document.getElementById('selectAllMetersBtn').addEventListener('click', selectAllMeters);
    document.getElementById('deselectAllMetersBtn').addEventListener('click', deselectAllMeters);
    document.getElementById('meterFilterInput').addEventListener('input', filterMeters);
    document.getElementById('printSelectedBtn').addEventListener('click', printSelectedMeters);
    document.getElementById('importSelectedBtn').addEventListener('click', importSelectedMeters);
    document.getElementById('parseCsvBtn').addEventListener('click', parseCsvFile);
    document.getElementById('exportBtn').addEventListener('click', exportMeters);

    //#############################################################################
    // VAREXP Import functions

    const parseVarexpBtn = document.getElementById('parseVarexpBtn');
    const varexpFileInput = document.getElementById('varexpFileInput');
    const recordsContainer = document.getElementById('varexpRecordsContainer');

    console.log('VAREXP setup:', parseVarexpBtn, varexpFileInput);

    if (parseVarexpBtn && varexpFileInput && recordsContainer) {
        parseVarexpBtn.addEventListener('click', () => {
            const file = varexpFileInput.files[0];
            if (!file) {
                return alert('Please select a VAREXP.DAT file first');
            }

            // Anti-forgery token
            const tokenEl = document.querySelector('input[name="__RequestVerificationToken"]');
            const token = tokenEl ? tokenEl.value : '';

            const formData = new FormData();
            formData.append('VarexpFile', file);

            fetch('/Import/ParseVarexp', {
                method: 'POST',
                credentials: 'same-origin',
                headers: { 'RequestVerificationToken': token },
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
                    // extract error message from JSON or text
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
                        renderVarexpRecords(data.records);
                    } else {
                        alert(data.error || 'No records returned');
                    }
                })
                .catch(err => {
                    console.error('VAREXP import failed:', err);
                    alert(`VAREXP import failed:\n${err.message}`);
                });
        });
    }
});

// Load HDS tables from the server
function loadHdsTables() {
    fetch('/Import/GetHdsTables')
        .then(response => response.json())
        .then(data => {
            const select = document.getElementById('hdsTable');
            select.innerHTML = '<option value="">Select a table...</option>';
            if (data.success && data.tables) {
                data.tables.forEach(table => {
                    const opt = document.createElement('option');
                    opt.value = table;
                    opt.textContent = table;
                    select.appendChild(opt);
                });
            }
        })
        .catch(err => console.error('Error loading HDS tables:', err));
}

// Load meters logic (unchanged)
function loadMetersFromHds() {
    const tableName = document.getElementById('hdsTable').value;
    if (!tableName) {
        alert('Please select an HDS table first');
        return;
    }

    const startDate = document.getElementById('startDate').value;
    const endDate = document.getElementById('endDate').value;
    let limit = parseInt(document.getElementById('limit').value) || 1000;

    // Validate and sanitize limit
    if (limit <= 0) {
        limit = 1000;
        document.getElementById('limit').value = limit;
        alert('Limit must be greater than 0. Setting to default value of 1000.');
    }

    if (limit > 10000) {
        limit = 10000;
        document.getElementById('limit').value = limit;
        alert('Limit reduced to maximum allowed value of 10,000 for performance reasons.');
    }

    console.log(`Loading meters with limit: ${limit} from table: ${tableName}`);

    // Show meter selection section and set loading state
    document.getElementById('meterSelectionSection').classList.remove('d-none');
    document.getElementById('metersTableBody').innerHTML = `
                        <tr>
                            <td colspan="6" class="text-center">
                                <div class="spinner-border spinner-border-sm text-primary" role="status">
                                    <span class="visually-hidden">Loading...</span>
                                </div>
                                Loading meters from '${tableName}' (limit: ${limit})...
                            </td>
                        </tr>`;

    // Build query parameters
    const params = new URLSearchParams({
        tableName: tableName,
        limit: limit.toString()
    });

    if (startDate) params.append('startDate', startDate);
    if (endDate) params.append('endDate', endDate);

    // Update the button to show loading state
    const loadBtn = document.getElementById('loadMetersBtn');
    const originalText = loadBtn.innerHTML;
    loadBtn.innerHTML = '<span class="spinner-border spinner-border-sm" role="status" aria-hidden="true"></span> Loading...';
    loadBtn.disabled = true;

    // Fetch meters from the server
    fetch(`/Import/GetMetersFromTable?${params}`)
        .then(response => {
            console.log(`Response status: ${response.status}`);
            if (!response.ok) {
                throw new Error(`HTTP ${response.status}: ${response.statusText}`);
            }
            return response.json();
        })
        .then(data => {
            console.log('Server response:', data);

            // Reset button
            loadBtn.innerHTML = originalText;
            loadBtn.disabled = false;

            if (data.success && data.meters) {
                // Store parent options for later use
                window.parentOptions = data.parentOptions || [];

                // Show information about the results
                console.log(`Retrieved ${data.actualCount} meters (requested limit: ${data.requestedLimit})`);

                // Populate table with meters
                renderMetersTable(data.meters);

                // Update status with comprehensive information
                let statusMsg = `Loaded ${data.actualCount} meters`;
                if (data.actualCount === data.requestedLimit) {
                    statusMsg += ` (limit reached - more may be available)`;

                    // Show info alert if limit was reached
                    setTimeout(() => {
                        if (confirm(`Loaded ${data.actualCount} meters (limit reached). There may be more meters available. Would you like to increase the limit and reload?`)) {
                            // Suggest doubling the limit
                            const newLimit = Math.min(data.requestedLimit * 2, 10000);
                            document.getElementById('limit').value = newLimit;
                            // Don't auto-reload, let user decide
                        }
                    }, 1000);
                } else if (data.actualCount < data.requestedLimit) {
                    statusMsg += ` (all available meters loaded)`;
                }

                document.getElementById('meterFilterStatus').textContent = statusMsg;

            } else {
                // Handle server-side errors
                console.error('Server returned error:', data);

                let errorMsg = 'Error loading meters';
                if (data.error) {
                    errorMsg += `: ${data.error}`;
                }

                // Show detailed error information
                let errorDetails = '';
                if (data.sqlErrorNumber) {
                    errorDetails = `SQL Error ${data.sqlErrorNumber}`;
                }
                if (data.details) {
                    console.error('Error details:', data.details);
                }

                document.getElementById('metersTableBody').innerHTML = `
                                    <tr>
                                        <td colspan="6" class="text-center text-danger">
                                            <div class="alert alert-danger mb-0">
                                                <strong>Error:</strong> ${errorMsg}
                                                ${errorDetails ? `<br><small>${errorDetails}</small>` : ''}
                                                <br><small>Check the console for more details or try a different table.</small>
                                            </div>
                                        </td>
                                    </tr>`;
            }
        })
        .catch(error => {
            console.error('Network or parsing error:', error);

            // Reset button
            loadBtn.innerHTML = originalText;
            loadBtn.disabled = false;

            // Show user-friendly error message
            let errorMsg = 'Failed to load meters';
            if (error.message.includes('HTTP 500')) {
                errorMsg = 'Server error - please check the table name and try again';
            } else if (error.message.includes('HTTP 404')) {
                errorMsg = 'Service not found - please check your connection';
            } else if (error.message.includes('NetworkError') || error.message.includes('fetch')) {
                errorMsg = 'Network error - please check your connection';
            } else {
                errorMsg += `: ${error.message}`;
            }

            document.getElementById('metersTableBody').innerHTML = `
                                <tr>
                                    <td colspan="6" class="text-center text-danger">
                                        <div class="alert alert-danger mb-0">
                                            <strong>Connection Error:</strong> ${errorMsg}
                                            <br><small>Please check your network connection and try again.</small>
                                        </div>
                                    </td>
                                </tr>`;
        });
}

// Render meters table
function renderMetersTable(meters) {
    const tbody = document.getElementById('metersTableBody');
    tbody.innerHTML = '';

    if (!meters || meters.length === 0) {
        tbody.innerHTML = '<tr><td colspan="6" class="text-center">No meters found</td></tr>';
        return;
    }

    // Add a header row with limit information if we have the maximum number of meters
    const limit = parseInt(document.getElementById('limit').value) || 1000;
    if (meters.length === limit) {
        const headerRow = document.createElement('tr');
        headerRow.className = 'table-warning';
        headerRow.innerHTML = `
                            <td colspan="6" class="text-center">
                                <small><strong>Note:</strong> Showing ${meters.length} meters (limit reached).
                                There may be more meters available. Increase the limit to see more.</small>
                            </td>
                        `;
        tbody.appendChild(headerRow);
    }

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

        // Meter name cell
        const nameCell = document.createElement('td');
        nameCell.textContent = meter.hdsMeterName;
        nameCell.title = `Meter ${index + 1} of ${meters.length}`;

        // Unit cell
        const unitCell = document.createElement('td');
        unitCell.innerHTML = `
                            <input type="text" class="form-control form-control-sm meter-unit"
                                   value="${meter.unit || ''}" placeholder="Unit">
                        `;

        // Parent meter cell
        const parentCell = document.createElement('td');
        let parentOptionsHtml = '<option value="">None</option>';

        if (window.parentOptions && window.parentOptions.length > 0) {
            window.parentOptions.forEach(option => {
                const selected = option.value === meter.parentMeterId ? 'selected' : '';
                parentOptionsHtml += `<option value="${option.value}" ${selected}>${option.text}</option>`;
            });
        }

        parentCell.innerHTML = `
                            <select class="form-select form-select-sm meter-parent">
                                ${parentOptionsHtml}
                            </select>
                        `;

        // Type cell
        const typeCell = document.createElement('td');
        const isSubType = (meter.type || '').toLowerCase() === 'sub';
        typeCell.innerHTML = `
                            <select class="form-select form-select-sm meter-type">
                                <option value="main" ${!isSubType ? 'selected' : ''}>Main</option>
                                <option value="sub" ${isSubType ? 'selected' : ''}>Sub</option>
                            </select>
                        `;

        // Active cell
        const activeCell = document.createElement('td');
        activeCell.className = 'text-center';
        activeCell.innerHTML = `
                            <div class="form-check d-flex justify-content-center">
                                <input class="form-check-input meter-active" type="checkbox"
                                       id="active_${index}" ${meter.active ? 'checked' : ''}>
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

    // Update meter count
    updateMeterCount();
}

// Meter utils: update count, select/deselect, filter, print, import
function updateMeterCount() {
    const total = document.querySelectorAll('.meter-checkbox').length;
    const selected = document.querySelectorAll('.meter-checkbox:checked').length;
    const limit = parseInt(document.getElementById('limit').value) || 1000;

    // Create a comprehensive status message
    let statusMessage = `Selected ${selected} of ${total} meters`;

    if (total === limit) {
        statusMessage += ` (limit reached - may be more available)`;
    } else if (total < limit) {
        statusMessage += ` (${limit - total} under limit)`;
    }

    // Update the status display
    const statusElement = document.getElementById('meterFilterStatus');
    if (statusElement) {
        statusElement.textContent = statusMessage;

        // Add visual indicator if limit was reached
        if (total === limit) {
            statusElement.className = 'input-group-text text-warning';
            statusElement.title = 'Limit reached. Increase limit to see more meters.';
        } else {
            statusElement.className = 'input-group-text';
            statusElement.title = '';
        }
    }

    // Update import button text with selection info
    const importBtn = document.getElementById('importSelectedBtn');
    if (importBtn) {
        if (selected > 0) {
            importBtn.innerHTML = `<i class="bi bi-cloud-upload"></i> Import Selected (${selected})`;
        } else {
            importBtn.innerHTML = '<i class="bi bi-cloud-upload"></i> Import Selected';
        }
    }

    // Update print button text with selection info
    const printBtn = document.getElementById('printSelectedBtn');
    if (printBtn) {
        if (selected > 0) {
            printBtn.innerHTML = `<i class="bi bi-printer"></i> Print Selected (${selected})`;
        } else {
            printBtn.innerHTML = '<i class="bi bi-printer"></i> Print Selected';
        }
    }

    // Log the current state for debugging
    console.log(`Meter count updated: ${selected}/${total} selected, limit: ${limit}`);
}
function selectAllMeters() {
    const checkboxes = document.querySelectorAll('.meter-checkbox');
    checkboxes.forEach(checkbox => {
        checkbox.checked = true;
    });
    updateMeterCount();
}

// Deselect all meters
function deselectAllMeters() {
    const checkboxes = document.querySelectorAll('.meter-checkbox');
    checkboxes.forEach(checkbox => {
        checkbox.checked = false;
    });
    updateMeterCount();
}

// Filter meters by name
function filterMeters() {
    const filter = document.getElementById('meterFilterInput').value.toLowerCase();
    const rows = document.querySelectorAll('#metersTableBody tr');
    let visibleCount = 0;

    rows.forEach(row => {
        const meterName = row.cells[1].textContent.toLowerCase();

        if (meterName.includes(filter)) {
            row.style.display = '';
            visibleCount++;
        } else {
            row.style.display = 'none';
        }
    });

    // Update filter status
    const total = rows.length;
    document.getElementById('meterFilterStatus').textContent =
        `Showing ${visibleCount} of ${total} meters`;
}

// Print selected meters
function printSelectedMeters() {
    const selectedMeters = getSelectedMeters();

    if (selectedMeters.length === 0) {
        alert('Please select at least one meter to print');
        return;
    }

    // Extract data from selected meters
    const tableName = document.getElementById('hdsTable').value;
    const selectedMeterNames = [];
    const selectedMeterTypes = [];
    const selectedMeterUnits = [];

    // Separate the data into arrays
    selectedMeters.forEach(meter => {
        selectedMeterNames.push(meter.name);
        selectedMeterTypes.push(meter.type);
        selectedMeterUnits.push(meter.unit);
    });

    console.log('Printing meters with data:', {
        tableName,
        selectedMeterNames,
        selectedMeterTypes,
        selectedMeterUnits
    });

    // Send data to server
    fetch('/Import/PrintSelectedMeters', {
        method: 'POST',
        headers: {
            'Content-Type': 'application/json'
        },
        body: JSON.stringify({
            tableName,
            selectedMeterNames,
            selectedMeterTypes,
            selectedMeterUnits
        })
    })
        .then(response => response.json())
        .then(data => {
            if (data.success) {
                alert(`Successfully printed ${selectedMeters.length} meters (check terminal)`);
            } else {
                console.error('Error printing meters:', data.error);
                alert('Error printing meters');
            }
        })
        .catch(error => {
            console.error('Error printing meters:', error);
            alert('Error printing meters');
        });
}
function getSelectedMeters() {
    const selectedMeters = [];
    const checkboxes = document.querySelectorAll('.meter-checkbox:checked');

    console.log('Found checked checkboxes:', checkboxes.length);

    checkboxes.forEach(checkbox => {
        const row = checkbox.closest('tr');

        const meterData = {
            name: row.cells[1].textContent.trim(),
            unit: row.querySelector('.meter-unit').value,
            parentId: row.querySelector('.meter-parent').value,
            type: row.querySelector('.meter-type').value,
            active: row.querySelector('.meter-active').checked
        };

        console.log('Adding meter:', meterData);
        selectedMeters.push(meterData);
    });

    console.log('Total selected meters:', selectedMeters.length);
    return selectedMeters;
}

// Improved importSelectedMeters function
// Updated importSelectedMeters function with readings import support
function importSelectedMeters() {
    const selectedMeters = getSelectedMeters();

    if (selectedMeters.length === 0) {
        alert('Please select at least one meter to import');
        return;
    }

    // Get import options
    const tableName = document.getElementById('hdsTable').value;

    // Check if readings import is enabled
    const importReadingsCheckbox = document.getElementById('importReadings');
    const importReadings = importReadingsCheckbox ? importReadingsCheckbox.checked : false;

    console.log('=== IMPORT DEBUG INFO ===');
    console.log('importReadings checkbox found:', !!importReadingsCheckbox);
    console.log('importReadings checked:', importReadings);
    console.log('selectedMeters count:', selectedMeters.length);
    console.log('tableName:', tableName);

    // Show progress indicator
    const importBtn = document.getElementById('importSelectedBtn');
    importBtn.innerHTML = '<span class="spinner-border spinner-border-sm" role="status" aria-hidden="true"></span> Importing...';
    importBtn.disabled = true;

    console.log('Importing meters:', selectedMeters);

    // Format data for the server
    const meters = selectedMeters.map(meter => {
        return {
            hdsMeterName: meter.name,
            unit: meter.unit || "",
            parentMeterId: meter.parentId || "",
            type: meter.type.toLowerCase(),
            active: !!meter.active,
            lastReading: "0"
        };
    });

    console.log('Formatted meters for import:', meters);

    // Send import request to server
    fetch('/Import/ImportMeters', {
        method: 'POST',
        headers: {
            'Content-Type': 'application/json'
        },
        body: JSON.stringify({
            tableName: tableName,
            meters: meters,
            skipExisting: true,
            updateExisting: false,
            createMissingParents: false
        })
    })
        .then(response => {
            if (!response.ok) {
                throw new Error(`Server returned ${response.status}: ${response.statusText}`);
            }
            return response.json();
        })
        .then(data => {
            console.log('=== IMPORT METERS RESPONSE ===');
            console.log('data.success:', data.success);
            console.log('data.importedCount:', data.importedCount);
            console.log('data.updatedCount:', data.updatedCount);
            console.log('data.skippedCount:', data.skippedCount);

            // Reset button
            importBtn.innerHTML = '<i class="bi bi-cloud-upload"></i> Import Selected';
            importBtn.disabled = false;

            // Show result message for meter import
            let message = `Import completed:\n`;
            message += `- ${data.importedCount} meters imported\n`;
            message += `- ${data.updatedCount || 0} meters updated\n`;
            message += `- ${data.skippedCount || 0} meters skipped`;

            if (data.importedCount === 0 && data.updatedCount === 0 && data.skippedCount === 0) {
                message += `\n\nWarning: No meters were processed. Check server logs.`;
            }

            // Check if we should import readings
            console.log('=== CHECKING READINGS IMPORT CONDITIONS ===');
            console.log('data.success:', data.success);
            console.log('importReadings:', importReadings);
            console.log('Should import readings:', data.success && importReadings);

            if (data.success && importReadings) {
                console.log('*** CALLING IMPORT READINGS ***');

                // Get meter names for readings import
                const meterNames = selectedMeters.map(meter => meter.name);

                // Call the readings import function
                importMeterReadings(meterNames, tableName)
                    .then(readingsResult => {
                        // Update the message with readings import results
                        if (readingsResult.success) {
                            message += `\n\nReadings Import:\n`;
                            message += `- ${readingsResult.totalReadingsImported || 0} readings imported\n`;
                            message += `- ${readingsResult.totalMetersProcessed || 0} meters processed`;
                        } else {
                            message += `\n\nReadings Import Failed:\n`;
                            message += `- Error: ${readingsResult.error || 'Unknown error'}`;
                        }

                        alert(message);
                    })
                    .catch(error => {
                        console.error('Error importing readings:', error);
                        message += `\n\nReadings Import Failed:\n`;
                        message += `- Error: ${error.message}`;
                        alert(message);
                    });
            } else {
                console.log('*** NOT IMPORTING READINGS ***');
                console.log('Reason: success=' + data.success + ', importReadings=' + importReadings);
                alert(message);
            }
        })
        .catch(error => {
            console.error('Error importing meters:', error);

            // Reset button
            importBtn.innerHTML = '<i class="bi bi-cloud-upload"></i> Import Selected';
            importBtn.disabled = false;

            alert(`Error importing meters: ${error.message}`);
        });
}

// New function to handle readings import
function importMeterReadings(meterNames, tableName) {
    console.log('=== IMPORT METER READINGS FUNCTION ===');
    console.log('meterNames:', meterNames);
    console.log('tableName:', tableName);

    const requestBody = {
        tableName: tableName,
        meterNames: meterNames
    };

    console.log('Sending readings import request:', requestBody);

    return fetch('/Import/ImportMeterReadings', {
        method: 'POST',
        headers: {
            'Content-Type': 'application/json'
        },
        body: JSON.stringify(requestBody)
    })
        .then(response => {
            console.log('Readings response status:', response.status);
            if (!response.ok) {
                throw new Error(`Server returned ${response.status}: ${response.statusText}`);
            }
            return response.json();
        })
        .then(data => {
            console.log('Readings response data:', data);
            return data;
        });
}

// CSV placeholders
function parseCsvFile() {
    alert('CSV parsing functionality will be implemented soon');
}
function exportMeters() {
    alert('Export functionality will be implemented soon');
}

// Render VAREXP records as table
function renderVarexpRecords(records) {
    const container = document.getElementById('varexpRecordsContainer');
    if (!container) return console.error('No container');
    container.innerHTML = '';
    if (!records || records.length === 0) {
        container.textContent = 'No records found.';
        return;
    }

    // Exclude undesired rows
    const excludedPrefixes = [
        'DEFACTALABEL', 'DEFALMALABEL', 'DEFBITALABEL', 'LNSLCA',
        'OPCCONF', 'BACCONF', 'M104CONF', 'M61850CONF', 'MDNP3CONF', 'SNMPCONF'
    ];
    const filtered = records.filter(fld => {
        const a = fld[0]?.trim() || '';
        const b = fld[2]?.trim().toLowerCase() || '';
        if (excludedPrefixes.some(p => a.startsWith(p))) return false;
        if (b === 'system') return false;
        return true;
    });
    if (filtered.length === 0) {
        container.textContent = 'No records after filtering.';
        return;
    }

    // Build table
    const tbl = document.createElement('table');
    tbl.className = 'table table-bordered';
    const thr = tbl.createTHead().insertRow();
    thr.insertCell().outerHTML = '<th><input type="checkbox" id="selectAllVarexp"></th>';
    filtered[0].forEach((_, i) => {
        const th = document.createElement('th');
        th.textContent = `Field ${i}`;
        thr.appendChild(th);
    });
    const tb = tbl.createTBody();
    filtered.forEach((fields, idx) => {
        const tr = tb.insertRow();
        tr.insertCell().innerHTML = `
            <input type="checkbox" class="varexp-record-checkbox" data-index="${idx}">
        `;
        fields.forEach(val => {
            const cell = tr.insertCell();
            cell.textContent = val;
        });
    });

    // Wrap table in a horizontally scrollable container at the bottom of the grid
    const wrapper = document.createElement('div');
    wrapper.className = 'table-responsive';
    wrapper.style.marginTop = '1rem'; // spacing above wrapper
    wrapper.appendChild(tbl);
    container.appendChild(wrapper);

    // Select all functionality
    const selAll = document.getElementById('selectAllVarexp');
    if (selAll) {
        selAll.addEventListener('change', function () {
            document.querySelectorAll('.varexp-record-checkbox').forEach(cb => cb.checked = this.checked);
        });
    }
}

