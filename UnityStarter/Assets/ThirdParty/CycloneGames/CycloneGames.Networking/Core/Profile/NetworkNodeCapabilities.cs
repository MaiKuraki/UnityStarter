using System;
using System.Collections.Generic;

namespace CycloneGames.Networking
{
    public readonly struct NetworkCapabilityId : IEquatable<NetworkCapabilityId>
    {
        public readonly string Value;

        public NetworkCapabilityId(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                throw new ArgumentException("Capability id must not be null or empty.", nameof(value));
            }

            Value = value;
        }

        public bool Equals(NetworkCapabilityId other)
        {
            return string.Equals(Value, other.Value, StringComparison.Ordinal);
        }

        public override bool Equals(object obj)
        {
            return obj is NetworkCapabilityId other && Equals(other);
        }

        public override int GetHashCode()
        {
            return StringComparer.Ordinal.GetHashCode(Value ?? string.Empty);
        }

        public override string ToString()
        {
            return Value ?? string.Empty;
        }
    }

    public static class NetworkCapabilityIds
    {
        public static readonly NetworkCapabilityId RealtimeTransport = new NetworkCapabilityId("net.realtime");
        public static readonly NetworkCapabilityId DirectConnect = new NetworkCapabilityId("net.direct");
        public static readonly NetworkCapabilityId LanDiscovery = new NetworkCapabilityId("net.lan.discovery");
        public static readonly NetworkCapabilityId PlatformLobby = new NetworkCapabilityId("net.platform.lobby");
        public static readonly NetworkCapabilityId Relay = new NetworkCapabilityId("net.relay");
        public static readonly NetworkCapabilityId NatTraversal = new NetworkCapabilityId("net.nat.traversal");
        public static readonly NetworkCapabilityId ListenServer = new NetworkCapabilityId("net.listen_server");
        public static readonly NetworkCapabilityId DedicatedServer = new NetworkCapabilityId("net.dedicated_server");
        public static readonly NetworkCapabilityId AuthoritativeSimulation = new NetworkCapabilityId("sim.authoritative");
        public static readonly NetworkCapabilityId HostMigration = new NetworkCapabilityId("session.host_migration");
        public static readonly NetworkCapabilityId Reconnection = new NetworkCapabilityId("session.reconnection");
        public static readonly NetworkCapabilityId Matchmaking = new NetworkCapabilityId("session.matchmaking");
        public static readonly NetworkCapabilityId RoomDirectory = new NetworkCapabilityId("session.room_directory");
        public static readonly NetworkCapabilityId Persistence = new NetworkCapabilityId("world.persistence");
        public static readonly NetworkCapabilityId Sharding = new NetworkCapabilityId("world.sharding");
        public static readonly NetworkCapabilityId ZoneTransfer = new NetworkCapabilityId("world.zone_transfer");
        public static readonly NetworkCapabilityId Spectator = new NetworkCapabilityId("session.spectator");
        public static readonly NetworkCapabilityId Encryption = new NetworkCapabilityId("security.encryption");
        public static readonly NetworkCapabilityId Compression = new NetworkCapabilityId("payload.compression");
        public static readonly NetworkCapabilityId WebGLCompatible = new NetworkCapabilityId("platform.webgl");
        public static readonly NetworkCapabilityId MobileSuspendRecovery = new NetworkCapabilityId("platform.mobile.suspend_recovery");
        public static readonly NetworkCapabilityId ModSupport = new NetworkCapabilityId("content.mods");
        public static readonly NetworkCapabilityId AntiCheatSignal = new NetworkCapabilityId("security.anticheat.signal");
    }

    public readonly struct NetworkCapability
    {
        public readonly NetworkCapabilityId Id;
        public readonly int Level;
        public readonly double Score;
        public readonly string Description;

        public NetworkCapability(NetworkCapabilityId id, int level = 1, double score = 0d, string description = "")
        {
            Id = id;
            Level = level;
            Score = double.IsNaN(score) ? 0d : score;
            Description = description ?? string.Empty;
        }

        public bool IsEnabled
        {
            get
            {
                return Level > 0;
            }
        }
    }

    public sealed class NetworkNodeCapabilities
    {
        private readonly Dictionary<NetworkCapabilityId, NetworkCapability> _capabilities;
        private readonly Dictionary<string, string> _labels;

        internal NetworkNodeCapabilities(NetworkNodeCapabilitiesBuilder builder)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            NodeId = builder.NodeId ?? string.Empty;
            RuntimeId = builder.RuntimeId;
            RuntimeName = builder.RuntimeName ?? string.Empty;
            Region = builder.Region ?? string.Empty;
            Platform = builder.Platform ?? string.Empty;
            MaxConnections = ValidateNonNegative(builder.MaxConnections, nameof(builder.MaxConnections));
            MaxPayloadBytes = ValidateNonNegative(builder.MaxPayloadBytes, nameof(builder.MaxPayloadBytes));
            MaxPacketsPerSecond = ValidateNonNegative(builder.MaxPacketsPerSecond, nameof(builder.MaxPacketsPerSecond));
            MaxBytesPerSecond = ValidateNonNegative(builder.MaxBytesPerSecond, nameof(builder.MaxBytesPerSecond));
            CpuScore = ValidateNonNegative(builder.CpuScore, nameof(builder.CpuScore));
            MemoryScore = ValidateNonNegative(builder.MemoryScore, nameof(builder.MemoryScore));
            _capabilities = new Dictionary<NetworkCapabilityId, NetworkCapability>(builder.Capabilities);
            _labels = new Dictionary<string, string>(builder.Labels, StringComparer.Ordinal);
        }

        public string NodeId { get; }
        public NetworkRuntimeId RuntimeId { get; }
        public string RuntimeName { get; }
        public string Region { get; }
        public string Platform { get; }
        public int MaxConnections { get; }
        public int MaxPayloadBytes { get; }
        public int MaxPacketsPerSecond { get; }
        public int MaxBytesPerSecond { get; }
        public int CpuScore { get; }
        public int MemoryScore { get; }

        public IReadOnlyDictionary<NetworkCapabilityId, NetworkCapability> Capabilities
        {
            get
            {
                return _capabilities;
            }
        }

        public IReadOnlyDictionary<string, string> Labels
        {
            get
            {
                return _labels;
            }
        }

        public bool Supports(NetworkCapabilityId id, int minimumLevel = 1)
        {
            return _capabilities.TryGetValue(id, out NetworkCapability capability)
                   && capability.Level >= minimumLevel;
        }

        public bool TryGet(NetworkCapabilityId id, out NetworkCapability capability)
        {
            return _capabilities.TryGetValue(id, out capability);
        }

        public bool TryGetLabel(string key, out string value)
        {
            if (!string.IsNullOrEmpty(key) && _labels.TryGetValue(key, out value))
            {
                return true;
            }

            value = string.Empty;
            return false;
        }

        public NetworkNodeCapabilitiesBuilder ToBuilder()
        {
            var builder = new NetworkNodeCapabilitiesBuilder
            {
                NodeId = NodeId,
                RuntimeId = RuntimeId,
                RuntimeName = RuntimeName,
                Region = Region,
                Platform = Platform,
                MaxConnections = MaxConnections,
                MaxPayloadBytes = MaxPayloadBytes,
                MaxPacketsPerSecond = MaxPacketsPerSecond,
                MaxBytesPerSecond = MaxBytesPerSecond,
                CpuScore = CpuScore,
                MemoryScore = MemoryScore
            };

            foreach (KeyValuePair<NetworkCapabilityId, NetworkCapability> pair in _capabilities)
            {
                builder.Add(pair.Value);
            }

            foreach (KeyValuePair<string, string> pair in _labels)
            {
                builder.SetLabel(pair.Key, pair.Value);
            }

            return builder;
        }

        private static int ValidateNonNegative(int value, string name)
        {
            if (value < 0)
            {
                throw new ArgumentOutOfRangeException(name);
            }

            return value;
        }
    }

    public sealed class NetworkNodeCapabilitiesBuilder
    {
        internal readonly Dictionary<NetworkCapabilityId, NetworkCapability> Capabilities =
            new Dictionary<NetworkCapabilityId, NetworkCapability>();

        internal readonly Dictionary<string, string> Labels =
            new Dictionary<string, string>(StringComparer.Ordinal);

        public string NodeId { get; set; } = string.Empty;
        public NetworkRuntimeId RuntimeId { get; set; } = NetworkRuntimeId.None;
        public string RuntimeName { get; set; } = string.Empty;
        public string Region { get; set; } = string.Empty;
        public string Platform { get; set; } = string.Empty;
        public int MaxConnections { get; set; }
        public int MaxPayloadBytes { get; set; }
        public int MaxPacketsPerSecond { get; set; }
        public int MaxBytesPerSecond { get; set; }
        public int CpuScore { get; set; }
        public int MemoryScore { get; set; }

        public NetworkNodeCapabilitiesBuilder Add(NetworkCapability capability)
        {
            Capabilities[capability.Id] = capability;
            return this;
        }

        public NetworkNodeCapabilitiesBuilder Add(NetworkCapabilityId id, int level = 1, double score = 0d, string description = "")
        {
            return Add(new NetworkCapability(id, level, score, description));
        }

        public NetworkNodeCapabilitiesBuilder SetLabel(string key, string value)
        {
            if (string.IsNullOrEmpty(key))
            {
                throw new ArgumentException("Capability label key must not be null or empty.", nameof(key));
            }

            Labels[key] = value ?? string.Empty;
            return this;
        }

        public NetworkNodeCapabilities Build()
        {
            return new NetworkNodeCapabilities(this);
        }
    }

    public sealed class NetworkCapabilityQuery
    {
        private const int DEFAULT_CAPABILITY_LIST_CAPACITY = 8;

        private readonly List<NetworkCapability> _required = new List<NetworkCapability>(DEFAULT_CAPABILITY_LIST_CAPACITY);
        private readonly List<NetworkCapabilityId> _preferred = new List<NetworkCapabilityId>(DEFAULT_CAPABILITY_LIST_CAPACITY);

        public IReadOnlyList<NetworkCapability> Required
        {
            get
            {
                return _required;
            }
        }

        public IReadOnlyList<NetworkCapabilityId> Preferred
        {
            get
            {
                return _preferred;
            }
        }

        public int MinimumConnections { get; set; }
        public int MinimumPayloadBytes { get; set; }
        public string Region { get; set; }
        public string Platform { get; set; }

        public NetworkCapabilityQuery Require(NetworkCapabilityId id, int minimumLevel = 1)
        {
            _required.Add(new NetworkCapability(id, minimumLevel));
            return this;
        }

        public NetworkCapabilityQuery Prefer(NetworkCapabilityId id)
        {
            _preferred.Add(id);
            return this;
        }
    }

    public static class NetworkNodeCapabilityMatcher
    {
        private const double REQUIRED_CAPABILITY_SCORE_BASE = 100d;
        private const double PREFERRED_CAPABILITY_SCORE_BASE = 10d;

        public static bool Matches(NetworkNodeCapabilities capabilities, NetworkCapabilityQuery query)
        {
            return TryScore(capabilities, query, out _);
        }

        public static bool TryScore(NetworkNodeCapabilities capabilities, NetworkCapabilityQuery query, out double score)
        {
            if (capabilities == null)
            {
                throw new ArgumentNullException(nameof(capabilities));
            }

            query ??= new NetworkCapabilityQuery();
            score = 0d;

            if (query.MinimumConnections > 0 && capabilities.MaxConnections < query.MinimumConnections)
            {
                return false;
            }

            if (query.MinimumPayloadBytes > 0 && capabilities.MaxPayloadBytes < query.MinimumPayloadBytes)
            {
                return false;
            }

            if (!string.IsNullOrEmpty(query.Region)
                && !string.Equals(query.Region, capabilities.Region, StringComparison.Ordinal))
            {
                return false;
            }

            if (!string.IsNullOrEmpty(query.Platform)
                && !string.Equals(query.Platform, capabilities.Platform, StringComparison.Ordinal))
            {
                return false;
            }

            for (int i = 0; i < query.Required.Count; i++)
            {
                NetworkCapability required = query.Required[i];
                if (!capabilities.Supports(required.Id, required.Level))
                {
                    return false;
                }

                score += REQUIRED_CAPABILITY_SCORE_BASE + required.Level;
            }

            for (int i = 0; i < query.Preferred.Count; i++)
            {
                if (capabilities.TryGet(query.Preferred[i], out NetworkCapability preferred)
                    && preferred.IsEnabled)
                {
                    score += PREFERRED_CAPABILITY_SCORE_BASE + preferred.Score;
                }
            }

            score += capabilities.CpuScore * 0.1d;
            score += capabilities.MemoryScore * 0.05d;
            score += capabilities.MaxConnections * 0.001d;
            return true;
        }
    }
}
