using CycloneGames.Logger;

namespace CycloneGames.UIFramework.Runtime
{
    /// <summary>
    /// Generic UIWindow base that automatically creates and manages a Presenter.
    /// Fully backward-compatible: inherit from UIWindow for traditional usage,
    /// or UIWindow<TPresenter> for Presenter for MVP pattern with automatic lifecycle management.
    /// 
    /// Lifecycle mapping:
    /// - Awake() → Presenter created, SetView() called
    /// - OnStartOpen() → Presenter.OnViewOpening()
    /// - OnFinishedOpen() → Presenter.OnViewOpened()
    /// - OnStartClose() → Presenter.OnViewClosing()
    /// - OnFinishedClose() → Presenter.OnViewClosed()
    /// - OnDestroy() → Presenter.Dispose()
    /// 
    /// Note: new() constraint removed to support VContainer constructor injection.
    /// UIPresenterFactory handles creation via CustomFactory (DI) or Activator (requires parameterless ctor).
    /// </summary>
    /// <typeparam name="TPresenter">The presenter type. Must implement IUIPresenter.</typeparam>
    public abstract class UIWindow<TPresenter> : UIWindow
        where TPresenter : class, IUIPresenter
    {
        private TPresenter _presenter;

        /// <summary>
        /// The auto-created presenter instance. Available after Awake().
        /// </summary>
        protected TPresenter Presenter => _presenter;

        protected override void Awake()
        {
            base.Awake();

            // Create presenter instance via factory (supports DI override)
            _presenter = UIPresenterFactory.Create<TPresenter>();

            if (_presenter != null)
            {
                _presenter.SetView(this);
            }
            else
            {
                CLogger.LogError($"[UIWindow<T>] Failed to create presenter {typeof(TPresenter).Name} for {GetType().Name}");
            }
        }

        protected override void OnStartOpen()
        {
            base.OnStartOpen();
            _presenter?.OnViewOpening();
        }

        protected override void OnFinishedOpen()
        {
            base.OnFinishedOpen();
            _presenter?.OnViewOpened();
        }

        protected override void OnStartClose()
        {
            _presenter?.OnViewClosing();
            base.OnStartClose();
        }

        protected override void OnFinishedClose()
        {
            _presenter?.OnViewClosed();
            base.OnFinishedClose();
        }

        protected override void OnDestroy()
        {
            if (_presenter != null)
            {
                _presenter.Dispose();
                _presenter = null;
            }
            base.OnDestroy();
        }
    }
}
