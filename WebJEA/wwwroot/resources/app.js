// app.js
// Page bootstrap: fetches /api/config and /api/menu and populates the static shell.
// The data-page attribute on the script tag selects dashboard or command behavior.

(function (global) {
    'use strict';

    // DOMPurify default profile: admin-supplied HTML (DashboardHtml, synopsis) keeps
    // its markup but scripts/event handlers are stripped. This supersedes the old
    // server-side BalanceHtmlFragment tag balancing.
    function sanitizeHtml(html) {
        if (typeof DOMPurify !== 'undefined') {
            return DOMPurify.sanitize(html);
        }
        return '';
    }

    function escapeHtml(str) {
        if (str === null || str === undefined) return '';
        return String(str)
            .replace(/&/g, '&amp;')
            .replace(/</g, '&lt;')
            .replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;')
            .replace(/'/g, '&#39;');
    }

    function setText(id, text) {
        var el = document.getElementById(id);
        if (el) el.textContent = text;
    }

    function fetchJson(url) {
        return fetch(url).then(function (response) {
            if (!response.ok) throw new Error('Request failed: ' + response.status);
            return response.json();
        });
    }

    function applyConfig(cfg) {
        document.documentElement.lang = cfg.htmlLanguage || 'en-US';
        var version = cfg.version ? 'v' + cfg.version : '';
        setText('lblTitleDash', cfg.title || '');
        setText('lblVersionDash', version);
        setText('lblTitle', cfg.title || '');
        setText('lblTitleDesktop', cfg.title || '');
        setText('lblVersion', version);
        return cfg;
    }

    function initDashboard() {
        fetchJson('api/config').then(applyConfig).then(function (cfg) {
            document.title = (cfg.title || 'WebJEA') + ' - WebJEA';

            if (cfg.dashboardHtml) {
                var container = document.getElementById('divDashboardHtml');
                if (container) {
                    container.innerHTML = '<div class="card border-0 rounded-0 w-100 dashboard-html"><div class="card-body">'
                        + sanitizeHtml(cfg.dashboardHtml) + '</div></div>';
                }
            }
        }).catch(function (err) { console.error('config load failed', err); });

        fetchJson('api/menu').then(function (menu) {
            var html = '';
            menu.forEach(function (mi) {
                html += '<div class="tile">';
                html += '<a class="card h-100 text-decoration-none tile-card" href="' + escapeHtml(mi.uri) + '">';
                html += '<div class="card-body">';
                html += '<p class="card-title fw-semibold">' + escapeHtml(mi.displayName) + '</p>';
                if (mi.synopsis) {
                    html += '<p class="card-synopsis">' + sanitizeHtml(mi.synopsis) + '</p>';
                }
                if (mi.description) {
                    html += '<p class="card-text">' + escapeHtml(mi.description) + '</p>';
                }
                html += '</div></a></div>';
            });
            var tileView = document.getElementById('divTileView');
            if (tileView) tileView.innerHTML = html;
        }).catch(function (err) { console.error('menu load failed', err); });
    }

    function currentCmdId() {
        var qs = new URLSearchParams(global.location.search);
        var cmdid = '';
        qs.forEach(function (value, key) {
            if (key.toLowerCase() === 'cmdid' && !cmdid) cmdid = value;
        });
        return cmdid;
    }

    function initCommand() {
        var cmdid = currentCmdId();

        fetchJson('api/config').then(applyConfig)
            .catch(function (err) { console.error('config load failed', err); });

        fetchJson('api/menu').then(function (menu) {
            var navList = document.getElementById('nav-list');
            if (!navList) return;
            var html = '';
            menu.forEach(function (mi) {
                var css = mi.id === cmdid ? ' active' : '';
                html += '<a class="nav-item-link' + css + '" href="' + escapeHtml(mi.uri) + '">'
                    + escapeHtml(mi.displayName)
                    + '<span class="nav-tip">' + escapeHtml(mi.description) + '</span></a>';
            });
            navList.innerHTML = html;
        }).catch(function (err) { console.error('menu load failed', err); });

        // sidebar drawer wiring (toggleSidebar/closeSidebar come from startup.js)
        var toggleBtn = document.getElementById('btnToggleSidebar');
        if (toggleBtn && typeof global.toggleSidebar === 'function') {
            toggleBtn.addEventListener('click', function () { global.toggleSidebar(); });
        }

        var overlay = document.getElementById('overlay');
        if (overlay && typeof global.closeSidebar === 'function') {
            overlay.addEventListener('click', function () { global.closeSidebar(); });
        }

        if (typeof global.webjeaRenderForm === 'function') {
            global.webjeaRenderForm(cmdid);
        }
    }

    var script = document.currentScript;
    var page = script ? script.getAttribute('data-page') : null;
    if (page === 'dashboard') {
        initDashboard();
    } else if (page === 'command') {
        initCommand();
    }

    // Expose pure helper functions for Node.js unit testing.
    if (typeof module !== 'undefined' && module.exports) {
        module.exports = {
            escapeHtml: escapeHtml,
            sanitizeHtml: sanitizeHtml
        };
    }

}(typeof window !== 'undefined' ? window : {}));
