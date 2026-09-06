// form-renderer.js
// Client-side equivalent of ControlBuilder.vb: fetches /api/command/{cmdid} and renders
// the input form. DOM structure and class names match what ControlBuilder generated so
// the existing CSS and PSWebParser.js keep working unchanged.

(function (global) {
    'use strict';

    function getSearch() {
        return (global.location && global.location.search) || '';
    }

    // Request.QueryString equivalent: case-insensitive lookup of a query parameter.
    function queryValue(name, search) {
        var qs = new URLSearchParams(search !== undefined ? search : getSearch());
        var found = null;
        qs.forEach(function (value, key) {
            if (found === null && key.toLowerCase() === name.toLowerCase()) found = value;
        });
        return found;
    }

    // ReadGetPost equivalent: the query string overrides the parsed default value.
    function prefillValue(name, defaultValue, search) {
        var qv = queryValue(name, search);
        return qv !== null ? qv : defaultValue;
    }

    function parseBoolean(value) {
        if (value === true || value === false) return value;
        if (value === null || value === undefined) return null;
        var lower = String(value).trim().toLowerCase();
        if (lower === 'true' || lower === '1') return true;
        if (lower === 'false' || lower === '0') return false;
        return null;
    }

    function createEl(tag, className, text) {
        var el = document.createElement(tag);
        if (className) el.className = className;
        if (text) el.textContent = text;
        return el;
    }

    function buildLabel(text, forId) {
        var label = createEl('label', 'form-label', text);
        label.htmlFor = forId;
        return label;
    }

    function addRequired(parent) {
        parent.appendChild(createEl('span', 'reqopt', 'Required'));
    }

    function addHelpMessage(parent, message) {
        parent.appendChild(createEl('span', 'help-message', message));
    }

    function addHelpDetail(parent, detail) {
        parent.appendChild(createEl('p', 'help-block', detail));
    }

    // Emits the hidden .valmsg spans consumed by validation.js (webjeaValidate).
    function addValidators(row, param) {
        (param.validation || []).forEach(function (rule) {
            var msg;
            var val = createEl('span', 'valmsg');
            val.style.display = 'none';
            val.setAttribute('data-control', param.name);

            switch (rule.type) {
                case 'required':
                    val.setAttribute('data-valtype', 'required');
                    msg = 'Required Field';
                    break;
                case 'mandatoryCheckbox':
                    val.setAttribute('data-valtype', 'mandatoryCheckbox');
                    msg = 'You must check the box for ' + param.name + '.';
                    break;
                case 'length':
                    val.setAttribute('data-valtype', 'length');
                    val.setAttribute('data-min', rule.min);
                    val.setAttribute('data-max', rule.max);
                    msg = 'Not in allowed length (' + rule.min + '-' + rule.max + ')';
                    break;
                case 'pattern':
                    val.setAttribute('data-valtype', 'pattern');
                    val.setAttribute('data-pattern', rule.pattern);
                    msg = 'Did not match pattern: ' + rule.pattern;
                    break;
                case 'range':
                    if (param.isMultiValued) {
                        val.setAttribute('data-valtype', 'rangeMultiline');
                        msg = 'Each value must be in allowed range (' + rule.min + '-' + rule.max + ')';
                    } else {
                        val.setAttribute('data-valtype', 'range');
                        msg = 'Not in allowed range (' + rule.min + '-' + rule.max + ')';
                    }

                    val.setAttribute('data-min', rule.min);
                    val.setAttribute('data-max', rule.max);
                    val.setAttribute('data-type', rule.valueType === 'float' ? 'float' : 'integer');
                    break;
                case 'count':
                    val.setAttribute('data-valtype', 'count');
                    val.setAttribute('data-min', rule.min);
                    val.setAttribute('data-max', rule.max);
                    msg = 'Number of selected items not in allowed range (' + rule.min + '-' + rule.max + ')';
                    break;
                default:
                    return;
            }

            val.textContent = msg;
            row.appendChild(val);
        });
    }

    function buildText(param, search) {
        var row = createEl('div', 'form-group');

        var labelText = param.labelOverride || param.name;
        row.appendChild(buildLabel(labelText, param.name));
        if (param.isMandatory) addRequired(row);

        var control;
        if (param.control === 'textarea') {
            control = document.createElement('textarea');
            control.rows = param.rows || 5;
            control.cols = 100;
        } else {
            control = document.createElement('input');
            control.type = 'text';
        }

        control.id = param.name;
        control.className = 'form-control';

        var text = '';
        if (param.isMultiValued && Array.isArray(param.defaultValue)) {
            text = param.defaultValue.join('\r\n');
        } else if (param.defaultValue !== null && param.defaultValue !== undefined) {
            text = String(param.defaultValue);
        }

        control.value = prefillValue(param.name, text, search) || '';

        row.appendChild(control);
        if (param.helpDetail) addHelpDetail(row, param.helpDetail);
        addValidators(row, param);
        return row;
    }

    function buildDate(param, search) {
        var row = createEl('div', 'form-group');

        row.appendChild(buildLabel(param.name, param.name));
        if (param.helpMessage) addHelpMessage(row, param.helpMessage);
        if (param.isMandatory) addRequired(row);

        var control = document.createElement('input');
        control.type = 'text';
        control.id = param.name;
        control.className = 'form-control';
        control.setAttribute('data-type', param.control === 'datetime' ? 'datetime' : 'date');

        var text = param.defaultValue !== null && param.defaultValue !== undefined ? String(param.defaultValue) : '';
        control.value = prefillValue(param.name, text, search) || '';

        row.appendChild(control);
        if (param.helpDetail) addHelpDetail(row, param.helpDetail);
        addValidators(row, param);
        return row;
    }

    function buildSelect(param, search) {
        var row = createEl('div', 'form-group');

        row.appendChild(buildLabel(param.name, param.name));
        if (param.helpMessage) addHelpMessage(row, param.helpMessage);
        if (param.isMandatory) addRequired(row);

        var control = document.createElement('select');
        control.id = param.name;
        control.className = 'form-control';

        var defval = null;
        if (param.defaultValue !== null && param.defaultValue !== undefined && param.defaultValue !== '') {
            defval = String(param.defaultValue);
        }

        defval = prefillValue(param.name, defval, search);

        var placeholder = document.createElement('option');
        placeholder.text = '--Select--';
        placeholder.value = '';
        if (defval === null) placeholder.selected = true;
        control.appendChild(placeholder);

        (param.allowedValues || []).forEach(function (allowedval) {
            var opt = document.createElement('option');
            opt.value = allowedval;
            opt.text = allowedval;
            if (allowedval === defval) opt.selected = true;
            control.appendChild(opt);
        });

        row.appendChild(control);
        if (param.helpDetail) addHelpDetail(row, param.helpDetail);
        addValidators(row, param);
        return row;
    }

    function buildMultiselect(param, search) {
        var row = createEl('div', 'form-group');

        row.appendChild(buildLabel(param.name, param.name));
        if (param.helpMessage) addHelpMessage(row, param.helpMessage);
        if (param.isMandatory) addRequired(row);

        var control = document.createElement('select');
        control.id = param.name;
        control.className = 'form-control';
        control.multiple = true;
        control.size = param.rows || 5;

        var defval = Array.isArray(param.defaultValue) ? param.defaultValue.slice() : [];
        var postget = queryValue(param.name, search);
        if (postget !== null && postget !== '') {
            defval = postget.split('\r\n').filter(function (v) { return v !== ''; });
        }

        if (!param.isMandatory) {
            var placeholder = document.createElement('option');
            placeholder.text = '--Select--';
            placeholder.value = '--Select--';
            if (param.defaultValue === null || param.defaultValue === undefined) placeholder.selected = true;
            control.appendChild(placeholder);
        }

        (param.allowedValues || []).forEach(function (allowedval) {
            var opt = document.createElement('option');
            opt.value = allowedval;
            opt.text = allowedval;
            if (defval.indexOf(allowedval) > -1) opt.selected = true;
            control.appendChild(opt);
        });

        row.appendChild(control);
        if (param.helpDetail) addHelpDetail(row, param.helpDetail);
        addValidators(row, param);
        return row;
    }

    function buildCheckbox(param, search) {
        var row = createEl('div', 'checkbox');

        var label = document.createElement('label');

        var control = document.createElement('input');
        control.type = 'checkbox';
        control.id = param.name;

        var checked = parseBoolean(param.defaultValue);
        if (checked !== null) control.checked = checked;

        var qv = queryValue(param.name, search);
        if (qv !== null) {
            var qb = parseBoolean(qv);
            if (qb !== null) control.checked = qb;
        }

        label.appendChild(control);
        label.appendChild(createEl('span', 'form-label', param.name));
        if (param.helpMessage) addHelpMessage(label, param.helpMessage);
        if (param.isMandatory) addRequired(label);

        row.appendChild(label);
        if (param.helpDetail) addHelpDetail(row, param.helpDetail);
        addValidators(row, param);
        return row;
    }

    function buildParam(param, search) {
        switch (param.control) {
            case 'select': return buildSelect(param, search);
            case 'multiselect': return buildMultiselect(param, search);
            case 'checkbox': return buildCheckbox(param, search);
            case 'date':
            case 'datetime': return buildDate(param, search);
            default: return buildText(param, search);
        }
    }

    function buildVerboseControl() {
        var row = createEl('div', 'checkbox verbose-control');
        var label = document.createElement('label');
        var control = document.createElement('input');
        control.type = 'checkbox';
        control.id = 'chkWebJEAVerbose';
        label.htmlFor = control.id;
        label.appendChild(control);
        label.appendChild(createEl('span', 'form-label', 'Verbose'));
        row.appendChild(label);
        return row;
    }

    function sanitizeHtml(html) {
        if (typeof DOMPurify !== 'undefined') {
            return DOMPurify.sanitize(html);
        }
        return '';
    }

    function setSanitizedHtml(id, html) {
        var el = document.getElementById(id);
        if (el) el.innerHTML = sanitizeHtml(html);
    }

    function show(id) {
        var el = document.getElementById(id);
        if (el) el.classList.remove('collapse');
    }

    function applyMetadata(metadata) {
        document.title = metadata.displayName + ' - ' + metadata.title + ' - WebJEA';

        var title = document.getElementById('lblCmdTitle');
        if (title) title.textContent = metadata.displayName;

        var hasSynopsis = !!metadata.synopsis;
        var hasDescription = !!metadata.description;
        if (hasSynopsis && hasDescription) {
            // Both present: show accordion
            show('SynopsisAndDescription');
            setSanitizedHtml('lblCmdSynopsis', metadata.synopsis);
            setSanitizedHtml('lblCmdDescription', metadata.description);
        } else if (hasSynopsis || hasDescription) {
            // Only one present: show simple div
            show('divSingleInfo');
            setSanitizedHtml('lblSingleInfo', hasSynopsis ? metadata.synopsis : metadata.description);
        }

        var panelOnload = document.getElementById('panelOnload');
        if (panelOnload) {
            if (metadata.hasOnload) {
                panelOnload.setAttribute('data-has-onload', 'true');
            } else {
                panelOnload.classList.add('collapse');
            }
        }

        if (!metadata.hasScript) {
            var panelInput = document.getElementById('panelInput');
            if (panelInput) panelInput.style.display = 'none';
        }

        var container = document.getElementById('divParameters');
        if (container) {
            container.textContent = '';
            (metadata.parameters || []).forEach(function (param) {
                container.appendChild(buildParam(param));
            });

            if (metadata.showVerbose) {
                container.appendChild(buildVerboseControl());
            }
        }

        // date/time pickers for the freshly rendered controls
        if (typeof global.jQuery !== 'undefined') {
            var $ = global.jQuery;
            if ($.fn.datepicker) $('*[data-type="date"]').datepicker({ dateFormat: 'yy/mm/dd' });
            if ($.fn.datetimepicker) $('*[data-type="datetime"]').datetimepicker({ dateFormat: 'yy/mm/dd' });
        }

        var btnRun = document.getElementById('btnRun');
        if (btnRun && typeof global.webjeaSubmit === 'function') {
            btnRun.addEventListener('click', function (e) {
                e.preventDefault();
                global.webjeaSubmit();
            });
        }

        global.webjeaCmdId = metadata.id;

        // fire the onload script now that the panels are wired up
        if (typeof global.webjeaInit === 'function') {
            global.webjeaInit();
        }
    }

    // Entry point, called by app.js on the command page.
    global.webjeaRenderForm = function (cmdid) {
        if (!cmdid) {
            global.location.replace('./');
            return;
        }

        fetch('api/command/' + encodeURIComponent(cmdid))
            .then(function (response) {
                if (response.status === 403) {
                    // parity with the old page: unauthorized commands bounce to the dashboard
                    global.location.replace('./');
                    return null;
                }

                if (!response.ok) throw new Error('Request failed: ' + response.status);
                return response.json();
            })
            .then(function (metadata) {
                if (metadata) applyMetadata(metadata);
            })
            .catch(function (err) {
                console.error('command metadata load failed', err);
                var body = document.getElementById('divCmdBody');
                if (body) body.textContent = 'Failed to load this command.';
            });
    };

    // Expose pure/DOM helper functions for Node.js unit testing.
    if (typeof module !== 'undefined' && module.exports) {
        module.exports = {
            queryValue: queryValue,
            prefillValue: prefillValue,
            parseBoolean: parseBoolean,
            buildParam: buildParam,
            buildVerboseControl: buildVerboseControl
        };
    }

}(typeof window !== 'undefined' ? window : {}));
