using System.Collections.Generic;

namespace CycloneGames.Networking.Interest
{
    /// <summary>
    /// Determines which network entities are relevant to each connection.
    /// Essential for large worlds (GTA, ARK, Minecraft, WoW, FFXIV, 64-player sandbox)
    /// to limit bandwidth and reduce client processing.
    /// </summary>
    public interface IInterestManager
    {
        /// <summary>
        /// Rebuild the interest set for a connection. Called each server tick or at configurable intervals.
        /// </summary>
        /// <param name="connection">The observer connection</param>
        /// <param name="allEntities">All spawned network entities</param>
        /// <param name="results">Output set of entity IDs visible to this connection. Must be pre-allocated.</param>
        void RebuildForConnection(INetConnection connection, IReadOnlyList<INetworkEntity> allEntities, HashSet<uint> results);

        /// <summary>
        /// Check if a specific entity is visible to a connection without full rebuild.
        /// </summary>
        bool IsVisible(INetConnection connection, INetworkEntity entity);

        /// <summary>
        /// Global update called once per tick before per-connection rebuilds.
        /// Use for spatial data structure maintenance (grid rehash, octree rebuild, etc.)
        /// </summary>
        void PreUpdate(IReadOnlyList<INetworkEntity> allEntities);
    }

    /// <summary>
    /// Minimal interface for network-synchronized entities tracked by interest management.
    /// </summary>
    public interface INetworkEntity
    {
        uint NetworkId { get; }
        UnityEngine.Vector3 Position { get; }

        /// <summary>
        /// Owner connection ID. -1 if server-owned (NPCs, world objects).
        /// </summary>
        int OwnerConnectionId { get; }

        /// <summary>
        /// If true, this entity is always visible to all connections (global chat, world events).
        /// </summary>
        bool AlwaysRelevant { get; }

        /// <summary>
        /// Custom relevance groups (party, guild, instance, room).
        /// Entities sharing a group with a connection's groups are always visible.
        /// </summary>
        int RelevanceGroup { get; }
    }
}
