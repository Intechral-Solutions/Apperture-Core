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
        }
    };
})();
