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

        // Handle Import Selected button click
        if (event.target.id === 'importSelectedBtn' || event.target.closest('#importSelectedBtn')) {
            console.log('Import Selected button clicked (delegated)');
            handleImport();
        }
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
function handleImport() {
    const selectedCheckboxes = document.querySelectorAll('.meter-checkbox:checked');

    if (selectedCheckboxes.length === 0) {
        alert('Please select at least one meter to import.');
        return;
    }

    console.log(`Starting import of ${selectedCheckboxes.length} meters`);

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

    // Simulate progress (would be replaced with actual AJAX call)
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