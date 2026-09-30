const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
const path = require('node:path');

function page() {
    const inputs = {
        startDate: { value: '2026-08-31T00:00', addEventListener(event, handler) { this[event] = handler; } },
        endDate: { value: '2026-09-30T00:00', addEventListener(event, handler) { this[event] = handler; } }
    };
    let tick;
    let now = new Date(2026, 8, 30, 15, 58, 0);
    const RealDate = Date;
    class FakeDate extends RealDate {
        constructor(...args) { super(...(args.length ? args : [now.getTime()])); }
        static now() { return now.getTime(); }
    }
    const requests = [];
    const document = {
        hidden: false,
        getElementById(id) { return inputs[id] || null; },
        querySelectorAll() { return []; },
        querySelector() { return null; }
    };
    const window = { setInterval(callback, interval) {
        assert.equal(interval, 30000);
        tick = callback;
    } };
    const context = vm.createContext({
        document, window, Date: FakeDate, URLSearchParams,
        fetch(url) {
            requests.push(new URL(url, 'http://localhost'));
            return Promise.resolve({ json: () => Promise.resolve({
                success: true, data: [], pagination: { currentPage: 1, totalCount: 0, totalPages: 0, pageSize: 50 }
            }) });
        },
        console
    });
    const source = fs.readFileSync(path.join(__dirname, '../../PoWorks Rework/wwwroot/js/meter-readings.js'), 'utf8');
    vm.runInContext(source, context);
    return {
        manager: window.MeterReadings,
        inputs, requests,
        tick: () => tick(),
        setNow(date) { now = date; }
    };
}

async function settle() {
    await new Promise(resolve => setImmediate(resolve));
}

test('initial range ends now and automatic refresh includes the next readings', async () => {
    const state = page();
    state.manager.init({ selectedMeterIds: [4], endDate: state.inputs.endDate.value, startDate: state.inputs.startDate.value });
    await settle();
    assert.equal(state.manager.config.selectedMeterId, '4');
    assert.match(state.requests[0].searchParams.get('endDate'), /T15:58$/);

    state.setNow(new Date(2026, 8, 30, 16, 0, 0));
    state.tick();
    await settle();
    assert.match(state.requests.at(-1).searchParams.get('endDate'), /T16:00$/);
    assert.equal(state.requests.at(-1).searchParams.get('meterIds'), '4');
});

test('a custom end date remains fixed during automatic refresh', async () => {
    const state = page();
    state.manager.init({ endDate: state.inputs.endDate.value });
    await settle();
    state.inputs.endDate.value = '2026-09-15T12:00';
    state.inputs.endDate.change();
    state.setNow(new Date(2026, 8, 30, 16, 0, 0));
    state.tick();
    await settle();
    assert.equal(state.requests.at(-1).searchParams.get('endDate'), '2026-09-15T12:00');
});
