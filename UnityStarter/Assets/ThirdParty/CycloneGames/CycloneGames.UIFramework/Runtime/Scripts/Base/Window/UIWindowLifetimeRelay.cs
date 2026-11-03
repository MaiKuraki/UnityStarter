using System;
using UnityEngine;

namespace CycloneGames.UIFramework.Runtime
{
    [DisallowMultipleComponent]
    internal sealed class UIWindowLifetimeRelay : MonoBehaviour
    {
        private UIWindow _window;
        private IUIWindowLifetimeObserver _observer;
        private bool _notified;

        internal void Initialize(UIWindow window, IUIWindowLifetimeObserver observer)
        {
            _window = window ?? throw new ArgumentNullException(nameof(window));
            _observer = observer ?? throw new ArgumentNullException(nameof(observer));
            _notified = false;
        }

        private void OnDestroy()
        {
            if (_notified)
            {
                return;
            }

            _notified = true;
            UIWindow window = _window;
            _window = null;
            _observer = null;

            // Delegate to the window, which owns the single-shot flag: either this path or
            // UIWindow.OnDestroy notifies, whichever component Unity tears down first.
            if (!ReferenceEquals(window, null))
            {
                window.NotifyDestroyed();
            }
        }
    }
}
