using System;

namespace CycloneGames.Networking
{
    /// <summary>
    /// Represents an abstract network connection (client-to-server or server-to-client).
    /// </summary>
    public interface INetConnection : IEquatable<INetConnection>
    {
        int ConnectionId { get; }
        string RemoteAddress { get; }

        bool IsConnected { get; }

        /// <summary>
        /// True if the connection has passed authentication and is ready for gameplay messages.
        /// </summary>
        bool IsAuthenticated { get; }

        /// <summary>
        /// Round-trip time in milliseconds.
        /// </summary>
        int Ping { get; }

        /// <summary>
        /// Unique player identifier associated with this connection (e.g., Database ID).
        /// Useful for persistence and reconnection logic.
        /// </summary>
        ulong PlayerId { get; set; }
    }
}