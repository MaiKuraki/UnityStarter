using System;

namespace CycloneGames.UIFramework.Runtime
{
    /// <summary>
    /// Lifecycle contract for a presenter owned by an IUIWindowBinding.
    /// All methods are invoked on the Unity main thread.
    /// </summary>
    public interface IUIPresenter : IDisposable
    {
        /// <summary>
        /// Assigns the view. A binding failure must throw so the binder can roll back.
        /// </summary>
        void SetView(UIWindow view);

        /// <summary>
        /// Assigns the UI service before the view is bound.
        /// </summary>
        void SetUIService(IUIService uiService);

        /// <summary>
        /// Called when the window starts opening, before its transition.
        /// </summary>
        void OnViewOpening();

        /// <summary>
        /// Called when the window finishes opening and becomes interactive.
        /// </summary>
        void OnViewOpened();

        /// <summary>
        /// Called when the window starts closing, before its transition.
        /// </summary>
        void OnViewClosing();

        /// <summary>
        /// Called after the close transition and before the binding is released.
        /// </summary>
        void OnViewClosed();
    }
}
