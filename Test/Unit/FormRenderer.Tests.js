/**
 * @jest-environment jsdom
 */
// Tests for the metadata-driven form renderer that replaced ControlBuilder.vb.

const { queryValue, prefillValue, parseBoolean, buildParam, buildVerboseControl } =
    require('../../WebJEA/wwwroot/resources/form-renderer.js');

describe('parseBoolean', () => {
    test.each([
        ['true', true],
        ['True', true],
        ['1', true],
        ['false', false],
        ['0', false],
        [true, true],
        [false, false]
    ])('parses %p as %p', (input, expected) => {
        expect(parseBoolean(input)).toBe(expected);
    });

    test('returns null for unparseable values', () => {
        expect(parseBoolean('banana')).toBeNull();
        expect(parseBoolean(null)).toBeNull();
    });
});

describe('queryValue / prefillValue', () => {
    test('query lookup is case-insensitive', () => {
        expect(queryValue('input01', '?Input01=abc')).toBe('abc');
    });

    test('query value overrides default', () => {
        expect(prefillValue('Var', 'default', '?Var=fromqs')).toBe('fromqs');
    });

    test('missing query value keeps default', () => {
        expect(prefillValue('Var', 'default', '?Other=x')).toBe('default');
    });
});

describe('buildParam - text control', () => {
    const base = {
        name: 'Var', type: 'string', control: 'text', labelOverride: null,
        helpMessage: '', helpDetail: '', isMandatory: false, isMultiValued: false,
        rows: null, allowedValues: null, defaultValue: null, validation: []
    };

    test('renders form-group with label and input', () => {
        const row = buildParam({ ...base }, '');
        expect(row.className).toBe('form-group');
        expect(row.querySelector('label.form-label').textContent).toBe('Var');
        const input = row.querySelector('input#Var');
        expect(input.className).toBe('form-control');
        expect(input.value).toBe('');
    });

    test('labelOverride replaces the label text', () => {
        const row = buildParam({ ...base, labelOverride: 'Enter the computer name' }, '');
        expect(row.querySelector('label.form-label').textContent).toBe('Enter the computer name');
    });

    test('mandatory adds Required marker and validator span', () => {
        const row = buildParam({ ...base, isMandatory: true, validation: [{ type: 'required' }] }, '');
        expect(row.querySelector('span.reqopt').textContent).toBe('Required');
        const val = row.querySelector('span.valmsg');
        expect(val.getAttribute('data-valtype')).toBe('required');
        expect(val.getAttribute('data-control')).toBe('Var');
    });

    test('default value and query-string prefill', () => {
        expect(buildParam({ ...base, defaultValue: 'ABC' }, '').querySelector('input').value).toBe('ABC');
        expect(buildParam({ ...base, defaultValue: 'ABC' }, '?Var=XYZ').querySelector('input').value).toBe('XYZ');
    });

    test('textarea for multivalued with CRLF-joined array default', () => {
        const row = buildParam({
            ...base, control: 'textarea', isMultiValued: true, rows: 5,
            defaultValue: ['a', 'b']
        }, '');
        const ta = row.querySelector('textarea#Var');
        expect(ta.rows).toBe(5);
        // the DOM normalizes CRLF to LF when reading textarea values
        expect(ta.value).toBe('a\nb');
    });

    test('helpDetail rendered as help-block', () => {
        const row = buildParam({ ...base, helpDetail: 'More detail' }, '');
        expect(row.querySelector('p.help-block').textContent).toBe('More detail');
    });
});

describe('buildParam - select controls', () => {
    const base = {
        name: 'Var', type: 'string', control: 'select', labelOverride: null,
        helpMessage: '', helpDetail: '', isMandatory: false, isMultiValued: false,
        rows: null, allowedValues: ['Input', 'Output', 'Both'], defaultValue: null, validation: []
    };

    test('dropdown gets --Select-- placeholder with empty value', () => {
        const select = buildParam({ ...base }, '').querySelector('select#Var');
        expect(select.options[0].text).toBe('--Select--');
        expect(select.options[0].value).toBe('');
        expect(select.options[0].selected).toBe(true);
        expect(select.options.length).toBe(4);
    });

    test('dropdown default value selects matching option', () => {
        const select = buildParam({ ...base, defaultValue: 'Output' }, '').querySelector('select');
        expect(select.value).toBe('Output');
    });

    test('multiselect placeholder uses --Select-- value and only when optional', () => {
        const multi = buildParam({ ...base, control: 'multiselect', isMultiValued: true, rows: 3 }, '')
            .querySelector('select');
        expect(multi.multiple).toBe(true);
        expect(multi.size).toBe(3);
        expect(multi.options[0].value).toBe('--Select--');

        const mandatory = buildParam({
            ...base, control: 'multiselect', isMultiValued: true, rows: 3, isMandatory: true
        }, '').querySelector('select');
        expect(mandatory.options[0].value).toBe('Input');
    });

    test('multiselect array default selects matching options', () => {
        const multi = buildParam({
            ...base, control: 'multiselect', isMultiValued: true, rows: 3, defaultValue: ['Input', 'Both']
        }, '').querySelector('select');
        const selected = Array.from(multi.options).filter(o => o.selected).map(o => o.value);
        expect(selected).toEqual(['Input', 'Both']);
    });
});

