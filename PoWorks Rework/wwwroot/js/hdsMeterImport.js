/**
 * hdsMeterImport.js - Complete implementation for HDS meter import functionality
 * This file handles all the functionality for the HDS meter selection and import modal
 */

// Wait for the document to be fully loaded
document.addEventListener('DOMContentLoaded', function () {
    console.log('HDS Meter Import JS loaded - DOM Content Loaded');

    // Initialize event handlers that need to be set up immediately
    initializeEventHandlers();

    loadSqlServerConnections();

    setupConnectionEventListeners();

    // Try to initialize immediately if the modal is already in the DOM
    const modal = document.getElementById('hdsMeterSelectionModal');
    if (modal) {
        console.log('Modal already in DOM, initializing directly');
        // Use setTimeout to ensure this runs after everything else is loaded
        setTimeout(initializeModal, 100);
    }
});

/**
 * Populate the connection dropdown
 */
function populateConnectionDropdown(connections) {
    const connectionSelect = document.getElementById('hdsConnection');
    if (!connectionSelect) return;

    // Clear existing options
    connectionSelect.innerHTML = '<option value="">Select a connection...</option>';

    // Add connections - SIMPLIFIED to show only connection name
    connections.forEach(connection => {
        const option = document.createElement('option');
        option.value = connection.connectionId;

        // Show only the connection name
        option.textContent = connection.connectionName || `Connection ${connection.connectionId}`;
        if (connection.isDefault) {
            option.textContent += ' [Default]';
        }

        connectionSelect.appendChild(option);
    });

    // Auto-select default connection if available
    const defaultConnection = connections.find(c => c.isDefault);
    if (defaultConnection) {
        connectionSelect.value = defaultConnection.connectionId;
        handleConnectionChange();
    }

    updateConnectionStatus('success', `${connections.length} connections available`);
}

/**
 * Handle connection selection change
 */
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
        // No connection selected
        tableSelect.disabled = true;
        tableSelect.innerHTML = '<option value="">Select a table...</option>';
        loadButton.disabled = true;
        updateConnectionStatus('info', 'Select a connection to continue');
        return;
    }

    // Enable table selection and load tables
    updateConnectionStatus('loading', 'Loading tables...');
    loadTablesForConnection(selectedConnectionId);
}

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

/**
 * Enhanced event listener setup
 */
function setupConnectionEventListeners() {
    // Connection dropdown change listener
    const connectionSelect = document.getElementById('hdsConnection');
    if (connectionSelect) {
        // Remove any existing listeners
        connectionSelect.removeEventListener('change', handleConnectionChange);
        // Add the listener
        connectionSelect.addEventListener('change', handleConnectionChange);
        console.log('Connection change listener added');
    } else {
        console.warn('Connection select element not found');
    }
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

    // Disable table selection while loading
    if (tableSelect) {
        tableSelect.disabled = true;
        tableSelect.innerHTML = '<option value="">Loading tables...</option>';
    }
    if (loadButton) {
        loadButton.disabled = true;
    }

    // Fetch tables from the server
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

                // Reset table dropdown
                if (tableSelect) {
                    tableSelect.innerHTML = '<option value="">Failed to load tables</option>';
                    tableSelect.disabled = true;
                }
            }
        })
        .catch(error => {
            console.error('Error loading tables:', error);
            updateConnectionStatus('error', `Error loading tables: ${error.message}`);

            // Reset table dropdown
            if (tableSelect) {
                tableSelect.innerHTML = '<option value="">Error loading tables</option>';
                tableSelect.disabled = true;
            }
        });
}

/**
 * Populate the table dropdown
 */
function populateTableDropdown(tables) {
    const tableSelect = document.getElementById('hdsTable');
    const loadButton = document.getElementById('loadMetersBtn');

    if (!tableSelect) {
        console.error('Table select element not found');
        return;
    }

    // Clear and populate tables
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

    // Enable table selection
    tableSelect.disabled = false;

    // Add event listener for table selection (remove any existing listeners first)
    tableSelect.removeEventListener('change', handleTableSelection);
    tableSelect.addEventListener('change', handleTableSelection);
}

