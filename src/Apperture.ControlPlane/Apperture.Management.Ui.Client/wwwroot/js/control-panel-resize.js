// Epic shell — resizable control panel width (CSS var --shell-panel-width). Persisted in localStorage.
(function () {
    var STORAGE_KEY = 'apperture.shell.panelWidthPx';
    var MIN_WIDTH = 220;
    var CONTENT_MIN = 280;
    var ABS_MAX = 720;

    function parseWidthPx(value) {
        var n = parseInt(value, 10);
        return isFinite(n) ? n : NaN;
    }

    function railWidthPx() {
        var el = document.querySelector('#nav-rail .w-17');
        if (el) return el.getBoundingClientRect().width;
        var tmp = document.createElement('div');
        tmp.style.cssText = 'position:absolute;visibility:hidden;width:calc(var(--spacing) * 17)';
        document.body.appendChild(tmp);
        var w = tmp.getBoundingClientRect().width;
        document.body.removeChild(tmp);
        return w || 68;
    }

    function maxPanelPx() {
        return Math.max(MIN_WIDTH, Math.min(ABS_MAX, window.innerWidth - railWidthPx() - CONTENT_MIN));
    }

    function clampWidth(w) {
        var m = maxPanelPx();
        return Math.min(m, Math.max(MIN_WIDTH, w));
    }

    function applyWidthPx(px) {
        document.documentElement.style.setProperty('--shell-panel-width', Math.round(px) + 'px');
    }

    function initFromStorage() {
        try {
            var raw = localStorage.getItem(STORAGE_KEY);
            if (!raw) return;
            var n = parseWidthPx(raw);
            if (!isFinite(n)) return;
            applyWidthPx(clampWidth(n));
        } catch (e) { /* ignore */ }
    }

    function persistWidth(px) {
        try {
            localStorage.setItem(STORAGE_KEY, String(Math.round(px)));
        } catch (e) { /* ignore */ }
    }

    var dragging = false;
    var startX = 0;
    var startWidth = 0;

    function onMove(e) {
        if (!dragging) return;
        var dx = e.clientX - startX;
        applyWidthPx(clampWidth(startWidth + dx));
    }

    function onUp() {
        if (!dragging) return;
        dragging = false;
        document.removeEventListener('pointermove', onMove);
        document.removeEventListener('pointerup', onUp);
        document.removeEventListener('pointercancel', onUp);
        document.body.style.removeProperty('cursor');
        document.body.style.removeProperty('user-select');
        var panel = document.getElementById('control-panel');
        if (panel) persistWidth(panel.getBoundingClientRect().width);
    }

    function onDown(e) {
        var t = e.target && e.target.closest && e.target.closest('#control-panel-resize-handle');
        if (!t || e.button !== 0) return;
        var panel = document.getElementById('control-panel');
        if (!panel) return;
        dragging = true;
        startX = e.clientX;
        startWidth = panel.getBoundingClientRect().width;
        try {
            t.setPointerCapture(e.pointerId);
        } catch (err) { /* ignore */ }
        document.addEventListener('pointermove', onMove);
        document.addEventListener('pointerup', onUp);
        document.addEventListener('pointercancel', onUp);
        document.body.style.setProperty('cursor', 'ew-resize');
        document.body.style.setProperty('user-select', 'none');
        e.preventDefault();
    }

    function onResize() {
        if (dragging) return;
        var panel = document.getElementById('control-panel');
        if (!panel) return;
        var current = panel.getBoundingClientRect().width;
        var clamped = clampWidth(current);
        if (Math.abs(clamped - current) > 0.5) {
            applyWidthPx(clamped);
            persistWidth(clamped);
        }
    }

    function attach() {
        document.addEventListener('pointerdown', onDown, true);
        window.addEventListener('resize', onResize);
    }

    initFromStorage();
    if (document.readyState === 'loading')
        document.addEventListener('DOMContentLoaded', attach);
    else
        attach();
})();
