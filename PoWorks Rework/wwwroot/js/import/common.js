// =====================================================
// DOM EVENT INITIALIZATION
// =====================================================

/**
 * Initializes various DOM events and input validation
 * when the document has finished loading.
 */
document.addEventListener('DOMContentLoaded', function () {
    // Let each module (hds_import.js, webservices_import.js, etc.) handle their own initialization
    setupUnifiedEventDelegation();
});

// =====================================================
// UNIFIED EVENT DELEGATION
// =====================================================

/**
 * Sets up unified event delegation for all shared buttons
 * This ensures the shared functions are called regardless of which module is active
 */
function setupUnifiedEventDelegation() {
    // console.log('🔧 Setting up unified event delegation');

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

    const dataType = window.currentMeterDataType || 'UNKNOWN';

    switch (dataType) {
        case 'HDS':           
            if (typeof handleHDSPrint === 'function') {
                handleHDSPrint();
            } else {
                alert('HDS print functionality not available');
            }
            break;

        case 'VAREXP':
            if (typeof handleVarexpPrint === 'function') {
                handleVarexpPrint();
            } else {
                alert('VAREXP print functionality not available');
            }
            break;

        case 'WebService':
            if (typeof handleWebServicePrint === 'function') {
                const wsCheckboxes = document.querySelectorAll('.web-service-variable-checkbox:checked');
                if (wsCheckboxes.length > 0) {
                    handleWebServicePrint();
                } else {
                    alert('Please select at least one Web Service variable to print.');
                }
            } else {
                alert('WebService print functionality not available');
            }
            break;

        default:

            // 🎯 ENHANCED FALLBACK: Check for WebService content first
            const webServiceTable = document.querySelector('.web-service-meter-selection-container');
            if (webServiceTable) {
                window.currentMeterDataType = 'WebService';
                handleUnifiedPrint(); // Retry with detected type
                return;
            }

            const tableBody = document.getElementById('metersTableBody');
            if (tableBody) {
                const headerRow = tableBody.querySelector('.table-info');
                if (headerRow && headerRow.textContent.includes('HDS Import')) {
                    window.currentMeterDataType = 'HDS';
                    updatePrintButtonForDataType('HDS');
                    handleUnifiedPrint(); // Retry with detected type
                } else if (headerRow && headerRow.textContent.includes('VAREXP Import')) {
                    window.currentMeterDataType = 'VAREXP';
                    updatePrintButtonForDataType('VAREXP');
                    handleUnifiedPrint(); // Retry with detected type
                } else if (headerRow && headerRow.textContent.includes('Web Service Import')) {
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
    const dataType = window.currentMeterDataType || 'UNKNOWN';
    let checkboxes;

    if (dataType === 'WebService') {
        checkboxes = document.querySelectorAll('.web-service-variable-checkbox');
    } else {
        checkboxes = document.querySelectorAll('.meter-checkbox');
    }

    checkboxes.forEach(checkbox => {
        checkbox.checked = true;
    });

    updateMeterCounter();
}

/**
 * Deselects all checkboxes for the current meter data type
 * and updates the selection counter.
 */
function handleDeselectAll() {
    const dataType = window.currentMeterDataType || 'UNKNOWN';
    let checkboxes;

    if (dataType === 'WebService') {
        checkboxes = document.querySelectorAll('.web-service-variable-checkbox');
    } else {
        checkboxes = document.querySelectorAll('.meter-checkbox');
    }

    checkboxes.forEach(checkbox => {
        checkbox.checked = false;
    });

    updateMeterCounter();
}

// =====================================================
// COUNTER UPDATE
// =====================================================

/**
 * Updates the counter display showing how many meters/variables
 * are selected out of the total available.
 */
function updateMeterCounter() {
    const dataType = window.currentMeterDataType || 'UNKNOWN';
    let checkboxes, checkedBoxes;

    if (dataType === 'WebService') {
        checkboxes = document.querySelectorAll('.web-service-variable-checkbox');
        checkedBoxes = document.querySelectorAll('.web-service-variable-checkbox:checked');
    } else {
        checkboxes = document.querySelectorAll('.meter-checkbox');
        checkedBoxes = document.querySelectorAll('.meter-checkbox:checked');
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
    } 
}

// =====================================================
// IMPORT HANDLER
// =====================================================

/**
 * Handles importing data for the current meter data type
 * by routing to the appropriate import function.
 */
function handleImport() {

    const dataType = window.currentMeterDataType || 'UNKNOWN';
   
    if (dataType === 'HDS') {       
        if (typeof handleHDSImport === 'function') {
            handleHDSImport();
        } else {
            alert('HDS import functionality not available');
        }
    } else if (dataType === 'VAREXP') {        
        if (typeof handleVarexpImport === 'function') {
            handleVarexpImport();
        } else {
            alert('VAREXP import functionality not available');
        }
    } else if (dataType === 'WebService') {

        if (typeof importWebServiceVariables === 'function') {           
            importWebServiceVariables();
        } else {
            alert('WebService import functionality not available');
        }
    } else {
        alert('Unknown data type. Please reload the meters and try again.');
    }
}