using System;
using CycloneGames.Networking;

namespace CycloneGames.GameplayAbilities.Networking
{
    /// <summary>Immutable v1 message catalog, limits, and compatibility negotiation.</summary>
    public static class GameplayAbilitiesNetworkProtocol
    {
        public const string MessageOwner = "CycloneGames.GameplayAbilities";
        public const byte ProtocolVersion = 1;
        public const ushort MessageIdBase = 10000;
        public const ushort MessageIdMax = 10999;

        public const ushort HandshakeMessageId = MessageIdBase;
        public const ushort AbilityCommandMessageId = MessageIdBase + 1;
        public const ushort CommandResultMessageId = MessageIdBase + 2;
        public const ushort StateBatchChunkMessageId = MessageIdBase + 3;
        public const ushort StateAcknowledgementMessageId = MessageIdBase + 4;
        public const ushort ResyncRequestMessageId = MessageIdBase + 5;
        public const ushort CueExecutedMessageId = MessageIdBase + 6;

        public const int MaxActorTargets = 32;
        public const int MaxRecordsPerChunk = 64;
        public const int MaxExplicitLooseTagCount = 4096;
        public const int MaxChunksPerBatch = 64;
        public const uint MaxSequence = int.MaxValue;

        public const int MaxHandshakePayloadBytes = GASNetworkWireCodec.HandshakePayloadBytes;
        public const int MaxAbilityCommandPayloadBytes = GASNetworkWireCodec.MaxAbilityCommandPayloadBytes;
        public const int MaxCommandResultPayloadBytes = GASNetworkWireCodec.CommandResultPayloadBytes;
        public const int MaxStateBatchChunkPayloadBytes = NetworkConstants.DefaultMaxPayloadSize;
        public const int MaxStateAcknowledgementPayloadBytes = GASNetworkWireCodec.StateAcknowledgementPayloadBytes;
        public const int MaxResyncRequestPayloadBytes = GASNetworkWireCodec.ResyncRequestPayloadBytes;
        public const int MaxCueExecutedPayloadBytes = GASNetworkWireCodec.CueExecutedPayloadBytes;

        public const GASNetworkFeatureFlags SupportedFeatures =
            GASNetworkFeatureFlags.AuthorityCommands |
            GASNetworkFeatureFlags.TargetData |
            GASNetworkFeatureFlags.StateReplication |
            GASNetworkFeatureFlags.PredictionReconciliation |
            GASNetworkFeatureFlags.GameplayCues;

        private const string HandshakeContractId = "GASNetworkHandshake:v1";
        private const string AbilityCommandContractId = "GASAbilityCommand:v1";
        private const string CommandResultContractId = "GASCommandResult:v1";
        private const string StateBatchChunkContractId = "GASStateBatchChunk:v1";
        private const string StateAcknowledgementContractId = "GASStateAcknowledgement:v1";
        private const string ResyncRequestContractId = "GASResyncRequest:v1";
        private const string CueExecutedContractId = "GASCueExecuted:v1";

        private const string HandshakeWireSchema =
            "GASNetworkHandshake:v1|ProtocolFingerprint:u64le|WireSchemaFingerprint:u64le|" +
            "ContentCatalogHash:u64le|GameplayTagManifestHash:u64le|Features:u16le|MinVersion:u8|Version:u8|size:36";
        private const string AbilityCommandWireSchema =
            "GASAbilityCommand:v1|Version:u8|Epoch:u32le|Sequence:u32le|Entity:u64le|Grant:u64le|" +
            "Kind:u8|TargetKind:u8|TargetCount:u8|" +
            "ActorList:TargetCount*u64le|SingleHit:Entity:u64le,Point:3*f32le,Normal:3*f32le," +
            "Distance:f32le,Surface:u64le,Flags:u8|base:28|max:284";
        private const string CommandResultWireSchema =
            "GASCommandResult:v1|Version:u8|Epoch:u32le|Sequence:u32le|Entity:u64le|Grant:u64le|" +
            "CommandKind:u8|Status:u8|StateVersion:u64le|size:35";
        private const string StateBatchWireSchema =
            "GASStateBatchChunk:v1|Version:u8|Epoch:u32le|BatchSequence:u32le|Entity:u64le|Kind:u8|" +
            "BaseVersion:u64le|StateVersion:u64le|LastCommand:u32le|ChunkIndex:u16le|ChunkCount:u16le|" +
            "Counts:6*u16le|Checksum:u64le|header:62|Ability:GrantingEffect:u64le,size:30|Attribute:25|" +
            "Effect:SourceEntity:u64le,SourceEpoch:u32le,SourceGrant:u64le,size:74|" +
            "EffectTag:18|EffectMagnitude:26|LooseTag:13,countMax:4096|max:1200";
        private const string StateAcknowledgementWireSchema =
            "GASStateAcknowledgement:v1|Version:u8|Epoch:u32le|BatchSequence:u32le|Entity:u64le|" +
            "AppliedVersion:u64le|Checksum:u64le|size:33";
        private const string ResyncRequestWireSchema =
            "GASResyncRequest:v1|Version:u8|Epoch:u32le|RequestSequence:u32le|Entity:u64le|" +
            "ObservedVersion:u64le|ExpectedBatch:u32le|ObservedChecksum:u64le|Reason:u8|size:38";
        private const string CueExecutedWireSchema =
            "GASCueExecuted:v1|Version:u8|Epoch:u32le|CueSequence:u32le|Entity:u64le|CueTag:u64le|" +
            "Instigator:u64le|SourceEffect:u64le|SourceCommand:u32le|StateVersion:u64le|Event:u8|" +
            "Flags:u8|Magnitude:f32le|Location:3*f32le|Normal:3*f32le|size:83";
        private const string EnumWireSchema =
            "Features:u16:1,2,4,8,16|Command:u8:1..6|Target:u8:0..2|CommandStatus:u8:1..8|" +
            "BatchKind:u8:1..2|RecordOperation:u8:1..2|MagnitudeKeyKind:u8:1..2|" +
            "ResyncReason:u8:1..7|CueEvent:u8:1..4";

