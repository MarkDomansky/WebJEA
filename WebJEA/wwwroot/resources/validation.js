// validation.js
// Client-side validation for the dynamically rendered form. form-renderer.js emits
// hidden <span class="valmsg" data-valtype="..." data-control="..."> elements next to
// each control; webjeaValidate() evaluates every rule, toggles the message visibility,
// and returns overall validity. Mirrors the old ASP.NET validator semantics: only
// "required" rules reject empty values — all other rules pass on empty input.

(function (global) {
    'use strict';

    function requiredValid(control) {
        if (control.tagName && control.tagName.toUpperCase() === 'SELECT') {
            return control.value !== '' && control.value !== '--Select--';
        }

        return String(control.value || '').trim() !== '';
    }

    function lengthValid(value, min, max) {
        if (value === '') return true;
        // same expression the old RegularExpressionValidator used: [\S\s]{min,max}
        var match = value.match(new RegExp('[\\S\\s]{' + min + ',' + max + '}'));
        return match !== null && match[0] === value;
    }

    function patternValid(value, pattern) {
        if (value === '') return true;
        var match;
        try {
            match = value.match(new RegExp(pattern));
        } catch (e) {
            return true; // unparseable pattern: server-side validation still applies
        }

        return match !== null && match.index === 0 && match[0] === value;
    }

    function rangeValid(value, min, max, isFloat) {
        if (value === '') return true;
        var val = isFloat ? parseFloat(value) : parseInt(value, 10);
        return !isNaN(val) && val >= min && val <= max;
    }

    // Each non-blank line of a textarea is validated independently against the range.
    function rangeMultilineValid(value, min, max, isFloat) {
        var lines = String(value).split('\n');
        for (var i = 0; i < lines.length; i++) {
            var trimmed = lines[i].trim();
            if (trimmed === '') continue; // skip blank lines
            var val = isFloat ? parseFloat(trimmed) : parseInt(trimmed, 10);
            if (isNaN(val) || val < min || val > max) return false;
        }

        return true;
    }

    // Counts selected options (ignoring the --Select-- placeholder) or non-blank lines.
    function countSelectedItems(control) {
        var count = 0;
        if (control.tagName.toUpperCase() === 'SELECT') {
            var options = control.options;
            for (var i = 0; i < options.length; i++) {
                if (options[i].selected === true && options[i].value !== '--Select--') {
                    count++;
                }
            }
        } else {
            var lines = String(control.value || '').split('\n');
            for (var j = 0; j < lines.length; j++) {
                if (lines[j].trim() !== '') count++;
            }
        }

        return count;
    }

    function evaluateValidator(validator, doc) {
        var d = doc || document;
        var type = validator.getAttribute('data-valtype');
        var control = d.getElementById(validator.getAttribute('data-control'));
        if (!control) return true;

        var min = parseFloat(validator.getAttribute('data-min'));
        var max = parseFloat(validator.getAttribute('data-max'));
        var isFloat = validator.getAttribute('data-type') === 'float';
        var value = String(control.value || '');

        switch (type) {
            case 'required':
                return requiredValid(control);
            case 'mandatoryCheckbox':
                return control.checked === true;
            case 'length':
                return lengthValid(value, validator.getAttribute('data-min'), validator.getAttribute('data-max'));
            case 'pattern':
                return patternValid(value, validator.getAttribute('data-pattern'));
            case 'range':
                return rangeValid(value, min, max, isFloat);
            case 'rangeMultiline':
                return rangeMultilineValid(value, min, max, isFloat);
            case 'count':
                var items = countSelectedItems(control);
                return items >= min && items <= max;
            default:
                return true;
        }
    }

    // Called by webjeaSubmit (PSWebParser.js) before executing the command.
    global.webjeaValidate = function () {
        var valid = true;
        var validators = document.querySelectorAll('span.valmsg[data-valtype]');
        for (var i = 0; i < validators.length; i++) {
            var ok = evaluateValidator(validators[i]);
            validators[i].style.display = ok ? 'none' : '';
            if (!ok) valid = false;
        }

        return valid;
    };

    // Expose rule evaluators for Node.js unit testing.
    if (typeof module !== 'undefined' && module.exports) {
        module.exports = {
            requiredValid: requiredValid,
            lengthValid: lengthValid,
            patternValid: patternValid,
            rangeValid: rangeValid,
            rangeMultilineValid: rangeMultilineValid,
            countSelectedItems: countSelectedItems,
            evaluateValidator: evaluateValidator
        };
    }

}(typeof window !== 'undefined' ? window : {}));
