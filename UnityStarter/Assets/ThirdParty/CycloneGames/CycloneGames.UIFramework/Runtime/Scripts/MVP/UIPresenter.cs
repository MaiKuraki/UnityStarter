using System;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace CycloneGames.UIFramework.Runtime
{
    /// <summary>
    /// Base class for a presenter bound to a window through a view interface.
    /// </summary>
    /// <remarks>
    /// Presenter binding and lifecycle callbacks run on the Unity main thread. This
    /// type does not add synchronization around the view or the UI service.
    /// </remarks>
    public abstract class UIPresenter<TView> : IUIPresenter where TView : class
    {
        private TView _view;
        private UIWindow _window;
        private IUIService _uiService;

        /// <summary>
        /// Gets the bound view. The value is null before binding and after disposal.
        /// </summary>
        protected TView View => _view;

        protected IUINavigationService NavigationService => _uiService?.NavigationService;

        void IUIPresenter.SetView(UIWindow view)
        {
            if (view == null)
            {
                throw new ArgumentNullException(nameof(view));
            }

            if (!(view is TView typedView))
            {
                throw new InvalidOperationException(
                    $"View type mismatch. Presenter expects {typeof(TView).Name}, but received {view.GetType().Name}.");
            }

            _window = view;
            _view = typedView;

            try
            {
                OnViewBound();
            }
            catch
            {
                _view = null;
                _window = null;
                throw;
            }
        }

        void IUIPresenter.SetUIService(IUIService uiService)
        {
            _uiService = uiService ?? throw new ArgumentNullException(nameof(uiService));
        }

        /// <summary>
        /// Called after both the UI service and the view have been assigned.
        /// </summary>
        protected virtual void OnViewBound()
        {
        }

        public virtual void OnViewOpening()
        {
        }

        public virtual void OnViewOpened()
        {
        }

        public virtual void OnViewClosing()
        {
        }

        public virtual void OnViewClosed()
        {
        }

        /// <summary>
        /// Releases presenter-owned subscriptions and references.
        /// </summary>
        public virtual void Dispose()
        {
            _view = null;
            _window = null;
            _uiService = null;
        }

        /// <summary>
        /// Opens a window and records the current window as its opener.
        /// </summary>
        protected UniTask<UIWindow> NavigateToAsync(
            string targetWindow,
            object context = null,
            CancellationToken cancellationToken = default)
        {
            if (_uiService == null)
            {
                throw new InvalidOperationException("IUIService is not bound.");
            }

            return _uiService.OpenAsync(
                targetWindow,
                new UIOpenOptions(_window?.WindowId, context),
                cancellationToken);
        }

        /// <summary>
        /// Closes the current window and leaves any active opener unchanged.
        /// If no active opener exists, the current window is still closed.
        /// </summary>
        protected async UniTask NavigateBackAsync(
            ChildClosePolicy policy = ChildClosePolicy.Reparent,
            CancellationToken cancellationToken = default)
        {
            if (_uiService == null)
            {
                throw new InvalidOperationException("IUIService is not bound.");
            }

            string currentWindow = _window?.WindowId;
            if (string.IsNullOrEmpty(currentWindow))
            {
                return;
            }

            await _uiService.CloseAsync(currentWindow, policy, cancellationToken);
        }
    }
}
