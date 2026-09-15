const test = require('node:test');
const assert = require('node:assert/strict');
const core = require('../../PoWorks Rework/wwwroot/js/energy-chart-core.js');

test('parseBucketTimestamp supports hourly, daily, monthly and annual buckets', () => {
    assert.ok(Number.isFinite(core.parseBucketTimestamp('2026-09-15 10:00')));
    assert.ok(Number.isFinite(core.parseBucketTimestamp('2026-09-15')));
    assert.ok(Number.isFinite(core.parseBucketTimestamp('2026-09')));
    assert.ok(Number.isFinite(core.parseBucketTimestamp('2026')));
});

test('toTimeSeries preserves metadata and missing buckets as null instead of zero', () => {
    const result = core.toTimeSeries({
        labels: ['2026-09-15 10:00', '2026-09-15 11:00'],
        datasets: [{
            meterId: 7,
            tenantId: 10,
            seriesKey: 'tenant:10',
            label: 'Tenant A aggregate',
            meterName: 'Tenant A aggregate',
            tenantName: 'Tenant A',
            unit: 'kWh',
            measurementMetric: 'energy',
            isAggregate: true,
            sourceCount: 4,
            data: [12.5, null]
        }]
    });

    const dataset = result.datasets[0];
    assert.equal(dataset.meterId, 7);
    assert.equal(dataset.tenantId, 10);
    assert.equal(dataset.seriesKey, 'tenant:10');
    assert.equal(dataset.measurementMetric, 'energy');
    assert.equal(dataset.isAggregate, true);
    assert.equal(dataset.sourceCount, 4);
    assert.equal(dataset.data[0].y, 12.5);
    assert.equal(dataset.data[1].y, null);
});

test('validateData rejects mixed units on one axis', () => {
    const mixed = core.validateData({
        datasets: [
            { unit: 'kWh', measurementMetric: 'energy', data: [{ x: 1, y: 2 }] },
            { unit: 'm3', measurementMetric: 'volume', data: [{ x: 1, y: 3 }] }
        ]
    });

    assert.equal(mixed.valid, false);
    assert.ok(mixed.errors.some(error => error.includes('Incompatible')));
});

test('validateData rejects negative quantities but allows negative temperatures', () => {
    const negativeEnergy = core.validateData({
        datasets: [{
            unit: 'kWh',
            measurementMetric: 'energy',
            data: [{ x: 1, y: -1 }]
        }]
    });
    assert.equal(negativeEnergy.valid, false);

    const negativeTemperature = core.validateData({
        datasets: [{
            unit: '°C',
            measurementMetric: 'temperature',
            data: [{ x: 1, y: -12 }]
        }]
    });
    assert.equal(negativeTemperature.valid, true);
});

test('comparison pairs use stable seriesKey for aggregate and tenant views', () => {
    const current = core.toTimeSeries({
        labels: ['2026-09-15'],
        datasets: [{
            meterId: 0,
            seriesKey: 'aggregate',
            label: 'Building aggregate',
            meterName: 'Building aggregate',
            unit: 'kWh',
            measurementMetric: 'energy',
            isAggregate: true,
            sourceCount: 20,
            data: [100]
        }]
    });

    const paired = core.buildComparisonPairs(
        current,
        {
            labels: ['2026-09-01'],
            datasets: [{
                meterId: 999,
                seriesKey: 'aggregate',
                label: 'Different server label',
                meterName: 'Different server label',
                unit: 'kWh',
                measurementMetric: 'energy',
                isAggregate: true,
                sourceCount: 18,
                data: [80]
            }]
        },
        '2026-09-15',
        '2026-09-01');

    assert.equal(paired.datasets.length, 2);
    assert.equal(paired.datasets[0].pairKey, 'aggregate');
    assert.equal(paired.datasets[1].pairKey, 'aggregate');
    assert.equal(paired.datasets[1].isCompare, true);
    assert.equal(paired.datasets[1].data[0].x, paired.datasets[0].data[0].x);
});

test('comparison omits comparison series when stable key is absent', () => {
    const current = core.toTimeSeries({
        labels: ['2026-09-15'],
        datasets: [{
            meterId: 1,
            seriesKey: 'tenant:10',
            label: 'Tenant A',
            unit: 'kWh',
            measurementMetric: 'energy',
            data: [10]
        }]
    });

    const paired = core.buildComparisonPairs(
        current,
        {
            labels: ['2026-09-14'],
            datasets: [{
                meterId: 2,
                seriesKey: 'tenant:20',
                label: 'Tenant B',
                unit: 'kWh',
                measurementMetric: 'energy',
                data: [8]
            }]
        },
        '2026-09-15',
        '2026-09-14');

    assert.equal(paired.datasets.length, 1);
    assert.equal(paired.datasets[0].seriesKey, 'tenant:10');
});

test('getBounds ignores null points and returns negative and positive y extents', () => {
    const bounds = core.getBounds([
        {
            unit: '°C',
            measurementMetric: 'temperature',
            data: [
                { x: 100, y: -5 },
                { x: 200, y: null },
                { x: 300, y: 12 }
            ]
        }
    ]);

    assert.deepEqual(bounds, {
        minX: 100,
        maxX: 300,
        minY: -5,
        maxY: 12,
        count: 2
    });
});

test('limitDatasets remains deterministic for legacy callers', () => {
    const result = core.limitDatasets({
        datasets: [
            { meterId: 1, label: 'A', unit: 'kWh', data: [{ x: 1, y: 100 }] },
            { meterId: 2, label: 'B', unit: 'kWh', data: [{ x: 1, y: 50 }] },
            { meterId: 3, label: 'C', unit: 'kWh', data: [{ x: 1, y: 25 }] },
            { meterId: 4, label: 'D', unit: 'kWh', data: [{ x: 1, y: 10 }] }
        ]
    }, 3);

    assert.equal(result.data.datasets.length, 3);
    assert.equal(result.data.datasets[0].meterId, 1);
    assert.equal(result.data.datasets[1].meterId, 2);
    assert.equal(result.data.datasets[2].isOthers, true);
    assert.equal(result.data.datasets[2].data[0].y, 35);
    assert.equal(result.groupedCount, 2);
});
