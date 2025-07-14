/**
 * Web Service Variable to Meter Selection Functionality
 * This file handles the meter selection table after browsing PCVue web service variables
 */

/**
 * Create the meter selection table from browsed web service variables
 */
/**
 * Create the meter selection table from browsed web service variables
 * UPDATED: Now compatible with unified import button system
 */
// 🎯 REPLACE the existing createWebServiceMeterSelectionTable function with this:

function createWebServiceMeterSelectionTable(variables, parentOptions, connectionInfo) {
    console.log('🔧 Creating Web Service meter selection table with', variables.length, 'variables');

    if (!variables || variables.length === 0) {
        console.warn('🔧 No variables provided for Web Service meter selection table');
        return '<p class="text-warning">No variables found to display.</p>';
    }

    // Build parent options HTML
    let parentOptionsHtml = '<option value="">None</option>';
    if (parentOptions && parentOptions.length > 0) {
        parentOptions.forEach(option => {
            if (option.value !== '') { // Skip the "None" option as we already added it
                parentOptionsHtml += `<option value="${option.value}">${option.text}</option>`;
            }
        });
    }

    // 🎯 UPDATED: Build table that works with shared button structure
    let tableHtml = `
        <div class="table-responsive" style="max-height: 500px; overflow-y: auto;">
            <table class="table table-sm table-striped table-hover">
                <thead class="sticky-top">
                    <tr>
                        <th style="width: 50px; background-color: #e9ecef !important; color: black !important; border-color: #dee2e6 !important;">
                            <span class="text-dark fw-bold">Import</span>
                        </th>
                        <th style="width: 60px; background-color: #e9ecef !important; color: black !important; border-color: #dee2e6 !important;"><span class="text-dark fw-bold">Type</span></th>
                        <th style="width: 300px; background-color: #e9ecef !important; color: black !important; border-color: #dee2e6 !important;"><span class="text-dark fw-bold">Variable Name</span></th>
                        <th style="width: 150px; background-color: #e9ecef !important; color: black !important; border-color: #dee2e6 !important;"><span class="text-dark fw-bold">Unit</span></th>
                        <th style="width: 100px; background-color: #e9ecef !important; color: black !important; border-color: #dee2e6 !important;"><span class="text-dark fw-bold">Meter Type</span></th>
                        <th style="width: 200px; background-color: #e9ecef !important; color: black !important; border-color: #dee2e6 !important;"><span class="text-dark fw-bold">Parent Meter</span></th>
                        <th style="width: 80px; background-color: #e9ecef !important; color: black !important; border-color: #dee2e6 !important;"><span class="text-dark fw-bold">Active</span></th>
                        <th style="width: 100px; background-color: #e9ecef !important; color: black !important; border-color: #dee2e6 !important;"><span class="text-dark fw-bold">PCVue Type</span></th>
                        <th style="width: 80px; background-color: #e9ecef !important; color: black !important; border-color: #dee2e6 !important;"><span class="text-dark fw-bold">Read Only</span></th>
                    </tr>
                </thead>
                <tbody id="metersTableBody">`;

    // Add info row
    tableHtml += `
        <tr class="table-info">
            <td colspan="9">
                <small>
                    <strong>Web Service Import:</strong> Converting ${variables.length} PCVue variables to meters. 
                    Connection: ${connectionInfo?.connectionName || 'Unknown'} | 
                    Fill in units and configure meter settings as needed.
                </small>
            </td>
        </tr>`;

    // Add variable rows
    variables.forEach((variable, index) => {
        console.log(`🔧 Processing variable ${index}:`, variable.variableName);

        // Determine variable type badge
        let typeBadge = 'TXT';
        let badgeClass = 'bg-secondary';

        if (variable.variableType) {
            switch (variable.variableType.toLowerCase()) {
                case 'numeric':
                case 'real':
                case 'double':
                    typeBadge = 'NUM';
                    badgeClass = 'bg-primary';
                    break;
                case 'boolean':
                case 'bool':
                    typeBadge = 'BOOL';
                    badgeClass = 'bg-warning text-dark';
                    break;
                case 'string':
                case 'text':
                    typeBadge = 'TXT';
                    badgeClass = 'bg-secondary';
                    break;
                default:
                    typeBadge = variable.variableType.substring(0, 4).toUpperCase();
                    badgeClass = 'bg-info';
            }
        }

        tableHtml += `
            <tr class="web-service-variable-row" data-variable-index="${index}">
                <td>
                    <input type="checkbox" class="form-check-input meter-checkbox web-service-variable-checkbox" checked data-variable-index="${index}">
                </td>
                <td>
                    <span class="badge ${badgeClass}">${typeBadge}</span>
                </td>
                <td class="text-break">
                    <small>${variable.variableName || 'Unknown'}</small>
                </td>
                <td>
                    <input type="text" class="form-control form-control-sm web-service-unit-input" 
                           placeholder="Enter unit (e.g., kWh, °C)" data-variable-index="${index}">
                </td>
                <td>
                    <select class="form-select form-select-sm web-service-type-select" data-variable-index="${index}">
                        <option value="main" selected>Main</option>
                        <option value="sub">Sub</option>
                    </select>
                </td>
                <td>
                    <select class="form-select form-select-sm web-service-parent-select" data-variable-index="${index}">
                        ${parentOptionsHtml}
                    </select>
                </td>
                <td>
                    <div class="form-check">
                        <input type="checkbox" class="form-check-input web-service-active-checkbox" checked data-variable-index="${index}">
                    </div>
                </td>
                <td>
                    <small class="text-muted">${variable.variableType || 'Unknown'}</small>
                </td>
                <td>
                    <small class="text-muted">${variable.isReadOnly ? 'Yes' : 'No'}</small>
                </td>
            </tr>`;
    });

    // Close table
    tableHtml += `
                </tbody>
            </table>
        </div>`;

    console.log('🔧 WebService table HTML generated successfully');
    return tableHtml;
}

