/**
 * Meter Selection Modal Functionality
 * This file handles all the functionality for the HDS meter selection modal
 */

// Wait for the document to be fully loaded
document.addEventListener('DOMContentLoaded', function () {
    console.log('HDS Meter Selection JS loaded - DOM Content Loaded');

    // Set up event delegation for the parent page
    // This will work even if the modal is dynamically loaded later
    document.body.addEventListener('click', function (event) {
        // Handle Select All button click
        if (event.target.id === 'selectAllBtn' || event.target.closest('#selectAllBtn')) {
            console.log('Select All button clicked (delegated)');
            handleSelectAll();
        }

        // Handle Deselect All button click
        if (event.target.id === 'deselectAllBtn' || event.target.closest('#deselectAllBtn')) {
            console.log('Deselect All button clicked (delegated)');
            handleDeselectAll();
        }

        // Handle Apply Bulk Type button click
        if (event.target.id === 'applyBulkType' || event.target.closest('#applyBulkType')) {
            console.log('Apply Bulk Type button clicked (delegated)');
            applyBulkType();
        }

        // Handle Apply Bulk Parent button click
        if (event.target.id === 'applyBulkParent' || event.target.closest('#applyBulkParent')) {
            console.log('Apply Bulk Parent button clicked (delegated)');
            applyBulkParent();
        }

        // Handle Apply Bulk Active button click
        if (event.target.id === 'applyBulkActive' || event.target.closest('#applyBulkActive')) {
            console.log('Apply Bulk Active button clicked (delegated)');
            applyBulkActive();
        }
        /*
        // Handle Import Selected button click
        if (event.target.id === 'importSelectedBtn' || event.target.closest('#importSelectedBtn')) {
            console.log('Import Selected button clicked (delegated)');
            handleHDSImportProgress();
        */
    });

    // Also listen for the Bootstrap modal shown event
    document.body.addEventListener('shown.bs.modal', function (event) {
        // Check if this is our meter selection modal
        if (event.target.id === 'hdsMeterSelectionModal') {
            console.log('HDS Meter Selection Modal shown');
            initializeModal();
        }
    });

    // Setup filter functionality
    document.body.addEventListener('input', function (event) {
        if (event.target.id === 'filterInput') {
            console.log('Filter input changed');
            filterRows(event.target.value);
        }
    });

    // Setup checkbox change events using event delegation
    document.body.addEventListener('change', function (event) {
        if (event.target.classList.contains('meter-checkbox')) {
            handleCheckboxChange(event.target);
        }
    });

    // Try to initialize immediately if the modal is already in the DOM
    const modal = document.getElementById('hdsMeterSelectionModal');
    if (modal) {
        console.log('Modal already in DOM, initializing directly');
        // Use setTimeout to ensure this runs after everything else is loaded
        setTimeout(initializeModal, 100);
    }
});

/**
 * Initialize the modal with all required functionality
 */
function initializeModal() {
    console.log('Initializing HDS Meter Selection Modal');

    // Setup all checkboxes
    setupCheckboxes();

    // Update selection counter
    updateCounter();

    console.log('Modal initialization complete');
}

/**
 * Set up all checkboxes with proper initial state
 */
function setupCheckboxes() {
    const checkboxes = document.querySelectorAll('.meter-checkbox');
    console.log(`Found ${checkboxes.length} meter checkboxes`);

    checkboxes.forEach(function (checkbox) {
        // Set initial state of hidden input
        const hiddenInput = checkbox.nextElementSibling;
        if (hiddenInput && hiddenInput.type === 'hidden') {
            hiddenInput.disabled = checkbox.checked;
        }
    });
}

/**
 * Handle checkbox change events
 */
function handleCheckboxChange(checkbox) {
    console.log(`Checkbox changed: ${checkbox.id}, Checked: ${checkbox.checked}`);

    // Find corresponding hidden input and update its disabled state
    const hiddenInput = checkbox.nextElementSibling;
    if (hiddenInput && hiddenInput.type === 'hidden') {
        hiddenInput.disabled = checkbox.checked;
        console.log(`Updated hidden input disabled: ${hiddenInput.disabled}`);
    }

    // Update selection counter
    updateCounter();
}

/**
 * Update the selection counter display
 */
