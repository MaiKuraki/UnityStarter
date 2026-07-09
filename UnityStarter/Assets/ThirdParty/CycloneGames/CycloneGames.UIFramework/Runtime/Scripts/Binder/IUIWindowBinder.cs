namespace CycloneGames.UIFramework.Runtime
{
    /// <summary>
    /// [OPTIONAL EXTENSION POINT]
    /// 
    /// Interface for custom window binding logic when windows are created/destroyed.
    /// UIManager invokes registered binders during window creation, destruction, and
    /// lifecycle state transitions.
    /// 
    /// TYPICAL USE CASES:
    /// - Optional MVP presenter binding for selected windows
    /// - Dependency injection into UIWindow components at the composition boundary
    /// - Global UI analytics/logging
    /// - Custom resource preloading
    /// - Event subscription management
    /// 
    /// Classic UIWindow usage does not require a binder. Register binders only for
    /// project-level integrations that should observe all windows.
    /// </summary>
    public enum WindowStateCallbackType
    {
        OnStartOpen,
        OnFinishedOpen,
        OnStartClose,
        OnFinishedClose
    }

    public interface IUIWindowBinder
    {
        /// <summary>
        /// Called immediately after a window is instantiated, before Open() is called.
        /// Use for dependency injection, presenter binding, or custom initialization.
        /// </summary>
        void OnWindowCreated(UIWindow window);

        /// <summary>
        /// Called when a window is about to be destroyed.
        /// Use for cleanup, releasing resources, or notifying DI containers.
        /// </summary>
        void OnWindowDestroying(UIWindow window);

        /// <summary>
        /// Called when the Window transitions state.
        /// </summary>
        void OnWindowStateChanged(UIWindow window, WindowStateCallbackType state);
    }
}
