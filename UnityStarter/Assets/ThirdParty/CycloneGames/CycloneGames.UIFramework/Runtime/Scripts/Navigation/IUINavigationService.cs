using System.Collections.Generic;

namespace CycloneGames.UIFramework.Runtime
{
    /// <summary>
    /// Maintains the causal graph of active windows.
    /// The service is confined to its owning main thread and performs no synchronization.
    /// Query buffers are owned by the caller and are cleared before results are copied.
    /// </summary>
    public interface IUINavigationService
    {
        /// <summary>Gets the most recently registered active window, or null.</summary>
        string CurrentWindow { get; }

        /// <summary>Gets whether the current window has an active opener.</summary>
        bool CanNavigateBack { get; }

        /// <summary>
        /// Registers a new active window.
        /// </summary>
        /// <returns>
        /// True when registration succeeds; false when the id is empty, already registered,
        /// self-referencing, or names an opener that is not active.
        /// </returns>
        bool Register(string windowId, string openerId = null, object context = null);

        /// <summary>
        /// Removes an active window and applies the requested child policy.
        /// <paramref name="affectedWindowIds"/> is cleared first. On success it contains the
        /// removed root and, for <see cref="ChildClosePolicy.Cascade"/>, all removed descendants
        /// in stable depth-first parent-before-child order.
        /// </summary>
        bool Unregister(
            string windowId,
            ChildClosePolicy policy,
            List<string> affectedWindowIds);

        /// <summary>Clears the graph and releases all context references.</summary>
        void Clear();

        /// <summary>Gets the active opener for a window, or null.</summary>
        string GetOpener(string windowId);

        /// <summary>Gets the context associated with a window, or null.</summary>
        object GetContext(string windowId);

        /// <summary>Gets the nearest active back-navigation target, or null.</summary>
        string ResolveBackTarget(string windowId);

        /// <summary>
        /// Copies active ancestors in oldest-to-newest order into a caller-owned buffer.
        /// The buffer is cleared before copying.
        /// </summary>
        int CopyAncestors(string windowId, List<string> destination);

        /// <summary>
        /// Copies immediate active children in deterministic registration order into a
        /// caller-owned buffer. The buffer is cleared before copying.
        /// </summary>
        int CopyChildren(string windowId, List<string> destination);

        /// <summary>
        /// Copies active entries in registration order into a caller-owned buffer.
        /// The buffer is cleared before copying.
        /// </summary>
        int CopyHistory(List<UINavigationEntry> destination);
    }
}
