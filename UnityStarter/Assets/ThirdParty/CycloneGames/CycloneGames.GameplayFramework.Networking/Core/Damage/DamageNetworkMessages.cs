using System.Runtime.CompilerServices;
using CycloneGames.Networking;
using CycloneGames.Networking.Serialization;

namespace CycloneGames.GameplayFramework.Networking
{
    /// <summary>
    /// Outcome of a server-authoritative damage validation. Doubles as the wire-level result code
    /// in <see cref="DamageResultMessage"/> so clients can react to rejected attacks without a side channel.
    /// <see cref="DefaultServerDamageValidator"/> emits <see cref="Accepted"/>, <see cref="InvalidPayload"/>,
    /// <see cref="OwnershipMismatch"/>, <see cref="TargetNotDamageable"/>, <see cref="OutOfRange"/> and
    /// <see cref="OnCooldown"/>; <see cref="TargetNotFound"/> and <see cref="Custom"/> are produced by the
        /// integration layer and game-specific validators.
    /// </summary>
    public enum ServerDamageRejectReason : byte
    {
        Unknown = 0,
        Accepted = 1,
        InvalidPayload = 2,
        OwnershipMismatch = 3,
        TargetNotDamageable = 4,
        OutOfRange = 5,
        OnCooldown = 6,

        /// <summary>The integration layer could not resolve the target actor id to a live actor.</summary>
        TargetNotFound = 7,

        /// <summary>
        /// Rejected by a game-specific <see cref="IServerDamageValidator"/> rule (friendly fire, invulnerability,
        /// line-of-sight, resource cost, etc.). Reserved so custom validators can reject without growing this enum.
        /// </summary>
        Custom = 8,

        /// <summary>The bounded authoritative cooldown tracker has no capacity for a new instigator.</summary>
        CooldownCapacityReached = 9
    }

    /// <summary>
    /// Client-to-server damage intent. Every field is untrusted: the server treats this as a request,
    /// re-derives authoritative facts (positions, ownership, weapon rules) and clamps the final damage.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Serialized field-by-field (not as a blittable memcpy) so the wire layout is identical across
    /// platforms and free of struct padding. The encoding is little-endian via the shared
    /// <see cref="INetWriter"/>/<see cref="INetReader"/> primitives.
    /// </para>
    /// <para>
    /// Scope: this is generic, float-based, server-authoritative actor damage for sources that are NOT modeled
    /// as GameplayAbilities effects (environment, projectiles, melee). Ability-sourced damage should flow
    /// through GameplayAbilities instead (fixed-point GameplayEffect modifiers replicated over GAS networking).
    /// Never apply the same hit through both paths or it double-counts. Because this path uses float it targets
    /// server-authoritative play, not bit-deterministic lockstep; route deterministic combat through the
    /// fixed-point GAS pipeline. Flood protection is expected to come from the shared network security pipeline
    /// (rate limiter) in addition to the per-instigator cooldown enforced during validation.
    /// </para>
    /// </remarks>
    public struct DamageRequestMessage
    {
        /// <summary>Client-assigned correlation id, echoed back in the result for prediction reconciliation.</summary>
        public uint Sequence;

        /// <summary>Attacking actor the client claims to control.</summary>
        public int InstigatorActorId;

        /// <summary>Actor the client claims to have hit.</summary>
        public int TargetActorId;

        /// <summary>Server-side identifier for the weapon/ability whose authoritative damage rules apply.</summary>
        public int WeaponOrAbilityId;

        /// <summary>Damage category (<c>EDamageEventType</c> encoded as a byte to stay engine-free).</summary>
        public byte DamageEventType;

        /// <summary>Client-claimed raw damage. The server clamps this to the authoritative weapon cap.</summary>
        public float RequestedDamage;

        /// <summary>Client-claimed shot origin. The server validates it against the authoritative instigator position.</summary>
        public NetworkVector3 ShotOrigin;

        /// <summary>Client-claimed hit location. Echoed in the result for hit-feedback VFX after validation.</summary>
        public NetworkVector3 HitLocation;

        /// <summary>Client timestamp in seconds, used only as a lag-compensation hint. The server never trusts it as time.</summary>
        public float ClientTimeSeconds;
    }

    /// <summary>
    /// Server-to-clients authoritative damage outcome. <see cref="AppliedDamage"/> is the real value the
    /// server applied (0 when rejected). Clients use <see cref="RequestSequence"/> to reconcile prediction.
    /// </summary>
    public struct DamageResultMessage
    {
        /// <summary>Echoes <see cref="DamageRequestMessage.Sequence"/>; 0 for server-initiated damage.</summary>
        public uint RequestSequence;

