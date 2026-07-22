using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using CycloneGames.Networking;

namespace CycloneGames.BehaviorTree.Networking
{
    public enum BehaviorTreeNetworkSyncModel : byte
    {
        Manual = 0,
        ServerAuthoritative = 1,
        BlackboardReplicated = 2,
        DeterministicHashValidated = 3,
        OwnerPredicted = 4
    }

    [Flags]
    public enum BehaviorTreeNetworkFeatureFlags : uint
    {
        None = 0,
        FullSnapshot = 1u << 0,
        BlackboardDelta = 1u << 1,
        DeterministicHash = 1u << 2,
        TickControl = 1u << 3,
        AuthorityTransfer = 1u << 4,
        InterestFiltered = 1u << 5,
        HostMigrationSnapshot = 1u << 6
    }

    public sealed class BehaviorTreeNetworkProfile
    {
        private readonly Dictionary<string, int> _intSettings;
        private readonly Dictionary<string, string> _stringSettings;
        private readonly ReadOnlyDictionary<string, int> _readOnlyIntSettings;
        private readonly ReadOnlyDictionary<string, string> _readOnlyStringSettings;

        internal BehaviorTreeNetworkProfile(BehaviorTreeNetworkProfileBuilder builder)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            ProfileId = string.IsNullOrEmpty(builder.ProfileId) ? "default" : builder.ProfileId;
            SyncModel = builder.SyncModel;
            Features = builder.Features;
            SnapshotChannel = builder.SnapshotChannel;
            DeltaChannel = builder.DeltaChannel;
            ControlChannel = builder.ControlChannel;
            SnapshotIntervalTicks = ValidatePositive(builder.SnapshotIntervalTicks, nameof(builder.SnapshotIntervalTicks));
            DeltaIntervalTicks = ValidatePositive(builder.DeltaIntervalTicks, nameof(builder.DeltaIntervalTicks));
            HashIntervalTicks = ValidatePositive(builder.HashIntervalTicks, nameof(builder.HashIntervalTicks));
            MaxTrackedBlackboardKeys = ValidatePositive(builder.MaxTrackedBlackboardKeys, nameof(builder.MaxTrackedBlackboardKeys));
            MaxSnapshotPayloadBytes = ValidatePositive(builder.MaxSnapshotPayloadBytes, nameof(builder.MaxSnapshotPayloadBytes));
            MaxDeltaPayloadBytes = ValidatePositive(builder.MaxDeltaPayloadBytes, nameof(builder.MaxDeltaPayloadBytes));
            MaxDesyncReportsPerWindow = ValidateNonNegative(builder.MaxDesyncReportsPerWindow, nameof(builder.MaxDesyncReportsPerWindow));
            WakeTreeOnRemoteDelta = builder.WakeTreeOnRemoteDelta;
            AllowClientAuthoritativeBlackboardWrites = builder.AllowClientAuthoritativeBlackboardWrites;
            ForceFullSnapshotOnAuthorityTransfer = builder.ForceFullSnapshotOnAuthorityTransfer;

            _intSettings = new Dictionary<string, int>(builder.IntSettings, StringComparer.Ordinal);
            _stringSettings = new Dictionary<string, string>(builder.StringSettings, StringComparer.Ordinal);
            _readOnlyIntSettings = new ReadOnlyDictionary<string, int>(_intSettings);
            _readOnlyStringSettings = new ReadOnlyDictionary<string, string>(_stringSettings);
        }

        public string ProfileId { get; }
        public BehaviorTreeNetworkSyncModel SyncModel { get; }
        public BehaviorTreeNetworkFeatureFlags Features { get; }
        public NetworkChannel SnapshotChannel { get; }
        public NetworkChannel DeltaChannel { get; }
        public NetworkChannel ControlChannel { get; }
        public int SnapshotIntervalTicks { get; }
        public int DeltaIntervalTicks { get; }
        public int HashIntervalTicks { get; }
        public int MaxTrackedBlackboardKeys { get; }
        public int MaxSnapshotPayloadBytes { get; }
        public int MaxDeltaPayloadBytes { get; }
        public int MaxDesyncReportsPerWindow { get; }
        public bool WakeTreeOnRemoteDelta { get; }
        public bool AllowClientAuthoritativeBlackboardWrites { get; }
        public bool ForceFullSnapshotOnAuthorityTransfer { get; }

