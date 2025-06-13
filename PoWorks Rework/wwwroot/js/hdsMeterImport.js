/**
 * hdsMeterImport.js - Clean HDS meter import functionality
 * Handles HDS meter selection, import, and print functionality
 */

// =====================================================
// INITIALIZATION & SETUP
// =====================================================

document.addEventListener('DOMContentLoaded', function () {
    console.log('HDS Meter Import JS loaded');

    initializeEventHandlers();
    loadSqlServerConnections();
    setupConnectionEventListeners();
});

function initializeEventHandlers() {
    // Unified event delegation for all HDS functionality
    document.body.addEventListener('click', function (event) {
        // Load Meters Button
        if (event.target.id === 'loadMetersBtn' || event.target.closest('#loadMetersBtn')) {
            console.log('Load Meters button clicked');
            handleLoadMeters(event);
        }

        // Select All / Deselect All buttons (unified for both HDS and VAREXP)
        if (event.target.id === 'selectAllMetersBtn' || event.target.closest('#selectAllMetersBtn')) {
            console.log('Select All button clicked');
            handleSelectAll();
        }

        if (event.target.id === 'deselectAllMetersBtn' || event.target.closest('#deselectAllMetersBtn')) {
            console.log('Deselect All button clicked');
            handleDeselectAll();
        }

        // Import Selected button
        if (event.target.id === 'importSelectedBtn' || event.target.closest('#importSelectedBtn')) {
            console.log('Import Selected button clicked');
            handleImport();
        }

        // 🎯 UNIFIED PRINT BUTTON HANDLER
        if (event.target.id === 'printSelectedBtn' || event.target.closest('#printSelectedBtn')) {
            console.log('🎯 Unified Print Button clicked');
            handleUnifiedPrint();
        }
    });

    // Change event delegation
    document.body.addEventListener('change', function (event) {
        if (event.target.id === 'hdsConnection') {
            console.log('Connection selection changed');
            handleConnectionChange();
        }

        if (event.target.id === 'hdsTable') {
            console.log('Table selection changed');
            handleTableSelection();
        }

        if (event.target.classList.contains('meter-checkbox')) {
            handleCheckboxChange(event.target);
        }
    });

    // Input event delegation
    document.body.addEventListener('input', function (event) {
        if (event.target.id === 'meterFilterInput') {
            console.log('Filter input changed');
            handleFilterInput(event);
        }
    });
}

// =====================================================
// CONNECTION & TABLE MANAGEMENT
// =====================================================

function loadSqlServerConnections() {
    console.log('Loading SQL Server connections...');

    fetch('/Import/GetSqlServerConnections')
        .then(response => {
            if (!response.ok) {
                throw new Error(`Failed to load connections: ${response.statusText}`);
            }
            return response.json();
        })
        .then(data => {
            if (data.success) {
                populateConnectionDropdown(data.connections);
            } else {
                console.error('Failed to load connections:', data.error);
                updateConnectionStatus('error', data.error || 'Failed to load connections');
            }
        })
        .catch(error => {
            console.error('Error loading connections:', error);
            updateConnectionStatus('error', 'Error loading connections');
        });
}

function populateConnectionDropdown(connections) {
    const connectionSelect = document.getElementById('hdsConnection');
    if (!connectionSelect) return;

    connectionSelect.innerHTML = '<option value="">Select a connection...</option>';

    connections.forEach(connection => {
        const option = document.createElement('option');
        option.value = connection.connectionId;
        option.textContent = connection.connectionName || `Connection ${connection.connectionId}`;
        if (connection.isDefault) {
            option.textContent += ' [Default]';
        }
        connectionSelect.appendChild(option);
    });

    // Auto-select default connection
    const defaultConnection = connections.find(c => c.isDefault);
    if (defaultConnection) {
        connectionSelect.value = defaultConnection.connectionId;
        handleConnectionChange();
    }

    updateConnectionStatus('success', `${connections.length} connections available`);
}

function setupConnectionEventListeners() {
    const connectionSelect = document.getElementById('hdsConnection');
    if (connectionSelect) {
        connectionSelect.removeEventListener('change', handleConnectionChange);
        connectionSelect.addEventListener('change', handleConnectionChange);
        console.log('Connection change listener added');
    }
}

