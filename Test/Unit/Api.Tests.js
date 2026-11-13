/**
 * @jest-environment jsdom
 */
// Tests for api.js: how API failures are classified and the alerts the pages show.

window.DOMPurify = require('../../WebJEA/wwwroot/resources/purify.min.js');
const api = require('../../WebJEA/wwwroot/resources/api.js');
const { renderExecuteError } = require('../../WebJEA/wwwroot/resources/PSWebParser.js');

function mockResponse(status, body, contentType) {
    const isJson = body !== undefined && typeof body !== 'string';
    return {
        ok: status >= 200 && status < 300,
        status: status,
        type: 'basic',
        headers: { get: () => contentType || (isJson ? 'application/json; charset=utf-8' : 'text/html') },
        json: () => (isJson ? Promise.resolve(body) : Promise.reject(new SyntaxError("Unexpected token '<'")))
    };
}

function respondWith(response) {
    window.fetch = jest.fn(() => Promise.resolve(response));
}

async function rejection(promise) {
    try {
        await promise;
    } catch (err) {
        return err;
    }
    throw new Error('expected the request to fail');
}

afterEach(() => {
    document.body.innerHTML = '';
    api.setNoAccessHelp('');
});

// ── request ───────────────────────────────────────────────────────────────────

describe('request', () => {
    test('resolves with the JSON body of a 2xx response', async () => {
        respondWith(mockResponse(200, [{ id: 'one' }]));

        await expect(api.request('api/menu')).resolves.toEqual([{ id: 'one' }]);
    });

    test('resolves with an empty menu (the page decides what that means)', async () => {
        respondWith(mockResponse(200, []));

        await expect(api.request('api/menu')).resolves.toEqual([]);
    });

    test('asks fetch not to follow redirects and to send credentials', async () => {
        respondWith(mockResponse(200, {}));

        await api.request('api/config');

        const init = window.fetch.mock.calls[0][1];
        expect(init.redirect).toBe('manual');
        expect(init.credentials).toBe('same-origin');
        expect(init.signal).toBeDefined();
    });

    test('POST requests have no timeout', async () => {
        respondWith(mockResponse(200, {}));

        await api.request('api/execute', { method: 'POST', body: '{}' });

        expect(window.fetch.mock.calls[0][1].signal).toBeUndefined();
    });

    test('a fetch TypeError is a network error', async () => {
        window.fetch = jest.fn(() => Promise.reject(new TypeError('Failed to fetch')));

        const err = await rejection(api.request('api/menu'));

        expect(err).toBeInstanceOf(api.ApiError);
        expect(err.kind).toBe('network');
    });

    test('a GET that times out is a network error', async () => {
        window.fetch = jest.fn((url, init) => new Promise((resolve, reject) => {
            init.signal.addEventListener('abort', () => {
                const abort = new Error('aborted');
                abort.name = 'AbortError';
                reject(abort);
            });
        }));

        const err = await rejection(api.request('api/menu', { timeout: 5 }));

        expect(err.kind).toBe('network');
        expect(err.message).toBe('Request timed out');
    });

    test('a redirect (Entra sign-in) is an expired session', async () => {
        respondWith({ ok: false, status: 0, type: 'opaqueredirect', headers: { get: () => null } });

        const err = await rejection(api.request('api/menu'));

        expect(err.kind).toBe('session');
    });

    test.each([
        [401, { error: 'x' }],
        [401, ''] // empty Negotiate challenge
    ])('HTTP %p is an expired session', async (status, body) => {
        respondWith(mockResponse(status, body));

        const err = await rejection(api.request('api/menu'));

        expect(err.kind).toBe('session');
    });

    test('403 is noaccess and keeps the body', async () => {
        const body = { error: 'Not available.', requestId: 'req-403' };
        respondWith(mockResponse(403, body));

        const err = await rejection(api.request('api/command/x'));

        expect(err.kind).toBe('noaccess');
        expect(err.status).toBe(403);
        expect(err.body).toEqual(body);
    });

    test('403 with reason accesschanged is accesschanged', async () => {
        respondWith(mockResponse(403, { status: 403, statusmessage: 'Your access has changed.', reason: 'accesschanged', messages: [] }));

        const err = await rejection(api.request('api/execute', { method: 'POST' }));

        expect(err.kind).toBe('accesschanged');
    });

    test('503 JSON from WebJEA is unverified, with the request ID', async () => {
        respondWith(mockResponse(503, { status: 503, statusmessage: "Couldn't verify your sign-in.", requestId: 'req-503', messages: [] }));

        const err = await rejection(api.request('api/execute', { method: 'POST' }));

        expect(err.kind).toBe('unverified');
        expect(err.requestId).toBe('req-503');
    });

    test('a proxy HTML 503 is a server error', async () => {
        respondWith(mockResponse(503, '<html>Service Unavailable</html>'));

        const err = await rejection(api.request('api/menu'));

        expect(err.kind).toBe('server');
    });

    test('500 JSON is a server error with the request ID', async () => {
        respondWith(mockResponse(500, { error: 'Internal server error: configuration failure.', requestId: 'req-500' }));

        const err = await rejection(api.request('api/menu'));

        expect(err.kind).toBe('server');
        expect(err.requestId).toBe('req-500');
        expect(err.message).toBe('Internal server error: configuration failure.');
    });

    test('an /api/execute error uses statusmessage', async () => {
        respondWith(mockResponse(400, { status: 400, statusmessage: 'cmdid is required.', messages: [] }));

        const err = await rejection(api.request('api/execute', { method: 'POST' }));

        expect(err.kind).toBe('server');
        expect(err.message).toBe('cmdid is required.');
    });

    test.each([500, 200])('an HTML body (HTTP %p) is a server error', async (status) => {
        respondWith(mockResponse(status, '<html>error</html>'));

        const err = await rejection(api.request('api/menu'));

        expect(err.kind).toBe('server');
    });
});

