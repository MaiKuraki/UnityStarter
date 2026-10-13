namespace CycloneGames.Networking
{
    public enum NetworkPerformanceTier : byte
    {
        /// <summary>Low-end mobile, older consoles, single-board devices.</summary>
        Low = 0,
        /// <summary>Mainstream mobile, current consoles, mid-range PC.</summary>
        Medium = 1,
        /// <summary>High-end PC and flagship mobile.</summary>
        High = 2,
        /// <summary>Dedicated server: maximize connection count and throughput.</summary>
        Server = 3
    }

    /// <summary>
    /// Tier-tuned runtime profiles. The knobs that actually scale with device
    /// capability are buffer/pool capacities (memory pressure vs. churn),
    /// send rate (bandwidth vs. freshness) and connection count; the protocol
    /// itself is identical on every tier so cross-tier sessions stay compatible.
    /// </summary>
    public static class NetworkDeviceProfiles
    {
        public const string LowTierProfileId = "tier-low";
        public const string MediumTierProfileId = "tier-medium";
        public const string HighTierProfileId = "tier-high";
        public const string ServerTierProfileId = "tier-server";

        public static NetworkRuntimeProfile ForTier(NetworkPerformanceTier tier)
        {
            NetworkRuntimeProfileBuilder builder = NetworkRuntimeProfiles.CreateDefaultBuilder();
            switch (tier)
            {
                case NetworkPerformanceTier.Low:
                    builder.ProfileId = LowTierProfileId;
                    builder.DisplayName = "Low Tier";
                    builder.MaxConnections = 32;
                    builder.SendRate = 15;
                    builder.PoolSize = 16;
                    builder.SnapshotBufferSize = 32;
                    builder.MaxPayloadBytes = NetworkConstants.DefaultMaxPayloadSize;
                    break;
                case NetworkPerformanceTier.Medium:
                    builder.ProfileId = MediumTierProfileId;
                    builder.DisplayName = "Medium Tier";
                    builder.MaxConnections = NetworkConstants.DefaultMaxConnections;
                    builder.SendRate = NetworkConstants.DefaultSendRate;
                    break;
                case NetworkPerformanceTier.High:
                    builder.ProfileId = HighTierProfileId;
                    builder.DisplayName = "High Tier";
                    builder.MaxConnections = 128;
                    builder.SendRate = 30;
                    builder.PoolSize = 64;
                    builder.SnapshotBufferSize = 128;
                    break;
                case NetworkPerformanceTier.Server:
                    builder.ProfileId = ServerTierProfileId;
                    builder.DisplayName = "Dedicated Server";
                    builder.MaxConnections = 1024;
                    builder.TickRate = 60;
                    builder.SendRate = 30;
                    builder.PoolSize = 256;
                    builder.SnapshotBufferSize = 256;
                    break;
                default:
                    throw new System.ArgumentOutOfRangeException(nameof(tier), tier, "Unknown performance tier.");
            }

            return builder.Build();
        }

        public static NetworkRuntimeProfileRegistry CreateTierRegistry()
        {
            var registry = new NetworkRuntimeProfileRegistry(capacity: 8);
            registry.Register(ForTier(NetworkPerformanceTier.Low));
            registry.Register(ForTier(NetworkPerformanceTier.Medium));
            registry.Register(ForTier(NetworkPerformanceTier.High));
            registry.Register(ForTier(NetworkPerformanceTier.Server));
            return registry;
        }

        /// <summary>
        /// Heuristic device classification from host-reported capabilities. Kept in
        /// Core as pure inputs so the Unity layer supplies SystemInfo values and the
        /// mapping stays unit-testable.
        /// </summary>
        public static NetworkPerformanceTier Classify(int logicalProcessorCount, long systemMemoryMB, bool isDedicatedServer)
        {
            if (isDedicatedServer)
                return NetworkPerformanceTier.Server;
            if (logicalProcessorCount <= 4 || systemMemoryMB <= 4096)
                return NetworkPerformanceTier.Low;
            if (logicalProcessorCount <= 8 || systemMemoryMB <= 16384)
                return NetworkPerformanceTier.Medium;
            return NetworkPerformanceTier.High;
        }
    }
}
