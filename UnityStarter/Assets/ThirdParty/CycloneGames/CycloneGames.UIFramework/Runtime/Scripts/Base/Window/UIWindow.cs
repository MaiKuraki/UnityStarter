using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace CycloneGames.UIFramework.Runtime
{
    internal interface IUIWindowLifetimeObserver
    {
        void OnWindowDestroyed(UIWindow window);
    }

    [DisallowMultipleComponent]
    public class UIWindow : MonoBehaviour
    {
        private UIWindowConfiguration _configuration;
        private string _windowId;
        private UILayer _parentLayer;
        private CanvasGroup _canvasGroup;
        private UIWindowState _state = UIWindowState.Created;
        private bool _initialized;
        private bool _isSceneBound;
        private int _boundSceneHandle = -1;
        private int _lifecycleCallbackDepth;

        public string WindowId => _windowId ?? string.Empty;
        public int Priority => _configuration != null ? _configuration.Priority : 0;
        public UIWindowConfiguration Configuration => _configuration;
        public UILayer ParentLayer => _parentLayer;
        public UIWindowState State => _state;
        public bool IsSceneBound => _isSceneBound;
        public int BoundSceneHandle => _boundSceneHandle;
        public CanvasGroup CanvasGroup => _canvasGroup;

        /// <summary>
        /// True while one of this window's own lifecycle callbacks is executing synchronously.
        /// UIService uses this to reject re-entrant open/close requests for the same window.
        /// </summary>
        internal bool IsDispatchingLifecycleCallback => _lifecycleCallbackDepth > 0;

        protected virtual void Awake()
        {
            _canvasGroup = GetComponent<CanvasGroup>();
        }

        internal void InitializeRuntime(
            string windowId,
            UIWindowConfiguration configuration,
            bool isSceneBound,
            int sceneHandle,
            IUIWindowLifetimeObserver lifetimeObserver)
        {
            if (_initialized)
            {
                throw new InvalidOperationException($"Window '{name}' is already initialized.");
            }

            if (string.IsNullOrWhiteSpace(windowId))
            {
                throw new ArgumentException("Window id cannot be empty.", nameof(windowId));
            }

            _windowId = windowId;
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            if (_canvasGroup == null)
            {
                _canvasGroup = GetComponent<CanvasGroup>();
            }
            _isSceneBound = isSceneBound;
            _boundSceneHandle = isSceneBound ? sceneHandle : -1;
            UIWindowLifetimeRelay lifetimeRelay = GetComponent<UIWindowLifetimeRelay>();
            if (lifetimeRelay == null)
            {
                lifetimeRelay = gameObject.AddComponent<UIWindowLifetimeRelay>();
            }

            lifetimeRelay.Initialize(this, lifetimeObserver);
            _state = UIWindowState.Created;
            _initialized = true;
            gameObject.name = windowId;
        }

        internal void SetLayer(UILayer layer) => _parentLayer = layer;

        internal async UniTask RunOpenAsync(
            IUIWindowTransitionDriver transitionDriver,
            CancellationToken cancellationToken)
        {
            TransitionTo(UIWindowState.Opening);
            InvokeOpening();

            if (transitionDriver != null)
            {
                await transitionDriver.PlayOpenAsync(this, cancellationToken);
                await UniTask.SwitchToMainThread();
            }

            cancellationToken.ThrowIfCancellationRequested();
            TransitionTo(UIWindowState.Open);
            InvokeOpened();
        }

        internal async UniTask RunCloseAsync(
            IUIWindowTransitionDriver transitionDriver,
            CancellationToken cancellationToken)
        {
            if (_state == UIWindowState.Closed)
            {
                return;
            }

            TransitionTo(UIWindowState.Closing);
            InvokeClosing();

            if (transitionDriver != null)
            {
                await transitionDriver.PlayCloseAsync(this, cancellationToken);
                await UniTask.SwitchToMainThread();
            }

            cancellationToken.ThrowIfCancellationRequested();
            TransitionTo(UIWindowState.Closed);
            InvokeClosed();
        }

        internal void ForceClosed()
        {
            if (_state == UIWindowState.Closed)
            {
                return;
            }

            Exception firstException = null;
            if (_state != UIWindowState.Closing)
            {
                _state = UIWindowState.Closing;
                try
                {
                    InvokeClosing();
                }
                catch (Exception exception)
                {
                    firstException = exception;
                }
            }

            _state = UIWindowState.Closed;
            try
            {
                InvokeClosed();
            }
            catch (Exception exception)
            {
                if (firstException == null)
                {
                    firstException = exception;
                }
                else
                {
                    throw new AggregateException(
                        "UIWindow forced-close callbacks failed.",
                        firstException,
                        exception);
                }
            }

            if (firstException != null)
            {
                throw firstException;
            }
        }

        public virtual void SetVisible(bool visible)
        {
            if (_canvasGroup != null)
            {
                _canvasGroup.alpha = visible ? 1f : 0f;
                _canvasGroup.interactable = visible;
                _canvasGroup.blocksRaycasts = visible;

                if (_configuration != null && _configuration.CullWhenHidden)
                {
                    // Culling deactivates the whole GameObject; the CanvasGroup state set
                    // above is what the window comes back with when re-shown.
                    if (gameObject.activeSelf != visible)
                    {
                        gameObject.SetActive(visible);
                    }
                }

                return;
            }

            gameObject.SetActive(visible);
        }

        protected virtual void OnOpening() { }
        protected virtual void OnOpened() { }
        protected virtual void OnClosing() { }
        protected virtual void OnClosed() { }

        // Lifecycle callbacks run synchronously; the depth counter lets UIService detect a
        // re-entrant open/close requested from inside the callback without arming the guard
        // across the (possibly long) transition awaits that follow.
        private void InvokeOpening()
        {
            _lifecycleCallbackDepth++;
            try
            {
                OnOpening();
            }
            finally
            {
                _lifecycleCallbackDepth--;
            }
        }

        private void InvokeOpened()
        {
            _lifecycleCallbackDepth++;
            try
            {
                OnOpened();
            }
            finally
            {
                _lifecycleCallbackDepth--;
            }
        }

        private void InvokeClosing()
        {
            _lifecycleCallbackDepth++;
            try
            {
                OnClosing();
            }
            finally
            {
                _lifecycleCallbackDepth--;
            }
        }

        private void InvokeClosed()
        {
            _lifecycleCallbackDepth++;
            try
            {
                OnClosed();
            }
            finally
            {
                _lifecycleCallbackDepth--;
            }
        }

        private void TransitionTo(UIWindowState next)
        {
            bool valid = (_state == UIWindowState.Created && next == UIWindowState.Opening) ||
                         (_state == UIWindowState.Opening && (next == UIWindowState.Open || next == UIWindowState.Closing)) ||
                         (_state == UIWindowState.Open && next == UIWindowState.Closing) ||
                         (_state == UIWindowState.Closing && next == UIWindowState.Closed);

            if (!valid)
            {
                throw new InvalidOperationException(
                    $"Window '{WindowId}' cannot transition from {_state} to {next}.");
            }

            _state = next;
        }

        protected virtual void OnDestroy()
        {
            _state = UIWindowState.Closed;
            _parentLayer = null;
        }
    }
}
