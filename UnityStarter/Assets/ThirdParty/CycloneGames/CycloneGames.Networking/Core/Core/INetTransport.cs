using System;
using System.Collections.Generic;

namespace CycloneGames.Networking
{
    /// <summary>
    /// Standardized Quality of Service (QoS) channels for game networking.
    /// </summary>
    public enum NetworkChannel
    {
        /// <summary>
        /// Guaranteed delivery, ordered. Use for critical gameplay events (RPCs, Spawning).
        /// </summary>
        Reliable,

        /// <summary>
        /// No guarantee, unordered (or sequenced). Use for high-frequency data (Position/Rotation updates).
        /// </summary>
        Unreliable,

        /// <summary>
        /// Reliable but unordered. Good for file transfer or chat where global order doesn't matter.
        /// </summary>
        ReliableUnordered,

        /// <summary>
        /// Unreliable but sequenced (older packets are dropped). Good for VoIP.
        /// </summary>
        UnreliableSequenced
    }

    /// <summary>
    /// Low-level transport interface responsible for raw byte delivery and connection lifecycle.
    /// </summary>
    public interface INetTransport
    {
        bool IsServer { get; }
        bool IsClient { get; }
        bool IsRunning { get; }
        bool IsEncrypted { get; }

        /// <summary>
        /// True if this transport is available on the current platform.
        /// </summary>
        bool Available { get; }

        // --- Channels ---

        /// <summary>
        /// Maps a standardized channel type to the underlying transport's integer channel ID.
        /// </summary>
        int GetChannelId(NetworkChannel channel);

        /// <summary>
        /// Maximum payload size for the given channel. Use for payload validation.
        /// </summary>
        int GetMaxPacketSize(int channelId);

        // --- Diagnostics ---

        /// <summary>
        /// Get current transport statistics for monitoring and debugging.
        /// </summary>
        NetworkStatistics GetStatistics();

        // --- Lifecycle Events ---

        /// <summary>
        /// Invoked on Server when a client connects.
        /// </summary>
        event Action<INetConnection> OnClientConnected;

        /// <summary>
        /// Invoked on Server when a client disconnects.
        /// </summary>
        event Action<INetConnection> OnClientDisconnected;

        /// <summary>
        /// Invoked on Client when successfully connected to server.
        /// </summary>
        event Action OnConnectedToServer;

        /// <summary>
        /// Invoked on Client when disconnected from server.
        /// </summary>
        event Action OnDisconnectedFromServer;

        /// <summary>
        /// Invoked when a transport error occurs. Connection may be null for client-side errors.
        /// </summary>
        event Action<INetConnection, TransportError, string> OnError;

        // --- Control ---

        void StartServer();
        void StartClient(string address);
        void Stop();

        /// <summary>
        /// Forcefully disconnects a connection (Server kicking client, or Client disconnecting self).
        /// </summary>
        void Disconnect(INetConnection connection);

        // --- Raw I/O ---

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
