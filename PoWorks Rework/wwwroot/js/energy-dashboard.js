// Energy Dashboard JavaScript
(function () {
    'use strict';

    // Global variables
    let chart = null;
    let currentData = [];
    let tenants = [];
    let meters = [];

    // Initialize
    document.addEventListener('DOMContentLoaded', function () {
        initializeDateFilters();
        loadTenants();
        attachEventListeners();
        loadChartData();
    });

    // Initialize date filters with default values
    function initializeDateFilters() {
        const endDate = new Date();
        const startDate = new Date();
        startDate.setMonth(startDate.getMonth() - 1);

        document.getElementById('startDate').value = formatDate(startDate);
        document.getElementById('endDate').value = formatDate(endDate);
    }

    // Format date to YYYY-MM-DD
    function formatDate(date) {
        return date.toISOString().split('T')[0];
    }

    // Attach event listeners
    function attachEventListeners() {
        document.getElementById('tenantFilter').addEventListener('change', onTenantChange);
        document.getElementById('applyFilters').addEventListener('click', loadChartData);
        document.getElementById('resetFilters').addEventListener('click', resetFilters);
        document.getElementById('chartType').addEventListener('change', updateChartType);
        document.getElementById('dateFilter').addEventListener('change', onDateFilterChange);
    }

    // Load tenants
    function loadTenants() {
        fetch('/api/DashboardApi/GetTenants')
            .then(response => response.json())
            .then(data => {
                tenants = data;
                populateTenantDropdown();
            })
            .catch(error => {
                console.error('Error loading tenants:', error);
                showNotification('Error loading tenants', 'error');
            });
    }

    // Populate tenant dropdown
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

    // Handle tenant change
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

    // Load meters for selected tenant
    function loadMeters(tenantId) {
        fetch(`/api/DashboardApi/GetMetersByTenant/${tenantId}`)
            .then(response => response.json())
            .then(data => {
                meters = data;
                populateMeterDropdown();
            })
            .catch(error => {
                console.error('Error loading meters:', error);
                showNotification('Error loading meters', 'error');
            });
    }

    // Populate meter dropdown
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

    // Handle date filter change
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

    // Load chart data
    function loadChartData() {
        showLoading(true);

        const filters = {
            dateFilter: document.getElementById('dateFilter').value,
            tenantId: document.getElementById('tenantFilter').value,
            meterId: document.getElementById('meterFilter').value,
            startDate: document.getElementById('startDate').value,
            endDate: document.getElementById('endDate').value
        };

        fetch('/api/DashboardApi/GetConsumptionData', {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json'
            },
            body: JSON.stringify(filters)
        })
            .then(response => response.json())
            .then(data => {
                currentData = data.chartData;
                updateChart(data.chartData);
                updateSummaryCards(data.summary);
                showLoading(false);
            })
            .catch(error => {
                console.error('Error loading chart data:', error);
                showNotification('Error loading chart data', 'error');
                showLoading(false);
            });
    }

    // Update chart
    function updateChart(data) {
        const ctx = document.getElementById('consumptionChart').getContext('2d');
        const chartType = document.getElementById('chartType').value;

        if (chart) {
            chart.destroy();
        }

        chart = new Chart(ctx, {
            type: chartType,
            data: {
                labels: data.labels,
                datasets: data.datasets.map((dataset, index) => ({
                    label: dataset.label,
                    data: dataset.data,
                    backgroundColor: chartType === 'bar' ?
                        getColor(index, 0.6) : 'transparent',
                    borderColor: getColor(index, 1),
                    borderWidth: 2,
                    tension: 0.1,
                    fill: false
                }))
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
    }

    // Update chart type
    function updateChartType() {
        if (currentData && currentData.labels) {
            updateChart(currentData);
        }
    }

    // Get color for chart
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

    // Update summary cards
    function updateSummaryCards(summary) {
        document.getElementById('totalConsumption').textContent = `${summary.totalConsumption.toFixed(2)} kWh`;
        document.getElementById('avgDaily').textContent = `${summary.averageDaily.toFixed(2)} kWh`;
        document.getElementById('peakUsage').textContent = `${summary.peakUsage.toFixed(2)} kWh`;
        document.getElementById('activeMeters').textContent = summary.activeMeters;
    }

    // Reset filters
    function resetFilters() {
        document.getElementById('dateFilter').value = 'monthly';
        document.getElementById('tenantFilter').value = '';
        document.getElementById('meterFilter').value = '';
        document.getElementById('meterFilter').disabled = true;
        document.getElementById('chartType').value = 'bar';

        initializeDateFilters();
        loadChartData();
    }

    // Show/hide loading spinner
    function showLoading(show) {
        const spinner = document.getElementById('loadingSpinner');
        const chart = document.getElementById('consumptionChart');

        if (show) {
            spinner.classList.remove('d-none');
            chart.style.display = 'none';
        } else {
            spinner.classList.add('d-none');
            chart.style.display = 'block';
        }
    }

    // Show notification
    function showNotification(message, type) {
        // Simple alert for now - you can implement a better notification system
        console.log(`${type}: ${message}`);

        // Create a temporary alert div
        const alertDiv = document.createElement('div');
        alertDiv.className = `alert alert-${type === 'error' ? 'danger' : 'success'} alert-dismissible fade show`;
        alertDiv.innerHTML = `
            ${message}
            <button type="button" class="btn-close" data-bs-dismiss="alert" aria-label="Close"></button>
        `;

        // Insert at the top of the container
        const container = document.querySelector('.container-fluid');
        container.insertBefore(alertDiv, container.firstChild);

        // Auto-remove after 5 seconds
        setTimeout(() => {
            alertDiv.remove();
        }, 5000);
    }

})();