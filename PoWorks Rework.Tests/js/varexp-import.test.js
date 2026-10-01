const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
const path = require('node:path');

test('VAREXP selection includes CTV variables and their configured units', () => {
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
        ['TXT', 'Comments.Line01', '', 'I', ''],
        ['CTV', 'System.Value', '', 'I', '%']
    ]);

    assert.equal(selected.length, 3);
    assert.equal(selected[0].hdsMeterName, 'Building.ExternalTemp');
    assert.equal(selected[0].unit, '°C');
    assert.equal(selected[1].unit, 'kW');
    assert.equal(selected[2].unit, '');
});
