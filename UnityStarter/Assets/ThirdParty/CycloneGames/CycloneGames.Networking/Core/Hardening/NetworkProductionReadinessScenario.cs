using System;
using System.Collections.Generic;

namespace CycloneGames.Networking
{
    public sealed class NetworkProductionReadinessScenario
    {
        private readonly NetworkCapability[] _requiredCapabilities;
        private readonly NetworkCapabilityId[] _preferredCapabilities;
        private readonly NetworkFaultRequirement[] _requiredFaults;
        private readonly Dictionary<string, string> _metadata;

        internal NetworkProductionReadinessScenario(NetworkProductionReadinessScenarioBuilder builder)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            ScenarioId = builder.ScenarioId;
            DisplayName = string.IsNullOrEmpty(builder.DisplayName) ? ScenarioId.ToString() : builder.DisplayName;
            Description = builder.Description ?? string.Empty;
            MinimumProfileConnections = ValidateNonNegative(builder.MinimumProfileConnections, nameof(builder.MinimumProfileConnections));
            MinimumNodeConnections = ValidateNonNegative(builder.MinimumNodeConnections, nameof(builder.MinimumNodeConnections));
            MinimumSameAreaConnections = ValidateNonNegative(builder.MinimumSameAreaConnections, nameof(builder.MinimumSameAreaConnections));
            MinimumTickRate = ValidateNonNegative(builder.MinimumTickRate, nameof(builder.MinimumTickRate));
            MinimumSendRate = ValidateNonNegative(builder.MinimumSendRate, nameof(builder.MinimumSendRate));
            MinimumPayloadBytes = ValidateNonNegative(builder.MinimumPayloadBytes, nameof(builder.MinimumPayloadBytes));
            MinimumSessionSearchResults = ValidateNonNegative(builder.MinimumSessionSearchResults, nameof(builder.MinimumSessionSearchResults));
            MinimumReconnectWindowSeconds = ValidateNonNegative(builder.MinimumReconnectWindowSeconds, nameof(builder.MinimumReconnectWindowSeconds));
            MinimumHostMigrationTimeoutSeconds = ValidateNonNegative(builder.MinimumHostMigrationTimeoutSeconds, nameof(builder.MinimumHostMigrationTimeoutSeconds));
            MinimumProtocolManifestCount = ValidateNonNegative(builder.MinimumProtocolManifestCount, nameof(builder.MinimumProtocolManifestCount));
            RequireProtocolManifest = builder.RequireProtocolManifest;
            RequiredRegion = builder.RequiredRegion ?? string.Empty;
            RequiredPlatform = builder.RequiredPlatform ?? string.Empty;
            _requiredCapabilities = builder.RequiredCapabilities.ToArray();
            _preferredCapabilities = builder.PreferredCapabilities.ToArray();
            _requiredFaults = builder.RequiredFaults.ToArray();
            _metadata = new Dictionary<string, string>(builder.Metadata, StringComparer.Ordinal);
        }

        public NetworkHardeningScenarioId ScenarioId { get; }
        public string DisplayName { get; }
        public string Description { get; }
        public int MinimumProfileConnections { get; }
        public int MinimumNodeConnections { get; }
        public int MinimumSameAreaConnections { get; }
        public int MinimumTickRate { get; }
        public int MinimumSendRate { get; }
        public int MinimumPayloadBytes { get; }
        public int MinimumSessionSearchResults { get; }
        public double MinimumReconnectWindowSeconds { get; }
        public double MinimumHostMigrationTimeoutSeconds { get; }
        public int MinimumProtocolManifestCount { get; }
        public bool RequireProtocolManifest { get; }
        public string RequiredRegion { get; }
        public string RequiredPlatform { get; }

        public IReadOnlyList<NetworkCapability> RequiredCapabilities
        {
            get
            {
                return _requiredCapabilities;
            }
        }

        public IReadOnlyList<NetworkCapabilityId> PreferredCapabilities
        {
            get
            {
                return _preferredCapabilities;
            }
        }

        public IReadOnlyList<NetworkFaultRequirement> RequiredFaults
        {
            get
            {
                return _requiredFaults;
            }
        }

        public IReadOnlyDictionary<string, string> Metadata
        {
            get
            {
                return _metadata;
            }
        }

        public NetworkCapabilityQuery CreateCapabilityQuery()
        {
            var query = new NetworkCapabilityQuery
            {
                MinimumConnections = MinimumNodeConnections,
                MinimumPayloadBytes = MinimumPayloadBytes,
                Region = RequiredRegion,
                Platform = RequiredPlatform
            };

            for (int i = 0; i < _requiredCapabilities.Length; i++)
            {
                NetworkCapability required = _requiredCapabilities[i];
                query.Require(required.Id, required.Level);
            }

            for (int i = 0; i < _preferredCapabilities.Length; i++)
            {
                query.Prefer(_preferredCapabilities[i]);
            }

            return query;
        }

