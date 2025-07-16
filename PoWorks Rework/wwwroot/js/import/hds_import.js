/**
 * HDS Meter Import & Selection
 * Handles:
 * - Meter selection modal interactions
 * - Bulk actions
 * - Filtering
 * - HDS import logic
 * - Print request for HDS meters
 * - HDS-specific connection management
 */

// =====================================================
// INITIALIZATION & EVENT HANDLERS
// =====================================================

/**
 * Sets up all DOM event listeners once the document is loaded.
 */
document.addEventListener('DOMContentLoaded', function () {
    console.log('HDS Meter Selection JS loaded - DOM Content Loaded');

    initializeEventDelegation();
    initializeModalIfAlreadyPresent();
    initializeHdsDateRange();
    setupHdsQuickRangeButtons();
    setupHdsDateValidation();

    // HDS-specific initialization
    loadSqlServerConnections();
    setupConnectionEventListeners();
});

/**
 * Adds delegated event listeners for clicks, inputs, and changes
 * throughout the document body.
 */
function initializeEventDelegation() {
    console.log('Initializing event delegation');

    document.body.addEventListener('click', function (event) {
        // Use shared functions from common.js
        if (event.target.id === 'selectAllBtn' || event.target.closest('#selectAllBtn')) {
            handleSelectAll(); // From common.js
        }

        if (event.target.id === 'deselectAllBtn' || event.target.closest('#deselectAllBtn')) {
            handleDeselectAll(); // From common.js
        }

        // HDS-specific bulk actions
        if (event.target.id === 'applyBulkType' || event.target.closest('#applyBulkType')) {
            applyBulkType();
        }

        if (event.target.id === 'applyBulkParent' || event.target.closest('#applyBulkParent')) {
            applyBulkParent();
        }

        if (event.target.id === 'applyBulkActive' || event.target.closest('#applyBulkActive')) {
            applyBulkActive();
        }

        if (event.target.id === 'printSelectedHDSBtn') {
            handleHDSPrint();
        }

        // HDS-specific load meters button
        if (event.target.id === 'loadMetersBtn' || event.target.closest('#loadMetersBtn')) {
            handleLoadMeters();
        }
    });

    document.body.addEventListener('shown.bs.modal', function (event) {
        if (event.target.id === 'hdsMeterSelectionModal') {
            initializeModal();
        }
    });

    document.body.addEventListener('input', function (event) {
        if (event.target.id === 'filterInput') {
            filterRows(event.target.value);
        }
    });

    document.body.addEventListener('change', function (event) {
        if (event.target.classList.contains('meter-checkbox')) {
            handleCheckboxChange(event.target);
        }

        // HDS connection changes
        if (event.target.id === 'hdsConnection') {
            handleConnectionChange();
        }

        if (event.target.id === 'hdsTable') {
            handleTableChange();
        }
    });
}

/**
 * If the modal is already in the DOM on page load,
 * initialize it directly.
 */
function initializeModalIfAlreadyPresent() {
    const modal = document.getElementById('hdsMeterSelectionModal');
    if (modal) {
        setTimeout(initializeModal, 100);
    }
}

// =====================================================
// HDS CONNECTION MANAGEMENT
// =====================================================

/**
 * Loads SQL Server connections for HDS import
 */
function loadSqlServerConnections() {
    console.log('Loading SQL Server connections...');

    fetch('/Import/GetSqlServerConnections')
        .then(response => {
            console.log(`SQL Server connections response status: ${response.status}`);

            if (!response.ok) {
                throw new Error(`Failed to load connections: ${response.status} ${response.statusText}`);
            }
            return response.json();
        })
        .then(data => {
            console.log('SQL Server connections response:', data);

            if (data.success && data.connections) {
                console.log(`Found ${data.connections.length} SQL Server connections`);
                populateConnectionDropdown(data.connections);
            } else if (data.connections) {
                // Handle case where success flag might be missing
                console.log(`Found ${data.connections.length} connections (no success flag)`);
                populateConnectionDropdown(data.connections);
            } else {
                console.error('Failed to load connections:', data.error);
                updateConnectionStatus('error', data.error || 'Failed to load connections');
            }
        })
        .catch(error => {
            console.error('Error loading SQL Server connections:', error);
            updateConnectionStatus('error', 'Error loading connections - check console for details');
        });
}