        public static readonly ulong WireSchemaFingerprint = ComputeWireSchemaFingerprint();
        public static readonly NetworkModuleProtocol Module = new NetworkModuleProtocol(CreateProtocolManifest());
        public static readonly NetworkProtocolManifest DefaultManifest = Module.Manifest;
        public static readonly NetworkMessageIdRange MessageRange = Module.MessageRange;
        public static readonly ulong ProtocolFingerprint = Module.Fingerprint;

        public static bool IsGameplayAbilitiesMessageId(ushort messageId) => Module.ContainsMessageId(messageId);
        public static bool IsSupportedProtocolVersion(byte protocolVersion) => Module.IsSupportedProtocolVersion(protocolVersion);
        public static void RegisterMessageCatalog(INetworkMessageCatalog catalog) => Module.Register(catalog);

        public static GASNetworkHandshake CreateHandshake(ulong contentCatalogHash, ulong gameplayTagManifestHash)
        {
            if (contentCatalogHash == 0UL)
            {
                throw new ArgumentOutOfRangeException(nameof(contentCatalogHash));
            }

            if (gameplayTagManifestHash == 0UL)
            {
                throw new ArgumentOutOfRangeException(nameof(gameplayTagManifestHash));
            }

            return new GASNetworkHandshake(
                ProtocolFingerprint,
                WireSchemaFingerprint,
                contentCatalogHash,
                gameplayTagManifestHash,
                SupportedFeatures,
                ProtocolVersion,
                ProtocolVersion);
        }

        public static bool IsWellFormed(in GASNetworkHandshake handshake)
        {
            return NetworkProtocolHandshake.IsWellFormed(in handshake) &&
                   handshake.WireSchemaFingerprint != 0UL &&
                   handshake.ContentCatalogHash != 0UL &&
                   handshake.GameplayTagManifestHash != 0UL &&
                   (handshake.SupportedFeatures & ~SupportedFeatures) == 0;
        }

        public static GASNetworkHandshakeResult Negotiate(
            in GASNetworkHandshake remote,
            ulong localContentCatalogHash,
            ulong localGameplayTagManifestHash,
            GASNetworkFeatureFlags requiredFeatures = SupportedFeatures)
        {
            if (localContentCatalogHash == 0UL)
            {
                throw new ArgumentOutOfRangeException(nameof(localContentCatalogHash));
            }

            if (localGameplayTagManifestHash == 0UL)
            {
                throw new ArgumentOutOfRangeException(nameof(localGameplayTagManifestHash));
            }

            if ((requiredFeatures & ~SupportedFeatures) != 0)
            {
                throw new ArgumentOutOfRangeException(nameof(requiredFeatures));
            }

            if (!IsWellFormed(in remote))
            {
                return GASNetworkHandshakeResult.Malformed;
            }

            NetworkHandshakeResult protocol = NetworkProtocolHandshake.Negotiate(in remote, Module);
            if (protocol == NetworkHandshakeResult.FingerprintMismatch)
            {
                return GASNetworkHandshakeResult.ProtocolFingerprintMismatch;
            }

            if (protocol == NetworkHandshakeResult.VersionIncompatible)
            {
                return GASNetworkHandshakeResult.ProtocolVersionMismatch;
            }

            if (protocol != NetworkHandshakeResult.Compatible)
            {
                return GASNetworkHandshakeResult.Malformed;
            }

            if (remote.WireSchemaFingerprint != WireSchemaFingerprint)
            {
                return GASNetworkHandshakeResult.WireSchemaMismatch;
            }

            if (remote.ContentCatalogHash != localContentCatalogHash)
            {
                return GASNetworkHandshakeResult.ContentCatalogMismatch;
            }

            if (remote.GameplayTagManifestHash != localGameplayTagManifestHash)
            {
                return GASNetworkHandshakeResult.GameplayTagManifestMismatch;
            }

            return (remote.SupportedFeatures & requiredFeatures) == requiredFeatures
                ? GASNetworkHandshakeResult.Compatible
                : GASNetworkHandshakeResult.RequiredFeatureMissing;
        }

