using System;
using System.Collections.Generic;
using CycloneGames.Networking;

namespace CycloneGames.AIPerception.Networking
{
    public enum AIPerceptionNetworkSyncModel : byte
    {
        Manual = 0,
        ServerAuthoritative = 1,
        SharedTeamAwareness = 2,
        ObserverDebug = 3,
        HostMigrationSnapshot = 4
    }

    [Flags]
    public enum AIPerceptionNetworkFeatureFlags : uint
    {
        None = 0,
        DetectionEvents = 1u << 0,
        DetectionSnapshots = 1u << 1,
        MemorySnapshots = 1u << 2,
        AuthorityTransfer = 1u << 3,
        InterestFiltered = 1u << 4,
        TeamShared = 1u << 5,
        DebugSpectator = 1u << 6,
        HostMigrationSnapshot = 1u << 7
    }

    public sealed class AIPerceptionNetworkProfile
    {
        private readonly Dictionary<string, int> _intSettings;
        private readonly Dictionary<string, string> _stringSettings;

        internal AIPerceptionNetworkProfile(AIPerceptionNetworkProfileBuilder builder)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            ProfileId = string.IsNullOrEmpty(builder.ProfileId) ? "ai-perception.default" : builder.ProfileId;
            SyncModel = builder.SyncModel;
            Features = builder.Features;
            EventChannel = builder.EventChannel;
            SnapshotChannel = builder.SnapshotChannel;
            ControlChannel = builder.ControlChannel;
            EventIntervalTicks = ValidatePositive(builder.EventIntervalTicks, nameof(builder.EventIntervalTicks));
            SnapshotIntervalTicks = ValidatePositive(builder.SnapshotIntervalTicks, nameof(builder.SnapshotIntervalTicks));
            MemorySnapshotIntervalTicks = ValidatePositive(builder.MemorySnapshotIntervalTicks, nameof(builder.MemorySnapshotIntervalTicks));
            MaxSnapshotEntries = ValidatePositive(builder.MaxSnapshotEntries, nameof(builder.MaxSnapshotEntries));
            MaxEventPayloadBytes = ValidatePositive(builder.MaxEventPayloadBytes, nameof(builder.MaxEventPayloadBytes));
            MaxSnapshotPayloadBytes = ValidatePositive(builder.MaxSnapshotPayloadBytes, nameof(builder.MaxSnapshotPayloadBytes));
            MaxFullStateRequestsPerWindow = ValidateNonNegative(builder.MaxFullStateRequestsPerWindow, nameof(builder.MaxFullStateRequestsPerWindow));
            SendMemoryEntries = builder.SendMemoryEntries;
            SendLostEvents = builder.SendLostEvents;
            ForceFullSnapshotOnAuthorityTransfer = builder.ForceFullSnapshotOnAuthorityTransfer;

            _intSettings = new Dictionary<string, int>(builder.IntSettings, StringComparer.Ordinal);
            _stringSettings = new Dictionary<string, string>(builder.StringSettings, StringComparer.Ordinal);
        }

        public string ProfileId { get; }
        public AIPerceptionNetworkSyncModel SyncModel { get; }
        public AIPerceptionNetworkFeatureFlags Features { get; }
        public NetworkChannel EventChannel { get; }
        public NetworkChannel SnapshotChannel { get; }
        public NetworkChannel ControlChannel { get; }
        public int EventIntervalTicks { get; }
        public int SnapshotIntervalTicks { get; }
        public int MemorySnapshotIntervalTicks { get; }
        public int MaxSnapshotEntries { get; }
        public int MaxEventPayloadBytes { get; }
        public int MaxSnapshotPayloadBytes { get; }
        public int MaxFullStateRequestsPerWindow { get; }
        public bool SendMemoryEntries { get; }
        public bool SendLostEvents { get; }
        public bool ForceFullSnapshotOnAuthorityTransfer { get; }

        public IReadOnlyDictionary<string, int> IntSettings => _intSettings;
        public IReadOnlyDictionary<string, string> StringSettings => _stringSettings;

        public bool HasFeature(AIPerceptionNetworkFeatureFlags feature)
        {
            return (Features & feature) == feature;
        }

        public bool TryGetInt(string key, out int value)
        {
            if (!string.IsNullOrEmpty(key) && _intSettings.TryGetValue(key, out value))
            {
                return true;
            }

            value = 0;
            return false;
        }

        public bool TryGetString(string key, out string value)
        {
            if (!string.IsNullOrEmpty(key) && _stringSettings.TryGetValue(key, out value))
            {
                return true;
            }

            value = string.Empty;
            return false;
        }

