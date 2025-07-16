function handleUnifiedPrint() {
    console.log('🎯 Enhanced Unified Print Handler - Detecting data type...');

    const dataType = window.currentMeterDataType || 'UNKNOWN';
    console.log(`🎯 Current data type: ${dataType}`);

    switch (dataType) {
        case 'HDS':
            console.log('🔵 Routing to HDS print handler');
            handleHDSPrint();
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
            // 🎯 ENHANCED: Check for WebService function AND checkboxes
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
                handleWebServicePrint();
                return;
            }

            // Original fallback for HDS/VAREXP
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
        } else if (dataType === 'WebService') {
            printBtn.innerHTML = '<i class="bi bi-printer"></i> Print Web Service Variables';
            printBtn.className = 'btn btn-outline-info'; // Light blue for WebService
        } else {
            // Default fallback for unknown data types
            printBtn.innerHTML = '<i class="bi bi-printer"></i> Print Selected';
            printBtn.className = 'btn btn-info';
        }

        console.log(`🎯 Updated print button for ${dataType} data type`);
    }
}


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




function updateMeterCounter() {
    console.log('🔧 updateMeterCounter called');

    const dataType = window.currentMeterDataType || 'UNKNOWN';
    console.log('🔧 Current data type:', dataType);

    let checkboxes, checkedBoxes;

    if (dataType === 'WebService') {
        // WebService uses different checkbox classes
        checkboxes = document.querySelectorAll('.web-service-variable-checkbox');
        checkedBoxes = document.querySelectorAll('.web-service-variable-checkbox:checked');
        console.log('🔧 WebService checkboxes found:', checkboxes.length, 'checked:', checkedBoxes.length);
    } else {
        // HDS and VAREXP use standard meter-checkbox class
        checkboxes = document.querySelectorAll('.meter-checkbox');
        checkedBoxes = document.querySelectorAll('.meter-checkbox:checked');
        console.log('🔧 Standard checkboxes found:', checkboxes.length, 'checked:', checkedBoxes.length);
    }

    // Update filter status to show selection count
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

    // Update import button text
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



function handleImport() {
    console.log('🔧 handleImport called!');

    const dataType = window.currentMeterDataType || 'UNKNOWN';
    console.log('🔧 Import for', dataType, 'data type');

    if (dataType === 'HDS') {
        console.log('🔧 Routing to HDS import handler');
        handleHDSImport();
    } else if (dataType === 'VAREXP') {
        console.log('🔧 Routing to VAREXP import handler');
        // This will be handled by import.js
        if (typeof handleVarexpImport === 'function') {
            handleVarexpImport();
        } else {
            console.error('🔧 VAREXP import handler not found');
            alert('VAREXP import functionality not available');
        }
    } else if (dataType === 'WebService') {
        console.log('🔧 Routing to WebService import handler');

        // Check if function exists
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


document.addEventListener('DOMContentLoaded', function () {
    // Initialize date ranges
    initializeHdsDateRange();

    // Set up quick range buttons
    setupHdsQuickRangeButtons();

    // Validate date range on input
    setupHdsDateValidation();

    // Load web service connections when page loads
    loadWebServiceConnections();

    // Handle web service connection change
    document.getElementById('webServiceConnection').addEventListener('change', function () {
        const selectedConnection = this.value;
        const browseBtn = document.getElementById('browseVariablesBtn');
        const statusSpan = document.getElementById('webServiceConnectionStatus');

        if (selectedConnection) {
            browseBtn.disabled = false;
            statusSpan.innerHTML = '<i class="bi bi-check-circle text-success"></i> Connection selected - ready to browse variables';
        } else {
            browseBtn.disabled = true;
            statusSpan.innerHTML = '<i class="bi bi-info-circle"></i> Select a web service connection';
        }
    });

    // Handle browse variables button click
    document.getElementById('browseVariablesBtn').addEventListener('click', function () {
        const selectedConnection = document.getElementById('webServiceConnection').value;
        if (!selectedConnection) {
            showWebServiceStatus('warning', 'Please select a web service connection first');
            return;
        }

        browseVariables(selectedConnection);
    });

    // Handle input validation for max variables
    document.getElementById('maxVariables').addEventListener('input', function () {
        const value = parseInt(this.value);
        if (value < 1) {
            this.value = 1;
        } else if (value > 1000000) {
            this.value = 1000000;
        }
    });
});