/**
 * Populates the HDS connection dropdown
 */
function populateConnectionDropdown(connections) {
    const connectionSelect = document.getElementById('hdsConnection');
    if (!connectionSelect) {
        console.error('HDS connection select element not found');
        return;
    }

    console.log(`Populating connection dropdown with ${connections?.length || 0} connections`);

    connectionSelect.innerHTML = '<option value="">Select a connection...</option>';

    if (!connections || connections.length === 0) {
        const noConnectionsOption = document.createElement('option');
        noConnectionsOption.value = '';
        noConnectionsOption.textContent = 'No connections available';
        noConnectionsOption.disabled = true;
        connectionSelect.appendChild(noConnectionsOption);

        updateConnectionStatus('warning', 'No SQL Server connections found');
        console.log('No connections available');
        return;
    }

    connections.forEach((connection, index) => {
        const option = document.createElement('option');

        // Handle different possible connection object formats
        if (typeof connection === 'string') {
            option.value = connection;
            option.textContent = connection;
        } else if (connection.connectionId && connection.connectionName) {
            option.value = connection.connectionId;
            option.textContent = connection.connectionName;
        } else if (connection.id && connection.name) {
            option.value = connection.id;
            option.textContent = connection.name;
        } else if (connection.ConnectionId && connection.ConnectionName) {
            option.value = connection.ConnectionId;
            option.textContent = connection.ConnectionName;
        } else {
            console.warn(`Unknown connection format at index ${index}:`, connection);
            option.value = `connection_${index}`;
            option.textContent = `Connection ${index + 1}`;
        }

        connectionSelect.appendChild(option);
    });

    updateConnectionStatus('success', `Found ${connections.length} connections`);
    console.log(`Successfully populated ${connections.length} HDS connections`);
}

/**
 * Sets up HDS connection event listeners
 */
function setupConnectionEventListeners() {
    const connectionSelect = document.getElementById('hdsConnection');
    const tableSelect = document.getElementById('hdsTable');

    if (connectionSelect) {
        connectionSelect.addEventListener('change', handleConnectionChange);
    }

    if (tableSelect) {
        tableSelect.addEventListener('change', handleTableChange);
    }
}

/**
 * Handles HDS connection dropdown change
 */
function handleConnectionChange() {
    const connectionSelect = document.getElementById('hdsConnection');
    const tableSelect = document.getElementById('hdsTable');
    const loadBtn = document.getElementById('loadMetersBtn');

    if (!connectionSelect || !tableSelect || !loadBtn) {
        console.error('Required HDS elements not found');
        return;
    }

    const connectionId = connectionSelect.value;
    const connectionText = connectionSelect.options[connectionSelect.selectedIndex]?.text || 'Unknown';

    console.log(`HDS Connection changed to: "${connectionId}" (${connectionText})`);

    if (connectionId) {
        updateConnectionStatus('info', 'Loading tables...');
        loadHdsTables(connectionId);
    } else {
        console.log('No connection selected, clearing tables');
        tableSelect.innerHTML = '<option value="">Select a table...</option>';
        tableSelect.disabled = true;
        loadBtn.disabled = true;
        updateConnectionStatus('warning', 'Please select a connection');
    }
}

/**
 * Handles HDS table dropdown change
 */
function handleTableChange() {
    const tableSelect = document.getElementById('hdsTable');
    const loadBtn = document.getElementById('loadMetersBtn');

    if (!tableSelect || !loadBtn) return;

    const tableName = tableSelect.value;

    if (tableName) {
        loadBtn.disabled = false;
        updateConnectionStatus('success', 'Ready to load meters');
    } else {
        loadBtn.disabled = true;
        updateConnectionStatus('info', 'Select a table to continue');
    }
}

