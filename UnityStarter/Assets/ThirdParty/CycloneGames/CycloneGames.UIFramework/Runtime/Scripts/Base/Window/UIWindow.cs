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

        public string WindowId => _windowId ?? string.Empty;
        public int Priority => _configuration != null ? _configuration.Priority : 0;
        public UIWindowConfiguration Configuration => _configuration;
        public UILayer ParentLayer => _parentLayer;
        public UIWindowState State => _state;
        public bool IsSceneBound => _isSceneBound;
        public int BoundSceneHandle => _boundSceneHandle;
        public CanvasGroup CanvasGroup => _canvasGroup;

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
            OnOpening();

            if (transitionDriver != null)
            {
                await transitionDriver.PlayOpenAsync(this, cancellationToken);
                await UniTask.SwitchToMainThread();
            }

            cancellationToken.ThrowIfCancellationRequested();
            TransitionTo(UIWindowState.Open);
            OnOpened();
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
            OnClosing();

            if (transitionDriver != null)
            {
                await transitionDriver.PlayCloseAsync(this, cancellationToken);
                await UniTask.SwitchToMainThread();
            }

            cancellationToken.ThrowIfCancellationRequested();
            TransitionTo(UIWindowState.Closed);
            OnClosed();
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
                    OnClosing();
                }
                catch (Exception exception)
                {
                    firstException = exception;
                }
            }

            _state = UIWindowState.Closed;
            try
            {
                OnClosed();
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
                return;
            }

            gameObject.SetActive(visible);
        }

        protected virtual void OnOpening() { }
        protected virtual void OnOpened() { }
        protected virtual void OnClosing() { }
        protected virtual void OnClosed() { }

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