function handleWebServicePrint() {
    // Bridge to existing printWebServiceVariables function
    printWebServiceVariables();
}

function handleWebServiceImport() {
    // Bridge to existing importWebServiceVariables function
    importWebServiceVariables();
}

/**
 * Handle Select All functionality
 */
function handleWebServiceSelectAll() {
    const checkboxes = document.querySelectorAll('.web-service-variable-checkbox');
    const selectAllCheckbox = document.getElementById('selectAllWebServiceCheckbox');

    checkboxes.forEach(checkbox => {
        checkbox.checked = true;
    });

    if (selectAllCheckbox) {
        selectAllCheckbox.checked = true;
    }

    console.log('Selected all Web Service variables');
}

/**
 * Handle Deselect All functionality
 */
function handleWebServiceDeselectAll() {
    const checkboxes = document.querySelectorAll('.web-service-variable-checkbox');
    const selectAllCheckbox = document.getElementById('selectAllWebServiceCheckbox');

    checkboxes.forEach(checkbox => {
        checkbox.checked = false;
    });

    if (selectAllCheckbox) {
        selectAllCheckbox.checked = false;
    }

    console.log('Deselected all Web Service variables');
}

/**
 * Print selected Web Service variables for debugging
 */
function printWebServiceVariables() {
    const selectedVariables = collectSelectedWebServiceVariables();

    if (selectedVariables.length === 0) {
        alert('Please select at least one variable to print.');
        return;
    }

    // Get connection info using the new utility function
    const connectionInfo = getStoredWebServiceConnectionInfo();

    const requestData = {
        connectionId: connectionInfo.connectionId,
        connectionName: connectionInfo.connectionName,
        selectedVariables: selectedVariables
    };

    console.log('Sending print request for Web Service variables:', requestData);

    fetch('/Import/PrintWebServiceMeters', {
        method: 'POST',
        headers: {
            'Content-Type': 'application/json'
        },
        body: JSON.stringify(requestData)
    })
        .then(response => response.json())
        .then(data => {
            if (data.success) {
                alert(`✅ Successfully printed ${data.count} Web Service variables to console. Check the terminal for details.`);
            } else {
                alert(`❌ Error printing Web Service variables: ${data.error}`);
            }
        })
        .catch(error => {
            console.error('Error printing Web Service variables:', error);
            alert('❌ Network error while printing Web Service variables.');
        });
}

/**
 * Import selected Web Service variables as meters
 */
