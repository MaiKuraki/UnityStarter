using System;

namespace CycloneGames.UIFramework.Runtime
{
    /// <summary>
    /// Defines how children of a closing node are handled.
    /// </summary>
    public enum ChildClosePolicy
    {
        /// <summary>Surviving children are re-parented to the closing node's opener.</summary>
        Reparent,
        /// <summary>All children (and their descendants) are forcibly closed.</summary>
        Cascade,
        /// <summary>Children are fully detached and become roots with no back-navigation target.</summary>
        Detach,
    }

    /// <summary>
    /// An immutable snapshot of a single UI navigation node in the navigation graph.
    /// Stored by value to avoid heap allocations per entry.
    /// </summary>
    public readonly struct UINavigationEntry
    {
        public readonly string WindowName;
        public readonly string OpenerName;  // null means this window was opened as a root
        public readonly object Context;     // optional payload passed by the opener
        public readonly DateTime OpenedAt;

        public UINavigationEntry(string windowName, string openerName, object context)
        {
            WindowName = windowName;
            OpenerName = openerName;
            Context    = context;
            OpenedAt   = DateTime.UtcNow;
        }

        public override string ToString() => $"{OpenerName ?? "ROOT"} → {WindowName}";
    }
}
