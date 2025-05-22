/**
 * hdsMeterImport.js - Complete implementation for HDS meter import functionality
 * This file handles all the functionality for the HDS meter selection and import modal
 */

// Wait for the document to be fully loaded
document.addEventListener('DOMContentLoaded', function () {
    console.log('HDS Meter Import JS loaded - DOM Content Loaded');

    // Initialize event handlers that need to be set up immediately
    initializeEventHandlers();

    // Try to initialize immediately if the modal is already in the DOM
    const modal = document.getElementById('hdsMeterSelectionModal');
    if (modal) {
        console.log('Modal already in DOM, initializing directly');
        // Use setTimeout to ensure this runs after everything else is loaded
        setTimeout(initializeModal, 100);
    }
});

/**
 * Set up all event handlers using event delegation
 */
function initializeEventHandlers() {
    // Set up event delegation for the entire document
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

        // Handle Import Selected button click
        if (event.target.id === 'importSelectedBtn' || event.target.closest('#importSelectedBtn')) {
            console.log('Import Selected button clicked (delegated)');
            handleImport();
        }

        // Handle Load Meters Button click
        if (event.target.id === 'loadMetersBtn' || event.target.closest('#loadMetersBtn')) {
            console.log('Load Meters button clicked (delegated)');
            handleLoadMeters(event);
        }
    });

    // Listen for the Bootstrap modal shown event
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
    // Prevent default behavior if this is a button click
    if (event) {
        event.preventDefault();
    }

    const button = document.getElementById('loadMetersBtn');
    if (!button) return;

    const tableName = document.getElementById('pcvueTable').value;
    const startDate = document.getElementById('importStartDate').value;
    const endDate = document.getElementById('importEndDate').value;
    const limit = document.getElementById('importLimit').value;

    if (!tableName) {
        alert('Please select a table to load meters from.');
        return;
    }

    // Show loading indicator
    button.innerHTML = '<span class="spinner-border spinner-border-sm" role="status" aria-hidden="true"></span> Loading...';
    button.disabled = true;

    // Build the query parameter string
    let queryParams = `tableName=${encodeURIComponent(tableName)}`;
    if (startDate) queryParams += `&startDate=${encodeURIComponent(startDate)}`;
    if (endDate) queryParams += `&endDate=${encodeURIComponent(endDate)}`;
    if (limit) queryParams += `&limit=${encodeURIComponent(limit)}`;

    // Fetch meters from the selected table
    fetch(`/HdsImport/GetMetersFromTable?${queryParams}`)
        .then(response => {
            if (!response.ok) {
                throw new Error(`Failed to load meters: ${response.statusText}`);
            }
            return response.text();
        })
        .then(html => {
            // Insert the meter selection modal into the page
            const container = document.getElementById('hdsMeterSelectionContainer');
            if (container) {
                container.innerHTML = html;
            } else {
                console.error('No container found for meter selection modal');
                throw new Error('Container element for meter selection modal not found');
            }

            // Show the modal
            const modalElement = document.getElementById('hdsMeterSelectionModal');
            if (modalElement) {
                const hdsMeterSelectionModal = new bootstrap.Modal(modalElement);
                hdsMeterSelectionModal.show();
            } else {
                console.error('Modal element not found after loading HTML');
                throw new Error('Modal element not found after loading HTML');
            }

            // Reset the button
            button.innerHTML = '<i class="bi bi-list"></i> Select Meters to Import';
            button.disabled = false;
        })
        .catch(error => {
            console.error('Error loading meters:', error);
            alert(`Error loading meters: ${error.message}`);

            // Reset the button
            button.innerHTML = '<i class="bi bi-list"></i> Select Meters to Import';
            button.disabled = false;
        });
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
/**
 * Import readings for the given meters
 */
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