/**
 * Handle table selection change
 */
function handleTableSelection() {
    const tableSelect = document.getElementById('hdsTable');
    const loadButton = document.getElementById('loadMetersBtn');

    if (loadButton) {
        loadButton.disabled = !tableSelect.value;
    }
}

/**
 * Update connection status display
 */
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


/**
 * Set up all event handlers using event delegation
 */
function initializeEventHandlers() {
    // Set up event delegation for the entire document
    document.body.addEventListener('click', function (event) {
        // Handle Load Meters Button click
        if (event.target.id === 'loadMetersBtn' || event.target.closest('#loadMetersBtn')) {
            console.log('Load Meters button clicked (delegated)');
            handleLoadMeters(event);
        }

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

        // Handle Import Selected button click
        if (event.target.id === 'importSelectedBtn' || event.target.closest('#importSelectedBtn')) {
            console.log('Import Selected button clicked (delegated)');
            handleImport();
        }
    });

    // Handle connection selection change
    document.body.addEventListener('change', function (event) {
        if (event.target.id === 'hdsConnection') {
            console.log('Connection selection changed');
            handleConnectionChange();
        }

        if (event.target.classList.contains('meter-checkbox')) {
            handleCheckboxChange(event.target);
        }
    });

    // Setup filter functionality
    document.body.addEventListener('input', function (event) {
        if (event.target.id === 'filterInput') {
            console.log('Filter input changed');
            filterRows(event.target.value);
        }
    });

    document.body.addEventListener('change', function (event) {
        if (event.target.id === 'hdsConnection') {
            console.log('Connection selection changed (delegated)');
            handleConnectionChange();
        }

        if (event.target.id === 'hdsTable') {
            console.log('Table selection changed (delegated)');
            handleTableSelection();
        }
    });

    // Listen for the Bootstrap modal shown event
    document.body.addEventListener('shown.bs.modal', function (event) {
        if (event.target.id === 'hdsMeterSelectionModal') {
            console.log('HDS Meter Selection Modal shown');
            initializeModal();
        }
    });
}

/**
 * Initialize the modal with all required functionality
 */
