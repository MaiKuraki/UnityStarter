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
        /// Connection quality based on latency and stability.
        /// </summary>
        ConnectionQuality Quality { get; }

        /// <summary>
        /// RTT variance in milliseconds. High jitter indicates unstable connection.
        /// </summary>
        double Jitter { get; }

        /// <summary>
        /// Total bytes sent through this connection.
        /// </summary>
        long BytesSent { get; }

        /// <summary>
        /// Total bytes received through this connection.
        /// </summary>
        long BytesReceived { get; }

        /// <summary>
        /// Unique player identifier associated with this connection (e.g., Database ID).
        /// Useful for persistence and reconnection logic.
        /// </summary>
        ulong PlayerId { get; set; }
    }
}