/**
 * Loads HDS tables for selected connection - SIMPLIFIED VERSION
 */
function loadHdsTables(connectionId) {
    const tableSelect = document.getElementById('hdsTable');
    const loadButton = document.getElementById('loadMetersBtn');

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

/**
 * Populates the HDS table dropdown - SIMPLIFIED VERSION
 */
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
    updateConnectionStatus('success', `Found ${tables.length} tables`);
}

/**
 * Updates connection status display
 */
function updateConnectionStatus(type, message) {
    const statusElement = document.getElementById('hdsConnectionStatus');
    if (!statusElement) return;

    const iconMap = {
        'success': 'bi-check-circle text-success',
        'error': 'bi-x-circle text-danger',
        'warning': 'bi-exclamation-triangle text-warning',
        'info': 'bi-info-circle text-info'
    };

    const icon = iconMap[type] || iconMap['info'];
    statusElement.innerHTML = `<i class="bi ${icon}"></i> ${message}`;
}

/**
 * Handles loading HDS meters - SIMPLIFIED VERSION
 */
function handleLoadMeters() {
    const connectionSelect = document.getElementById('hdsConnection');
    const tableSelect = document.getElementById('hdsTable');
    const loadBtn = document.getElementById('loadMetersBtn');

    if (!connectionSelect || !tableSelect || !loadBtn) {
        console.error('Required HDS elements not found');
        return;
    }

    const connectionId = connectionSelect.value;
    const tableName = tableSelect.value;

    if (!connectionId || !tableName) {
        alert('Please select both connection and table');
        return;
    }

    console.log(`Loading HDS meters from ${tableName} (${connectionId})`);

    // Store HDS context for later use
    window.currentHDSContext = {
        connectionId: connectionId,
        tableName: tableName
    };

    // Set data type to HDS
    window.currentMeterDataType = 'HDS';

    // Update print button for HDS
    if (typeof updatePrintButtonForDataType === 'function') {
        updatePrintButtonForDataType('HDS');
    }

    // Show loading state
    loadBtn.disabled = true;
    loadBtn.innerHTML = '<span class="spinner-border spinner-border-sm"></span> Loading...';

    // Use the working endpoint from ImportController
    let queryParams = `tableName=${encodeURIComponent(tableName)}&connectionId=${encodeURIComponent(connectionId)}`;

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

            console.log('Load Meters completed successfully');
        })
        .catch(error => {
            console.error('Error loading meters:', error);
            alert(`Error loading meters: ${error.message}`);
            updateConnectionStatus('error', 'Failed to load meters');
        })
        .finally(() => {
            loadBtn.disabled = false;
            loadBtn.innerHTML = '<i class="bi bi-download"></i> Load Meters';
        });
}

/**
 * Shows HDS meter selection
 */
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

    // SET DATA TYPE FLAG FOR UNIFIED PRINT BUTTON
    window.currentMeterDataType = 'HDS';
    window.currentHDSContext = {
        tableName: tableName,
        connectionId: connectionId
    };
    console.log('Set data type to HDS');

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

    // UPDATE PRINT BUTTON FOR HDS
    if (typeof updatePrintButtonForDataType === 'function') {
        updatePrintButtonForDataType('HDS');
    }

    // Update status
    const statusElement = document.getElementById('meterFilterStatus');
    if (statusElement) {
        statusElement.textContent = `Loaded ${meters.length} meters from ${tableName}`;
        console.log('Updated filter status');
    }

    console.log('HDS meter selection setup complete');
}

/**
 * Populates HDS meters table
 */
