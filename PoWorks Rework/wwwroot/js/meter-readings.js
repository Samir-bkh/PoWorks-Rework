/**
 * Meter Readings JavaScript Module
 * Handles all dynamic functionality for the meter readings page
 */

const MeterReadings = {
    // Configuration
    config: {
        currentViewType: 'raw',
        selectedMeterId: null,
        currentPage: 1,
        pageSize: 50,
        startDate: null,
        endDate: null,
        isLoading: false
    },

    // API endpoints
    endpoints: {
        getReadings: '/MeterReadings/GetReadings',
        getMeterStats: '/MeterReadings/GetMeterStats',
        exportReadings: '/MeterReadings/Export'
    },

    /**
     * Initialize the module
     */
    init: function (options = {}) {
        console.log('Initializing MeterReadings module');

        // Merge configuration
        Object.assign(this.config, options);

        // Set up event listeners
        this.setupEventListeners();

        // Initialize UI state
        this.updateUIState();

        console.log('MeterReadings module initialized with config:', this.config);
    },

    /**
     * Set up all event listeners
     */
    setupEventListeners: function () {
        // Tab switching
        document.querySelectorAll('[data-view-type]').forEach(tab => {
            tab.addEventListener('click', (e) => {
                e.preventDefault();
                const viewType = e.target.getAttribute('data-view-type');
                this.switchViewType(viewType);
            });
        });

        // Filter controls
        const meterSelect = document.getElementById('meterSelect');
        const startDate = document.getElementById('startDate');
        const endDate = document.getElementById('endDate');
        const pageSize = document.getElementById('pageSize');

        if (meterSelect) {
            meterSelect.addEventListener('change', () => {
                this.config.selectedMeterId = meterSelect.value || null;
                this.updateMeterStats();
            });
        }

        if (startDate) {
            startDate.addEventListener('change', () => {
                this.config.startDate = startDate.value;
            });
        }

        if (endDate) {
            endDate.addEventListener('change', () => {
                this.config.endDate = endDate.value;
            });
        }

        if (pageSize) {
            pageSize.addEventListener('change', () => {
                this.config.pageSize = parseInt(pageSize.value);
                this.config.currentPage = 1; // Reset to first page
                this.loadReadings();
            });
        }

        // Button event listeners
        const applyFiltersBtn = document.getElementById('applyFiltersBtn');
        const clearFiltersBtn = document.getElementById('clearFiltersBtn');
        const refreshBtn = document.getElementById('refreshBtn');
        const exportBtn = document.getElementById('exportBtn');

        if (applyFiltersBtn) {
            applyFiltersBtn.addEventListener('click', () => this.applyFilters());
        }

        if (clearFiltersBtn) {
            clearFiltersBtn.addEventListener('click', () => this.clearFilters());
        }

        if (refreshBtn) {
            refreshBtn.addEventListener('click', () => this.refreshData());
        }

        if (exportBtn) {
            exportBtn.addEventListener('click', () => this.showExportDialog());
        }

        // Keyboard shortcuts
        document.addEventListener('keydown', (e) => {
            if (e.ctrlKey || e.metaKey) {
                switch (e.key) {
                    case 'r':
                        e.preventDefault();
                        this.refreshData();
                        break;
                    case 'e':
                        e.preventDefault();
                        this.showExportDialog();
                        break;
                }
            }
        });
    },

    /**
     * Switch between view types (raw, daily, monthly, yearly)
     */
    switchViewType: function (viewType) {
        if (this.config.isLoading) {
            console.log('Already loading, ignoring view type switch');
            return;
        }

        if (this.config.currentViewType === viewType) {
            console.log('Already on view type:', viewType);
            return;
        }

        console.log(`Switching view type from ${this.config.currentViewType} to ${viewType}`);

        this.config.currentViewType = viewType;
        this.config.currentPage = 1; // Reset to first page when switching views

        // Update active tab
        this.updateActiveTab(viewType);

        // Load new data
        this.loadReadings();
    },

    /**
     * Update active tab UI
     */
    updateActiveTab: function (viewType) {
        // Remove active class from all tabs
        document.querySelectorAll('[data-view-type]').forEach(tab => {
            tab.classList.remove('active');
            tab.setAttribute('aria-selected', 'false');
        });

        // Add active class to current tab
        const activeTab = document.querySelector(`[data-view-type="${viewType}"]`);
        if (activeTab) {
            activeTab.classList.add('active');
            activeTab.setAttribute('aria-selected', 'true');
        }

        // Update page title
        const titleElement = document.querySelector('.card-header h4');
        if (titleElement) {
            const viewTypeNames = {
                'raw': 'Raw Readings',
                'daily': 'Daily Aggregated',
                'monthly': 'Monthly Aggregated',
                'yearly': 'Yearly Aggregated'
            };
            titleElement.textContent = `Meter Readings - ${viewTypeNames[viewType]}`;
        }
    },

    /**
     * Load readings data via AJAX
     */
    loadReadings: function () {
        if (this.config.isLoading) {
            console.log('Already loading readings');
            return;
        }

        this.config.isLoading = true;
        this.showLoading(true);

        // Build query parameters
        const params = new URLSearchParams({
            viewType: this.config.currentViewType,
            page: this.config.currentPage,
            pageSize: this.config.pageSize
        });

        if (this.config.selectedMeterId) {
            params.append('meterId', this.config.selectedMeterId);
        }

        if (this.config.startDate) {
            params.append('startDate', this.config.startDate);
        }

        if (this.config.endDate) {
            params.append('endDate', this.config.endDate);
        }

        console.log('Loading readings with params:', params.toString());

        fetch(`${this.endpoints.getReadings}?${params}`)
            .then(response => {
                if (!response.ok) {
                    throw new Error(`HTTP ${response.status}: ${response.statusText}`);
                }
                return response.json();
            })
            .then(data => {
                console.log('Readings loaded successfully:', data);

                if (data.success) {
                    this.updateReadingsTable(data.data, data.pagination);
                    this.updatePaginationInfo(data.pagination);
                } else {
                    throw new Error(data.error || 'Failed to load readings');
                }
            })
            .catch(error => {
                console.error('Error loading readings:', error);
                this.showError('Failed to load readings: ' + error.message);
            })
            .finally(() => {
                this.config.isLoading = false;
                this.showLoading(false);
            });
    },

    /**
     * Update the readings table with new data
     */
    updateReadingsTable: function (readings, pagination) {
        // Find the active tab content
        const activeTabPane = document.querySelector('.tab-pane.active #readingsContent');
        if (!activeTabPane) {
            console.error('Active tab pane not found');
            return;
        }

        // Create table HTML
        const tableHtml = this.generateTableHTML(readings, pagination);

        // Update the content
        activeTabPane.innerHTML = tableHtml;

        // Update pagination info
        this.updatePaginationInfo(pagination);
    },

    /**
     * Generate table HTML for readings
     */
    generateTableHTML: function (readings, pagination) {
        if (!readings || readings.length === 0) {
            return this.generateEmptyStateHTML();
        }

        const isRaw = this.config.currentViewType === 'raw';
        const isYearly = this.config.currentViewType === 'yearly';

        let html = `
        <div class="card">
            <div class="card-header bg-light">
                <div class="d-flex justify-content-between align-items-center">
                    <h6 class="mb-0">
                        ${this.getViewTypeDisplayName()} 
                        ${this.config.selectedMeterId ? `- ${this.getSelectedMeterName()}` : ''}
                    </h6>
                    <span class="text-muted">
                        Showing ${pagination.currentPage * pagination.pageSize - pagination.pageSize + 1}-${Math.min(pagination.currentPage * pagination.pageSize, pagination.totalCount)} of ${pagination.totalCount} readings
                    </span>
                </div>
            </div>
            <div class="card-body p-0">
                <div class="table-responsive">
                    <table class="table table-hover mb-0" id="readingsTable">
                        <thead class="table-light">
                            <tr>
                                <th>Meter</th>
                                <th>Timestamp</th>
                                <th class="text-end">Value</th>`;

        if (isRaw) {
            html += '<th class="text-center">Quality</th>';
        } else {
            html += `
                                <th class="text-end">Min</th>
                                <th class="text-end">Max</th>
                                <th class="text-end">Count</th>`;
            if (!isYearly) {
                html += '<th class="text-end">Sum</th>';
            }
        }

        html += `
                                <th class="text-center">Actions</th>
                            </tr>
                        </thead>
                        <tbody>`;

        // Generate rows
        readings.forEach(reading => {
            html += this.generateTableRow(reading, isRaw, isYearly);
        });

        html += `
                        </tbody>
                    </table>
                </div>
            </div>`;

        // Add pagination if needed
        if (pagination.totalPages > 1) {
            html += this.generatePaginationHTML(pagination);
        }

        html += `</div>`;

        return html;
    },

    /**
     * Generate table row HTML
     */
    generateTableRow: function (reading, isRaw, isYearly) {
        let html = `
        <tr data-reading-id="${reading.readingId}" data-meter-id="${reading.meterId}">
            <td>
                <div class="d-flex align-items-center">
                    <span class="fw-medium">${reading.meterName}</span>
                </div>
            </td>
            <td>
                <span class="font-monospace">${this.formatTimestamp(reading.timestamp)}</span>
            </td>
            <td class="text-end">
                <span class="fw-bold">${this.formatValue(reading.value)}</span>
            </td>`;

        if (isRaw) {
            html += `
            <td class="text-center">
                ${this.formatQuality(reading.quality)}
            </td>`;
        } else {
            html += `
            <td class="text-end">
                <span class="text-muted">${reading.minValue ? this.formatValue(reading.minValue) : '-'}</span>
            </td>
            <td class="text-end">
                <span class="text-muted">${reading.maxValue ? this.formatValue(reading.maxValue) : '-'}</span>
            </td>
            <td class="text-end">
                ${reading.readingCount ? `<span class="badge bg-info">${reading.readingCount}</span>` : '<span class="text-muted">-</span>'}
            </td>`;

            if (!isYearly) {
                html += `
            <td class="text-end">
                <span class="text-muted">${reading.sumValue ? this.formatValue(reading.sumValue) : '-'}</span>
            </td>`;
            }
        }

        html += `
            <td class="text-center">
                <div class="btn-group btn-group-sm" role="group">
                    <button type="button" class="btn btn-outline-primary btn-sm" 
                            title="View Details" onclick="MeterReadings.viewReadingDetails(${reading.readingId})">
                        <i class="bi bi-eye"></i>
                    </button>`;

        if (!isRaw) {
            html += `
                    <button type="button" class="btn btn-outline-info btn-sm" 
                            title="View Raw Data" onclick="MeterReadings.viewRawReadings(${reading.meterId}, '${reading.timestamp}')">
                        <i class="bi bi-list-ul"></i>
                    </button>`;
        }

        html += `
                </div>
            </td>
        </tr>`;

        return html;
    },

    /**
     * Generate empty state HTML
     */
    generateEmptyStateHTML: function () {
        return `
        <div class="card">
            <div class="card-body">
                <div class="text-center p-5">
                    <div class="mb-3">
                        <i class="bi bi-graph-up-arrow display-1 text-muted"></i>
                    </div>
                    <h5 class="text-muted">No readings found</h5>
                    <p class="text-muted">
                        ${this.config.selectedMeterId ?
                'No readings available for the selected meter and date range.' :
                'Try selecting a specific meter or adjusting the date range.'}
                    </p>
                    <button type="button" class="btn btn-primary" onclick="MeterReadings.clearFilters()">
                        <i class="bi bi-funnel"></i> Clear Filters
                    </button>
                </div>
            </div>
        </div>`;
    },

    /**
     * Generate pagination HTML
     */
    generatePaginationHTML: function (pagination) {
        const current = pagination.currentPage;
        const total = pagination.totalPages;
        const startPage = Math.max(1, current - 2);
        const endPage = Math.min(total, startPage + 4);

        let html = `
        <div class="card-footer">
            <div class="d-flex justify-content-between align-items-center">
                <div>
                    <nav aria-label="Readings pagination">
                        <ul class="pagination mb-0">
                            <li class="page-item ${current === 1 ? 'disabled' : ''}">
                                <button class="page-link" onclick="MeterReadings.goToPage(1)" 
                                        ${current === 1 ? 'disabled' : ''} aria-label="First">
                                    <span aria-hidden="true">&laquo;</span>
                                </button>
                            </li>
                            <li class="page-item ${current === 1 ? 'disabled' : ''}">
                                <button class="page-link" onclick="MeterReadings.goToPage(${current - 1})" 
                                        ${current === 1 ? 'disabled' : ''} aria-label="Previous">
                                    <span aria-hidden="true">&lsaquo;</span>
                                </button>
                            </li>`;

        for (let i = startPage; i <= endPage; i++) {
            html += `
                            <li class="page-item ${i === current ? 'active' : ''}">
                                <button class="page-link" onclick="MeterReadings.goToPage(${i})">${i}</button>
                            </li>`;
        }

        html += `
                            <li class="page-item ${current === total ? 'disabled' : ''}">
                                <button class="page-link" onclick="MeterReadings.goToPage(${current + 1})" 
                                        ${current === total ? 'disabled' : ''} aria-label="Next">
                                    <span aria-hidden="true">&rsaquo;</span>
                                </button>
                            </li>
                            <li class="page-item ${current === total ? 'disabled' : ''}">
                                <button class="page-link" onclick="MeterReadings.goToPage(${total})" 
                                        ${current === total ? 'disabled' : ''} aria-label="Last">
                                    <span aria-hidden="true">&raquo;</span>
                                </button>
                            </li>
                        </ul>
                    </nav>
                </div>
                <div class="d-flex align-items-center">
                    <span class="me-3 text-muted">Page ${current} of ${total}</span>
                    <div class="input-group" style="width: 120px;">
                        <input type="number" class="form-control form-control-sm" 
                               id="quickJumpPage" placeholder="Page" min="1" max="${total}">
                        <button class="btn btn-outline-secondary btn-sm" type="button" 
                                onclick="MeterReadings.quickJumpToPage()">Go</button>
                    </div>
                </div>
            </div>
        </div>`;

        return html;
    },

    /**
     * Go to specific page
     */
    goToPage: function (page) {
        this.config.currentPage = page;
        this.loadReadings();
    },

    /**
     * Quick jump to page from input
     */
    quickJumpToPage: function () {
        const input = document.getElementById('quickJumpPage');
        if (input) {
            const page = parseInt(input.value);
            if (page && page >= 1) {
                this.goToPage(page);
                input.value = '';
            }
        }
    },

    /**
     * Apply current filters
     */
    applyFilters: function () {
        this.config.currentPage = 1; // Reset to first page
        this.loadReadings();
        this.updateMeterStats();
    },

    /**
     * Clear all filters
     */
    clearFilters: function () {
        // Reset form controls
        const meterSelect = document.getElementById('meterSelect');
        const startDate = document.getElementById('startDate');
        const endDate = document.getElementById('endDate');
        const pageSize = document.getElementById('pageSize');

        if (meterSelect) meterSelect.value = '';
        if (startDate) startDate.value = '';
        if (endDate) endDate.value = '';
        if (pageSize) pageSize.value = '50';

        // Reset config
        this.config.selectedMeterId = null;
        this.config.startDate = null;
        this.config.endDate = null;
        this.config.pageSize = 50;
        this.config.currentPage = 1;

        // Hide stats panel
        const statsPanel = document.getElementById('statsPanel');
        if (statsPanel) {
            statsPanel.style.display = 'none';
        }

        // Reload data
        this.loadReadings();
    },

    /**
     * Refresh current data
     */
    refreshData: function () {
        console.log('Refreshing data');
        this.loadReadings();
        this.updateMeterStats();
    },

    /**
     * Update meter statistics
     */
    updateMeterStats: function () {
        if (!this.config.selectedMeterId) {
            const statsPanel = document.getElementById('statsPanel');
            if (statsPanel) {
                statsPanel.style.display = 'none';
            }
            return;
        }

        const params = new URLSearchParams({
            meterId: this.config.selectedMeterId
        });

        if (this.config.startDate) {
            params.append('startDate', this.config.startDate);
        }

        if (this.config.endDate) {
            params.append('endDate', this.config.endDate);
        }

        fetch(`${this.endpoints.getMeterStats}?${params}`)
            .then(response => response.json())
            .then(data => {
                if (data.success) {
                    this.updateStatsPanel(data.data);
                } else {
                    console.error('Failed to load meter stats:', data.error);
                }
            })
            .catch(error => {
                console.error('Error loading meter stats:', error);
            });
    },

    /**
     * Update statistics panel
     */
    updateStatsPanel: function (stats) {
        const statsPanel = document.getElementById('statsPanel');
        if (!statsPanel) return;

        // Update individual stat elements
        const statElements = {
            'statReadingCount': stats.readingCount,
            'statMinValue': this.formatValue(stats.minValue),
            'statAvgValue': this.formatValue(stats.avgValue),
            'statMaxValue': this.formatValue(stats.maxValue),
            'statFirstReading': this.formatDateTime(stats.firstReading),
            'statLastReading': this.formatDateTime(stats.lastReading)
        };

        Object.entries(statElements).forEach(([id, value]) => {
            const element = document.getElementById(id);
            if (element) {
                element.textContent = value;
            }
        });

        // Show the panel
        statsPanel.style.display = 'block';
    },

    /**
     * Show/hide loading indicator
     */
    showLoading: function (show) {
        const indicator = document.getElementById('loadingIndicator');
        if (indicator) {
            indicator.style.display = show ? 'inline-block' : 'none';
        }

        // Disable/enable interactive elements
        document.querySelectorAll('button, select, input').forEach(element => {
            element.disabled = show;
        });
    },

    /**
     * Show error message
     */
    showError: function (message) {
        // Remove any existing alerts
        document.querySelectorAll('.alert-danger').forEach(alert => alert.remove());

        // Create new alert
        const alertDiv = document.createElement('div');
        alertDiv.className = 'alert alert-danger alert-dismissible fade show';
        alertDiv.innerHTML = `
            ${message}
            <button type="button" class="btn-close" data-bs-dismiss="alert" aria-label="Close"></button>
        `;

        // Insert at top of card body
        const cardBody = document.querySelector('.card-body');
        if (cardBody) {
            cardBody.insertBefore(alertDiv, cardBody.firstChild);
        }
    },

    /**
     * Update UI state
     */
    updateUIState: function () {
        // Update form controls
        const meterSelect = document.getElementById('meterSelect');
        const startDate = document.getElementById('startDate');
        const endDate = document.getElementById('endDate');
        const pageSize = document.getElementById('pageSize');

        if (meterSelect && this.config.selectedMeterId) {
            meterSelect.value = this.config.selectedMeterId;
        }

        if (startDate && this.config.startDate) {
            startDate.value = this.config.startDate;
        }

        if (endDate && this.config.endDate) {
            endDate.value = this.config.endDate;
        }

        if (pageSize) {
            pageSize.value = this.config.pageSize;
        }

        // Update active tab
        this.updateActiveTab(this.config.currentViewType);
    },

    /**
     * Utility functions
     */
    getViewTypeDisplayName: function () {
        const names = {
            'raw': 'Raw Readings',
            'daily': 'Daily Aggregated',
            'monthly': 'Monthly Aggregated',
            'yearly': 'Yearly Aggregated'
        };
        return names[this.config.currentViewType] || 'Unknown View';
    },

    getSelectedMeterName: function () {
        const meterSelect = document.getElementById('meterSelect');
        if (meterSelect && meterSelect.selectedOptions[0]) {
            return meterSelect.selectedOptions[0].text;
        }
        return 'Unknown Meter';
    },

    formatValue: function (value) {
        if (value === null || value === undefined) return '-';
        return parseFloat(value).toFixed(2);
    },

    formatTimestamp: function (timestamp) {
        if (!timestamp) return '-';
        const date = new Date(timestamp);
        const viewType = this.config.currentViewType;

        switch (viewType) {
            case 'daily':
                return date.toISOString().split('T')[0]; // YYYY-MM-DD
            case 'monthly':
                return date.toISOString().substr(0, 7); // YYYY-MM
            case 'yearly':
                return date.getFullYear().toString(); // YYYY
            default:
                return date.toLocaleString(); // Full datetime
        }
    },

    formatDateTime: function (dateTime) {
        if (!dateTime || dateTime === '0001-01-01T00:00:00') return 'No data';
        return new Date(dateTime).toLocaleString();
    },

    formatQuality: function (quality) {
        const qualityMap = {
            0: { text: 'Good', class: 'badge bg-success' },
            1: { text: 'Uncertain', class: 'badge bg-warning' },
            2: { text: 'Bad', class: 'badge bg-danger' }
        };

        const q = qualityMap[quality] || { text: 'Unknown', class: 'badge bg-secondary' };
        return `<span class="${q.class}">${q.text}</span>`;
    },

    updatePaginationInfo: function (pagination) {
        this.config.currentPage = pagination.currentPage;
        // Update URL parameters if needed
        if (history.replaceState) {
            const url = new URL(window.location);
            url.searchParams.set('page', pagination.currentPage);
            url.searchParams.set('viewType', this.config.currentViewType);
            history.replaceState(null, '', url);
        }
    },

    /**
     * Modal functions
     */
    viewReadingDetails: function (readingId) {
        console.log('Viewing details for reading:', readingId);
        // Implementation for reading details modal
        alert('Reading details functionality will be implemented');
    },

    viewRawReadings: function (meterId, date) {
        console.log('Viewing raw readings for meter:', meterId, 'date:', date);
        // Switch to raw view with filters
        this.config.selectedMeterId = meterId;
        this.config.startDate = date;
        this.config.endDate = date;
        this.switchViewType('raw');
    },

    showExportDialog: function () {
        console.log('Showing export dialog');
        // Implementation for export functionality
        alert('Export functionality will be implemented');
    }
};

// Make it globally available
window.MeterReadings = MeterReadings;