        public IReadOnlyDictionary<string, int> IntSettings => _readOnlyIntSettings;
        public IReadOnlyDictionary<string, string> StringSettings => _readOnlyStringSettings;

        public bool HasFeature(BehaviorTreeNetworkFeatureFlags feature)
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

        public BehaviorTreeNetworkProfileBuilder ToBuilder()
        {
            var builder = new BehaviorTreeNetworkProfileBuilder
            {
                ProfileId = ProfileId,
                SyncModel = SyncModel,
                Features = Features,
                SnapshotChannel = SnapshotChannel,
                DeltaChannel = DeltaChannel,
                ControlChannel = ControlChannel,
                SnapshotIntervalTicks = SnapshotIntervalTicks,
                DeltaIntervalTicks = DeltaIntervalTicks,
                HashIntervalTicks = HashIntervalTicks,
                MaxTrackedBlackboardKeys = MaxTrackedBlackboardKeys,
                MaxSnapshotPayloadBytes = MaxSnapshotPayloadBytes,
                MaxDeltaPayloadBytes = MaxDeltaPayloadBytes,
                MaxDesyncReportsPerWindow = MaxDesyncReportsPerWindow,
                WakeTreeOnRemoteDelta = WakeTreeOnRemoteDelta,
                AllowClientAuthoritativeBlackboardWrites = AllowClientAuthoritativeBlackboardWrites,
                ForceFullSnapshotOnAuthorityTransfer = ForceFullSnapshotOnAuthorityTransfer
            };

            foreach (KeyValuePair<string, int> pair in _intSettings)
            {
                builder.SetInt(pair.Key, pair.Value);
            }

            foreach (KeyValuePair<string, string> pair in _stringSettings)
            {
                builder.SetString(pair.Key, pair.Value);
            }

            return builder;
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

    public sealed class BehaviorTreeNetworkProfileBuilder
    {
        internal readonly Dictionary<string, int> IntSettings = new Dictionary<string, int>(StringComparer.Ordinal);
        internal readonly Dictionary<string, string> StringSettings = new Dictionary<string, string>(StringComparer.Ordinal);

        public string ProfileId { get; set; } = "behavior-tree.default";
        public BehaviorTreeNetworkSyncModel SyncModel { get; set; } = BehaviorTreeNetworkSyncModel.ServerAuthoritative;
        public BehaviorTreeNetworkFeatureFlags Features { get; set; } =
            BehaviorTreeNetworkFeatureFlags.FullSnapshot |
            BehaviorTreeNetworkFeatureFlags.BlackboardDelta |
            BehaviorTreeNetworkFeatureFlags.DeterministicHash |
            BehaviorTreeNetworkFeatureFlags.TickControl |
            BehaviorTreeNetworkFeatureFlags.AuthorityTransfer;
        public NetworkChannel SnapshotChannel { get; set; } = NetworkChannel.Reliable;
        public NetworkChannel DeltaChannel { get; set; } = NetworkChannel.UnreliableSequenced;
        public NetworkChannel ControlChannel { get; set; } = NetworkChannel.Reliable;
        public int SnapshotIntervalTicks { get; set; } = 10;
        public int DeltaIntervalTicks { get; set; } = 1;
        public int HashIntervalTicks { get; set; } = 15;
        public int MaxTrackedBlackboardKeys { get; set; } = 64;
        public int MaxSnapshotPayloadBytes { get; set; } = BehaviorTreeNetworkProtocol.DEFAULT_MAX_SNAPSHOT_PAYLOAD_SIZE;
        public int MaxDeltaPayloadBytes { get; set; } = BehaviorTreeNetworkProtocol.DEFAULT_MAX_DELTA_PAYLOAD_SIZE;
        public int MaxDesyncReportsPerWindow { get; set; } = 4;
        public bool WakeTreeOnRemoteDelta { get; set; } = true;
        public bool AllowClientAuthoritativeBlackboardWrites { get; set; }
        public bool ForceFullSnapshotOnAuthorityTransfer { get; set; } = true;

        public BehaviorTreeNetworkProfileBuilder SetInt(string key, int value)
        {
            ValidateKey(key);
            IntSettings[key] = value;
            return this;
        }

        public BehaviorTreeNetworkProfileBuilder SetString(string key, string value)
        {
            ValidateKey(key);
            StringSettings[key] = value ?? string.Empty;
            return this;
        }

        public BehaviorTreeNetworkProfile Build()
        {
            return new BehaviorTreeNetworkProfile(this);
        }

        private static void ValidateKey(string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                throw new ArgumentException("Profile setting key must not be null or empty.", nameof(key));
            }
        }
    }

