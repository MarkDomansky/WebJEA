// PSWebParser.js
// Client-side equivalent of OutputRenderer.vb.
// Parses the /api/execute JSON response and renders it safely to the page.

(function (global) {
    'use strict';

    // ── DOMPurify configuration ──────────────────────────────────────────────
    // Allowlist only the tags and attributes that this renderer intentionally
    // produces, so any unexpected markup is stripped before hitting the DOM.
    var PURIFY_CONFIG = {
        ALLOWED_TAGS: ['span', 'br', 'a', 'table', 'thead', 'tbody', 'tr', 'th', 'td', 'pre', 'img'],
        ALLOWED_ATTR: ['class', 'href', 'src', 'rel']
    };

    function purify(html) {
        if (typeof DOMPurify !== 'undefined') {
            return DOMPurify.sanitize(html, PURIFY_CONFIG);
        }
        // DOMPurify not loaded — return the already-escaped string as-is.
        // This path should never be reached in production.
        return html;
    }

    // ── XSS helpers ─────────────────────────────────────────────────────────

    function escapeHtml(str) {
        if (str === null || str === undefined) return '';
        return String(str)
            .replace(/&/g, '&amp;')
            .replace(/</g, '&lt;')
            .replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;')
            .replace(/'/g, '&#39;');
    }

    // Block dangerous URL schemes before injecting into href/src attributes.
    function sanitizeUrl(url) {
        if (!url) return '#';
        var trimmed = url.replace(/[\x00-\x1f\s]/g, '').toLowerCase();
        if (trimmed.startsWith('javascript:') || trimmed.startsWith('data:')) {
            return '#';
        }
        return url;
    }

    // Validate that a CSS class name contains only safe characters.
    function isSafeCssClass(cls) {
        return /^[a-zA-Z0-9_-]+$/.test(cls);
    }

    // ── Tag markup processing ────────────────────────────────────────────────
    // Applied AFTER escapeHtml, so the surrounding text is already safe.
    // Content captured from the already-escaped string must NOT be re-escaped.
    //
    // Processes tags right-to-left (last [[  first) so that nested inner tags
    // are resolved before the outer tag that contains them — matching the
    // behaviour of the original VB EncodeOutputTags implementation.

    function processTagMarkup(text) {
        var idx = text.lastIndexOf('[[');
        while (idx !== -1) {
            var slice = text.slice(idx);
            var match, replacement;

            // [[a|url|label]]
            match = slice.match(/^\[\[a\|(.+?)\|(.+?)\]\]/i);
            if (match) {
                // url/label are already HTML-escaped; only sanitize the URL scheme
                replacement = '<a href="' + sanitizeUrl(match[1]) + '" rel="noopener noreferrer">' + match[2] + '</a>';
                text = text.slice(0, idx) + replacement + text.slice(idx + match[0].length);
            } else {
                // [[span|cssclass|text]]
                match = slice.match(/^\[\[span\|(.+?)\|(.+?)\]\]/i);
                if (match) {
                    replacement = isSafeCssClass(match[1])
                        ? '<span class="' + match[1] + '">' + match[2] + '</span>'
                        : match[2];
                    text = text.slice(0, idx) + replacement + text.slice(idx + match[0].length);
                } else {
                    // [[img|cssclass|src]]
                    match = slice.match(/^\[\[img\|(.*?)\|(.+?)\]\]/i);
                    if (match) {
                        var safeClass = isSafeCssClass(match[1]) ? ' class="' + match[1] + '"' : '';
                        replacement = '<img' + safeClass + ' src="' + sanitizeUrl(match[2]) + '" />';
                        text = text.slice(0, idx) + replacement + text.slice(idx + match[0].length);
                    }
                }
            }

            idx = idx > 0 ? text.lastIndexOf('[[', idx - 1) : -1;
        }
        return text;
    }

    // ── Stream → HTML ────────────────────────────────────────────────────────

    var STREAM_MAP = {
        'debug':   { css: 'psdebug',   prefix: 'DEBUG: '   },
        'err':     { css: 'pserror',   prefix: ''           },
        'warn':    { css: 'pswarning', prefix: 'WARNING: ' },
        'info':    { css: 'psoutput',  prefix: ''           },
        'output':  { css: 'psoutput',  prefix: ''           },
        'verbose': { css: 'psverbose', prefix: 'VERBOSE: ' }
    };

    function renderMessage(stream, message) {
        var mapping = STREAM_MAP[stream] || { css: 'psoutput', prefix: '' };
        var safe = escapeHtml(mapping.prefix + message);
        safe = processTagMarkup(safe);
        return '<span class="' + mapping.css + '">' + safe + '</span><br/>';
    }

    // ── Structured output objects ────────────────────────────────────────────

    function isPlainObject(val) {
        return val !== null && typeof val === 'object' && !Array.isArray(val);
    }

    // Build an HTML table from an array of uniform objects.
    function buildTable(rows) {
        var keys = Object.keys(rows[0]);
        var html = '<table class="table table-sm table-bordered ps-output-table"><thead><tr>';
        keys.forEach(function (k) {
            html += '<th>' + escapeHtml(k) + '</th>';
        });
        html += '</tr></thead><tbody>';
        rows.forEach(function (row) {
            html += '<tr>';
            keys.forEach(function (k) {
                var cell = row[k] === null || row[k] === undefined ? '' : row[k];
                html += '<td>' + escapeHtml(String(cell)) + '</td>';
            });
            html += '</tr>';
        });
        html += '</tbody></table>';
        return html;
    }

    function renderOutputObjects(output) {
        if (output === null || output === undefined) return '';

        // Single primitive
        if (typeof output !== 'object') {
            return '<span class="psoutput">' + escapeHtml(String(output)) + '</span><br/>';
        }

        // Normalise to array
        var items = Array.isArray(output) ? output : [output];
        if (items.length === 0) return '';

        // Determine if all items are plain objects with the same keys
        var allObjects = items.every(isPlainObject);
        if (allObjects && items.length > 0) {
            var firstKeys = Object.keys(items[0]).join(',');
            var uniform = items.every(function (item) {
                return Object.keys(item).join(',') === firstKeys;
            });
            if (uniform) {
                return buildTable(items);
            }
        }

        // Mixed or non-uniform — render as formatted JSON in a pre block
        try {
            var json = JSON.stringify(output, null, 2);
            return '<pre class="ps-output-json">' + escapeHtml(json) + '</pre>';
        } catch (e) {
            return '<span class="pserror">Unable to render output.</span><br/>';
        }
    }

    // ── API response → DOM ──────────────────────────────────────────────────

    function renderApiResponse(containerEl, apiResponse) {
        var html = '';

        if (apiResponse.messages && Array.isArray(apiResponse.messages)) {
            apiResponse.messages.forEach(function (msg) {
                html += renderMessage(msg.stream, msg.message);
            });
        }

        if (apiResponse.output !== null && apiResponse.output !== undefined) {
            html += renderOutputObjects(apiResponse.output);
        }

        containerEl.innerHTML = purify(html);
    }

    // ── Form data collection ─────────────────────────────────────────────────

    function collectFormParams() {
        var params = {};
        var container = document.getElementById('divParameters');
        if (!container) return params;

        var inputs = container.querySelectorAll('input, select, textarea');
        inputs.forEach(function (el) {
            var id = el.id;
            // Skip elements with no id or ASP.NET internal controls (__VIEWSTATE etc.)
            if (!id || id.startsWith('__') || id === '') return;

            var paramName = id;

            if (el.tagName === 'INPUT' && el.type === 'checkbox') {
                params[paramName] = el.checked;
            } else if (el.tagName === 'SELECT' && el.multiple) {
                var selected = [];
                for (var i = 0; i < el.options.length; i++) {
                    if (el.options[i].selected && el.options[i].value !== '--Select--') {
                        selected.push(el.options[i].value);
                    }
                }
                if (selected.length > 0) {
                    params[paramName] = selected;
                }
            } else if (el.tagName === 'SELECT') {
                if (el.value && el.value !== '--Select--') {
                    params[paramName] = el.value;
                }
            } else if (el.tagName === 'TEXTAREA') {
                var lines = el.value.split('\n').filter(function (l) { return l.trim() !== ''; });
                if (lines.length > 1) {
                    params[paramName] = lines;
                } else if (lines.length === 1) {
                    params[paramName] = lines[0];
                }
            } else {
                if (el.value !== '') {
                    params[paramName] = el.value;
                }
            }
        });

        return params;
    }

    function collectVerbose() {
        var el = document.getElementById('chkWebJEAVerbose');
        return el ? el.checked : false;
    }

    // ── API call ─────────────────────────────────────────────────────────────

    function showLoader(el) {
        el.innerHTML = '<img src="resources/loader.svg" alt="Loading..." width="150" />';
    }

    function callApi(cmdid, params, runOnload, verbose, loaderEl, outputEl, completeCallback) {
        showLoader(loaderEl);

        var body = {
            cmdid: cmdid,
            runOnload: runOnload,
            verbose: verbose,
            parameters: params
        };

        fetch('api/execute', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(body)
        })
        .then(function (response) {
            return response.json().then(function (data) {
                return { status: response.status, data: data };
            });
        })
        .then(function (result) {
            renderApiResponse(outputEl, result.data);
        })
        .catch(function (err) {
            outputEl.innerHTML = purify('<span class="pserror">Request failed: ' + escapeHtml(String(err)) + '</span><br/>');
        })
        .finally(function () {
            try {
                if (typeof completeCallback === 'function') completeCallback();
            } catch (e) { }
        });
    }

    // ── Page entry points ────────────────────────────────────────────────────

    // Called from the Submit button's onclick — replaces disableOnPostback().
    // Returns false to prevent normal form postback.
    global.webjeaSubmit = function () {
        try {
            if (typeof webjeaValidate === 'function') {
                if (!webjeaValidate()) return false;
            } else if (typeof Page_ClientValidate === 'function' && !Page_ClientValidate()) {
                return false;
            }
        } catch (e) { /* validators not present */ }

        var cmdid = global.webjeaCmdId || '';
        var params = collectFormParams();
        var verbose = collectVerbose();

        // Find the submit button and disable it to prevent duplicate clicks.
        var submitBtn = document.getElementById('btnRun') || document.querySelector('#frmMain button[type="button"].btn-primary, #frmMain input[type="submit"], #frmMain button[type="submit"]');
        if (submitBtn) {
            try {
                submitBtn.disabled = true;
                submitBtn.classList.add('disabled');
            } catch (e) { }
        }

        var panelOutput = document.getElementById('panelOutput');
        var consoleOutput = document.getElementById('consoleOutput');

        if (panelOutput) panelOutput.classList.remove('collapse');
        if (consoleOutput) {
            window.location.hash = '#panelOutput';
            callApi(cmdid, params, false, verbose, consoleOutput, consoleOutput, function () {
                // Re-enable the submit button when the API call finishes (success or failure).
                if (submitBtn) {
                    try {
                        submitBtn.disabled = false;
                        submitBtn.classList.remove('disabled');
                    } catch (e) { }
                }
            });
        }

        return false;
    };

    // Called once the form has been rendered (or on DOMContentLoaded if still loading).
    // If panelOnload carries data-has-onload="true", fires the onload API call.
    global.webjeaInit = function () {
        var run = function () {
            var cmdid = global.webjeaCmdId || '';
            var panelOnload = document.getElementById('panelOnload');
            var consoleOnload = document.getElementById('consoleOnload');

            if (panelOnload && panelOnload.getAttribute('data-has-onload') === 'true' && consoleOnload) {
                // Collect query-string values as onload parameters
                var params = {};
                try {
                    var qs = new URLSearchParams(window.location.search);
                    qs.forEach(function (value, key) {
                        var lower = key.toLowerCase();
                        if (lower !== 'cmdid') {
                            params[key] = value;
                        }
                    });
                } catch (e) { /* URLSearchParams not supported */ }

                callApi(cmdid, params, true, false, consoleOnload, consoleOnload);
            }
        };

        if (document.readyState === 'loading') {
            document.addEventListener('DOMContentLoaded', run);
        } else {
            run();
        }
    };

    // Expose pure helper functions for Node.js unit testing.
    if (typeof module !== 'undefined' && module.exports) {
        module.exports = {
            escapeHtml: escapeHtml,
            sanitizeUrl: sanitizeUrl,
            isSafeCssClass: isSafeCssClass,
            processTagMarkup: processTagMarkup
        };
    }

}(typeof window !== 'undefined' ? window : {}));

