using System;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace CycloneGames.UIFramework.Runtime
{
    public enum WindowStateCallbackType
    {
        OnStartOpen,
        OnFinishedOpen,
        OnStartClose,
        OnFinishedClose
    }

    /// <summary>
    /// Immutable inputs supplied to a window binder for one window instance.
    /// </summary>
    public readonly struct UIWindowBindingContext
    {
        public UIWindowBindingContext(UIWindow window, IUIService uiService)
            : this(window, uiService, null, null, default)
        {
        }

        public UIWindowBindingContext(
            UIWindow window,
            IUIService uiService,
            string openerId,
            object openContext,
            CancellationToken lifetimeToken)
        {
            Window = window != null ? window : throw new ArgumentNullException(nameof(window));
            UIService = uiService ?? throw new ArgumentNullException(nameof(uiService));
            OpenerId = openerId ?? string.Empty;
            OpenContext = openContext;
            LifetimeToken = lifetimeToken;
        }

        public UIWindow Window { get; }

        public IUIService UIService { get; }

        /// <summary>The active opener supplied for this window session, or an empty string.</summary>
        public string OpenerId { get; }

        /// <summary>Caller-owned data supplied through UIOpenOptions for this session.</summary>
        public object OpenContext { get; }

        /// <summary>
        /// Cancels when the window session begins final cleanup. Bindings must not use
        /// this token or retain OpenContext after they have been disposed.
        /// </summary>
        public CancellationToken LifetimeToken { get; }

        public bool TryGetOpenContext<TContext>(out TContext context)
        {
            if (OpenContext is TContext typedContext)
            {
                context = typedContext;
                return true;
            }

            context = default;
            return false;
        }
    }

    /// <summary>
    /// Owns one successful binder-to-window relationship.
    /// </summary>
    /// <remarks>
    /// The UI runtime forwards state notifications to bindings in creation order and
    /// disposes bindings in reverse order. Implementations must make Dispose idempotent.
    /// </remarks>
    public interface IUIWindowBinding : IDisposable
    {
        void OnWindowStateChanged(WindowStateCallbackType state);
    }

    /// <summary>
    /// Optional asynchronous lifecycle participant for ordered, cancelable window setup
    /// and teardown work. UIService calls this method instead of the synchronous callback.
    /// </summary>
    public interface IAsyncUIWindowBinding : IUIWindowBinding
    {
        UniTask OnWindowStateChangedAsync(
            WindowStateCallbackType state,
            CancellationToken cancellationToken);
    }

    /// <summary>
    /// Creates an optional transactional binding for a newly instantiated window.
    /// </summary>
    /// <remarks>
    /// Return null when the binder does not apply to the supplied window. If Bind throws,
    /// it must release any resources acquired before the exception escapes.
    /// </remarks>
    public interface IUIWindowBinder
    {
        IUIWindowBinding Bind(UIWindowBindingContext context);
    }
}
