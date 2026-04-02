using System;
using System.Collections.Generic;
using CycloneGames.Localization.Runtime;
using UnityEngine;

namespace CycloneGames.UIFramework.Runtime.Integrations.Localization
{
    /// <summary>
    /// <see cref="IUIWindowBinder"/> that bridges <see cref="ILocalizationService"/> locale
    /// changes to every <see cref="ILocaleResponder"/> found within active UIWindows.
    /// <para>
    /// Register via <c>UIManager.RegisterWindowBinder(binder)</c>.
    /// With VContainer: register <see cref="LocalizationWindowBinder"/> as a singleton and
    /// let the container call <c>RegisterWindowBinder</c> during setup.
    /// </para>
    /// </summary>
    public sealed class LocalizationWindowBinder : IUIWindowBinder, IDisposable
    {
        private readonly ILocalizationService _service;
        private readonly List<UIWindow> _activeWindows = new(16);

        // Reusable buffer for GetComponentsInChildren – single-threaded only.
        private static readonly List<ILocaleResponder> SharedResponderCache = new(32);

        public LocalizationWindowBinder(ILocalizationService service)
        {
            _service = service ?? throw new ArgumentNullException(nameof(service));
            _service.OnLocaleChanged += OnLocaleChanged;
        }

        // ── IUIWindowBinder ─────────────────────────────────────
        public void OnWindowCreated(UIWindow window)
        {
            _activeWindows.Add(window);
            NotifyResponders(window, _service.CurrentLocale);
        }

        public void OnWindowDestroying(UIWindow window)
        {
            _activeWindows.Remove(window);
        }

        public void OnWindowStateChanged(UIWindow window, WindowStateCallbackType state) { }

        // ── Locale change propagation ───────────────────────────
        private void OnLocaleChanged(LocaleId newLocale)
        {
            for (int i = _activeWindows.Count - 1; i >= 0; i--)
            {
                if (i >= _activeWindows.Count) continue;
                var window = _activeWindows[i];
                if (window == null)
                {
                    _activeWindows.RemoveAt(i);
                    continue;
                }

                NotifyResponders(window, newLocale);
            }
        }

        private static void NotifyResponders(UIWindow window, LocaleId locale)
        {
            window.GetComponentsInChildren(true, SharedResponderCache);
            for (int i = 0; i < SharedResponderCache.Count; i++)
                SharedResponderCache[i].OnLocaleChanged(locale);
            SharedResponderCache.Clear();
        }

        // ── Cleanup ─────────────────────────────────────────────
        public void Dispose()
        {
            if (_service != null)
                _service.OnLocaleChanged -= OnLocaleChanged;
            _activeWindows.Clear();
        }
    }
}
