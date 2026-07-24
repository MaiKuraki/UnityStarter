using System;
using CycloneGames.Hash.Core;
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

    /// <summary>Immutable, typed synchronization policy included in peer compatibility checks.</summary>
    public sealed class AIPerceptionNetworkProfile
    {
        public const int MaxProfileIdLength = 64;

        internal AIPerceptionNetworkProfile(AIPerceptionNetworkProfileBuilder builder)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            ProfileId = ValidateProfileId(builder.ProfileId);
            SyncModel = ValidateSyncModel(builder.SyncModel);
            Features = ValidateFeatures(builder.Features, nameof(builder.Features));
            RequiredFeatures = ValidateFeatures(builder.RequiredFeatures, nameof(builder.RequiredFeatures));
            if ((RequiredFeatures & ~Features) != 0)
            {
                throw new ArgumentException("Required features must be a subset of supported features.", nameof(builder));
            }

            EventChannel = ValidateChannel(builder.EventChannel, nameof(builder.EventChannel));
            SnapshotChannel = ValidateChannel(builder.SnapshotChannel, nameof(builder.SnapshotChannel));
            MemorySnapshotChannel = ValidateChannel(
                builder.MemorySnapshotChannel,
                nameof(builder.MemorySnapshotChannel));
            ControlChannel = ValidateChannel(builder.ControlChannel, nameof(builder.ControlChannel));
            EventIntervalTicks = ValidatePositive(builder.EventIntervalTicks, nameof(builder.EventIntervalTicks));
            SnapshotIntervalTicks = ValidatePositive(builder.SnapshotIntervalTicks, nameof(builder.SnapshotIntervalTicks));
            MemorySnapshotIntervalTicks = ValidatePositive(
                builder.MemorySnapshotIntervalTicks,
                nameof(builder.MemorySnapshotIntervalTicks));
            MaxSnapshotPayloadBytes = ValidateSnapshotPayloadBytes(builder.MaxSnapshotPayloadBytes);
            MaxSnapshotEntries = ValidatePositive(builder.MaxSnapshotEntries, nameof(builder.MaxSnapshotEntries));
            int payloadEntryLimit = AIPerceptionNetworkWireCodec.GetMaxSnapshotEntries(MaxSnapshotPayloadBytes);
            if (MaxSnapshotEntries > AIPerceptionNetworkProtocol.MAX_SNAPSHOT_ENTRIES ||
                MaxSnapshotEntries > payloadEntryLimit)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(builder.MaxSnapshotEntries),
                    "Snapshot entry count exceeds the protocol or configured payload budget.");
            }

            SendMemoryEntries = builder.SendMemoryEntries;
            SendLostEvents = builder.SendLostEvents;
            ForceFullSnapshotOnAuthorityTransfer = builder.ForceFullSnapshotOnAuthorityTransfer;
            ProfileHash = ComputeProfileHash(this);
        }

        public string ProfileId { get; }
        public AIPerceptionNetworkSyncModel SyncModel { get; }
        public AIPerceptionNetworkFeatureFlags Features { get; }
        public AIPerceptionNetworkFeatureFlags RequiredFeatures { get; }
        public NetworkChannel EventChannel { get; }
        public NetworkChannel SnapshotChannel { get; }
        public NetworkChannel MemorySnapshotChannel { get; }
        public NetworkChannel ControlChannel { get; }
        public int EventIntervalTicks { get; }
        public int SnapshotIntervalTicks { get; }
        public int MemorySnapshotIntervalTicks { get; }
        public int MaxSnapshotEntries { get; }
        public int MaxSnapshotPayloadBytes { get; }
        public bool SendMemoryEntries { get; }
        public bool SendLostEvents { get; }
        public bool ForceFullSnapshotOnAuthorityTransfer { get; }
        public ulong ProfileHash { get; }

        public bool HasFeature(AIPerceptionNetworkFeatureFlags feature)
        {
            return (Features & feature) == feature;
        }

        private static ulong ComputeProfileHash(AIPerceptionNetworkProfile profile)
        {
            ulong hash = Fnv1a64.OffsetBasis;
            hash = Fnv1a64.CombineUInt32LittleEndian(hash, (uint)profile.ProfileId.Length);
            for (int i = 0; i < profile.ProfileId.Length; i++)
            {
                hash = CombineByte(hash, (byte)profile.ProfileId[i]);
            }

            hash = CombineByte(hash, AIPerceptionNetworkProtocol.PROTOCOL_VERSION);
            hash = CombineByte(hash, (byte)profile.SyncModel);
            hash = Fnv1a64.CombineUInt32LittleEndian(hash, (uint)profile.Features);
            hash = Fnv1a64.CombineUInt32LittleEndian(hash, (uint)profile.RequiredFeatures);
            hash = CombineByte(hash, (byte)profile.EventChannel);
            hash = CombineByte(hash, (byte)profile.SnapshotChannel);
            hash = CombineByte(hash, (byte)profile.MemorySnapshotChannel);
            hash = CombineByte(hash, (byte)profile.ControlChannel);
            hash = Fnv1a64.CombineUInt32LittleEndian(hash, (uint)profile.EventIntervalTicks);
            hash = Fnv1a64.CombineUInt32LittleEndian(hash, (uint)profile.SnapshotIntervalTicks);
            hash = Fnv1a64.CombineUInt32LittleEndian(hash, (uint)profile.MemorySnapshotIntervalTicks);
            hash = Fnv1a64.CombineUInt32LittleEndian(hash, (uint)profile.MaxSnapshotEntries);
            hash = Fnv1a64.CombineUInt32LittleEndian(hash, (uint)profile.MaxSnapshotPayloadBytes);
            hash = CombineByte(hash, profile.SendMemoryEntries ? (byte)1 : (byte)0);
            hash = CombineByte(hash, profile.SendLostEvents ? (byte)1 : (byte)0);
            hash = CombineByte(hash, profile.ForceFullSnapshotOnAuthorityTransfer ? (byte)1 : (byte)0);
            return hash == 0UL ? Fnv1a64.OffsetBasis : hash;
        }

        private static string ValidateProfileId(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length > MaxProfileIdLength)
            {
                throw new ArgumentException("Profile ID must contain 1-64 printable ASCII characters.", nameof(value));
            }

            for (int i = 0; i < value.Length; i++)
            {
                if (value[i] < 0x21 || value[i] > 0x7E)
                {
                    throw new ArgumentException("Profile ID must contain printable ASCII without spaces.", nameof(value));
                }
            }

            return value;
        }

        private static AIPerceptionNetworkSyncModel ValidateSyncModel(AIPerceptionNetworkSyncModel value)
        {
            if (value < AIPerceptionNetworkSyncModel.Manual ||
                value > AIPerceptionNetworkSyncModel.HostMigrationSnapshot)
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }

            return value;
        }

        private static AIPerceptionNetworkFeatureFlags ValidateFeatures(
            AIPerceptionNetworkFeatureFlags value,
            string name)
        {
            if (!AIPerceptionNetworkProtocol.AreKnownFeatures(value))
            {
                throw new ArgumentOutOfRangeException(name);
            }

            return value;
        }

        private static NetworkChannel ValidateChannel(NetworkChannel value, string name)
        {
            if (value < NetworkChannel.Reliable || value > NetworkChannel.UnreliableSequenced)
            {
                throw new ArgumentOutOfRangeException(name);
            }

            return value;
        }

        private static int ValidatePositive(int value, string name)
        {
            if (value <= 0)
            {
                throw new ArgumentOutOfRangeException(name);
            }

            return value;
        }

        private static int ValidateSnapshotPayloadBytes(int value)
        {
            if (value < AIPerceptionNetworkWireCodec.DetectionSnapshotHeaderBytes ||
                value > AIPerceptionNetworkProtocol.DEFAULT_MAX_SNAPSHOT_PAYLOAD_SIZE)
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }

            return value;
        }

        private static ulong CombineByte(ulong hash, byte value)
        {
            unchecked
            {
                hash ^= value;
                return hash * Fnv1a64.Prime;
            }
        }
    }

    public sealed class AIPerceptionNetworkProfileBuilder
    {
        public string ProfileId { get; set; } = "ai-perception.default";
        public AIPerceptionNetworkSyncModel SyncModel { get; set; } =
            AIPerceptionNetworkSyncModel.ServerAuthoritative;
        public AIPerceptionNetworkFeatureFlags Features { get; set; } =
            AIPerceptionNetworkFeatureFlags.DetectionEvents |
            AIPerceptionNetworkFeatureFlags.DetectionSnapshots |
            AIPerceptionNetworkFeatureFlags.MemorySnapshots |
            AIPerceptionNetworkFeatureFlags.AuthorityTransfer |
            AIPerceptionNetworkFeatureFlags.InterestFiltered;
        public AIPerceptionNetworkFeatureFlags RequiredFeatures { get; set; } =
            AIPerceptionNetworkFeatureFlags.DetectionSnapshots |
            AIPerceptionNetworkFeatureFlags.InterestFiltered;
        public NetworkChannel EventChannel { get; set; } = NetworkChannel.UnreliableSequenced;
        public NetworkChannel SnapshotChannel { get; set; } = NetworkChannel.UnreliableSequenced;
        public NetworkChannel MemorySnapshotChannel { get; set; } = NetworkChannel.Reliable;
        public NetworkChannel ControlChannel { get; set; } = NetworkChannel.Reliable;
        public int EventIntervalTicks { get; set; } = 1;
        public int SnapshotIntervalTicks { get; set; } = 10;
        public int MemorySnapshotIntervalTicks { get; set; } = 30;
        public int MaxSnapshotEntries { get; set; } =
            (NetworkConstants.DefaultMaxPayloadSize - AIPerceptionNetworkWireCodec.DetectionSnapshotHeaderBytes) /
            AIPerceptionNetworkWireCodec.DetectionEntryBytes;
        public int MaxSnapshotPayloadBytes { get; set; } = NetworkConstants.DefaultMaxPayloadSize;
        public bool SendMemoryEntries { get; set; } = true;
        public bool SendLostEvents { get; set; } = true;
        public bool ForceFullSnapshotOnAuthorityTransfer { get; set; } = true;

        public AIPerceptionNetworkProfile Build()
        {
            return new AIPerceptionNetworkProfile(this);
        }
    }

    public static class AIPerceptionNetworkProfiles
    {
        private static readonly AIPerceptionNetworkProfile ServerAuthoritativeProfile =
            CreateServerAuthoritativeBuilder().Build();
        private static readonly AIPerceptionNetworkProfile SharedTeamAwarenessProfile =
            CreateSharedTeamAwarenessBuilder().Build();
        private static readonly AIPerceptionNetworkProfile DebugSpectatorProfile =
            CreateDebugSpectatorBuilder().Build();

        public static AIPerceptionNetworkProfile ServerAuthoritative => ServerAuthoritativeProfile;
        public static AIPerceptionNetworkProfile SharedTeamAwareness => SharedTeamAwarenessProfile;
        public static AIPerceptionNetworkProfile DebugSpectator => DebugSpectatorProfile;

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
                RequiredFeatures = AIPerceptionNetworkFeatureFlags.DetectionSnapshots |
                                   AIPerceptionNetworkFeatureFlags.InterestFiltered,
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
                RequiredFeatures = AIPerceptionNetworkFeatureFlags.MemorySnapshots |
                                   AIPerceptionNetworkFeatureFlags.TeamShared,
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
                RequiredFeatures = AIPerceptionNetworkFeatureFlags.DetectionSnapshots |
                                   AIPerceptionNetworkFeatureFlags.DebugSpectator,
                SnapshotChannel = NetworkChannel.Reliable,
                MemorySnapshotChannel = NetworkChannel.Reliable,
                EventIntervalTicks = 1,
                SnapshotIntervalTicks = 5,
                MemorySnapshotIntervalTicks = 10,
                MaxSnapshotPayloadBytes = AIPerceptionNetworkProtocol.DEFAULT_MAX_SNAPSHOT_PAYLOAD_SIZE,
                MaxSnapshotEntries = AIPerceptionNetworkProtocol.MAX_SNAPSHOT_ENTRIES
            };
        }
    }
}
