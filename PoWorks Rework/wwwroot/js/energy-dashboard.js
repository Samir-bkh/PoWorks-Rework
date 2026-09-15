// amCharts 5 Integration - Energy Dashboard
// v4: added "max curves displayed" cap with automatic "Others" aggregation.
// Comparison mode (period-vs-period) and meter grouping are next steps, not yet implemented here.
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
        console.log('Dashboard v5 initializing with amCharts 5...');

        try {
            attachEventListeners();
            const initialDateFilter = document.getElementById('dateFilter');
            if (initialDateFilter) initialDateFilter.value = 'daily';

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
        document.getElementById('tenantFilter')?.addEventListener('change', onTenantChange);
        document.getElementById('applyFilters')?.addEventListener('click', loadChartData);
        document.getElementById('resetFilters')?.addEventListener('click', resetFilters);
        document.getElementById('chartType')?.addEventListener('change', () => loadChartData());

        document.getElementById('dateFilter')?.addEventListener('change', onDateFilterChange);
        document.getElementById('startDate')?.addEventListener('change', onDateRangeChange);
        document.getElementById('endDate')?.addEventListener('change', onDateRangeChange);

        document.getElementById('meterLimit')?.addEventListener('change', onMeterLimitChange);
        document.getElementById('refreshMeters')?.addEventListener('click', refreshMeters);

        // NEW: "Max curves displayed" selector - just reloads/re-renders with the new cap
        document.getElementById('maxCurves')?.addEventListener('change', () => loadChartData());

        document.getElementById('autoRefresh')?.addEventListener('click', toggleAutoRefresh);
        document.getElementById('exportChart')?.addEventListener('click', exportChart);

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
            radio.addEventListener('change', () => {
                toggleComparisonOptions();
                loadChartData();
            });
        });

        // NEW: comparison mode controls
        document.getElementById('comparePreset')?.addEventListener('change', () => {
            const customDates = document.getElementById('compareCustomDates');
            const isCustom = document.getElementById('comparePreset').value === 'custom';
            customDates?.classList.toggle('d-none', !isCustom);
            if (!isCustom) loadChartData();
        });
        document.getElementById('compareStartDate')?.addEventListener('change', () => loadChartData());
        document.getElementById('compareEndDate')?.addEventListener('change', () => loadChartData());
    }

    // Shows/hides the "Comparer à" row depending on Standard vs Comparison mode
    function toggleComparisonOptions() {
        const isComparison = document.getElementById('modeComparison').checked;
        document.getElementById('comparisonOptions')?.classList.toggle('d-none', !isComparison);
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

        const points = chartData.labels.map(label => parseLabelToTs(label));
        const datasets = (chartData.datasets || []).map(ds => {
            const unit = ds.unit || 'unit';
            const meterName = ds.meterName || ds.label || 'Meter';
            const tenantName = ds.tenantName || 'Unassigned';

            return {
                label: ds.label,
                meterName,
                tenantName,
                unit,
                data: (ds.data || []).map((value, index) => ({
                    x: points[index],
                    y: Number(value) || 0,
                    meterName,
                    tenantName,
                    unit,
                    seriesLabel: ds.label,
                    periodLabel: 'Current period'
                }))
            };
        });

        return { datasets };
    }

    // ------------------------------------------------------------
    // NEW: Curve limit    // ------------------------------------------------------------
    // NEW: Curve limit + automatic "Others" aggregation
    // ------------------------------------------------------------
    // If more meters are selected than the configured max, we don't block
    // with an error: we keep the top consumers (by total consumption) and
    // sum everything else into a single grey "Autres" series, so the chart
    // always stays readable no matter how many meters exist.
    function applyCurveLimit(data) {
        const warningDiv = document.getElementById('curveLimitWarning');
        const warningText = document.getElementById('curveLimitWarningText');
        const maxCurves = parseInt(document.getElementById('maxCurves')?.value) || 10;

        if (!data || !data.datasets || data.datasets.length <= maxCurves) {
            warningDiv?.classList.add('d-none');
            return data;
        }

        const withTotals = data.datasets.map(ds => ({
            ds,
            total: ds.data.reduce((sum, point) => sum + ((point && point.y) || 0), 0)
        })).sort((a, b) => b.total - a.total);

        const keptSlots = Math.max(1, maxCurves - 1);
        const kept = withTotals.slice(0, keptSlots).map(x => x.ds);
        const rest = withTotals.slice(keptSlots);
        const restUnits = [...new Set(rest.map(x => (x.ds.unit || 'unit').toLowerCase()))];
        const othersUnit = restUnits.length === 1 ? rest[0].ds.unit : 'mixed';

        const othersMap = new Map();
        rest.forEach(({ ds }) => {
            ds.data.forEach(point => {
                if (!point) return;
                othersMap.set(point.x, (othersMap.get(point.x) || 0) + (point.y || 0));
            });
        });

        const othersData = Array.from(othersMap.entries())
            .sort((a, b) => a[0] - b[0])
            .map(([x, y]) => ({
                x,
                y,
                meterName: 'Other meters',
                tenantName: 'Multiple tenants',
                unit: othersUnit,
                seriesLabel: `Others (${rest.length} meters)`,
                periodLabel: 'Current period'
            }));

        const othersDataset = {
            label: `Others (${rest.length} meter${rest.length > 1 ? 's' : ''})`,
            meterName: 'Other meters',
            tenantName: 'Multiple tenants',
            unit: othersUnit,
            data: othersData,
            isOthers: true
        };

        if (warningDiv && warningText) {
            warningText.textContent =
                `Displaying the ${keptSlots} largest consumers. ${rest.length} additional meter(s) are grouped into “Others” to keep the chart readable.`;
            warningDiv.classList.remove('d-none');
        }

        return { datasets: [...kept, othersDataset] };
    }

    // ------------------------------------------------------------
    // Comparing meters    // ------------------------------------------------------------
    // Comparing meters to each other is already what Standard mode does
    // (several meters overlaid). "Comparison" here means: same meter(s),
    // two different time periods overlaid (e.g. this month vs last month).
    function computeCompareRange(startDateStr, endDateStr) {
        const preset = document.getElementById('comparePreset')?.value || 'previous';
        const start = new Date(startDateStr + 'T00:00:00');
        const end = new Date(endDateStr + 'T00:00:00');
        const durationMs = end.getTime() - start.getTime();

        if (preset === 'custom') {
            const cs = document.getElementById('compareStartDate')?.value;
            const ce = document.getElementById('compareEndDate')?.value;
            if (!cs || !ce) return null;
            return { start: cs, end: ce };
        }

        if (preset === 'lastYear') {
            const cs = new Date(start); cs.setFullYear(cs.getFullYear() - 1);
            const ce = new Date(end); ce.setFullYear(ce.getFullYear() - 1);
            return { start: formatDate(cs), end: formatDate(ce) };
        }

        // 'previous' (default): same duration, immediately preceding the current period
        const ce = new Date(start.getTime() - 24 * 60 * 60 * 1000);
        const cs = new Date(ce.getTime() - durationMs);
        return { start: formatDate(cs), end: formatDate(ce) };
    }

    // Pairs each kept current-period meter with its comparison-period counterpart
    // (matched by label, since both queries use the same meter selection), and
    // shifts the comparison timestamps so both periods overlap on the same x-axis.
    // Note: the "Autres" aggregate (from applyCurveLimit) is not paired - it only
    // reflects the current period, since the grouping of "the rest" could differ
    // between two periods and isn't meaningful to compare directly.
    function buildComparisonPairs(currentFormatted, compareChartDataRaw, currentStartDateStr, compareStartDateStr) {
        const compareFormatted = toTimeSeriesFormat(compareChartDataRaw);
        const currentStartTs = new Date(currentStartDateStr + 'T00:00:00').getTime();
        const compareStartTs = new Date(compareStartDateStr + 'T00:00:00').getTime();
        const shiftMs = currentStartTs - compareStartTs;

        const pairs = [];
        currentFormatted.datasets.forEach(curDs => {
            if (curDs.isOthers) {
                pairs.push(curDs);
                return;
            }

            pairs.push({
                ...curDs,
                label: `${curDs.label} · Current`,
                pairKey: curDs.label,
                data: curDs.data.map(point => ({
                    ...point,
                    periodLabel: 'Current period'
                }))
            });

            const compareDs = compareFormatted.datasets?.find(ds => ds.label === curDs.label);
            if (compareDs) {
                pairs.push({
                    ...compareDs,
                    label: `${curDs.label} · Comparison`,
                    pairKey: curDs.label,
                    isCompare: true,
                    data: compareDs.data.map(point => ({
                        ...point,
                        x: point.x + shiftMs,
                        periodLabel: 'Comparison period'
                    }))
                });
            }
        });

        return { datasets: pairs };
    }

    async function loadChartData() {
        showLoading(true);
        setChartEmpty(false);

        const dateFilterValue = document.getElementById('dateFilter').value;
        const selectedMeters = Array.from(document.querySelectorAll('.meter-checkbox:checked'))
            .map(cb => parseInt(cb.value, 10))
            .filter(Number.isFinite);
        const isComparisonActive = document.getElementById('modeComparison').checked;

        const filters = {
            dateFilter: dateFilterValue,
            tenantId: document.getElementById('tenantFilter').value || null,
            meterIds: selectedMeters,
            startDate: document.getElementById('startDate').value,
            endDate: document.getElementById('endDate').value,
            limit: parseInt(document.getElementById('meterLimit').value, 10) || 5,
            isComparisonMode: false,
            groupBy: 'meter'
        };

        let compareRange = null;
        if (isComparisonActive) {
            compareRange = computeCompareRange(filters.startDate, filters.endDate);
            if (compareRange) {
                filters.compareStartDate = compareRange.start;
                filters.compareEndDate = compareRange.end;
            } else {
                showNotification('Select a valid comparison period', 'warning');
            }
        }

        try {
            const response = await fetch('/Dashboard/GetConsumptionData', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(filters)
            });

            if (!response.ok) throw new Error(`HTTP ${response.status}`);
            const data = await response.json();

            if (data.noDataInRange) {
                currentData = { datasets: [] };
                updateAmChart(currentData);
                updateSummaryCards(data.summary || {});
                updateDashboardContext(data.summary || {}, filters);
                renderTopConsumers([]);
                setChartEmpty(true);
                if (data.message) showNotification(data.message, 'warning');
                showLoading(false);
                return;
            }

            if (data.dataInfo) updateDataInfoDisplay(data.dataInfo);

            let currentFormatted = toTimeSeriesFormat(data.chartData);
            currentFormatted = applyCurveLimit(currentFormatted);
            const rankingData = currentFormatted.datasets || [];

            let formatted = currentFormatted;
            if (isComparisonActive && compareRange && data.compareChartData) {
                formatted = buildComparisonPairs(
                    currentFormatted,
                    data.compareChartData,
                    filters.startDate,
                    compareRange.start);
            } else if (isComparisonActive && compareRange && !data.compareChartData) {
                showNotification('No data found for the comparison period', 'warning');
            }

            currentData = formatted;
            updateAmChart(currentData);
            renderTopConsumers(rankingData);
            updateSummaryCards(data.summary);
            updateDashboardContext(data.summary, filters);
            setChartEmpty(!currentData.datasets || currentData.datasets.length === 0);
            showLoading(false);
        } catch (error) {
            console.error('Chart loading error:', error);
            setChartEmpty(true);
            showNotification(`Unable to load dashboard data: ${error.message}`, 'error');
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

        if (!data || !data.datasets || data.datasets.length === 0) {
            chartdiv.innerHTML = '';
            return;
        }

        const chartType = document.getElementById('chartType')?.value || 'area';
        const dateFilter = document.getElementById('dateFilter')?.value || 'daily';
        const units = [...new Set(
            data.datasets
                .filter(ds => !ds.isCompare)
                .map(ds => (ds.unit || 'unit').trim())
                .filter(Boolean)
        )];
        const axisUnit = units.length === 1 ? units[0] : 'mixed units';

        const timeUnit =
            dateFilter === 'yearly' ? 'year' :
            dateFilter === 'monthly' ? 'month' :
            'day';

        const dateFormat =
            dateFilter === 'yearly' ? 'yyyy' :
            dateFilter === 'monthly' ? 'MMM yyyy' :
            'dd MMM yyyy';

        const startDateInput = document.getElementById('startDate').value;
        const endDateInput = document.getElementById('endDate').value;
        const startTs = new Date(startDateInput + 'T00:00:00').getTime();
        const endTs = new Date(endDateInput + 'T23:59:59').getTime();

        root = am5.Root.new('chartdiv');
        if (root._logo) root._logo.dispose();
        root.setThemes([am5themes_Animated.new(root)]);

        const chart = root.container.children.push(am5xy.XYChart.new(root, {
            panX: true,
            panY: false,
            wheelX: 'panX',
            wheelY: 'zoomX',
            pinchZoomX: true,
            layout: root.verticalLayout,
            paddingTop: 8,
            paddingRight: 10
        }));

        const xRenderer = am5xy.AxisRendererX.new(root, {
            minGridDistance: 62,
            minorGridEnabled: false
        });
        xRenderer.grid.template.setAll({
            stroke: am5.color(0xCBD5E1),
            strokeOpacity: .28
        });
        xRenderer.labels.template.setAll({
            fill: am5.color(0x64748B),
            fontSize: 12,
            paddingTop: 9
        });

        const xAxis = chart.xAxes.push(am5xy.DateAxis.new(root, {
            min: startTs,
            max: endTs,
            strictMinMax: true,
            maxDeviation: .1,
            baseInterval: { timeUnit, count: 1 },
            renderer: xRenderer
        }));

        const yRenderer = am5xy.AxisRendererY.new(root, {
            minGridDistance: 42
        });
        yRenderer.grid.template.setAll({
            stroke: am5.color(0xCBD5E1),
            strokeOpacity: .32
        });
        yRenderer.labels.template.setAll({
            fill: am5.color(0x64748B),
            fontSize: 12,
            paddingRight: 8
        });

        const yAxis = chart.yAxes.push(am5xy.ValueAxis.new(root, {
            min: 0,
            renderer: yRenderer,
            numberFormat: '#,###.##'
        }));

        yAxis.children.unshift(am5.Label.new(root, {
            text: `Consumption (${axisUnit})`,
            rotation: -90,
            y: am5.p50,
            centerX: am5.p50,
            fill: am5.color(0x475569),
            fontSize: 13,
            fontWeight: '600',
            paddingBottom: 12
        }));

        const cursor = chart.set('cursor', am5xy.XYCursor.new(root, {
            behavior: 'zoomX',
            xAxis,
            yAxis
        }));
        cursor.lineX.setAll({
            stroke: am5.color(0x64748B),
            strokeOpacity: .65,
            strokeDasharray: [4, 4]
        });
        cursor.lineY.setAll({
            stroke: am5.color(0x94A3B8),
            strokeOpacity: .35,
            strokeDasharray: [3, 3]
        });
        // Tooltips are deliberately bound to rendered data elements below.
        // The cursor remains available for crosshair/zoom, but must not trigger
        // a tooltip just because the pointer is somewhere in the plot area.
        const bindHoverTooltip = (target, tooltip) => {
            target.setAll({
                tooltip,
                tooltipPosition: 'pointer',
                interactive: true
            });
        };

        const colors = [
            am5.color(0x2563EB), am5.color(0x0EA5E9), am5.color(0x10B981),
            am5.color(0x8B5CF6), am5.color(0xF59E0B), am5.color(0xEF4444),
            am5.color(0x14B8A6), am5.color(0x6366F1)
        ];
        const othersColor = am5.color(0x94A3B8);
        const colorForKey = new Map();
        let nextColorIndex = 0;

        data.datasets.forEach(ds => {
            if (ds.isOthers) return;
            const key = ds.pairKey || ds.label;
            if (!colorForKey.has(key)) {
                colorForKey.set(key, colors[nextColorIndex % colors.length]);
                nextColorIndex++;
            }
        });

        data.datasets.forEach(ds => {
            const color = ds.isOthers
                ? othersColor
                : colorForKey.get(ds.pairKey || ds.label);

            const tooltip = am5.Tooltip.new(root, {
                getFillFromSprite: false,
                getStrokeFromSprite: false,
                autoTextColor: false,
                centerX: am5.p50,
                dy: -10
            });
            tooltip.get('background').setAll({
                fill: am5.color(0xFFFFFF),
                fillOpacity: 1,
                stroke: am5.color(0xCBD5E1),
                strokeOpacity: 1,
                cornerRadius: 8,
                shadowColor: am5.color(0x0F172A),
                shadowBlur: 12,
                shadowOffsetY: 4,
                shadowOpacity: .12
            });
            tooltip.label.setAll({
                fill: am5.color(0x0F172A),
                fontSize: 12,
                lineHeight: 17,
                maxWidth: 220,
                oversizedBehavior: 'wrap',
                paddingTop: 8,
                paddingRight: 10,
                paddingBottom: 8,
                paddingLeft: 10
            });
            tooltip.label.set('text',
                `[bold]{meterName}[/]\n` +
                `{valueX.formatDate('${dateFormat}')} · [bold]{valueY.formatNumber('#,###.00')} {unit}[/]\n` +
                `Tenant: {tenantName} · {periodLabel}`);

            let series;
            if (chartType === 'bar') {
                series = chart.series.push(am5xy.ColumnSeries.new(root, {
                    name: ds.label,
                    xAxis,
                    yAxis,
                    valueYField: 'y',
                    valueXField: 'x',
                    fill: color,
                    stroke: color
                }));
                series.columns.template.setAll({
                    cornerRadiusTL: 5,
                    cornerRadiusTR: 5,
                    width: am5.percent(72),
                    fillOpacity: ds.isCompare ? .35 : (ds.isOthers ? .48 : .82),
                    strokeOpacity: .9
                });
                bindHoverTooltip(series.columns.template, tooltip);
            } else {
                series = chart.series.push(am5xy.LineSeries.new(root, {
                    name: ds.label,
                    xAxis,
                    yAxis,
                    valueYField: 'y',
                    valueXField: 'x',
                    fill: color,
                    stroke: color,
                    connect: false,
                    minBulletDistance: 18
                }));

                let dashArray;
                if (ds.isOthers) dashArray = [8, 5];
                else if (ds.isCompare) dashArray = [4, 4];

                series.strokes.template.setAll({
                    strokeWidth: ds.isCompare ? 2 : 2.5,
                    strokeDasharray: dashArray,
                    strokeLinecap: 'round',
                    strokeLinejoin: 'round'
                });

                series.fills.template.setAll({
                    visible: chartType === 'area',
                    fillOpacity: ds.isOthers ? .05 : (ds.isCompare ? .03 : .12)
                });

                series.bullets.push(() => {
                    const marker = am5.Circle.new(root, {
                        radius: ds.isCompare ? 3 : 4.5,
                        fill: color,
                        stroke: am5.color(0xFFFFFF),
                        strokeWidth: 2
                    });
                    bindHoverTooltip(marker, tooltip);
                    return am5.Bullet.new(root, { sprite: marker });
                });
            }

            series.data.setAll(ds.data);
        });

        const legend = chart.children.push(am5.Legend.new(root, {
            centerX: am5.p50,
            x: am5.p50,
            paddingTop: 16,
            useDefaultMarker: true,
            layout: root.horizontalLayout
        }));
        legend.labels.template.setAll({
            fill: am5.color(0x475569),
            fontSize: 12,
            maxWidth: 260,
            oversizedBehavior: 'truncate'
        });
        legend.data.setAll(chart.series.values);

        const scrollbarX = am5xy.XYChartScrollbar.new(root, {
            orientation: 'horizontal',
            height: 46,
            marginTop: 8
        });
        chart.set('scrollbarX', scrollbarX);

        const sbxAxis = scrollbarX.chart.xAxes.push(am5xy.DateAxis.new(root, {
            baseInterval: { timeUnit, count: 1 },
            renderer: am5xy.AxisRendererX.new(root, {
                strokeOpacity: 0,
                minGridDistance: 80
            })
        }));
        const sbyAxis = scrollbarX.chart.yAxes.push(am5xy.ValueAxis.new(root, {
            renderer: am5xy.AxisRendererY.new(root, {})
        }));

        const firstSeries = data.datasets.find(ds => !ds.isCompare) || data.datasets[0];
        if (firstSeries) {
            const preview = scrollbarX.chart.series.push(am5xy.LineSeries.new(root, {
                xAxis: sbxAxis,
                yAxis: sbyAxis,
                valueYField: 'y',
                valueXField: 'x',
                stroke: am5.color(0x64748B),
                fill: am5.color(0xCBD5E1)
            }));
            preview.strokes.template.setAll({ strokeWidth: 1.5 });
            preview.fills.template.setAll({ visible: true, fillOpacity: .22 });
            preview.data.setAll(firstSeries.data);
        }

        exporting = am5plugins_exporting.Exporting.new(root, {
            menu: null,
            dataSource: chart
        });

        chart.appear(650, 50);
    }

    function showDemoChart() {
        showLoading(false);
        setChartEmpty(true);
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


    function formatMetric(value, unit, suffix = '') {
        const numeric = Number(value) || 0;
        const formatter = new Intl.NumberFormat(undefined, {
            maximumFractionDigits: 2,
            minimumFractionDigits: numeric !== 0 && Math.abs(numeric) < 100 ? 2 : 0
        });
        return `${formatter.format(numeric)} ${unit || ''}${suffix}`.trim();
    }

    function updateSummaryCards(summary) {
        if (!summary) return;

        const mixed = summary.hasMixedUnits === true;
        const unit = summary.unit && summary.unit !== 'mixed' ? summary.unit : '';
        const total = document.getElementById('totalConsumption');
        const average = document.getElementById('avgDaily');
        const peak = document.getElementById('peakUsage');
        const active = document.getElementById('activeMeters');

        if (mixed) {
            if (total) total.textContent = 'Multiple units';
            if (average) average.textContent = '—';
            if (peak) peak.textContent = '—';
            document.getElementById('totalConsumptionDetail').textContent =
                'Select meters with the same unit for aggregate KPIs';
            document.getElementById('avgDailyDetail').textContent =
                'Daily averages cannot combine incompatible units';
            document.getElementById('peakUsageDetail').textContent =
                'Peak totals cannot combine incompatible units';
        } else {
            if (total) total.textContent = formatMetric(summary.totalConsumption, unit);
            if (average) average.textContent = formatMetric(summary.averageDaily, unit, '/day');
            if (peak) peak.textContent = formatMetric(summary.peakUsage, unit);

            const meterText = summary.totalMeters
                ? `${summary.activeMeters} of ${summary.totalMeters} available meter(s)`
                : `${summary.activeMeters} contributing meter(s)`;
            document.getElementById('totalConsumptionDetail').textContent = meterText;
            document.getElementById('avgDailyDetail').textContent =
                `Across all ${summary.periodDays || 1} calendar day(s) in the selected range`;
            document.getElementById('peakUsageDetail').textContent =
                summary.peakPeriodLabel || 'Highest displayed period total';
        }

        if (active) active.textContent = summary.activeMeters ?? 0;
        const activeDetail = document.getElementById('activeMetersDetail');
        if (activeDetail && !activeDetail.textContent) {
            activeDetail.textContent = 'Meters contributing to this view';
        }

        const unitLabel = mixed ? 'Mixed units' : (unit || '—');
        const badge = document.querySelector('#chartUnitBadge span');
        if (badge) badge.textContent = unitLabel;
        const unitContext = document.getElementById('dashboardUnitContext');
        if (unitContext) unitContext.textContent = unitLabel;
    }

    function updateDashboardContext(summary, filters) {
        const tenantSelect = document.getElementById('tenantFilter');
        const tenantContext = document.getElementById('dashboardTenantContext');
        if (tenantContext) {
            tenantContext.textContent =
                tenantSelect?.selectedOptions?.[0]?.textContent?.trim() || 'All tenants';
        }

        const meterContext = document.getElementById('dashboardMeterContext');
        const selectedCount = document.querySelectorAll('.meter-checkbox:checked').length;
        if (meterContext) {
            meterContext.textContent = selectedCount > 0
                ? `${selectedCount} selected`
                : `Top ${filters.limit || 5} by activity`;
        }

        const periodContext = document.getElementById('dashboardPeriodContext');
        if (periodContext) {
            periodContext.textContent = filters.startDate && filters.endDate
                ? `${filters.startDate} → ${filters.endDate}`
                : 'Default range';
        }

        const rankingScope = document.getElementById('rankingScopeBadge');
        if (rankingScope) {
            rankingScope.textContent = tenantSelect?.value
                ? tenantSelect.selectedOptions[0].textContent.trim()
                : 'Current workspace';
        }
    }

    function setChartEmpty(show) {
        document.getElementById('chartEmptyState')?.classList.toggle('d-none', !show);
    }

    function resetFilters() {
        document.getElementById('dateFilter').value = 'monthly';
        document.getElementById('tenantFilter').value = '';
        document.querySelectorAll('.meter-checkbox').forEach(cb => cb.checked = false);
        if (typeof updateMeterDropdownText === 'function') updateMeterDropdownText();
        document.getElementById('meterLimit').value = '5';
        document.getElementById('chartType').value = 'area';
        if (document.getElementById('maxCurves')) document.getElementById('maxCurves').value = '10';

        loadDateRangeSuggestions().then(() => {
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
        if (spinner) spinner.classList.toggle('d-none', !show);
    }

    function showNotification(message, type = 'info') {
        const existingAlerts = document.querySelectorAll('.dashboard-alert');
        existingAlerts.forEach(alert => alert.remove());
        const alertDiv = document.createElement('div');
        alertDiv.className = `alert alert-${type === 'error' ? 'danger' : type} alert-dismissible fade show dashboard-alert`;
        alertDiv.innerHTML = `${message}<button type="button" class="btn-close" data-bs-dismiss="alert"></button>`;
        const container = document.querySelector('.dashboard-shell');
        if (container) {
            container.insertBefore(alertDiv, container.firstChild);
            setTimeout(() => { if (alertDiv.parentNode) alertDiv.remove(); }, 5000);
        }
    }

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
                ${(suggestions.alternatives || []).map(alt =>
            `<button type="button" class="btn btn-outline-secondary" onclick="applySuggestedDateRange('${alt.startDate}', '${alt.endDate}')" title="${alt.description}">
                        ${alt.name}
                    </button>`
        ).join('')}
            </div>
            <button type="button" class="btn-close" data-bs-dismiss="alert" aria-label="Close"></button>
        `;
        const container = document.querySelector('.dashboard-shell');
        if (container) container.insertBefore(alertDiv, container.children[1]);
    }

    window.applySuggestedDateRange = function (startDate, endDate) {
        document.getElementById('startDate').value = startDate;
        document.getElementById('endDate').value = endDate;
        document.querySelectorAll('.alert-info').forEach(a => a.remove());
        onDateRangeChange();
    };

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

        const currentLimit = parseInt(document.getElementById('meterLimit').value, 10) || 5;
        if (titleElement) {
            titleElement.innerHTML =
                `<i class="bi bi-trophy me-2 text-warning"></i>Top ${currentLimit} meters`;
        }

        const usable = (datasets || []).filter(ds => !ds.isCompare);
        if (usable.length === 0) {
            container.innerHTML =
                '<div class="text-center text-muted py-3">No meter consumption available for this period.</div>';
            return;
        }

        const units = [...new Set(usable.map(ds => (ds.unit || 'unit').toLowerCase()))];
        if (units.length > 1) {
            container.innerHTML =
                '<div class="alert alert-light border mb-0">Ranking is hidden because the selected meters use different units. Compare like-for-like meters to get a meaningful ranking.</div>';
            return;
        }

        const totals = usable.map(ds => ({
            name: ds.meterName || ds.label,
            tenant: ds.tenantName || 'Unassigned',
            unit: ds.unit || 'unit',
            total: ds.data.reduce((sum, point) => sum + ((point && point.y) || 0), 0)
        })).sort((a, b) => b.total - a.total);

        const topList = totals.slice(0, currentLimit);
        const maxTotal = Math.max(topList[0]?.total || 0, 1);

        container.innerHTML = topList.map((item, index) => {
            const percent = Math.max(2, (item.total / maxTotal) * 100);
            return `
                <div class="dashboard-ranking-row">
                    <div class="dashboard-ranking-name" title="${item.name} · ${item.tenant}">
                        <span class="text-muted me-2">#${index + 1}</span>${item.name}
                        <div class="dashboard-note ms-4">${item.tenant}</div>
                    </div>
                    <div class="dashboard-ranking-track" aria-hidden="true">
                        <div class="dashboard-ranking-fill" style="width:${percent}%"></div>
                    </div>
                    <div class="dashboard-ranking-value">
                        ${formatMetric(item.total, item.unit)}
                    </div>
                </div>`;
        }).join('');
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