        public static NetworkProtocolManifest CreateProtocolManifest()
        {
            var builder = new NetworkProtocolManifestBuilder(MessageOwner, MessageIdBase, MessageIdMax)
            {
                ProtocolId = "CycloneGames.GameplayAbilities.Networking",
                CurrentVersion = ProtocolVersion,
                MinimumSupportedVersion = ProtocolVersion
            };

            builder
                .SetMetadata("module", "GameplayAbilities")
                .SetMetadata("scope", "commands-state-cues")
                .SetMetadata("wireSchemaFingerprint", WireSchemaFingerprint.ToString("X16"))
                .AddMessage(HandshakeContractId, HandshakeMessageId, ComputeAsciiFnv1a64(HandshakeContractId), NetworkChannel.Reliable, MaxHandshakePayloadBytes)
                .AddMessage(AbilityCommandContractId, AbilityCommandMessageId, ComputeAsciiFnv1a64(AbilityCommandContractId), NetworkChannel.Reliable, MaxAbilityCommandPayloadBytes)
                .AddMessage(CommandResultContractId, CommandResultMessageId, ComputeAsciiFnv1a64(CommandResultContractId), NetworkChannel.Reliable, MaxCommandResultPayloadBytes)
                .AddMessage(StateBatchChunkContractId, StateBatchChunkMessageId, ComputeAsciiFnv1a64(StateBatchChunkContractId), NetworkChannel.Reliable, MaxStateBatchChunkPayloadBytes)
                .AddMessage(StateAcknowledgementContractId, StateAcknowledgementMessageId, ComputeAsciiFnv1a64(StateAcknowledgementContractId), NetworkChannel.Reliable, MaxStateAcknowledgementPayloadBytes)
                .AddMessage(ResyncRequestContractId, ResyncRequestMessageId, ComputeAsciiFnv1a64(ResyncRequestContractId), NetworkChannel.Reliable, MaxResyncRequestPayloadBytes)
                .AddMessage(CueExecutedContractId, CueExecutedMessageId, ComputeAsciiFnv1a64(CueExecutedContractId), NetworkChannel.Reliable, MaxCueExecutedPayloadBytes);

            return builder.Build();
        }

        internal static ulong ComputeCompatibilityHash(ulong contentCatalogHash, ulong gameplayTagManifestHash)
        {
            const ulong offsetBasis = 14695981039346656037UL;
            const ulong prime = 1099511628211UL;
            ulong hash = Combine(offsetBasis, contentCatalogHash, prime);
            return Combine(hash, gameplayTagManifestHash, prime);
        }

        private static ulong ComputeWireSchemaFingerprint()
        {
            const ulong offsetBasis = 14695981039346656037UL;
            ulong hash = offsetBasis;
            hash = AppendAscii(hash, HandshakeWireSchema);
            hash = AppendDelimiter(hash);
            hash = AppendAscii(hash, AbilityCommandWireSchema);
            hash = AppendDelimiter(hash);
            hash = AppendAscii(hash, CommandResultWireSchema);
            hash = AppendDelimiter(hash);
            hash = AppendAscii(hash, StateBatchWireSchema);
            hash = AppendDelimiter(hash);
            hash = AppendAscii(hash, StateAcknowledgementWireSchema);
            hash = AppendDelimiter(hash);
            hash = AppendAscii(hash, ResyncRequestWireSchema);
            hash = AppendDelimiter(hash);
            hash = AppendAscii(hash, CueExecutedWireSchema);
            hash = AppendDelimiter(hash);
            return AppendAscii(hash, EnumWireSchema);
        }

        private static ulong ComputeAsciiFnv1a64(string value)
        {
            return AppendAscii(14695981039346656037UL, value);
        }

        private static ulong AppendAscii(ulong hash, string value)
        {
            const ulong prime = 1099511628211UL;
            unchecked
            {
                for (int i = 0; i < value.Length; i++)
                {
                    char character = value[i];
                    if (character > 0x7F)
                    {
                        throw new InvalidOperationException("Wire schema descriptors must be ASCII.");
                    }

                    hash ^= (byte)character;
                    hash *= prime;
                }
            }

            return hash;
        }

        private static ulong AppendDelimiter(ulong hash)
        {
            const ulong prime = 1099511628211UL;
            unchecked
            {
                hash ^= 0xFF;
                return hash * prime;
            }
        }

        private static ulong Combine(ulong hash, ulong value, ulong prime)
        {
            unchecked
            {
                for (int i = 0; i < 8; i++)
                {
                    hash ^= (byte)(value >> (i * 8));
                    hash *= prime;
                }
            }

            return hash;
        }
    }
}
