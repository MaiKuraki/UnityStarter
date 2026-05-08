namespace CycloneGames.Networking
{
    /// <summary>
    /// Aggregated bandwidth and message rate telemetry for a network transport.
    /// Sampled per-second for diagnostics, adaptive quality, and congestion control.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The meter separates send and receive statistics. Values are computed over
    /// a rolling window (typically 1 second) to provide stable readings while
    /// remaining responsive to traffic changes.
    /// </para>
    /// <para>
    /// Use <see cref="BandwidthTrackingTransport"/> to wrap any <see cref="INetTransport"/>
    /// with automatic bandwidth tracking, or implement manually for custom transports.
    /// </para>
    /// </remarks>
    public interface IBandwidthMeter
    {
        /// <summary>Bytes sent in the current sampling window.</summary>
        long BytesSentPerSecond { get; }

        /// <summary>Bytes received in the current sampling window.</summary>
        long BytesReceivedPerSecond { get; }

        /// <summary>Number of send calls in the current sampling window.</summary>
        int MessagesSentPerSecond { get; }

        /// <summary>Number of received messages in the current sampling window.</summary>
        int MessagesReceivedPerSecond { get; }

        /// <summary>
        /// Ratio of current send bandwidth to estimated available bandwidth, 0.0 to 1.0.
        /// Values approaching 1.0 indicate the transport is saturating its channel.
        /// </summary>
        float SendUtilization { get; }

        /// <summary>
        /// Ratio of current receive bandwidth to estimated available bandwidth, 0.0 to 1.0.
        /// </summary>
        float ReceiveUtilization { get; }

        /// <summary>
        /// Reset all counters and rolling windows. Useful when the transport
        /// reconnects or the game transitions to a new session.
        /// </summary>
        void Reset();
    }
}
