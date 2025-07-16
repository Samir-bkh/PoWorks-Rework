// =====================================================
// DOM EVENT INITIALIZATION
// =====================================================

/**
 * Initializes various DOM events and input validation
 * when the document has finished loading.
 */
document.addEventListener('DOMContentLoaded', function () {
    console.log('🚀 Common.js loaded - Setting up unified event handlers');

    // 🔧 SIMPLIFIED: Only handle unified functionality in common.js
    // Let each module (hds_import.js, webservices_import.js, etc.) handle their own initialization
    setupUnifiedEventDelegation();
});

// =====================================================
// 🔧 ADD: UNIFIED EVENT DELEGATION
// =====================================================

/**
 * Sets up unified event delegation for all shared buttons
 * This ensures the shared functions are called regardless of which module is active
 */
function setupUnifiedEventDelegation() {
    console.log('🔧 Setting up unified event delegation');

    // Single event delegation for the entire document
    document.body.addEventListener('click', function (event) {
        // Unified Print Button
        if (event.target.id === 'printSelectedBtn' || event.target.closest('#printSelectedBtn')) {
            console.log('🎯 Unified Print Button clicked');
            event.preventDefault();
            handleUnifiedPrint();
            return;
        }

        // Unified Import Button
        if (event.target.id === 'importSelectedBtn' || event.target.closest('#importSelectedBtn')) {
            console.log('🔧 Unified Import Button clicked');
            event.preventDefault();
            handleImport();
            return;
        }

        // Unified Select All Button
        if (event.target.id === 'selectAllBtn' || event.target.closest('#selectAllBtn')) {
            console.log('🔧 Unified Select All Button clicked');
            event.preventDefault();
            handleSelectAll();
            return;
        }

        // Unified Deselect All Button
        if (event.target.id === 'deselectAllBtn' || event.target.closest('#deselectAllBtn')) {
            console.log('🔧 Unified Deselect All Button clicked');
            event.preventDefault();
            handleDeselectAll();
            return;
        }
    });

    // Unified checkbox change delegation
    document.body.addEventListener('change', function (event) {
        if (event.target.classList.contains('meter-checkbox') ||
            event.target.classList.contains('web-service-variable-checkbox')) {
            console.log('🔧 Checkbox changed, updating counter');
            updateMeterCounter();
        }
    });

    console.log('✅ Unified event delegation setup complete');
}

// =====================================================
// GLOBAL EXPORTS
// =====================================================

/**
 * Handles unified printing by detecting the current meter data type
 * and routing the request to the corresponding print handler.
 */
// Can be removed from Prod - only for Debugging 
function handleUnifiedPrint() {
    console.log('🎯 Enhanced Unified Print Handler - Detecting data type...');

    const dataType = window.currentMeterDataType || 'UNKNOWN';
    console.log(`🎯 Current data type: ${dataType}`);

    switch (dataType) {
        case 'HDS':
            console.log('🔵 Routing to HDS print handler');
            if (typeof handleHDSPrint === 'function') {
                handleHDSPrint();
            } else {
                console.error('🔴 HDS print handler not found');
                alert('HDS print functionality not available');
            }
            break;

        case 'VAREXP':
            console.log('🟨 Routing to VAREXP print handler');
            if (typeof handleVarexpPrint === 'function') {
                handleVarexpPrint();
            } else {
                console.error('🔴 VAREXP print handler not found');
                alert('VAREXP print functionality not available');
            }
            break;

        case 'WebService':
            console.log('🟠 Routing to WebService print handler');
            if (typeof handleWebServicePrint === 'function') {
                const wsCheckboxes = document.querySelectorAll('.web-service-variable-checkbox:checked');
                if (wsCheckboxes.length > 0) {
                    handleWebServicePrint();
                } else {
                    alert('Please select at least one Web Service variable to print.');
                }
            } else {
                console.error('🔴 WebService print handler not found');
                alert('WebService print functionality not available');
            }
            break;

        default:
            console.error('🔴 Unknown data type - attempting enhanced fallback detection');

            // 🎯 ENHANCED FALLBACK: Check for WebService content first
            const webServiceTable = document.querySelector('.web-service-meter-selection-container');
            if (webServiceTable) {
                console.log('🟠 Fallback detected WebService from container class');
                window.currentMeterDataType = 'WebService';
                handleUnifiedPrint(); // Retry with detected type
                return;
            }

            const tableBody = document.getElementById('metersTableBody');
            if (tableBody) {
                const headerRow = tableBody.querySelector('.table-info');
                if (headerRow && headerRow.textContent.includes('HDS Import')) {
                    console.log('🔵 Fallback detected HDS from table header');
                    window.currentMeterDataType = 'HDS';
                    updatePrintButtonForDataType('HDS');
                    handleUnifiedPrint(); // Retry with detected type
                } else if (headerRow && headerRow.textContent.includes('VAREXP Import')) {
                    console.log('🟨 Fallback detected VAREXP from table header');
                    window.currentMeterDataType = 'VAREXP';
                    updatePrintButtonForDataType('VAREXP');
                    handleUnifiedPrint(); // Retry with detected type
                } else if (headerRow && headerRow.textContent.includes('Web Service Import')) {
                    console.log('🟠 Fallback detected WebService from table header');
                    window.currentMeterDataType = 'WebService';
                    updatePrintButtonForDataType('WebService');
                    handleUnifiedPrint(); // Retry with detected type
                } else {
                    alert('Cannot determine data type. Please reload the meters and try again.');
                }
            } else {
                alert('No meter data found. Please load meters first.');
            }
            break;
    }
}

