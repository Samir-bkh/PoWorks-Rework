const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

function setup(start, end) {
    const events = {};
    const requests = [];
    const button = { disabled: false, addEventListener: (_, callback) => { events.click = callback; } };
    const status = { textContent: '' };
    const elements = {
        probePcVueHistoryBtn: button,
        pcVueHistoryProbeStatus: status,
        webServiceConnection: { value: 'pcvue-1' },
        webServiceHistoryProbeVariable: { value: 'Building.Power.kW' },
        webServiceStartDate: { value: start },
        webServiceEndDate: { value: end }
    };
    const context = vm.createContext({
        document: {
            addEventListener: (_, callback) => { events.load = callback; },
            getElementById: id => elements[id]
        },
        window: {},
        sessionStorage: { getItem: () => null },
        fetch: async (url, options) => {
            requests.push({ url, payload: JSON.parse(options.body) });
            return { ok: true, json: async () => ({
                success: true, pointCount: 42,
                firstUtc: '2024-06-11T10:00:00Z', lastUtc: '2024-06-11T15:00:00Z'
            }) };
        },
        console
    });
    vm.runInContext(fs.readFileSync(path.join(__dirname,
        '../../PoWorks Rework/wwwroot/js/import/webservices_import_v2.js'), 'utf8'), context);
    events.load();
    return { events, requests, status };
}

test('history check queries the selected PcVue server without importing meters', async () => {
    const { events, requests, status } = setup('2024-06-11T00:00', '2024-06-12T00:00');
    await events.click();

    assert.equal(requests.length, 1);
    assert.equal(requests[0].url, '/Import/ProbePcVueHistory');
    assert.equal(requests[0].payload.connectionId, 'pcvue-1');
    assert.equal(requests[0].payload.variableName, 'Building.Power.kW');
    assert.equal(requests[0].payload.startDate, new Date('2024-06-11T00:00').toISOString());
    assert.equal(requests[0].payload.endDate, new Date('2024-06-12T00:00').toISOString());
    assert.match(status.textContent, /42 original points/);
});

test('history check rejects an excessively broad range before contacting PcVue', async () => {
    const { events, requests, status } = setup('2025-11-06T00:00', '2026-10-01T00:00');
    await events.click();
    assert.equal(requests.length, 0);
    assert.match(status.textContent, /up to 7 days/);
});

test('Web Service import accepts January 2024 through today and rejects reversed dates', () => {
    const invalid = new Set();
    const elements = Object.fromEntries(['webServiceStartDate', 'webServiceEndDate'].map(id => [id, {
        value: '',
        classList: {
            add: value => invalid.add(id + ':' + value),
            remove: value => invalid.delete(id + ':' + value)
        }
    }]));
    const context = vm.createContext({
        window: {},
        document: {
            addEventListener: () => {},
            getElementById: id => elements[id]
        },
        console,
        Date
    });
    vm.runInContext(fs.readFileSync(path.join(__dirname,
        '../../PoWorks Rework/wwwroot/js/import/webservices_import.js'), 'utf8'), context);

    elements.webServiceStartDate.value = '2024-01-01T00:00';
    elements.webServiceEndDate.value = '2026-10-01T12:00';
    assert.equal(vm.runInContext('validateWebServiceDateRange()', context), true);
    assert.equal(invalid.size, 0);

    elements.webServiceEndDate.value = '2023-12-31T00:00';
    assert.equal(vm.runInContext('validateWebServiceDateRange()', context), false);
    assert.ok(invalid.has('webServiceEndDate:is-invalid'));
});
