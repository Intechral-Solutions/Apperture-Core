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
        // #region agent log
        /** UIX-2 debug: POST NDJSON to local debug ingest (session 4bd3d8). */
        debugLog: function (location, message, hypothesisId, data) {
            try {
                fetch('http://127.0.0.1:7502/ingest/d56ff1c3-6c46-43da-8333-b1782c5d1416', {
                    method: 'POST',
                    headers: {
                        'Content-Type': 'application/json',
                        'X-Debug-Session-Id': '4bd3d8'
                    },
                    body: JSON.stringify({
                        sessionId: '4bd3d8',
                        location: location || 'apperture-shell',
                        message: message || '',
                        hypothesisId: hypothesisId || '',
                        data: data || {},
                        timestamp: Date.now()
                    })
                }).catch(function () { });
            } catch (e) { /* ignore */ }
        }
        // #endregion
    };
})();