describe('buildParam - checkbox', () => {
    const base = {
        name: 'Var', type: 'boolean', control: 'checkbox', labelOverride: null,
        helpMessage: '', helpDetail: '', isMandatory: false, isMultiValued: false,
        rows: null, allowedValues: null, defaultValue: null, validation: []
    };

    test('renders checkbox row with span label', () => {
        const row = buildParam({ ...base }, '');
        expect(row.className).toBe('checkbox');
        const input = row.querySelector('input#Var');
        expect(input.type).toBe('checkbox');
        expect(input.checked).toBe(false);
        expect(row.querySelector('span.form-label').textContent).toBe('Var');
    });

    test('boolean default true checks the box', () => {
        expect(buildParam({ ...base, defaultValue: true }, '').querySelector('input').checked).toBe(true);
    });

    test('query-string prefill overrides default', () => {
        expect(buildParam({ ...base, defaultValue: false }, '?Var=true').querySelector('input').checked).toBe(true);
    });
});

describe('buildParam - date controls', () => {
    const base = {
        name: 'Var', type: 'date', control: 'date', labelOverride: null,
        helpMessage: 'Pick one', helpDetail: '', isMandatory: false, isMultiValued: false,
        rows: null, allowedValues: null, defaultValue: null, validation: []
    };

    test('date control carries data-type=date and separate help-message', () => {
        const row = buildParam({ ...base }, '');
        expect(row.querySelector('input').getAttribute('data-type')).toBe('date');
        expect(row.querySelector('span.help-message').textContent).toBe('Pick one');
        expect(row.querySelector('label.form-label').textContent).toBe('Var');
    });

    test('datetime control carries data-type=datetime', () => {
        const row = buildParam({ ...base, control: 'datetime' }, '');
        expect(row.querySelector('input').getAttribute('data-type')).toBe('datetime');
    });
});

describe('buildParam - validation attributes', () => {
    test('range on multivalued becomes rangeMultiline', () => {
        const row = buildParam({
            name: 'Var', type: 'int', control: 'textarea', isMultiValued: true, rows: 5,
            helpMessage: '', helpDetail: '', isMandatory: false, labelOverride: null,
            allowedValues: null, defaultValue: null,
            validation: [{ type: 'range', min: 1, max: 10, valueType: 'integer' }]
        }, '');
        const val = row.querySelector('span.valmsg');
        expect(val.getAttribute('data-valtype')).toBe('rangeMultiline');
        expect(val.getAttribute('data-min')).toBe('1');
        expect(val.getAttribute('data-max')).toBe('10');
        expect(val.getAttribute('data-type')).toBe('integer');
    });

    test('mandatory checkbox rule message names the parameter', () => {
        const row = buildParam({
            name: 'Agree', type: 'boolean', control: 'checkbox', isMultiValued: false,
            helpMessage: '', helpDetail: '', isMandatory: true, labelOverride: null,
            allowedValues: null, defaultValue: null,
            validation: [{ type: 'mandatoryCheckbox' }]
        }, '');
        const val = row.querySelector('span.valmsg');
        expect(val.getAttribute('data-valtype')).toBe('mandatoryCheckbox');
        expect(val.textContent).toBe('You must check the box for Agree.');
    });
});

describe('buildVerboseControl', () => {
    test('renders the chkWebJEAVerbose checkbox', () => {
        const row = buildVerboseControl();
        expect(row.className).toBe('checkbox verbose-control');
        const input = row.querySelector('input#chkWebJEAVerbose');
        expect(input.type).toBe('checkbox');
        expect(input.checked).toBe(false);
        expect(row.querySelector('span.form-label').textContent).toBe('Verbose');
    });
});
