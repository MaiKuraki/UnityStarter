using System;

namespace CycloneGames.InputSystem.Runtime
{
    /// <summary>
    /// Copy-on-write handler arrays for events that must not allocate when they fire. Adding or removing a handler
    /// allocates one array; invocation only reads a snapshot.
    /// </summary>
    /// <remarks>
    /// The array is replaced rather than mutated, so a handler may subscribe or unsubscribe during a notification
    /// without disturbing the pass in progress. The last registered handler is invoked last.
    /// </remarks>
    internal static class HandlerList
    {
        internal static T[] Append<T>(T[] handlers, T handler) where T : Delegate
        {
            if (handler == null) return handlers;

            var updated = new T[handlers.Length + 1];
            Array.Copy(handlers, updated, handlers.Length);
            updated[handlers.Length] = handler;
            return updated;
        }

        internal static T[] Remove<T>(T[] handlers, T handler) where T : Delegate
        {
            if (handler == null) return handlers;

            for (int i = handlers.Length - 1; i >= 0; i--)
            {
                if (!handlers[i].Equals(handler)) continue;
                if (handlers.Length == 1) return Array.Empty<T>();

                var updated = new T[handlers.Length - 1];
                Array.Copy(handlers, 0, updated, 0, i);
                Array.Copy(handlers, i + 1, updated, i, handlers.Length - i - 1);
                return updated;
            }

            return handlers;
        }
    }
}