function updateCounter() {
    const checkboxes = document.querySelectorAll('.meter-checkbox');
    const checkedBoxes = document.querySelectorAll('.meter-checkbox:checked');
    console.log(`Selection count: ${checkedBoxes.length} of ${checkboxes.length}`);

    // Update selection status badge
    const statusBadge = document.getElementById('selectionStatus');
    if (statusBadge) {
        statusBadge.textContent = `Selected ${checkedBoxes.length} of ${checkboxes.length} meters`;
    }

    // Update import button text
    const importBtn = document.getElementById('importSelectedBtn');
    if (importBtn) {
        importBtn.textContent = checkedBoxes.length > 0 ?
            `Import Selected (${checkedBoxes.length})` :
            'Import Selected';
    }
}

/**
 * Handle Select All button click
 */
function handleSelectAll() {
    const checkboxes = document.querySelectorAll('.meter-checkbox');
    console.log(`Selecting all ${checkboxes.length} checkboxes`);

    checkboxes.forEach(function (checkbox) {
        checkbox.checked = true;

        // Disable corresponding hidden input
        const hiddenInput = checkbox.nextElementSibling;
        if (hiddenInput && hiddenInput.type === 'hidden') {
            hiddenInput.disabled = true;
        }
    });

    updateCounter();
}

/**
 * Handle Deselect All button click
 */
function handleDeselectAll() {
    const checkboxes = document.querySelectorAll('.meter-checkbox');
    console.log(`Deselecting all ${checkboxes.length} checkboxes`);

    checkboxes.forEach(function (checkbox) {
        checkbox.checked = false;

        // Enable corresponding hidden input
        const hiddenInput = checkbox.nextElementSibling;
        if (hiddenInput && hiddenInput.type === 'hidden') {
            hiddenInput.disabled = false;
        }
    });

    updateCounter();
}

/**
 * Filter the meter rows based on search text
 */
function filterRows(filterText) {
    filterText = filterText.toLowerCase();
    const rows = document.querySelectorAll('#hdsMeterTable tbody tr');
    let visibleCount = 0;

    rows.forEach(function (row) {
        // Get the meter name from the second cell
        const meterName = row.cells[1].textContent.toLowerCase();

        if (meterName.includes(filterText)) {
            row.style.display = '';
            visibleCount++;
        } else {
            row.style.display = 'none';
        }
    });

    // Update filter status
    const filterStatus = document.getElementById('filterStatus');
    if (filterStatus) {
        filterStatus.textContent = `Showing ${visibleCount} of ${rows.length} meters`;
    }
}

/**
 * Get the list of currently selected meter rows
 */
function getSelectedRows() {
    const selectedCheckboxes = document.querySelectorAll('.meter-checkbox:checked');
    const rows = [];

    selectedCheckboxes.forEach(function (checkbox) {
        // Extract the row index from the checkbox ID (format: meter_X)
        const rowIndex = checkbox.id.split('_')[1];
        if (rowIndex !== undefined) {
            rows.push(rowIndex);
        }
    });

    console.log(`Selected rows: ${rows.length}`);
    return rows;
}

/**
 * Apply the selected type to all selected meters
 */
function applyBulkType() {
    // Get selected type value
    let typeValue = '';
    const radioButtons = document.getElementsByName('bulkType');

    for (let i = 0; i < radioButtons.length; i++) {
        if (radioButtons[i].checked) {
            typeValue = radioButtons[i].value;
            break;
        }
    }

    if (!typeValue) {
        alert('Please select a type to apply.');
        return;
    }

    // Get selected rows
    const selectedRows = getSelectedRows();
    if (selectedRows.length === 0) {
        alert('Please select at least one meter first.');
        return;
    }

    console.log(`Applying type "${typeValue}" to ${selectedRows.length} rows`);

    // Apply to all selected rows
    selectedRows.forEach(function (rowIndex) {
        const typeSelect = document.querySelector(`select[name="SelectedMeters[${rowIndex}].Type"]`);
        if (typeSelect) {
            typeSelect.value = typeValue;
        }
    });
}

/**
 * Apply the selected parent to all selected meters
 */
function applyBulkParent() {
    // Get selected parent value
    const parentValue = document.getElementById('bulkParent').value;

    // Get selected rows
    const selectedRows = getSelectedRows();
    if (selectedRows.length === 0) {
        alert('Please select at least one meter first.');
        return;
    }

    console.log(`Applying parent "${parentValue}" to ${selectedRows.length} rows`);

    // Apply to all selected rows
    selectedRows.forEach(function (rowIndex) {
        const parentSelect = document.querySelector(`select[name="SelectedMeters[${rowIndex}].ParentMeterId"]`);
        if (parentSelect) {
            parentSelect.value = parentValue;
        }
    });
}