function handleConnectionChange() {
    const connectionSelect = document.getElementById('hdsConnection');
    const tableSelect = document.getElementById('hdsTable');
    const loadButton = document.getElementById('loadMetersBtn');

    if (!connectionSelect || !tableSelect || !loadButton) {
        console.error('Required elements not found');
        return;
    }

    const selectedConnectionId = connectionSelect.value;
    console.log('Connection changed to:', selectedConnectionId);

    if (!selectedConnectionId) {
        tableSelect.disabled = true;
        tableSelect.innerHTML = '<option value="">Select a table...</option>';
        loadButton.disabled = true;
        updateConnectionStatus('info', 'Select a connection to continue');
        return;
    }

    updateConnectionStatus('loading', 'Loading tables...');
    loadTablesForConnection(selectedConnectionId);
}

function loadTablesForConnection(connectionId) {
    console.log('Loading tables for connection:', connectionId);

    const tableSelect = document.getElementById('hdsTable');
    const loadButton = document.getElementById('loadMetersBtn');

    if (!connectionId) {
        console.error('No connection ID provided');
        updateConnectionStatus('error', 'No connection selected');
        return;
    }

    if (tableSelect) {
        tableSelect.disabled = true;
        tableSelect.innerHTML = '<option value="">Loading tables...</option>';
    }
    if (loadButton) {
        loadButton.disabled = true;
    }

    fetch(`/HdsImport/GetTables?connectionId=${encodeURIComponent(connectionId)}`)
        .then(response => {
            console.log('Tables response status:', response.status);
            if (!response.ok) {
                throw new Error(`Failed to load tables: ${response.status} ${response.statusText}`);
            }
            return response.json();
        })
        .then(data => {
            console.log('Tables response data:', data);

            if (data.success) {
                populateTableDropdown(data.tables);
                updateConnectionStatus('success', `${data.tables.length} tables available`);
            } else {
                console.error('Failed to load tables:', data.error);
                updateConnectionStatus('error', data.error || 'Failed to load tables');
                if (tableSelect) {
                    tableSelect.innerHTML = '<option value="">Failed to load tables</option>';
                    tableSelect.disabled = true;
                }
            }
        })
        .catch(error => {
            console.error('Error loading tables:', error);
            updateConnectionStatus('error', `Error loading tables: ${error.message}`);
            if (tableSelect) {
                tableSelect.innerHTML = '<option value="">Error loading tables</option>';
                tableSelect.disabled = true;
            }
        });
}

function populateTableDropdown(tables) {
    const tableSelect = document.getElementById('hdsTable');
    const loadButton = document.getElementById('loadMetersBtn');

    if (!tableSelect) {
        console.error('Table select element not found');
        return;
    }

    tableSelect.innerHTML = '<option value="">Select a table...</option>';

    if (!tables || tables.length === 0) {
        tableSelect.innerHTML = '<option value="">No tables found</option>';
        tableSelect.disabled = true;
        if (loadButton) loadButton.disabled = true;
        return;
    }

    tables.forEach(tableName => {
        const option = document.createElement('option');
        option.value = tableName;
        option.textContent = tableName;
        tableSelect.appendChild(option);
    });

    tableSelect.disabled = false;
    tableSelect.removeEventListener('change', handleTableSelection);
    tableSelect.addEventListener('change', handleTableSelection);
}

function handleTableSelection() {
    const tableSelect = document.getElementById('hdsTable');
    const loadButton = document.getElementById('loadMetersBtn');

    if (loadButton) {
        loadButton.disabled = !tableSelect.value;
    }
}

function updateConnectionStatus(type, message) {
    const statusElement = document.getElementById('connectionStatus');
    if (!statusElement) return;

    let icon = 'bi-info-circle';
    let className = 'text-muted';

    switch (type) {
        case 'loading':
            icon = 'bi-arrow-clockwise';
            className = 'text-primary';
            break;
        case 'success':
            icon = 'bi-check-circle';
            className = 'text-success';
            break;
        case 'error':
            icon = 'bi-exclamation-triangle';
            className = 'text-danger';
            break;
    }

    statusElement.innerHTML = `<i class="${icon}"></i> ${message}`;
    statusElement.className = className;
}