function importWebServiceVariables() {
    const selectedVariables = collectSelectedWebServiceVariables();

    if (selectedVariables.length === 0) {
        alert('Please select at least one variable to import.');
        return;
    }

    // Validate that units are filled for selected variables
    const variablesWithoutUnits = selectedVariables.filter(v => !v.unit || v.unit.trim() === '');
    if (variablesWithoutUnits.length > 0) {
        const confirmMessage = `${variablesWithoutUnits.length} selected variables don't have units specified. Continue anyway?`;
        if (!confirm(confirmMessage)) {
            return;
        }
    }

    // Get import options
    const skipExisting = confirm('Skip existing meters? (Click OK to skip, Cancel to update existing)');
    const updateExisting = !skipExisting && confirm('Update existing meters with new information?');

    const requestData = {
        variables: selectedVariables,
        skipExisting: skipExisting,
        updateExisting: updateExisting
    };

    console.log('Sending import request for Web Service variables:', requestData);

    // Disable import button during processing
    const importBtn = document.getElementById('importWebServiceVariablesBtn');
    if (importBtn) {
        importBtn.disabled = true;
        importBtn.innerHTML = '<i class="bi bi-hourglass-split"></i> Importing...';
    }

    fetch('/Import/ImportWebServiceMeters', {
        method: 'POST',
        headers: {
            'Content-Type': 'application/json'
        },
        body: JSON.stringify(requestData)
    })
        .then(response => response.json())
        .then(data => {
            if (data.success) {
                alert(`✅ Web Service import completed successfully!\n\n` +
                    `Imported: ${data.importedCount}\n` +
                    `Updated: ${data.updatedCount}\n` +
                    `Skipped: ${data.skippedCount}\n` +
                    `Errors: ${data.errorCount}`);
            } else {
                let errorMessage = `❌ Web Service import failed: ${data.error}\n\n`;
                if (data.detailedErrors && Object.keys(data.detailedErrors).length > 0) {
                    errorMessage += 'Detailed errors:\n';
                    Object.entries(data.detailedErrors).forEach(([variable, error]) => {
                        errorMessage += `- ${variable}: ${error}\n`;
                    });
                }
                alert(errorMessage);
            }
        })
        .catch(error => {
            console.error('Error importing Web Service variables:', error);
            alert('❌ Network error while importing Web Service variables.');
        })
        .finally(() => {
            // Re-enable import button
            if (importBtn) {
                importBtn.disabled = false;
                importBtn.innerHTML = '<i class="bi bi-download"></i> Import Selected Variables as Meters';
            }
        });
}

/**
 * Collect selected Web Service variables with their configuration
 */
function collectSelectedWebServiceVariables() {
    console.log('🟠 Collecting selected WebService variables...');

    const selectedVariables = [];
    const selectedCheckboxes = document.querySelectorAll('.web-service-variable-checkbox:checked');

    console.log(`🟠 Found ${selectedCheckboxes.length} selected checkboxes`);

    selectedCheckboxes.forEach((checkbox, index) => {
        const dataIndex = checkbox.getAttribute('data-variable-index');
        const row = checkbox.closest('.web-service-variable-row');

        if (row) {
            const cells = row.cells;
            console.log(`🟠 Processing variable ${index + 1}, data-index: ${dataIndex}`);

            // 🎯 FIXED: Extract variable name from cells[2] (Variable Name column) inside <small> element
            const nameCell = cells[2]; // Was cells[1], should be cells[2]
            const smallElement = nameCell?.querySelector('small'); // Was looking for <strong>, should be <small>
            const variableName = smallElement ? smallElement.textContent.trim() : '';

            console.log(`🟠 Extracted variable name: "${variableName}"`);

            // Get other values using the data-variable-index
            const unitInput = document.querySelector(`.web-service-unit-input[data-variable-index="${dataIndex}"]`);
            const unit = unitInput?.value || '';

            const typeSelect = document.querySelector(`.web-service-type-select[data-variable-index="${dataIndex}"]`);
            const type = typeSelect?.value || 'main';

            const parentSelect = document.querySelector(`.web-service-parent-select[data-variable-index="${dataIndex}"]`);
            const parentMeterId = parentSelect?.value || '';

            const activeCheckbox = document.querySelector(`.web-service-active-checkbox[data-variable-index="${dataIndex}"]`);
            const active = activeCheckbox?.checked || false;

            // 🎯 FIXED: Extract PCVue type from correct cell (cells[7] for PCVue Type column)
            const variableTypeBadge = cells[7]?.querySelector('small');
            const variableType = variableTypeBadge ? variableTypeBadge.textContent.trim() : '';

            // 🎯 FIXED: Extract read-only status from correct cell (cells[8] for Read Only column)
            const readOnlyCell = cells[8]?.querySelector('small');
            const isReadOnly = readOnlyCell ? readOnlyCell.textContent.trim() === 'Yes' : false;

            const variableData = {
                variableName: variableName,
                unit: unit,
                type: type,
                parentMeterId: parentMeterId,
                active: active,
                variableType: variableType,
                isReadOnly: isReadOnly,
                isSelected: true
            };

            console.log(`🟠 Variable ${index + 1} data:`, variableData);
            selectedVariables.push(variableData);
        } else {
            console.error(`🔴 Could not find row for checkbox with data-index: ${dataIndex}`);
        }
    });

    console.log(`🟠 Collected ${selectedVariables.length} variables total`);
    return selectedVariables;
}

