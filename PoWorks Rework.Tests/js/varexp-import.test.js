const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
const path = require('node:path');

test('VAREXP selection keeps numeric/boolean variables and their units, excluding text', () => {
    const context = vm.createContext({
        document: { addEventListener() {} },
        window: {},
        console
    });
    const source = fs.readFileSync(path.join(__dirname,
        '../../PoWorks Rework/wwwroot/js/import/varexp_import.js'), 'utf8');
    vm.runInContext(source, context);
    let selected;
    context.showMeterSelectionForVarexp = meters => { selected = meters; };

    context.convertVarexpToMeterSelection([
        ['Class', 'CombinedName', 'Description', 'Source', 'Unit'],
        ['CTV', 'Building.ExternalTemp', '', 'I', '°C'],
        ['CTV', 'Building.Power.kW', '', 'I', 'kW'],
        ['BIT', 'Building.Pump.Running', '', 'I', ''],
        ['REG', 'Building.Counter', '', 'I', 'kWh'],
        ['TXT', 'Comments.Line01', '', 'I', ''],
        ['CXT', 'Comments.Line02', '', 'I', ''],
        ['CTV', 'System.Value', '', 'I', '%']
    ]);

    assert.equal(selected.length, 4);
    assert.equal(selected[0].hdsMeterName, 'Building.ExternalTemp');
    assert.equal(selected[0].unit, '°C');
    assert.equal(selected[1].unit, 'kW');
    assert.equal(selected[2].unit, '');
    assert.equal(selected[2].recordType, 'BIT');
    assert.equal(selected[3].unit, 'kWh');
});

function createImportContext({ connectionId = 'pcvue-1', startDate = '2025-06-01T00:00', endDate = '2025-06-08T00:00' } = {}) {
    const alerts = [];
    const requests = [];
    const follow = [];
    const meter = {
        closest: () => row
    };
    const row = {
        classList: { contains: () => false },
        querySelector(selector) {
            if (selector === 'td:nth-child(2)') return { querySelector: () => ({ textContent: 'Building.Power.kW' }) };
            return {
                '.meter-unit': { value: 'kW' },
                '.meter-type': { value: 'main' },
                '.meter-parent': { value: '' },
                '.meter-active': { checked: true }
            }[selector];
        }
    };
    const button = { disabled: false, innerHTML: 'Import' };
    const elements = {
        importSelectedBtn: button,
        importReadings: { checked: true },
        webServiceConnection: { value: connectionId },
        webServiceStartDate: { value: startDate },
        webServiceEndDate: { value: endDate }
    };
    const context = vm.createContext({
        document: {
            addEventListener() {},
            getElementById: id => elements[id],
            querySelectorAll: () => [meter]
        },
        window: { PoWorksHistoricalImport: { follow: id => follow.push(id) } },
        alert: message => alerts.push(message),
        confirm: () => true,
        fetch: async (url, options) => {
            requests.push({ url, body: JSON.parse(options.body) });
            return { ok: true, json: async () => url.includes('UpsertWebService')
                ? { success: true, trendsQueued: true, historyJobId: 'history-1' }
                : { success: true, importedCount: 0, updatedCount: 0, skippedCount: 1, totalProcessed: 1 }
            };
        },
        console
    });
    vm.runInContext(fs.readFileSync(path.join(__dirname,
        '../../PoWorks Rework/wwwroot/js/import/varexp_import.js'), 'utf8'), context);
    return { context, alerts, requests, follow, button };
}

test('VAREXP imports existing meter history using selected PcVue connection and past dates', async () => {
    const { context, requests, follow } = createImportContext();
    await context.handleVarexpImport();

    assert.equal(requests.length, 2);
    assert.equal(requests[0].url, '/VarexpImport/ImportVarexpMeters');
    assert.equal(requests[1].url, '/Import/UpsertWebServiceVariablesWithTrends');
    assert.equal(requests[1].body.variables[0].variableName, 'Building.Power.kW');
    assert.equal(requests[1].body.connectionId, 'pcvue-1');
    assert.equal(requests[1].body.trendsStartDate, '2025-06-01T00:00');
    assert.equal(requests[1].body.trendsEndDate, '2025-06-08T00:00');
    assert.deepEqual(follow, ['history-1']);
});

test('VAREXP refuses to save meters when historical import has no connection', async () => {
    const { context, requests, alerts } = createImportContext({ connectionId: '' });
    await context.handleVarexpImport();
    assert.equal(requests.length, 0);
    assert.match(alerts[0], /PcVue Web Service connection/);
});
