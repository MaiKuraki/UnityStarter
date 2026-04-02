using System.Collections.Generic;

namespace CycloneGames.UIFramework.Runtime
{
    /// <summary>
    /// Tracks the causal graph of UI window openings.
    /// All query methods are safe to call from any thread.
    /// All mutation methods (Register / Unregister / Clear) must be called from the main thread.
    /// </summary>
    public interface IUINavigationService : System.IDisposable
    {
        /// <summary>Returns the most recently opened window that is still alive, or null.</summary>
        string CurrentWindow { get; }

        /// <summary>True when at least one window has a resolvable back-navigation target.</summary>
        bool CanNavigateBack { get; }

        /// <summary>
        /// Registers a window as it becomes active.
        /// Must be called on the main thread immediately after the window is opened.
        /// </summary>
        /// <param name="windowName">The name of the newly opened window.</param>
        /// <param name="openerName">The name of the window that triggered this open, or null for root.</param>
        /// <param name="context">Optional payload for the new window to query later.</param>
        void Register(string windowName, string openerName = null, object context = null);

        /// <summary>
        /// Unregisters a window as it is closing.
        /// Must be called on the main thread before the window is destroyed.
        /// </summary>
        /// <param name="windowName">The window that is closing.</param>
        /// <param name="policy">Governs what happens to children of this node.</param>
        void Unregister(string windowName, ChildClosePolicy policy = ChildClosePolicy.Reparent);

        /// <summary>Clears the entire navigation graph (e.g., on main-menu reset).</summary>
        void Clear();

        // ── Queries ─────────────────────────────────────────────────────────────

        /// <summary>Returns the opener name for the given window, or null if none.</summary>
        string GetOpener(string windowName);

        /// <summary>
        /// Returns the context payload that was passed when the given window was opened.
        /// Returns null if the window was opened without a context or is not registered.
        /// </summary>
        object GetContext(string windowName);

        /// <summary>
        /// Returns the full ancestor chain for the window, from oldest to newest opener.
        /// Only includes windows that are still registered (alive).
        /// Thread-safe snapshot — the returned list is a fresh allocation.
        /// </summary>
        List<string> GetAncestors(string windowName);

        /// <summary>
        /// Returns all immediate children of the given window that are still alive.
        /// Thread-safe snapshot — the returned list is a fresh allocation.
        /// </summary>
        List<string> GetChildren(string windowName);

        /// <summary>
        /// Resolves the best "back" target for the given window in the current graph state.
        /// Returns null if no valid target exists.
        /// </summary>
        string ResolveBackTarget(string windowName);

        /// <summary>
        /// Ordered flat view of the navigation history (registered order, oldest first).
        /// Thread-safe snapshot — the returned list is a fresh allocation.
        /// </summary>
        List<UINavigationEntry> GetHistory();
    }
}
