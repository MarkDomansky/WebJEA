/**
 * @jest-environment jsdom
 */
// Tests for the client-side validation rules (validation.js). Semantics mirror the old
// ASP.NET validators: only "required" rejects empty values.

const {
    requiredValid, lengthValid, patternValid, rangeValid,
    rangeMultilineValid, countSelectedItems, evaluateValidator
} = require('../../WebJEA/wwwroot/resources/validation.js');

describe('requiredValid', () => {
    test('rejects empty and whitespace text input', () => {
        expect(requiredValid({ tagName: 'INPUT', value: '' })).toBe(false);
        expect(requiredValid({ tagName: 'INPUT', value: '   ' })).toBe(false);
        expect(requiredValid({ tagName: 'INPUT', value: 'x' })).toBe(true);
    });

    test('rejects --Select-- and empty select values', () => {
        expect(requiredValid({ tagName: 'SELECT', value: '' })).toBe(false);
        expect(requiredValid({ tagName: 'SELECT', value: '--Select--' })).toBe(false);
        expect(requiredValid({ tagName: 'SELECT', value: 'Input' })).toBe(true);
    });
});

describe('lengthValid', () => {
    test('passes empty values (non-required semantics)', () => {
        expect(lengthValid('', 3, 5)).toBe(true);
    });

    test('enforces min and max', () => {
        expect(lengthValid('ab', 3, 5)).toBe(false);
        expect(lengthValid('abc', 3, 5)).toBe(true);
        expect(lengthValid('abcde', 3, 5)).toBe(true);
        expect(lengthValid('abcdef', 3, 5)).toBe(false);
    });

    test('counts whitespace and newlines like [\\S\\s]', () => {
        expect(lengthValid('a b', 3, 5)).toBe(true);
        expect(lengthValid('a\nb', 3, 5)).toBe(true);
    });
});

describe('patternValid', () => {
    test('passes empty values', () => {
        expect(patternValid('', '^[a-z]+$')).toBe(true);
    });

    test('requires full match like RegularExpressionValidator', () => {
        expect(patternValid('abc', '[a-z]+')).toBe(true);
        expect(patternValid('abc1', '[a-z]+')).toBe(false);
        expect(patternValid('1abc', '[a-z]+')).toBe(false);
    });
});

describe('rangeValid', () => {
    test('passes empty values', () => {
        expect(rangeValid('', 1, 10, false)).toBe(true);
    });

    test('validates integers', () => {
        expect(rangeValid('5', 1, 10, false)).toBe(true);
        expect(rangeValid('0', 1, 10, false)).toBe(false);
        expect(rangeValid('11', 1, 10, false)).toBe(false);
        expect(rangeValid('abc', 1, 10, false)).toBe(false);
    });

    test('validates floats', () => {
        expect(rangeValid('1.5', 1, 2, true)).toBe(true);
        expect(rangeValid('2.5', 1, 2, true)).toBe(false);
    });
});

describe('rangeMultilineValid', () => {
    test('validates each non-blank line', () => {
        expect(rangeMultilineValid('1\n5\n10', 1, 10, false)).toBe(true);
        expect(rangeMultilineValid('1\n\n10', 1, 10, false)).toBe(true);
        expect(rangeMultilineValid('1\n11', 1, 10, false)).toBe(false);
        expect(rangeMultilineValid('1\nabc', 1, 10, false)).toBe(false);
    });
});

describe('countSelectedItems', () => {
    test('counts selected options ignoring the --Select-- placeholder', () => {
        document.body.innerHTML =
            '<select id="s" multiple>' +
            '<option value="--Select--" selected>--Select--</option>' +
            '<option value="a" selected>a</option>' +
            '<option value="b">b</option>' +
            '<option value="c" selected>c</option>' +
            '</select>';
        expect(countSelectedItems(document.getElementById('s'))).toBe(2);
    });

    test('counts non-blank textarea lines', () => {
        document.body.innerHTML = '<textarea id="t"></textarea>';
        const ta = document.getElementById('t');
        ta.value = 'one\n\n two \n';
        expect(countSelectedItems(ta)).toBe(2);
    });
});

describe('evaluateValidator', () => {
    test('required validator against rendered DOM', () => {
        document.body.innerHTML =
            '<input id="Var" value="" />' +
            '<span id="v" class="valmsg" data-valtype="required" data-control="Var"></span>';
        const validator = document.getElementById('v');
        expect(evaluateValidator(validator, document)).toBe(false);
        document.getElementById('Var').value = 'hello';
        expect(evaluateValidator(validator, document)).toBe(true);
    });

    test('mandatoryCheckbox validator', () => {
        document.body.innerHTML =
            '<input type="checkbox" id="Agree" />' +
            '<span id="v" class="valmsg" data-valtype="mandatoryCheckbox" data-control="Agree"></span>';
        const validator = document.getElementById('v');
        expect(evaluateValidator(validator, document)).toBe(false);
        document.getElementById('Agree').checked = true;
        expect(evaluateValidator(validator, document)).toBe(true);
    });

    test('missing control passes', () => {
        document.body.innerHTML =
            '<span id="v" class="valmsg" data-valtype="required" data-control="Nope"></span>';
        expect(evaluateValidator(document.getElementById('v'), document)).toBe(true);
    });
});
