(function (root, factory) {
    if (typeof module === 'object' && module.exports) {
        module.exports = factory();
    } else {
        root.PoWorksEnergyChartCore = factory();
    }
}(typeof globalThis !== 'undefined' ? globalThis : this, function () {
    'use strict';

    function parseBucketTimestamp(label) {
        if (typeof label === 'number') return label;
        if (typeof label !== 'string' || !label.trim()) return NaN;

        const value = label.trim();
        if (/^\d{4}$/.test(value))
            return new Date(value + '-01-01T00:00:00').getTime();
        if (/^\d{4}-\d{2}$/.test(value))
            return new Date(value + '-01T00:00:00').getTime();
        if (/^\d{4}-\d{2}-\d{2}$/.test(value))
            return new Date(value + 'T00:00:00').getTime();
        if (/^\d{4}-\d{2}-\d{2} \d{2}:\d{2}$/.test(value))
            return new Date(value.replace(' ', 'T') + ':00').getTime();

        return new Date(value).getTime();
    }

    function shiftIsoDateByYears(dateString, deltaYears) {
        if (typeof dateString !== 'string' || !/^\d{4}-\d{2}-\d{2}$/.test(dateString))
            return null;

        const parts = dateString.split('-').map(Number);
        const year = parts[0] + Number(deltaYears || 0);
        const monthIndex = parts[1] - 1;
        const day = parts[2];

        if (!Number.isInteger(year) || monthIndex < 0 || monthIndex > 11 || day < 1)
            return null;

        const lastDay = new Date(year, monthIndex + 1, 0).getDate();
        const clampedDay = Math.min(day, lastDay);
        const month = String(monthIndex + 1).padStart(2, '0');
        const dayText = String(clampedDay).padStart(2, '0');

        return year + '-' + month + '-' + dayText;
    }

    function toTimeSeries(chartData, periodLabel) {
        if (!chartData || !Array.isArray(chartData.labels))
            return { datasets: [] };

        const timestamps = chartData.labels.map(parseBucketTimestamp);
        const effectivePeriod = periodLabel || 'Current period';

        const datasets = (chartData.datasets || []).map((dataset, datasetIndex) => {
            const unit = (dataset.unit || '').trim() || 'unit';
            const meterName = dataset.meterName || dataset.label || 'Meter';
            const tenantName = dataset.tenantName || 'Unassigned';

            return {
                meterId: Number.isFinite(Number(dataset.meterId))
                    ? Number(dataset.meterId)
                    : datasetIndex + 1,
                tenantId: dataset.tenantId ?? null,
                seriesKey: dataset.seriesKey || ('meter:' + (dataset.meterId ?? datasetIndex + 1)),
                label: dataset.label || meterName,
                meterName,
                tenantName,
                unit,
                measurementMetric: dataset.measurementMetric || 'energy',
                isAggregate: dataset.isAggregate === true,
                sourceCount: Number(dataset.sourceCount) || 1,
                data: (dataset.data || []).map((value, index) => {
                    const numericValue =
                        value === null || value === undefined ? null : Number(value);
                    const timestamp = timestamps[index];

                    return {
                        x: Number.isFinite(timestamp) ? timestamp : null,
                        y: Number.isFinite(numericValue) ? numericValue : null,
                        meterName,
                        tenantName,
                        unit,
                        seriesKey: dataset.seriesKey || ('meter:' + (dataset.meterId ?? datasetIndex + 1)),
                        measurementMetric: dataset.measurementMetric || 'energy',
                        seriesLabel: dataset.label || meterName,
                        periodLabel: effectivePeriod
                    };
                })
            };
        });

        return { datasets };
    }

    function sumDataset(dataset) {
        return (dataset?.data || []).reduce(
            (sum, point) =>
                sum + (point && Number.isFinite(point.y) ? point.y : 0),
            0);
    }

    function getCommonUnit(datasets) {
        const values = (datasets || [])
            .filter(dataset => !dataset.isCompare)
            .map(dataset => (dataset.unit || '').trim())
            .filter(Boolean);

        if (values.length === 0) return null;

        const first = values[0];
        const normalized = new Set(values.map(value => value.toLowerCase()));
        return normalized.size === 1 ? first : null;
    }

    function getBounds(datasets) {
        let minX = Infinity;
        let maxX = -Infinity;
        let minY = Infinity;
        let maxY = -Infinity;
        let count = 0;

        (datasets || []).forEach(dataset => {
            (dataset.data || []).forEach(point => {
                if (!point || !Number.isFinite(point.x) || !Number.isFinite(point.y))
                    return;

                minX = Math.min(minX, point.x);
                maxX = Math.max(maxX, point.x);
                minY = Math.min(minY, point.y);
                maxY = Math.max(maxY, point.y);
                count++;
            });
        });

        return count === 0
            ? null
            : { minX, maxX, minY, maxY, count };
    }

    function validateData(data) {
        const errors = [];
        const datasets = data?.datasets || [];

        if (datasets.length === 0)
            errors.push('No chart datasets are available.');

        const commonUnit = getCommonUnit(datasets);
        if (datasets.length > 0 && !commonUnit)
            errors.push('Incompatible measurement units cannot share one chart axis.');

        datasets.forEach(dataset => {
            (dataset.data || []).forEach(point => {
                if (!point || point.y === null || point.y === undefined) return;
                if (!Number.isFinite(point.y))
                    errors.push('A chart point contains a non-numeric consumption value.');
                else if (
                    point.y < 0 &&
                    ['energy', 'volume'].includes(dataset.measurementMetric))
                    errors.push('Consumption quantities cannot be negative.');
            });
        });

        if (!getBounds(datasets))
            errors.push('No finite consumption points are available.');

        return {
            valid: errors.length === 0,
            errors: Array.from(new Set(errors)),
            unit: commonUnit
        };
    }

    function limitDatasets(data, maxCurves) {
        const datasets = data?.datasets || [];
        const limit = Math.max(1, Number(maxCurves) || 10);

        if (datasets.length <= limit) {
            return {
                data: { datasets: datasets.slice() },
                groupedCount: 0,
                keptCount: datasets.length
            };
        }

        const ranked = datasets
            .map(dataset => ({ dataset, total: sumDataset(dataset) }))
            .sort((left, right) => right.total - left.total);

        if (limit === 1) {
            return {
                data: { datasets: [ranked[0].dataset] },
                groupedCount: ranked.length - 1,
                keptCount: 1
            };
        }

        const keptCount = limit - 1;
        const kept = ranked.slice(0, keptCount).map(item => item.dataset);
        const rest = ranked.slice(keptCount);
        const unit = getCommonUnit(rest.map(item => item.dataset)) || 'kWh';
        const totalsByTimestamp = new Map();

        rest.forEach(item => {
            (item.dataset.data || []).forEach(point => {
                if (!point || !Number.isFinite(point.x) || !Number.isFinite(point.y))
                    return;
                totalsByTimestamp.set(
                    point.x,
                    (totalsByTimestamp.get(point.x) || 0) + point.y);
            });
        });

        const othersData = Array.from(totalsByTimestamp.entries())
            .sort((left, right) => left[0] - right[0])
            .map(([x, y]) => ({
                x,
                y,
                meterName: 'Other meters',
                tenantName: 'Multiple tenants',
                unit,
                seriesLabel: 'Others (' + rest.length + ' meters)',
                periodLabel: 'Current period'
            }));

        kept.push({
            meterId: -1,
            label: 'Others (' + rest.length + ' meters)',
            meterName: 'Other meters',
            tenantName: 'Multiple tenants',
            unit,
            data: othersData,
            isOthers: true
        });

        return {
            data: { datasets: kept },
            groupedCount: rest.length,
            keptCount
        };
    }

    function buildComparisonPairs(
        currentFormatted,
        compareChartDataRaw,
        currentStartDateStr,
        compareStartDateStr) {

        const compareFormatted = toTimeSeries(
            compareChartDataRaw,
            'Comparison period');
        const currentStartTs =
            new Date(currentStartDateStr + 'T00:00:00').getTime();
        const compareStartTs =
            new Date(compareStartDateStr + 'T00:00:00').getTime();
        const shiftMs = currentStartTs - compareStartTs;
        const pairs = [];

        (currentFormatted?.datasets || []).forEach(current => {
            const pairKey = current.seriesKey || String(current.meterId);

            pairs.push({
                ...current,
                label: current.label + ' · Current',
                pairKey,
                data: (current.data || []).map(point => ({
                    ...point,
                    periodLabel: 'Current period'
                }))
            });

            const compare = (compareFormatted.datasets || [])
                .find(candidate =>
                    (candidate.seriesKey || String(candidate.meterId)) === pairKey);

            if (!compare) return;

            pairs.push({
                ...compare,
                label: current.label + ' · Comparison',
                pairKey,
                seriesKey: pairKey,
                isCompare: true,
                data: (compare.data || []).map(point => ({
                    ...point,
                    x: Number.isFinite(point.x) ? point.x + shiftMs : point.x,
                    periodLabel: 'Comparison period'
                }))
            });
        });

        return { datasets: pairs };
    }

    return {
        parseBucketTimestamp,
        shiftIsoDateByYears,
        toTimeSeries,
        sumDataset,
        getCommonUnit,
        getBounds,
        validateData,
        limitDatasets,
        buildComparisonPairs
    };
}));
