// Optimized Energy Dashboard JavaScript - Fast Loading with Timeout Handling
(function () {
    'use strict';

    // Global variables
    let chart = null;
    let currentData = [];
    let tenants = [];
    let meters = [];
    let loadingTimeout = null;

    // ✅ OPTIMIZATION 1: Faster initialization
    document.addEventListener('DOMContentLoaded', function () {
        console.log('🚀 Dashboard initializing...');

        try {
            initializeDateFilters();
            attachEventListeners();

            // ✅ Load data in parallel
            Promise.all([
                loadTenants(),
                loadChartData()
            ]).then(() => {
                console.log('✅ Dashboard initialization completed');
            }).catch(error => {
                console.error('❌ Dashboard initialization error:', error);
                showNotification('Dashboard initialization failed, showing demo data', 'warning');
            });

        } catch (initError) {
            console.error('❌ Critical initialization error:', initError);
            showDemoChart();
        }
    });

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

    function attachEventListeners() {
        document.getElementById('tenantFilter').addEventListener('change', onTenantChange);
        document.getElementById('applyFilters').addEventListener('click', loadChartData);
        document.getElementById('resetFilters').addEventListener('click', resetFilters);
        document.getElementById('chartType').addEventListener('change', updateChartType);
        document.getElementById('dateFilter').addEventListener('change', onDateFilterChange);
    }

    // ✅ OPTIMIZATION 2: Fast tenant loading with timeout
    async function loadTenants() {
        try {
            const controller = new AbortController();
            const timeoutId = setTimeout(() => controller.abort(), 10000); // 10 second timeout

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
                showNotification('Tenant loading timeout - using defaults', 'warning');
            }
            // Continue without tenants - don't block the UI
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

    function onTenantChange(event) {
        const tenantId = event.target.value;
        const meterSelect = document.getElementById('meterFilter');

        if (tenantId) {
            meterSelect.disabled = false;
            loadMeters(tenantId);
        } else {
            meterSelect.disabled = true;
            meterSelect.innerHTML = '<option value="">Select Tenant First</option>';
        }
    }

    // ✅ OPTIMIZATION 3: Fast meter loading with timeout
    async function loadMeters(tenantId) {
        try {
            const controller = new AbortController();
            const timeoutId = setTimeout(() => controller.abort(), 8000); // 8 second timeout

            const response = await fetch(`/Dashboard/GetMetersByTenant/${tenantId}`, {
                signal: controller.signal
            });

            clearTimeout(timeoutId);

            if (!response.ok) {
                throw new Error(`HTTP ${response.status}: ${response.statusText}`);
            }

            const data = await response.json();
            meters = data || [];
            populateMeterDropdown();

        } catch (error) {
            console.error('Error loading meters:', error);
            if (error.name === 'AbortError') {
                showNotification('Meter loading timeout', 'warning');
            }
        }
    }

    function populateMeterDropdown() {
        const meterSelect = document.getElementById('meterFilter');
        meterSelect.innerHTML = '<option value="">All Meters</option>';

        meters.forEach(meter => {
            const option = document.createElement('option');
            option.value = meter.id;
            option.textContent = `${meter.name} (${meter.type})`;
            meterSelect.appendChild(option);
        });
    }

    function onDateFilterChange(event) {
        const filterType = event.target.value;
        const startDate = document.getElementById('startDate');
        const endDate = document.getElementById('endDate');
        const today = new Date();

        switch (filterType) {
            case 'daily':
                startDate.value = formatDate(new Date(today.getTime() - 7 * 24 * 60 * 60 * 1000));
                endDate.value = formatDate(today);
                break;
            case 'monthly':
                startDate.value = formatDate(new Date(today.getFullYear(), today.getMonth() - 11, 1));
                endDate.value = formatDate(today);
                break;
            case 'yearly':
                startDate.value = formatDate(new Date(today.getFullYear() - 5, 0, 1));
                endDate.value = formatDate(today);
                break;
        }
    }

    // ✅ OPTIMIZATION 4: Fast chart loading with timeout and fallback
    async function loadChartData() {
        console.log('🔧 Loading chart data...');
        showLoading(true);

        // ✅ Clear any existing timeout
        if (loadingTimeout) {
            clearTimeout(loadingTimeout);
        }

        // ✅ Set a timeout for chart loading
        loadingTimeout = setTimeout(() => {
            console.warn('⏰ Chart loading timeout - showing demo data');
            showDemoChart();
            showNotification('Chart loading took too long - showing demo data', 'warning');
        }, 15000); // 15 second timeout

        const filters = {
            dateFilter: document.getElementById('dateFilter').value,
            tenantId: document.getElementById('tenantFilter').value,
            meterId: document.getElementById('meterFilter').value,
            startDate: document.getElementById('startDate').value,
            endDate: document.getElementById('endDate').value
        };

        console.log('📋 Filters:', filters);

        try {
            const controller = new AbortController();
            const timeoutId = setTimeout(() => controller.abort(), 12000); // 12 second request timeout

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

            console.log('🌐 Response Status:', response.status);

            if (!response.ok) {
                throw new Error(`HTTP ${response.status}: ${response.statusText}`);
            }

            const data = await response.json();
            console.log('📊 Received data:', data);

            // ✅ Handle demo data notification
            if (data.isDemoData) {
                showNotification(data.message || 'Showing demo data', 'info');
            } else if (data.message) {
                showNotification(data.message, 'info');
            }

            currentData = data.chartData;
            updateChart(data.chartData);
            updateSummaryCards(data.summary);
            showLoading(false);

            console.log('✅ Chart updated successfully');

        } catch (error) {
            clearTimeout(loadingTimeout);
            console.error('❌ Chart loading error:', error);

            if (error.name === 'AbortError') {
                console.warn('⏰ Request timeout - showing demo data');
                showNotification('Request timeout - showing demo data', 'warning');
            } else {
                showNotification(`Error: ${error.message} - showing demo data`, 'error');
            }

            showDemoChart();
        }
    }

    // ✅ OPTIMIZATION 5: Demo chart for better UX
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

    // ✅ OPTIMIZATION 6: Improved chart rendering
    function updateChart(data) {
        console.log('📈 Updating chart with data:', data);

        const canvas = document.getElementById('consumptionChart');
        if (!canvas) {
            console.error('❌ Canvas not found');
            return;
        }

        const ctx = canvas.getContext('2d');
        const chartType = document.getElementById('chartType')?.value || 'bar';

        // Check Chart.js availability
        if (typeof Chart === 'undefined') {
            console.error('❌ Chart.js not loaded');
            showNotification('Chart library not loaded', 'error');
            return;
        }

        // Destroy existing chart
        if (chart) {
            chart.destroy();
        }

        // Validate data
        if (!data || !data.labels || !data.datasets) {
            console.warn('⚠️ Invalid chart data structure');
            showDemoChart();
            return;
        }

        try {
            const processedDatasets = data.datasets.map((dataset, index) => ({
                label: dataset.label,
                data: dataset.data,
                backgroundColor: chartType === 'bar' ? getColor(index, 0.6) : 'transparent',
                borderColor: getColor(index, 1),
                borderWidth: 2,
                tension: 0.1,
                fill: false
            }));

            chart = new Chart(ctx, {
                type: chartType,
                data: {
                    labels: data.labels,
                    datasets: processedDatasets
                },
                options: {
                    responsive: true,
                    maintainAspectRatio: false,
                    plugins: {
                        title: {
                            display: true,
                            text: 'Energy Consumption Over Time'
                        },
                        legend: {
                            display: true,
                            position: 'top'
                        },
                        tooltip: {
                            mode: 'index',
                            intersect: false
                        }
                    },
                    scales: {
                        x: {
                            display: true,
                            title: {
                                display: true,
                                text: 'Date'
                            }
                        },
                        y: {
                            display: true,
                            title: {
                                display: true,
                                text: 'Consumption (kWh)'
                            },
                            beginAtZero: true
                        }
                    }
                }
            });

            console.log('✅ Chart created successfully');

        } catch (chartError) {
            console.error('❌ Chart creation error:', chartError);
            showNotification('Chart rendering failed', 'error');
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
        } catch (error) {
            console.error('Error updating summary cards:', error);
        }
    }

    function resetFilters() {
        document.getElementById('dateFilter').value = 'monthly';
        document.getElementById('tenantFilter').value = '';
        document.getElementById('meterFilter').value = '';
        document.getElementById('meterFilter').disabled = true;
        document.getElementById('chartType').value = 'bar';

        initializeDateFilters();
        loadChartData();
    }

    // ✅ OPTIMIZATION 7: Improved loading states
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

    // ✅ OPTIMIZATION 8: Better notification system
    function showNotification(message, type = 'info') {
        console.log(`${type.toUpperCase()}: ${message}`);

        // Remove existing notifications
        const existingAlerts = document.querySelectorAll('.dashboard-alert');
        existingAlerts.forEach(alert => alert.remove());

        // Create new notification
        const alertDiv = document.createElement('div');
        alertDiv.className = `alert alert-${getBootstrapAlertClass(type)} alert-dismissible fade show dashboard-alert`;
        alertDiv.innerHTML = `
            ${message}
            <button type="button" class="btn-close" data-bs-dismiss="alert" aria-label="Close"></button>
        `;

        // Insert at the top of the container
        const container = document.querySelector('.container-fluid');
        if (container) {
            container.insertBefore(alertDiv, container.firstChild);

            // Auto-remove after delay
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

})();