/**
 * Apply the selected active state to all selected meters
 */
function applyBulkActive() {
    // Get selected active value
    const activeValue = document.getElementById('bulkActive').checked;

    // Get selected rows
    const selectedRows = getSelectedRows();
    if (selectedRows.length === 0) {
        alert('Please select at least one meter first.');
        return;
    }

    console.log(`Applying active state "${activeValue}" to ${selectedRows.length} rows`);

    // Apply to all selected rows
    selectedRows.forEach(function (rowIndex) {
        const activeCheckbox = document.getElementById(`active_${rowIndex}`);
        if (activeCheckbox) {
            activeCheckbox.checked = activeValue;

            // Handle hidden input
            const hiddenInput = activeCheckbox.nextElementSibling;
            if (hiddenInput && hiddenInput.type === 'hidden') {
                hiddenInput.disabled = activeValue;
            }
        }
    });
}

/**
 * Handle the import process
 */
function handleHDSImportProgress() {
    const selectedCheckboxes = document.querySelectorAll('.meter-checkbox:checked');

    if (selectedCheckboxes.length === 0) {
        alert('Please select at least one meter to import.');
        return;
    }

    console.log(`Starting import progress animation for ${selectedCheckboxes.length} meters`);

    // Show progress container
    const progressContainer = document.getElementById('meterImportProgressContainer');
    if (progressContainer) {
        progressContainer.classList.remove('d-none');
    }

    // Reset progress bar
    const progressBar = document.getElementById('meterImportProgressBar');
    if (progressBar) {
        progressBar.style.width = '0%';
        progressBar.setAttribute('aria-valuenow', '0');
        progressBar.textContent = '0%';
    }

    // Update status text
    const statusText = document.getElementById('meterImportStatus');
    if (statusText) {
        statusText.textContent = 'Starting import...';
    }

    // Simulate progress (this is just UI animation, not real import)
    let progress = 0;
    const interval = setInterval(function () {
        progress += 5;

        if (progressBar) {
            progressBar.style.width = progress + '%';
            progressBar.setAttribute('aria-valuenow', progress);
            progressBar.textContent = progress + '%';
        }

        if (statusText) {
            const currentMeter = Math.ceil((progress / 100) * selectedCheckboxes.length);

            if (progress < 100) {
                statusText.textContent = `Importing meter ${currentMeter} of ${selectedCheckboxes.length}...`;
            } else {
                statusText.textContent = 'Import completed successfully!';
                clearInterval(interval);

                // Show success message
                setTimeout(function () {
                    alert(`Successfully imported ${selectedCheckboxes.length} meters.`);
                    // Optionally close the modal
                    // $('#hdsMeterSelectionModal').modal('hide');
                }, 500);
            }
        }
    }, 100);
}


// Make key functions available in the global scope for debugging
window.HDSMeterSelection = {
    initialize: initializeModal,
    selectAll: handleSelectAll,
    deselectAll: handleDeselectAll,
    updateCounter: updateCounter,
    checkboxStatus: function () {
        const checkboxes = document.querySelectorAll('.meter-checkbox');
        const checkedBoxes = document.querySelectorAll('.meter-checkbox:checked');
        console.log(`Checkbox status: ${checkedBoxes.length} checked of ${checkboxes.length} total`);
        return { checked: checkedBoxes.length, total: checkboxes.length };
    }
};

console.log('HDS Meter Selection JS initialization complete');

document.body.addEventListener('click', function (e) {
    // Use HDS-specific button ID
    if (e.target.id !== 'printSelectedHDSBtn') return;

    console.log('🔵 HDS Print Handler - Processing HDS meters...');
    handleHDSPrint();
});

