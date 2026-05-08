using System;
using System.Collections.Generic;

namespace CycloneGames.Networking
{
    /// <summary>
    /// Resolves actor identity to process location for distributed deployments.
    /// Provides location-transparent message routing so gameplay code sends to
    /// an actor by ID without knowing which process hosts it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The default in-process implementation (<c>ActorRouteTable</c>) maps actor IDs
    /// to process IDs via an in-memory table, suitable for single-process development
    /// and testing. Replace with a distributed implementation (Redis, etcd, custom
    /// RPC-based lookup) for multi-process deployments.
    /// </para>
    /// <para>
    /// This interface lives in Networking.Core (no Unity dependency) so the router
    /// can be used from dedicated server processes that do not link Unity.
    /// </para>
    /// </remarks>
    public interface IActorRouter
    {
        /// <summary>
        /// Number of actor entries currently tracked by this router instance.
        /// </summary>
        int TrackedActorCount { get; }

        /// <summary>
        /// Register an actor's current location or update its existing mapping.
        /// Must be called when an actor is spawned or migrates into a process.
        /// </summary>
        /// <param name="actorId">Globally unique actor identifier.</param>
        /// <param name="processId">Identifier of the process currently hosting this actor.</param>
        void Register(int actorId, string processId);

        /// <summary>
        /// Remove an actor's location mapping.
        /// Must be called when an actor is destroyed or migrates out of a process.
        /// </summary>
        /// <param name="actorId">Actor identifier to unregister.</param>
        void Unregister(int actorId);

        /// <summary>
        /// Attempt to resolve an actor's current hosting process.
        /// </summary>
        /// <param name="actorId">Actor identifier to look up.</param>
        /// <param name="processId">Set to the hosting process ID if found; null otherwise.</param>
        /// <returns>True if the actor is registered in the routing table.</returns>
        bool TryResolve(int actorId, out string processId);

        /// <summary>
        /// Returns a snapshot of all currently registered actor-to-process mappings.
        /// Useful for debugging and diagnostics. The returned collection is a copy
        /// and safe to enumerate.
        /// </summary>
        IReadOnlyDictionary<int, string> GetAllMappings();

        /// <summary>
        /// Returns all actor IDs currently known to be hosted on the given process.
        /// </summary>
        IReadOnlyList<int> GetActorsOnProcess(string processId);
    }
}
