// api.js
// Shared fetch wrapper for /api/* calls, and the alerts the pages show when one fails.
// Every failure becomes an ApiError whose kind picks the message:
//   network  - the request never got an answer (offline, server down, GET timeout)
//   session  - 401, or a redirect to the sign-in page (Entra)
//   noaccess - 403: the command doesn't exist or the user isn't permitted on it
//   server   - anything else, including non-JSON bodies
// "noaccess" is also what the pages show for an empty menu, so "nothing configured",
// "not permitted" and "doesn't exist" all read the same. The server log says which.

(function (global) {
    'use strict';

    var GET_TIMEOUT_MS = 30000;

    var DEFAULT_NOACCESS_HELP = 'Contact your WebJEA administrator if you need access.';

    var MESSAGES = {
        network: { title: "Can't reach WebJEA.", text: 'Check your connection and try again.' },
        session: { title: 'Please sign in again.', text: 'Your session ended. Reload the page to sign in again.' },
        noaccess: { title: 'Not available.', text: "Either no matching commands exist or you don't have permission to use them." },
        accesschanged: { title: 'Your access has changed.', text: 'You no longer have permission to run this command. Reload the page to see the commands available to you.' },
        unverified: { title: "Couldn't verify your sign-in.", text: "WebJEA couldn't reach Microsoft Entra to confirm your access. Try again in a few minutes. If it keeps happening, give your WebJEA administrator the reference ID below." },
        server: { title: 'Something went wrong.', text: 'If it keeps happening, give your WebJEA administrator the reference ID below.' }
    };

    // NoAccessHelpHtml from webjea.json, delivered by /api/config. Empty means the default line.
    var noAccessHelpHtml = '';

    function ApiError(kind, status, message, body) {
        this.name = 'ApiError';
        this.kind = kind;
        this.status = status;
        this.message = message;
        this.body = body;
        this.requestId = body && body.requestId ? String(body.requestId) : '';
    }
    ApiError.prototype = Object.create(Error.prototype);
    ApiError.prototype.constructor = ApiError;

    // Only for JSON bodies from WebJEA itself: a proxy's HTML 503 stays 'server'.
    function kindForStatus(status, data) {
        if (status === 401) return 'session';
        if (status === 403) return data && data.reason === 'accesschanged' ? 'accesschanged' : 'noaccess';
        if (status === 503) return 'unverified';
        return 'server';
    }

    // undefined when the body isn't JSON (error.html, an IIS error page, an empty 401).
    function parseBody(response) {
        var contentType = (response.headers && response.headers.get('Content-Type')) || '';
        if (contentType.toLowerCase().indexOf('json') === -1) return Promise.resolve(undefined);
        return response.json().catch(function () { return undefined; });
    }

    function handleResponse(response) {
        // redirect:'manual' turns the sign-in redirect into an opaque response instead of
        // a cross-origin fetch that fails like a network error.
        if (response.type === 'opaqueredirect') {
            throw new ApiError('session', 0, 'Redirected to sign-in');
        }

        return parseBody(response).then(function (data) {
            if (data === undefined) {
                var kind = response.status === 401 ? 'session' : 'server';
                throw new ApiError(kind, response.status, 'Unexpected response (HTTP ' + response.status + ')');
            }

            if (!response.ok) {
                var message = data.error || data.statusmessage || ('Request failed: ' + response.status);
                throw new ApiError(kindForStatus(response.status, data), response.status, message, data);
            }

            return data;
        });
    }

    // Resolves with the parsed JSON body of a 2xx response; rejects with an ApiError
    // otherwise. A rejected 4xx/5xx keeps its JSON body on err.body.
    function request(url, options) {
        options = options || {};
        var method = (options.method || 'GET').toUpperCase();
        var init = {
            method: method,
            headers: options.headers,
            body: options.body,
            credentials: 'same-origin',
            redirect: 'manual'
        };

        // No timeout for POST: /api/execute runs scripts that can take a long time.
        var timer = null;
        if (method === 'GET' && typeof AbortController !== 'undefined') {
            var controller = new AbortController();
            init.signal = controller.signal;
            timer = setTimeout(function () { controller.abort(); }, options.timeout || GET_TIMEOUT_MS);
        }

        var pending;
        try {
            pending = Promise.resolve(global.fetch(url, init));
        } catch (e) {
            pending = Promise.reject(e);
        }

        return pending
            .then(handleResponse, function (err) {
                var timedOut = err && err.name === 'AbortError';
                throw new ApiError('network', 0, timedOut ? 'Request timed out' : String(err && err.message || err));
            })
            .then(function (data) {
                if (timer) clearTimeout(timer);
                return data;
            }, function (err) {
                if (timer) clearTimeout(timer);
                throw err;
            });
    }

    // ── Alerts ───────────────────────────────────────────────────────────────

    function sanitizeHtml(html) {
        if (typeof global.DOMPurify !== 'undefined') return global.DOMPurify.sanitize(html);
        return null;
    }

    function fillNoAccessHelp(span) {
        var safe = noAccessHelpHtml ? sanitizeHtml(noAccessHelpHtml) : null;
        if (safe) {
            span.innerHTML = safe;
        } else {
            span.textContent = DEFAULT_NOACCESS_HELP;
        }
    }

    function setNoAccessHelp(html) {
        noAccessHelpHtml = html ? String(html) : '';
        // /api/config and /api/menu load in parallel; update alerts that rendered first.
        var spans = document.querySelectorAll('.webjea-noaccess-help');
        for (var i = 0; i < spans.length; i++) fillNoAccessHelp(spans[i]);
    }

    function kindOf(err) {
        if (typeof err === 'string') return MESSAGES[err] ? err : 'server';
        return err && MESSAGES[err.kind] ? err.kind : 'server';
    }

    function button(label, onClick) {
        var btn = document.createElement('button');
        btn.type = 'button';
        btn.className = 'btn btn-sm btn-outline-secondary me-2';
        btn.textContent = label;
        btn.addEventListener('click', onClick);
        return btn;
    }

    // Builds the alert for an ApiError (or a kind name). opts.retry adds a "Try again"
    // button for network/server errors; opts.dashboardLink adds a link back to the dashboard.
    function buildAlert(err, opts) {
        opts = opts || {};
        var kind = kindOf(err);
        var msg = MESSAGES[kind];

        var div = document.createElement('div');
        var warning = kind === 'noaccess' || kind === 'session' || kind === 'accesschanged';
        div.className = 'alert webjea-alert ' + (warning ? 'alert-warning' : 'alert-danger');
        div.setAttribute('role', 'alert');
        div.setAttribute('data-kind', kind);

        var title = document.createElement('strong');
        title.textContent = msg.title;
        div.appendChild(title);
        div.appendChild(document.createTextNode(' ' + msg.text));

        if (kind === 'noaccess') {
            var help = document.createElement('span');
            help.className = 'webjea-noaccess-help';
            fillNoAccessHelp(help);
            div.appendChild(document.createTextNode(' '));
            div.appendChild(help);
        }

        // Not shown for noaccess: an empty menu has no request ID, and the two must look alike.
        if ((kind === 'server' || kind === 'unverified') && err && err.requestId) {
            var ref = document.createElement('div');
            ref.className = 'webjea-alert-ref small mt-1';
            ref.textContent = 'Reference: ' + err.requestId;
            div.appendChild(ref);
        }

        var actions = document.createElement('div');
        if (kind === 'session' || kind === 'accesschanged') {
            actions.appendChild(button('Reload', function () { global.location.reload(); }));
        } else if ((kind === 'network' || kind === 'server' || kind === 'unverified') && typeof opts.retry === 'function') {
            actions.appendChild(button('Try again', opts.retry));
        }
        if (opts.dashboardLink) {
            var link = document.createElement('a');
            link.href = './';
            link.className = 'alert-link';
            link.textContent = 'Return to the dashboard';
            actions.appendChild(link);
        }
        if (actions.childNodes.length > 0) {
            actions.className = 'mt-2';
            div.appendChild(actions);
        }

        return div;
    }

    // Replaces the container's contents with the alert.
    function renderAlert(container, err, opts) {
        if (!container) return null;
        var alert = buildAlert(err, opts);
        container.textContent = '';
        container.appendChild(alert);

        // An in-place alert supersedes a page-level banner of the same kind.
        var region = document.getElementById('divApiAlert');
        if (region && region !== container) {
            var dupes = region.querySelectorAll('.webjea-alert[data-kind="' + alert.getAttribute('data-kind') + '"]');
            for (var i = 0; i < dupes.length; i++) dupes[i].parentNode.removeChild(dupes[i]);
        }
        return alert;
    }

    // Adds a banner to #divApiAlert, unless the page already shows an alert of that kind
    // (a config failure and a menu failure on the same page show one message, not two).
    function pageAlert(err, opts) {
        var region = document.getElementById('divApiAlert');
        if (!region) return null;
        var kind = kindOf(err);
        if (document.querySelector('.webjea-alert[data-kind="' + kind + '"]')) return null;
        var alert = buildAlert(err, opts);
        region.appendChild(alert);
        return alert;
    }

    function showLoading(container) {
        if (!container) return;
        container.innerHTML = '<div class="webjea-loading"><div class="spinner-border spinner-border-sm text-secondary" role="status">'
            + '<span class="visually-hidden">Loading...</span></div></div>';
    }

    global.webjeaApi = {
        MESSAGES: MESSAGES,
        DEFAULT_NOACCESS_HELP: DEFAULT_NOACCESS_HELP,
        ApiError: ApiError,
        request: request,
        renderAlert: renderAlert,
        pageAlert: pageAlert,
        setNoAccessHelp: setNoAccessHelp,
        showLoading: showLoading
    };

    // Expose for Node.js unit testing.
    if (typeof module !== 'undefined' && module.exports) {
        module.exports = global.webjeaApi;
    }

}(typeof window !== 'undefined' ? window : {}));
