/**
 * HDS Meter Import Functionality
 * 
 * This script handles the complete process of importing meters and their
 * readings from PCVue HDS SQL Server database into the PoWorks PostgreSQL database.
 * 
 * Features:
 * - Select/deselect meters from HDS database
 * - Configure meter properties (type, parent, units, etc.)
 * - Import meters to the Meters table
 * - Import historical readings to the MeterReadings table
 * - Progress tracking and error handling
 */

// Wait for the document to be fully loaded
document.addEventListener('DOMContentLoaded', function () {
    console.log('HDS Meter Import JS loaded');

    // Set up event delegation for the parent page
    document.body.addEventListener('click', function (event) {
        // Handle Select All button click
        if (event.target.id === 'selectAllBtn' || event.target.closest('#selectAllBtn')) {
            console.log('Select All button clicked');
            handleSelectAll();
        }

        // Handle Deselect All button click
        if (event.target.id === 'deselectAllBtn' || event.target.closest('#deselectAllBtn')) {
            console.log('Deselect All button clicked');
            handleDeselectAll();
        }

        // Handle Apply Bulk Type button click
        if (event.target.id === 'applyBulkType' || event.target.closest('#applyBulkType')) {
            console.log('Apply Bulk Type button clicked');
            applyBulkType();
        }

        // Handle Apply Bulk Parent button click
        if (event.target.id === 'applyBulkParent' || event.target.closest('#applyBulkParent')) {
            console.log('Apply Bulk Parent button clicked');
            applyBulkParent();
        }

        // Handle Apply Bulk Active button click
        if (event.target.id === 'applyBulkActive' || event.target.closest('#applyBulkActive')) {
            console.log('Apply Bulk Active button clicked');
            applyBulkActive();
        }

        // Handle Import Selected button click
        if (event.target.id === 'importSelectedBtn' || event.target.closest('#importSelectedBtn')) {
            console.log('Import Selected button clicked');
            handleImport();
        }

        // Handle Load Meters Button click
        if (event.target.id === 'loadMetersBtn' || event.target.closest('#loadMetersBtn')) {
            console.log('Load Meters button clicked');
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

    // Initialize date pickers with default values
    initializeDateRange();
});

/**
 * Initialize date picker fields with default values
 */
function initializeDateRange() {
    const today = new Date();
    const oneMonthAgo = new Date();
    oneMonthAgo.setMonth(today.getMonth() - 1);

    // Format dates as ISO strings (YYYY-MM-DD)
    const endDateStr = today.toISOString().split('T')[0];
    const startDateStr = oneMonthAgo.toISOString().split('T')[0];

    // Set default date range for import
    const startDateInput = document.getElementById('importStartDate');
    const endDateInput = document.getElementById('importEndDate');

    if (startDateInput) startDateInput.value = startDateStr;
    if (endDateInput) endDateInput.value = endDateStr;
}

/**
 * Load meters from the selected HDS table
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
            document.getElementById('hdsMeterSelectionContainer').innerHTML = html;

            // Show the modal
            const hdsMeterSelectionModal = new bootstrap.Modal(document.getElementById('hdsMeterSelectionModal'));
            hdsMeterSelectionModal.show();

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
function handleImport() {
    const selectedCheckboxes = document.querySelectorAll('.meter-checkbox:checked');

    if (selectedCheckboxes.length === 0) {
        alert('Please select at least one meter to import.');
        return;
    }

    console.log(`Starting import of ${selectedCheckboxes.length} meters`);

    // Gather all selected meters data
    const selectedMeters = gatherSelectedMetersData();
    if (!selectedMeters.length) {
        alert('Error collecting meter data. Please try again.');
        return;
    }

    console.log(`Successfully collected ${selectedMeters.length} meters data`);

    // Get import options
    const tableName = document.getElementById('hdsMeterSelectionModal').getAttribute('data-table-name');
    const skipExisting = document.getElementById('skipExisting')?.checked || false;
    const updateExisting = document.getElementById('updateExisting')?.checked || false;
    const createMissingTenants = document.getElementById('createMissingTenants')?.checked || false;
    const importReadings = document.getElementById('importReadings')?.checked || true;
    const startDate = document.getElementById('readingsStartDate')?.value || '';
    const endDate = document.getElementById('readingsEndDate')?.value || '';
    const limit = parseInt(document.getElementById('readingsLimit')?.value || '1000');

    // Create import request payload
    const importRequest = {
        meters: selectedMeters,
        tableName: tableName,
        options: {
            skipExisting: skipExisting,
            updateExisting: updateExisting,
            createMissingParents: true,
            createMissingTenants: createMissingTenants,
            importReadings: importReadings,
            readingsStartDate: startDate,
            readingsEndDate: endDate,
            readingsLimit: limit
        }
    };

    console.log('Import request:', JSON.stringify(importRequest, null, 2));

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

    // Disable the import button to prevent multiple clicks
    const importBtn = document.getElementById('importSelectedBtn');
    if (importBtn) {
        importBtn.disabled = true;
        importBtn.innerHTML = '<span class="spinner-border spinner-border-sm" role="status" aria-hidden="true"></span> Importing...';
    }

    // Perform the import
    fetch('/HdsImport/ImportMeters', {
        method: 'POST',
        headers: {
            'Content-Type': 'application/json',
            'Accept': 'application/json'
        },
        body: JSON.stringify(importRequest)
    })
        .then(response => {
            if (!response.ok) {
                const contentType = response.headers.get("content-type");
                if (contentType && contentType.indexOf("application/json") !== -1) {
                    return response.json().then(data => {
                        throw new Error(`Import failed: ${data.errorMessage || response.statusText}`);
                    });
                } else {
                    return response.text().then(text => {
                        throw new Error(`Import failed: ${text || response.statusText}`);
                    });
                }
            }
            return response.json();
        })
        .then(data => {
            console.log('Import response:', data);

            if (data.success) {
                // Update progress to 100%
                if (progressBar) {
                    progressBar.style.width = '100%';
                    progressBar.setAttribute('aria-valuenow', '100');
                    progressBar.textContent = '100%';
                }

                if (statusText) {
                    statusText.textContent = `Import completed: ${data.importedCount} meters imported, ${data.errorCount} errors.`;
                }

                // Show success message with details
                setTimeout(function () {
                    // Format a detailed message
                    let message = `Successfully imported ${data.importedCount} meters`;
                    if (data.errorCount > 0) {
                        message += ` with ${data.errorCount} errors.\n\nErrors occurred on: ${data.errorMeters.join(', ')}`;
                    } else {
                        message += '.';
                    }

                    if (data.importedReadings) {
                        message += `\n\nAlso imported ${data.importedReadings} historical readings.`;
                    }

                    alert(message);

                    // Close the modal
                    const modal = bootstrap.Modal.getInstance(document.getElementById('hdsMeterSelectionModal'));
                    if (modal) {
                        modal.hide();
                    }

                    // Optionally refresh the page or table
                    window.location.href = '/Meter/Management';
                }, 1000);
            } else {
                // Show error message
                if (statusText) {
                    statusText.textContent = `Import failed: ${data.errorMessage}`;
                }

                // Re-enable the import button
                if (importBtn) {
                    importBtn.disabled = false;
                    importBtn.textContent = 'Import Selected';
                }

                // Show alert with error
                alert(`Import failed: ${data.errorMessage}`);
            }
        })
        .catch(error => {
            console.error('Error during import:', error);

            // Show error in status
            if (statusText) {
                statusText.textContent = `Error: ${error.message}`;
            }

            // Re-enable the import button
            if (importBtn) {
                importBtn.disabled = false;
                importBtn.textContent = 'Import Selected';
            }

            // Show alert with error
            alert(`Import failed: ${error.message}`);
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
 * Add event handler for when the modal is about to close
 * This is useful to confirm if the user wants to discard changes
 */
document.addEventListener('DOMContentLoaded', function () {
    document.body.addEventListener('hide.bs.modal', function (event) {
        if (event.target.id === 'hdsMeterSelectionModal') {
            // Check if there are selected meters
            const selectedCheckboxes = document.querySelectorAll('.meter-checkbox:checked');
            if (selectedCheckboxes.length > 0) {
                // Only prompt if user hasn't just imported (check if progress is 100%)
                const progressBar = document.getElementById('meterImportProgressBar');
                if (!progressBar || progressBar.style.width !== '100%') {
                    if (!confirm('You have selected meters that have not been imported. Are you sure you want to close this window?')) {
                        event.preventDefault();
                    }
                }
            }
        }
    });
});

// Make key functions available in the global scope for debugging and external access
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
    loadMeters: handleLoadMeters
};

console.log('HDS Meter Import JS initialization complete');