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

        /// <summary>
        /// Called immediately after the view is bound. Override for early initialization.
        /// This is called during UIWindow.Awake(), before the window is opened.
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
        /// </summary>
        public virtual void Dispose()
        {
            _view = null;
        }
    }
}