// ── renderAlert ───────────────────────────────────────────────────────────────

describe('renderAlert', () => {
    function container() {
        const div = document.createElement('div');
        document.body.appendChild(div);
        return div;
    }

    test('an empty menu and an unknown or denied command show the same alert', () => {
        const emptyMenu = api.renderAlert(container(), 'noaccess');
        const denied = api.renderAlert(container(), new api.ApiError('noaccess', 403, 'x', { requestId: 'req-1' }));

        expect(denied.outerHTML).toBe(emptyMenu.outerHTML);
        expect(emptyMenu.textContent).toBe(
            "Not available. Either no matching commands exist or you don't have permission to use them. "
            + 'Contact your WebJEA administrator if you need access.');
        expect(emptyMenu.className).toContain('alert-warning');
    });

    test('replaces the container contents', () => {
        const div = container();
        div.innerHTML = '<p>loading</p>';

        api.renderAlert(div, 'network');

        expect(div.querySelector('p')).toBeNull();
        expect(div.querySelectorAll('.webjea-alert').length).toBe(1);
    });

    test('a server error shows the reference ID', () => {
        const alert = api.renderAlert(container(), new api.ApiError('server', 500, 'x', { requestId: 'req-42' }));

        expect(alert.className).toContain('alert-danger');
        expect(alert.querySelector('.webjea-alert-ref').textContent).toBe('Reference: req-42');
    });

    test('server text is never parsed as HTML', () => {
        const alert = api.renderAlert(container(), new api.ApiError('server', 500, 'x', { requestId: '<img src=x onerror="alert(1)">' }));

        expect(alert.querySelector('img')).toBeNull();
        expect(alert.textContent).toContain('<img src=x');
    });

    test('network and server errors get a Try again button that calls retry', () => {
        const retry = jest.fn();
        const alert = api.renderAlert(container(), 'network', { retry: retry });

        const btn = alert.querySelector('button');
        expect(btn.textContent).toBe('Try again');
        btn.click();
        expect(retry).toHaveBeenCalledTimes(1);
    });

    test('noaccess never gets a Try again button', () => {
        const alert = api.renderAlert(container(), 'noaccess', { retry: jest.fn() });

        expect(alert.querySelector('button')).toBeNull();
    });

    test('an expired session offers Reload instead of retry', () => {
        const alert = api.renderAlert(container(), 'session', { retry: jest.fn() });

        const buttons = alert.querySelectorAll('button');
        expect(buttons.length).toBe(1);
        expect(buttons[0].textContent).toBe('Reload');
    });

    test('an ended session asks the user to sign in again', () => {
        const alert = api.renderAlert(container(), 'session');

        expect(alert.textContent).toContain('Please sign in again. Your session ended. Reload the page to sign in again.');
        expect(alert.className).toContain('alert-warning');
    });

    test('changed access explains itself and offers Reload', () => {
        const alert = api.renderAlert(container(), new api.ApiError('accesschanged', 403, 'x', { requestId: 'req-1' }));

        expect(alert.textContent).toContain('Your access has changed. You no longer have permission to run this command.');
        expect(alert.className).toContain('alert-warning');
        expect(alert.querySelector('button').textContent).toBe('Reload');
        expect(alert.querySelector('.webjea-alert-ref')).toBeNull();
    });

    test('an unverified sign-in shows the reference ID and can be retried', () => {
        const retry = jest.fn();
        const alert = api.renderAlert(container(), new api.ApiError('unverified', 503, 'x', { requestId: 'req-503' }), { retry: retry });

        expect(alert.textContent).toContain("Couldn't verify your sign-in.");
        expect(alert.querySelector('.webjea-alert-ref').textContent).toBe('Reference: req-503');
        expect(alert.querySelector('button').textContent).toBe('Try again');
    });

    test('dashboardLink adds a link back to the dashboard', () => {
        const alert = api.renderAlert(container(), 'noaccess', { dashboardLink: true });

        expect(alert.querySelector('a.alert-link').getAttribute('href')).toBe('./');
    });

    test('an unknown error is treated as a server error', () => {
        const alert = api.renderAlert(container(), new Error('boom'));

        expect(alert.getAttribute('data-kind')).toBe('server');
    });
});

// ── NoAccessHelpHtml ──────────────────────────────────────────────────────────

