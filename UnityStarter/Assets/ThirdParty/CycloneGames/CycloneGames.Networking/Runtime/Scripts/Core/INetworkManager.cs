using System;
using System.Collections.Generic;
using CycloneGames.Networking.Serialization;

namespace CycloneGames.Networking
{
    /// <summary>
    /// High-level networking manager that handles message registration, serialization, and transport management.
    /// Acts as the main entry point for gameplay logic.
    /// </summary>
    public interface INetworkManager
    {
        INetTransport Transport { get; }
        INetSerializer Serializer { get; }

        /// <summary>
        /// Registers a handler for a specific message ID.
        /// The handler receives the connection source and the deserialized message.
        /// </summary>
        void RegisterHandler<T>(ushort msgId, Action<INetConnection, T> handler) where T : struct;

        /// <summary>
        /// Unregisters a handler for a specific message ID.
        /// </summary>
        void UnregisterHandler(ushort msgId);

        /// <summary>
        /// Sends a message to the server.
        /// </summary>
        /// <param name="channel">QoS channel to use (default Reliable).</param>
        void SendToServer<T>(ushort msgId, T message, NetworkChannel channel = NetworkChannel.Reliable) where T : struct;

        /// <summary>
        /// Sends a message to a specific client connection.
        /// </summary>
        /// <param name="channel">QoS channel to use (default Reliable).</param>
        void SendToClient<T>(INetConnection connection, ushort msgId, T message, NetworkChannel channel = NetworkChannel.Reliable) where T : struct;

        /// <summary>
        /// Broadcasts a message to all connected clients.
        /// </summary>
        /// <param name="channel">QoS channel to use (default Reliable).</param>
        void BroadcastToClients<T>(ushort msgId, T message, NetworkChannel channel = NetworkChannel.Reliable) where T : struct;

        /// <summary>
        /// Broadcasts a message to a list of connections.
        /// </summary>
        /// <param name="channel">QoS channel to use (default Reliable).</param>
        void Broadcast<T>(IReadOnlyList<INetConnection> connections, ushort msgId, T message, NetworkChannel channel = NetworkChannel.Reliable) where T : struct;

        /// <summary>
        /// Disconnects a client.
        /// </summary>
        void DisconnectClient(INetConnection connection);
    }
}