        public int InstigatorActorId;
        public int TargetActorId;

        /// <summary>Authoritative damage actually applied. 0 when <see cref="ResultCode"/> is not <see cref="ServerDamageRejectReason.Accepted"/>.</summary>
        public float AppliedDamage;

        /// <summary><see cref="ServerDamageRejectReason"/> encoded as a byte.</summary>
        public ServerDamageRejectReason ResultCode;

        /// <summary>Damage category (<c>EDamageEventType</c> encoded as a byte).</summary>
        public byte DamageEventType;

        /// <summary>Server-confirmed hit location for feedback VFX.</summary>
        public NetworkVector3 HitLocation;
    }

    /// <summary>
    /// Field-by-field serialization for the damage wire contracts. The read path validates that every
    /// float is finite and lives behind a cold throw helper, so the normal path is allocation-free and
    /// stays small enough for the JIT to inline.
    /// </summary>
    public static class DamageNetworkingExtensions
    {
        public static void WriteDamageRequest(this INetWriter writer, in DamageRequestMessage message)
        {
            ValidateDamageRequest(in message);
            writer.WriteUInt(message.Sequence);
            writer.WriteInt(message.InstigatorActorId);
            writer.WriteInt(message.TargetActorId);
            writer.WriteInt(message.WeaponOrAbilityId);
            writer.WriteByte(message.DamageEventType);
            writer.WriteFloat(message.RequestedDamage);
            WriteVector3(writer, message.ShotOrigin);
            WriteVector3(writer, message.HitLocation);
            writer.WriteFloat(message.ClientTimeSeconds);
        }

        public static DamageRequestMessage ReadDamageRequest(this INetReader reader)
        {
            RequireExactPayloadSize(reader, GameplayFrameworkNetworkProtocol.DamageRequestPayloadBytes);
            DamageRequestMessage message;
            message.Sequence = reader.ReadUInt();
            message.InstigatorActorId = reader.ReadInt();
            message.TargetActorId = reader.ReadInt();
            message.WeaponOrAbilityId = reader.ReadInt();
            message.DamageEventType = reader.ReadByte();
            message.RequestedDamage = ReadFiniteFloat(reader, "RequestedDamage");
            message.ShotOrigin = ReadFiniteVector3(reader, "ShotOrigin");
            message.HitLocation = ReadFiniteVector3(reader, "HitLocation");
            message.ClientTimeSeconds = ReadFiniteFloat(reader, "ClientTimeSeconds");
            ValidateDamageRequest(in message);
            RequireFullyConsumed(reader);
            return message;
        }

        public static void WriteDamageResult(this INetWriter writer, in DamageResultMessage message)
        {
            ValidateDamageResult(in message);
            writer.WriteUInt(message.RequestSequence);
            writer.WriteInt(message.InstigatorActorId);
            writer.WriteInt(message.TargetActorId);
            writer.WriteFloat(message.AppliedDamage);
            writer.WriteByte((byte)message.ResultCode);
            writer.WriteByte(message.DamageEventType);
            WriteVector3(writer, message.HitLocation);
        }

