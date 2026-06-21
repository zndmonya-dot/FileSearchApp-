// 起動スプラッシュ・初期テーマ（index.html 専用）
(function () {
    var ver = window.__PANOLEON_BOOT_VERSION;
    if (ver) {
        var verEl = document.querySelector('.boot-splash__version');
        if (verEl) verEl.textContent = ver;
    }
    var theme = 'dark';
    try {
        var injected = window.__PANOLEON_BOOT_THEME;
        if (injected === 'light' || injected === 'dark') {
            theme = injected;
        } else {
            var stored = localStorage.getItem('panoleon-ui-theme');
            if (stored === 'light' || stored === 'dark') theme = stored;
        }
    } catch (e) { /* WebView 制限 */ }
    var root = document.documentElement;
    root.classList.remove('boot-theme-dark', 'boot-theme-light');
    root.classList.add(theme === 'light' ? 'boot-theme-light' : 'boot-theme-dark');
})();

window._bootSplashStart = Date.now();
window._bootSplashMinMs = 2600;
window._bootSplash = {
    display: 0,
    target: 6,
    hiding: false,
    tickId: null,
    statusTimer: null
};

function bootSplashRender() {
    var state = window._bootSplash;
    var bar = document.getElementById('boot-splash-bar');
    var pct = document.getElementById('boot-splash-percent');
    var track = document.getElementById('boot-splash-track');
    var shown = Math.round(state.display);
    if (bar) bar.style.width = shown + '%';
    if (pct) pct.textContent = shown + '%';
    if (track) track.setAttribute('aria-valuenow', String(shown));
}

function bootSplashSetStatus(text) {
    var status = document.getElementById('boot-splash-status');
    if (!status || !text || status.textContent === text) return;
    var state = window._bootSplash;
    if (state.statusTimer) window.clearTimeout(state.statusTimer);
    status.classList.add('is-changing');
    state.statusTimer = window.setTimeout(function () {
        status.textContent = text;
        status.classList.remove('is-changing');
        state.statusTimer = null;
    }, 100);
}

function bootSplashTick() {
    var state = window._bootSplash;
    if (state.display < state.target) {
        var gap = state.target - state.display;
        var step = Math.max(0.25, gap * (state.hiding ? 0.18 : 0.1));
        state.display = Math.min(state.target, state.display + step);
        bootSplashRender();
    }
    if (state.hiding && state.display >= 99.5 && state.target >= 100) {
        window.clearInterval(state.tickId);
        state.tickId = null;
        bootSplashFinishHide();
        return;
    }
    if (state.display < state.target || (state.hiding && state.display < 100)) return;
    if (!state.hiding && state.target < 86) {
        state.target = Math.min(86, state.target + 0.06);
    }
}

window.setBootSplashProgress = function (percent, statusText) {
    var state = window._bootSplash;
    var next = Math.max(0, Math.min(100, Number(percent) || 0));
    state.target = Math.max(state.target, next);
    if (statusText) bootSplashSetStatus(statusText);
    if (next >= 95) {
        var splash = document.getElementById('boot-splash');
        if (splash) splash.classList.add('boot-splash--complete');
    }
    if (!state.tickId) state.tickId = window.setInterval(bootSplashTick, 32);
    bootSplashTick();
};

window.setBootSplashTheme = function (theme) {
    var resolved = theme === 'light' ? 'light' : 'dark';
    try { localStorage.setItem('panoleon-ui-theme', resolved); } catch (e) { /* WebView 制限 */ }
    var root = document.documentElement;
    root.classList.remove('boot-theme-dark', 'boot-theme-light');
    root.classList.add(resolved === 'light' ? 'boot-theme-light' : 'boot-theme-dark');
};

function bootSplashFinishHide() {
    var el = document.getElementById('boot-splash');
    if (!el || el.classList.contains('boot-splash--hidden')) return;
    el.classList.add('boot-splash--hidden');
    window.setTimeout(function () {
        if (el.parentNode) el.parentNode.removeChild(el);
    }, 340);
}

window.hideBootSplash = function () {
    var el = document.getElementById('boot-splash');
    if (!el || el.classList.contains('boot-splash--hidden')) return;
    var state = window._bootSplash;
    state.hiding = true;
    state.target = 100;
    window.setBootSplashProgress(100, '準備完了'); // UserMessages.BootSplashReady
    var elapsed = Date.now() - (window._bootSplashStart || Date.now());
    var remaining = Math.max(0, (window._bootSplashMinMs || 2600) - elapsed);
    var tryHide = function () {
        if (state.display >= 99.5) bootSplashFinishHide();
        else window.setTimeout(tryHide, 50);
    };
    window.setTimeout(tryHide, remaining);
};

window.setBootSplashProgress(8, '起動しています...'); // UserMessages.BootSplashStarting

window.getPreferredColorScheme = function () {
    return window.matchMedia && window.matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light';
};
