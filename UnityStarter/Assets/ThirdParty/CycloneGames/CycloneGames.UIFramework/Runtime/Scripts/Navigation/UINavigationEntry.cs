namespace CycloneGames.UIFramework.Runtime
{
    /// <summary>
    /// Defines how direct children are handled when a navigation node is removed.
    /// </summary>
    public enum ChildClosePolicy
    {
        /// <summary>Reconnect children to the removed node's active opener.</summary>
        Reparent,

        /// <summary>Remove the complete descendant subtree.</summary>
        Cascade,

        /// <summary>Keep children active and make them root nodes.</summary>
        Detach,
    }

    /// <summary>
    /// Immutable value snapshot of an active navigation node.
    /// </summary>
    public readonly struct UINavigationEntry
    {
        public string WindowId { get; }
        public string OpenerId { get; }
        public object Context { get; }
        public long Sequence { get; }

        public UINavigationEntry(string windowId, string openerId, object context, long sequence)
        {
            WindowId = windowId;
            OpenerId = openerId;
            Context = context;
            Sequence = sequence;
        }

        public override string ToString()
        {
            return $"{OpenerId ?? "ROOT"} -> {WindowId} ({Sequence})";
        }
    }
}