        public static DamageResultMessage ReadDamageResult(this INetReader reader)
        {
            RequireExactPayloadSize(reader, GameplayFrameworkNetworkProtocol.DamageResultPayloadBytes);
            DamageResultMessage message;
            message.RequestSequence = reader.ReadUInt();
            message.InstigatorActorId = reader.ReadInt();
            message.TargetActorId = reader.ReadInt();
            message.AppliedDamage = ReadFiniteFloat(reader, "AppliedDamage");
            message.ResultCode = ReadResultCode(reader);
            message.DamageEventType = reader.ReadByte();
            message.HitLocation = ReadFiniteVector3(reader, "HitLocation");
            ValidateDamageResult(in message);
            RequireFullyConsumed(reader);
            return message;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static void ValidateDamageRequest(in DamageRequestMessage message)
        {
            if (message.InstigatorActorId <= 0
                || message.TargetActorId <= 0
                || message.InstigatorActorId == message.TargetActorId
                || message.WeaponOrAbilityId < 0)
            {
                ThrowInvalidRequestIdentifiers();
            }

            if (message.RequestedDamage < 0f
                || !float.IsFinite(message.RequestedDamage)
                || !message.ShotOrigin.IsFinite()
                || !message.HitLocation.IsFinite()
                || message.ClientTimeSeconds < 0f
                || !float.IsFinite(message.ClientTimeSeconds))
            {
                ThrowInvalidDamageRequest();
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static void ValidateDamageResult(in DamageResultMessage message)
        {
            ValidateResultCode(message.ResultCode);
            if (message.AppliedDamage < 0f ||
                float.IsNaN(message.AppliedDamage) ||
                float.IsInfinity(message.AppliedDamage))
            {
                ThrowInvalidAppliedDamage();
            }

            if (!message.HitLocation.IsFinite())
            {
                ThrowNotFinite("HitLocation");
            }

            if (message.InstigatorActorId < 0 || message.TargetActorId < 0)
            {
                ThrowInvalidRejectedActorIdentifiers();
            }

            if (message.ResultCode != ServerDamageRejectReason.Accepted && message.AppliedDamage != 0f)
            {
                ThrowRejectedDamageIsNonZero();
            }

            if (message.ResultCode == ServerDamageRejectReason.Accepted &&
                (message.InstigatorActorId <= 0 ||
                 message.TargetActorId <= 0 ||
                 message.InstigatorActorId == message.TargetActorId))
            {
                ThrowInvalidAcceptedActorIdentifiers();
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void WriteVector3(INetWriter writer, in NetworkVector3 value)
        {
            writer.WriteFloat(value.X);
            writer.WriteFloat(value.Y);
            writer.WriteFloat(value.Z);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static NetworkVector3 ReadFiniteVector3(INetReader reader, string field)
        {
            float x = ReadFiniteFloat(reader, field);
            float y = ReadFiniteFloat(reader, field);
            float z = ReadFiniteFloat(reader, field);
            return new NetworkVector3(x, y, z);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float ReadFiniteFloat(INetReader reader, string field)
        {
            float value = reader.ReadFloat();
            if (float.IsNaN(value) || float.IsInfinity(value))
            {
                ThrowNotFinite(field);
            }

            return value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ServerDamageRejectReason ReadResultCode(INetReader reader)
        {
            ServerDamageRejectReason resultCode = (ServerDamageRejectReason)reader.ReadByte();
            ValidateResultCode(resultCode);
            return resultCode;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void ValidateResultCode(ServerDamageRejectReason resultCode)
        {
            byte value = (byte)resultCode;
            if (value == (byte)ServerDamageRejectReason.Unknown
                || value > (byte)ServerDamageRejectReason.CooldownCapacityReached)
            {
                ThrowInvalidResultCode(value);
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void ThrowNotFinite(string field)
        {
            throw new System.InvalidOperationException("Damage message field '" + field + "' is not a finite number.");
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void ThrowInvalidResultCode(byte value)
        {
            throw new System.InvalidOperationException("Damage result contains an invalid result code: " + value + ".");
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void RequireExactPayloadSize(INetReader reader, int expectedBytes)
        {
            if (reader == null)
            {
                throw new System.ArgumentNullException(nameof(reader));
            }

            if (reader.Remaining != expectedBytes)
            {
                ThrowInvalidPayloadSize(expectedBytes, reader.Remaining);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void RequireFullyConsumed(INetReader reader)
        {
            if (reader.Remaining != 0)
            {
                ThrowTrailingPayload(reader.Remaining);
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void ThrowInvalidAppliedDamage()
        {
            throw new System.InvalidOperationException("Damage result AppliedDamage must be finite and non-negative.");
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void ThrowRejectedDamageIsNonZero()
        {
            throw new System.InvalidOperationException("A rejected damage result must have zero AppliedDamage.");
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void ThrowInvalidAcceptedActorIdentifiers()
        {
            throw new System.InvalidOperationException(
                "An accepted damage result requires distinct positive instigator and target Actor IDs.");
        }


        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void ThrowInvalidRequestIdentifiers()
        {
            throw new System.InvalidOperationException(
                "A damage request requires distinct positive Actor IDs and a non-negative weapon or ability ID.");
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void ThrowInvalidDamageRequest()
        {
            throw new System.InvalidOperationException(
                "Damage request values must be finite, and damage and client time must be non-negative.");
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void ThrowInvalidRejectedActorIdentifiers()
        {
            throw new System.InvalidOperationException(
                "Damage result Actor IDs cannot be negative; zero is reserved for unresolved rejected results.");
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void ThrowInvalidPayloadSize(int expectedBytes, int actualBytes)
        {
            throw new System.InvalidOperationException(
                "Damage payload length must be exactly " + expectedBytes + " bytes; received " + actualBytes + ".");
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void ThrowTrailingPayload(int trailingBytes)
        {
            throw new System.InvalidOperationException(
                "Damage payload contains " + trailingBytes + " trailing bytes.");
        }
    }
}
