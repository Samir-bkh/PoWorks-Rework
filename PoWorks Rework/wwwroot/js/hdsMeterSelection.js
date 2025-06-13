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



// —————————————————————————————————————————————————————
// Before the printForm actually submits, build our hidden inputs
// —————————————————————————————————————————————————————
// at the bottom of hdsMeterSelection.js

document.body.addEventListener('click', function (e) {
    if (e.target.id !== 'printSelectedBtn') return;

    console.log('🔥 ===== COMPREHENSIVE HDS PRINT DEBUG START =====');
    console.log('🔥 Print button clicked, starting comprehensive debugging...');

    // ✅ DEBUG 1: Check what elements exist
    console.log('🔍 DEBUG 1: Checking page elements...');
    const meterSelectionSection = document.getElementById('meterSelectionSection');
    const sectionHeader = meterSelectionSection?.querySelector('.card-header h5');
    const metersTableBody = document.getElementById('metersTableBody');

    console.log('🔍 meterSelectionSection exists:', !!meterSelectionSection);
    console.log('🔍 sectionHeader exists:', !!sectionHeader);
    console.log('🔍 sectionHeader text:', sectionHeader?.textContent);
    console.log('🔍 metersTableBody exists:', !!metersTableBody);

    // ✅ DEBUG 2: Check if this is HDS context
    const isHDSContext = sectionHeader && sectionHeader.textContent.includes('HDS Meter Selection');
    console.log('🔍 DEBUG 2: Is HDS context?', isHDSContext);

    if (!isHDSContext) {
        console.log('🔍 Not HDS context, exiting HDS handler');
        return;
    }

    console.log('🖨️ HDS context confirmed - continuing with HDS print handler');

    // ✅ DEBUG 3: Find all checkboxes and their states
    console.log('🔍 DEBUG 3: Analyzing checkboxes...');
    const allCheckboxes = document.querySelectorAll('.meter-checkbox');
    const sectionCheckboxes = document.querySelectorAll('#meterSelectionSection .meter-checkbox');
    const checkedCheckboxes = document.querySelectorAll('.meter-checkbox:checked');
    const sectionCheckedCheckboxes = document.querySelectorAll('#meterSelectionSection .meter-checkbox:checked');

    console.log('🔍 Total .meter-checkbox elements found:', allCheckboxes.length);
    console.log('🔍 Checkboxes in #meterSelectionSection:', sectionCheckboxes.length);
    console.log('🔍 Total checked checkboxes:', checkedCheckboxes.length);
    console.log('🔍 Checked checkboxes in section:', sectionCheckedCheckboxes.length);

    // ✅ DEBUG 4: Examine first few checkboxes in detail
    console.log('🔍 DEBUG 4: Examining first 3 checkboxes in detail...');
    sectionCheckboxes.forEach((checkbox, index) => {
        if (index < 3) {
            console.log(`🔍 Checkbox ${index}:`, {
                id: checkbox.id,
                checked: checkbox.checked,
                className: checkbox.className,
                dataAttributes: {
                    'data-meter-name': checkbox.getAttribute('data-meter-name')
                },
                parentElement: checkbox.parentElement?.innerHTML
            });
        }
    });

    if (sectionCheckedCheckboxes.length === 0) {
        console.error('🔥 CRITICAL: No checked checkboxes found in section!');
        alert('No meters selected. Check console for detailed debugging info.');
        return;
    }

    // ✅ DEBUG 5: Examine table structure
    console.log('🔍 DEBUG 5: Examining table structure...');
    const tableRows = document.querySelectorAll('#metersTableBody tr');
    console.log('🔍 Total rows in table:', tableRows.length);

    tableRows.forEach((row, index) => {
        if (index < 3) {
            console.log(`🔍 Row ${index}:`, {
                classList: Array.from(row.classList),
                cellCount: row.cells.length,
                innerHTML: row.innerHTML.substring(0, 200) + '...'
            });
        }
    });

    // ✅ DEBUG 6: Process selected checkboxes with detailed logging
    console.log('🔍 DEBUG 6: Processing selected checkboxes...');
    const selectedMeterNames = [];
    const selectedMeterTypes = [];
    const selectedMeterUnits = [];

    sectionCheckedCheckboxes.forEach(function (checkbox, index) {
        console.log(`🔍 Processing checkbox ${index}:`, checkbox.id);

        const row = checkbox.closest('tr');
        if (!row) {
            console.error(`🔥 ERROR: No row found for checkbox ${index}`);
            return;
        }

        console.log(`🔍 Row ${index} found:`, {
            classList: Array.from(row.classList),
            cellCount: row.cells.length
        });

        // Skip header row
        if (row.classList.contains('table-info')) {
            console.log(`🔍 Skipping header row ${index}`);
            return;
        }

        // ✅ DEBUG 7: Try multiple methods to get meter name
        console.log(`🔍 DEBUG 7: Extracting meter name for row ${index}...`);

        // Method 1: data-meter-name attribute
        const dataAttrName = checkbox.getAttribute('data-meter-name');
        console.log(`🔍 Method 1 - data-meter-name: "${dataAttrName}"`);

        // Method 2: strong element
        const nameCell = row.cells[1];
        console.log(`🔍 Name cell exists:`, !!nameCell);
        if (nameCell) {
            console.log(`🔍 Name cell HTML:`, nameCell.innerHTML);
        }

        const strongElement = nameCell?.querySelector('strong');
        const strongName = strongElement?.textContent.trim() || '';
        console.log(`🔍 Method 2 - strong element: "${strongName}"`);

        // Method 3: hidden input
        const hiddenInput = nameCell?.querySelector('.meter-name');
        const hiddenName = hiddenInput?.value || '';
        console.log(`🔍 Method 3 - hidden input: "${hiddenName}"`);

        // Method 4: entire cell text
        const cellText = nameCell?.textContent.trim() || '';
        console.log(`🔍 Method 4 - cell text: "${cellText}"`);

        // Choose best name
        const meterName = dataAttrName || strongName || hiddenName || cellText;
        console.log(`🔍 Final meter name chosen: "${meterName}"`);

        if (!meterName) {
            console.error(`🔥 ERROR: No meter name found for checkbox ${index} using any method!`);
            return;
        }

        // ✅ DEBUG 8: Get unit and type with detailed logging
        console.log(`🔍 DEBUG 8: Getting unit and type for row ${index}...`);

        const unitInput = row.querySelector('.meter-unit');
        const meterUnit = unitInput?.value?.trim() || '';
        console.log(`🔍 Unit input found:`, !!unitInput);
        console.log(`🔍 Unit value: "${meterUnit}"`);

        const typeSelect = row.querySelector('.meter-type');
        const meterType = typeSelect?.value || 'main';
        console.log(`🔍 Type select found:`, !!typeSelect);
        console.log(`🔍 Type value: "${meterType}"`);

        // Add to arrays
        selectedMeterNames.push(meterName);
        selectedMeterTypes.push(meterType);
        selectedMeterUnits.push(meterUnit);

        console.log(`✅ Successfully processed meter ${index + 1}: Name="${meterName}", Type="${meterType}", Unit="${meterUnit}"`);
    });

    // ✅ DEBUG 9: Final data validation
    console.log('🔍 DEBUG 9: Final data validation...');
    console.log('🔍 Final collected data:', {
        names: selectedMeterNames,
        types: selectedMeterTypes,
        units: selectedMeterUnits,
        totalCount: selectedMeterNames.length
    });

    if (selectedMeterNames.length === 0) {
        console.error('🔥 CRITICAL ERROR: No valid meter names collected!');
        console.log('🔥 This suggests the DOM structure is different than expected.');
        alert('No valid HDS meters found. Check console for detailed debugging info.');
        return;
    }

    // ✅ DEBUG 10: Get table name
    console.log('🔍 DEBUG 10: Getting table name...');
    let tableName = '';

    try {
        tableName = getCurrentTableName();
        console.log('🔍 getCurrentTableName() returned:', tableName);
    } catch (error) {
        console.error('🔥 Error calling getCurrentTableName():', error);
    }

    if (!tableName) {
        tableName = 'TRENDTABLE1';
        console.log('🔍 Using fallback table name:', tableName);
    }

    // ✅ DEBUG 11: Prepare request
    console.log('🔍 DEBUG 11: Preparing request...');
    const requestData = {
        tableName: tableName,
        selectedMeterNames: selectedMeterNames,
        selectedMeterTypes: selectedMeterTypes,
        selectedMeterUnits: selectedMeterUnits
    };

    console.log('🔍 Request data prepared:', requestData);

    // ✅ DEBUG 12: Send request
    console.log('🔍 DEBUG 12: Sending request to server...');

    fetch('/Import/PrintSelectedMeters', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(requestData)
    })
        .then(response => {
            console.log('🔍 Server response status:', response.status);
            console.log('🔍 Server response ok:', response.ok);
            return response.json();
        })
        .then(data => {
            console.log('🔍 Server response data:', data);

            if (data.success) {
                console.log('✅ SUCCESS: Print request completed successfully');
                alert(`Successfully printed ${data.count} HDS meters to console.`);
            } else {
                console.error('🔥 Server reported failure:', data.error);
                alert('HDS Print failed: ' + (data.error || 'Unknown error'));
            }
        })
        .catch(error => {
            console.error('🔥 Request failed with error:', error);
            alert('HDS Print request failed: ' + error.message);
        });

    console.log('🔥 ===== COMPREHENSIVE HDS PRINT DEBUG END =====');
});