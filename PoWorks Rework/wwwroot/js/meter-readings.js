/**
 * Meter Readings JavaScript Module
 */

const MeterReadings = {
    config: {
        currentViewType: 'raw',
        selectedMeterId: null,
        currentPage: 1,
        pageSize: 50,
        startDate: null,
        endDate: null,
        isLoading: false
    },

    endpoints: {
        getReadings: '/MeterReadings/GetReadings',
        getMeterStats: '/MeterReadings/GetMeterStats',
        exportReadings: '/MeterReadings/Export'
    },

    init: function (options = {}) {
        Object.assign(this.config, options);
        this.setupEventListeners();
        this.updateUIState();
    },

    setupEventListeners: function () {
        document.querySelectorAll('[data-view-type]').forEach(tab => {
            tab.addEventListener('click', (e) => {
                e.preventDefault();
                this.switchViewType(e.target.getAttribute('data-view-type'));
            });
        });

        const meterSelect = document.getElementById('meterSelect');
        const startDate = document.getElementById('startDate');
        const endDate = document.getElementById('endDate');
        const pageSize = document.getElementById('pageSize');

        if (meterSelect) {
            meterSelect.addEventListener('change', () => {
                const selected = Array.from(meterSelect.selectedOptions).map(opt => opt.value);
                this.config.selectedMeterId = selected.length > 0 ? selected.join(',') : null;
                this.config.currentPage = 1;
                this.updateMeterStats();
                this.loadReadings(); 
            });
        }

        if (startDate) startDate.addEventListener('change', () => this.config.startDate = startDate.value);
        if (endDate) endDate.addEventListener('change', () => this.config.endDate = endDate.value);
        if (pageSize) {
            pageSize.addEventListener('change', () => {
                this.config.pageSize = parseInt(pageSize.value);
                this.config.currentPage = 1;
                this.loadReadings();
            });
        }

        document.getElementById('applyFiltersBtn')?.addEventListener('click', (e) => {
            e.preventDefault(); 
            this.applyFilters();
        });

        document.getElementById('clearFiltersBtn')?.addEventListener('click', () => this.clearFilters());
        document.getElementById('refreshBtn')?.addEventListener('click', () => this.refreshData());
        document.getElementById('exportBtn')?.addEventListener('click', () => this.showExportDialog());
    },

    switchViewType: function (viewType) {
        if (this.config.isLoading || this.config.currentViewType === viewType) return;
        this.config.currentViewType = viewType;
        this.config.currentPage = 1;

        const startDateInput = document.getElementById('startDate');
        const endDateInput = document.getElementById('endDate');
        
        if (startDateInput && endDateInput) {
            const today = new Date();
            let newStart = new Date();

            if (viewType === 'raw' || viewType === 'daily') newStart.setDate(today.getDate() - 30);
            else if (viewType === 'monthly') newStart.setFullYear(today.getFullYear() - 1);
            else if (viewType === 'yearly') newStart.setFullYear(today.getFullYear() - 5);

     
            startDateInput.value = newStart.toISOString().split('T')[0] + 'T00:00';
            endDateInput.value = today.toISOString().split('T')[0] + 'T23:59';
            
            this.config.startDate = startDateInput.value;
            this.config.endDate = endDateInput.value;
        }

        this.updateActiveTab(viewType);
        this.loadReadings();
    },

    updateActiveTab: function (viewType) {
        document.querySelectorAll('[data-view-type]').forEach(tab => {
            tab.classList.remove('active');
            tab.setAttribute('aria-selected', 'false');
        });

        const activeTab = document.querySelector(`[data-view-type="${viewType}"]`);
        if (activeTab) {
            activeTab.classList.add('active');
            activeTab.setAttribute('aria-selected', 'true');
        }

        const titleElement = document.querySelector('.card-header h4');
        if (titleElement) {
            const viewTypeNames = { 'raw': 'Raw Readings', 'daily': 'Daily Aggregated', 'monthly': 'Monthly Aggregated', 'yearly': 'Yearly Aggregated' };
            titleElement.textContent = `Meter Readings - ${viewTypeNames[viewType]}`;
        }
    },

    loadReadings: function () {
        if (this.config.isLoading) return;

        this.config.isLoading = true;
        this.showLoading(true);

        const params = new URLSearchParams({
            viewType: this.config.currentViewType,
            page: this.config.currentPage,
            pageSize: this.config.pageSize
        });

    
        if (this.config.selectedMeterId) params.append('meterIds', this.config.selectedMeterId);
        if (this.config.startDate) params.append('startDate', this.config.startDate);
        if (this.config.endDate) params.append('endDate', this.config.endDate);

        fetch(`${this.endpoints.getReadings}?${params}`)
            .then(response => response.json())
            .then(data => {
                if (data.success) {
                    this.updateReadingsTable(data.data, data.pagination);
                    this.updatePaginationInfo(data.pagination);
                } else {
                    throw new Error(data.error || 'Failed to load readings');
                }
            })
            .catch(error => this.showError('Failed to load readings: ' + error.message))
            .finally(() => {
                this.config.isLoading = false;
                this.showLoading(false);
            });
    },

    updateReadingsTable: function (readings, pagination) {
        const activeTabPane = document.querySelector('.tab-pane.active #readingsContent');
        if (activeTabPane) {
            activeTabPane.innerHTML = this.generateTableHTML(readings, pagination);
            this.updatePaginationInfo(pagination);
        }
    },

    generateTableHTML: function (readings, pagination) {
        if (!readings || readings.length === 0) return this.generateEmptyStateHTML();

        const isRaw = this.config.currentViewType === 'raw';
        const isYearly = this.config.currentViewType === 'yearly';

        let html = `
        <div class="card">
            <div class="card-header bg-light">
                <div class="d-flex justify-content-between align-items-center">
                    <h6 class="mb-0">${this.getViewTypeDisplayName()}</h6>
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
            html += `<th class="text-end">Min</th><th class="text-end">Max</th><th class="text-end">Count</th>`;
            if (!isYearly) html += '<th class="text-end">Sum</th>';
        }

        html += `               <th class="text-center">Actions</th>
                            </tr>
                        </thead>
                        <tbody>`;

        readings.forEach(reading => { html += this.generateTableRow(reading, isRaw, isYearly); });

        html += `           </tbody>
                    </table>
                </div>
            </div>`;

        if (pagination.totalPages > 1) html += this.generatePaginationHTML(pagination);
        html += `</div>`;
        return html;
    },

    generateTableRow: function (reading, isRaw, isYearly) {
        let html = `
        <tr data-reading-id="${reading.readingId}" data-meter-id="${reading.meterId}">
            <td><span class="fw-medium">${reading.meterName}</span></td>
            <td><span class="font-monospace">${this.formatTimestamp(reading.timestamp)}</span></td>
            <td class="text-end"><span class="fw-bold text-success">${this.formatValue(reading.value)}</span></td>`;

        if (isRaw) {
            html += `<td class="text-center">${this.formatQuality(reading.quality)}</td>`;
        } else {
            html += `
            <td class="text-end"><span class="text-muted">${reading.minValue ? this.formatValue(reading.minValue) : '-'}</span></td>
            <td class="text-end"><span class="text-muted">${reading.maxValue ? this.formatValue(reading.maxValue) : '-'}</span></td>
            <td class="text-end">${reading.readingCount ? `<span class="badge bg-info">${reading.readingCount}</span>` : '<span class="text-muted">-</span>'}</td>`;
            if (!isYearly) {
                html += `<td class="text-end"><span class="text-muted">${reading.sumValue ? this.formatValue(reading.sumValue) : '-'}</span></td>`;
            }
        }

        html += `
            <td class="text-center">
                <div class="btn-group btn-group-sm" role="group">
                    <button type="button" class="btn btn-outline-primary btn-sm" title="View Details" onclick="MeterReadings.viewReadingDetails(${reading.readingId})">
                        <i class="bi bi-eye"></i>
                    </button>`;

        if (!isRaw) {
            html += `<button type="button" class="btn btn-outline-info btn-sm" title="View Raw Data" onclick="MeterReadings.viewRawReadings(${reading.meterId}, '${reading.timestamp}')"><i class="bi bi-list-ul"></i></button>`;
        }

        html += `</div></td></tr>`;
        return html;
    },

    generateEmptyStateHTML: function () {
        return `
        <div class="card">
            <div class="card-body">
                <div class="text-center p-5">
                    <div class="mb-3"><i class="bi bi-graph-up-arrow display-1 text-muted"></i></div>
                    <h5 class="text-muted">No readings found</h5>
                    <button type="button" class="btn btn-primary mt-3" onclick="MeterReadings.clearFilters()"><i class="bi bi-funnel"></i> Clear Filters</button>
                </div>
            </div>
        </div>`;
    },

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
                                <button class="page-link" onclick="MeterReadings.goToPage(1)">&laquo;</button>
                            </li>
                            <li class="page-item ${current === 1 ? 'disabled' : ''}">
                                <button class="page-link" onclick="MeterReadings.goToPage(${current - 1})">&lsaquo;</button>
                            </li>`;

        for (let i = startPage; i <= endPage; i++) {
            html += `<li class="page-item ${i === current ? 'active' : ''}"><button class="page-link" onclick="MeterReadings.goToPage(${i})">${i}</button></li>`;
        }

        html += `           <li class="page-item ${current === total ? 'disabled' : ''}">
                                <button class="page-link" onclick="MeterReadings.goToPage(${current + 1})">&rsaquo;</button>
                            </li>
                            <li class="page-item ${current === total ? 'disabled' : ''}">
                                <button class="page-link" onclick="MeterReadings.goToPage(${total})">&raquo;</button>
                            </li>
                        </ul>
                    </nav>
                </div>
                <div class="d-flex align-items-center">
                    <span class="me-3 text-muted">Page ${current} of ${total}</span>
                </div>
            </div>
        </div>`;
        return html;
    },

    goToPage: function (page) {
        this.config.currentPage = page;
        this.loadReadings();
    },

    applyFilters: function () {
        this.config.currentPage = 1;
        this.loadReadings();
        this.updateMeterStats();
    },

    clearFilters: function () {
        if (window.meterMultiSelect) window.meterMultiSelect.clearSelection();
        const startDate = document.getElementById('startDate');
        const endDate = document.getElementById('endDate');

        if (startDate) startDate.value = '';
        if (endDate) endDate.value = '';

        this.config.selectedMeterId = null;
        this.config.startDate = null;
        this.config.endDate = null;
        this.config.currentPage = 1;

        const statsPanel = document.getElementById('statsPanel');
        if (statsPanel) statsPanel.style.display = 'none';

        this.loadReadings();
    },

    refreshData: function () {
        this.loadReadings();
        this.updateMeterStats();
    },

    updateMeterStats: function () {
        if (!this.config.selectedMeterId) {
            const statsPanel = document.getElementById('statsPanel');
            if (statsPanel) statsPanel.style.display = 'none';
            return;
        }

        const params = new URLSearchParams({ meterIds: this.config.selectedMeterId });
        if (this.config.startDate) params.append('startDate', this.config.startDate);
        if (this.config.endDate) params.append('endDate', this.config.endDate);

        fetch(`${this.endpoints.getMeterStats}?${params}`)
            .then(response => response.json())
            .then(data => {
                if (data.success && data.data) {
                    this.updateStatsPanel(data.data);
                }
            });
    },

    updateStatsPanel: function (stats) {
        const statsPanel = document.getElementById('statsPanel');
        if (!statsPanel) return;

        document.getElementById('statReadingCount').textContent = stats.readingCount;
        document.getElementById('statMinValue').textContent = this.formatValue(stats.minValue);
        document.getElementById('statAvgValue').textContent = this.formatValue(stats.avgValue);
        document.getElementById('statMaxValue').textContent = this.formatValue(stats.maxValue);
        document.getElementById('statFirstReading').textContent = this.formatDateTime(stats.firstReading);
        document.getElementById('statLastReading').textContent = this.formatDateTime(stats.lastReading);

        statsPanel.style.display = 'block';
    },

    showLoading: function (show) {
        const indicator = document.getElementById('loadingIndicator');
        if (indicator) indicator.style.display = show ? 'inline-block' : 'none';
    },

    showError: function (message) {
        document.querySelectorAll('.alert-danger').forEach(alert => alert.remove());
        const alertDiv = document.createElement('div');
        alertDiv.className = 'alert alert-danger alert-dismissible fade show mt-3';
        alertDiv.innerHTML = `${message} <button type="button" class="btn-close" data-bs-dismiss="alert"></button>`;
        document.querySelector('.card-body').prepend(alertDiv);
    },

    updateUIState: function () {
        this.updateActiveTab(this.config.currentViewType);
    },

    getViewTypeDisplayName: function () {
        const names = { 'raw': 'Raw Readings', 'daily': 'Daily Aggregated', 'monthly': 'Monthly Aggregated', 'yearly': 'Yearly Aggregated' };
        return names[this.config.currentViewType] || 'Unknown View';
    },

    formatValue: function (value) {
        if (value === null || value === undefined) return '-';
        return parseFloat(value).toFixed(2);
    },

    formatTimestamp: function (timestamp) {
        if (!timestamp) return '-';
        const date = new Date(timestamp);
        return this.config.currentViewType === 'daily' ? date.toISOString().split('T')[0] : 
               this.config.currentViewType === 'monthly' ? date.toISOString().substr(0, 7) : 
               this.config.currentViewType === 'yearly' ? date.getFullYear().toString() : 
               date.toLocaleString();
    },

    formatDateTime: function (dateTime) {
        if (!dateTime || dateTime === '0001-01-01T00:00:00') return 'No data';
        return new Date(dateTime).toLocaleString();
    },

    formatQuality: function (quality) {
        return (quality === null || quality === undefined) ? '<span class="badge bg-secondary">N/A</span>' : `<span class="badge bg-info">${quality}</span>`;
    },

    updatePaginationInfo: function (pagination) {
        this.config.currentPage = pagination.currentPage;
    },
viewReadingDetails: function (readingId) {
        console.log("STEP 1: Click detected on reading ID", readingId);
        
        const modalEl = document.getElementById('readingDetailsModal');
        if (!modalEl) {
            alert("Error: Modal element not found in DOM.");
            return;
        }

        try {
            const modal = bootstrap.Modal.getOrCreateInstance(modalEl);
            const content = document.getElementById('readingDetailsContent');
            const row = document.querySelector(`tr[data-reading-id="${readingId}"]`);
            
            console.log("STEP 2: Elements search...", { 
                ModalFound: !!content, 
                RowFound: !!row 
            });

            if (row && content) {
                const meterName = row.cells[0].innerText.trim();
                const timestamp = row.cells[1].innerText.trim();
                const value = row.cells[2].innerText.trim();
                
                content.innerHTML = `
                    <div class="alert alert-light border border-primary text-start m-3">
                        <h4 class="alert-heading text-primary"><i class="bi bi-speedometer2"></i> ${meterName}</h4>
                        <hr>
                        <div class="row mt-3">
                            <div class="col-md-6">
                                <p class="mb-1 text-muted">Timestamp</p>
                                <p class="fs-5 fw-bold">${timestamp}</p>
                            </div>
                            <div class="col-md-6">
                                <p class="mb-1 text-muted">Recorded Value</p>
                                <p class="fs-3 fw-bold text-success">${value}</p>
                            </div>
                        </div>
                    </div>
                `;
                modal.show();
                console.log("STEP 3: Success, modal is open!");
            } else {
                alert("Error: Cannot find table row for this ID.");
            }
        } catch (error) {
            console.error("Bootstrap critical error:", error);
        }
    }
    };

window.MeterReadings = MeterReadings;