function populateHDSMetersTable(meters) {
    console.log('Populating HDS meters table with', meters.length, 'meters');

    // 🔧 DEBUG: Log the exact data structure
    console.log('🔧 DEBUGGING METER DATA STRUCTURE:');
    console.log('🔧 Meters array:', meters);
    console.log('🔧 First meter object:', meters[0]);
    console.log('🔧 First meter keys:', meters[0] ? Object.keys(meters[0]) : 'No first meter');

    if (meters.length > 0) {
        const firstMeter = meters[0];
        console.log('🔧 First meter properties:');
        console.log('  - HdsMeterName:', firstMeter.HdsMeterName);
        console.log('  - hdsMeterName:', firstMeter.hdsMeterName);
        console.log('  - meterName:', firstMeter.meterName);
        console.log('  - MeterName:', firstMeter.MeterName);
        console.log('  - name:', firstMeter.name);
        console.log('  - Name:', firstMeter.Name);
    }

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
            Configure settings and select meters to import.</small>
        </td>
    `;
    tbody.appendChild(headerRow);

    // Add meter rows
    meters.forEach((meter, index) => {
        const row = document.createElement('tr');

        // Checkbox cell
        const checkboxCell = document.createElement('td');
        checkboxCell.className = 'text-center';
        checkboxCell.innerHTML = `
            <div class="form-check d-flex justify-content-center">
                <input class="form-check-input meter-checkbox" type="checkbox"
                       id="meter_${index}" checked>
            </div>
        `;

        // Name cell - 🔧 ROBUST PROPERTY ACCESS
        const nameCell = document.createElement('td');

        // Try all possible property names for meter name
        const meterName = meter.HdsMeterName ||
            meter.hdsMeterName ||
            meter.meterName ||
            meter.MeterName ||
            meter.name ||
            meter.Name ||
            'Unknown';

        console.log(`🔧 Meter ${index}: Using name "${meterName}" from meter:`, meter);
        nameCell.innerHTML = `<strong>${meterName}</strong>`;

        // Unit cell
        const unitCell = document.createElement('td');
        unitCell.innerHTML = `
            <input type="text" class="form-control form-control-sm meter-unit"
                   value="${meter.unit || ''}" placeholder="Enter unit">
        `;

        // Parent cell
        const parentCell = document.createElement('td');
        parentCell.innerHTML = `
            <select class="form-select form-select-sm meter-parent">
                ${renderParentOptions(window.parentOptions)}
            </select>
        `;

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
    if (typeof updateMeterCounter === 'function') {
        updateMeterCounter();
    }
}

/**
 * Renders parent options dropdown
 */
function renderParentOptions(parentOptions) {
    if (!parentOptions || !parentOptions.length) {
        return '<option value="">No parent meters available</option>';
    }

    let html = '<option value="">None</option>';
    parentOptions.forEach(option => {
        html += `<option value="${option.value}">${option.text}</option>`;
    });

    return html;
}

// =====================================================
// HDS IMPORT FUNCTIONALITY
// =====================================================

/**
 * Handles importing selected HDS meters into the database.
 */
function handleHDSImport() {
    console.log('HDS Import button clicked');

    const selectedCheckboxes = document.querySelectorAll('.meter-checkbox:checked');
    if (!selectedCheckboxes.length) {
        alert('Please select at least one meter to import.');
        return;
    }

    if (!confirm(`Import ${selectedCheckboxes.length} HDS meters?`)) {
        return;
    }

    const importBtn = document.getElementById('importSelectedBtn');
    const originalText = importBtn.textContent;
    importBtn.disabled = true;
    importBtn.innerHTML = '<span class="spinner-border spinner-border-sm"></span> Importing HDS Meters...';

    // Get import options
    const skipExisting = document.getElementById('skipExisting')?.checked ?? true;
    const updateExisting = document.getElementById('updateExisting')?.checked ?? false;
    const importReadings = document.getElementById('importReadings')?.checked ?? false;

    // Extract meter data
    const meters = extractHDSMeterData(selectedCheckboxes);
    const hdsContext = window.currentHDSContext || {};

    const importData = {
        meters,
        skipExisting,
        updateExisting,
        importReadings,
        connectionId: hdsContext.connectionId,
        tableName: hdsContext.tableName
    };

    fetch('/Import/ImportHdsMeters', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(importData)
    })
        .then(response => {
            if (!response.ok) {
                throw new Error(`Import failed: ${response.status} ${response.statusText}`);
            }
            return response.json();
        })
        .then(data => {
            importBtn.textContent = originalText;
            importBtn.disabled = false;

            if (data.success) {
                let message = `✅ Successfully imported ${data.importedCount || 0} HDS meters!`;
                if (data.readingsEnabled) {
                    message += `\n📊 Readings: ${data.readingsImported || 0} imported`;
                }
                alert(message);

                if (confirm('Import completed! Would you like to reload the page?')) {
                    window.location.reload();
                }
            } else {
                alert(`❌ Import failed: ${data.error || 'Unknown error'}`);
            }
        })
        .catch(error => {
            console.error('Import error:', error);
            importBtn.textContent = originalText;
            importBtn.disabled = false;
            alert(`❌ Import error: ${error.message}`);
        });
}

// =====================================================
// MODAL INITIALIZATION
// =====================================================

/**
 * Initializes the HDS meter selection modal.
 */
function initializeModal() {
    console.log('Initializing HDS Meter Selection Modal');
    setupCheckboxes();

    // Use shared counter from common.js
    if (typeof updateMeterCounter === 'function') {
        updateMeterCounter();
    } else {
        updateCounter(); // Fallback to local function
    }
}

/**
 * Ensures all hidden inputs associated with checkboxes
 * have correct initial disabled/enabled states.
 */
function setupCheckboxes() {
    const checkboxes = document.querySelectorAll('.meter-checkbox');
    console.log(`Found ${checkboxes.length} meter checkboxes`);

    checkboxes.forEach(function (checkbox) {
        const hiddenInput = checkbox.nextElementSibling;
        if (hiddenInput && hiddenInput.type === 'hidden') {
            hiddenInput.disabled = checkbox.checked;
        }
    });
}

// =====================================================
// CHECKBOX & SELECTION HANDLING
// =====================================================

/**
 * Handles checkbox toggle events inside the modal.
 * @param {HTMLInputElement} checkbox
 */
function handleCheckboxChange(checkbox) {
    console.log(`Checkbox changed: ${checkbox.id}, Checked: ${checkbox.checked}`);

    const hiddenInput = checkbox.nextElementSibling;
    if (hiddenInput && hiddenInput.type === 'hidden') {
        hiddenInput.disabled = checkbox.checked;
    }

    // Use shared counter from common.js if available
    if (typeof updateMeterCounter === 'function') {
        updateMeterCounter();
    } else {
        updateCounter(); // Fallback to local function
    }
}

/**
 * Local counter update (fallback if common.js not available)
 */
function updateCounter() {
    const checkboxes = document.querySelectorAll('.meter-checkbox');
    const checkedBoxes = document.querySelectorAll('.meter-checkbox:checked');
    console.log(`Selection count: ${checkedBoxes.length} of ${checkboxes.length}`);

    const statusBadge = document.getElementById('selectionStatus');
    if (statusBadge) {
        statusBadge.textContent = `Selected ${checkedBoxes.length} of ${checkboxes.length} meters`;
    }

    const importBtn = document.getElementById('importSelectedBtn');
    if (importBtn) {
        importBtn.textContent = checkedBoxes.length > 0
            ? `Import Selected (${checkedBoxes.length})`
            : 'Import Selected';
    }
}

/**
 * Filters rows in the meter table based on user input.
 * @param {string} filterText
 */
function filterRows(filterText) {
    filterText = filterText.toLowerCase();
    const rows = document.querySelectorAll('#hdsMeterTable tbody tr');
    let visibleCount = 0;

    rows.forEach(row => {
        const meterName = row.cells[1]?.textContent.toLowerCase() || '';
        if (meterName.includes(filterText)) {
            row.style.display = '';
            visibleCount++;
        } else {
            row.style.display = 'none';
        }
    });

    const filterStatus = document.getElementById('filterStatus');
    if (filterStatus) {
        filterStatus.textContent = `Showing ${visibleCount} of ${rows.length} meters`;
    }
}

/**
 * Returns an array of row indices for currently selected meters.
 */
function getSelectedRows() {
    const selectedCheckboxes = document.querySelectorAll('.meter-checkbox:checked');
    const rows = [];

    selectedCheckboxes.forEach(checkbox => {
        const rowIndex = checkbox.id.split('_')[1];
        if (rowIndex !== undefined) {
            rows.push(rowIndex);
        }
    });

    console.log(`Selected rows: ${rows.length}`);
    return rows;
}

// =====================================================
// BULK ACTIONS
// =====================================================

/**
 * Applies a selected Type value to all selected meters.
 */
function applyBulkType() {
    const radioButtons = document.getElementsByName('bulkType');
    let typeValue = '';

    for (let radio of radioButtons) {
        if (radio.checked) {
            typeValue = radio.value;
            break;
        }
    }

    if (!typeValue) {
        alert('Please select a type to apply.');
        return;
    }

    const selectedRows = getSelectedRows();
    if (selectedRows.length === 0) {
        alert('Please select at least one meter first.');
        return;
    }

    console.log(`Applying type "${typeValue}" to ${selectedRows.length} rows`);

    selectedRows.forEach(rowIndex => {
        const typeSelect = document.querySelector(`select[name="SelectedMeters[${rowIndex}].Type"]`);
        if (typeSelect) {
            typeSelect.value = typeValue;
        }
    });
}

/**
 * Applies a selected Parent meter to all selected meters.
 */
function applyBulkParent() {
    const parentValue = document.getElementById('bulkParent').value;

    const selectedRows = getSelectedRows();
    if (selectedRows.length === 0) {
        alert('Please select at least one meter first.');
        return;
    }

    console.log(`Applying parent "${parentValue}" to ${selectedRows.length} rows`);

    selectedRows.forEach(rowIndex => {
        const parentSelect = document.querySelector(`select[name="SelectedMeters[${rowIndex}].ParentMeterId"]`);
        if (parentSelect) {
            parentSelect.value = parentValue;
        }
    });
}

/**
 * Applies an Active state (true/false) to all selected meters.
 */
function applyBulkActive() {
    const activeValue = document.getElementById('bulkActive').checked;

    const selectedRows = getSelectedRows();
    if (selectedRows.length === 0) {
        alert('Please select at least one meter first.');
        return;
    }

    console.log(`Applying active state "${activeValue}" to ${selectedRows.length} rows`);

    selectedRows.forEach(rowIndex => {
        const activeCheckbox = document.getElementById(`active_${rowIndex}`);
        if (activeCheckbox) {
            activeCheckbox.checked = activeValue;

            const hiddenInput = activeCheckbox.nextElementSibling;
            if (hiddenInput && hiddenInput.type === 'hidden') {
                hiddenInput.disabled = activeValue;
            }
        }
    });
}

// =====================================================
// PRINT FUNCTIONALITY
// =====================================================

/**
 * Initiates printing of the selected HDS meters.
 */
function handleHDSPrint() {
    console.log('Starting HDS print process...');

    try {
        const selectedCheckboxes = document.querySelectorAll('.meter-checkbox:checked');

        if (selectedCheckboxes.length === 0) {
            alert('Please select at least one HDS meter to print.');
            return;
        }

        const selectedHDSMeters = extractHDSMeterData(selectedCheckboxes);
        if (selectedHDSMeters.length === 0) {
            alert('No valid HDS meter data found.');
            return;
        }

        const hdsRequest = buildHDSPrintRequest(selectedHDSMeters);
        sendHDSPrintRequest(hdsRequest);

    } catch (error) {
        console.error('Error in HDS print process:', error);
        alert(`HDS Print failed: ${error.message}`);
    }
}

/**
 * Extracts meter data for printing from selected table rows.
 * @param {NodeListOf<HTMLInputElement>} selectedCheckboxes
 */
function extractHDSMeterData(selectedCheckboxes) {
    console.log('Extracting HDS meter data from table...');
    const hdsMeters = [];

    selectedCheckboxes.forEach((checkbox, index) => {
        const row = checkbox.closest('tr');
        if (!row || row.classList.contains('table-info')) return;

        const cells = row.cells;
        if (cells.length < 6) return;

        const strongElement = cells[1]?.querySelector('strong');
        const hdsMeterName = strongElement?.textContent.trim() || '';

        const unitInput = cells[2]?.querySelector('.meter-unit');
        const parentSelect = cells[3]?.querySelector('.meter-parent');
        const typeSelect = cells[4]?.querySelector('.meter-type');
        const activeCheckbox = cells[5]?.querySelector('.meter-active');

        hdsMeters.push({
            hdsMeterName,
            unit: unitInput?.value || '',
            parentMeterId: parentSelect?.value || '',
            type: typeSelect?.value || 'main',
            active: activeCheckbox?.checked || true,
            isSelected: true
        });
    });

    console.log(`Successfully extracted ${hdsMeters.length} HDS meters`);
    return hdsMeters;
}

/**
 * Builds the request payload for printing selected meters.
 * @param {Array<Object>} meters
 */
function buildHDSPrintRequest(meters) {
    const hdsContext = window.currentHDSContext || {};
    return {
        tableName: hdsContext.tableName,
        connectionId: hdsContext.connectionId,
        selectedMeters: meters,
        importHistoricalReadings: document.getElementById('importReadings')?.checked || false,
        startDate: document.getElementById('hdsStartDate')?.value || null,
        endDate: document.getElementById('hdsEndDate')?.value || null
    };
}

/**
 * Sends the print request to the server and handles response.
 * @param {Object} hdsRequest
 */
function sendHDSPrintRequest(hdsRequest) {
    console.log('Sending HDS print request:', hdsRequest);

    fetch('/Import/PrintHDSMeters', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(hdsRequest)
    })
        .then(response => {
            console.log('HDS print response status:', response.status);
            if (!response.ok) {
                throw new Error(`HTTP ${response.status}: ${response.statusText}`);
            }
            return response.json();
        })
        .then(data => {
            if (data.success) {
                alert(`✅ Successfully printed ${data.count} HDS meters.`);
            } else {
                throw new Error(data.error || 'Unknown error occurred');
            }
        })
        .catch(error => {
            console.error('HDS print request failed:', error);
            alert(`❌ HDS Print Failed: ${error.message}`);
        });
}

// =====================================================
// DATE RANGE FUNCTIONALITY
// =====================================================

/**
 * Initializes default HDS date range (last 24 hours).
 */
function initializeHdsDateRange() {
    const endDateInput = document.getElementById('hdsEndDate');
    const startDateInput = document.getElementById('hdsStartDate');

    if (!endDateInput || !startDateInput) return;

    const now = new Date();
    const yesterday = new Date(now.getTime() - 24 * 60 * 60 * 1000);

    endDateInput.value = formatDateTimeLocal(now);
    startDateInput.value = formatDateTimeLocal(yesterday);

    console.log('Initialized HDS date range: Last 24 hours');
}

/**
 * Formats a Date object to an HTML input datetime-local string.
 * @param {Date} date
 */
function formatDateTimeLocal(date) {
    const year = date.getFullYear();
    const month = String(date.getMonth() + 1).padStart(2, '0');
    const day = String(date.getDate()).padStart(2, '0');
    const hours = String(date.getHours()).padStart(2, '0');
    const minutes = String(date.getMinutes()).padStart(2, '0');
    return `${year}-${month}-${day}T${hours}:${minutes}`;
}

/**
 * Attaches event listeners to quick-range buttons.
 */
function setupHdsQuickRangeButtons() {
    const btn24h = document.getElementById('hdsQuickRange24h');
    const btn7d = document.getElementById('hdsQuickRange7d');
    const btn30d = document.getElementById('hdsQuickRange30d');

    if (btn24h) {
        btn24h.addEventListener('click', () => {
            setHdsQuickRange(24, 'hours');
            highlightActiveHdsQuickRange('hdsQuickRange24h');
        });
    }

    if (btn7d) {
        btn7d.addEventListener('click', () => {
            setHdsQuickRange(7, 'days');
            highlightActiveHdsQuickRange('hdsQuickRange7d');
        });
    }

    if (btn30d) {
        btn30d.addEventListener('click', () => {
            setHdsQuickRange(30, 'days');
            highlightActiveHdsQuickRange('hdsQuickRange30d');
        });
    }
}

/**
 * Sets the HDS start and end dates to a quick range.
 * @param {number} amount
 * @param {string} unit
 */
function setHdsQuickRange(amount, unit) {
    const now = new Date();
    const startDate = new Date();

    if (unit === 'hours') {
        startDate.setHours(startDate.getHours() - amount);
    } else if (unit === 'days') {
        startDate.setDate(startDate.getDate() - amount);
    }

    const endDateInput = document.getElementById('hdsEndDate');
    const startDateInput = document.getElementById('hdsStartDate');

    if (endDateInput) endDateInput.value = formatDateTimeLocal(now);
    if (startDateInput) startDateInput.value = formatDateTimeLocal(startDate);

    validateHdsDateRange();
}

/**
 * Highlights the active quick-range button visually.
 * @param {string} activeId
 */
function highlightActiveHdsQuickRange(activeId) {
    ['hdsQuickRange24h', 'hdsQuickRange7d', 'hdsQuickRange30d'].forEach(id => {
        const btn = document.getElementById(id);
        if (btn) {
            btn.classList.toggle('btn-secondary', id === activeId);
            btn.classList.toggle('btn-outline-secondary', id !== activeId);
        }
    });
}

/**
 * Attaches validation logic to HDS date range fields.
 */
function setupHdsDateValidation() {
    const startDateInput = document.getElementById('hdsStartDate');
    const endDateInput = document.getElementById('hdsEndDate');

    if (startDateInput) startDateInput.addEventListener('change', validateHdsDateRange);
    if (endDateInput) endDateInput.addEventListener('change', validateHdsDateRange);
}

/**
 * Validates the HDS date range selection.
 * Ensures start date is before end date and range is ≤ 1 year.
 */
function validateHdsDateRange() {
    const startDateInput = document.getElementById('hdsStartDate');
    const endDateInput = document.getElementById('hdsEndDate');

    if (!startDateInput || !endDateInput) return true;

    const startDate = new Date(startDateInput.value);
    const endDate = new Date(endDateInput.value);

    startDateInput.classList.remove('is-invalid');
    endDateInput.classList.remove('is-invalid');

    if (startDate >= endDate) {
        endDateInput.classList.add('is-invalid');
        console.warn('Invalid HDS date range');
        return false;
    }

    const oneYear = 365 * 24 * 60 * 60 * 1000;
    if (endDate - startDate > oneYear) {
        startDateInput.classList.add('is-invalid');
        endDateInput.classList.add('is-invalid');
        console.warn('HDS date range exceeds 1 year');
        return false;
    }

    console.log('Valid HDS date range selected');
    return true;
}

// =====================================================
// DEBUGGING
// =====================================================

/**
 * Debug utility to extract current selected meter data.
 */
window.debugHDSExtraction = function () {
    console.log('DEBUG: Testing HDS meter extraction...');
    const selectedCheckboxes = document.querySelectorAll('.meter-checkbox:checked');
    const extractedData = extractHDSMeterData(selectedCheckboxes);
    console.log('Extracted HDS data:', extractedData);
    return extractedData;
};

console.log('✅ HDS Meter Import script initialized (simplified version)');