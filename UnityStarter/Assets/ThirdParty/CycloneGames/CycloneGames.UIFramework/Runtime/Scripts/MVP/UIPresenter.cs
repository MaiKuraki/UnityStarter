using CycloneGames.Logger;

namespace CycloneGames.UIFramework.Runtime
{
    /// <summary>
    /// Generic base class for UI Presenters. Handles business logic and communicates
    /// with views through the TView interface. Thread-safe for property access.
    /// All lifecycle methods can be overridden for custom behavior.
    /// </summary>
    /// <typeparam name="TView">The view interface type this presenter works with.</typeparam>
    public abstract class UIPresenter<TView> : IUIPresenter where TView : class
    {
        private TView _view;
        private IUIService _uiService;

        /// <summary>
        /// The view this presenter is bound to. Null until SetView is called.
        /// </summary>
        protected TView View => _view;

        void IUIPresenter.SetView(UIWindow view)
        {
            _view = view as TView;
            if (_view == null && view != null)
            {
                CLogger.LogError($"[UIPresenter] View type mismatch: expected {typeof(TView).Name}, got {view.GetType().Name}");
            }
            OnViewBound();
        }

        void IUIPresenter.SetUIService(IUIService uiService)
        {
            _uiService = uiService;
        }

        // ── Lifecycle ─────────────────────────────────────────────────────────

        /// <summary>
        /// Called immediately after the view is bound. Override for early initialization.
        /// This is called by the IUIWindowBinder before the window starts opening.
        /// </summary>
        protected virtual void OnViewBound() { }

        /// <summary>
        /// Called when the window starts opening (before transition animation).
        /// Use for preparing data or starting loading operations.
        /// </summary>
        public virtual void OnViewOpening() { }

        /// <summary>
        /// Called when the window is fully opened and interactive.
        /// Use for populating UI with data, starting animations, etc.
        /// </summary>
        public virtual void OnViewOpened() { }

        /// <summary>
        /// Called when the window starts closing (before transition animation).
        /// Use for saving state or cancelling ongoing operations.
        /// </summary>
        public virtual void OnViewClosing() { }

        /// <summary>
        /// Called when the window finishes closing (after transition animation).
        /// Use for final cleanup before destruction.
        /// </summary>
        public virtual void OnViewClosed() { }

        /// <summary>
        /// Cleanup resources. Called when the window is destroyed (OnDestroy).
        /// Always call base.Dispose() when overriding.
        ///
        /// IMPORTANT: Unsubscribe from all events here to prevent memory leaks.
        /// Example:
        /// <code>
        /// public override void Dispose()
        /// {
        ///     SomeService.OnDataChanged -= HandleDataChanged;
        ///     base.Dispose();
        /// }
        /// </code>
        /// </summary>
        public virtual void Dispose()
        {
            _view = null;
            _uiService = null;
        }

        // ── Navigation helpers ────────────────────────────────────────────────

        /// <summary>
        /// Provides access to the navigation graph for read-only queries (ancestors, context, etc.).
        /// Returns null when no navigation service has been configured.
        /// </summary>
        protected IUINavigationService NavigationService => _uiService?.NavigationService;

        /// <summary>
        /// Opens <paramref name="targetWindow"/> and records this window as its opener in the navigation graph.
        /// The optional <paramref name="context"/> payload can be retrieved in the target window via
        /// <c>NavigationService.GetContext(windowName)</c>.
        /// </summary>
        protected void NavigateTo(string targetWindow, object context = null)
        {
            if (_uiService == null)
            {
                CLogger.LogError("[UIPresenter] Cannot navigate: IUIService is not set. Ensure UIPresenterBinder.SetUIService() was called.");
                return;
            }

            string myWindow = (_view as UIWindow)?.WindowName;
            // Pre-register so the navigation entry exists before UIManager fires its own register callback.
            _uiService.NavigationService?.Register(targetWindow, myWindow, context);
            _uiService.OpenUI(targetWindow);
        }

        /// <summary>
        /// Resolves the nearest alive ancestor and opens it, then closes this window.
        /// <paramref name="policy"/> controls what happens to any children of this window.
        /// </summary>
        protected void NavigateBack(ChildClosePolicy policy = ChildClosePolicy.Reparent)
        {
            if (_uiService == null)
            {
                CLogger.LogError("[UIPresenter] Cannot navigate back: IUIService is not set.");
                return;
            }

            string myWindow = (_view as UIWindow)?.WindowName;
            if (string.IsNullOrEmpty(myWindow)) return;

            string backTarget = _uiService.NavigationService?.ResolveBackTarget(myWindow);
            if (!string.IsNullOrEmpty(backTarget))
                _uiService.OpenUI(backTarget);

            _uiService.NavigationService?.Unregister(myWindow, policy);
            _uiService.CloseUI(myWindow);
        }
    }
}