// =====================================================
// METER LOADING & TABLE POPULATION
// =====================================================

function handleLoadMeters(event) {
    console.log('Load Meters button clicked');

    const button = event.target.closest('#loadMetersBtn') || event.target;
    const connectionSelect = document.getElementById('hdsConnection');
    const tableSelect = document.getElementById('hdsTable');
    const startDateInput = document.getElementById('startDate');
    const endDateInput = document.getElementById('endDate');
    const limitInput = document.getElementById('limit');

    if (!connectionSelect || !tableSelect) {
        console.error('Required elements not found');
        alert('Required form elements not found');
        return;
    }

    const connectionId = connectionSelect.value;
    const tableName = tableSelect.value;
    const startDate = startDateInput ? startDateInput.value : null;
    const endDate = endDateInput ? endDateInput.value : null;
    const limit = limitInput ? limitInput.value : 1000;

    console.log('Load Meters Parameters:', {
        connectionId,
        tableName,
        startDate,
        endDate,
        limit
    });

    if (!connectionId) {
        alert('Please select a connection first.');
        return;
    }

    if (!tableName) {
        alert('Please select a table to load meters from.');
        return;
    }

    // Show loading indicator
    const originalButtonContent = button.innerHTML;
    button.innerHTML = '<span class="spinner-border spinner-border-sm" role="status" aria-hidden="true"></span> Loading...';
    button.disabled = true;

    // Build query parameters
    let queryParams = `tableName=${encodeURIComponent(tableName)}&connectionId=${encodeURIComponent(connectionId)}`;
    if (startDate) queryParams += `&startDate=${encodeURIComponent(startDate)}`;
    if (endDate) queryParams += `&endDate=${encodeURIComponent(endDate)}`;
    if (limit) queryParams += `&limit=${encodeURIComponent(limit)}`;

    console.log('Fetching meters with query:', queryParams);

    // Fetch meters from the selected table
    fetch(`/Import/GetMetersFromTable?${queryParams}`)
        .then(response => {
            console.log('Response status:', response.status);
            if (!response.ok) {
                throw new Error(`Failed to load meters: ${response.status} ${response.statusText}`);
            }
            return response.json();
        })
        .then(data => {
            console.log('Received JSON response:', data);

            if (!data.success) {
                throw new Error(data.error || 'Failed to load meters');
            }

            // Store parent options globally
            window.parentOptions = data.parentOptions || [];

            // Show the meter selection table
            showHDSMeterSelection(data.meters, tableName, connectionId);

            // Reset button
            button.innerHTML = originalButtonContent;
            button.disabled = false;

            console.log('Load Meters completed successfully');
        })
        .catch(error => {
            console.error('Error loading meters:', error);
            alert(`Error loading meters: ${error.message}`);

            button.innerHTML = originalButtonContent;
            button.disabled = false;
        });
}

function showHDSMeterSelection(meters, tableName, connectionId) {
    console.log('Showing HDS meter selection:', {
        meterCount: meters.length,
        tableName,
        connectionId
    });

    // Show the meter selection section
    const meterSelectionSection = document.getElementById('meterSelectionSection');
    if (meterSelectionSection) {
        meterSelectionSection.classList.remove('d-none');
        console.log('Meter selection section shown');
    } else {
        console.error('Meter selection section not found!');
        return;
    }

    // Update section title
    const sectionHeader = meterSelectionSection.querySelector('.card-header h5');
    if (sectionHeader) {
        sectionHeader.textContent = `HDS Meter Selection - ${tableName}`;
        console.log('Updated section header');
    }

    // 🎯 SET DATA TYPE FLAG FOR UNIFIED PRINT BUTTON
    window.currentMeterDataType = 'HDS';
    window.currentHDSContext = {
        tableName: tableName,
        connectionId: connectionId
    };
    console.log('🔵 Set data type to HDS');

    // Show the "Import historical readings" checkbox for HDS
    const importReadingsCheckbox = document.getElementById('importReadings');
    if (importReadingsCheckbox) {
        const checkboxContainer = importReadingsCheckbox.closest('.form-check');
        if (checkboxContainer) {
            checkboxContainer.style.display = 'block';
            console.log('Shown import readings checkbox');
        }
    }

    // Populate the table with HDS meters
    populateHDSMetersTable(meters);

    // 🎯 UPDATE PRINT BUTTON FOR HDS
    updatePrintButtonForDataType('HDS');

    // Update status
    const statusElement = document.getElementById('meterFilterStatus');
    if (statusElement) {
        statusElement.textContent = `Loaded ${meters.length} meters from ${tableName}`;
        console.log('Updated filter status');
    }

    console.log('HDS meter selection setup complete');
}