    public static class BehaviorTreeNetworkProfiles
    {
        public static BehaviorTreeNetworkProfile ServerAuthoritative => CreateServerAuthoritativeBuilder().Build();
        public static BehaviorTreeNetworkProfile BlackboardReplicated => CreateBlackboardReplicatedBuilder().Build();
        public static BehaviorTreeNetworkProfile DeterministicHashValidated => CreateDeterministicHashValidatedBuilder().Build();

        public static BehaviorTreeNetworkProfileBuilder CreateServerAuthoritativeBuilder()
        {
            return new BehaviorTreeNetworkProfileBuilder
            {
                ProfileId = "behavior-tree.server-authoritative",
                SyncModel = BehaviorTreeNetworkSyncModel.ServerAuthoritative,
                Features = BehaviorTreeNetworkFeatureFlags.FullSnapshot |
                           BehaviorTreeNetworkFeatureFlags.BlackboardDelta |
                           BehaviorTreeNetworkFeatureFlags.DeterministicHash |
                           BehaviorTreeNetworkFeatureFlags.TickControl |
                           BehaviorTreeNetworkFeatureFlags.AuthorityTransfer |
                           BehaviorTreeNetworkFeatureFlags.HostMigrationSnapshot,
                SnapshotChannel = NetworkChannel.Reliable,
                DeltaChannel = NetworkChannel.UnreliableSequenced,
                ControlChannel = NetworkChannel.Reliable,
                SnapshotIntervalTicks = 10,
                DeltaIntervalTicks = 1,
                HashIntervalTicks = 15,
                AllowClientAuthoritativeBlackboardWrites = false
            };
        }

        public static BehaviorTreeNetworkProfileBuilder CreateBlackboardReplicatedBuilder()
        {
            return new BehaviorTreeNetworkProfileBuilder
            {
                ProfileId = "behavior-tree.blackboard-replicated",
                SyncModel = BehaviorTreeNetworkSyncModel.BlackboardReplicated,
                Features = BehaviorTreeNetworkFeatureFlags.BlackboardDelta |
                           BehaviorTreeNetworkFeatureFlags.DeterministicHash |
                           BehaviorTreeNetworkFeatureFlags.TickControl,
                SnapshotIntervalTicks = 30,
                DeltaIntervalTicks = 1,
                HashIntervalTicks = 30,
                AllowClientAuthoritativeBlackboardWrites = true
            };
        }

        public static BehaviorTreeNetworkProfileBuilder CreateDeterministicHashValidatedBuilder()
        {
            return new BehaviorTreeNetworkProfileBuilder
            {
                ProfileId = "behavior-tree.deterministic-hash",
                SyncModel = BehaviorTreeNetworkSyncModel.DeterministicHashValidated,
                Features = BehaviorTreeNetworkFeatureFlags.DeterministicHash |
                           BehaviorTreeNetworkFeatureFlags.FullSnapshot,
                SnapshotIntervalTicks = 60,
                DeltaIntervalTicks = 4,
                HashIntervalTicks = 4,
                AllowClientAuthoritativeBlackboardWrites = false
            };
        }
    }
}
