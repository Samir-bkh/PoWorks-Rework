// PoWorks Building Analytics dashboard
(function () {
    'use strict';

    const chartCore = window.PoWorksEnergyChartCore;
    const preferenceKey = 'poworks.dashboard.analytics.v3';

    let metricCatalog = [];
    let tenants = [];
    let meters = [];
    let root = null;
    let chart = null;
    let exporting = null;
    let autoRefreshInterval = null;
    let reloadTimer = null;

    document.addEventListener('DOMContentLoaded', async function () {
        if (!chartCore) {
            console.error('Dashboard chart core is not loaded.');
            setChartEmpty(true, 'Dashboard unavailable', 'The chart engine could not be loaded.');
            return;
        }

        bindEvents();

        try {
            await Promise.all([
                loadAnalyticsCatalog(),
                loadTenants(),
                loadDateRangeSuggestions()
            ]);

            restorePreferences();
            refreshAggregationOptions();
            setActiveGranularity(valueOf('dateFilter', 'daily'));
            updateComparisonUi();

            await loadMetersForCurrentDateRange();
            await loadChartData();
        } catch (error) {
            console.error('Dashboard initialization failed:', error);
            updateDataStatus('Unable to initialize building analytics.', 'danger');
            setChartEmpty(true, 'Unable to initialize', 'Check the database connection and dashboard configuration.');
        }
    });

    function bindEvents() {
        bindAsyncChange('tenantFilter', async function () {
            await loadMetersForCurrentDateRange();
            await loadChartData();
        });

        bindAsyncChange('measurementMetric', async function () {
            refreshAggregationOptions();
            populateMeterDropdown();
            savePreferences();
            await loadChartData();
        });

        bindAsyncChange('scopeMode', async function () {
            updateScopeDescription();
            updateAdvancedControlState();
            savePreferences();
            await loadChartData();
        });

        bindAsyncChange('aggregationMode', async function () {
            savePreferences();
            await loadChartData();
        });

        bindAsyncChange('chartType', async function () {
            savePreferences();
            await loadChartData();
        });

        bindAsyncChange('maxCurves', async function () {
            savePreferences();
            await loadChartData();
        });

        bindAsyncChange('meterLimit', async function () {
            savePreferences();
            await loadChartData();
        });

        document.getElementById('applyFilters')?.addEventListener('click', async function () {
            if (!validateDateRange()) return;
            await loadMetersForCurrentDateRange();
            await loadChartData();
        });

        document.getElementById('resetFilters')?.addEventListener('click', resetFilters);
        document.getElementById('refreshMeters')?.addEventListener('click', refreshMeters);
        document.getElementById('autoRefresh')?.addEventListener('click', toggleAutoRefresh);
        document.getElementById('exportChart')?.addEventListener('click', exportChart);
        document.getElementById('fullscreenChart')?.addEventListener('click', toggleFullscreen);
        document.getElementById('resetZoomBtn')?.addEventListener('click', resetZoom);

        document.getElementById('startDate')?.addEventListener('change', updateComparisonUi);
        document.getElementById('endDate')?.addEventListener('change', updateComparisonUi);

        document.getElementById('tabHourly')?.addEventListener('click', function () { switchGranularity('hourly'); });
        document.getElementById('tabDaily')?.addEventListener('click', function () { switchGranularity('daily'); });
        document.getElementById('tabMonthly')?.addEventListener('click', function () { switchGranularity('monthly'); });
        document.getElementById('tabYearly')?.addEventListener('click', function () { switchGranularity('yearly'); });

        document.querySelectorAll('input[name="viewMode"]').forEach(function (input) {
            input.addEventListener('change', async function () {
                updateComparisonUi();
                savePreferences();
                await loadChartData();
            });
        });

        document.getElementById('comparePreset')?.addEventListener('change', async function () {
            updateComparisonUi();
            savePreferences();
            await loadChartData();
        });

        document.getElementById('compareStartDate')?.addEventListener('change', async function () {
            updateComparisonUi();
            await loadChartData();
        });
    }

    function bindAsyncChange(id, handler) {
        document.getElementById(id)?.addEventListener('change', handler);
    }

    async function loadAnalyticsCatalog() {
        const response = await fetch('/Dashboard/GetAnalyticsCatalog');
        if (!response.ok) throw new Error('Unable to load analytics catalogue.');

        const payload = await response.json();
        metricCatalog = payload.metrics || [];

        const select = document.getElementById('measurementMetric');
        select.innerHTML = '';

        metricCatalog.forEach(function (metric) {
            const option = document.createElement('option');
            option.value = metric.key;
            option.textContent = metric.label;
            select.appendChild(option);
        });

        if (!metricCatalog.some(function (metric) { return metric.key === 'energy'; })) {
            throw new Error('Energy metric definition is missing.');
        }

        select.value = 'energy';
        updateMetricDescription();
    }

    async function loadTenants() {
        try {
            const response = await fetch('/Dashboard/GetTenants');
            if (!response.ok) throw new Error('Unable to load tenants.');

            tenants = await response.json() || [];
            const select = document.getElementById('tenantFilter');
            select.innerHTML = '<option value="">All tenants</option>';

            tenants.forEach(function (tenant) {
                const option = document.createElement('option');
                option.value = tenant.id;
                option.textContent = tenant.name;
                select.appendChild(option);
            });
        } catch (error) {
            console.error(error);
            tenants = [];
        }
    }

    async function loadDateRangeSuggestions() {
        try {
            const response = await fetch('/Dashboard/GetDateRangeSuggestions');
            if (!response.ok) throw new Error('Unable to load date range.');

            const payload = await response.json();
            if (payload.success) {
                document.getElementById('startDate').value = payload.defaultStartDate;
                document.getElementById('endDate').value = payload.defaultEndDate;
                updateDataStatus(payload.message, 'info');
                return;
            }
        } catch (error) {
            console.warn('Date range suggestions unavailable:', error);
        }

        const end = new Date();
        const start = new Date();
        start.setDate(start.getDate() - 30);
        document.getElementById('startDate').value = formatLocalDate(start);
        document.getElementById('endDate').value = formatLocalDate(end);
    }

    function restorePreferences() {
        let preferences = {};
        try {
            preferences = JSON.parse(localStorage.getItem(preferenceKey) || '{}');
        } catch {
            preferences = {};
        }

        setSelectValueIfAvailable('measurementMetric', preferences.metric);
        setSelectValueIfAvailable('scopeMode', preferences.scopeMode);
        setSelectValueIfAvailable('chartType', preferences.chartType);
        setSelectValueIfAvailable('maxCurves', preferences.maxCurves);
        setSelectValueIfAvailable('meterLimit', preferences.rankingLimit);
        setSelectValueIfAvailable('dateFilter', preferences.dateFilter);
        setSelectValueIfAvailable('comparePreset', preferences.comparePreset);

        if (preferences.comparison === true) {
            const comparison = document.getElementById('modeComparison');
            if (comparison) comparison.checked = true;
        }

        updateMetricDescription();
        updateScopeDescription();
        updateAdvancedControlState();
    }

    function savePreferences() {
        const preferences = {
            metric: valueOf('measurementMetric', 'energy'),
            scopeMode: valueOf('scopeMode', 'aggregate'),
            aggregation: valueOf('aggregationMode', 'auto'),
            chartType: valueOf('chartType', 'line'),
            maxCurves: valueOf('maxCurves', '10'),
            rankingLimit: valueOf('meterLimit', '5'),
            dateFilter: valueOf('dateFilter', 'daily'),
            comparePreset: valueOf('comparePreset', 'previous'),
            comparison: document.getElementById('modeComparison')?.checked === true
        };

        try {
            localStorage.setItem(preferenceKey, JSON.stringify(preferences));
        } catch {
        }
    }

    function setSelectValueIfAvailable(id, value) {
        if (value === undefined || value === null) return;
        const element = document.getElementById(id);
        if (!element) return;

        const exists = Array.from(element.options || []).some(function (option) {
            return option.value === String(value);
        });
        if (exists) element.value = String(value);
    }

    function getMetricDefinition() {
        const key = valueOf('measurementMetric', 'energy');
        return metricCatalog.find(function (item) { return item.key === key; })
            || metricCatalog.find(function (item) { return item.key === 'energy'; })
            || {
                key: 'energy',
                label: 'Energy consumption',
                canonicalUnit: 'kWh',
                valueKind: 'quantity',
                defaultAggregation: 'sum',
                allowedAggregations: ['sum'],
                description: ''
            };
    }

    function updateMetricDescription() {
        const definition = getMetricDefinition();
        const description = document.getElementById('metricDescription');
        if (description) description.textContent = definition.description || '';

        const context = document.getElementById('dashboardMetricContext');
        if (context) context.textContent = definition.label;
    }

    function refreshAggregationOptions() {
        const definition = getMetricDefinition();
        const select = document.getElementById('aggregationMode');
        if (!select) return;

        const previous = select.value || loadSavedAggregation();
        select.innerHTML = '';

        const autoOption = document.createElement('option');
        autoOption.value = 'auto';
        autoOption.textContent = 'Auto · ' + aggregationLabel(definition.defaultAggregation);
        select.appendChild(autoOption);

        (definition.allowedAggregations || []).forEach(function (aggregation) {
            const option = document.createElement('option');
            option.value = aggregation;
            option.textContent = aggregationLabel(aggregation);
            select.appendChild(option);
        });

        const valid = Array.from(select.options).some(function (option) {
            return option.value === previous;
        });
        select.value = valid ? previous : 'auto';

        updateMetricDescription();
        updateMetricAvailabilityCounts();
    }

    function loadSavedAggregation() {
        try {
            const saved = JSON.parse(localStorage.getItem(preferenceKey) || '{}');
            return saved.aggregation || 'auto';
        } catch {
            return 'auto';
        }
    }

    function aggregationLabel(value) {
        return ({
            sum: 'Sum',
            average: 'Average',
            min: 'Minimum',
            max: 'Maximum'
        })[value] || value;
    }

    function updateScopeDescription() {
        const scope = valueOf('scopeMode', 'aggregate');
        const element = document.getElementById('scopeDescription');
        if (!element) return;

        if (scope === 'tenant') {
            element.textContent = 'One series per tenant; unassigned facility meters remain a separate scope.';
        } else if (scope === 'meter') {
            element.textContent = 'Inspect individual compatible meters, limited by Maximum visible series.';
        } else {
            element.textContent = 'One physically meaningful line for the full selected scope.';
        }
    }

    function updateAdvancedControlState() {
        const maxSeries = document.getElementById('maxCurves');
        if (maxSeries) maxSeries.disabled = valueOf('scopeMode', 'aggregate') === 'aggregate';
    }

    async function loadMetersForCurrentDateRange() {
        const selectedBefore = new Set(selectedMeterIds().map(String));

        try {
            const body = {
                startDate: valueOf('startDate'),
                endDate: valueOf('endDate'),
                tenantId: nullableNumber(valueOf('tenantFilter')),
                limit: 2000,
                offset: 0,
                includeNullTenants: true
            };

            const response = await fetch('/Dashboard/GetMetersWithData', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(body)
            });

            if (!response.ok) throw new Error('Unable to load meters.');
            const payload = await response.json();

            meters = payload.success ? (payload.meters || []) : [];
            populateMeterDropdown(selectedBefore);
            updateMetricAvailabilityCounts();

            updateDataStatus(
                meters.length === 0
                    ? 'No sources contain readings in the selected period.'
                    : meters.length + ' source(s) contain readings in the selected period.',
                meters.length ? 'success' : 'warning');
        } catch (error) {
            console.error('Meter discovery failed:', error);
            meters = [];
            populateMeterDropdown(selectedBefore);
            updateDataStatus('Unable to load meter sources.', 'warning');
        }
    }

    function updateMetricAvailabilityCounts() {
        const select = document.getElementById('measurementMetric');
        if (!select || metricCatalog.length === 0) return;

        metricCatalog.forEach(function (metric) {
            const option = Array.from(select.options).find(function (candidate) {
                return candidate.value === metric.key;
            });
            if (!option) return;

            const count = meters.filter(function (meter) {
                return (meter.compatibleMetrics || []).includes(metric.key);
            }).length;

            option.textContent = metric.label + ' · ' + count;
        });
    }

    function populateMeterDropdown(selectedBefore) {
        const container = document.getElementById('meterFilterContainer');
        const metric = valueOf('measurementMetric', 'energy');
        const remembered = selectedBefore || new Set(selectedMeterIds().map(String));

        container.innerHTML = '';

        const toolbar = document.createElement('li');
        toolbar.className = 'dashboard-meter-toolbar';
        toolbar.innerHTML =
            '<div class="input-group input-group-sm mb-2">' +
            '<span class="input-group-text bg-light"><i class="bi bi-search"></i></span>' +
            '<input type="text" id="meterSearchInput" class="form-control" placeholder="Search name, tenant or unit…" autocomplete="off">' +
            '</div>' +
            '<div class="d-flex justify-content-between">' +
            '<button type="button" class="btn btn-sm btn-link text-decoration-none p-0 fw-semibold" id="selectAllMeters">Select compatible</button>' +
            '<button type="button" class="btn btn-sm btn-link text-decoration-none p-0 text-danger" id="clearAllMeters">Clear selection</button>' +
            '</div>';
        container.appendChild(toolbar);

        if (meters.length === 0) {
            const empty = document.createElement('li');
            empty.innerHTML = '<span class="dropdown-item-text text-muted py-3">No sources available for this period.</span>';
            container.appendChild(empty);
            updateMeterDropdownText();
            return;
        }

        const compatible = meters.filter(function (meter) {
            return (meter.compatibleMetrics || []).includes(metric);
        });
        const incompatible = meters.filter(function (meter) {
            return !(meter.compatibleMetrics || []).includes(metric);
        });

        appendMeterGroup(container, 'Compatible with current measurement', compatible, remembered, false);
        appendMeterGroup(container, 'Other measurements', incompatible, remembered, true);

        updateMeterDropdownText();

        const searchInput = document.getElementById('meterSearchInput');
        searchInput?.addEventListener('click', function (event) { event.stopPropagation(); });
        searchInput?.addEventListener('input', function (event) {
            const term = event.target.value.trim().toLowerCase();
            container.querySelectorAll('.dashboard-meter-item').forEach(function (item) {
                const text = (item.dataset.search || '').toLowerCase();
                item.classList.toggle('d-none', Boolean(term) && !text.includes(term));
            });
            updateMeterGroupVisibility(container);
        });

        container.querySelectorAll('.meter-checkbox').forEach(function (checkbox) {
            checkbox.addEventListener('change', function () {
                updateMeterDropdownText();
                scheduleChartReload();
            });
        });

        document.getElementById('selectAllMeters')?.addEventListener('click', function (event) {
            event.preventDefault();
            let changed = false;
            container.querySelectorAll('.meter-checkbox:not(:disabled)').forEach(function (checkbox) {
                const item = checkbox.closest('.dashboard-meter-item');
                if (!item?.classList.contains('d-none') && !checkbox.checked) {
                    checkbox.checked = true;
                    changed = true;
                }
            });
            if (changed) {
                updateMeterDropdownText();
                scheduleChartReload();
            }
        });

        document.getElementById('clearAllMeters')?.addEventListener('click', function (event) {
            event.preventDefault();
            let changed = false;
            container.querySelectorAll('.meter-checkbox:checked').forEach(function (checkbox) {
                checkbox.checked = false;
                changed = true;
            });
            if (changed) {
                updateMeterDropdownText();
                scheduleChartReload();
            }
        });
    }

    function appendMeterGroup(container, title, sourceMeters, remembered, disabled) {
        if (sourceMeters.length === 0) return;

        const header = document.createElement('li');
        header.className = 'dashboard-meter-group-header px-2 pt-2 pb-1 text-uppercase text-muted small fw-semibold';
        header.dataset.groupHeader = 'true';
        header.textContent = title + ' · ' + sourceMeters.length;
        container.appendChild(header);

        sourceMeters.slice().sort(function (a, b) {
            return (a.displayName || a.name).localeCompare(b.displayName || b.name);
        }).forEach(function (meter) {
            const id = String(meter.id);
            const checked = !disabled && remembered.has(id);
            const tenant = meter.tenantName || 'Facility / unassigned';
            const label = meter.displayName || meter.name;
            const family = familyLabel(meter.measurementFamily);

            const item = document.createElement('li');
            item.className = 'dashboard-meter-item' + (disabled ? ' is-incompatible' : '');
            item.dataset.search = label + ' ' + tenant + ' ' + (meter.unit || '') + ' ' + family;
            item.dataset.group = disabled ? 'other' : 'compatible';

            item.innerHTML =
                '<div class="form-check">' +
                '<input class="form-check-input meter-checkbox" type="checkbox" value="' + escapeHtml(id) + '" id="meterCb_' + escapeHtml(id) + '" ' +
                (checked ? 'checked ' : '') + (disabled ? 'disabled' : '') + '>' +
                '<label class="form-check-label w-100" for="meterCb_' + escapeHtml(id) + '">' +
                '<div class="d-flex justify-content-between gap-2">' +
                '<span class="text-truncate" title="' + escapeHtml(label) + '">' + escapeHtml(label) + '</span>' +
                '<span class="dashboard-unit-tag">' + escapeHtml(meter.unit || '—') + '</span>' +
                '</div>' +
                '<div class="dashboard-meter-meta">' +
                '<span>' + escapeHtml(tenant) + '</span><span>·</span>' +
                '<span class="dashboard-family-tag">' + escapeHtml(family) + '</span>' +
                (disabled ? '<span>· not compatible with current measurement</span>' : '') +
                '</div></label></div>';

            container.appendChild(item);
        });
    }

    function updateMeterGroupVisibility(container) {
        container.querySelectorAll('.dashboard-meter-group-header').forEach(function (header) {
            const nextItems = [];
            let cursor = header.nextElementSibling;
            while (cursor && !cursor.dataset.groupHeader) {
                if (cursor.classList.contains('dashboard-meter-item')) nextItems.push(cursor);
                cursor = cursor.nextElementSibling;
            }
            header.classList.toggle(
                'd-none',
                nextItems.length > 0 && nextItems.every(function (item) {
                    return item.classList.contains('d-none');
                }));
        });
    }

    function familyLabel(family) {
        return ({
            energy: 'Energy',
            power: 'Power',
            volume: 'Volume',
            flow: 'Flow',
            temperature: 'Temperature',
            pressure: 'Pressure',
            percentage: 'Percentage',
            raw: 'Other'
        })[family] || 'Other';
    }

    function selectedMeterIds() {
        return Array.from(document.querySelectorAll('.meter-checkbox:checked'))
            .map(function (checkbox) { return Number(checkbox.value); })
            .filter(Number.isFinite);
    }

    function updateMeterDropdownText() {
        const selected = selectedMeterIds();
        const button = document.getElementById('meterDropdownText');
        if (!button) return;

        if (selected.length === 0) {
            button.textContent = 'All compatible sources';
            button.classList.add('text-muted');
        } else if (selected.length === 1) {
            const meter = meters.find(function (item) { return Number(item.id) === selected[0]; });
            button.textContent = meter?.displayName || meter?.name || '1 source selected';
            button.classList.remove('text-muted');
        } else {
            button.textContent = selected.length + ' sources selected';
            button.classList.remove('text-muted');
        }
    }

    function scheduleChartReload() {
        window.clearTimeout(reloadTimer);
        reloadTimer = window.setTimeout(function () { loadChartData(); }, 180);
    }

    async function refreshMeters() {
        await loadMetersForCurrentDateRange();
        await loadChartData();
        showNotification('Source catalogue refreshed.', 'success');
    }

    function validateDateRange() {
        const start = parseLocalDate(valueOf('startDate'));
        const end = parseLocalDate(valueOf('endDate'));

        if (!start || !end || start > end) {
            showNotification('Start date must be on or before end date.', 'warning');
            return false;
        }
        return true;
    }

    function buildRequest() {
        const comparison = document.getElementById('modeComparison')?.checked === true
            ? computeComparisonRange()
            : null;

        return {
            metric: valueOf('measurementMetric', 'energy'),
            scopeMode: valueOf('scopeMode', 'aggregate'),
            aggregation: valueOf('aggregationMode', 'auto'),
            dateFilter: valueOf('dateFilter', 'daily'),
            tenantId: nullableNumber(valueOf('tenantFilter')),
            meterIds: selectedMeterIds(),
            startDate: valueOf('startDate'),
            endDate: valueOf('endDate'),
            limit: Number(valueOf('meterLimit', '5')),
            maxSeries: Number(valueOf('maxCurves', '10')),
            compareStartDate: comparison?.startDate || null
        };
    }

    async function loadChartData() {
        if (!validateDateRange()) return;

        showLoading(true);
        setChartEmpty(false);
        updateComparisonUi();

        const request = buildRequest();

        try {
            const response = await fetch('/Dashboard/GetConsumptionData', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(request)
            });

            if (!response.ok) throw new Error('HTTP ' + response.status);
            const payload = await response.json();

            if (payload.success === false) {
                disposeChart();
                setChartEmpty(
                    true,
                    payload.validationError ? 'Selection needs attention' : 'Unable to calculate view',
                    payload.message || 'Adjust the selected analytical options.');
                updateDataStatus(payload.message || 'Analytics unavailable.', 'warning');
                clearSummary();
                clearRanking();
                return;
            }

            if (payload.noDataInRange || !payload.chartData?.datasets?.length) {
                disposeChart();
                setChartEmpty(
                    true,
                    'No compatible data',
                    'No readings match this measurement, tenant, source selection and period.');
                updateSummaryCards(payload.summary, payload.compareSummary, payload.metadata);
                updateRanking(payload.ranking || [], payload.metadata);
                updateDashboardContext(payload.summary, payload.metadata, request);
                updateCoverage(payload.summary);
                updateDataStatus(payload.message || 'No compatible data found.', 'warning');
                return;
            }

            let chartData = chartCore.toTimeSeries(payload.chartData, 'Current period');

            if (
                document.getElementById('modeComparison')?.checked === true &&
                payload.compareChartData?.datasets?.length &&
                payload.comparison?.startDate) {

                chartData = chartCore.buildComparisonPairs(
                    chartData,
                    payload.compareChartData,
                    request.startDate,
                    payload.comparison.startDate);
            }

            renderChart(chartData, payload.metadata);
            updateSummaryCards(payload.summary, payload.compareSummary, payload.metadata);
            updateRanking(payload.ranking || [], payload.metadata);
            updateDashboardContext(payload.summary, payload.metadata, request);
            updateSeriesWarning(payload.metadata);
            updateCoverage(payload.summary);
            updateChartHeader(payload.metadata, payload.summary, request);
            updateDataStatus(payload.message || 'Analytics updated.', 'success');
            savePreferences();
        } catch (error) {
            console.error('Dashboard analytics request failed:', error);
            disposeChart();
            setChartEmpty(true, 'Unable to load analytics', 'The dashboard request failed. Check server logs and the selected period.');
            updateDataStatus('Unable to load analytical data.', 'danger');
        } finally {
            showLoading(false);
        }
    }

    function renderChart(data, metadata) {
        disposeChart();

        const chartdiv = document.getElementById('chartdiv');
        chartdiv.innerHTML = '';

        const validation = chartCore.validateData(data);
        if (!validation.valid) {
            setChartEmpty(true, 'Invalid chart data', validation.errors.join(' '));
            return;
        }

        const chartType = valueOf('chartType', 'line');
        const dateFilter = valueOf('dateFilter', 'daily');
        const timeUnit =
            dateFilter === 'yearly' ? 'year' :
            dateFilter === 'monthly' ? 'month' :
            dateFilter === 'hourly' ? 'hour' : 'day';
        const axisUnit = validation.unit || metadata?.canonicalUnit || '';
        const bounds = chartCore.getBounds(data.datasets);

        root = am5.Root.new('chartdiv');
        if (root._logo) root._logo.dispose();
        root.setThemes([am5themes_Animated.new(root)]);

        chart = root.container.children.push(am5xy.XYChart.new(root, {
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

        const xAxisSettings = {
            maxDeviation: .1,
            baseInterval: { timeUnit: timeUnit, count: 1 },
            renderer: xRenderer
        };

        if (bounds && bounds.minX === bounds.maxX) {
            const padding =
                timeUnit === 'hour' ? 60 * 60 * 1000 :
                timeUnit === 'day' ? 24 * 60 * 60 * 1000 :
                timeUnit === 'month' ? 31 * 24 * 60 * 60 * 1000 :
                366 * 24 * 60 * 60 * 1000;
            xAxisSettings.min = bounds.minX - padding;
            xAxisSettings.max = bounds.maxX + padding;
            xAxisSettings.strictMinMax = true;
        }

        const xAxis = chart.xAxes.push(am5xy.DateAxis.new(root, xAxisSettings));

        const yRenderer = am5xy.AxisRendererY.new(root, { minGridDistance: 42 });
        yRenderer.grid.template.setAll({
            stroke: am5.color(0xCBD5E1),
            strokeOpacity: .32
        });
        yRenderer.labels.template.setAll({
            fill: am5.color(0x64748B),
            fontSize: 12,
            paddingRight: 8
        });

        const ySettings = {
            renderer: yRenderer,
            numberFormat: '#,###.##',
            extraMax: .08,
            extraMin: .05
        };
        if (metadata?.valueKind === 'quantity' && (!bounds || bounds.minY >= 0)) {
            ySettings.min = 0;
        }

        const yAxis = chart.yAxes.push(am5xy.ValueAxis.new(root, ySettings));
        yAxis.children.unshift(am5.Label.new(root, {
            text: (metadata?.metricLabel || 'Measurement') + ' (' + (axisUnit || 'unit') + ')',
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
            xAxis: xAxis
        }));
        cursor.lineX.setAll({
            stroke: am5.color(0x64748B),
            strokeOpacity: .5,
            strokeDasharray: [4, 4]
        });
        cursor.lineY.set('visible', false);

        if (window.getComputedStyle(chartdiv).position === 'static') {
            chartdiv.style.position = 'relative';
        }

        const hoverOverlay = document.createElement('div');
        hoverOverlay.className = 'poworks-chart-hover-tooltip';
        hoverOverlay.setAttribute('aria-hidden', 'true');
        chartdiv.appendChild(hoverOverlay);

        const hideTooltip = function () {
            hoverOverlay.style.display = 'none';
            hoverOverlay.setAttribute('aria-hidden', 'true');
        };

        const showTooltip = function (point, dataset, originalEvent) {
            if (!point || !Number.isFinite(point.x) || !Number.isFinite(point.y)) return;

            const sourceInfo = dataset.isAggregate
                ? (dataset.sourceCount || 0) + ' source(s)'
                : (point.tenantName || dataset.tenantName || 'Facility / unassigned');

            hoverOverlay.textContent =
                dataset.label + '\n' +
                formatPointDate(point.x, dateFilter) + ' · ' + formatNumber(point.y) + ' ' + axisUnit + '\n' +
                sourceInfo + ' · ' + (point.periodLabel || 'Current period');
            hoverOverlay.style.display = 'block';
            hoverOverlay.setAttribute('aria-hidden', 'false');
            positionTooltip(hoverOverlay, chartdiv, originalEvent);
        };

        chartdiv.onmouseleave = hideTooltip;
        chartdiv.onpointerdown = hideTooltip;

        const colors = [
            am5.color(0x2563EB), am5.color(0x0EA5E9), am5.color(0x10B981),
            am5.color(0x8B5CF6), am5.color(0xF59E0B), am5.color(0xEF4444),
            am5.color(0x14B8A6), am5.color(0x6366F1), am5.color(0x84CC16),
            am5.color(0xEC4899), am5.color(0x64748B)
        ];
        const colorByPair = new Map();
        let colorIndex = 0;

        data.datasets.forEach(function (dataset) {
            const key = dataset.pairKey || dataset.seriesKey || String(dataset.meterId);
            if (!colorByPair.has(key)) {
                colorByPair.set(key, colors[colorIndex % colors.length]);
                colorIndex++;
            }
        });

        data.datasets.forEach(function (dataset) {
            const key = dataset.pairKey || dataset.seriesKey || String(dataset.meterId);
            const color = colorByPair.get(key);
            let series;

            if (chartType === 'bar') {
                series = chart.series.push(am5xy.ColumnSeries.new(root, {
                    name: dataset.label,
                    xAxis: xAxis,
                    yAxis: yAxis,
                    valueYField: 'y',
                    valueXField: 'x',
                    fill: color,
                    stroke: color
                }));

                series.columns.template.setAll({
                    width: am5.percent(data.datasets.length > 4 ? 55 : 72),
                    cornerRadiusTL: 4,
                    cornerRadiusTR: 4,
                    fillOpacity: dataset.isCompare ? .35 : .82,
                    strokeOpacity: .9,
                    interactive: true,
                    cursorOverStyle: 'pointer'
                });

                series.columns.template.events.on('pointerover', function (event) {
                    showTooltip(event.target.dataItem?.dataContext, dataset, event.originalEvent);
                });
                series.columns.template.events.on('pointerout', hideTooltip);
            } else {
                series = chart.series.push(am5xy.LineSeries.new(root, {
                    name: dataset.label,
                    xAxis: xAxis,
                    yAxis: yAxis,
                    valueYField: 'y',
                    valueXField: 'x',
                    fill: color,
                    stroke: color,
                    connect: false
                }));

                series.strokes.template.setAll({
                    strokeWidth: dataset.isCompare ? 2 : 2.5,
                    strokeDasharray: dataset.isCompare ? [5, 4] : undefined,
                    strokeLinecap: 'round',
                    strokeLinejoin: 'round'
                });

                series.fills.template.setAll({
                    visible: chartType === 'area',
                    fillOpacity: dataset.isCompare ? .025 : .09
                });

                // Real data points own their own hover target. No global pointer
                // proximity algorithm is used anywhere in the chart.
                series.bullets.push(function (bulletRoot, _series, dataItem) {
                    const point = dataItem?.dataContext;
                    if (!point || !Number.isFinite(point.y)) return undefined;

                    const hit = am5.Circle.new(bulletRoot, {
                        radius: 9,
                        fill: color,
                        fillOpacity: .001,
                        stroke: color,
                        strokeOpacity: 0,
                        interactive: true,
                        cursorOverStyle: 'pointer'
                    });

                    hit.states.create('hover', {
                        fillOpacity: .16,
                        strokeOpacity: 1,
                        strokeWidth: 2
                    });

                    hit.events.on('pointerover', function (event) {
                        showTooltip(point, dataset, event.originalEvent);
                    });
                    hit.events.on('pointerout', hideTooltip);

                    return am5.Bullet.new(bulletRoot, { sprite: hit });
                });
            }

            series.data.setAll(dataset.data);
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
            maxWidth: 280,
            oversizedBehavior: 'truncate'
        });
        legend.data.setAll(chart.series.values);

        const scrollbar = am5xy.XYChartScrollbar.new(root, {
            orientation: 'horizontal',
            height: 46,
            marginTop: 8
        });
        chart.set('scrollbarX', scrollbar);

        const sbxAxis = scrollbar.chart.xAxes.push(am5xy.DateAxis.new(root, {
            baseInterval: { timeUnit: timeUnit, count: 1 },
            renderer: am5xy.AxisRendererX.new(root, {
                strokeOpacity: 0,
                minGridDistance: 90
            })
        }));
        const sbyAxis = scrollbar.chart.yAxes.push(am5xy.ValueAxis.new(root, {
            renderer: am5xy.AxisRendererY.new(root, {})
        }));

        const previewSource = data.datasets.find(function (dataset) {
            return !dataset.isCompare;
        }) || data.datasets[0];

        if (previewSource) {
            const preview = scrollbar.chart.series.push(am5xy.LineSeries.new(root, {
                xAxis: sbxAxis,
                yAxis: sbyAxis,
                valueYField: 'y',
                valueXField: 'x',
                stroke: am5.color(0x64748B),
                fill: am5.color(0xCBD5E1),
                connect: false
            }));
            preview.strokes.template.setAll({ strokeWidth: 1.4 });
            preview.fills.template.setAll({ visible: true, fillOpacity: .18 });
            preview.data.setAll(previewSource.data);
        }

        exporting = am5plugins_exporting.Exporting.new(root, {
            menu: null,
            dataSource: chart
        });

        chart.appear(350, 20);
    }

    function positionTooltip(tooltip, chartdiv, originalEvent) {
        if (!originalEvent) return;

        const rect = chartdiv.getBoundingClientRect();
        const width = tooltip.offsetWidth;
        const height = tooltip.offsetHeight;
        const pointerX = originalEvent.clientX - rect.left;
        const pointerY = originalEvent.clientY - rect.top;
        const margin = 8;
        const offset = 14;

        let left = pointerX + offset;
        let top = pointerY - height - offset;

        if (left + width > rect.width - margin) left = pointerX - width - offset;
        if (top < margin) top = pointerY + offset;

        left = Math.max(margin, Math.min(left, rect.width - width - margin));
        top = Math.max(margin, Math.min(top, rect.height - height - margin));

        tooltip.style.left = left + 'px';
        tooltip.style.top = top + 'px';
    }

    function updateSummaryCards(summary, compareSummary, metadata) {
        const currentKpis = summary?.kpis || [];
        const comparisonKpis = compareSummary?.kpis || [];

        for (let index = 0; index < 4; index++) {
            const position = index + 1;
            const kpi = currentKpis[index];
            const label = document.getElementById('kpi' + position + 'Label');
            const value = document.getElementById('kpi' + position + 'Value');
            const detail = document.getElementById('kpi' + position + 'Detail');
            const compare = document.getElementById('kpi' + position + 'Compare');

            if (!kpi) {
                if (label) label.textContent = '—';
                if (value) value.textContent = '—';
                if (detail) detail.textContent = '';
                compare?.classList.add('d-none');
                continue;
            }

            if (label) label.textContent = kpi.label;
            if (value) value.textContent = formatMetric(kpi.value, kpi.unit);
            if (detail) detail.textContent = kpi.detail || '';

            const comparisonKpi = comparisonKpis.find(function (item) {
                return item.key === kpi.key;
            });
            updateKpiComparison(compare, kpi, comparisonKpi, metadata);
        }

        const unit = summary?.unit || metadata?.canonicalUnit || '—';
        const badge = document.querySelector('#chartUnitBadge span');
        if (badge) badge.textContent = unit;
        const unitContext = document.getElementById('dashboardUnitContext');
        if (unitContext) unitContext.textContent = unit;
    }

    function updateKpiComparison(element, current, comparison, metadata) {
        if (!element || !comparison || !document.getElementById('modeComparison')?.checked) {
            element?.classList.add('d-none');
            return;
        }

        element.className = 'dashboard-kpi-compare';
        const currentValue = Number(current.value) || 0;
        const comparisonValue = Number(comparison.value) || 0;

        if (current.key === 'meters') {
            const difference = currentValue - comparisonValue;
            element.textContent = (difference >= 0 ? '+' : '') + difference + ' vs comparison';
            element.classList.add('is-neutral');
            return;
        }

        if (comparisonValue === 0) {
            element.textContent = 'No comparison baseline';
            element.classList.add('is-neutral');
            return;
        }

        const percent = (currentValue - comparisonValue) / Math.abs(comparisonValue) * 100;
        element.textContent = (percent >= 0 ? '+' : '') + percent.toFixed(1) + '% vs comparison';

        if (metadata?.valueKind === 'quantity') {
            element.classList.add(percent > .05 ? 'is-up' : percent < -.05 ? 'is-down' : 'is-neutral');
        } else {
            element.classList.add('is-neutral');
        }
    }

    function updateCoverage(summary) {
        const badge = document.getElementById('coverageBadge');
        const span = badge?.querySelector('span');
        const coverage = Number(summary?.coveragePercent);

        if (!badge || !span || !Number.isFinite(coverage)) return;

        span.textContent = 'Coverage ' + coverage.toFixed(0) + '%';
        badge.classList.remove('is-good', 'is-warning');
        badge.classList.add(coverage >= 90 ? 'is-good' : 'is-warning');
    }

    function updateSeriesWarning(metadata) {
        const warning = document.getElementById('curveLimitWarning');
        const text = document.getElementById('curveLimitWarningText');
        const omitted = Number(metadata?.omittedSeries) || 0;

        warning?.classList.toggle('d-none', omitted === 0);
        if (text && omitted > 0) {
            text.textContent =
                omitted + ' additional series are hidden to keep the chart readable. ' +
                'Increase “Maximum visible series” or narrow the scope to inspect them.';
        }
    }

    function updateRanking(items, metadata) {
        const container = document.getElementById('topConsumersList');
        const title = document.getElementById('topConsumersTitle');
        const description = document.getElementById('rankingDescription');
        const definition = getMetricDefinition();
        const limit = Number(valueOf('meterLimit', '5'));
        const scope = valueOf('scopeMode', 'aggregate');
        const tenantSelected = Boolean(document.getElementById('tenantFilter')?.value);
        const byTenant = scope === 'tenant' ||
            (scope === 'aggregate' && !tenantSelected && selectedMeterIds().length === 0);
        const entityLabel = byTenant ? 'tenants / facility' : 'meters';

        if (title) {
            title.innerHTML =
                '<i class="bi bi-trophy me-2 text-warning"></i>' +
                (definition.valueKind === 'quantity' ? 'Top ' : 'Highest ') +
                limit + ' ' + entityLabel;
        }

        if (description) {
            description.textContent = definition.valueKind === 'quantity'
                ? 'Largest total contribution over the selected period.'
                : 'Highest average normalized value over the selected period.';
        }

        if (!container) return;
        if (!items || items.length === 0) {
            container.innerHTML = '<div class="text-center text-muted py-3">No ranking data for this selection.</div>';
            return;
        }

        const maxValue = Math.max.apply(null, items.map(function (item) {
            return Math.abs(Number(item.value) || 0);
        }).concat([1]));

        container.innerHTML = items.map(function (item, index) {
            const numeric = Number(item.value) || 0;
            const percent = Math.max(2, Math.abs(numeric) / maxValue * 100);
            const subtext = item.tenantName
                ? escapeHtml(item.tenantName)
                : item.meterCount > 1
                    ? item.meterCount + ' meters'
                    : '';

            return '<div class="dashboard-ranking-row">' +
                '<div class="dashboard-ranking-name" title="' + escapeHtml(item.name) + '">' +
                '<span class="text-muted me-2">#' + (index + 1) + '</span>' + escapeHtml(item.name) +
                (subtext ? '<div class="dashboard-ranking-subtext ms-4">' + subtext + '</div>' : '') +
                '</div>' +
                '<div class="dashboard-ranking-track" aria-hidden="true">' +
                '<div class="dashboard-ranking-fill" style="width:' + percent + '%"></div>' +
                '</div>' +
                '<div class="dashboard-ranking-value">' + formatMetric(numeric, item.unit) + '</div>' +
                '</div>';
        }).join('');
    }

    function clearRanking() {
        const container = document.getElementById('topConsumersList');
        if (container) {
            container.innerHTML = '<div class="text-center text-muted py-3">No ranking available.</div>';
        }
    }

    function updateDashboardContext(summary, metadata, request) {
        const tenantSelect = document.getElementById('tenantFilter');
        const tenantContext = document.getElementById('dashboardTenantContext');
        if (tenantContext) {
            tenantContext.textContent =
                tenantSelect?.selectedOptions?.[0]?.textContent?.trim() || 'All tenants';
        }

        const metricContext = document.getElementById('dashboardMetricContext');
        if (metricContext) metricContext.textContent = metadata?.metricLabel || getMetricDefinition().label;

        const viewContext = document.getElementById('dashboardViewContext');
        if (viewContext) {
            viewContext.textContent = ({
                aggregate: 'Aggregate',
                tenant: 'By tenant',
                meter: 'Individual meters'
            })[metadata?.scopeMode || request.scopeMode] || 'Aggregate';
        }

        const periodContext = document.getElementById('dashboardPeriodContext');
        if (periodContext) periodContext.textContent = request.startDate + ' → ' + request.endDate;

        const rankingScope = document.getElementById('rankingScopeBadge');
        if (rankingScope) {
            rankingScope.textContent = tenantSelect?.value
                ? tenantSelect.selectedOptions[0].textContent.trim()
                : 'Current workspace';
        }
    }

    function updateChartHeader(metadata, summary, request) {
        const title = document.getElementById('chartTitle');
        const subtitle = document.getElementById('chartSubtitle');

        if (title) title.textContent = metadata?.metricLabel || 'Building analytics';

        if (subtitle) {
            let scopeLabel = 'Building aggregate';
            if (request.scopeMode === 'tenant') scopeLabel = 'Breakdown by tenant';
            else if (request.scopeMode === 'meter') scopeLabel = 'Individual meter view';
            else if (request.tenantId) scopeLabel = 'Selected tenant aggregate';

            subtitle.textContent =
                scopeLabel + ' · ' +
                aggregationLabel(summary?.aggregation || metadata?.aggregation || 'sum') + ' · ' +
                (summary?.activeMeters || 0) + ' contributing source(s)';
        }
    }

    function clearSummary() {
        for (let index = 1; index <= 4; index++) {
            const label = document.getElementById('kpi' + index + 'Label');
            const value = document.getElementById('kpi' + index + 'Value');
            const detail = document.getElementById('kpi' + index + 'Detail');
            if (label) label.textContent = '—';
            if (value) value.textContent = '—';
            if (detail) detail.textContent = '';
            document.getElementById('kpi' + index + 'Compare')?.classList.add('d-none');
        }
    }

    function computeComparisonRange() {
        const start = parseLocalDate(valueOf('startDate'));
        const end = parseLocalDate(valueOf('endDate'));
        if (!start || !end) return null;

        const durationDays = Math.max(1, daysBetween(start, end) + 1);
        const preset = valueOf('comparePreset', 'previous');
        let compareStart;

        if (preset === 'lastYear') {
            compareStart = new Date(start);
            compareStart.setFullYear(compareStart.getFullYear() - 1);
        } else if (preset === 'custom') {
            compareStart = parseLocalDate(valueOf('compareStartDate'));
            if (!compareStart) return null;
        } else {
            compareStart = addDays(start, -durationDays);
        }

        const compareEnd = addDays(compareStart, durationDays - 1);
        return {
            startDate: formatLocalDate(compareStart),
            endDate: formatLocalDate(compareEnd),
            durationDays: durationDays
        };
    }

    function updateComparisonUi() {
        const enabled = document.getElementById('modeComparison')?.checked === true;
        document.getElementById('comparisonOptions')?.classList.toggle('d-none', !enabled);

        const custom = valueOf('comparePreset', 'previous') === 'custom';
        document.getElementById('compareCustomDates')?.classList.toggle('d-none', !custom);

        if (custom && !document.getElementById('compareStartDate')?.value) {
            const primaryStart = parseLocalDate(valueOf('startDate'));
            if (primaryStart) {
                document.getElementById('compareStartDate').value =
                    formatLocalDate(addDays(primaryStart, -365));
            }
        }

        const range = enabled ? computeComparisonRange() : null;
        const resolved = document.getElementById('comparisonResolvedRange');
        if (resolved) {
            resolved.textContent = range
                ? range.startDate + ' → ' + range.endDate + ' · ' + range.durationDays + ' day(s)'
                : 'Choose a valid comparison start';
        }
    }

    function switchGranularity(value) {
        const dateFilter = document.getElementById('dateFilter');
        if (dateFilter) dateFilter.value = value;
        setActiveGranularity(value);
        savePreferences();
        loadChartData();
    }

    function setActiveGranularity(value) {
        const mapping = {
            hourly: 'tabHourly',
            daily: 'tabDaily',
            monthly: 'tabMonthly',
            yearly: 'tabYearly'
        };

        Object.keys(mapping).forEach(function (key) {
            document.getElementById(mapping[key])?.classList.toggle('active', key === value);
        });
    }

    async function resetFilters() {
        try {
            localStorage.removeItem(preferenceKey);
        } catch {
        }

        document.getElementById('tenantFilter').value = '';
        document.getElementById('measurementMetric').value = 'energy';
        document.getElementById('scopeMode').value = 'aggregate';
        document.getElementById('chartType').value = 'line';
        document.getElementById('maxCurves').value = '10';
        document.getElementById('meterLimit').value = '5';
        document.getElementById('dateFilter').value = 'daily';
        document.getElementById('modeStandard').checked = true;
        document.getElementById('comparePreset').value = 'previous';
        document.getElementById('compareStartDate').value = '';

        refreshAggregationOptions();
        document.getElementById('aggregationMode').value = 'auto';
        setActiveGranularity('daily');
        updateScopeDescription();
        updateAdvancedControlState();
        updateComparisonUi();

        await loadDateRangeSuggestions();
        await loadMetersForCurrentDateRange();
        await loadChartData();
    }

    function toggleAutoRefresh() {
        const button = document.getElementById('autoRefresh');

        if (autoRefreshInterval) {
            window.clearInterval(autoRefreshInterval);
            autoRefreshInterval = null;
            button?.classList.remove('active');
            showNotification('Live refresh disabled.', 'info');
            return;
        }

        autoRefreshInterval = window.setInterval(function () {
            loadChartData();
        }, 30000);
        button?.classList.add('active');
        showNotification('Live refresh enabled every 30 seconds.', 'success');
    }

    function exportChart() {
        if (!exporting) {
            showNotification('No chart is available to export.', 'warning');
            return;
        }

        exporting.download('png');
        showNotification('Chart export started.', 'success');
    }

    function resetZoom() {
        try {
            chart?.xAxes?.getIndex(0)?.zoom(0, 1);
        } catch (error) {
            console.warn('Unable to reset zoom:', error);
        }
    }

    function toggleFullscreen() {
        const card = document.getElementById('chartdiv')?.closest('.card');
        const icon = document.querySelector('#fullscreenChart i');
        if (!card) return;

        const fullscreen = card.classList.toggle('chart-fullscreen');
        document.body.style.overflow = fullscreen ? 'hidden' : '';
        icon?.classList.toggle('bi-arrows-fullscreen', !fullscreen);
        icon?.classList.toggle('bi-fullscreen-exit', fullscreen);
    }

    function disposeChart() {
        if (root) {
            root.dispose();
            root = null;
            chart = null;
            exporting = null;
        }

        document.getElementById('chartdiv')
            ?.querySelectorAll('.poworks-chart-hover-tooltip')
            .forEach(function (element) { element.remove(); });
    }

    function showLoading(show) {
        document.getElementById('loadingSpinner')?.classList.toggle('d-none', !show);
    }

    function setChartEmpty(show, title, message) {
        const state = document.getElementById('chartEmptyState');
        state?.classList.toggle('d-none', !show);

        if (!show) return;

        const titleElement = document.getElementById('chartEmptyTitle');
        const messageElement = document.getElementById('chartEmptyMessage');
        if (titleElement) titleElement.textContent = title || 'No compatible data';
        if (messageElement) messageElement.textContent = message || 'Adjust the analytical selection.';
    }

    function updateDataStatus(message, type) {
        const container = document.getElementById('dataStatus');
        const text = document.getElementById('dataStatusText');
        if (!container || !text) return;

        const actualType = type || 'info';
        text.textContent = message;
        container.className = 'dashboard-status alert alert-' + actualType + ' mb-0';
        container.style.display = 'block';
    }

    function showNotification(message, type) {
        document.querySelectorAll('.dashboard-alert').forEach(function (alert) {
            alert.remove();
        });

        const actualType = type || 'info';
        const alert = document.createElement('div');
        alert.className = 'alert alert-' + actualType + ' alert-dismissible fade show dashboard-alert';
        alert.innerHTML =
            escapeHtml(message) +
            '<button type="button" class="btn-close" data-bs-dismiss="alert"></button>';

        const shell = document.querySelector('.dashboard-shell');
        if (!shell) return;

        shell.insertBefore(alert, shell.firstChild);
        window.setTimeout(function () { alert.remove(); }, 5000);
    }

    function formatMetric(value, unit) {
        const numeric = Number(value);
        if (!Number.isFinite(numeric)) return '—';

        const absolute = Math.abs(numeric);
        const digits = absolute !== 0 && absolute < 10 ? 3 : absolute < 100 ? 2 : 1;

        return new Intl.NumberFormat(undefined, {
            maximumFractionDigits: digits
        }).format(numeric) + (unit ? ' ' + unit : '');
    }

    function formatNumber(value) {
        return new Intl.NumberFormat(undefined, {
            maximumFractionDigits: Math.abs(value) < 10 ? 3 : 2
        }).format(value);
    }

    function formatPointDate(timestamp, granularity) {
        const options =
            granularity === 'yearly' ? { year: 'numeric' } :
            granularity === 'monthly' ? { month: 'short', year: 'numeric' } :
            granularity === 'hourly'
                ? { day: '2-digit', month: 'short', year: 'numeric', hour: '2-digit', minute: '2-digit' }
                : { day: '2-digit', month: 'short', year: 'numeric' };

        return new Intl.DateTimeFormat(undefined, options).format(new Date(timestamp));
    }

    function valueOf(id, fallback) {
        const element = document.getElementById(id);
        if (!element || element.value === undefined || element.value === null || element.value === '') {
            return fallback === undefined ? '' : fallback;
        }
        return element.value;
    }

    function nullableNumber(value) {
        if (value === '' || value === null || value === undefined) return null;
        const number = Number(value);
        return Number.isFinite(number) ? number : null;
    }

    function parseLocalDate(value) {
        if (!value) return null;
        const parts = value.split('-').map(Number);
        if (parts.length !== 3 || parts.some(function (part) { return !Number.isFinite(part); })) return null;
        return new Date(parts[0], parts[1] - 1, parts[2]);
    }

    function formatLocalDate(date) {
        const year = date.getFullYear();
        const month = String(date.getMonth() + 1).padStart(2, '0');
        const day = String(date.getDate()).padStart(2, '0');
        return year + '-' + month + '-' + day;
    }

    function addDays(date, days) {
        const result = new Date(date);
        result.setDate(result.getDate() + days);
        return result;
    }

    function daysBetween(start, end) {
        const utcStart = Date.UTC(start.getFullYear(), start.getMonth(), start.getDate());
        const utcEnd = Date.UTC(end.getFullYear(), end.getMonth(), end.getDate());
        return Math.round((utcEnd - utcStart) / 86400000);
    }

    function escapeHtml(value) {
        return String(value === null || value === undefined ? '' : value)
            .replaceAll('&', '&amp;')
            .replaceAll('<', '&lt;')
            .replaceAll('>', '&gt;')
            .replaceAll('"', '&quot;')
            .replaceAll("'", '&#039;');
    }
})();
