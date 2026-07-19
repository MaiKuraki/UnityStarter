using System;
using CycloneGames.Networking;

namespace CycloneGames.GameplayAbilities.Networking
{
    [Flags]
    public enum GASNetworkFeatureFlags : ushort
    {
        None = 0,
        AuthorityCommands = 1 << 0,
        TargetData = 1 << 1,
        StateReplication = 1 << 2,
        PredictionReconciliation = 1 << 3,
        GameplayCues = 1 << 4
    }

    public enum GASNetworkHandshakeResult : byte
    {
        Invalid = 0,
        Compatible = 1,
        Malformed = 2,
        ProtocolFingerprintMismatch = 3,
        ProtocolVersionMismatch = 4,
        WireSchemaMismatch = 5,
        ContentCatalogMismatch = 6,
        GameplayTagManifestMismatch = 7,
        RequiredFeatureMissing = 8
    }

    /// <summary>Fail-closed compatibility declaration exchanged before GAS traffic is accepted.</summary>
    public readonly struct GASNetworkHandshake : INetworkProtocolHandshakeMessage
    {
        public readonly ulong ProtocolFingerprint;
        public readonly ulong WireSchemaFingerprint;
        public readonly ulong ContentCatalogHash;
        public readonly ulong GameplayTagManifestHash;
        public readonly GASNetworkFeatureFlags SupportedFeatures;
        public readonly byte MinimumSupportedProtocolVersion;
        public readonly byte CurrentProtocolVersion;

        public GASNetworkHandshake(
            ulong protocolFingerprint,
            ulong wireSchemaFingerprint,
            ulong contentCatalogHash,
            ulong gameplayTagManifestHash,
            GASNetworkFeatureFlags supportedFeatures,
            byte minimumSupportedProtocolVersion,
            byte currentProtocolVersion)
        {
            ProtocolFingerprint = protocolFingerprint;
            WireSchemaFingerprint = wireSchemaFingerprint;
            ContentCatalogHash = contentCatalogHash;
            GameplayTagManifestHash = gameplayTagManifestHash;
            SupportedFeatures = supportedFeatures;
            MinimumSupportedProtocolVersion = minimumSupportedProtocolVersion;
            CurrentProtocolVersion = currentProtocolVersion;
        }

        ulong INetworkProtocolHandshakeMessage.ProtocolFingerprint => ProtocolFingerprint;
        byte INetworkProtocolHandshakeMessage.CurrentProtocolVersion => CurrentProtocolVersion;
        byte INetworkProtocolHandshakeMessage.MinimumSupportedProtocolVersion => MinimumSupportedProtocolVersion;
        ulong INetworkProtocolHandshakeMessage.DomainStateHash =>
            GameplayAbilitiesNetworkProtocol.ComputeCompatibilityHash(ContentCatalogHash, GameplayTagManifestHash);

        public bool IsWellFormed => GameplayAbilitiesNetworkProtocol.IsWellFormed(in this);
    }

    /// <summary>Authority-issued wire identity. It is never a Unity instance ID or a local GAS handle.</summary>
    public readonly struct GASNetworkEntityId : IEquatable<GASNetworkEntityId>
    {
        public readonly ulong Value;
        public GASNetworkEntityId(ulong value) => Value = value;
        public bool IsValid => Value != 0UL;
        public bool Equals(GASNetworkEntityId other) => Value == other.Value;
        public override bool Equals(object obj) => obj is GASNetworkEntityId other && Equals(other);
        public override int GetHashCode() => Value.GetHashCode();
        public override string ToString() => Value.ToString();
        public static bool operator ==(GASNetworkEntityId left, GASNetworkEntityId right) => left.Equals(right);
        public static bool operator !=(GASNetworkEntityId left, GASNetworkEntityId right) => !left.Equals(right);
    }