/**
 * Updates the print button text and style based on the current data type.
 * @param {string} dataType - The detected data type (HDS, VAREXP, WebService, etc.)
 */

// Can be removed from Prod - only for Debugging 
function updatePrintButtonForDataType(dataType) {
    const printBtn = document.getElementById('printSelectedBtn');

    if (printBtn) {
        if (dataType === 'HDS') {
            printBtn.innerHTML = '<i class="bi bi-printer"></i> Print HDS Meters';
            printBtn.className = 'btn btn-info';
        } else if (dataType === 'VAREXP') {
            printBtn.innerHTML = '<i class="bi bi-printer"></i> Print VAREXP Meters';
            printBtn.className = 'btn btn-secondary';
        } else if (dataType === 'WebService') {
            printBtn.innerHTML = '<i class="bi bi-printer"></i> Print Web Service Variables';
            printBtn.className = 'btn btn-outline-info';
        } else {
            printBtn.innerHTML = '<i class="bi bi-printer"></i> Print Selected';
            printBtn.className = 'btn btn-info';
        }

        console.log(`🎯 Updated print button for ${dataType} data type`);
    }
}

// =====================================================
// SELECT / DESELECT HANDLERS
// =====================================================

/**
 * Selects all checkboxes for the current meter data type
 * and updates the selection counter.
 */
function handleSelectAll() {
    console.log('🔧 Select All clicked');

    const dataType = window.currentMeterDataType || 'UNKNOWN';
    console.log('🔧 Data type for select all:', dataType);

    let checkboxes;

    if (dataType === 'WebService') {
        checkboxes = document.querySelectorAll('.web-service-variable-checkbox');
        console.log('🔧 Found WebService checkboxes:', checkboxes.length);
    } else {
        checkboxes = document.querySelectorAll('.meter-checkbox');
        console.log('🔧 Found standard checkboxes:', checkboxes.length);
    }

    checkboxes.forEach(checkbox => {
        checkbox.checked = true;
    });

    updateMeterCounter();
    console.log(`🔧 Selected all ${checkboxes.length} items for ${dataType}`);
}

/**
 * Deselects all checkboxes for the current meter data type
 * and updates the selection counter.
 */
function handleDeselectAll() {
    console.log('🔧 Deselect All clicked');

    const dataType = window.currentMeterDataType || 'UNKNOWN';
    console.log('🔧 Data type for deselect all:', dataType);

    let checkboxes;

    if (dataType === 'WebService') {
        checkboxes = document.querySelectorAll('.web-service-variable-checkbox');
        console.log('🔧 Found WebService checkboxes:', checkboxes.length);
    } else {
        checkboxes = document.querySelectorAll('.meter-checkbox');
        console.log('🔧 Found standard checkboxes:', checkboxes.length);
    }

    checkboxes.forEach(checkbox => {
        checkbox.checked = false;
    });

    updateMeterCounter();
    console.log(`🔧 Deselected all ${checkboxes.length} items for ${dataType}`);
}

