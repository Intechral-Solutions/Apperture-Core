// Epic 42 — theme persistence and DOM application (must stay aligned with ThemePreferenceParser.StorageKey).
(function () {
    const storageKey = 'apperture.theme';

    function isSystemDark() {
        return window.matchMedia && window.matchMedia('(prefers-color-scheme: dark)').matches;
    }

    function apply(preference) {
        const dark = preference === 'Dark' || (preference === 'System' && isSystemDark());
        document.documentElement.classList.toggle('dark', dark);
    }

    window.appertureShell = {
        storageKey: storageKey,
        getPreference: function () {
            try {
                return localStorage.getItem(storageKey) || 'System';
            } catch {
                return 'System';
            }
        },
        setPreference: function (value) {
            try {
                localStorage.setItem(storageKey, value);
            } catch { /* ignore */ }
        },
        /** CTX-3A: per-user remembered ResourceContext view (§4). Keyed by user id so a shared browser never seeds one operator's view from another's. */
        getViewPreference: function (userId) {
            try { return localStorage.getItem('apperture.view.' + userId); } catch { return null; }
        },
        setViewPreference: function (userId, value) {
            try { localStorage.setItem('apperture.view.' + userId, value); } catch { /* ignore */ }
        },
        /** CTX-3A Task 8: one-time View selector discovery hint (§3.3). Instance-wide (not
         * user-keyed) -- the hint is about UI discovery, not a per-operator preference. */
        isViewHintDismissed: function () {
            try { return localStorage.getItem('apperture.view.hintDismissed') === 'true'; } catch { return true; }
        },
        dismissViewHint: function () {
            try { localStorage.setItem('apperture.view.hintDismissed', 'true'); } catch { /* ignore */ }
        },
        isSystemDark: isSystemDark,
        apply: apply,
        initFromStorage: function () {
            const raw = window.appertureShell.getPreference();
            apply(raw);
        },
        subscribeSystemTheme: function (dotNetHelper) {
            if (!window.matchMedia) return 0;
            const mq = window.matchMedia('(prefers-color-scheme: dark)');
            const handler = function () {
                dotNetHelper.invokeMethodAsync('OnSystemThemeChanged');
            };
            mq.addEventListener('change', handler);
            return 1;
        },
        /** SWM-4: browser IANA time zone id (e.g. America/Los_Angeles), or null if unavailable. */
        getTimeZone: function () {
            try {
                return Intl.DateTimeFormat().resolvedOptions().timeZone || null;
            } catch {
                return null;
            }
        },
        /** UIX-6: id of the currently focused element, or null. Read-only. */
        captureFocusedElementId: function () {
            try {
                var el = document.activeElement;
                return el && el.id ? el.id : null;
            } catch {
                return null;
            }
        },
        /** UIX-6: focus an element by id (focus restoration). No DOM mutation. */
        focusElementById: function (elementId) {
            if (!elementId) return false;
            try {
                var el = document.getElementById(elementId);
                if (!el || typeof el.focus !== 'function') return false;
                el.focus({ preventScroll: true });
                return true;
            } catch {
                return false;
            }
        },
        /**
         * UIX-6: focus the first or last tabbable element inside a container (dialog focus wrap).
         * Queries and focuses only; never reparents or rewrites Blazor-owned DOM.
         */
        focusEdgeIn: function (containerId, last) {
            if (!containerId) return false;
            try {
                var root = document.getElementById(containerId);
                if (!root) return false;
                var candidates = root.querySelectorAll(
                    'a[href],button:not([disabled]),input:not([disabled]),select:not([disabled]),textarea:not([disabled]),summary,[tabindex]:not([tabindex="-1"])');
                var visible = [];
                for (var i = 0; i < candidates.length; i++) {
                    var el = candidates[i];
                    if (el.offsetParent !== null || el === document.activeElement) visible.push(el);
                }
                if (visible.length === 0) return false;
                var target = last ? visible[visible.length - 1] : visible[0];
                target.focus({ preventScroll: true });
                return true;
            } catch {
                return false;
            }
        },
        /**
         * UIX-6: modal scroll ownership. Toggles a class on document.body, which is outside
         * the Blazor render root, so no Blazor-owned DOM is modified. Reference counted.
         */
        setModalOpen: function (open) {
            try {
                var count = window.appertureShell.__modalCount || 0;
                count = open ? count + 1 : Math.max(0, count - 1);
                window.appertureShell.__modalCount = count;
                document.body.classList.toggle('apperture-modal-open', count > 0);
                return count;
            } catch {
                return 0;
            }
        },
        /** UIX-3: scroll a rendered element into view by id (no Blazor DOM rewrite). */
        scrollElementIntoViewById: function (elementId) {
            if (!elementId) return false;
            var el = document.getElementById(elementId);
            if (!el || typeof el.scrollIntoView !== 'function') return false;
            try {
                el.scrollIntoView({ block: 'nearest', inline: 'nearest', behavior: 'smooth' });
                if (typeof el.focus === 'function') {
                    try { el.focus({ preventScroll: true }); } catch { el.focus(); }
                }
                return true;
            } catch {
                return false;
            }
        }
    };
})();
