using System;

namespace CycloneGames.Networking
{
    /// <summary>
    /// Instance-scoped deployment defaults. NetworkConstants holds protocol
    /// invariants and baseline values only; products that need different
    /// budgets per session, region or composition root construct one of these
    /// and pass it to NetworkRuntimeProfileBuilder instead of mutating a
    /// process-wide constant.
    /// </summary>
    public sealed class NetworkDeploymentDefaults
    {
        public NetworkDeploymentDefaults(
            int maxConnections = NetworkConstants.DefaultMaxConnections,
            int tickRate = NetworkConstants.DefaultTickRate,
            int sendRate = NetworkConstants.DefaultSendRate,
            int mtu = NetworkConstants.DefaultMTU,
            int maxPayloadBytes = NetworkConstants.DefaultMaxPayloadSize,
            int bufferSize = NetworkConstants.DefaultBufferSize,
            int poolSize = NetworkConstants.DefaultPoolSize,
            int snapshotBufferSize = NetworkConstants.MaxSnapshotBufferSize,
            int sessionSearchMaxResults = NetworkConstants.DefaultSessionSearchMaxResults,
            float timeoutSeconds = NetworkConstants.DefaultTimeoutSeconds,
            float heartbeatIntervalSeconds = NetworkConstants.DefaultHeartbeatInterval,
            float disconnectTimeoutSeconds = NetworkConstants.DefaultDisconnectTimeout,
            double reconnectWindowSeconds = NetworkConstants.DefaultReconnectWindowSeconds,
            double hostMigrationTimeoutSeconds = NetworkConstants.DefaultHostMigrationTimeoutSeconds)
        {
            MaxConnections = maxConnections;
            TickRate = tickRate;
            SendRate = sendRate;
            Mtu = mtu;
            MaxPayloadBytes = maxPayloadBytes;
            BufferSize = bufferSize;
            PoolSize = poolSize;
            SnapshotBufferSize = snapshotBufferSize;
            SessionSearchMaxResults = sessionSearchMaxResults;
            TimeoutSeconds = timeoutSeconds;
            HeartbeatIntervalSeconds = heartbeatIntervalSeconds;
            DisconnectTimeoutSeconds = disconnectTimeoutSeconds;
            ReconnectWindowSeconds = reconnectWindowSeconds;
            HostMigrationTimeoutSeconds = hostMigrationTimeoutSeconds;
        }

        public int MaxConnections { get; }
        public int TickRate { get; }
        public int SendRate { get; }
        public int Mtu { get; }
        public int MaxPayloadBytes { get; }
        public int BufferSize { get; }
        public int PoolSize { get; }
        public int SnapshotBufferSize { get; }
        public int SessionSearchMaxResults { get; }
        public float TimeoutSeconds { get; }
        public float HeartbeatIntervalSeconds { get; }
        public float DisconnectTimeoutSeconds { get; }
        public double ReconnectWindowSeconds { get; }
        public double HostMigrationTimeoutSeconds { get; }

        /// <summary>Baseline defaults derived from the protocol constants.</summary>
        public static NetworkDeploymentDefaults CreateStandard() => new NetworkDeploymentDefaults();
    }
}