function populateHDSMetersTable(meters) {
    console.log('Populating HDS meters table with', meters.length, 'meters');

    const tbody = document.getElementById('metersTableBody');
    if (!tbody) {
        console.error('Table body not found!');
        return;
    }

    tbody.innerHTML = '';
    console.log('Cleared table body');

    if (!meters || meters.length === 0) {
        tbody.innerHTML = '<tr><td colspan="6" class="text-center">No meters found in selected table</td></tr>';
        console.log('No meters to display');
        return;
    }

    // Add info header
    const headerRow = document.createElement('tr');
    headerRow.className = 'table-info';
    headerRow.innerHTML = `
        <td colspan="6" class="text-center">
            <small><strong>HDS Import:</strong> Showing ${meters.length} meters from SQL Server. 
            Configure settings below and select meters to import.</small>
        </td>
    `;
    tbody.appendChild(headerRow);

    // Add each meter as a row
    meters.forEach((meter, index) => {
        const row = document.createElement('tr');

        // Checkbox cell
        const checkboxCell = document.createElement('td');
        checkboxCell.className = 'text-center';
        checkboxCell.innerHTML = `
            <input type="checkbox" class="meter-checkbox" 
                   id="meter_${index}" 
                   data-meter-name="${meter.hdsMeterName}" 
                   checked>
        `;

        // Name cell
        const nameCell = document.createElement('td');
        nameCell.innerHTML = `
            <strong>${meter.hdsMeterName}</strong>
            <input type="hidden" class="meter-name" value="${meter.hdsMeterName}">
        `;

        // Unit cell
        const unitCell = document.createElement('td');
        unitCell.innerHTML = `
            <input type="text" class="form-control form-control-sm meter-unit" 
                   value="${meter.unit || ''}" 
                   placeholder="Enter unit">
        `;

        // Parent cell
        const parentCell = document.createElement('td');
        if (window.parentOptions && window.parentOptions.length > 0) {
            let parentSelectHtml = `<select class="form-select form-select-sm meter-parent">`;
            parentSelectHtml += `<option value="">No Parent</option>`;

            window.parentOptions.forEach(option => {
                const value = option.value || option.Value;
                const text = option.text || option.Text;
                parentSelectHtml += `<option value="${value}">${text}</option>`;
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

        tbody.appendChild(row);
    });

    console.log(`Table populated with ${meters.length} meters`);
    updateMeterCounter();
}

// =====================================================
// UNIFIED PRINT BUTTON SYSTEM
// =====================================================

function handleUnifiedPrint() {
    console.log('🎯 Unified Print Handler - Detecting data type...');

    const dataType = window.currentMeterDataType || 'UNKNOWN';
    console.log(`🎯 Current data type: ${dataType}`);

    switch (dataType) {
        case 'HDS':
            console.log('🔵 Routing to HDS print handler');
            handleHDSPrint();
            break;

        case 'VAREXP':
            console.log('🟨 Routing to VAREXP print handler');
            // This will be handled by import.js
            if (typeof handleVarexpPrint === 'function') {
                handleVarexpPrint();
            } else {
                console.error('🔴 VAREXP print handler not found');
                alert('VAREXP print functionality not available');
            }
            break;

        default:
            console.error('🔴 Unknown data type - cannot determine print handler');

            // Fallback: Try to detect from table content
            const tableBody = document.getElementById('metersTableBody');
            if (tableBody) {
                const headerRow = tableBody.querySelector('.table-info');
                if (headerRow && headerRow.textContent.includes('HDS Import')) {
                    console.log('🔵 Fallback detected HDS from table header');
                    window.currentMeterDataType = 'HDS';
                    handleHDSPrint();
                } else if (headerRow && headerRow.textContent.includes('VAREXP Import')) {
                    console.log('🟨 Fallback detected VAREXP from table header');
                    window.currentMeterDataType = 'VAREXP';
                    if (typeof handleVarexpPrint === 'function') {
                        handleVarexpPrint();
                    }
                } else {
                    alert('Cannot determine data type. Please reload the meters and try again.');
                }
            } else {
                alert('No meter data found. Please load meters first.');
            }
            break;
    }
}

function updatePrintButtonForDataType(dataType) {
    const printBtn = document.getElementById('printSelectedBtn');

    if (printBtn) {
        if (dataType === 'HDS') {
            printBtn.innerHTML = '<i class="bi bi-printer"></i> Print HDS Meters';
            printBtn.className = 'btn btn-info'; // Blue for HDS
        } else if (dataType === 'VAREXP') {
            printBtn.innerHTML = '<i class="bi bi-printer"></i> Print VAREXP Meters';
            printBtn.className = 'btn btn-secondary'; // Grey for VAREXP
        } else {
            printBtn.innerHTML = '<i class="bi bi-printer"></i> Print Selected';
            printBtn.className = 'btn btn-info';
        }

        console.log(`🎯 Updated print button for ${dataType} data type`);
    }
}

function handleHDSPrint() {
    console.log('🔵 Starting HDS print process...');

    try {
        const selectedCheckboxes = document.querySelectorAll('.meter-checkbox:checked');

        if (selectedCheckboxes.length === 0) {
            alert('Please select at least one HDS meter to print.');
            return;
        }

        console.log(`🔵 Found ${selectedCheckboxes.length} selected HDS meters`);

        // Extract HDS meter data from the table
        const selectedHDSMeters = extractHDSMeterData(selectedCheckboxes);

        if (selectedHDSMeters.length === 0) {
            alert('No valid HDS meter data found. Please check your selection.');
            return;
        }

        // Get HDS context information
        const hdsContext = window.currentHDSContext || {};
        const importReadings = document.getElementById('importReadings')?.checked || false;
        const startDate = document.getElementById('hdsStartDate')?.value || null;
        const endDate = document.getElementById('hdsEndDate')?.value || null;

        // Prepare HDS-specific request
        const hdsRequest = {
            tableName: hdsContext.tableName,
            connectionId: hdsContext.connectionId,
            selectedMeters: selectedHDSMeters,
            importHistoricalReadings: importReadings,
            startDate: startDate,
            endDate: endDate
        };

        console.log('🔵 HDS Request prepared:', hdsRequest);

        // Send to HDS-specific endpoint
        sendHDSPrintRequest(hdsRequest);

    } catch (error) {
        console.error('🔴 Error in HDS print process:', error);
        alert(`HDS Print failed: ${error.message}`);
    }
}

function extractHDSMeterData(selectedCheckboxes) {
    console.log('🔵 Extracting HDS meter data from table...');
    const hdsMeters = [];

    selectedCheckboxes.forEach((checkbox, index) => {
        try {
            const row = checkbox.closest('tr');
            if (!row || row.classList.contains('table-info')) return;

            const cells = row.cells;
            if (cells.length < 6) {
                console.warn(`Row ${index} has insufficient cells: ${cells.length}`);
                return;
            }

            // Extract meter data from HDS table structure
            const nameCell = cells[1];
            const strongElement = nameCell?.querySelector('strong');
            const hdsMeterName = strongElement ? strongElement.textContent.trim() : '';

            if (!hdsMeterName) {
                console.warn(`No HDS meter name found in row ${index}`);
                return;
            }

            const unitInput = cells[2]?.querySelector('.meter-unit, input[type="text"]');
            const unit = unitInput ? unitInput.value.trim() : '';

            const parentSelect = cells[3]?.querySelector('.meter-parent, select');
            const parentMeterId = parentSelect ? parentSelect.value : '';

            const typeSelect = cells[4]?.querySelector('.meter-type, select');
            const type = typeSelect ? typeSelect.value : 'main';

            const activeCheckbox = cells[5]?.querySelector('.meter-active, input[type="checkbox"]');
            const active = activeCheckbox ? activeCheckbox.checked : true;

            const meterData = {
                hdsMeterName: hdsMeterName,
                unit: unit,
                type: type,
                parentMeterId: parentMeterId,
                active: active,
                isSelected: true
            };

            hdsMeters.push(meterData);
            console.log(`🔵 Extracted HDS meter ${index + 1}: ${hdsMeterName}`);

        } catch (error) {
            console.error(`🔴 Error extracting meter data from row ${index}:`, error);
        }
    });

    console.log(`🔵 Successfully extracted ${hdsMeters.length} HDS meters`);
    return hdsMeters;
}

function sendHDSPrintRequest(hdsRequest) {
    console.log('🔵 Sending HDS print request to server...');

    fetch('/Import/PrintHDSMeters', {
        method: 'POST',
        headers: {
            'Content-Type': 'application/json'
        },
        body: JSON.stringify(hdsRequest)
    })
        .then(response => {
            console.log('🔵 HDS print response status:', response.status);

            if (!response.ok) {
                throw new Error(`HTTP ${response.status}: ${response.statusText}`);
            }

            return response.json();
        })
        .then(data => {
            console.log('🔵 HDS print response:', data);

            if (data.success) {
                const message = `✅ Successfully printed ${data.count} HDS meters to console!\n\n` +
                    `📋 Details:\n` +
                    `• Table: ${data.tableName}\n` +
                    `• Connection: ${data.connectionId}\n` +
                    `• Meters: ${data.count}\n\n` +
                    `Check the server console for detailed HDS meter information.`;

                alert(message);
                console.log('✅ HDS print completed successfully');
            } else {
                throw new Error(data.error || 'Unknown error occurred');
            }
        })
        .catch(error => {
            console.error('🔴 HDS print request failed:', error);
            alert(`❌ HDS Print Failed\n\nError: ${error.message}\n\nCheck the browser console for more details.`);
        })
        .finally(() => {
            console.log('🔵 HDS print process completed');
        });
}

// =====================================================
// METER SELECTION & INTERACTION
// =====================================================

function handleSelectAll() {
    const dataType = window.currentMeterDataType || 'UNKNOWN';
    console.log(`Select All for ${dataType} data type`);

    const checkboxes = document.querySelectorAll('.meter-checkbox');
    checkboxes.forEach(checkbox => {
        checkbox.checked = true;
    });

    updateMeterCounter();
}

function handleDeselectAll() {
    const dataType = window.currentMeterDataType || 'UNKNOWN';
    console.log(`Deselect All for ${dataType} data type`);

    const checkboxes = document.querySelectorAll('.meter-checkbox');
    checkboxes.forEach(checkbox => {
        checkbox.checked = false;
    });

    updateMeterCounter();
}

function handleCheckboxChange(checkbox) {
    console.log(`Checkbox changed: ${checkbox.id}, Checked: ${checkbox.checked}`);
    updateMeterCounter();
}

function handleFilterInput(event) {
    const filterText = event.target.value.toLowerCase();
    const rows = document.querySelectorAll('#metersTableBody tr');
    let visibleCount = 0;

    rows.forEach(row => {
        if (row.classList.contains('table-info')) {
            return; // Skip header row
        }

        const meterNameCell = row.querySelector('strong');
        if (meterNameCell) {
            const meterName = meterNameCell.textContent.toLowerCase();

            if (meterName.includes(filterText)) {
                row.style.display = '';
                visibleCount++;
            } else {
                row.style.display = 'none';
            }
        }
    });

    // Update filter status
    const statusElement = document.getElementById('meterFilterStatus');
    if (statusElement) {
        const totalMeters = document.querySelectorAll('.meter-checkbox').length;
        statusElement.textContent = `Showing ${visibleCount} of ${totalMeters} meters`;
    }
}

function updateMeterCounter() {
    const checkboxes = document.querySelectorAll('.meter-checkbox');
    const checkedBoxes = document.querySelectorAll('.meter-checkbox:checked');

    // Update filter status to show selection count
    const statusElement = document.getElementById('meterFilterStatus');
    if (statusElement) {
        const totalMeters = checkboxes.length;
        const selectedMeters = checkedBoxes.length;
        statusElement.textContent = `Selected ${selectedMeters} of ${totalMeters} meters`;
    }

    // Update import button text
    const importBtn = document.getElementById('importSelectedBtn');
    if (importBtn) {
        importBtn.textContent = checkedBoxes.length > 0 ?
            `Import Selected (${checkedBoxes.length})` :
            'Import Selected';
    }

    console.log(`Counter updated: ${checkedBoxes.length} selected of ${checkboxes.length} total`);
}

// =====================================================
// IMPORT FUNCTIONALITY
// =====================================================

function handleImport() {
    const dataType = window.currentMeterDataType || 'UNKNOWN';
    console.log(`Import for ${dataType} data type`);

    if (dataType === 'HDS') {
        handleHDSImport();
    } else if (dataType === 'VAREXP') {
        // This will be handled by import.js
        if (typeof handleVarexpImport === 'function') {
            handleVarexpImport();
        } else {
            console.error('VAREXP import handler not found');
            alert('VAREXP import functionality not available');
        }
    } else {
        alert('Unknown data type. Please reload the meters and try again.');
    }
}

function handleHDSImport() {
    console.log('HDS Import button clicked');

    const selectedCheckboxes = document.querySelectorAll('.meter-checkbox:checked');
    if (selectedCheckboxes.length === 0) {
        alert('Please select at least one meter to import.');
        return;
    }

    // Gather meter data
    const meters = [];
    selectedCheckboxes.forEach((checkbox) => {
        const row = checkbox.closest('tr');
        if (!row || row.classList.contains('table-info')) return;

        const meterName = row.querySelector('.meter-name')?.value ||
            row.querySelector('strong')?.textContent;
        const unit = row.querySelector('.meter-unit')?.value || '';
        const parentId = row.querySelector('.meter-parent')?.value || '';
        const type = row.querySelector('.meter-type')?.value || 'main';
        const active = row.querySelector('.meter-active')?.checked || true;

        if (meterName) {
            meters.push({
                hdsMeterName: meterName,
                unit: unit,
                parentMeterId: parentId,
                type: type,
                active: active
            });
        }
    });

    if (meters.length === 0) {
        alert('No valid meters found to import.');
        return;
    }

    console.log('Prepared meters for import:', meters);

    // Get import options
    const skipExisting = document.getElementById('skipExisting')?.checked || false;
    const updateExisting = document.getElementById('updateExisting')?.checked || false;
    const importReadings = document.getElementById('importReadings')?.checked || false;

    // Prepare import request
    const hdsContext = window.currentHDSContext || {};
    const importData = {
        meters: meters,
        skipExisting: skipExisting,
        updateExisting: updateExisting,
        importReadings: importReadings,
        connectionId: hdsContext.connectionId,
        tableName: hdsContext.tableName
    };

    console.log('Import data:', importData);

    // Show loading state
    const importBtn = document.getElementById('importSelectedBtn');
    const originalText = importBtn.textContent;
    importBtn.innerHTML = '<span class="spinner-border spinner-border-sm"></span> Importing...';
    importBtn.disabled = true;

    // Send import request
    fetch('/Import/ImportHDSMeters', {
        method: 'POST',
        headers: {
            'Content-Type': 'application/json'
        },
        body: JSON.stringify(importData)
    })
        .then(response => {
            if (!response.ok) {
                throw new Error(`Import failed: ${response.status} ${response.statusText}`);
            }
            return response.json();
        })
        .then(data => {
            console.log('Import response:', data);

            // Reset button
            importBtn.textContent = originalText;
            importBtn.disabled = false;

            // Show results
            if (data.success) {
                alert(`Successfully imported ${data.importedCount || 0} meters!`);

                if (confirm('Import completed! Would you like to reload the page to see the imported meters?')) {
                    window.location.reload();
                }
            } else {
                alert(`Import failed: ${data.error || 'Unknown error'}`);
            }
        })
        .catch(error => {
            console.error('Import error:', error);

            // Reset button
            importBtn.textContent = originalText;
            importBtn.disabled = false;

            alert(`Import error: ${error.message}`);
        });
}

// =====================================================
// GLOBAL EXPORTS
// =====================================================

// Make key functions available globally for debugging
window.HDSMeterImport = {
    handleUnifiedPrint: handleUnifiedPrint,
    handleSelectAll: handleSelectAll,
    handleDeselectAll: handleDeselectAll,
    updateMeterCounter: updateMeterCounter,
    handleImport: handleImport
};

console.log('HDS Meter Import JS initialization complete');