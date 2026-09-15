const test = require('node:test');
const assert = require('node:assert/strict');
const core = require('../../PoWorks Rework/wwwroot/js/energy-chart-core.js');

test('parseBucketTimestamp supports hourly, daily, monthly and annual buckets', () => {
    assert.ok(Number.isFinite(core.parseBucketTimestamp('2026-09-15 10:00')));
    assert.ok(Number.isFinite(core.parseBucketTimestamp('2026-09-15')));
    assert.ok(Number.isFinite(core.parseBucketTimestamp('2026-09')));
    assert.ok(Number.isFinite(core.parseBucketTimestamp('2026')));
});

test('toTimeSeries preserves missing buckets as null instead of zero', () => {
    const result = core.toTimeSeries({
        labels: ['2026-09-15 10:00', '2026-09-15 11:00'],
        datasets: [{
            meterId: 7,
            label: 'Main meter',
            meterName: 'Main meter',
            tenantName: 'Tenant A',
            unit: 'kWh',
            data: [12.5, null]
        }]
    });

    assert.equal(result.datasets[0].meterId, 7);
    assert.equal(result.datasets[0].data[0].y, 12.5);
    assert.equal(result.datasets[0].data[1].y, null);
});

test('validateData rejects mixed units and negative consumption', () => {
    const mixed = core.validateData({
        datasets: [
            { unit: 'kWh', data: [{ x: 1, y: 2 }] },
            { unit: 'm3', data: [{ x: 1, y: 3 }] }
        ]
    });
    assert.equal(mixed.valid, false);
    assert.ok(mixed.errors.some(error => error.includes('Incompatible')));

    const negative = core.validateData({
        datasets: [{ unit: 'kWh', data: [{ x: 1, y: -1 }] }]
    });
    assert.equal(negative.valid, false);
});

test('limitDatasets keeps largest consumers and groups the remainder', () => {
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

test('comparison pairs match by meter id rather than display label', () => {
    const current = core.toTimeSeries({
        labels: ['2026-09-15'],
        datasets: [{
            meterId: 42,
            label: 'Renamed display label',
            meterName: 'Meter 42',
            unit: 'kWh',
            data: [10]
        }]
    });

    const paired = core.buildComparisonPairs(
        current,
        {
            labels: ['2026-09-14'],
            datasets: [{
                meterId: 42,
                label: 'Old label',
                meterName: 'Meter 42',
                unit: 'kWh',
                data: [8]
            }]
        },
        '2026-09-15',
        '2026-09-14');

    assert.equal(paired.datasets.length, 2);
    assert.equal(paired.datasets[0].pairKey, '42');
    assert.equal(paired.datasets[1].pairKey, '42');
    assert.equal(paired.datasets[1].isCompare, true);
    assert.equal(
        paired.datasets[1].data[0].x,
        paired.datasets[0].data[0].x);
});

test('getBounds ignores null points and returns finite visible extent', () => {
    const bounds = core.getBounds([
        {
            unit: 'kWh',
            data: [
                { x: 100, y: 5 },
                { x: 200, y: null },
                { x: 300, y: 12 }
            ]
        }
    ]);

    assert.deepEqual(bounds, {
        minX: 100,
        maxX: 300,
        maxY: 12,
        count: 2
    });
});