        private static int ValidatePositive(int value, string name)
        {
            if (value <= 0)
            {
                throw new ArgumentOutOfRangeException(name);
            }

            return value;
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

    public sealed class AIPerceptionNetworkProfileBuilder
    {
        internal readonly Dictionary<string, int> IntSettings = new Dictionary<string, int>(StringComparer.Ordinal);
        internal readonly Dictionary<string, string> StringSettings = new Dictionary<string, string>(StringComparer.Ordinal);

        public string ProfileId { get; set; } = "ai-perception.default";
        public AIPerceptionNetworkSyncModel SyncModel { get; set; } = AIPerceptionNetworkSyncModel.ServerAuthoritative;
        public AIPerceptionNetworkFeatureFlags Features { get; set; } =
            AIPerceptionNetworkFeatureFlags.DetectionEvents |
            AIPerceptionNetworkFeatureFlags.DetectionSnapshots |
            AIPerceptionNetworkFeatureFlags.MemorySnapshots |
            AIPerceptionNetworkFeatureFlags.AuthorityTransfer |
            AIPerceptionNetworkFeatureFlags.InterestFiltered;
        public NetworkChannel EventChannel { get; set; } = NetworkChannel.UnreliableSequenced;
        public NetworkChannel SnapshotChannel { get; set; } = NetworkChannel.UnreliableSequenced;
        public NetworkChannel ControlChannel { get; set; } = NetworkChannel.Reliable;
        public int EventIntervalTicks { get; set; } = 1;
        public int SnapshotIntervalTicks { get; set; } = 10;
        public int MemorySnapshotIntervalTicks { get; set; } = 30;
        public int MaxSnapshotEntries { get; set; } = 32;
        public int MaxEventPayloadBytes { get; set; } = AIPerceptionNetworkProtocol.DEFAULT_MAX_EVENT_PAYLOAD_SIZE;
        public int MaxSnapshotPayloadBytes { get; set; } = AIPerceptionNetworkProtocol.DEFAULT_MAX_SNAPSHOT_PAYLOAD_SIZE;
        public int MaxFullStateRequestsPerWindow { get; set; } = 4;
        public bool SendMemoryEntries { get; set; } = true;
        public bool SendLostEvents { get; set; } = true;
        public bool ForceFullSnapshotOnAuthorityTransfer { get; set; } = true;

        public AIPerceptionNetworkProfileBuilder SetInt(string key, int value)
        {
            ValidateKey(key);
            IntSettings[key] = value;
            return this;
        }

        public AIPerceptionNetworkProfileBuilder SetString(string key, string value)
        {
            ValidateKey(key);
            StringSettings[key] = value ?? string.Empty;
            return this;
        }

        public AIPerceptionNetworkProfile Build()
        {
            return new AIPerceptionNetworkProfile(this);
        }

        private static void ValidateKey(string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                throw new ArgumentException("Profile setting key must not be null or empty.", nameof(key));
            }
        }
    }

    public static class AIPerceptionNetworkProfiles
    {
        public static AIPerceptionNetworkProfile ServerAuthoritative => CreateServerAuthoritativeBuilder().Build();
        public static AIPerceptionNetworkProfile SharedTeamAwareness => CreateSharedTeamAwarenessBuilder().Build();
        public static AIPerceptionNetworkProfile DebugSpectator => CreateDebugSpectatorBuilder().Build();

        public static AIPerceptionNetworkProfileBuilder CreateServerAuthoritativeBuilder()
        {
            return new AIPerceptionNetworkProfileBuilder
            {
                ProfileId = "ai-perception.server-authoritative",
                SyncModel = AIPerceptionNetworkSyncModel.ServerAuthoritative,
                Features = AIPerceptionNetworkFeatureFlags.DetectionEvents |
                           AIPerceptionNetworkFeatureFlags.DetectionSnapshots |
                           AIPerceptionNetworkFeatureFlags.MemorySnapshots |
                           AIPerceptionNetworkFeatureFlags.AuthorityTransfer |
                           AIPerceptionNetworkFeatureFlags.InterestFiltered |
                           AIPerceptionNetworkFeatureFlags.HostMigrationSnapshot,
                EventIntervalTicks = 1,
                SnapshotIntervalTicks = 10,
                MemorySnapshotIntervalTicks = 30
            };
        }

        public static AIPerceptionNetworkProfileBuilder CreateSharedTeamAwarenessBuilder()
        {
            return new AIPerceptionNetworkProfileBuilder
            {
                ProfileId = "ai-perception.shared-team-awareness",
                SyncModel = AIPerceptionNetworkSyncModel.SharedTeamAwareness,
                Features = AIPerceptionNetworkFeatureFlags.DetectionEvents |
                           AIPerceptionNetworkFeatureFlags.MemorySnapshots |
                           AIPerceptionNetworkFeatureFlags.TeamShared |
                           AIPerceptionNetworkFeatureFlags.InterestFiltered,
                EventIntervalTicks = 2,
                SnapshotIntervalTicks = 20,
                MemorySnapshotIntervalTicks = 20,
                MaxSnapshotEntries = 24
            };
        }

        public static AIPerceptionNetworkProfileBuilder CreateDebugSpectatorBuilder()
        {
            return new AIPerceptionNetworkProfileBuilder
            {
                ProfileId = "ai-perception.debug-spectator",
                SyncModel = AIPerceptionNetworkSyncModel.ObserverDebug,
                Features = AIPerceptionNetworkFeatureFlags.DetectionSnapshots |
                           AIPerceptionNetworkFeatureFlags.MemorySnapshots |
                           AIPerceptionNetworkFeatureFlags.DebugSpectator,
                SnapshotChannel = NetworkChannel.Reliable,
                EventIntervalTicks = 1,
                SnapshotIntervalTicks = 5,
                MemorySnapshotIntervalTicks = 10,
                MaxSnapshotEntries = 128
            };
        }
    }
}

