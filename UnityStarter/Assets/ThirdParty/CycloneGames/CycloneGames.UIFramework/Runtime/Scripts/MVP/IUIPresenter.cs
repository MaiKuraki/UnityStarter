using System;

namespace CycloneGames.UIFramework.Runtime
{
    /// <summary>
    /// Lifecycle interface for UI presenters. Presenters handle business logic
    /// and communicate with views through defined interfaces.
    /// All lifecycle methods correspond to UIWindow lifecycle hooks.
    /// </summary>
    public interface IUIPresenter : IDisposable
    {
        /// <summary>
        /// Sets the view reference. Called automatically during UIWindow.Awake().
        /// </summary>
        void SetView(UIWindow view);

        /// <summary>
        /// Called when the window starts opening (before transition animation).
        /// Corresponds to UIWindow.OnStartOpen().
        /// </summary>
        void OnViewOpening();

        /// <summary>
        /// Called when the window finishes opening and is fully interactive.
        /// Corresponds to UIWindow.OnFinishedOpen().
        /// </summary>
        void OnViewOpened();

        /// <summary>
        /// Called when the window starts closing (before transition animation).
        /// Corresponds to UIWindow.OnStartClose().
        /// </summary>
        void OnViewClosing();

        /// <summary>
        /// Called when the window finishes closing (after transition animation, before destruction).
        /// Corresponds to UIWindow.OnFinishedClose().
        /// </summary>
        void OnViewClosed();
    }
}