/**
 * Store Web Service connection info for later use
 */
function storeWebServiceConnectionInfo(connectionInfo) {
    window.webServiceConnectionInfo = connectionInfo;
}

/**
 * Get stored Web Service connection info
 */
function getStoredWebServiceConnectionInfo() {
    return window.webServiceConnectionInfo || {};
}

/**
* Web Service Browse Integration
* Add this to your existing web service browse JavaScript
*/

// Example of how to integrate with your existing web service browse handler
function handleWebServiceBrowseResponse(data) {
    const statusDiv = document.getElementById('webServiceStatus');

    if (data.success) {
        console.log('✅ Web Service browse successful:', data);

        // Check if we have variables to display
        if (data.variables && data.variables.length > 0) {

            // Show success status
            if (statusDiv) {
                statusDiv.innerHTML = `
                    <div class="alert alert-success">
                        <i class="bi bi-check-circle"></i> 
                        Successfully browsed ${data.totalVariables} variables from ${data.connectionInfo?.connectionName || 'Web Service'}. 
                        ${data.variables.length} variables ready for meter import. Configure settings below.
                    </div>`;
                statusDiv.style.display = 'block';
            }

            // 🎯 KEY INTEGRATION: Show the web service meter selection table
            showWebServiceMeterSelection(
                data.variables,           // Variables array from controller
                data.parentOptions || [], // Parent meter options from controller  
                data.connectionInfo || {} // Connection info from controller
            );

            // Scroll to the meter selection section
            const meterSelectionSection = document.getElementById('meterSelectionSection');
            if (meterSelectionSection) {
                meterSelectionSection.scrollIntoView({
                    behavior: 'smooth',
                    block: 'start'
                });
            }

        } else {
            // No variables found
            if (statusDiv) {
                statusDiv.innerHTML = `
                    <div class="alert alert-warning">
                        <i class="bi bi-exclamation-triangle"></i> 
                        No variables found. Try adjusting your filters or check the connection.
                    </div>`;
                statusDiv.style.display = 'block';
            }
        }

    } else {
        // Error occurred
        console.error('❌ Web Service browse failed:', data);

        if (statusDiv) {
            statusDiv.innerHTML = `
                <div class="alert alert-danger">
                    <i class="bi bi-exclamation-circle"></i> 
                    Browse failed: ${data.message || data.error || 'Unknown error'}
                </div>`;
            statusDiv.style.display = 'block';
        }
    }
}

// Example: Complete web service browse function
function browseWebServiceVariables() {
    const connectionId = document.getElementById('webServiceConnection')?.value;
    const maxVariables = document.getElementById('maxVariables')?.value || 100000;
    const branchFilter = document.getElementById('branchFilter')?.value || '';
    const includeSystemVars = document.getElementById('includeSystemVariables')?.checked || false;

    if (!connectionId) {
        alert('Please select a web service connection first.');
        return;
    }

    // Show loading state
    const browseBtn = document.getElementById('browseVariablesBtn');
    const originalBtnText = browseBtn?.innerHTML;
    if (browseBtn) {
        browseBtn.disabled = true;
        browseBtn.innerHTML = '<i class="bi bi-hourglass-split"></i> Browsing...';
    }

    const requestData = {
        connectionId: connectionId,
        maxVariables: parseInt(maxVariables),
        branchFilter: branchFilter,
        includeSystemVariables: includeSystemVars
    };

    console.log('🔍 Browsing web service variables:', requestData);

    // Call the controller
    fetch('/Import/BrowseVariablesWebService', {
        method: 'POST',
        headers: {
            'Content-Type': 'application/json'
        },
        body: JSON.stringify(requestData)
    })
        .then(response => response.json())
        .then(data => {
            // 🎯 Use the integration handler
            handleWebServiceBrowseResponse(data);
        })
        .catch(error => {
            console.error('Network error during web service browse:', error);
            const statusDiv = document.getElementById('webServiceStatus');
            if (statusDiv) {
                statusDiv.innerHTML = `
                <div class="alert alert-danger">
                    <i class="bi bi-wifi-off"></i> 
                    Network error: ${error.message}
                </div>`;
                statusDiv.style.display = 'block';
            }
        })
        .finally(() => {
            // Restore button state
            if (browseBtn && originalBtnText) {
                browseBtn.disabled = false;
                browseBtn.innerHTML = originalBtnText;
            }
        });
}