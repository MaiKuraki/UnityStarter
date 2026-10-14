using System;
using System.Collections.Generic;

namespace CycloneGames.EventBus.Core
{
    /// <summary>
    /// Snapshot of one live subscription, produced by <see cref="IEventBusSubscriberInspection"/>.
    /// Diagnostics tooling only; never allocated on the dispatch path.
    /// </summary>
    public struct EventSubscriberInfo
    {
        /// <summary>Handler method name. May be stripped in a Player build; Editor tooling is the consumer.</summary>
        public string MethodName;

        /// <summary>Name of the type declaring the handler method.</summary>
        public string DeclaringTypeName;

        /// <summary>
        /// Delegate target, or null for a static handler. Held so tooling can apply engine-level
        /// liveness checks (e.g. a destroyed Unity object); never retained by the bus itself.
        /// </summary>
        public object Target;
    }

    /// <summary>
    /// Cold-path subscriber inspection port, separate from <see cref="IEventBusDiagnostics"/> so the
    /// counters contract stays stable. Owner thread only, like every other bus member. Reads
    /// delegate metadata — intended for Editor diagnostics; do not call per frame in a Player.
    /// </summary>
    public interface IEventBusSubscriberInspection
    {
        /// <summary>Appends the live subscribers in slot order. Tombstones are skipped.</summary>
        void CopySubscribersTo(List<EventSubscriberInfo> destination);
    }
}
