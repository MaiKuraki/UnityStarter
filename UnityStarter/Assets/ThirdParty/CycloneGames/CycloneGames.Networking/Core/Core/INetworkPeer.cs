using System;
using System.Collections.Generic;

namespace CycloneGames.Networking
{
    /// <summary>
    /// Extended connection interface with metadata, tags, and lifecycle management.
    /// </summary>
    public interface INetworkPeer : INetConnection
    {
        NetworkMode Mode { get; }
        DateTime ConnectedAt { get; }
        float ConnectionDuration { get; }

        // Arbitrary metadata for game-specific needs (room assignment, team, instance ID)
        IReadOnlyDictionary<string, object> Metadata { get; }
        void SetMetadata(string key, object value);
        T GetMetadata<T>(string key, T defaultValue = default);
        bool RemoveMetadata(string key);

        // Rate limiting
        int MessagesThisSecond { get; }
        long BytesThisSecond { get; }
    }
}