function initializeModal() {
    console.log('Initializing HDS Meter Selection Modal');

    // Setup all checkboxes
    setupCheckboxes();

    // Update selection counter
    updateCounter();

    // Link skipExisting and updateExisting checkboxes
    const skipExistingCheck = document.getElementById('skipExisting');
    const updateExistingCheck = document.getElementById('updateExisting');

    if (skipExistingCheck && updateExistingCheck) {
        updateExistingCheck.addEventListener('change', function () {
            if (this.checked) {
                skipExistingCheck.checked = true;
                skipExistingCheck.disabled = true;
            } else {
                skipExistingCheck.disabled = false;
            }
        });

        skipExistingCheck.addEventListener('change', function () {
            if (!this.checked) {
                updateExistingCheck.checked = false;
            }
        });
    }

    // Toggle readings date and limit fields based on importReadings checkbox
    const importReadingsCheck = document.getElementById('importReadings');
    const readingsDateControls = document.querySelectorAll('#readingsStartDate, #readingsEndDate, #readingsLimit');

    if (importReadingsCheck && readingsDateControls.length > 0) {
        // Set initial state
        const isEnabled = importReadingsCheck.checked;
        readingsDateControls.forEach(control => {
            control.disabled = !isEnabled;
        });

        // Add change listener
        importReadingsCheck.addEventListener('change', function () {
            readingsDateControls.forEach(control => {
                control.disabled = !this.checked;
            });
        });
    }

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
 * Load meters from PCVue HDS
 */
function handleLoadMeters(event) {
    console.log('Load Meters button clicked');

    const button = event.target.closest('#loadMetersBtn') || event.target;
    const connectionSelect = document.getElementById('hdsConnection');
    const tableSelect = document.getElementById('hdsTable');
    const startDateInput = document.getElementById('startDate');
    const endDateInput = document.getElementById('endDate');
    const limitInput = document.getElementById('limit');

    // Validate inputs
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

    // Build the query parameter string
    let queryParams = `tableName=${encodeURIComponent(tableName)}&connectionId=${encodeURIComponent(connectionId)}`;
    if (startDate) queryParams += `&startDate=${encodeURIComponent(startDate)}`;
    if (endDate) queryParams += `&endDate=${encodeURIComponent(endDate)}`;
    if (limit) queryParams += `&limit=${encodeURIComponent(limit)}`;

    console.log('Fetching meters with query:', queryParams);

    // Fetch meters from the selected table using the selected connection
    fetch(`/Import/GetMetersFromTable?${queryParams}`)
        .then(response => {
            console.log('Response status:', response.status);
            if (!response.ok) {
                throw new Error(`Failed to load meters: ${response.status} ${response.statusText}`);
            }
            return response.json(); // CORRECTED: Expect JSON, not HTML
        })
        .then(data => {
            console.log('Received JSON response:', data);

            if (!data.success) {
                throw new Error(data.error || 'Failed to load meters');
            }

            // Store parent options globally for table population
            window.parentOptions = data.parentOptions || [];

            // Use the existing function to show the meter selection table
            showHDSMeterSelection(data.meters, tableName, connectionId);

            // Reset the button
            button.innerHTML = originalButtonContent;
            button.disabled = false;

            console.log('Load Meters completed successfully');
        })
        .catch(error => {
            console.error('Error loading meters:', error);
            alert(`Error loading meters: ${error.message}`);

            // Reset the button
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

    // Show the existing meter selection section
    const meterSelectionSection = document.getElementById('meterSelectionSection');
    if (meterSelectionSection) {
        meterSelectionSection.classList.remove('d-none');
        console.log('Meter selection section shown');
    } else {
        console.error('Meter selection section not found!');
        return;
    }

    // Update the section title to indicate HDS source
    const sectionHeader = meterSelectionSection.querySelector('.card-header h5');
    if (sectionHeader) {
        sectionHeader.textContent = `HDS Meter Selection - ${tableName}`;
        console.log('Updated section header');
    }

    // 🎯 ADD THIS CALL: Switch to HDS print button
    switchToHDSPrintButton();

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

    // Update status
    const statusElement = document.getElementById('meterFilterStatus');
    if (statusElement) {
        statusElement.textContent = `Loaded ${meters.length} meters from ${tableName}`;
        console.log('Updated filter status');
    }

    console.log('HDS meter selection setup complete');

    // Setup event handlers for the controls
    setupHDSMeterEventHandlers();
}

function switchToHDSPrintButton() {
    const hdsBtn = document.getElementById('printSelectedHDSBtn');
    const varexpBtn = document.getElementById('printSelectedBtn');

    if (hdsBtn) {
        hdsBtn.classList.remove('d-none');
        console.log('🔵 HDS print button shown');
    }
    if (varexpBtn) {
        varexpBtn.classList.add('d-none');
        console.log('🔵 VAREXP print button hidden');
    }
}

/**
 * Handle filter input for HDS meters
 */
function handleHDSFilterInput(event) {
    const filterText = event.target.value.toLowerCase();
    const rows = document.querySelectorAll('#metersTableBody tr');
    let visibleCount = 0;

    rows.forEach(row => {
        // Skip the info header row
        if (row.classList.contains('table-info')) {
            return;
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

/**
 * Handle checkbox changes for HDS meters
 */
function handleHDSCheckboxChange(event) {
    if (event.target.classList.contains('meter-checkbox')) {
        updateHDSMeterCounter();
    }
}

/**
 * Handle import for HDS meters
 */
function handleHDSImport() {
    console.log('HDS Import button clicked');

    const selectedCheckboxes = document.querySelectorAll('.meter-checkbox:checked');
    if (selectedCheckboxes.length === 0) {
        alert('Please select at least one meter to import.');
        return;
    }

    // Gather meter data
    const meters = [];
    selectedCheckboxes.forEach((checkbox, index) => {
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
    const importData = {
        meters: meters,
        skipExisting: skipExisting,
        updateExisting: updateExisting,
        importReadings: importReadings,
        connectionId: getCurrentConnectionId(),
        tableName: getCurrentTableName()
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

                // Optionally reload page or update UI
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

/**
 * Helper functions to get current connection and table info
 */
function getCurrentConnectionId() {
    const connectionSelect = document.getElementById('hdsConnection');
    return connectionSelect ? connectionSelect.value : '';
}

function getCurrentTableName() {
    const tableSelect = document.getElementById('hdsTable');
    return tableSelect ? tableSelect.value : '';
}

function handleHDSSelectAll() {
    console.log('HDS Select All clicked');
    const checkboxes = document.querySelectorAll('.meter-checkbox');

    checkboxes.forEach(checkbox => {
        checkbox.checked = true;
    });

    updateHDSMeterCounter();
}

/**
 * Handle Deselect All for HDS meters
 */
function handleHDSDeselectAll() {
    console.log('HDS Deselect All clicked');
    const checkboxes = document.querySelectorAll('.meter-checkbox');

    checkboxes.forEach(checkbox => {
        checkbox.checked = false;
    });

    updateHDSMeterCounter();
}

function setupHDSMeterEventHandlers() {
    console.log('Setting up HDS meter event handlers');

    // Select All button
    const selectAllBtn = document.getElementById('selectAllMetersBtn');
    if (selectAllBtn) {
        selectAllBtn.removeEventListener('click', handleHDSSelectAll);
        selectAllBtn.addEventListener('click', handleHDSSelectAll);
    }

    // Deselect All button
    const deselectAllBtn = document.getElementById('deselectAllMetersBtn');
    if (deselectAllBtn) {
        deselectAllBtn.removeEventListener('click', handleHDSDeselectAll);
        deselectAllBtn.addEventListener('click', handleHDSDeselectAll);
    }

    // Filter input
    const filterInput = document.getElementById('meterFilterInput');
    if (filterInput) {
        filterInput.removeEventListener('input', handleHDSFilterInput);
        filterInput.addEventListener('input', handleHDSFilterInput);
    }

    // Import button
    const importBtn = document.getElementById('importSelectedBtn');
    if (importBtn) {
        importBtn.removeEventListener('click', handleHDSImport);
        importBtn.addEventListener('click', handleHDSImport);
    }

    // Setup delegation for dynamically created checkboxes
    document.body.removeEventListener('change', handleHDSCheckboxChange);
    document.body.addEventListener('change', handleHDSCheckboxChange);

    console.log('HDS meter event handlers setup complete');
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

        // Append row to table
        tbody.appendChild(row);
    });

    console.log(`Table populated with ${meters.length} meters`);

    // Update counter
    updateHDSMeterCounter();
}

function updateHDSMeterCounter() {
    const checkboxes = document.querySelectorAll('.meter-checkbox');
    const checkedBoxes = document.querySelectorAll('.meter-checkbox:checked');

    // Update filter status to show selection count
    const statusElement = document.getElementById('meterFilterStatus');
    if (statusElement) {
        const totalMeters = checkboxes.length - 1; // Subtract 1 for header row
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

    console.log(`Counter updated: ${checkedBoxes.length} selected of ${checkboxes.length - 1} total`);
}


/**
 * Gather data from all selected meters
 */
function gatherSelectedMetersData() {
    const selectedMeters = [];
    const selectedRows = getSelectedRows();

    console.log(`Gathering data for ${selectedRows.length} selected rows`);

    selectedRows.forEach(function (rowIndex) {
        try {
            // Get meter data from form fields
            const hdsMeterName = document.querySelector(`input[name="SelectedMeters[${rowIndex}].HdsMeterName"]`).value;
            const unit = document.querySelector(`input[name="SelectedMeters[${rowIndex}].Unit"]`).value || '';
            const parentMeterId = document.querySelector(`select[name="SelectedMeters[${rowIndex}].ParentMeterId"]`).value;
            const type = document.querySelector(`select[name="SelectedMeters[${rowIndex}].Type"]`).value;

            // Make sure we get a boolean value for active
            const activeCheckbox = document.getElementById(`active_${rowIndex}`);
            const active = activeCheckbox ? activeCheckbox.checked : true;

            console.log(`Meter ${hdsMeterName}: type=${type}, parentMeterId=${parentMeterId}, active=${active}`);

            selectedMeters.push({
                hdsMeterName: hdsMeterName,
                unit: unit,
                parentMeterId: parentMeterId,
                type: type,
                active: active
            });
        } catch (error) {
            console.error(`Error gathering data for row ${rowIndex}:`, error);
        }
    });

    console.log(`Successfully gathered data for ${selectedMeters.length} meters`);
    return selectedMeters;
}

/**
 * Display a detailed error or success message
 */
function showDetailedMessage(data) {
    // Format a detailed message
    let message = '';

    if (data.success) {
        message = `Successfully imported ${data.importedCount} meters.`;
    } else {
        message = `Import completed with ${data.errorCount} errors:\n\n`;

        // Add detailed error messages if available
        if (data.detailedErrors && Object.keys(data.detailedErrors).length > 0) {
            for (const [meterName, errorMsg] of Object.entries(data.detailedErrors)) {
                message += `• ${meterName}: ${errorMsg}\n\n`;
            }
        } else if (data.errorMeters && data.errorMeters.length > 0) {
            message += `Errors occurred on: ${data.errorMeters.join(', ')}\n\n`;
        }

        if (data.errorMessage) {
            message += `Error message: ${data.errorMessage}`;
        }
    }

    // Display the message in a custom modal for better formatting
    const errorModalElement = document.getElementById('importErrorModal');
    if (errorModalElement) {
        const errorContent = document.getElementById('importErrorContent');
        if (errorContent) {
            // Format the message for HTML display (convert newlines to <br>)
            errorContent.innerHTML = message.replace(/\n/g, '<br>');
            const errorModal = new bootstrap.Modal(errorModalElement);
            errorModal.show();
        } else {
            // Fallback to alert if modal content element not found
            alert(message);
        }
    } else {
        // Fallback to regular alert if no custom modal exists
        alert(message);
    }
}

/**
 * Handle the import process
 */

function handleImport() {
    const selectedCheckboxes = document.querySelectorAll('.meter-checkbox:checked');
    if (selectedCheckboxes.length === 0) {
        alert('Please select at least one meter to import.');
        return;
    }

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
        statusText.textContent = 'Preparing to import meters...';
    }

    // Gather import data
    const tableName = document.getElementById('hdsMeterSelectionModal').getAttribute('data-table-name');
    const skipExisting = document.getElementById('skipExisting').checked;
    const updateExisting = document.getElementById('updateExisting').checked;
    const createMissingParents = document.getElementById('createMissingParents').checked;

    // Check if readings import is enabled
    const importReadings = document.getElementById('importReadings')?.checked || false;

    // Gather meter data and keep track of meter names
    const meters = [];
    const meterNames = [];

    selectedCheckboxes.forEach((checkbox) => {
        const row = checkbox.closest('tr');
        const meterName = row.querySelector('td:nth-child(2)').textContent.trim();
        const meterUnit = row.querySelector('input[name$=".Unit"]').value;
        const parentMeterId = row.querySelector('select[name$=".ParentMeterId"]').value;
        const type = row.querySelector('select[name$=".Type"]').value;
        const active = row.querySelector('input[type="checkbox"][name$=".Active"]').checked;
        const lastReading = row.querySelector('input[name$=".LastReading"]') ?
            row.querySelector('input[name$=".LastReading"]').value : '0';

        meters.push({
            hdsMeterName: meterName,
            unit: meterUnit,
            parentMeterId: parentMeterId,
            type: type,
            active: active,
            lastReading: lastReading
        });

        meterNames.push(meterName);
    });

    // Update status
    if (statusText) {
        statusText.textContent = `Importing ${meters.length} meters...`;
    }

    // Log what we're doing
    console.log('Importing meters with options:', {
        meterCount: meters.length,
        skipExisting,
        updateExisting,
        createMissingParents,
        importReadings
    });

    // Send import request for meters
    fetch('/Import/ImportMeters', {
        method: 'POST',
        headers: {
            'Content-Type': 'application/json'
        },
        body: JSON.stringify({
            tableName: tableName,
            meters: meters,
            skipExisting: skipExisting,
            updateExisting: updateExisting,
            createMissingParents: createMissingParents
        })
    })
        .then(response => {
            if (!response.ok) {
                throw new Error(`Server returned ${response.status}: ${response.statusText}`);
            }
            return response.json();
        })
        .then(data => {
            console.log('Meter import response:', data);

            // Update progress - 50% if we'll import readings, 100% otherwise
            if (progressBar) {
                const progressValue = importReadings ? 50 : 100;
                progressBar.style.width = `${progressValue}%`;
                progressBar.setAttribute('aria-valuenow', progressValue.toString());
                progressBar.textContent = `${progressValue}%`;
            }

            // Update status text
            if (statusText) {
                if (data.success) {
                    const statusMsg = `Meters imported: ${data.importedCount}, updated: ${data.updatedCount}, skipped: ${data.skippedCount}`;
                    if (importReadings) {
                        statusText.textContent = `${statusMsg}. Now importing readings...`;
                    } else {
                        statusText.textContent = statusMsg;
                    }
                } else {
                    statusText.textContent = `Import completed with ${data.errorCount || 0} errors.`;
                }
            }

            // If meter import was successful and we want to import readings
            if (data.success && importReadings) {
                console.log('Starting readings import for meters:', meterNames);

                // We'll add a small delay to ensure the UI updates
                setTimeout(() => {
                    // Call our importMeterReadings function 
                    // NOTE: Make sure this function exists!
                    importMeterReadings(meterNames, tableName);
                }, 500);
            } else {
                // Either the meter import failed, or we don't need to import readings
                console.log('Not importing readings:', {
                    success: data.success,
                    importReadings,
                    metersImportedOrUpdated: (data.importedCount > 0 || data.updatedCount > 0)
                });

                // If everything was successful, close the modal after a delay
                if (data.success && data.errorCount === 0) {
                    setTimeout(() => {
                        const modal = bootstrap.Modal.getInstance(document.getElementById('hdsMeterSelectionModal'));
                        if (modal) {
                            modal.hide();
                        }
                        // Reload the page to show the imported meters
                        window.location.reload();
                    }, 2000);
                }
            }
        })
        .catch(error => {
            console.error('Error importing meters:', error);

            // Update status text
            if (statusText) {
                statusText.textContent = `Error importing meters: ${error.message}`;
            }

            // Show error message
            alert(`Error importing meters: ${error.message}`);
        });
}

/**
 * Import readings for the given meters
 */

function importMeterReadings(meterNames, tableName) {
    console.log('importMeterReadings called with:', {
        meterNames,
        tableName
    });

    if (!meterNames || meterNames.length === 0) {
        console.warn('No meters provided for readings import');
        alert('No meters selected for readings import');
        return;
    }

    // Get the progress elements
    const progressBar = document.getElementById('meterImportProgressBar');
    const statusText = document.getElementById('meterImportStatus');

    // Update status
    if (statusText) {
        statusText.textContent = `Importing readings for ${meterNames.length} meters...`;
    }

    // Prepare the request body
    const requestBody = {
        tableName: tableName,
        meterNames: meterNames
    };

    // Add optional date range if available
    const startDate = document.getElementById('readingsStartDate')?.value;
    const endDate = document.getElementById('readingsEndDate')?.value;
    const limit = document.getElementById('readingsLimit')?.value;

    if (startDate) requestBody.startDate = startDate;
    if (endDate) requestBody.endDate = endDate;
    if (limit) requestBody.limit = parseInt(limit);

    console.log('Sending readings import request:', requestBody);

    // Send the request
    fetch('/Import/ImportMeterReadings', {
        method: 'POST',
        headers: {
            'Content-Type': 'application/json'
        },
        body: JSON.stringify(requestBody)
    })
        .then(response => {
            console.log('ImportMeterReadings response status:', response.status);
            if (!response.ok) {
                throw new Error(`Server returned ${response.status}: ${response.statusText}`);
            }
            return response.json();
        })
        .then(data => {
            console.log('ImportMeterReadings response data:', data);

            // Update progress to 100%
            if (progressBar) {
                progressBar.style.width = '100%';
                progressBar.setAttribute('aria-valuenow', '100');
                progressBar.textContent = '100%';
            }

            // Update status text
            if (statusText) {
                statusText.textContent = `Readings import completed: ${data.message || 'No details available'}`;
            }

            // Show alert with results
            alert(`Readings import completed: ${data.message || 'Operation completed'}`);

            // Close modal and reload page after successful import
            setTimeout(() => {
                const modal = bootstrap.Modal.getInstance(document.getElementById('hdsMeterSelectionModal'));
                if (modal) {
                    modal.hide();
                }
                // Reload the page to show the imported meters
                window.location.reload();
            }, 2000);
        })
        .catch(error => {
            console.error('Error importing readings:', error);

            // Update progress bar to indicate error
            if (progressBar) {
                progressBar.classList.add('bg-danger');
            }

            // Update status text
            if (statusText) {
                statusText.textContent = `Error importing readings: ${error.message}`;
            }

            // Show error message
            alert(`Error importing readings: ${error.message}`);
        });
}

// Helper function to show detailed import results
function showDetailedMessage(data) {
    // Format a detailed message
    let message = '';

    if (data.success) {
        message = `Successfully imported ${data.importedCount} meters, updated ${data.updatedCount}, skipped ${data.skippedCount}.`;
    } else {
        message = `Import completed with ${data.errorCount} errors:\n\n`;

        // Add detailed error messages if available
        if (data.detailedErrors && Object.keys(data.detailedErrors).length > 0) {
            for (const [meterName, errorMsg] of Object.entries(data.detailedErrors)) {
                message += `• ${meterName}: ${errorMsg}\n\n`;
            }
        } else if (data.errorMeters && data.errorMeters.length > 0) {
            message += `Errors occurred on: ${data.errorMeters.join(', ')}\n\n`;
        }

        if (data.errorMessage) {
            message += `Error message: ${data.errorMessage}`;
        }
    }

    // Display the message in a modal or alert
    const resultModal = document.getElementById('importResultModal');
    if (resultModal) {
        const resultContent = document.getElementById('importResultContent');
        if (resultContent) {
            // Format the message for HTML display (convert newlines to <br>)
            resultContent.innerHTML = message.replace(/\n/g, '<br>');
            const modal = new bootstrap.Modal(resultModal);
            modal.show();
        } else {
            // Fallback to alert if modal content element not found
            alert(message);
        }
    } else {
        // Fallback to regular alert if no modal exists
        alert(message);
    }
}

// Make key functions available in the global scope
window.HDSMeterImport = {
    initialize: initializeModal,
    selectAll: handleSelectAll,
    deselectAll: handleDeselectAll,
    updateCounter: updateCounter,
    filterRows: filterRows,
    applyBulkType: applyBulkType,
    applyBulkParent: applyBulkParent,
    applyBulkActive: applyBulkActive,
    handleImport: handleImport,
    loadMeters: handleLoadMeters,
    gatherSelectedMetersData: gatherSelectedMetersData
};