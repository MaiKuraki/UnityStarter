using System;

namespace CycloneGames.Networking
{
    /// <summary>
    /// Bandwidth-aware send rate controller for large-scale multiplayer scenarios
    /// where client connection quality varies significantly (PC, mobile, WiFi, 4G).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Implementations should use <see cref="IBandwidthMeter"/> telemetry to
    /// dynamically adjust per-connection send frequency, snapshot compression,
    /// and AOI radius. The controller is a game-level concern; the interface
    /// defines the minimum contract the networking layer expects.
    /// </para>
    /// <para>
    /// Typical integration: the server's per-connection update loop queries
    /// <see cref="GetTargetSendInterval"/> before building the next snapshot,
    /// and adjusts entity update inclusion based on <see cref="GetPriorityBudget"/>.
    /// </para>
    /// </remarks>
    public interface IAdaptiveSendRate
    {
        /// <summary>
        /// Recommended interval in seconds between full state snapshots for the
        /// given connection. Returns a value between <c>MinSendInterval</c> and
        /// <c>MaxSendInterval</c> based on current bandwidth utilization and RTT.
        /// </summary>
        float GetTargetSendInterval(int connectionId);

        /// <summary>
        /// Priority budget for this frame: a value 0.0 (send nothing beyond
        /// critical state) to 1.0 (full replication). Use to decide which
        /// entities to include in partial snapshots.
        /// </summary>
        float GetPriorityBudget(int connectionId);

        /// <summary>
        /// Minimum send interval this controller will ever recommend.
        /// Typically equals the tick interval.
        /// </summary>
        float MinSendInterval { get; }

        /// <summary>
        /// Maximum send interval before the connection is considered stale.
        /// </summary>
        float MaxSendInterval { get; }

        /// <summary>
        /// Called each frame with the latest bandwidth telemetry and connection
        /// quality. Implementations should update their internal models here.
        /// </summary>
        void Update(int connectionId, in NetworkStatistics stats, ConnectionQuality quality, float deltaTime);

        /// <summary>
        /// Reset all internal state for a connection (on reconnect or session start).
        /// </summary>
        void ResetConnection(int connectionId);

        /// <summary>
        /// Fires when the controller drops a connection below the minimum
        /// acceptable quality threshold.
        /// </summary>
        event Action<int, string> OnConnectionDegraded;
    }
}
