using System;
using System.Collections.Generic;
using CycloneGames.Localization.Core;
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
        private readonly List<WindowResponderBinding> _activeWindows = new(16);

        // Reusable discovery buffer. Locale changes use the per-window cached responder arrays.
        private static readonly List<ILocaleResponder> SharedResponderCache = new(32);

        private sealed class WindowResponderBinding
        {
            public UIWindow Window;
            public ILocaleResponder[] Responders;
        }

        public LocalizationWindowBinder(ILocalizationService service)
        {
            _service = service ?? throw new ArgumentNullException(nameof(service));
            _service.OnLocaleChanged += OnLocaleChanged;
        }

        // IUIWindowBinder
        public void OnWindowCreated(UIWindow window)
        {
            if (window == null) return;

            for (int i = 0; i < _activeWindows.Count; i++)
            {
                if (_activeWindows[i].Window == window)
                    return;
            }

            var binding = new WindowResponderBinding
            {
                Window = window,
                Responders = BuildResponderCache(window)
            };
            _activeWindows.Add(binding);
            NotifyResponders(binding.Responders, _service.CurrentLocale);
        }

        public void OnWindowDestroying(UIWindow window)
        {
            for (int i = _activeWindows.Count - 1; i >= 0; i--)
            {
                if (_activeWindows[i].Window == window)
                {
                    _activeWindows.RemoveAt(i);
                    return;
                }
            }
        }

        public void OnWindowStateChanged(UIWindow window, WindowStateCallbackType state) { }

        // Locale change propagation
        private void OnLocaleChanged(LocaleId newLocale)
        {
            for (int i = _activeWindows.Count - 1; i >= 0; i--)
            {
                if (i >= _activeWindows.Count) continue;
                var binding = _activeWindows[i];
                if (binding.Window == null)
                {
                    _activeWindows.RemoveAt(i);
                    continue;
                }

                NotifyResponders(binding.Responders, newLocale);
            }
        }

        private static ILocaleResponder[] BuildResponderCache(UIWindow window)
        {
            window.GetComponentsInChildren(true, SharedResponderCache);
            var responders = SharedResponderCache.Count > 0
                ? SharedResponderCache.ToArray()
                : Array.Empty<ILocaleResponder>();
            SharedResponderCache.Clear();
            return responders;
        }

        private static void NotifyResponders(ILocaleResponder[] responders, LocaleId locale)
        {
            for (int i = 0; i < responders.Length; i++)
                responders[i].OnLocaleChanged(locale);
        }

        // Cleanup
        public void Dispose()
        {
            if (_service != null)
                _service.OnLocaleChanged -= OnLocaleChanged;
            _activeWindows.Clear();
            SharedResponderCache.Clear();
        }
    }
}
