// NEW: amCharts 5 Integration - Energy Dashboard
(function () {
    'use strict';

    // Global variables
    let currentData = [];
    let tenants = [];
    let meters = [];
    let autoRefreshInterval = null;

    // AmCharts globals
    let root = null;
    let exporting = null;

    document.addEventListener('DOMContentLoaded', function () {
        console.log('Dashboard initializing with amCharts 5...');

        try {
            attachEventListeners();
            document.getElementById('dateFilter').value = 'daily'; 

            Promise.all([
                loadDateRangeSuggestions(),
                loadTenants()
            ]).then(() => {
                return loadDashboardStats();
            }).then(() => {
                return loadMetersForCurrentDateRange();
            }).then(() => {
                return loadChartData();
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

    async function loadDateRangeSuggestions() {
        try {
            const response = await fetch('/Dashboard/GetDateRangeSuggestions');
            if (!response.ok) throw new Error(`HTTP ${response.status}`);
            const suggestions = await response.json();

            if (suggestions.success) {
                document.getElementById('startDate').value = suggestions.defaultStartDate;
                document.getElementById('endDate').value = suggestions.defaultEndDate;
                updateDataStatus(suggestions.message, 'info');

                if (suggestions.alternatives && suggestions.alternatives.length > 0) {
                    addDateRangeAlternatives(suggestions.alternatives);
                }
            } else {
                initializeDateFilters();
                updateDataStatus('Using default date range', 'warning');
            }
        } catch (error) {
            initializeDateFilters();
            updateDataStatus('Error loading optimal date range, using defaults', 'warning');
        }
    }

    function addDateRangeAlternatives(alternatives) {
        const dateFilter = document.getElementById('dateFilter');
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

    function attachEventListeners() {
        document.getElementById('tenantFilter').addEventListener('change', onTenantChange);
        document.getElementById('applyFilters').addEventListener('click', loadChartData);
        document.getElementById('resetFilters').addEventListener('click', resetFilters);
        document.getElementById('chartType').addEventListener('change', () => loadChartData());

        document.getElementById('dateFilter').addEventListener('change', onDateFilterChange);
        document.getElementById('startDate').addEventListener('change', onDateRangeChange);
        document.getElementById('endDate').addEventListener('change', onDateRangeChange);

        document.getElementById('meterLimit').addEventListener('change', onMeterLimitChange);
        document.getElementById('refreshMeters').addEventListener('click', refreshMeters);

        document.getElementById('autoRefresh').addEventListener('click', toggleAutoRefresh);
        document.getElementById('exportChart').addEventListener('click', exportChart);

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
            if (root) {
                let chartObj = root.container.children.getIndex(0);
                if (chartObj && chartObj.xAxes) {
                    chartObj.xAxes.getIndex(0).zoom(0, 1);
                }
            }
        });

        document.querySelectorAll('input[name="viewMode"]').forEach(radio => {
            radio.addEventListener('change', () => loadChartData());
        });
    }

    async function loadTenants() {
        try {
            const response = await fetch('/Dashboard/GetTenants');
            if (!response.ok) throw new Error(`HTTP ${response.status}`);
            tenants = await response.json() || [];
            populateTenantDropdown();
        } catch (error) {
            console.error('Error loading tenants:', error);
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
        loadMetersForCurrentDateRange();
    }

    function onDateFilterChange(event) {
        const filterType = event.target.value;

        if (filterType.startsWith('custom_')) {
            const option = event.target.selectedOptions[0];
            document.getElementById('startDate').value = option.dataset.startDate;
            document.getElementById('endDate').value = option.dataset.endDate;
            showNotification(`Applied ${option.textContent}`, 'info');
        } else {
            const startDate = document.getElementById('startDate');
            const endDate = document.getElementById('endDate');
            const today = new Date();

            switch (filterType) {
                case 'daily':
                    startDate.value = formatDate(new Date(today.getTime() - 30 * 24 * 60 * 60 * 1000));
                    endDate.value = formatDate(today);
                    break;
                case 'monthly':
                    startDate.value = formatDate(new Date(today.getFullYear(), today.getMonth() - 11, 1));
                    endDate.value = formatDate(today);
                    break;
                case 'yearly':
                    startDate.value = formatDate(new Date(today.getFullYear() - 4, 0, 1));
                    endDate.value = formatDate(today);
                    break;
            }
        }
        onDateRangeChange();
    }

    async function onDateRangeChange() {
        const startDate = new Date(document.getElementById('startDate').value);
        const endDate = new Date(document.getElementById('endDate').value);
        if (startDate >= endDate) {
            showNotification('Start date must be before end date', 'error');
            return;
        }
        try {
            await loadMetersForCurrentDateRange();
            await loadChartData();
        } catch (error) {
            console.error('Error handling date range change:', error);
        }
    }

    async function loadDashboardStats() {
        try {
            const startDate = document.getElementById('startDate').value;
            const endDate = document.getElementById('endDate').value;
            const url = `/Dashboard/GetDashboardStats?startDate=${startDate}&endDate=${endDate}`;
            const response = await fetch(url);
            if (!response.ok) throw new Error(`HTTP ${response.status}`);
            const stats = await response.json();

            if (stats.dateRange && !stats.dateRange.hasDataInRange) {
                updateDataStatus(`No data found in range.`, 'warning');
            } else {
                updateDataStatus(stats.message, stats.hasData ? 'success' : 'warning');
            }
        } catch (error) {
            updateDataStatus('Unable to load dashboard statistics', 'warning');
        }
    }

    async function loadMetersForCurrentDateRange() {
        try {
            const startDate = document.getElementById('startDate').value;
            const endDate = document.getElementById('endDate').value;
            const tenantId = document.getElementById('tenantFilter').value;

            const requestBody = {
                startDate: startDate,
                endDate: endDate,
                tenantId: tenantId || null,
                limit: 1000,
                offset: 0,
                includeNullTenants: true
            };

            const response = await fetch('/Dashboard/GetMetersWithData', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(requestBody)
            });

            if (!response.ok) throw new Error(`HTTP ${response.status}`);
            const data = await response.json();

            if (data.success) {
                meters = data.meters || [];
                populateMeterDropdown();
            }
        } catch (error) {
            console.error('Error loading meters:', error);
            meters = [];
            populateMeterDropdown();
        }
    }

    function populateMeterDropdown() {
        const container = document.getElementById('meterFilterContainer');
        const btnText = document.getElementById('meterDropdownText');
        const previouslyChecked = Array.from(document.querySelectorAll('.meter-checkbox:checked')).map(cb => cb.value);

        container.innerHTML = '';

        if (meters.length === 0) {
            container.innerHTML = '<li><span class="dropdown-item-text text-muted">No meters available</span></li>';
            btnText.textContent = 'No meters available';
            return;
        }

        container.innerHTML = `
            <li class="px-2 pb-2 border-bottom sticky-top bg-white" style="z-index: 1050; margin-top: -8px; padding-top: 8px;">
                <div class="input-group input-group-sm mb-2">
                    <span class="input-group-text bg-light"><i class="bi bi-search"></i></span>
                    <input type="text" id="meterSearchInput" class="form-control" placeholder="Search meters..." autocomplete="off">
                </div>
                <div class="d-flex justify-content-between">
                    <button type="button" class="btn btn-sm btn-link text-decoration-none p-0 fw-bold" id="selectAllMeters">Select All</button>
                    <button type="button" class="btn btn-sm btn-link text-decoration-none p-0 text-danger" id="clearAllMeters">Clear</button>
                </div>
            </li>
        `;

        meters.forEach(meter => {
            const isChecked = previouslyChecked.includes(meter.id.toString()) ? 'checked' : '';
            let label = meter.displayName || `${meter.name} (${meter.type})`;
            if (meter.tenantName) label += ` - ${meter.tenantName}`;

            const li = document.createElement('li');
            li.className = 'dropdown-item-text p-1 meter-item';
            li.innerHTML = `
                <div class="form-check cursor-pointer">
                    <input class="form-check-input meter-checkbox" type="checkbox" value="${meter.id}" id="meterCb_${meter.id}" ${isChecked} style="cursor: pointer;">
                    <label class="form-check-label d-block text-truncate meter-label" for="meterCb_${meter.id}" title="${label}" style="cursor: pointer;">
                        ${label}
                    </label>
                </div>
            `;
            container.appendChild(li);
        });

        updateMeterDropdownText();

        const searchInput = document.getElementById('meterSearchInput');
        searchInput?.addEventListener('click', (e) => e.stopPropagation());
        searchInput?.addEventListener('input', function (e) {
            const searchTerm = e.target.value.toLowerCase();
            document.querySelectorAll('.meter-item').forEach(item => {
                const labelText = item.querySelector('.meter-label').textContent.toLowerCase();
                item.style.display = labelText.includes(searchTerm) ? '' : 'none';
            });
        });

        document.querySelectorAll('.meter-checkbox').forEach(cb => {
            cb.addEventListener('change', () => {
                updateMeterDropdownText();
                loadChartData();
            });
        });

        document.getElementById('selectAllMeters')?.addEventListener('click', () => {
            let changed = false;
            document.querySelectorAll('.meter-item').forEach(item => {
                if (item.style.display !== 'none') {
                    const cb = item.querySelector('.meter-checkbox');
                    if (!cb.checked) { cb.checked = true; changed = true; }
                }
            });
            if (changed) { updateMeterDropdownText(); loadChartData(); }
        });

        document.getElementById('clearAllMeters')?.addEventListener('click', () => {
            let changed = false;
            document.querySelectorAll('.meter-checkbox').forEach(cb => {
                if (cb.checked) { cb.checked = false; changed = true; }
            });
            if (changed) { updateMeterDropdownText(); loadChartData(); }
        });
    }

    function updateMeterDropdownText() {
        const checkedCount = document.querySelectorAll('.meter-checkbox:checked').length;
        const btnText = document.getElementById('meterDropdownText');
        const currentLimit = document.getElementById('meterLimit').value || 5;

        if (checkedCount === 0) {
            btnText.textContent = `Default View (Top ${currentLimit})`;
            btnText.classList.add('text-muted');
        } else if (checkedCount === 1) {
            const label = document.querySelector('.meter-checkbox:checked').nextElementSibling.textContent.trim();
            btnText.textContent = label;
            btnText.classList.remove('text-muted');
        } else {
            btnText.textContent = `${checkedCount} meters selected`;
            btnText.classList.remove('text-muted');
        }
    }

    function onMeterLimitChange(event) {
        document.querySelectorAll('.meter-checkbox').forEach(cb => cb.checked = false);
        if (typeof updateMeterDropdownText === 'function') updateMeterDropdownText();
        loadMetersForCurrentDateRange().then(() => loadChartData());
    }

    async function refreshMeters() {
        await loadMetersForCurrentDateRange();
        showNotification('Meter list refreshed', 'success');
    }

    // ============================================================
    // AMCHARTS 5 RENDERING
    // ============================================================

    // Transform backend labels into MS timestamps for amCharts
    function parseLabelToTs(label) {
        if (typeof label === 'number') return label;
        if (label.length === 4) return new Date(`${label}-01-01T00:00:00`).getTime();
        if (label.length === 7) return new Date(`${label}-01T00:00:00`).getTime();
        if (label.length === 10) return new Date(`${label}T00:00:00`).getTime();
        return new Date(label.replace(' ', 'T') + ':00').getTime();
    }

    // Format the backend data to [{x: timestamp, y: value}] required by our amCharts series
    function toTimeSeriesFormat(chartData) {
        if (!chartData || !chartData.labels) return chartData;
        const points = chartData.labels.map(l => parseLabelToTs(l));
        const datasets = chartData.datasets.map(ds => ({
            label: ds.label,
            data: ds.data.map((v, i) => ({ x: points[i], y: v || 0 }))
        }));
        return { datasets };
    }

    async function loadChartData() {
        console.log('Loading chart data...');
        showLoading(true);

        const dateFilterValue = document.getElementById('dateFilter').value;
        const selectedMeters = Array.from(document.querySelectorAll('.meter-checkbox:checked')).map(cb => parseInt(cb.value));

        const filters = {
            dateFilter: dateFilterValue,
            tenantId: document.getElementById('tenantFilter').value || null,
            meterIds: selectedMeters,
            startDate: document.getElementById('startDate').value,
            endDate: document.getElementById('endDate').value,
            limit: parseInt(document.getElementById('meterLimit').value) || 5,
            isComparisonMode: document.getElementById('modeComparison').checked,
            groupBy: 'meter'
        };

        try {
            const response = await fetch('/Dashboard/GetConsumptionData', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(filters)
            });

            if (!response.ok) throw new Error(`HTTP ${response.status}`);
            const data = await response.json();

            if (data.noDataInRange && data.suggestions) {
                showNotification(data.message, 'warning');
                showDateRangeSuggestions(data.suggestions);
                showLoading(false);
                return;
            }

            if (data.dataInfo) updateDataInfoDisplay(data.dataInfo);

            currentData = toTimeSeriesFormat(data.chartData);
            updateAmChart(currentData);

            if (currentData.datasets) renderTopConsumers(currentData.datasets);
            updateSummaryCards(data.summary);

            showLoading(false);

        } catch (error) {
            console.error('Chart loading error:', error);
            showNotification(`Error: ${error.message}`, 'error');
            showLoading(false);
        }
    }
    let chartRenderToken = 0;

    function updateAmChart(data) {
        const chartdiv = document.getElementById('chartdiv');
        if (!chartdiv) return;

        const myToken = ++chartRenderToken;

        requestAnimationFrame(() => {
            requestAnimationFrame(() => {
                if (myToken !== chartRenderToken) return;
                renderAmChartNow(data, chartdiv);
            });
        });
    }

    function renderAmChartNow(data, chartdiv) {
        if (root) {
            root.dispose();
            root = null;
            exporting = null;
        }

        if (!data || !data.datasets || data.datasets.length === 0) return;

        const chartType = document.getElementById('chartType')?.value || 'line';

        const dateFilter = document.getElementById('dateFilter').value;
        let timeUnit = "day";
        let tooltipFormat = "[bold]{name}[/]\n{valueX.formatDate('yyyy-MM-dd')}: {valueY} kWh";

        if (dateFilter === "daily") {
            timeUnit = "hour";
            tooltipFormat = "[bold]{name}[/]\n{valueX.formatDate('yyyy-MM-dd HH:mm')}: {valueY} kWh";
        } else if (dateFilter === "yearly") {
            timeUnit = "month";
            tooltipFormat = "[bold]{name}[/]\n{valueX.formatDate('MMM yyyy')}: {valueY} kWh";
        }

        const startDateInput = document.getElementById('startDate').value;
        const endDateInput = document.getElementById('endDate').value;
        const startTs = new Date(startDateInput + "T00:00:00").getTime();
        const endTs = new Date(endDateInput + "T23:59:59").getTime();

        root = am5.Root.new("chartdiv");

        if (root._logo) {
            root._logo.dispose();
        }

        root.setThemes([am5themes_Animated.new(root)]);

        let chart = root.container.children.push(am5xy.XYChart.new(root, {
            panX: false,
            panY: false, 
            wheelX: "panX",
            wheelY: "zoomX",
            layout: root.verticalLayout,
            pinchZoomX: true
        }));

        let xAxis = chart.xAxes.push(am5xy.DateAxis.new(root, {
            min: startTs,
            max: endTs,
            strictMinMax: true,
            maxDeviation: 0.2,
            baseInterval: { timeUnit: timeUnit, count: 1 },
            renderer: am5xy.AxisRendererX.new(root, {
                minGridDistance: 60,
                minorGridEnabled: true
            }),
            tooltip: am5.Tooltip.new(root, {})
        }));

        let yAxis = chart.yAxes.push(am5xy.ValueAxis.new(root, {
            renderer: am5xy.AxisRendererY.new(root, {}),
            tooltip: am5.Tooltip.new(root, {
                animationDuration: 150 
            })
        }));

        // 🟢 FIX 1 : LE VRAI CARRÉ DE SÉLECTION CLASSIQUE
        let cursor = chart.set("cursor", am5xy.XYCursor.new(root, {
            behavior: "zoomXY", // Permet de dessiner un vrai rectangle dans toutes les directions !
            xAxis: xAxis,
            yAxis: yAxis
        }));

        // On affiche une belle croix de visée (lignes en pointillé)
        cursor.lineX.setAll({ strokeDasharray: [3, 3] });
        cursor.lineY.setAll({ visible: true, strokeDasharray: [3, 3] });


        cursor.set("maxTooltipDistance", 0);

        const colors = [
            am5.color(0x36A2EB), am5.color(0xFF6384), am5.color(0xFFCE56),
            am5.color(0x4BC0C0), am5.color(0x9966FF), am5.color(0xFF9F40)
        ];

        data.datasets.forEach((ds, index) => {
            let color = colors[index % colors.length];
            let series;

            if (chartType === 'bar') {
                series = chart.series.push(am5xy.ColumnSeries.new(root, {
                    name: ds.label,
                    xAxis: xAxis,
                    yAxis: yAxis,
                    valueYField: "y",
                    valueXField: "x",
                    fill: color,
                    stroke: color,
                    tooltip: am5.Tooltip.new(root, {
                        labelText: tooltipFormat,
                        dy: -5 // Remonte un peu la bulle pour pas la cacher sous la souris
                    })
                }));
            } else {
                series = chart.series.push(am5xy.LineSeries.new(root, {
                    name: ds.label,
                    xAxis: xAxis,
                    yAxis: yAxis,
                    valueYField: "y",
                    valueXField: "x",
                    fill: color,
                    stroke: color,
                    // 🟢 FIX 2 : On redonne la bulle au graphique, le curseur se chargera de l'afficher proprement
                    tooltip: am5.Tooltip.new(root, {
                        labelText: tooltipFormat
                    })
                }));

                series.strokes.template.setAll({ strokeWidth: 3 });

                series.fills.template.setAll({
                    visible: true,
                    fillOpacity: 0.2
                });

                series.bullets.push(function () {
                    return am5.Bullet.new(root, {
                        sprite: am5.Circle.new(root, {
                            radius: 4,
                            fill: color,
                            stroke: root.interfaceColors.get("background"),
                            strokeWidth: 2
                        })
                    });
                });
            }

            series.data.setAll(ds.data);
        });

        
        let legend = chart.children.push(am5.Legend.new(root, {
            centerX: am5.p50,
            x: am5.p50,
            paddingTop: 15,
            useDefaultMarker: true
        }));
        legend.data.setAll(chart.series.values);

        let scrollbarX = am5xy.XYChartScrollbar.new(root, {
            orientation: "horizontal",
            height: 50
        });
        chart.set("scrollbarX", scrollbarX);

        let sbxAxis = scrollbarX.chart.xAxes.push(am5xy.DateAxis.new(root, {
            baseInterval: { timeUnit: timeUnit, count: 1 },
            renderer: am5xy.AxisRendererX.new(root, {
                opposite: false,
                strokeOpacity: 0
            })
        }));

        let sbyAxis = scrollbarX.chart.yAxes.push(am5xy.ValueAxis.new(root, {
            renderer: am5xy.AxisRendererY.new(root, {})
        }));

        if (data.datasets.length > 0) {
            let sbseries = scrollbarX.chart.series.push(am5xy.LineSeries.new(root, {
                xAxis: sbxAxis,
                yAxis: sbyAxis,
                valueYField: "y",
                valueXField: "x"
            }));
            let dsData = data.datasets[0].data;
            sbseries.fills.template.setAll({ visible: true, fillOpacity: 0.2 });
            sbseries.data.setAll(dsData);
        }

        exporting = am5plugins_exporting.Exporting.new(root, {
            menu: null,
            dataSource: chart
        });

        chart.appear(1000, 100);
    }

    function showDemoChart() {
        // Fallback removed for brevity - rely on true data flow now.
        showLoading(false);
    }

    // ============================================================
    // MISCELLANEOUS UI UPDATES
    // ============================================================
    function updateDataInfoDisplay(dataInfo) {
        const activeMetersDetail = document.getElementById('activeMetersDetail');
        if (activeMetersDetail && dataInfo.availableMeters) {
            activeMetersDetail.textContent = `Out of ${dataInfo.availableMeters} available`;
        }

        const statusMessage = `Showing ${dataInfo.shownMeters} of ${dataInfo.availableMeters} meters (${dataInfo.metersWithTenants} with tenants, ${dataInfo.metersWithoutTenants} without)`;
        updateDataStatus(statusMessage, 'success');
    }

    function updateDataStatus(message, type = 'info') {
        const statusDiv = document.getElementById('dataStatus');
        const statusText = document.getElementById('dataStatusText');

        if (statusDiv && statusText) {
            statusText.textContent = message;
            statusDiv.className = `alert alert-${type === 'error' ? 'danger' : type}`;
            statusDiv.style.display = 'block';
            statusDiv.style.opacity = '1';
            statusDiv.style.transition = 'opacity 0.5s ease'; 

            if (type === 'success') {
                setTimeout(() => {

                    statusDiv.style.opacity = '0';
                }, 4000);
            }
        }
    }


    function updateSummaryCards(summary) {
        if (!summary) return;
        document.getElementById('totalConsumption').textContent = `${summary.totalConsumption.toFixed(2)} kWh`;
        document.getElementById('avgDaily').textContent = `${summary.averageDaily.toFixed(2)} kWh`;
        document.getElementById('peakUsage').textContent = `${summary.peakUsage.toFixed(2)} kWh`;
        document.getElementById('activeMeters').textContent = summary.activeMeters;
        const totalDetail = document.getElementById('totalConsumptionDetail');
        if (totalDetail && summary.totalMeters) {
            totalDetail.textContent = `From ${summary.activeMeters} of ${summary.totalMeters} meters`;
        }
    }

    function resetFilters() {
        document.getElementById('dateFilter').value = 'monthly';
        document.getElementById('tenantFilter').value = '';
        document.querySelectorAll('.meter-checkbox').forEach(cb => cb.checked = false);
        if (typeof updateMeterDropdownText === 'function') updateMeterDropdownText();
        document.getElementById('meterLimit').value = '5';
        document.getElementById('chartType').value = 'line';

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
            autoRefreshInterval = setInterval(() => loadChartData(), 30000);
            button.classList.add('active');
            button.title = 'Disable Auto Refresh';
            showNotification('Auto refresh enabled (30s)', 'success');
        }
    }

    // Connect Export button to amCharts 5
    function exportChart() {
        if (exporting) {
            exporting.download("png");
            showNotification('Chart exported successfully', 'success');
        } else {
            showNotification('No chart available to export', 'warning');
        }
    }

    function showLoading(show) {
        const spinner = document.getElementById('loadingSpinner');
        const chartCanvas = document.getElementById('chartdiv');
        if (show) {
            if (spinner) spinner.classList.remove('d-none');
            if (chartCanvas) chartCanvas.style.opacity = '0.5';
        } else {
            if (spinner) spinner.classList.add('d-none');
            if (chartCanvas) chartCanvas.style.opacity = '1';
        }
    }

    function showNotification(message, type = 'info') {
        const existingAlerts = document.querySelectorAll('.dashboard-alert');
        existingAlerts.forEach(alert => alert.remove());
        const alertDiv = document.createElement('div');
        alertDiv.className = `alert alert-${type === 'error' ? 'danger' : type} alert-dismissible fade show dashboard-alert`;
        alertDiv.innerHTML = `${message}<button type="button" class="btn-close" data-bs-dismiss="alert"></button>`;
        const container = document.querySelector('.container-fluid');
        if (container) {
            container.insertBefore(alertDiv, container.firstChild);
            setTimeout(() => { if (alertDiv.parentNode) alertDiv.remove(); }, 5000);
        }
    }

    window.switchTab = function (filterValue, activeBtnId) {
        document.getElementById('tabDaily').classList.remove('active');
        document.getElementById('tabMonthly').classList.remove('active');
        document.getElementById('tabYearly').classList.remove('active');
        document.getElementById(activeBtnId).classList.add('active');

        const dateFilter = document.getElementById('dateFilter');
        if (dateFilter) {
            dateFilter.value = filterValue;
            dateFilter.dispatchEvent(new Event('change'));
        }
    };

    function renderTopConsumers(datasets) {
        const container = document.getElementById('topConsumersList');
        const titleElement = document.getElementById('topConsumersTitle');
        if (!container) return;

        const currentLimit = parseInt(document.getElementById('meterLimit').value) || 5;
        if (titleElement) titleElement.innerHTML = `<i class="bi bi-fire"></i> Top ${currentLimit} Meters`;

        if (!datasets || datasets.length <= 1) {
            container.innerHTML = `<div class="alert alert-light text-center border mt-2">Select <strong>All Meters</strong> to see ranking.</div>`;
            return;
        }

        const totals = datasets.map(ds => ({ name: ds.label, total: ds.data.reduce((a, b) => a + ((b && b.y) || 0), 0) }));
        totals.sort((a, b) => b.total - a.total);
        const topList = totals.slice(0, currentLimit);
        const maxTotal = topList[0].total > 0 ? topList[0].total : 1;

        const listHtml = topList.map(item => {
            const percent = (item.total / maxTotal) * 100;
            return `
                <div class="mb-3">
                    <div class="d-flex justify-content-between mb-1">
                        <span class="fw-bold text-secondary text-truncate" style="max-width: 70%;">${item.name}</span>
                        <span class="fw-bold text-nowrap">${item.total.toFixed(2)} kWh</span>
                    </div>
                    <div class="progress" style="height: 8px;">
                        <div class="progress-bar bg-danger" style="width: ${percent}%"></div>
                    </div>
                </div>
            `;
        }).join('');

        container.innerHTML = `<div style="max-height: 250px; overflow-y: auto; padding-right: 5px;">${listHtml}</div>`;
    }

    function toggleFullscreen() {
        const chartCard = document.getElementById('chartdiv').closest('.card');
        const icon = document.querySelector('#fullscreenChart i');
        if (chartCard.classList.contains('chart-fullscreen')) {
            chartCard.classList.remove('chart-fullscreen');
            icon.classList.remove('bi-fullscreen-exit');
            icon.classList.add('bi-arrows-fullscreen');
            document.body.style.overflow = 'auto';
        } else {
            chartCard.classList.add('chart-fullscreen');
            icon.classList.remove('bi-arrows-fullscreen');
            icon.classList.add('bi-fullscreen-exit');
            document.body.style.overflow = 'hidden';
        }
    }

})();