describe('setNoAccessHelp', () => {
    test('a configured value replaces the default line', () => {
        api.setNoAccessHelp('Use the <a href="https://helpdesk.example">service desk</a>.');

        const alert = api.renderAlert(document.body.appendChild(document.createElement('div')), 'noaccess');

        const help = alert.querySelector('.webjea-noaccess-help');
        expect(help.textContent).toBe('Use the service desk.');
        expect(help.querySelector('a').getAttribute('href')).toBe('https://helpdesk.example');
    });

    test('scripts and event handlers are stripped', () => {
        api.setNoAccessHelp('Hi<script>window.pwned = 1</script><img src="x" onerror="window.pwned = 1">');

        const alert = api.renderAlert(document.body.appendChild(document.createElement('div')), 'noaccess');

        const help = alert.querySelector('.webjea-noaccess-help');
        expect(help.querySelector('script')).toBeNull();
        expect(help.querySelector('img').getAttribute('onerror')).toBeNull();
    });

    test('updates an alert that rendered before /api/config returned', () => {
        const alert = api.renderAlert(document.body.appendChild(document.createElement('div')), 'noaccess');
        expect(alert.querySelector('.webjea-noaccess-help').textContent).toBe(api.DEFAULT_NOACCESS_HELP);

        api.setNoAccessHelp('Call extension 1234.');

        expect(alert.querySelector('.webjea-noaccess-help').textContent).toBe('Call extension 1234.');
    });

    test('an empty value goes back to the default line', () => {
        api.setNoAccessHelp('Call extension 1234.');
        api.setNoAccessHelp(null);

        const alert = api.renderAlert(document.body.appendChild(document.createElement('div')), 'noaccess');

        expect(alert.querySelector('.webjea-noaccess-help').textContent).toBe(api.DEFAULT_NOACCESS_HELP);
    });
});

// ── pageAlert ─────────────────────────────────────────────────────────────────

describe('pageAlert', () => {
    beforeEach(() => {
        document.body.innerHTML = '<div id="divApiAlert"></div><div id="divTileView"></div>';
    });

    test('shows one banner per kind', () => {
        api.pageAlert('network');
        api.pageAlert('network');
        api.pageAlert('server');

        expect(document.querySelectorAll('#divApiAlert .webjea-alert').length).toBe(2);
    });

    test('is skipped when an in-place alert of that kind is already showing', () => {
        api.renderAlert(document.getElementById('divTileView'), 'network');

        expect(api.pageAlert('network')).toBeNull();
        expect(document.querySelectorAll('#divApiAlert .webjea-alert').length).toBe(0);
    });

    test('an in-place alert replaces a banner of the same kind', () => {
        api.pageAlert('network');

        api.renderAlert(document.getElementById('divTileView'), 'network');

        expect(document.querySelectorAll('.webjea-alert').length).toBe(1);
        expect(document.querySelector('#divTileView .webjea-alert')).not.toBeNull();
    });
});

// ── /api/execute errors (PSWebParser.js) ──────────────────────────────────────

describe('renderExecuteError', () => {
    test('a validation failure lists the messages in the console, in red', () => {
        const el = document.createElement('span');
        const err = new api.ApiError('server', 400, 'Parameter validation failed.', {
            status: 400,
            statusmessage: 'Parameter validation failed.',
            messages: [{ stream: 'error', message: "Parameter 'Name' is required." }]
        });

        renderExecuteError(el, err);

        const lines = el.querySelectorAll('.pserror');
        expect(lines.length).toBe(2);
        expect(lines[0].textContent).toBe('Parameter validation failed.');
        expect(lines[1].textContent).toBe("Parameter 'Name' is required.");
    });

    test('a 403 shows the same noaccess alert as the rest of the app', () => {
        const el = document.createElement('span');

        renderExecuteError(el, new api.ApiError('noaccess', 403, 'x', { status: 403, messages: [] }));

        expect(el.querySelector('.webjea-alert').getAttribute('data-kind')).toBe('noaccess');
    });

    test.each([
        ['session', 401],
        ['accesschanged', 403],
        ['unverified', 503]
    ])('a %s error (HTTP %p) shows its alert, not console output', (kind, status) => {
        const el = document.createElement('span');

        renderExecuteError(el, new api.ApiError(kind, status, 'x', { status: status, messages: [] }));

        expect(el.querySelector('.webjea-alert').getAttribute('data-kind')).toBe(kind);
        expect(el.querySelector('.pserror')).toBeNull();
    });

    test('a 500 shows a server alert with the reference ID', () => {
        const el = document.createElement('span');

        renderExecuteError(el, new api.ApiError('server', 500, 'Internal server error.', { status: 500, requestId: 'req-9' }));

        expect(el.querySelector('.webjea-alert-ref').textContent).toBe('Reference: req-9');
    });

    test('a network failure shows a network alert', () => {
        const el = document.createElement('span');

        renderExecuteError(el, new api.ApiError('network', 0, 'Failed to fetch'));

        expect(el.querySelector('.webjea-alert').getAttribute('data-kind')).toBe('network');
    });
});