        private static int ValidateNonNegative(int value, string name)
        {
            if (value < 0)
            {
                throw new ArgumentOutOfRangeException(name);
            }

            return value;
        }

        private static double ValidateNonNegative(double value, string name)
        {
            if (value < 0d || double.IsNaN(value))
            {
                throw new ArgumentOutOfRangeException(name);
            }

            return value;
        }
    }

    public sealed class NetworkProductionReadinessScenarioBuilder
    {
        internal readonly List<NetworkCapability> RequiredCapabilities = new List<NetworkCapability>(8);
        internal readonly List<NetworkCapabilityId> PreferredCapabilities = new List<NetworkCapabilityId>(8);
        internal readonly List<NetworkFaultRequirement> RequiredFaults = new List<NetworkFaultRequirement>(8);
        internal readonly Dictionary<string, string> Metadata = new Dictionary<string, string>(StringComparer.Ordinal);

        public NetworkHardeningScenarioId ScenarioId { get; set; } = new NetworkHardeningScenarioId("custom");
        public string DisplayName { get; set; } = "Custom";
        public string Description { get; set; } = string.Empty;
        public int MinimumProfileConnections { get; set; }
        public int MinimumNodeConnections { get; set; }
        public int MinimumSameAreaConnections { get; set; }
        public int MinimumTickRate { get; set; }
        public int MinimumSendRate { get; set; }
        public int MinimumPayloadBytes { get; set; }
        public int MinimumSessionSearchResults { get; set; }
        public double MinimumReconnectWindowSeconds { get; set; }
        public double MinimumHostMigrationTimeoutSeconds { get; set; }
        public int MinimumProtocolManifestCount { get; set; }
        public bool RequireProtocolManifest { get; set; }
        public string RequiredRegion { get; set; } = string.Empty;
        public string RequiredPlatform { get; set; } = string.Empty;

        public NetworkProductionReadinessScenarioBuilder RequireCapability(NetworkCapabilityId id, int minimumLevel = 1)
        {
            RequiredCapabilities.Add(new NetworkCapability(id, minimumLevel));
            return this;
        }

        public NetworkProductionReadinessScenarioBuilder PreferCapability(NetworkCapabilityId id)
        {
            PreferredCapabilities.Add(id);
            return this;
        }

        public NetworkProductionReadinessScenarioBuilder RequireFault(
            NetworkFaultId id,
            NetworkReadinessSeverity severity = NetworkReadinessSeverity.Required,
            double minimumDurationSeconds = 0d,
            double minimumIntensity = 0d,
            string description = "")
        {
            RequiredFaults.Add(new NetworkFaultRequirement(
                id,
                severity,
                minimumDurationSeconds,
                minimumIntensity,
                description));
            return this;
        }

        public NetworkProductionReadinessScenarioBuilder SetMetadata(string key, string value)
        {
            if (string.IsNullOrEmpty(key))
            {
                throw new ArgumentException("Scenario metadata key must not be null or empty.", nameof(key));
            }

            Metadata[key] = value ?? string.Empty;
            return this;
        }

        public NetworkProductionReadinessScenario Build()
        {
            return new NetworkProductionReadinessScenario(this);
        }
    }

    public static class NetworkProductionReadinessScenarios
    {
        private const int SMALL_SESSION_CONNECTIONS = 8;
        private const int MEDIUM_AREA_CONNECTIONS = 100;
        private const int LARGE_AREA_CONNECTIONS = 1000;
        private const int MASSIVE_AREA_CONNECTIONS = 10000;
        private const int DEFAULT_AUTHORITATIVE_TICK_RATE = 30;
        private const int DEFAULT_ACTION_SEND_RATE = 20;
        private const int DEFAULT_ROOM_SEARCH_RESULTS = 50;
        private const double DEFAULT_RECONNECT_WINDOW_SECONDS = 180d;
        private const double DEFAULT_HOST_MIGRATION_WINDOW_SECONDS = 8d;
        private const double DEFAULT_FAULT_DURATION_SECONDS = 30d;

        public static NetworkProductionReadinessScenarioBuilder CreateSmallSessionBuilder()
        {
            return new NetworkProductionReadinessScenarioBuilder
            {
                ScenarioId = new NetworkHardeningScenarioId("session.small"),
                DisplayName = "Small Session",
                Description = "Small peer, listen-server, LAN, platform lobby, or relay-backed session.",
                MinimumProfileConnections = SMALL_SESSION_CONNECTIONS,
                MinimumNodeConnections = SMALL_SESSION_CONNECTIONS,
                MinimumSameAreaConnections = SMALL_SESSION_CONNECTIONS,
                MinimumTickRate = DEFAULT_AUTHORITATIVE_TICK_RATE,
                MinimumSendRate = DEFAULT_ACTION_SEND_RATE,
                MinimumSessionSearchResults = DEFAULT_ROOM_SEARCH_RESULTS,
                MinimumReconnectWindowSeconds = DEFAULT_RECONNECT_WINDOW_SECONDS,
                MinimumHostMigrationTimeoutSeconds = DEFAULT_HOST_MIGRATION_WINDOW_SECONDS,
                RequireProtocolManifest = true,
                MinimumProtocolManifestCount = 1
            }
            .RequireCapability(NetworkCapabilityIds.RealtimeTransport)
            .PreferCapability(NetworkCapabilityIds.LanDiscovery)
            .PreferCapability(NetworkCapabilityIds.PlatformLobby)
            .PreferCapability(NetworkCapabilityIds.HostMigration)
            .RequireFault(NetworkFaultIds.ClientDisconnect, minimumDurationSeconds: DEFAULT_FAULT_DURATION_SECONDS)
            .RequireFault(NetworkFaultIds.HostDisconnect, minimumDurationSeconds: DEFAULT_FAULT_DURATION_SECONDS)
            .RequireFault(NetworkFaultIds.PacketLoss, NetworkReadinessSeverity.Warning, DEFAULT_FAULT_DURATION_SECONDS);
        }

        public static NetworkProductionReadinessScenarioBuilder CreateAuthoritativeArenaBuilder()
        {
            return new NetworkProductionReadinessScenarioBuilder
            {
                ScenarioId = new NetworkHardeningScenarioId("session.authoritative_arena"),
                DisplayName = "Authoritative Arena",
                Description = "Server-authoritative action session with prediction, rollback, reconnection, and replay validation.",
                MinimumProfileConnections = MEDIUM_AREA_CONNECTIONS,
                MinimumNodeConnections = MEDIUM_AREA_CONNECTIONS,
                MinimumSameAreaConnections = MEDIUM_AREA_CONNECTIONS,
                MinimumTickRate = DEFAULT_AUTHORITATIVE_TICK_RATE,
                MinimumSendRate = DEFAULT_ACTION_SEND_RATE,
                MinimumPayloadBytes = NetworkConstants.DefaultMaxPayloadSize,
                MinimumReconnectWindowSeconds = DEFAULT_RECONNECT_WINDOW_SECONDS,
                RequireProtocolManifest = true,
                MinimumProtocolManifestCount = 1
            }
            .RequireCapability(NetworkCapabilityIds.RealtimeTransport)
            .RequireCapability(NetworkCapabilityIds.AuthoritativeSimulation)
            .PreferCapability(NetworkCapabilityIds.Reconnection)
            .PreferCapability(NetworkCapabilityIds.Spectator)
            .RequireFault(NetworkFaultIds.Latency, minimumDurationSeconds: DEFAULT_FAULT_DURATION_SECONDS)
            .RequireFault(NetworkFaultIds.Jitter, NetworkReadinessSeverity.Warning, DEFAULT_FAULT_DURATION_SECONDS)
            .RequireFault(NetworkFaultIds.PacketLoss, minimumDurationSeconds: DEFAULT_FAULT_DURATION_SECONDS)
            .RequireFault(NetworkFaultIds.ProtocolMismatch, NetworkReadinessSeverity.Warning);
        }

        public static NetworkProductionReadinessScenarioBuilder CreateLargeAreaBuilder()
        {
            return new NetworkProductionReadinessScenarioBuilder
            {
                ScenarioId = new NetworkHardeningScenarioId("world.large_area"),
                DisplayName = "Large Area",
                Description = "Large single-area session that requires AOI, send budgets, and backend-aware capacity planning.",
                MinimumProfileConnections = LARGE_AREA_CONNECTIONS,
                MinimumNodeConnections = LARGE_AREA_CONNECTIONS,
                MinimumSameAreaConnections = LARGE_AREA_CONNECTIONS,
                MinimumTickRate = DEFAULT_AUTHORITATIVE_TICK_RATE,
                MinimumSendRate = DEFAULT_ACTION_SEND_RATE,
                MinimumPayloadBytes = NetworkConstants.DefaultMaxPayloadSize,
                MinimumReconnectWindowSeconds = DEFAULT_RECONNECT_WINDOW_SECONDS,
                RequireProtocolManifest = true,
                MinimumProtocolManifestCount = 1
            }
            .RequireCapability(NetworkCapabilityIds.RealtimeTransport)
            .RequireCapability(NetworkCapabilityIds.AuthoritativeSimulation)
            .PreferCapability(NetworkCapabilityIds.Sharding)
            .PreferCapability(NetworkCapabilityIds.Persistence)
            .RequireFault(NetworkFaultIds.BandwidthCap, minimumDurationSeconds: DEFAULT_FAULT_DURATION_SECONDS)
            .RequireFault(NetworkFaultIds.ReconnectStorm, minimumDurationSeconds: DEFAULT_FAULT_DURATION_SECONDS)
            .RequireFault(NetworkFaultIds.BackendUnavailable, NetworkReadinessSeverity.Warning, DEFAULT_FAULT_DURATION_SECONDS);
        }

        public static NetworkProductionReadinessScenarioBuilder CreateMassiveShardBuilder()
        {
            return new NetworkProductionReadinessScenarioBuilder
            {
                ScenarioId = new NetworkHardeningScenarioId("world.massive_shard"),
                DisplayName = "Massive Shard",
                Description = "Massive world deployment that should be proven through shard, zone, fleet, and gateway capacity tests.",
                MinimumProfileConnections = MASSIVE_AREA_CONNECTIONS,
                MinimumNodeConnections = MASSIVE_AREA_CONNECTIONS,
                MinimumSameAreaConnections = MASSIVE_AREA_CONNECTIONS,
                MinimumTickRate = DEFAULT_AUTHORITATIVE_TICK_RATE,
                MinimumSendRate = DEFAULT_ACTION_SEND_RATE,
                MinimumPayloadBytes = NetworkConstants.DefaultMaxPayloadSize,
                MinimumReconnectWindowSeconds = DEFAULT_RECONNECT_WINDOW_SECONDS,
                RequireProtocolManifest = true,
                MinimumProtocolManifestCount = 1
            }
            .RequireCapability(NetworkCapabilityIds.RealtimeTransport)
            .RequireCapability(NetworkCapabilityIds.AuthoritativeSimulation)
            .RequireCapability(NetworkCapabilityIds.Sharding)
            .PreferCapability(NetworkCapabilityIds.ZoneTransfer)
            .PreferCapability(NetworkCapabilityIds.Persistence)
            .RequireFault(NetworkFaultIds.BandwidthCap, minimumDurationSeconds: DEFAULT_FAULT_DURATION_SECONDS)
            .RequireFault(NetworkFaultIds.ReconnectStorm, minimumDurationSeconds: DEFAULT_FAULT_DURATION_SECONDS)
            .RequireFault(NetworkFaultIds.BackendUnavailable, minimumDurationSeconds: DEFAULT_FAULT_DURATION_SECONDS)
            .RequireFault(NetworkFaultIds.ClockDrift, NetworkReadinessSeverity.Warning, DEFAULT_FAULT_DURATION_SECONDS);
        }

        public static NetworkProductionReadinessScenarioBuilder CreateWebMobileBuilder()
        {
            return new NetworkProductionReadinessScenarioBuilder
            {
                ScenarioId = new NetworkHardeningScenarioId("platform.web_mobile"),
                DisplayName = "Web and Mobile",
                Description = "Browser and mobile profile that covers WebGL throttling, suspend/resume, reconnect, and relay constraints.",
                MinimumProfileConnections = SMALL_SESSION_CONNECTIONS,
                MinimumNodeConnections = SMALL_SESSION_CONNECTIONS,
                MinimumSameAreaConnections = SMALL_SESSION_CONNECTIONS,
                MinimumTickRate = DEFAULT_AUTHORITATIVE_TICK_RATE,
                MinimumSendRate = DEFAULT_ACTION_SEND_RATE,
                MinimumReconnectWindowSeconds = DEFAULT_RECONNECT_WINDOW_SECONDS,
                RequireProtocolManifest = true,
                MinimumProtocolManifestCount = 1
            }
            .RequireCapability(NetworkCapabilityIds.RealtimeTransport)
            .PreferCapability(NetworkCapabilityIds.WebGLCompatible)
            .PreferCapability(NetworkCapabilityIds.MobileSuspendRecovery)
            .PreferCapability(NetworkCapabilityIds.Relay)
            .RequireFault(NetworkFaultIds.MobileSuspend, NetworkReadinessSeverity.Warning, DEFAULT_FAULT_DURATION_SECONDS)
            .RequireFault(NetworkFaultIds.WebGLThrottle, NetworkReadinessSeverity.Warning, DEFAULT_FAULT_DURATION_SECONDS)
            .RequireFault(NetworkFaultIds.ClientDisconnect, minimumDurationSeconds: DEFAULT_FAULT_DURATION_SECONDS);
        }
    }
}