    /// <summary>Authority-issued identity of one grant within an entity and stream epoch.</summary>
    public readonly struct GASNetworkGrantId : IEquatable<GASNetworkGrantId>
    {
        public readonly ulong Value;
        public GASNetworkGrantId(ulong value) => Value = value;
        public bool IsValid => Value != 0UL;
        public bool Equals(GASNetworkGrantId other) => Value == other.Value;
        public override bool Equals(object obj) => obj is GASNetworkGrantId other && Equals(other);
        public override int GetHashCode() => Value.GetHashCode();
        public override string ToString() => Value.ToString();
        public static bool operator ==(GASNetworkGrantId left, GASNetworkGrantId right) => left.Equals(right);
        public static bool operator !=(GASNetworkGrantId left, GASNetworkGrantId right) => !left.Equals(right);
    }

    /// <summary>Authority-issued identity of one active effect within an entity and stream epoch.</summary>
    public readonly struct GASNetworkEffectId : IEquatable<GASNetworkEffectId>
    {
        public readonly ulong Value;
        public GASNetworkEffectId(ulong value) => Value = value;
        public bool IsValid => Value != 0UL;
        public bool Equals(GASNetworkEffectId other) => Value == other.Value;
        public override bool Equals(object obj) => obj is GASNetworkEffectId other && Equals(other);
        public override int GetHashCode() => Value.GetHashCode();
        public override string ToString() => Value.ToString();
        public static bool operator ==(GASNetworkEffectId left, GASNetworkEffectId right) => left.Equals(right);
        public static bool operator !=(GASNetworkEffectId left, GASNetworkEffectId right) => !left.Equals(right);
    }

    /// <summary>Stable catalog identity for definitions, attributes, named keys, and target surfaces.</summary>
    public readonly struct GASNetworkContentId : IEquatable<GASNetworkContentId>
    {
        public readonly ulong Value;
        public GASNetworkContentId(ulong value) => Value = value;
        public bool IsValid => Value != 0UL;
        public bool Equals(GASNetworkContentId other) => Value == other.Value;
        public override bool Equals(object obj) => obj is GASNetworkContentId other && Equals(other);
        public override int GetHashCode() => Value.GetHashCode();
        public override string ToString() => Value.ToString();
        public static bool operator ==(GASNetworkContentId left, GASNetworkContentId right) => left.Equals(right);
        public static bool operator !=(GASNetworkContentId left, GASNetworkContentId right) => !left.Equals(right);
    }

    /// <summary>Stable GameplayTag manifest identity. It is resolved by the GameplayTags registry.</summary>
    public readonly struct GASNetworkTagId : IEquatable<GASNetworkTagId>
    {
        public readonly ulong Value;
        public GASNetworkTagId(ulong value) => Value = value;
        public bool IsValid => Value != 0UL;
        public bool Equals(GASNetworkTagId other) => Value == other.Value;
        public override bool Equals(object obj) => obj is GASNetworkTagId other && Equals(other);
        public override int GetHashCode() => Value.GetHashCode();
        public override string ToString() => Value.ToString();
        public static bool operator ==(GASNetworkTagId left, GASNetworkTagId right) => left.Equals(right);
        public static bool operator !=(GASNetworkTagId left, GASNetworkTagId right) => !left.Equals(right);
    }