function handleHDSPrint() {
    console.log('🔵 Starting HDS print process...');

    try {
        // Get all selected HDS meter checkboxes
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
        const hdsTableName = getCurrentHDSTableName();
        const hdsConnectionId = getCurrentHDSConnectionId();
        const importReadings = getImportReadingsCheckbox();
        const startDate = getHDSStartDate();
        const endDate = getHDSEndDate();

        // Prepare HDS-specific request
        const hdsRequest = {
            tableName: hdsTableName,
            connectionId: hdsConnectionId,
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
            if (!row) return;

            // Skip the info header row
            if (row.classList.contains('table-info')) {
                return;
            }

            const cells = row.cells;
            if (cells.length < 6) {
                console.warn(`Row ${index} has insufficient cells: ${cells.length}`);
                return;
            }

            // HDS Table Structure:
            // [0] = Checkbox
            // [1] = Meter Name (with <strong> tag)
            // [2] = Unit (input field)
            // [3] = Parent (select dropdown)
            // [4] = Type (select dropdown)
            // [5] = Active (checkbox)

            // Extract meter name from strong element (HDS-specific)
            const nameCell = cells[1];
            const strongElement = nameCell?.querySelector('strong');
            const hdsMeterName = strongElement ? strongElement.textContent.trim() : '';

            if (!hdsMeterName) {
                console.warn(`No HDS meter name found in row ${index}`);
                return;
            }

            // Extract unit from input field
            const unitInput = cells[2]?.querySelector('.meter-unit, input[type="text"]');
            const unit = unitInput ? unitInput.value.trim() : '';

            // Extract parent from select dropdown
            const parentSelect = cells[3]?.querySelector('.meter-parent, select');
            const parentMeterId = parentSelect ? parentSelect.value : '';

            // Extract type from select dropdown
            const typeSelect = cells[4]?.querySelector('.meter-type, select');
            const type = typeSelect ? typeSelect.value : 'main';

            // Extract active status from checkbox
            const activeCheckbox = cells[5]?.querySelector('.meter-active, input[type="checkbox"]');
            const active = activeCheckbox ? activeCheckbox.checked : true;

            // Try to get last reading from data attributes
            const lastReading = row.getAttribute('data-last-reading') ||
                nameCell.getAttribute('data-last-reading') ||
                strongElement.getAttribute('data-last-reading') || '';

            const meterData = {
                hdsMeterName: hdsMeterName,
                unit: unit,
                type: type,
                parentMeterId: parentMeterId,
                active: active,
                lastReading: lastReading,
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

function getCurrentHDSTableName() {
    const tableSelect = document.getElementById('hdsTable');
    return tableSelect ? tableSelect.value : 'UNKNOWN_TABLE';
}

function getCurrentHDSConnectionId() {
    const connectionSelect = document.getElementById('hdsConnection');
    return connectionSelect ? connectionSelect.value : 'default';
}

function getImportReadingsCheckbox() {
    const importReadingsCheck = document.getElementById('importReadings');
    return importReadingsCheck ? importReadingsCheck.checked : false;
}

function getHDSStartDate() {
    const startDateInput = document.getElementById('hdsStartDate');
    return startDateInput && startDateInput.value ? startDateInput.value : null;
}

function getHDSEndDate() {
    const endDateInput = document.getElementById('hdsEndDate');
    return endDateInput && endDateInput.value ? endDateInput.value : null;
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

// Debug function for testing HDS extraction
window.debugHDSExtraction = function () {
    console.log('🔧 DEBUG: Testing HDS meter extraction...');
    const selectedCheckboxes = document.querySelectorAll('.meter-checkbox:checked');
    console.log(`Found ${selectedCheckboxes.length} selected checkboxes`);

    const extractedData = extractHDSMeterData(selectedCheckboxes);
    console.log('Extracted HDS data:', extractedData);

    return extractedData;
};




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

// 🎯 UPDATED: Add this to the existing initializeEventHandlers() function in hdsMeterImport.js

function initializeEventHandlers() {
    console.log('🔧 Initializing event handlers');

    // Unified event delegation for all functionality
    document.body.addEventListener('click', function (event) {
        console.log('🔧 Click event detected on:', event.target.id, event.target.className);

        // Load Meters Button
        if (event.target.id === 'loadMetersBtn' || event.target.closest('#loadMetersBtn')) {
            console.log('🔧 Load Meters button clicked');
            handleLoadMeters(event);
        }

        // Select All / Deselect All buttons (unified for all types)
        if (event.target.id === 'selectAllMetersBtn' || event.target.closest('#selectAllMetersBtn')) {
            console.log('🔧 Select All button clicked');
            handleSelectAll();
        }

        if (event.target.id === 'deselectAllMetersBtn' || event.target.closest('#deselectAllMetersBtn')) {
            console.log('🔧 Deselect All button clicked');
            handleDeselectAll();
        }

        // 🎯 UNIFIED: Import Selected button (now handles all data types)
        if (event.target.id === 'importSelectedBtn' || event.target.closest('#importSelectedBtn')) {
            console.log('🔧 Import Selected button clicked');
            handleImport(); // Your updated function
        }

        // 🎯 UNIFIED: Print Button Handler
        if (event.target.id === 'printSelectedBtn' || event.target.closest('#printSelectedBtn')) {
            console.log('🔧 Unified Print Button clicked');
            handleUnifiedPrint();
        }
    });

    // 🎯 UPDATED: Change event delegation for all checkbox types
    document.body.addEventListener('change', function (event) {
        console.log('🔧 Change event detected on:', event.target.className);

        if (event.target.id === 'hdsConnection') {
            console.log('🔧 Connection selection changed');
            handleConnectionChange();
        }

        if (event.target.id === 'hdsTable') {
            console.log('🔧 Table selection changed');
            handleTableChange();
        }

        // 🎯 UPDATED: Handle both meter checkboxes and WebService variable checkboxes
        if (event.target.classList.contains('meter-checkbox') ||
            event.target.classList.contains('web-service-variable-checkbox')) {
            console.log('🔧 Checkbox changed:', event.target.className);
            updateMeterCounter(); // Updated function handles both types
        }
    });

    console.log('🔧 Event handlers initialized');
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

// =====================================================
// UPDATED METER COUNTER FUNCTION
// =====================================================

// =====================================================
// IMPORT FUNCTIONALITY
// =====================================================



// In hdsMeterImport.js - Update the handleHDSImport function

function handleHDSImport() {
    console.log('🔵 HDS Import button clicked');

    const selectedCheckboxes = document.querySelectorAll('.meter-checkbox:checked');
    if (selectedCheckboxes.length === 0) {
        alert('Please select at least one meter to import.');
        return;
    }

    console.log(`🔵 Starting import of ${selectedCheckboxes.length} HDS meters...`);

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

    console.log('🔵 Prepared meters for import:', meters);

    // ✅ Get import options (existing)
    const skipExisting = document.getElementById('skipExisting')?.checked || false;
    const updateExisting = document.getElementById('updateExisting')?.checked || false;
    const importReadings = document.getElementById('importReadings')?.checked || false;

    console.log('🔵 Import options:', {
        skipExisting,
        updateExisting,
        importReadings
    });

    // Prepare import request
    const hdsContext = window.currentHDSContext || {};
    const importData = {
        meters: meters,
        skipExisting: skipExisting,
        updateExisting: updateExisting,

        // ✅ NEW: Add readings import property
        importReadings: importReadings,

        connectionId: hdsContext.connectionId,
        tableName: hdsContext.tableName
    };

    console.log('🔵 Complete import data prepared:', importData);

    // Show loading state
    const importBtn = document.getElementById('importSelectedBtn');
    const originalText = importBtn.textContent;

    // ✅ NEW: Update button text based on what's being imported
    const loadingText = importReadings ?
        '<span class="spinner-border spinner-border-sm"></span> Importing Meters & Readings...' :
        '<span class="spinner-border spinner-border-sm"></span> Importing Meters...';

    importBtn.innerHTML = loadingText;
    importBtn.disabled = true;

    console.log('🔵 Sending request to /HdsImport/ImportMeters...');

    // Send import request
    fetch('/HdsImport/ImportMeters', {
        method: 'POST',
        headers: {
            'Content-Type': 'application/json'
        },
        body: JSON.stringify(importData)
    })
        .then(response => {
            console.log('🔵 Server response status:', response.status);

            if (!response.ok) {
                throw new Error(`Import failed: ${response.status} ${response.statusText}`);
            }
            return response.json();
        })
        .then(data => {
            console.log('🔵 Import response received:', data);

            // Reset button
            importBtn.textContent = originalText;
            importBtn.disabled = false;

            // ✅ NEW: Enhanced success message with readings info
            if (data.success) {
                console.log('✅ Import successful!');

                let message = `✅ Successfully imported ${data.importedCount || 0} meters!`;

                // Add readings information if applicable
                if (data.readingsEnabled) {
                    message += `\n📊 Readings: ${data.readingsImported || 0} imported`;
                }

                message += `\n\nDetails:`;
                message += `\n• Meters Imported: ${data.importedCount || 0}`;
                message += `\n• Meters Updated: ${data.updatedCount || 0}`;

                if (data.readingsEnabled) {
                    message += `\n• Readings Imported: ${data.readingsImported || 0}`;
                }

                message += `\n• Errors: ${data.errorCount || 0}`;

                if (data.details) {
                    console.log('📋 Detailed results:', data.details);
                }

                alert(message);

                if (confirm('Import completed! Would you like to reload the page to see the imported meters?')) {
                    window.location.reload();
                }
            } else {
                console.error('❌ Import failed:', data.error);
                let errorMessage = `❌ Import failed: ${data.error || 'Unknown error'}`;

                if (data.details && data.details.readingsMessage) {
                    errorMessage += `\n\nReadings: ${data.details.readingsMessage}`;
                }

                alert(errorMessage);
            }
        })
        .catch(error => {
            console.error('🔴 Import error:', error);

            // Reset button
            importBtn.textContent = originalText;
            importBtn.disabled = false;

            alert(`❌ Import error: ${error.message}\n\nCheck the browser console for more details.`);
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



// =============================================
// HDS DATE RANGE FUNCTIONALITY
// =============================================

function initializeHdsDateRange() {
        const now = new Date();
        const yesterday = new Date(now.getTime() - 24 * 60 * 60 * 1000);

        const endDateStr = formatDateTimeLocal(now);
        const startDateStr = formatDateTimeLocal(yesterday);

        document.getElementById('hdsEndDate').value = endDateStr;
        document.getElementById('hdsStartDate').value = startDateStr;

        console.log('🕒 Initialized HDS date range: Last 24 hours');
    }

function formatDateTimeLocal(date) {
        const year = date.getFullYear();
        const month = String(date.getMonth() + 1).padStart(2, '0');
        const day = String(date.getDate()).padStart(2, '0');
        const hours = String(date.getHours()).padStart(2, '0');
        const minutes = String(date.getMinutes()).padStart(2, '0');

        return `${year}-${month}-${day}T${hours}:${minutes}`;
    }

function setupHdsQuickRangeButtons() {
        document.getElementById('hdsQuickRange24h').addEventListener('click', function () {
            setHdsQuickRange(24, 'hours');
            highlightActiveHdsQuickRange(this);
        });

        document.getElementById('hdsQuickRange7d').addEventListener('click', function () {
            setHdsQuickRange(7, 'days');
            highlightActiveHdsQuickRange(this);
        });

        document.getElementById('hdsQuickRange30d').addEventListener('click', function () {
            setHdsQuickRange(30, 'days');
            highlightActiveHdsQuickRange(this);
        });
    }

function setHdsQuickRange(amount, unit) {
        const now = new Date();
        const startDate = new Date();

        if (unit === 'hours') {
            startDate.setHours(startDate.getHours() - amount);
        } else if (unit === 'days') {
            startDate.setDate(startDate.getDate() - amount);
        }

        document.getElementById('hdsEndDate').value = formatDateTimeLocal(now);
        document.getElementById('hdsStartDate').value = formatDateTimeLocal(startDate);

        console.log(`🕒 Set HDS quick range: Last ${amount} ${unit}`);
        validateHdsDateRange();
    }

function highlightActiveHdsQuickRange(activeButton) {
        document.querySelectorAll('#hdsQuickRange24h, #hdsQuickRange7d, #hdsQuickRange30d').forEach(btn => {
            btn.classList.remove('btn-secondary');
            btn.classList.add('btn-outline-secondary');
        });

        activeButton.classList.remove('btn-outline-secondary');
        activeButton.classList.add('btn-secondary');
    }

function setupHdsDateValidation() {
        const startDateInput = document.getElementById('hdsStartDate');
        const endDateInput = document.getElementById('hdsEndDate');

        startDateInput.addEventListener('change', validateHdsDateRange);
        endDateInput.addEventListener('change', validateHdsDateRange);
    }

function validateHdsDateRange() {
        const startDateInput = document.getElementById('hdsStartDate');
        const endDateInput = document.getElementById('hdsEndDate');

        const startDate = new Date(startDateInput.value);
        const endDate = new Date(endDateInput.value);

        startDateInput.classList.remove('is-invalid');
        endDateInput.classList.remove('is-invalid');

        if (startDate >= endDate) {
            endDateInput.classList.add('is-invalid');
            console.warn('⚠️ Invalid HDS date range: End date must be after start date');
            return false;
        }

        const oneYear = 365 * 24 * 60 * 60 * 1000;
        if (endDate - startDate > oneYear) {
            startDateInput.classList.add('is-invalid');
            endDateInput.classList.add('is-invalid');
            console.warn('⚠️ HDS date range too large: Maximum 1 year range allowed');
            return false;
        }

        console.log('✅ Valid HDS date range selected');
        return true;
    }