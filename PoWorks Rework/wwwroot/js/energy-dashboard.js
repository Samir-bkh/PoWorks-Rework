// FIXED: Energy Dashboard JavaScript - Date Filtering and Filter Dependencies
(function () {
    'use strict';

    // Global variables
    let chart = null;
    let currentData = [];
    let tenants = [];
    let meters = [];
    let loadingTimeout = null;
    let meterOffset = 0;
    let hasMoreMeters = false;
    let autoRefreshInterval = null;
    let availableDateRange = null;

    // FIXED: Enhanced initialization with intelligent date defaults
    document.addEventListener('DOMContentLoaded', function () {
        console.log('FIXED Dashboard initializing with intelligent date handling...');

        try {
            attachEventListeners();

            // FIXED: Load intelligent date defaults first, then initialize
            Promise.all([
                loadDateRangeSuggestions(),
                loadTenants()
            ]).then(() => {
                return loadDashboardStats();
            }).then(() => {
                return loadMetersForCurrentDateRange();
            }).then(() => {
                return loadChartData();
            }).then(() => {
                console.log('FIXED Dashboard initialization completed');
            }).catch(error => {
                console.error('Dashboard initialization error:', error);
                showNotification('Dashboard initialization failed, showing demo data', 'warning');
                showDemoChart();
            });

        } catch (initError) {
            console.error('Critical initialization error:', initError);
            showDemoChart();
        }
    });

    // FIXED: Load intelligent date range suggestions
    async function loadDateRangeSuggestions() {
        try {
            const response = await fetch('/Dashboard/GetDateRangeSuggestions');
            if (!response.ok) {
                throw new Error(`HTTP ${response.status}: ${response.statusText}`);
            }

            const suggestions = await response.json();

            if (suggestions.success) {
                // FIXED: Set intelligent defaults based on available data
                document.getElementById('startDate').value = suggestions.defaultStartDate;
                document.getElementById('endDate').value = suggestions.defaultEndDate;

                updateDataStatus(suggestions.message, 'info');

                // Store available date range info
                if (suggestions.alternatives && suggestions.alternatives.length > 0) {
                    addDateRangeAlternatives(suggestions.alternatives);
                }

                console.log('Set intelligent date defaults:', suggestions.defaultStartDate, 'to', suggestions.defaultEndDate);
            } else {
                // Fallback to default date initialization
                initializeDateFilters();
                updateDataStatus('Using default date range', 'warning');
            }

        } catch (error) {
            console.error('Error loading date range suggestions:', error);
            initializeDateFilters();
            updateDataStatus('Error loading optimal date range, using defaults', 'warning');
        }
    }

    // FIXED: Add date range alternatives to UI
    function addDateRangeAlternatives(alternatives) {
        const dateFilter = document.getElementById('dateFilter');

        // Add custom options for quick selection
        alternatives.forEach(alt => {
            const option = document.createElement('option');
            option.value = `custom_${alt.name.replace(/\s+/g, '_').toLowerCase()}`;
            option.textContent = alt.name;
            option.dataset.startDate = alt.startDate;
            option.dataset.endDate = alt.endDate;
            option.dataset.description = alt.description;
            dateFilter.appendChild(option);
        });
    }

    function initializeDateFilters() {
        const endDate = new Date();
        const startDate = new Date();
        startDate.setMonth(startDate.getMonth() - 1);

        document.getElementById('startDate').value = formatDate(startDate);
        document.getElementById('endDate').value = formatDate(endDate);
    }

    function formatDate(date) {
        return date.toISOString().split('T')[0];
    }

    // FIXED: Enhanced event listeners with date change handling
    function attachEventListeners() {
        document.getElementById('tenantFilter').addEventListener('change', onTenantChange);
        document.getElementById('applyFilters').addEventListener('click', loadChartData);
        document.getElementById('resetFilters').addEventListener('click', resetFilters);
        document.getElementById('chartType').addEventListener('change', updateChartType);

        // FIXED: Date filter changes now trigger meter reload
        document.getElementById('dateFilter').addEventListener('change', onDateFilterChange);
        document.getElementById('startDate').addEventListener('change', onDateRangeChange);
        document.getElementById('endDate').addEventListener('change', onDateRangeChange);

        // Meter controls
        document.getElementById('meterLimit').addEventListener('change', onMeterLimitChange);
        document.getElementById('loadMoreMeters').addEventListener('click', loadMoreMeters);
        document.getElementById('refreshMeters').addEventListener('click', refreshMeters);

        // Auto refresh and export
        document.getElementById('autoRefresh').addEventListener('click', toggleAutoRefresh);
        document.getElementById('exportChart').addEventListener('click', exportChart);

        // FULL SCREEN
        document.getElementById('fullscreenChart')?.addEventListener('click', toggleFullscreen);

        
        document.getElementById('tabDaily')?.addEventListener('click', (e) => { 
            e.preventDefault(); 
            switchTab('daily', 'tabDaily'); 
        });
        document.getElementById('tabMonthly')?.addEventListener('click', (e) => { 
            e.preventDefault(); 
            switchTab('monthly', 'tabMonthly'); 
        });
        document.getElementById('tabYearly')?.addEventListener('click', (e) => { 
            e.preventDefault(); 
            switchTab('yearly', 'tabYearly'); 
        });

        document.getElementById('resetZoomBtn')?.addEventListener('click', () => {
            if (chart) {
                chart.resetZoom();
            }
        }); 

        
        document.querySelectorAll('input[name="viewMode"]').forEach(radio => {
            radio.addEventListener('change', () => {
                console.log(`Display mode changed to : ${radio.value}`);
                loadChartData(); 
            });
        });

        
        document.querySelectorAll('input[name="groupBy"]').forEach(radio => {
            radio.addEventListener('change', (e) => {
                console.log(`Group changed to : ${e.target.value}`);
                
                const meterSelect = document.getElementById('meterFilter');
                
                if (e.target.value === 'tenant') {
                    
                    if (meterSelect.value !== '') {
                        showNotification("Filter reset: Showing all meters for the tenant.", "info");
                    }
                    
                    meterSelect.value = ''; // Force "All Meters"
                    meterSelect.disabled = true; 
                   
                    meterSelect.title = "Filter unavailable in Tenant view (Total consumption)"; 
                } else {
                    meterSelect.disabled = false; 
                    meterSelect.title = ""; 
                }

                loadChartData(); 
            });
        });
    }

    

    // Load tenants 
    async function loadTenants() {
        try {
            const controller = new AbortController();
            const timeoutId = setTimeout(() => controller.abort(), 10000);

            const response = await fetch('/Dashboard/GetTenants', {
                signal: controller.signal
            });

            clearTimeout(timeoutId);

            if (!response.ok) {
                throw new Error(`HTTP ${response.status}: ${response.statusText}`);
            }

            const data = await response.json();
            tenants = data || [];
            populateTenantDropdown();

        } catch (error) {
            console.error('Error loading tenants:', error);
            if (error.name === 'AbortError') {
                showNotification('Tenant loading timeout - continuing without tenants', 'warning');
            }
        }
    }

    function populateTenantDropdown() {
        const tenantSelect = document.getElementById('tenantFilter');
        tenantSelect.innerHTML = '<option value="">All Tenants</option>';

        tenants.forEach(tenant => {
            const option = document.createElement('option');
            option.value = tenant.id;
            option.textContent = tenant.name;
            tenantSelect.appendChild(option);
        });
    }

    // FIXED: Enhanced tenant change that considers date range
    function onTenantChange(event) {
        const tenantId = event.target.value;

        // FIXED: Always reload meters for the current date range when tenant changes
        loadMetersForCurrentDateRange();
    }

    // FIXED: Date filter change handler
    function onDateFilterChange(event) {
        const filterType = event.target.value;

        // Check if it's a custom alternative
        if (filterType.startsWith('custom_')) {
            const option = event.target.selectedOptions[0];
            document.getElementById('startDate').value = option.dataset.startDate;
            document.getElementById('endDate').value = option.dataset.endDate;
            showNotification(`Applied ${option.textContent}: ${option.dataset.description}`, 'info');
        } else {
            // Standard date filter logic
            const startDate = document.getElementById('startDate');
            const endDate = document.getElementById('endDate');
            const today = new Date();

            switch (filterType) {
                case 'daily':
                    // The past 30 days
                    startDate.value = formatDate(new Date(today.getTime() - 30 * 24 * 60 * 60 * 1000));
                    endDate.value = formatDate(today);
                    break;
                case 'monthly':
                    // The past 12 months
                    startDate.value = formatDate(new Date(today.getFullYear(), today.getMonth() - 11, 1));
                    endDate.value = formatDate(today);
                    break;
                case 'yearly':
                    // The past 5 years 
                    startDate.value = formatDate(new Date(today.getFullYear() - 4, 0, 1));
                    endDate.value = formatDate(today);
                    break;
            }
        }

        // FIXED: Trigger meter reload and chart update when date filter changes
        onDateRangeChange();
    }

    // FIXED: Date range change handler
    async function onDateRangeChange() {
        console.log('Date range changed, reloading meters and chart...');

        // Validate date range
        const startDate = new Date(document.getElementById('startDate').value);
        const endDate = new Date(document.getElementById('endDate').value);

        if (startDate >= endDate) {
            showNotification('Start date must be before end date', 'error');
            return;
        }

        const daysDiff = (endDate - startDate) / (1000 * 60 * 60 * 24);
        if (daysDiff > 365) {
            showNotification('Date range too large (max 365 days). Chart performance may be affected.', 'warning');
        }

        updateDataStatus('Checking for data in new date range', 'info');

        try {
            // FIXED: Reload meters for the new date range
            await loadMetersForCurrentDateRange();

            // FIXED: Reload chart data for new date range
            await loadChartData();

        } catch (error) {
            console.error('Error handling date range change:', error);
            showNotification('Error loading data for new date range', 'error');
        }
    }

    // NEW: Load dashboard statistics for current date range
    async function loadDashboardStats() {
        try {
            const startDate = document.getElementById('startDate').value;
            const endDate = document.getElementById('endDate').value;

            const url = `/Dashboard/GetDashboardStats?startDate=${startDate}&endDate=${endDate}`;
            const response = await fetch(url);

            if (!response.ok) {
                throw new Error(`HTTP ${response.status}: ${response.statusText}`);
            }

            const stats = await response.json();
            updateDashboardStats(stats);

            if (stats.dateRange && !stats.dateRange.hasDataInRange) {
                updateDataStatus(`No data found in range ${startDate} to ${endDate}. ${stats.message}`, 'warning');
            } else {
                updateDataStatus(stats.message, stats.hasData ? 'success' : 'warning');
            }

        } catch (error) {
            console.error('Error loading dashboard stats:', error);
            updateDataStatus('Unable to load dashboard statistics', 'warning');
        }
    }

   // Update dashboard statistics display
    function updateDashboardStats(stats) {
// The "Meter Information" panel was removed from the interface because it was unnecessary for the end user.
// We keep the function empty so as not to break the loading chain (loadDashboardStats).
    }

    // FIXED: Load meters that have data in the current date range
    async function loadMetersForCurrentDateRange() {
        try {
            const startDate = document.getElementById('startDate').value;
            const endDate = document.getElementById('endDate').value;
            const tenantId = document.getElementById('tenantFilter').value;
            const limit = parseInt(document.getElementById('meterLimit').value) || 5;

            updateDataStatus('Loading meters with data in date range...', 'info');

            const controller = new AbortController();
            const timeoutId = setTimeout(() => controller.abort(), 15000);

            const requestBody = {
                startDate: startDate,
                endDate: endDate,
                tenantId: tenantId || null,
                limit: limit,
                offset: 0,
                includeNullTenants: true
            };

            const response = await fetch('/Dashboard/GetMetersWithData', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(requestBody),
                signal: controller.signal
            });

            clearTimeout(timeoutId);

            if (!response.ok) {
                throw new Error(`HTTP ${response.status}: ${response.statusText}`);
            }

            const data = await response.json();

            if (data.success) {
                meters = data.meters || [];
                hasMoreMeters = data.hasMore || false;
                meterOffset = limit;

                populateMeterDropdown();
                updateLoadMoreButton();

                updateDataStatus(data.message, 'success');

                console.log(`Loaded ${meters.length} meters with data in range ${startDate} to ${endDate}`);
            } else {
                throw new Error(data.error || 'Failed to load meters');
            }

        } catch (error) {
            console.error('Error loading meters for date range:', error);

            if (error.name === 'AbortError') {
                showNotification('Meter loading timeout for date range', 'warning');
            } else {
                showNotification(`Error loading meters: ${error.message}`, 'error');
            }

            updateDataStatus('Error loading meters for date range', 'error');

            // Clear meter list on error
            meters = [];
            populateMeterDropdown();
        }
    }

    // Enhanced meter dropdown population (FIXED: Memorize selection)
    function populateMeterDropdown() {
        const meterSelect = document.getElementById('meterFilter');
        
        // 1. Save the currently selected counter before destroying everything
        const currentSelectedMeter = meterSelect.value;

        meterSelect.innerHTML = '<option value="">All Meters</option>';

        if (meters.length === 0) {
            const option = document.createElement('option');
            option.value = '';
            option.textContent = 'No meters with data in date range';
            option.disabled = true;
            meterSelect.appendChild(option);
            return;
        }

        meters.forEach(meter => {
            const option = document.createElement('option');
            option.value = meter.id;
            option.textContent = meter.displayName || `${meter.name} (${meter.type})`;

            if (meter.tenantName) {
                option.textContent += ` - ${meter.tenantName}`;
            } else {
                option.textContent += ' [No Tenant]';
            }

            meterSelect.appendChild(option);
        });

        // 2. Restore selection
        if (currentSelectedMeter) {
    
            const optionExists = Array.from(meterSelect.options).some(opt => opt.value === currentSelectedMeter);
            if (optionExists) {
                meterSelect.value = currentSelectedMeter; 
            } else {
             
                const extraOption = document.createElement('option');
                extraOption.value = currentSelectedMeter;
                extraOption.textContent = "Selected counter";
                meterSelect.appendChild(extraOption);
                meterSelect.value = currentSelectedMeter;
            }
        }
    }

    // Meter limit change handler (updated)
    function onMeterLimitChange(event) {
        const newLimit = parseInt(event.target.value);
        console.log(`Meter limit changed to: ${newLimit}`);

        // Reset and reload with new limit for current date range
        meterOffset = 0;
        hasMoreMeters = false;
        loadMetersForCurrentDateRange();
    }

    // Load more meters (updated)
    async function loadMoreMeters() {
        try {
            const startDate = document.getElementById('startDate').value;
            const endDate = document.getElementById('endDate').value;
            const tenantId = document.getElementById('tenantFilter').value;
            const limit = parseInt(document.getElementById('meterLimit').value) || 5;

            const controller = new AbortController();
            const timeoutId = setTimeout(() => controller.abort(), 15000);

            const requestBody = {
                startDate: startDate,
                endDate: endDate,
                tenantId: tenantId || null,
                limit: limit,
                offset: meterOffset,
                includeNullTenants: true
            };

            const response = await fetch('/Dashboard/GetMetersWithData', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(requestBody),
                signal: controller.signal
            });

            clearTimeout(timeoutId);

            if (!response.ok) {
                throw new Error(`HTTP ${response.status}: ${response.statusText}`);
            }

            const data = await response.json();

            if (data.success && data.meters.length > 0) {
                meters = meters.concat(data.meters);
                hasMoreMeters = data.hasMore || false;
                meterOffset += limit;

                populateMeterDropdown();
                updateLoadMoreButton();

                showNotification(`Loaded ${data.meters.length} additional meters`, 'success');
            } else {
                hasMoreMeters = false;
                updateLoadMoreButton();
                showNotification('No more meters to load', 'info');
            }

        } catch (error) {
            console.error('Error loading more meters:', error);
            showNotification('Error loading additional meters', 'error');
        }
    }

    // Refresh meters (updated)
    async function refreshMeters() {
        meterOffset = 0;
        hasMoreMeters = false;
        await loadMetersForCurrentDateRange();
        showNotification('Meter list refreshed for current date range', 'success');
    }

    // Update load more button (unchanged)
    function updateLoadMoreButton() {
        const loadMoreBtn = document.getElementById('loadMoreMeters');

        if (hasMoreMeters) {
            loadMoreBtn.style.display = 'block';
            loadMoreBtn.textContent = `Load More (${meters.length} loaded)`;
        } else {
            loadMoreBtn.style.display = 'none';
        }
    }

    // FIXED: Enhanced chart loading with better error handling for date ranges
    async function loadChartData(forcedChartType = null) {
        console.log('FIXED: Loading chart data with date validation...');
        showLoading(true);

        if (loadingTimeout) {
            clearTimeout(loadingTimeout);
        }

        loadingTimeout = setTimeout(() => {
            console.warn('Chart loading timeout - showing demo data');
            showDemoChart();
            showNotification('Chart loading took too long - showing demo data', 'warning');
        }, 15000);

        const filters = {
            dateFilter: document.getElementById('dateFilter').value,
            tenantId: document.getElementById('tenantFilter').value || null,
            meterId: document.getElementById('meterFilter').value || null,
            startDate: document.getElementById('startDate').value,
            endDate: document.getElementById('endDate').value,
            limit: parseInt(document.getElementById('meterLimit').value) || 5,
            chartType: forcedChartType, 
            isComparisonMode: document.getElementById('modeComparison').checked,
            groupBy: document.querySelector('input[name="groupBy"]:checked')?.value || 'meter'
        };

        console.log('FIXED Filters:', filters);

        try {
            const controller = new AbortController();
            const timeoutId = setTimeout(() => controller.abort(), 12000);

            const response = await fetch('/Dashboard/GetConsumptionData', {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json'
                },
                body: JSON.stringify(filters),
                signal: controller.signal
            });

            clearTimeout(timeoutId);
            clearTimeout(loadingTimeout);

            console.log('Response Status:', response.status);

            if (!response.ok) {
                throw new Error(`HTTP ${response.status}: ${response.statusText}`);
            }

            const data = await response.json();
            console.log('FIXED Received data:', data);

            // FIXED: Handle no data in range scenario
            if (data.noDataInRange && data.suggestions) {
                showNotification(data.message, 'warning');
                showDateRangeSuggestions(data.suggestions);
                showDemoChart();
                showLoading(false);
                return;
            }

            // Handle demo data notification
            if (data.isDemoData) {
                showNotification(data.message || 'Showing demo data', 'info');
            } else if (data.message) {
                showNotification(data.message, 'info');
            }

            // Enhanced data display with additional info
            if (data.dataInfo) {
                updateDataInfoDisplay(data.dataInfo);
            }

            currentData = data.chartData;
            updateChart(data.chartData);
            

            if (data.chartData && data.chartData.datasets) {
                renderTopConsumers(data.chartData.datasets);
            }
            
            updateSummaryCards(data.summary);
            updateSummaryCards(data.summary);
            showLoading(false);

            console.log('FIXED Chart updated successfully');

        } catch (error) {
            clearTimeout(loadingTimeout);
            console.error('Chart loading error:', error);

            if (error.name === 'AbortError') {
                console.warn('Request timeout - showing demo data');
                showNotification('Request timeout - showing demo data', 'warning');
            } else {
                showNotification(`Error: ${error.message} - showing demo data`, 'error');
            }

            showDemoChart();
        }
    }

    // NEW: Show date range suggestions when no data found
    function showDateRangeSuggestions(suggestions) {
        const alertDiv = document.createElement('div');
        alertDiv.className = 'alert alert-info alert-dismissible fade show mt-3';
        alertDiv.innerHTML = `
            <h6>Suggested Date Ranges:</h6>
            <p>${suggestions.message}</p>
            <div class="btn-group btn-group-sm" role="group">
                <button type="button" class="btn btn-outline-primary" onclick="applySuggestedDateRange('${suggestions.defaultStartDate}', '${suggestions.defaultEndDate}')">
                    Use Suggested Range
                </button>
                ${suggestions.alternatives.map(alt =>
            `<button type="button" class="btn btn-outline-secondary" onclick="applySuggestedDateRange('${alt.startDate}', '${alt.endDate}')" title="${alt.description}">
                        ${alt.name}
                    </button>`
        ).join('')}
            </div>
            <button type="button" class="btn-close" data-bs-dismiss="alert" aria-label="Close"></button>
        `;

        const container = document.querySelector('.container-fluid');
        if (container) {
            container.insertBefore(alertDiv, container.children[1]);
        }
    }

    // NEW: Apply suggested date range
    window.applySuggestedDateRange = function (startDate, endDate) {
        document.getElementById('startDate').value = startDate;
        document.getElementById('endDate').value = endDate;

        // Remove suggestion alert
        const alerts = document.querySelectorAll('.alert-info');
        alerts.forEach(alert => alert.remove());

        // Trigger reload
        onDateRangeChange();
    };

    // Rest of the functions remain unchanged from Phase 2
    // (updateDataInfoDisplay, showDemoChart, updateChart, etc.)

    function updateDataInfoDisplay(dataInfo) {
        const activeMetersDetail = document.getElementById('activeMetersDetail');
        if (activeMetersDetail && dataInfo.availableMeters) {
            activeMetersDetail.textContent = `Out of ${dataInfo.availableMeters} available (limit: ${dataInfo.appliedLimit})`;
        }

        const statusMessage = `Showing ${dataInfo.shownMeters} of ${dataInfo.availableMeters} meters (${dataInfo.metersWithTenants} with tenants, ${dataInfo.metersWithoutTenants} without)`;
        updateDataStatus(statusMessage, 'success');
    }

    function updateDataStatus(message, type = 'info') {
        const statusDiv = document.getElementById('dataStatus');
        const statusText = document.getElementById('dataStatusText');

        if (statusDiv && statusText) {
            statusText.textContent = message;
            statusDiv.className = `alert alert-${getBootstrapAlertClass(type)}`;
            statusDiv.style.display = 'block';

            if (type === 'success') {
                setTimeout(() => {
                    statusDiv.style.display = 'none';
                }, 5000);
            }
        }
    }

    // Keep all other functions from Phase 2 unchanged
    // (showDemoChart, updateChart, updateSummaryCards, resetFilters, etc.)

    function showDemoChart() {
        const demoData = {
            labels: ['2025-08-05', '2025-08-06', '2025-08-07', '2025-08-08', '2025-08-09', '2025-08-10', '2025-08-11'],
            datasets: [
                {
                    label: 'Demo Meter 1 (kWh)',
                    data: [120, 135, 148, 162, 155, 170, 165]
                },
                {
                    label: 'Demo Meter 2 (kWh)',
                    data: [200, 210, 235, 225, 240, 255, 250]
                }
            ]
        };

        const demoSummary = {
            totalConsumption: 1925,
            averageDaily: 275,
            peakUsage: 255,
            activeMeters: 2
        };

        currentData = demoData;
        updateChart(demoData);
        updateSummaryCards(demoSummary);
        showLoading(false);
    }

    if (typeof Chart !== 'undefined' && typeof ChartZoom !== 'undefined') {
    Chart.register(ChartZoom);
    }

   function updateChart(data, forcedType = null) {
        console.log('Updating chart with data:', data);

        const canvas = document.getElementById('consumptionChart');
        if (!canvas) {
            console.error('Canvas not found');
            return;
        }

        const ctx = canvas.getContext('2d');
        const chartType = forcedType || document.getElementById('chartType')?.value || 'bar';

        if (typeof Chart === 'undefined') {
            console.error('Chart.js not loaded');
            return;
        }

        if (chart) {
            chart.destroy();
        }

        try {
            const processedDatasets = data.datasets.map((dataset, index) => {
                
                const emvueColors = {
                    'Monday': '#4BC0C0',     
                    'Tuesday': '#36A2EB',     
                    'Wednesday': '#FF9F40', 
                    'Thursday': '#FFCE56',    
                    'Friday': '#9966FF',  
                    'Saturday': '#FF6384',    
                    'Sunday': '#C9CBCF'   
                };

                const colorKey = Object.keys(emvueColors).find(k => dataset.label.includes(k));
                const finalColor = colorKey ? emvueColors[colorKey] : getColor(index, 1);
                
               
                const validPointsCount = dataset.data.filter(v => v !== null && v !== undefined).length;
                
               
                const pRadius = validPointsCount <= 1 ? 6 : 3;
                
                return {
                    label: dataset.label,
                    data: dataset.data,
                    backgroundColor: chartType === 'line' ? 'transparent' : finalColor,
                    borderColor: finalColor,
                    borderWidth: chartType === 'line' ? 2.5 : 1, 
                    fill: false, 
                    tension: 0.1, 
                    spanGaps: true,  
                    pointRadius: pRadius,
                    pointBackgroundColor: finalColor, 
                    pointHoverRadius: 6 
                };
            });

            chart = new Chart(ctx, {
                type: chartType,
                data: {
                    labels: data.labels,
                    datasets: processedDatasets
                },
                options: {
                    responsive: true,
                    maintainAspectRatio: false,
                    interaction: { mode: 'index', intersect: false },
                    plugins: {
                        legend: { 
                            position: 'right',  
                            align: 'start',     
                            labels: { 
                                usePointStyle: true, 
                                boxWidth: 8,    
                                padding: 12,    
                                font: {
                                    size: 11    
                                }
                            } 
                        },
                        title: { display: false },
                        zoom: {
                            zoom: {
                                wheel: { enabled: true },
                                drag: { enabled: true, backgroundColor: 'rgba(233, 30, 99, 0.2)' },
                                mode: 'x',
                            },
                            pan: { enabled: true, mode: 'x' }
                        }
                    },
                    scales: {
                        y: {
                            beginAtZero: true,
                            grid: { color: '#f0f0f0' }, 
                            title: { display: true, text: 'Puissance (kW) / Énergie (kWh)', color: '#888' }
                        },
                        x: { grid: { display: false } }
                    }
                }
            });
            console.log('Chart created successfully');
        } catch (chartError) {
            console.error('Chart creation error:', chartError);
        }
    }

    function updateChartType() {
        if (currentData && currentData.labels) {
            updateChart(currentData);
        }
    }

    function getColor(index, alpha) {
        const colors = [
            `rgba(54, 162, 235, ${alpha})`,
            `rgba(255, 99, 132, ${alpha})`,
            `rgba(255, 206, 86, ${alpha})`,
            `rgba(75, 192, 192, ${alpha})`,
            `rgba(153, 102, 255, ${alpha})`,
            `rgba(255, 159, 64, ${alpha})`
        ];
        return colors[index % colors.length]; 
    }

    function updateSummaryCards(summary) {
        try {
            document.getElementById('totalConsumption').textContent = `${summary.totalConsumption.toFixed(2)} kWh`;
            document.getElementById('avgDaily').textContent = `${summary.averageDaily.toFixed(2)} kWh`;
            document.getElementById('peakUsage').textContent = `${summary.peakUsage.toFixed(2)} kWh`;
            document.getElementById('activeMeters').textContent = summary.activeMeters;

            const totalDetail = document.getElementById('totalConsumptionDetail');
            if (totalDetail && summary.totalMeters) {
                totalDetail.textContent = `From ${summary.activeMeters} of ${summary.totalMeters} meters`;
            }

        } catch (error) {
            console.error('Error updating summary cards:', error);
        }
    }

    function resetFilters() {
        document.getElementById('dateFilter').value = 'monthly';
        document.getElementById('tenantFilter').value = '';
        document.getElementById('meterFilter').value = '';
        document.getElementById('meterLimit').value = '5';
        document.getElementById('chartType').value = 'bar';

        // Reset to intelligent defaults
        loadDateRangeSuggestions().then(() => {
            meterOffset = 0;
            hasMoreMeters = false;
            loadMetersForCurrentDateRange();
            loadChartData();
        });
    }

    function toggleAutoRefresh() {
        const button = document.getElementById('autoRefresh');

        if (autoRefreshInterval) {
            clearInterval(autoRefreshInterval);
            autoRefreshInterval = null;
            button.classList.remove('active');
            button.title = 'Enable Auto Refresh (30s)';
            showNotification('Auto refresh disabled', 'info');
        } else {
            autoRefreshInterval = setInterval(() => {
                console.log('🔄 Auto refreshing chart data...');
                loadChartData();
            }, 30000);

            button.classList.add('active');
            button.title = 'Disable Auto Refresh';
            showNotification('Auto refresh enabled (30s)', 'success');
        }
    }

    function exportChart() {
        if (chart) {
            const url = chart.toBase64Image();
            const link = document.createElement('a');
            link.download = `energy-consumption-${new Date().toISOString().split('T')[0]}.png`;
            link.href = url;
            link.click();
            showNotification('Chart exported successfully', 'success');
        } else {
            showNotification('No chart available to export', 'warning');
        }
    }

    function showLoading(show) {
        const spinner = document.getElementById('loadingSpinner');
        const chartCanvas = document.getElementById('consumptionChart');

        if (show) {
            if (spinner) spinner.classList.remove('d-none');
            if (chartCanvas) chartCanvas.style.opacity = '0.5';
        } else {
            if (spinner) spinner.classList.add('d-none');
            if (chartCanvas) chartCanvas.style.opacity = '1';
        }
    }

    function showNotification(message, type = 'info') {
        console.log(`${type.toUpperCase()}: ${message}`);

        const existingAlerts = document.querySelectorAll('.dashboard-alert');
        existingAlerts.forEach(alert => alert.remove());

        const alertDiv = document.createElement('div');
        alertDiv.className = `alert alert-${getBootstrapAlertClass(type)} alert-dismissible fade show dashboard-alert`;
        alertDiv.innerHTML = `
            ${message}
            <button type="button" class="btn-close" data-bs-dismiss="alert" aria-label="Close"></button>
        `;

        const container = document.querySelector('.container-fluid');
        if (container) {
            container.insertBefore(alertDiv, container.firstChild);

            setTimeout(() => {
                if (alertDiv.parentNode) {
                    alertDiv.remove();
                }
            }, type === 'error' ? 8000 : 5000);
        }
    }

    function getBootstrapAlertClass(type) {
        switch (type) {
            case 'error': return 'danger';
            case 'warning': return 'warning';
            case 'success': return 'success';
            default: return 'info';
        }
    }

    
    window.switchTab = function(filterValue, activeBtnId) {
        console.log(`Clicked tab : ${filterValue}`);

        // 1. Change the apparence of the butom 
        document.getElementById('tabDaily').classList.remove('active');
        document.getElementById('tabMonthly').classList.remove('active');
        document.getElementById('tabYearly').classList.remove('active');
        document.getElementById(activeBtnId).classList.add('active');

       

        // 2. Change the date filter and trigger the update
        const dateFilter = document.getElementById('dateFilter');
        if (dateFilter) {
            dateFilter.value = filterValue;
            dateFilter.dispatchEvent(new Event('change')); 
        }
    };

    //Calculate and show the Top 5
    function renderTopConsumers(datasets) {
        const container = document.getElementById('topConsumersList');
        const titleElement = document.getElementById('topConsumersTitle'); 
        if (!container) return; 

        
        const tenantSelect = document.getElementById('tenantFilter');
        let tenantName = "Globale View"; 
        if (tenantSelect && tenantSelect.selectedIndex > 0) {
            tenantName = tenantSelect.options[tenantSelect.selectedIndex].text; 
        }

        const groupBy = document.querySelector('input[name="groupBy"]:checked')?.value || 'meter';
        const titleType = groupBy === 'tenant' ? 'tenants' : 'meters';

        if (titleElement) {
            titleElement.innerHTML = `<i class=""></i> Top 5 of ${titleType} (${tenantName})`;
        }
        
    
        if (tenantSelect && tenantSelect.selectedIndex > 0) {
            tenantName = tenantSelect.options[tenantSelect.selectedIndex].text; 
        }

        if (titleElement) {
            titleElement.innerHTML = `<i class=""></i> Top 5 of Meters (${tenantName})`;
        }
        // ----------------------------------

        if (!datasets || datasets.length <= 1) {
            container.innerHTML = `
                <div class="alert alert-light text-center border mt-2">
                    <i class="bi bi-info-circle text-primary"></i> 
                    Select <strong>All Meters</strong> (or increase the limit) to see the comparative ranking.
                </div>`;
            return;
        }

        // 1. Calucl the sum for each meter
        const totals = datasets.map(ds => {
            const sum = ds.data.reduce((a, b) => a + (b || 0), 0); 
            return { name: ds.label, total: sum };
        });

        // 2. (DESC)
        totals.sort((a, b) => b.total - a.total);

        // 3. Keep only the first 5
        const top5 = totals.slice(0, 5);

       
        const maxTotal = top5[0].total > 0 ? top5[0].total : 1;

        //Create the HTML code for progress bars
        container.innerHTML = top5.map(item => {
            const percent = (item.total / maxTotal) * 100;
            return `
                <div class="mb-3">
                    <div class="d-flex justify-content-between mb-1">
                        <span class="fw-bold text-secondary">${item.name}</span>
                        <span class="fw-bold">${item.total.toFixed(2)} kWh</span>
                    </div>
                    <div class="progress" style="height: 10px;">
                        <div class="progress-bar bg-danger" role="progressbar" style="width: ${percent}%"></div>
                    </div>
                </div>
            `;
        }).join('');
    }

    // Function to make the chart full screen
    function toggleFullscreen() {
        const chartCard = document.getElementById('consumptionChart').closest('.card');
        const icon = document.querySelector('#fullscreenChart i');
        
        if (chartCard.classList.contains('chart-fullscreen')) {
            // Exit the full screen
            chartCard.classList.remove('chart-fullscreen');
            icon.classList.remove('bi-fullscreen-exit');
            icon.classList.add('bi-arrows-fullscreen');
            document.body.style.overflow = 'auto'; // 
            showNotification("Full screen mode off", "info");
        } else {
            // Go full screen
            chartCard.classList.add('chart-fullscreen');
            icon.classList.remove('bi-arrows-fullscreen');
            icon.classList.add('bi-fullscreen-exit');
            document.body.style.overflow = 'hidden'; 
            showNotification("Full screen mode on (Press again to exit)", "success");
        }
    }

})();