// =====================================================
// COUNTER UPDATE
// =====================================================

/**
 * Updates the counter display showing how many meters/variables
 * are selected out of the total available.
 */
function updateMeterCounter() {
    console.log('🔧 updateMeterCounter called');

    const dataType = window.currentMeterDataType || 'UNKNOWN';
    console.log('🔧 Current data type:', dataType);

    let checkboxes, checkedBoxes;

    if (dataType === 'WebService') {
        checkboxes = document.querySelectorAll('.web-service-variable-checkbox');
        checkedBoxes = document.querySelectorAll('.web-service-variable-checkbox:checked');
        console.log('🔧 WebService checkboxes found:', checkboxes.length, 'checked:', checkedBoxes.length);
    } else {
        checkboxes = document.querySelectorAll('.meter-checkbox');
        checkedBoxes = document.querySelectorAll('.meter-checkbox:checked');
        console.log('🔧 Standard checkboxes found:', checkboxes.length, 'checked:', checkedBoxes.length);
    }

    const statusElement = document.getElementById('meterFilterStatus');
    if (statusElement) {
        const totalItems = checkboxes.length;
        const selectedItems = checkedBoxes.length;

        if (dataType === 'WebService') {
            statusElement.textContent = `Selected ${selectedItems} of ${totalItems} variables`;
        } else {
            statusElement.textContent = `Selected ${selectedItems} of ${totalItems} meters`;
        }
        console.log('🔧 Updated status element');
    } else {
        console.log('🔧 Status element not found');
    }

    const importBtn = document.getElementById('importSelectedBtn');
    if (importBtn) {
        if (dataType === 'WebService') {
            importBtn.innerHTML = checkedBoxes.length > 0
                ? `<i class="bi bi-cloud-upload"></i> Import Selected Variables (${checkedBoxes.length})`
                : '<i class="bi bi-cloud-upload"></i> Import Selected Variables';
        } else {
            importBtn.innerHTML = checkedBoxes.length > 0
                ? `<i class="bi bi-cloud-upload"></i> Import Selected (${checkedBoxes.length})`
                : '<i class="bi bi-cloud-upload"></i> Import Selected';
        }
        console.log('🔧 Updated import button text');
    } else {
        console.log('🔧 Import button not found');
    }

    console.log(`🔧 Counter updated: ${checkedBoxes.length} selected of ${checkboxes.length} total (${dataType})`);
}

// =====================================================
// IMPORT HANDLER
// =====================================================

/**
 * Handles importing data for the current meter data type
 * by routing to the appropriate import function.
 */
function handleImport() {
    console.log('🔧 handleImport called!');

    const dataType = window.currentMeterDataType || 'UNKNOWN';
    console.log('🔧 Import for', dataType, 'data type');

    if (dataType === 'HDS') {
        console.log('🔧 Routing to HDS import handler');
        if (typeof handleHDSImport === 'function') {
            handleHDSImport();
        } else {
            console.error('🔧 HDS import handler not found');
            alert('HDS import functionality not available');
        }
    } else if (dataType === 'VAREXP') {
        console.log('🔧 Routing to VAREXP import handler');
        if (typeof handleVarexpImport === 'function') {
            handleVarexpImport();
        } else {
            console.error('🔧 VAREXP import handler not found');
            alert('VAREXP import functionality not available');
        }
    } else if (dataType === 'WebService') {
        console.log('🔧 Routing to WebService import handler');

        if (typeof importWebServiceVariables === 'function') {
            console.log('🔧 importWebServiceVariables function found, calling it...');
            importWebServiceVariables();
        } else {
            console.error('🔧 WebService import handler not found');
            console.log('🔧 Available functions:', Object.getOwnPropertyNames(window).filter(name => name.includes('import')));
            alert('WebService import functionality not available');
        }
    } else {
        console.error('🔧 Unknown data type:', dataType);
        alert('Unknown data type. Please reload the meters and try again.');
    }
}

console.log('✅ Common.js initialization complete with unified event delegation');