    public readonly struct GASNetworkVector3 : IEquatable<GASNetworkVector3>
    {
        public readonly float X;
        public readonly float Y;
        public readonly float Z;

        public GASNetworkVector3(float x, float y, float z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public bool IsFinite => GASNetworkMessageValidator.IsFinite(X) &&
                                GASNetworkMessageValidator.IsFinite(Y) &&
                                GASNetworkMessageValidator.IsFinite(Z);
        public bool Equals(GASNetworkVector3 other) => X.Equals(other.X) && Y.Equals(other.Y) && Z.Equals(other.Z);
        public override bool Equals(object obj) => obj is GASNetworkVector3 other && Equals(other);
        public override int GetHashCode() => ((X.GetHashCode() * 397) ^ Y.GetHashCode()) * 397 ^ Z.GetHashCode();
        public static bool operator ==(GASNetworkVector3 left, GASNetworkVector3 right) => left.Equals(right);
        public static bool operator !=(GASNetworkVector3 left, GASNetworkVector3 right) => !left.Equals(right);
    }

    public enum GASAbilityCommandKind : byte
    {
        Invalid = 0,
        Activate = 1,
        Cancel = 2,
        InputPressed = 3,
        InputReleased = 4,
        ConfirmTarget = 5,
        CancelTarget = 6
    }

    public enum GASTargetDataKind : byte
    {
        None = 0,
        ActorList = 1,
        SingleHit = 2
    }

    [Flags]
    public enum GASTargetHitFlags : byte
    {
        None = 0,
        BlockingHit = 1 << 0,
        HasTargetEntity = 1 << 1
    }

    /// <summary>Portable hit data. Coordinates are raw float32 values in the product-defined world space.</summary>
    /// <remarks>The authority treats this as an untrusted hint and revalidates range, visibility, and collision.</remarks>
    public readonly struct GASNetworkSingleTargetHit
    {
        public readonly GASNetworkEntityId TargetEntity;
        public readonly GASNetworkVector3 Point;
        public readonly GASNetworkVector3 Normal;
        public readonly float Distance;
        public readonly GASNetworkContentId Surface;
        public readonly GASTargetHitFlags Flags;

        public GASNetworkSingleTargetHit(
            GASNetworkEntityId targetEntity,
            GASNetworkVector3 point,
            GASNetworkVector3 normal,
            float distance,
            GASNetworkContentId surface,
            GASTargetHitFlags flags)
        {
            TargetEntity = targetEntity;
            Point = point;
            Normal = normal;
            Distance = distance;
            Surface = surface;
            Flags = flags;
        }

        public bool IsValid => GASNetworkMessageValidator.Validate(in this) == GASNetworkMessageValidationResult.Valid;
    }

    /// <summary>Requester-to-authority intent. Actor-list elements are supplied separately to the codec.</summary>
    /// <remarks>
    /// The endpoint verifies the authenticated connection, handshake, direction, channel, and wire
    /// payload. Product composition must verify entity ownership, permission, rate limits, and world
    /// target semantics before passing the command to authority gameplay execution.
    /// </remarks>
    public readonly struct GASAbilityCommand
    {
        public readonly byte ProtocolVersion;
        public readonly uint StreamEpoch;
        public readonly uint CommandSequence;
        public readonly GASNetworkEntityId Entity;
        public readonly GASNetworkGrantId Grant;
        public readonly GASAbilityCommandKind Kind;
        public readonly GASTargetDataKind TargetDataKind;
        public readonly byte TargetCount;
        public readonly GASNetworkSingleTargetHit SingleHit;

        public GASAbilityCommand(
            uint streamEpoch,
            uint commandSequence,
            GASNetworkEntityId entity,
            GASNetworkGrantId grant,
            GASAbilityCommandKind kind,
            GASTargetDataKind targetDataKind = GASTargetDataKind.None,
            byte targetCount = 0,
            GASNetworkSingleTargetHit singleHit = default)
            : this(
                GameplayAbilitiesNetworkProtocol.ProtocolVersion,
                streamEpoch,
                commandSequence,
                entity,
                grant,
                kind,
                targetDataKind,
                targetCount,
                singleHit)
        {
        }

        public GASAbilityCommand(
            byte protocolVersion,
            uint streamEpoch,
            uint commandSequence,
            GASNetworkEntityId entity,
            GASNetworkGrantId grant,
            GASAbilityCommandKind kind,
            GASTargetDataKind targetDataKind,
            byte targetCount,
            GASNetworkSingleTargetHit singleHit)
        {
            ProtocolVersion = protocolVersion;
            StreamEpoch = streamEpoch;
            CommandSequence = commandSequence;
            Entity = entity;
            Grant = grant;
            Kind = kind;
            TargetDataKind = targetDataKind;
            TargetCount = targetCount;
            SingleHit = singleHit;
        }

        public bool IsHeaderValid => GASNetworkMessageValidator.ValidateHeader(in this) == GASNetworkMessageValidationResult.Valid;
    }

    public enum GASCommandStatus : byte
    {
        Invalid = 0,
        Accepted = 1,
        Rejected = 2,
        EntityUnavailable = 3,
        GrantUnavailable = 4,
        NotAuthorized = 5,
        RateLimited = 6,
        InvalidTargetData = 7,
        AuthorityUnavailable = 8
    }

    /// <summary>Single terminal authority response correlated by stream epoch and command sequence.</summary>
    public readonly struct GASCommandResult
    {
        public readonly byte ProtocolVersion;
        public readonly uint StreamEpoch;
        public readonly uint CommandSequence;
        public readonly GASNetworkEntityId Entity;
        public readonly GASNetworkGrantId Grant;
        public readonly GASAbilityCommandKind CommandKind;
        public readonly GASCommandStatus Status;
        public readonly ulong AuthoritativeStateVersion;

        public GASCommandResult(
            uint streamEpoch,
            uint commandSequence,
            GASNetworkEntityId entity,
            GASNetworkGrantId grant,
            GASAbilityCommandKind commandKind,
            GASCommandStatus status,
            ulong authoritativeStateVersion)
            : this(
                GameplayAbilitiesNetworkProtocol.ProtocolVersion,
                streamEpoch,
                commandSequence,
                entity,
                grant,
                commandKind,
                status,
                authoritativeStateVersion)
        {
        }

        public GASCommandResult(
            byte protocolVersion,
            uint streamEpoch,
            uint commandSequence,
            GASNetworkEntityId entity,
            GASNetworkGrantId grant,
            GASAbilityCommandKind commandKind,
            GASCommandStatus status,
            ulong authoritativeStateVersion)
        {
            ProtocolVersion = protocolVersion;
            StreamEpoch = streamEpoch;
            CommandSequence = commandSequence;
            Entity = entity;
            Grant = grant;
            CommandKind = commandKind;
            Status = status;
            AuthoritativeStateVersion = authoritativeStateVersion;
        }

        public bool IsValid => GASNetworkMessageValidator.Validate(in this) == GASNetworkMessageValidationResult.Valid;
    }

    public enum GASStateBatchKind : byte
    {
        Invalid = 0,
        Snapshot = 1,
        Delta = 2
    }

    public enum GASStateRecordOperation : byte
    {
        Invalid = 0,
        Upsert = 1,
        Remove = 2
    }

    [Flags]
    public enum GASAbilityStateFlags : byte
    {
        None = 0,
        Active = 1 << 0,
        InputPressed = 1 << 1,
        Predicted = 1 << 2
    }

    [Flags]
    public enum GASEffectStateFlags : byte
    {
        None = 0,
        Infinite = 1 << 0,
        Inhibited = 1 << 1,
        Predicted = 1 << 2
    }

    public enum GASEffectTagKind : byte
    {
        Invalid = 0,
        Granted = 1,
        Asset = 2
    }

    public readonly struct GASAbilityStateRecord
    {
        public readonly GASStateRecordOperation Operation;
        public readonly GASNetworkGrantId Grant;
        public readonly GASNetworkContentId Definition;
        public readonly GASNetworkEffectId GrantingEffect;
        public readonly int Level;
        public readonly GASAbilityStateFlags Flags;

        public GASAbilityStateRecord(
            GASStateRecordOperation operation,
            GASNetworkGrantId grant,
            GASNetworkContentId definition,
            GASNetworkEffectId grantingEffect,
            int level,
            GASAbilityStateFlags flags)
        {
            Operation = operation;
            Grant = grant;
            Definition = definition;
            GrantingEffect = grantingEffect;
            Level = level;
            Flags = flags;
        }
    }

    public readonly struct GASAttributeStateRecord
    {
        public readonly GASStateRecordOperation Operation;
        public readonly GASNetworkContentId Attribute;
        public readonly long BaseValueRaw;
        public readonly long CurrentValueRaw;

        public GASAttributeStateRecord(
            GASStateRecordOperation operation,
            GASNetworkContentId attribute,
            long baseValueRaw,
            long currentValueRaw)
        {
            Operation = operation;
            Attribute = attribute;
            BaseValueRaw = baseValueRaw;
            CurrentValueRaw = currentValueRaw;
        }
    }

    public readonly struct GASEffectStateRecord
    {
        public readonly GASStateRecordOperation Operation;
        public readonly GASNetworkEffectId Effect;
        public readonly GASNetworkContentId Definition;
        public readonly GASNetworkEntityId SourceEntity;
        public readonly uint SourceStreamEpoch;
        public readonly GASNetworkGrantId SourceGrant;
        public readonly int Level;
        public readonly int StackCount;
        public readonly long DurationRaw;
        public readonly long RemainingRaw;
        public readonly long PeriodRaw;
        public readonly uint SourceCommandSequence;
        public readonly GASEffectStateFlags Flags;

        public GASEffectStateRecord(
            GASStateRecordOperation operation,
            GASNetworkEffectId effect,
            GASNetworkContentId definition,
            GASNetworkEntityId sourceEntity,
            uint sourceStreamEpoch,
            GASNetworkGrantId sourceGrant,
            int level,
            int stackCount,
            long durationRaw,
            long remainingRaw,
            long periodRaw,
            uint sourceCommandSequence,
            GASEffectStateFlags flags)
        {
            Operation = operation;
            Effect = effect;
            Definition = definition;
            SourceEntity = sourceEntity;
            SourceStreamEpoch = sourceStreamEpoch;
            SourceGrant = sourceGrant;
            Level = level;
            StackCount = stackCount;
            DurationRaw = durationRaw;
            RemainingRaw = remainingRaw;
            PeriodRaw = periodRaw;
            SourceCommandSequence = sourceCommandSequence;
            Flags = flags;
        }
    }

    public readonly struct GASEffectTagStateRecord
    {
        public readonly GASStateRecordOperation Operation;
        public readonly GASNetworkEffectId Effect;
        public readonly GASNetworkTagId Tag;
        public readonly GASEffectTagKind Kind;

        public GASEffectTagStateRecord(
            GASStateRecordOperation operation,
            GASNetworkEffectId effect,
            GASNetworkTagId tag,
            GASEffectTagKind kind)
        {
            Operation = operation;
            Effect = effect;
            Tag = tag;
            Kind = kind;
        }
    }

    public enum GASEffectMagnitudeKeyKind : byte
    {
        Invalid = 0,
        GameplayTag = 1,
        Name = 2
    }

    /// <summary>Discriminated SetByCaller key. Tag and name identities use different registries.</summary>
    public readonly struct GASNetworkMagnitudeKey : IEquatable<GASNetworkMagnitudeKey>
    {
        public readonly GASEffectMagnitudeKeyKind Kind;
        public readonly ulong Value;

        private GASNetworkMagnitudeKey(GASEffectMagnitudeKeyKind kind, ulong value)
        {
            Kind = kind;
            Value = value;
        }

        public bool IsValid => Kind >= GASEffectMagnitudeKeyKind.GameplayTag &&
                               Kind <= GASEffectMagnitudeKeyKind.Name &&
                               Value != 0UL;
        public GASNetworkTagId Tag => Kind == GASEffectMagnitudeKeyKind.GameplayTag
            ? new GASNetworkTagId(Value)
            : default;
        public GASNetworkContentId Name => Kind == GASEffectMagnitudeKeyKind.Name
            ? new GASNetworkContentId(Value)
            : default;

        public static GASNetworkMagnitudeKey FromTag(GASNetworkTagId tag)
        {
            if (!tag.IsValid) throw new ArgumentOutOfRangeException(nameof(tag));
            return new GASNetworkMagnitudeKey(GASEffectMagnitudeKeyKind.GameplayTag, tag.Value);
        }

        public static GASNetworkMagnitudeKey FromName(GASNetworkContentId name)
        {
            if (!name.IsValid) throw new ArgumentOutOfRangeException(nameof(name));
            return new GASNetworkMagnitudeKey(GASEffectMagnitudeKeyKind.Name, name.Value);
        }

        internal static GASNetworkMagnitudeKey FromWire(GASEffectMagnitudeKeyKind kind, ulong value) =>
            new GASNetworkMagnitudeKey(kind, value);

        public bool Equals(GASNetworkMagnitudeKey other) => Kind == other.Kind && Value == other.Value;
        public override bool Equals(object obj) => obj is GASNetworkMagnitudeKey other && Equals(other);
        public override int GetHashCode() => ((int)Kind * 397) ^ Value.GetHashCode();
        public static bool operator ==(GASNetworkMagnitudeKey left, GASNetworkMagnitudeKey right) => left.Equals(right);
        public static bool operator !=(GASNetworkMagnitudeKey left, GASNetworkMagnitudeKey right) => !left.Equals(right);
    }

    public readonly struct GASEffectMagnitudeStateRecord
    {
        public readonly GASStateRecordOperation Operation;
        public readonly GASNetworkEffectId Effect;
        public readonly GASNetworkMagnitudeKey Key;
        public readonly long ValueRaw;

        public GASEffectMagnitudeStateRecord(
            GASStateRecordOperation operation,
            GASNetworkEffectId effect,
            GASNetworkMagnitudeKey key,
            long valueRaw)
        {
            Operation = operation;
            Effect = effect;
            Key = key;
            ValueRaw = valueRaw;
        }
    }

    public readonly struct GASLooseTagStateRecord
    {
        public readonly GASStateRecordOperation Operation;
        public readonly GASNetworkTagId Tag;
        public readonly int Count;

        public GASLooseTagStateRecord(
            GASStateRecordOperation operation,
            GASNetworkTagId tag,
            int count)
        {
            Operation = operation;
            Tag = tag;
            Count = count;
        }
    }

    /// <summary>Header for one bounded snapshot or delta chunk. Record arrays are caller-owned.</summary>
    public readonly struct GASStateBatchChunk
    {
        public readonly byte ProtocolVersion;
        public readonly uint StreamEpoch;
        public readonly uint BatchSequence;
        public readonly GASNetworkEntityId Entity;
        public readonly GASStateBatchKind Kind;
        public readonly ulong BaseStateVersion;
        public readonly ulong StateVersion;
        public readonly uint LastProcessedCommandSequence;
        public readonly ushort ChunkIndex;
        public readonly ushort ChunkCount;
        public readonly ushort AbilityCount;
        public readonly ushort AttributeCount;
        public readonly ushort EffectCount;
        public readonly ushort EffectTagCount;
        public readonly ushort EffectMagnitudeCount;
        public readonly ushort LooseTagCount;
        public readonly ulong StateChecksum;

        public GASStateBatchChunk(
            uint streamEpoch,
            uint batchSequence,
            GASNetworkEntityId entity,
            GASStateBatchKind kind,
            ulong baseStateVersion,
            ulong stateVersion,
            uint lastProcessedCommandSequence,
            ushort chunkIndex,
            ushort chunkCount,
            ushort abilityCount,
            ushort attributeCount,
            ushort effectCount,
            ushort effectTagCount,
            ushort effectMagnitudeCount,
            ushort looseTagCount,
            ulong stateChecksum)
            : this(
                GameplayAbilitiesNetworkProtocol.ProtocolVersion,
                streamEpoch,
                batchSequence,
                entity,
                kind,
                baseStateVersion,
                stateVersion,
                lastProcessedCommandSequence,
                chunkIndex,
                chunkCount,
                abilityCount,
                attributeCount,
                effectCount,
                effectTagCount,
                effectMagnitudeCount,
                looseTagCount,
                stateChecksum)
        {
        }

        public GASStateBatchChunk(
            byte protocolVersion,
            uint streamEpoch,
            uint batchSequence,
            GASNetworkEntityId entity,
            GASStateBatchKind kind,
            ulong baseStateVersion,
            ulong stateVersion,
            uint lastProcessedCommandSequence,
            ushort chunkIndex,
            ushort chunkCount,
            ushort abilityCount,
            ushort attributeCount,
            ushort effectCount,
            ushort effectTagCount,
            ushort effectMagnitudeCount,
            ushort looseTagCount,
            ulong stateChecksum)
        {
            ProtocolVersion = protocolVersion;
            StreamEpoch = streamEpoch;
            BatchSequence = batchSequence;
            Entity = entity;
            Kind = kind;
            BaseStateVersion = baseStateVersion;
            StateVersion = stateVersion;
            LastProcessedCommandSequence = lastProcessedCommandSequence;
            ChunkIndex = chunkIndex;
            ChunkCount = chunkCount;
            AbilityCount = abilityCount;
            AttributeCount = attributeCount;
            EffectCount = effectCount;
            EffectTagCount = effectTagCount;
            EffectMagnitudeCount = effectMagnitudeCount;
            LooseTagCount = looseTagCount;
            StateChecksum = stateChecksum;
        }

        public int TotalRecordCount => AbilityCount + AttributeCount + EffectCount + EffectTagCount + EffectMagnitudeCount + LooseTagCount;
        public bool IsValid => GASNetworkMessageValidator.Validate(in this) == GASNetworkMessageValidationResult.Valid;
    }

    public readonly struct GASStateAcknowledgement
    {
        public readonly byte ProtocolVersion;
        public readonly uint StreamEpoch;
        public readonly uint BatchSequence;
        public readonly GASNetworkEntityId Entity;
        public readonly ulong AppliedStateVersion;
        public readonly ulong StateChecksum;

        public GASStateAcknowledgement(
            uint streamEpoch,
            uint batchSequence,
            GASNetworkEntityId entity,
            ulong appliedStateVersion,
            ulong stateChecksum)
            : this(GameplayAbilitiesNetworkProtocol.ProtocolVersion, streamEpoch, batchSequence, entity, appliedStateVersion, stateChecksum)
        {
        }

        public GASStateAcknowledgement(
            byte protocolVersion,
            uint streamEpoch,
            uint batchSequence,
            GASNetworkEntityId entity,
            ulong appliedStateVersion,
            ulong stateChecksum)
        {
            ProtocolVersion = protocolVersion;
            StreamEpoch = streamEpoch;
            BatchSequence = batchSequence;
            Entity = entity;
            AppliedStateVersion = appliedStateVersion;
            StateChecksum = stateChecksum;
        }

        public bool IsValid => GASNetworkMessageValidator.Validate(in this) == GASNetworkMessageValidationResult.Valid;
    }

    public enum GASResyncReason : byte
    {
        Invalid = 0,
        MissingBaseline = 1,
        SequenceGap = 2,
        VersionMismatch = 3,
        ChecksumMismatch = 4,
        DecodeFailure = 5,
        ApplyFailure = 6,
        LocalStateLost = 7
    }

    public readonly struct GASResyncRequest
    {
        public readonly byte ProtocolVersion;
        public readonly uint StreamEpoch;
        public readonly uint RequestSequence;
        public readonly GASNetworkEntityId Entity;
        public readonly ulong ObservedStateVersion;
        public readonly uint ExpectedBatchSequence;
        public readonly ulong ObservedChecksum;
        public readonly GASResyncReason Reason;

        public GASResyncRequest(
            uint streamEpoch,
            uint requestSequence,
            GASNetworkEntityId entity,
            ulong observedStateVersion,
            uint expectedBatchSequence,
            ulong observedChecksum,
            GASResyncReason reason)
            : this(
                GameplayAbilitiesNetworkProtocol.ProtocolVersion,
                streamEpoch,
                requestSequence,
                entity,
                observedStateVersion,
                expectedBatchSequence,
                observedChecksum,
                reason)
        {
        }

        public GASResyncRequest(
            byte protocolVersion,
            uint streamEpoch,
            uint requestSequence,
            GASNetworkEntityId entity,
            ulong observedStateVersion,
            uint expectedBatchSequence,
            ulong observedChecksum,
            GASResyncReason reason)
        {
            ProtocolVersion = protocolVersion;
            StreamEpoch = streamEpoch;
            RequestSequence = requestSequence;
            Entity = entity;
            ObservedStateVersion = observedStateVersion;
            ExpectedBatchSequence = expectedBatchSequence;
            ObservedChecksum = observedChecksum;
            Reason = reason;
        }

        public bool IsValid => GASNetworkMessageValidator.Validate(in this) == GASNetworkMessageValidationResult.Valid;
    }

    public enum GASCueEvent : byte
    {
        Invalid = 0,
        Execute = 1,
        OnActive = 2,
        WhileActive = 3,
        Removed = 4
    }

    [Flags]
    public enum GASCueFlags : byte
    {
        None = 0,
        HasLocation = 1 << 0,
        HasNormal = 1 << 1,
        Predicted = 1 << 2
    }

    /// <summary>Transient cue notification. It never carries Unity object references.</summary>
    public readonly struct GASCueExecuted
    {
        public readonly byte ProtocolVersion;
        public readonly uint StreamEpoch;
        public readonly uint CueSequence;
        public readonly GASNetworkEntityId Entity;
        public readonly GASNetworkTagId Cue;
        public readonly GASNetworkEntityId Instigator;
        public readonly GASNetworkEffectId SourceEffect;
        public readonly uint SourceCommandSequence;
        public readonly ulong AuthoritativeStateVersion;
        public readonly GASCueEvent Event;
        public readonly GASCueFlags Flags;
        public readonly float Magnitude;
        public readonly GASNetworkVector3 Location;
        public readonly GASNetworkVector3 Normal;

        public GASCueExecuted(
            uint streamEpoch,
            uint cueSequence,
            GASNetworkEntityId entity,
            GASNetworkTagId cue,
            GASNetworkEntityId instigator,
            GASNetworkEffectId sourceEffect,
            uint sourceCommandSequence,
            ulong authoritativeStateVersion,
            GASCueEvent cueEvent,
            GASCueFlags flags,
            float magnitude,
            GASNetworkVector3 location,
            GASNetworkVector3 normal)
            : this(
                GameplayAbilitiesNetworkProtocol.ProtocolVersion,
                streamEpoch,
                cueSequence,
                entity,
                cue,
                instigator,
                sourceEffect,
                sourceCommandSequence,
                authoritativeStateVersion,
                cueEvent,
                flags,
                magnitude,
                location,
                normal)
        {
        }

        public GASCueExecuted(
            byte protocolVersion,
            uint streamEpoch,
            uint cueSequence,
            GASNetworkEntityId entity,
            GASNetworkTagId cue,
            GASNetworkEntityId instigator,
            GASNetworkEffectId sourceEffect,
            uint sourceCommandSequence,
            ulong authoritativeStateVersion,
            GASCueEvent cueEvent,
            GASCueFlags flags,
            float magnitude,
            GASNetworkVector3 location,
            GASNetworkVector3 normal)
        {
            ProtocolVersion = protocolVersion;
            StreamEpoch = streamEpoch;
            CueSequence = cueSequence;
            Entity = entity;
            Cue = cue;
            Instigator = instigator;
            SourceEffect = sourceEffect;
            SourceCommandSequence = sourceCommandSequence;
            AuthoritativeStateVersion = authoritativeStateVersion;
            Event = cueEvent;
            Flags = flags;
            Magnitude = magnitude;
            Location = location;
            Normal = normal;
        }

        public bool IsValid => GASNetworkMessageValidator.Validate(in this) == GASNetworkMessageValidationResult.Valid;
    }
}
