/**
 * Web Service Variable to Meter Selection Functionality
 * This file handles the meter selection table after browsing PCVue web service variables
 */

// Wait for the document to be fully loaded
document.addEventListener('DOMContentLoaded', function () {
    console.log('Web Service Meter Selection JS loaded - DOM Content Loaded');

    // Set up event delegation for the parent page
    document.body.addEventListener('click', function (event) {
        // Handle Apply Bulk Type button click
        if (event.target.id === 'applyWebServiceBulkType' || event.target.closest('#applyWebServiceBulkType')) {
            console.log('Apply Bulk Type button clicked (Web Service)');
            applyWebServiceBulkType();
        }

        // Handle Apply Bulk Parent button click
        if (event.target.id === 'applyWebServiceBulkParent' || event.target.closest('#applyWebServiceBulkParent')) {
            console.log('Apply Bulk Parent button clicked (Web Service)');
            applyWebServiceBulkParent();
        }

        // Handle Apply Bulk Active button click
        if (event.target.id === 'applyWebServiceBulkActive' || event.target.closest('#applyWebServiceBulkActive')) {
            console.log('Apply Bulk Active button clicked (Web Service)');
            applyWebServiceBulkActive();
        }
    });
});

/**
 * Create the meter selection table from browsed web service variables
 */
function createWebServiceMeterSelectionTable(variables, parentOptions, connectionInfo) {
    console.log('Creating Web Service meter selection table with', variables.length, 'variables');

    if (!variables || variables.length === 0) {
        console.warn('No variables provided for Web Service meter selection table');
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

    let tableHtml = `
    <div class="web-service-meter-selection-container">
        <div class="d-flex justify-content-between align-items-center mb-3">
            <h5>Web Service Variable to Meter Selection</h5>
        </div>
        <div class="table-responsive" style="max-height: 500px; overflow-y: auto;">
            <table class="table table-sm table-striped table-hover">
             <thead class="sticky-top">
                <tr>
                        <th style="width: 50px; background-color: #e9ecef !important; color: black !important; border-color: #dee2e6 !important;">
                            <span class="text-dark fw-bold">Select</span>
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
                <tbody>`;

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

    tableHtml += `
                    </tbody>
                </table>
            </div>

            <!-- Bulk Operations -->
            <div class="row mt-3">
                <div class="col-md-4">
                    <label class="form-label">Bulk Type:</label>
                    <div class="input-group">
                        <select class="form-select" id="webServiceBulkTypeSelect">
                            <option value="main">Main</option>
                            <option value="sub">Sub</option>
                        </select>
                        <button class="btn btn-outline-secondary" type="button" id="applyWebServiceBulkType">Apply</button>
                    </div>
                </div>
                <div class="col-md-4">
                    <label class="form-label">Bulk Parent:</label>
                    <div class="input-group">
                        <select class="form-select" id="webServiceBulkParentSelect">
                            ${parentOptionsHtml}
                        </select>
                        <button class="btn btn-outline-secondary" type="button" id="applyWebServiceBulkParent">Apply</button>
                    </div>
                </div>
                <div class="col-md-4">
                    <label class="form-label">Bulk Active:</label>
                    <div class="input-group">
                        <select class="form-select" id="webServiceBulkActiveSelect">
                            <option value="true">Active (True)</option>
                            <option value="false">Inactive (False)</option>
                        </select>
                        <button class="btn btn-outline-secondary" type="button" id="applyWebServiceBulkActive">Apply</button>
                    </div>
                </div>
            </div>

        </div>`;

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
 * Apply bulk type to selected variables
 */
function applyWebServiceBulkType() {
    const bulkType = document.getElementById('webServiceBulkTypeSelect')?.value;
    if (!bulkType) return;

    const selectedCheckboxes = document.querySelectorAll('.web-service-variable-checkbox:checked');
    selectedCheckboxes.forEach(checkbox => {
        const index = checkbox.getAttribute('data-variable-index');
        const typeSelect = document.querySelector(`.web-service-type-select[data-variable-index="${index}"]`);
        if (typeSelect) {
            typeSelect.value = bulkType;
        }
    });

    console.log(`Applied bulk type "${bulkType}" to ${selectedCheckboxes.length} selected variables`);
}

/**
 * Apply bulk parent to selected variables
 */
function applyWebServiceBulkParent() {
    const bulkParent = document.getElementById('webServiceBulkParentSelect')?.value;

    const selectedCheckboxes = document.querySelectorAll('.web-service-variable-checkbox:checked');
    selectedCheckboxes.forEach(checkbox => {
        const index = checkbox.getAttribute('data-variable-index');
        const parentSelect = document.querySelector(`.web-service-parent-select[data-variable-index="${index}"]`);
        if (parentSelect) {
            parentSelect.value = bulkParent || '';
        }
    });

    console.log(`Applied bulk parent "${bulkParent || 'None'}" to ${selectedCheckboxes.length} selected variables`);
}

/**
 * Apply bulk active status to selected variables
 */
function applyWebServiceBulkActive() {
    const bulkActive = document.getElementById('webServiceBulkActiveSelect')?.value === 'true';

    const selectedCheckboxes = document.querySelectorAll('.web-service-variable-checkbox:checked');
    selectedCheckboxes.forEach(checkbox => {
        const index = checkbox.getAttribute('data-variable-index');
        const activeCheckbox = document.querySelector(`.web-service-active-checkbox[data-variable-index="${index}"]`);
        if (activeCheckbox) {
            activeCheckbox.checked = bulkActive;
        }
    });

    console.log(`Applied bulk active status "${bulkActive}" to ${selectedCheckboxes.length} selected variables`);
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
    const selectedVariables = [];
    const selectedCheckboxes = document.querySelectorAll('.web-service-variable-checkbox:checked');

    selectedCheckboxes.forEach(checkbox => {
        const index = checkbox.getAttribute('data-variable-index');
        const row = checkbox.closest('.web-service-variable-row');

        if (row) {
            const cells = row.cells;

            // Extract variable name from the cell (now in cells[1])
            const nameCell = cells[1];
            const strongElement = nameCell?.querySelector('strong');
            const variableName = strongElement ? strongElement.textContent.trim() : '';

            // Get other values using the data-variable-index
            const unit = document.querySelector(`.web-service-unit-input[data-variable-index="${index}"]`)?.value || '';
            const type = document.querySelector(`.web-service-type-select[data-variable-index="${index}"]`)?.value || 'main';
            const parentMeterId = document.querySelector(`.web-service-parent-select[data-variable-index="${index}"]`)?.value || '';
            const active = document.querySelector(`.web-service-active-checkbox[data-variable-index="${index}"]`)?.checked || false;

            // Extract PCVue type and read-only status from badge elements
            const variableTypeBadge = cells[6]?.querySelector('.badge');
            const variableType = variableTypeBadge ? variableTypeBadge.textContent.trim() : '';

            const readOnlyBadge = cells[7]?.querySelector('.badge');
            const isReadOnly = readOnlyBadge ? readOnlyBadge.textContent.trim() === 'Yes' : false;

            selectedVariables.push({
                variableName: variableName,
                unit: unit,
                type: type,
                parentMeterId: parentMeterId,
                active: active,
                variableType: variableType,
                isReadOnly: isReadOnly,
                isSelected: true
            });
        }
    });

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