namespace CycloneGames.UIFramework.Runtime
{
    /// <summary>
    /// [OPTIONAL EXTENSION POINT]
    /// 
    /// Interface for custom window binding logic when windows are created/destroyed.
    /// This is NOT currently integrated into UIManager - it exists as an extension point
    /// for advanced scenarios where you need direct access to UIWindow instances.
    /// 
    /// TYPICAL USE CASES (if integrated):
    /// - Injecting dependencies directly into UIWindow (not recommended - use Presenter instead)
    /// - Global UI analytics/logging
    /// - Custom resource preloading
    /// - Event subscription management
    /// 
    /// FOR MOST USERS:
    /// Use UIWindow&lt;TPresenter&gt; with UIPresenterFactory.CustomFactory for DI integration.
    /// This interface is only needed if you must inject into UIWindow itself.
    /// 
    /// TO INTEGRATE (if needed in future):
    /// 1. Add IUIWindowBinder reference to UIManager
    /// 2. Call OnWindowCreated() after instantiation in OpenUIAsync()
    /// 3. Call OnWindowDestroying() before destruction in CloseUIAsync()
    /// </summary>
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
    }
}
