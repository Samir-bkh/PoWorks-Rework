/**
 * webservice_import.js
 * Handles:
 * - Browsing PCVue web service variables
 * - Displaying variables as meters in the unified table
 * - Importing Web Service variables as meters
 * - Printing selected variables
 */

// =====================================================
// INITIALIZATION
// =====================================================

/**
 * Initialize WebService functionality when DOM is ready
 */
document.addEventListener('DOMContentLoaded', function () {
    console.log('🔧 WebService import module loading...');

    // Load connections on startup
    loadWebServiceConnections();

    // Set up WebService-specific event listeners
    setupWebServiceEventListeners();

    // Set up WebService date range functionality - DELAY to ensure elements exist
    setTimeout(() => {
        setupWebServiceDateRange();
        console.log('✅ WebService date range setup completed');
    }, 100);
});
/**
 * Set up WebService-specific event listeners
 */

function setupWebServiceEventListeners() {
    // Web Service Connection dropdown change
    const webServiceConnection = document.getElementById('webServiceConnection');
    if (webServiceConnection) {
        webServiceConnection.addEventListener('change', function () {
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
    }

    // Browse Variables button
    const browseVariablesBtn = document.getElementById('browseVariablesBtn');
    if (browseVariablesBtn) {
        browseVariablesBtn.addEventListener('click', function () {
            const selectedConnection = document.getElementById('webServiceConnection').value;
            if (!selectedConnection) {
                showWebServiceStatus('warning', 'Please select a web service connection first');
                return;
            }
            browseVariables(selectedConnection);
        });
    }

    // Max Variables input validation
    const maxVariables = document.getElementById('maxVariables');
    if (maxVariables) {
        maxVariables.addEventListener('input', function () {
            const value = parseInt(this.value);
            if (value < 1) {
                this.value = 1;
            } else if (value > 1000000) {
                this.value = 1000000;
            }
        });
    }
}

/**
 * Loads available Web Service connections into the dropdown.
 */
function loadWebServiceConnections() {

    fetch('/WebServicesImport/GetWebServiceConnections')
        .then(response => response.json())
        .then(data => {
            const select = document.getElementById('webServiceConnection');
            if (!select) {
                return;
            }

            select.innerHTML = '<option value="">Select a connection...</option>';

            if (data.success && data.connections) {
                data.connections.forEach(conn => {
                    const option = document.createElement('option');
                    option.value = conn.connectionId;
                    option.textContent = `${conn.connectionName} (${conn.baseUrl})`;
                    if (conn.isDefault) {
                        option.textContent += ' - Default';
                    }
                    select.appendChild(option);
                });

                const defaultConnection = data.connections.find(c => c.isDefault);
                if (defaultConnection) {
                    select.value = defaultConnection.connectionId;
                    select.dispatchEvent(new Event('change'));
                }
            } else {
                showWebServiceStatus('danger', 'Failed to load web service connections. Please check your settings.');
            }
        })
        .catch(error => {
            showWebServiceStatus('danger', 'Error loading web service connections: ' + error.message);
        });
}

// =====================================================
// BROWSING VARIABLES
// =====================================================

/**
 * Triggers a browse request to the server for Web Service variables.
 */
function browseVariables(connectionId) {
    const maxVariables = parseInt(document.getElementById('maxVariables').value) || 100000;
    const branchFilter = document.getElementById('branchFilter').value.trim();
    const includeSystemVariables = document.getElementById('includeSystemVariables').checked;

    // GET connection name for storage
    const connectionSelect = document.getElementById('webServiceConnection');
    const selectedOption = connectionSelect.querySelector(`option[value="${connectionId}"]`);
    const connectionName = selectedOption ? selectedOption.textContent : connectionId;

    // STORE connection info for later use in import
    const connectionInfo = {
        connectionId: connectionId,
        connectionName: connectionName
    };
    storeWebServiceConnectionInfo(connectionInfo);

    console.log('🔗 Stored connection info:', connectionInfo);

    // GET DATE RANGE - ADD THIS
    const startDateInput = document.getElementById('webServiceStartDate');
    const endDateInput = document.getElementById('webServiceEndDate');
    const startDate = startDateInput?.value || null;
    const endDate = endDateInput?.value || null;

    // DEBUG CONSOLE LOG - ADD THIS
    console.log('📅 Date Range Debug:', {
        startDate: startDate,
        endDate: endDate,
        startDateInput: !!startDateInput,
        endDateInput: !!endDateInput
    });

    const systemMsg = includeSystemVariables
        ? " (including System variables)"
        : " (System variables filtered out)";

    showWebServiceStatus('info', `Starting variables browse... (Max: ${maxVariables.toLocaleString()}, Filter: ${branchFilter || 'None'})${systemMsg}`);

    const browseBtn = document.getElementById('browseVariablesBtn');
    browseBtn.disabled = true;
    browseBtn.innerHTML = '<i class="bi bi-hourglass-split"></i> Browsing...';

    const requestData = {
        connectionId,
        maxVariables,
        branchFilter,
        variableType: 'Any',
        depth: 0,
        includeSystemVariables,
        // ADD DATE RANGE TO REQUEST
        startDate: startDate,
        endDate: endDate
    };

    // DEBUG CONSOLE LOG - ADD THIS
    console.log('🔍 Request Data:', requestData);

    fetch('/WebServicesImport/BrowseVariablesWebService', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(requestData)
    })
        .then(response => {
            if (!response.ok) {
                throw new Error(`HTTP ${response.status}: ${response.statusText}`);
            }
            return response.json();
        })
        .then(data => {
            // STORE additional connection info from response
            if (data.connectionInfo) {
                const enhancedConnectionInfo = {
                    connectionId: data.connectionInfo.connectionId,
                    connectionName: data.connectionInfo.connectionName
                };
                storeWebServiceConnectionInfo(enhancedConnectionInfo);
                console.log('🔗 Updated connection info from response:', enhancedConnectionInfo);
            }

            handleWebServiceBrowseResponse(data);
        })
        .catch(error => {
            showWebServiceStatus('danger', 'Error during variables browse: ' + error.message);
        })
        .finally(() => {
            browseBtn.disabled = false;
            browseBtn.innerHTML = '<i class="bi bi-search"></i> Browse Variables';
        });
}

/**
 * Updates status area with a Bootstrap alert.
 */
function showWebServiceStatus(type, message) {
    const statusDiv = document.getElementById('webServiceStatus');
    if (!statusDiv) return;

    statusDiv.className = `alert alert-${type}`;
    statusDiv.innerHTML = message;
    statusDiv.style.display = 'block';

    if (type === 'success') {
        setTimeout(() => {
            statusDiv.style.display = 'none';
        }, 8000);
    }
}

/**
 * Handles the response from the browseVariables API call.
 */
function handleWebServiceBrowseResponse(data) {
    const statusDiv = document.getElementById('webServiceStatus');
    if (!statusDiv) return;

    if (data.success) {
        if (data.variables?.length > 0) {
            statusDiv.innerHTML = `
                <div class="alert alert-success">
                    <i class="bi bi-check-circle"></i>
                    Successfully browsed ${data.totalVariables} variables from ${data.connectionInfo?.connectionName || 'Web Service'}.
                    ${data.variables.length} variables ready for meter import.
                </div>`;
            statusDiv.style.display = 'block';

            showWebServiceMeterSelection(
                data.variables,
                data.parentOptions || [],
                data.connectionInfo || {}
            );

            document.getElementById('meterSelectionSection')?.scrollIntoView({
                behavior: 'smooth',
                block: 'start'
            });

        } else {
            statusDiv.innerHTML = `
                <div class="alert alert-warning">
                    <i class="bi bi-exclamation-triangle"></i>
                    No variables found. Try adjusting your filters or check the connection.
                </div>`;
            statusDiv.style.display = 'block';
        }

    } else {
        statusDiv.innerHTML = `
            <div class="alert alert-danger">
                <i class="bi bi-exclamation-circle"></i>
                Browse failed: ${data.message || data.error || 'Unknown error'}
            </div>`;
        statusDiv.style.display = 'block';
    }
}

// =====================================================
// METER SELECTION TABLE RENDERING
// =====================================================

/**
 * Displays the unified meter selection section
 * with Web Service variables loaded into a table.
 */
window.showWebServiceMeterSelection = function (variables, parentOptions, connectionInfo) {

    storeWebServiceConnectionInfo(connectionInfo);

    const meterSelectionSection = document.getElementById('meterSelectionSection');
    if (!meterSelectionSection) {
        return;
    }

    meterSelectionSection.classList.remove('d-none');

    const tableHtml = createWebServiceMeterSelectionTable(variables, parentOptions, connectionInfo);
    const cardBody = meterSelectionSection.querySelector('.card-body');
    if (cardBody) {
        cardBody.innerHTML = tableHtml;
    }

    const sectionHeader = meterSelectionSection.querySelector('.card-header h5');
    if (sectionHeader) {
        sectionHeader.innerHTML = '<i class="bi bi-cloud"></i> Web Service Variable to Meter Selection';
    }

    window.currentMeterDataType = 'WebService';
    window.currentWebServiceContext = connectionInfo;

    if (typeof updatePrintButtonForDataType === 'function') {
        updatePrintButtonForDataType('WebService');
    }

    document.getElementById('importReadings')?.closest('.form-check')?.style?.setProperty('display', 'none');

    const importBtn = document.getElementById('importSelectedBtn');
    if (importBtn) {
        importBtn.innerHTML = '<i class="bi bi-cloud-upload"></i> Import Selected Variables + Get Trends';
        importBtn.title = 'Import variables as meters and automatically retrieve trends data';
    }

    if (typeof updateMeterCounter === 'function') {
        updateMeterCounter();
    }
};

/**
 * Creates HTML for the meter table from Web Service variables.
 */
function createWebServiceMeterSelectionTable(variables, parentOptions, connectionInfo) {

    if (!variables.length) {
        return '<p class="text-warning">No variables found to display.</p>';
    }

    let parentOptionsHtml = '<option value="">None</option>';
    if (parentOptions?.length > 0) {
        parentOptions.forEach(opt => {
            if (opt.value !== '') {
                parentOptionsHtml += `<option value="${opt.value}">${opt.text}</option>`;
            }
        });
    }

    let tableHtml = `
        <div class="table-responsive" style="max-height: 500px; overflow-y: auto;">
            <table class="table table-sm table-striped table-hover">
                <thead class="sticky-top">
                    <tr>
                        ${['Import', 'Type', 'Variable Name', 'Unit', 'Meter Type', 'Parent Meter', 'Active', 'PCVue Type', 'Read Only'].map(h =>
        `<th style="background-color: #e9ecef; color: black; border-color: #dee2e6;">
                                <span class="text-dark fw-bold">${h}</span>
                            </th>`
    ).join('')}
                    </tr>
                </thead>
                <tbody id="metersTableBody">`;

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

    variables.forEach((variable, index) => {
        let badgeClass = 'bg-secondary';
        let typeBadge = 'TXT';

        switch ((variable.variableType || '').toLowerCase()) {
            case 'numeric':
            case 'real':
            case 'double':
                badgeClass = 'bg-primary';
                typeBadge = 'NUM';
                break;
            case 'boolean':
            case 'bool':
                badgeClass = 'bg-warning text-dark';
                typeBadge = 'BOOL';
                break;
            case 'string':
            case 'text':
                badgeClass = 'bg-secondary';
                typeBadge = 'TXT';
                break;
            default:
                if (variable.variableType) {
                    badgeClass = 'bg-info';
                    typeBadge = variable.variableType.substring(0, 4).toUpperCase();
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
                    <input type="text" class="form-control form-control-sm web-service-unit-input" placeholder="Enter unit (e.g., kWh, °C)" data-variable-index="${index}">
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
        </div>`;

    return tableHtml;
}

// =====================================================
// PRINT FUNCTIONALITY
// =====================================================

/**
 * Prints selected Web Service variables.
 */
function handleWebServicePrint() {
    printWebServiceVariables();
}

/**
 * Sends selected variables to the backend for printing.
 */
function printWebServiceVariables() {
    const selectedVariables = collectSelectedWebServiceVariables();

    if (!selectedVariables.length) {
        alert('Please select at least one variable to print.');
        return;
    }

    const connectionInfo = getStoredWebServiceConnectionInfo();

    const requestData = {
        connectionId: connectionInfo.connectionId,
        connectionName: connectionInfo.connectionName,
        selectedVariables
    };

    fetch('/WebServicesImport/PrintWebServiceMeters', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(requestData)
    })
        .then(res => res.json())
        .then(data => {
            if (data.success) {
                alert(`✅ Successfully printed ${data.count} Web Service variables to console.`);
            } else {
                alert(`❌ Error printing Web Service variables: ${data.error}`);
            }
        })
        .catch(err => {
            alert('❌ Network error while printing Web Service variables.');
        });
}

// =====================================================
// IMPORT FUNCTIONALITY
// =====================================================

/**
 * Initiates import of selected Web Service variables as meters.
 */
function handleWebServiceImport() {
    importWebServiceVariables();
}

/**
 * Imports selected Web Service variables.
 */
function importWebServiceVariables() {
    const selectedVariables = collectSelectedWebServiceVariables();

    if (!selectedVariables.length) {
        alert('Please select at least one variable to import.');
        return;
    }

    const variablesWithoutUnits = selectedVariables.filter(v => !v.unit || v.unit.trim() === '');
    if (variablesWithoutUnits.length > 0) {
        if (!confirm(`${variablesWithoutUnits.length} variables have no unit defined. Continue anyway?`)) {
            return;
        }
    }

    const skipExisting = confirm('Skip existing meters? (OK = skip, Cancel = update)');
    const updateExisting = !skipExisting && confirm('Update existing meters with new information?');

    // Get date range for trends
    const dateRange = getSelectedDateRange();
    const connectionInfo = getStoredWebServiceConnectionInfo();

    console.log('🔧 Import request data:', {
        variables: selectedVariables.length,
        dateRange: dateRange,
        connectionInfo: connectionInfo
    });

    const requestData = {
        variables: selectedVariables,
        skipExisting,
        updateExisting,
        // ADD date range and connection for trends
        startDate: dateRange.startDate,
        endDate: dateRange.endDate,
        connectionId: connectionInfo.connectionId
    };

    const importBtn = document.getElementById('importSelectedBtn');
    const originalText = importBtn?.textContent || 'Import Selected';

    if (importBtn) {
        importBtn.disabled = true;
        importBtn.innerHTML = '<i class="bi bi-hourglass-split"></i> Importing + Getting Trends...';
    }

    console.log('📤 Sending import request:', requestData);

    fetch('/WebServicesImport/ImportWebServiceMeters', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(requestData)
    })
        .then(response => response.json())
        .then(data => {
            console.log('📥 Import response:', data);

            if (data.success) {
                let message = `✅ Web Service import completed successfully!\n`;
                message += `Imported: ${data.importedCount}\n`;
                message += `Updated: ${data.updatedCount}\n`;
                message += `Skipped: ${data.skippedCount}\n`;
                message += `Errors: ${data.errorCount}`;

                // Add trends results if available
                if (data.trendsSuccessCount !== undefined || data.trendsFailedCount !== undefined) {
                    message += `\n\n📊 Trends Processing:\n`;
                    message += `Success: ${data.trendsSuccessCount || 0}\n`;
                    message += `Failed: ${data.trendsFailedCount || 0}`;
                }

                alert(message);
            } else {
                let errorMsg = `❌ Web Service import failed: ${data.error}\n\n`;
                if (data.detailedErrors) {
                    errorMsg += 'Detailed errors:\n';
                    Object.entries(data.detailedErrors).forEach(([variable, error]) => {
                        errorMsg += `- ${variable}: ${error}\n`;
                    });
                }
                alert(errorMsg);
            }
        })
        .catch(err => {
            console.error('❌ Import error:', err);
            alert('❌ Network error while importing Web Service variables.');
        })
        .finally(() => {
            if (importBtn) {
                importBtn.disabled = false;
                importBtn.innerHTML = originalText;
            }
        });
}


// =====================================================
// DATA COLLECTION
// =====================================================

/**
 * Collects selected Web Service variables from the table.
 */
function collectSelectedWebServiceVariables() {
    const selectedVariables = [];
    const dateRange = getSelectedDateRange();

    document.querySelectorAll('.web-service-variable-checkbox:checked').forEach(checkbox => {
        const index = checkbox.getAttribute('data-variable-index');
        const row = checkbox.closest('.web-service-variable-row');
        if (!row) return;

        const cells = row.cells;
        const nameCell = cells[2];
        const small = nameCell?.querySelector('small');
        const variableName = small?.textContent.trim() || '';

        const unit = document.querySelector(`.web-service-unit-input[data-variable-index="${index}"]`)?.value || '';
        const type = document.querySelector(`.web-service-type-select[data-variable-index="${index}"]`)?.value || 'main';
        const parentMeterId = document.querySelector(`.web-service-parent-select[data-variable-index="${index}"]`)?.value || '';
        const active = document.querySelector(`.web-service-active-checkbox[data-variable-index="${index}"]`)?.checked || false;

        const variableType = cells[7]?.querySelector('small')?.textContent.trim() || '';
        const isReadOnly = cells[8]?.querySelector('small')?.textContent.trim() === 'Yes';

        const variableData = {
            variableName,
            unit,
            type,
            parentMeterId,
            active,
            variableType,
            isReadOnly,
            isSelected: true,
            // ADD date range to each variable
            startDate: dateRange.startDate,
            endDate: dateRange.endDate
        };
        selectedVariables.push(variableData);
    });

    return selectedVariables;
}

// =====================================================
// STORAGE UTILITIES
// =====================================================

/**
 * Stores Web Service connection info globally.
 */
function storeWebServiceConnectionInfo(connectionInfo) {
    window.webServiceConnectionInfo = connectionInfo;
    console.log('💾 Stored connection info:', connectionInfo);

    // Also store in sessionStorage as backup
    try {
        sessionStorage.setItem('webServiceConnectionInfo', JSON.stringify(connectionInfo));
    } catch (e) {
        console.warn('⚠️ Could not store in sessionStorage:', e);
    }
}

/**
 * Retrieves stored Web Service connection info.
 */
function getStoredWebServiceConnectionInfo() {
    let connectionInfo = window.webServiceConnectionInfo;

    // If not in memory, try sessionStorage
    if (!connectionInfo) {
        try {
            const stored = sessionStorage.getItem('webServiceConnectionInfo');
            if (stored) {
                connectionInfo = JSON.parse(stored);
                window.webServiceConnectionInfo = connectionInfo; // Restore to memory
            }
        } catch (e) {
            console.warn('⚠️ Could not retrieve from sessionStorage:', e);
        }
    }

    // If still not available, try to get from current selection
    if (!connectionInfo) {
        const connectionSelect = document.getElementById('webServiceConnection');
        if (connectionSelect && connectionSelect.value) {
            const selectedOption = connectionSelect.querySelector(`option[value="${connectionSelect.value}"]`);
            connectionInfo = {
                connectionId: connectionSelect.value,
                connectionName: selectedOption ? selectedOption.textContent : connectionSelect.value
            };
            storeWebServiceConnectionInfo(connectionInfo); // Store it
        }
    }

    console.log('🔗 Retrieved connection info:', connectionInfo);
    return connectionInfo || {};
}


/**
* Set up WebService date range functionality
*/
function setupWebServiceDateRange() {
    console.log('🔧 Setting up WebService date range...');

    // Initialize default date range
    initializeWebServiceDateRange();

    // Setup quick range buttons
    setupWebServiceQuickRangeButtons();

    // Setup date validation
    setupWebServiceDateValidation();
}

/**
* Initialize default WebService date range (last 24 hours)
*/
function initializeWebServiceDateRange() {
    const endDateInput = document.getElementById('webServiceEndDate');
    const startDateInput = document.getElementById('webServiceStartDate');

    if (!endDateInput || !startDateInput) {
        console.warn('⚠️ WebService date inputs not found');
        return;
    }

    const now = new Date();
    const yesterday = new Date(now.getTime() - 24 * 60 * 60 * 1000);

    endDateInput.value = formatDateTimeLocal(now);
    startDateInput.value = formatDateTimeLocal(yesterday);

    console.log('✅ WebService date range initialized');
}


/**
 * Format Date object to HTML datetime-local string
 */
function formatDateTimeLocal(date) {
    const year = date.getFullYear();
    const month = String(date.getMonth() + 1).padStart(2, '0');
    const day = String(date.getDate()).padStart(2, '0');
    const hours = String(date.getHours()).padStart(2, '0');
    const minutes = String(date.getMinutes()).padStart(2, '0');
    return `${year}-${month}-${day}T${hours}:${minutes}`;
}

function setupWebServiceQuickRangeButtons() {
    const btn24h = document.getElementById('wsQuickRange24h');
    const btn7d = document.getElementById('wsQuickRange7d');
    const btn30d = document.getElementById('wsQuickRange30d');

    console.log('🔧 Setting up quick range buttons...', {
        btn24h: !!btn24h,
        btn7d: !!btn7d,
        btn30d: !!btn30d
    });

    if (btn24h) {
        btn24h.addEventListener('click', (e) => {
            e.preventDefault();
            console.log('📅 24h button clicked');
            setWebServiceQuickRange(24, 'hours');
            highlightActiveWebServiceQuickRange('wsQuickRange24h');
        });
    } else {
        console.warn('⚠️ 24h button not found');
    }

    if (btn7d) {
        btn7d.addEventListener('click', (e) => {
            e.preventDefault();
            console.log('📅 7d button clicked');
            setWebServiceQuickRange(7, 'days');
            highlightActiveWebServiceQuickRange('wsQuickRange7d');
        });
    } else {
        console.warn('⚠️ 7d button not found');
    }

    if (btn30d) {
        btn30d.addEventListener('click', (e) => {
            e.preventDefault();
            console.log('📅 30d button clicked');
            setWebServiceQuickRange(30, 'days');
            highlightActiveWebServiceQuickRange('wsQuickRange30d');
        });
    } else {
        console.warn('⚠️ 30d button not found');
    }
}




/**
 * Set WebService start and end dates to a quick range
 */
function setWebServiceQuickRange(amount, unit) {
    console.log(`📅 Setting quick range: ${amount} ${unit}`);

    const now = new Date();
    const startDate = new Date();

    if (unit === 'hours') {
        startDate.setHours(startDate.getHours() - amount);
    } else if (unit === 'days') {
        startDate.setDate(startDate.getDate() - amount);
    }

    const endDateInput = document.getElementById('webServiceEndDate');
    const startDateInput = document.getElementById('webServiceStartDate');

    if (endDateInput && startDateInput) {
        endDateInput.value = formatDateTimeLocal(now);
        startDateInput.value = formatDateTimeLocal(startDate);

        console.log('✅ Date range set:', {
            start: startDateInput.value,
            end: endDateInput.value
        });

        validateWebServiceDateRange();
    } else {
        console.error('❌ Date inputs not found');
    }
}

/**
 * Highlight the active quick-range button
 */
function highlightActiveWebServiceQuickRange(activeId) {
    ['wsQuickRange24h', 'wsQuickRange7d', 'wsQuickRange30d'].forEach(id => {
        const btn = document.getElementById(id);
        if (btn) {
            btn.classList.toggle('btn-secondary', id === activeId);
            btn.classList.toggle('btn-outline-secondary', id !== activeId);
        }
    });
}

/**
 * Setup date validation for WebService inputs
 */
function setupWebServiceDateValidation() {
    const startDateInput = document.getElementById('webServiceStartDate');
    const endDateInput = document.getElementById('webServiceEndDate');

    if (startDateInput) startDateInput.addEventListener('change', validateWebServiceDateRange);
    if (endDateInput) endDateInput.addEventListener('change', validateWebServiceDateRange);
}

/**
 * Validate WebService date range selection
 */
function validateWebServiceDateRange() {
    const startDateInput = document.getElementById('webServiceStartDate');
    const endDateInput = document.getElementById('webServiceEndDate');

    if (!startDateInput || !endDateInput) return true;

    const startDate = new Date(startDateInput.value);
    const endDate = new Date(endDateInput.value);

    startDateInput.classList.remove('is-invalid');
    endDateInput.classList.remove('is-invalid');

    if (startDate >= endDate) {
        endDateInput.classList.add('is-invalid');
        console.warn('Invalid WebService date range');
        return false;
    }

    const oneYear = 365 * 24 * 60 * 60 * 1000;
    if (endDate - startDate > oneYear) {
        startDateInput.classList.add('is-invalid');
        endDateInput.classList.add('is-invalid');
        return false;
    }
    return true;
}

function validateImportRequest() {
    const selectedVariables = collectSelectedWebServiceVariables();
    const dateRange = getSelectedDateRange();
    const connectionInfo = getStoredWebServiceConnectionInfo();

    console.log('🔧 Validating import request...');

    if (!selectedVariables.length) {
        console.warn('⚠️ No variables selected');
        return false;
    }

    if (!connectionInfo.connectionId) {
        console.warn('⚠️ No connection ID available');
        // This is okay for meter-only import, just warn
    }

    if (!dateRange.startDate || !dateRange.endDate) {
        console.warn('⚠️ Date range not set - trends will be skipped');
        // This is okay, just importing meters without trends
    }

    console.log('✅ Import request validation passed');
    return true;
}

/**
 * Get selected date range for WebServices (reusable function)
 */
function getSelectedDateRange() {
    const startDateInput = document.getElementById('webServiceStartDate');
    const endDateInput = document.getElementById('webServiceEndDate');

    const dateRange = {
        startDate: startDateInput?.value || null,
        endDate: endDateInput?.value || null
    };

    // Validate date range
    if (dateRange.startDate && dateRange.endDate) {
        const start = new Date(dateRange.startDate);
        const end = new Date(dateRange.endDate);

        if (start >= end) {
            console.warn('⚠️ Invalid date range: start date must be before end date');
        } else {
            const duration = (end - start) / (1000 * 60 * 60 * 24);
            console.log(`📅 Date range: ${duration.toFixed(1)} days`);
        }
    }

    console.log('📅 Getting date range:', dateRange);
    return dateRange;
}


function buildWebServicePrintRequest(selectedVariables) {
    const connectionInfo = getStoredWebServiceConnectionInfo();
    const dateRange = getSelectedDateRange();

    return {
        connectionId: connectionInfo.connectionId || '',
        connectionName: connectionInfo.connectionName || '',
        selectedVariables: selectedVariables,
        startDate: dateRange.startDate,
        endDate: dateRange.endDate
    };
}