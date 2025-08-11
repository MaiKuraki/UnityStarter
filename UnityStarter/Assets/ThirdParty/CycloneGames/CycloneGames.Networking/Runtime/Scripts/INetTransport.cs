using System;
using System.Collections.Generic;

namespace CycloneGames.Networking
{
    public interface INetTransport
    {
        bool IsServer { get; }
        bool IsClient { get; }
        bool IsEncrypted { get; }

        /// <summary>
        /// Reliable channel id (implementation-defined). Use for important state.
        /// </summary>
        int ReliableChannel { get; }

        /// <summary>
        /// Unreliable channel id (implementation-defined). Use for frequent transforms.
        /// </summary>
        int UnreliableChannel { get; }

        /// <summary>
        /// Send a raw payload to a connection using given channel.
        /// Must be zero-allocation in hot paths.
        /// </summary>
        void Send(INetConnection connection, in ArraySegment<byte> payload, int channelId);

        /// <summary>
        /// Broadcast to many connections using given channel.
        /// Implementations should batch for efficiency.
        /// </summary>
        void Broadcast(IReadOnlyList<INetConnection> connections, in ArraySegment<byte> payload, int channelId);
    }
}