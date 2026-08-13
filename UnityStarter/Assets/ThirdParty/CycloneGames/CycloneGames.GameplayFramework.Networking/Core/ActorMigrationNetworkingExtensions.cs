using System;
using System.Buffers;
using System.Runtime.CompilerServices;
using System.Text;
using CycloneGames.GameplayFramework.Core;
using CycloneGames.Networking;
using CycloneGames.Networking.Serialization;

namespace CycloneGames.GameplayFramework.Networking
{
    /// <summary>Engine-independent quaternion used by GameplayFramework network snapshots.</summary>
    public readonly struct NetworkQuaternion : IEquatable<NetworkQuaternion>
    {
        public NetworkQuaternion(float x, float y, float z, float w)
        {
            X = x;
            Y = y;
            Z = z;
            W = w;
        }

        public float X { get; }
        public float Y { get; }
        public float Z { get; }
        public float W { get; }

        public static NetworkQuaternion Identity => new NetworkQuaternion(0f, 0f, 0f, 1f);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsFinite()
        {
            return IsFinite(X) && IsFinite(Y) && IsFinite(Z) && IsFinite(W);
        }

        public bool Equals(NetworkQuaternion other)
        {
            return X == other.X && Y == other.Y && Z == other.Z && W == other.W;
        }

        public override bool Equals(object obj)
        {
            return obj is NetworkQuaternion other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = X.GetHashCode();
                hash = (hash * 397) ^ Y.GetHashCode();
                hash = (hash * 397) ^ Z.GetHashCode();
                return (hash * 397) ^ W.GetHashCode();
            }
        }

        public static bool operator ==(NetworkQuaternion left, NetworkQuaternion right) => left.Equals(right);
        public static bool operator !=(NetworkQuaternion left, NetworkQuaternion right) => !left.Equals(right);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }

    /// <summary>
    /// Version-1 actor migration wire value. Content identity is carried by
    /// <see cref="PrefabDefinitionId"/> and is independent of Unity asset paths.
    /// </summary>
    public readonly struct ActorMigrationState
    {
        private readonly string[] tags;

        public ActorMigrationState(
            NetworkVector3 position,
            NetworkQuaternion rotation,
            NetworkVector3 scale,
            string prefabDefinitionId,
            float remainingLifeSpan,
            bool canBeDamaged,
            bool hidden,
            ReadOnlySpan<string> tags,
            int ownerConnectionId,
            int instigatorActorId,
            string actorName,
            bool hasBegunPlay)
        {
            if (tags.Length > ActorTagLimits.MaximumTagCount)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(tags),
                    $"Actor migration tags cannot exceed {ActorTagLimits.MaximumTagCount} entries.");
            }

            Position = position;
            Rotation = rotation;
            Scale = scale;
            PrefabDefinitionId = prefabDefinitionId;
            RemainingLifeSpan = remainingLifeSpan;
            CanBeDamaged = canBeDamaged;
            Hidden = hidden;
            this.tags = tags.Length == 0
                ? Array.Empty<string>()
                : tags.ToArray();
            OwnerConnectionId = ownerConnectionId;
            InstigatorActorId = instigatorActorId;
            ActorName = actorName;
            HasBegunPlay = hasBegunPlay;
            ActorMigrationNetworkingExtensions.ValidateMigrationState(in this);
        }

        public NetworkVector3 Position { get; }
        public NetworkQuaternion Rotation { get; }
        public NetworkVector3 Scale { get; }
        public string PrefabDefinitionId { get; }
        public float RemainingLifeSpan { get; }
        public bool CanBeDamaged { get; }
        public bool Hidden { get; }
        public ReadOnlySpan<string> Tags => tags ?? Array.Empty<string>();
        public int TagCount => tags?.Length ?? 0;
        public string GetTag(int index) => (tags ?? Array.Empty<string>())[index];
        public int OwnerConnectionId { get; }
        public int InstigatorActorId { get; }
        public string ActorName { get; }
        public bool HasBegunPlay { get; }
    }

    /// <summary>
    /// Bounded version-1 codec for <see cref="ActorMigrationState"/>. Field order, message identity,
    /// and byte representation are fixed by <see cref="GameplayFrameworkNetworkProtocol"/>.
    /// </summary>
    public static class ActorMigrationNetworkingExtensions
    {
        public const int DefaultMaxRuntimeTagCount = ActorTagLimits.MaximumTagCount;
        public const int MaxPrefabDefinitionIdUtf8Bytes = 1024;
        public const int MaxActorNameUtf8Bytes = 256;
        public const int MaxTagUtf8Bytes = ActorTagLimits.MaximumTagLength * 3;

        /// <summary>
        /// Exact largest payload accepted by this codec: fixed fields, maximum UTF-8 strings, and every
        /// bounded Actor tag. Protocol descriptors use the same value so the transport never advertises a
        /// payload budget smaller than a legal state.
        /// </summary>
        public const int MaximumEncodedSize =
            10 * 4 +
            2 + MaxPrefabDefinitionIdUtf8Bytes +
            4 +
            3 +
            2 + ActorTagLimits.MaximumTagCount * (2 + MaxTagUtf8Bytes) +
            4 +
            4 +
            2 + MaxActorNameUtf8Bytes;

        private const int MaxWireStringBytes = ushort.MaxValue;
        private const int MaxWireTagCount = ushort.MaxValue;
        private const float MinimumQuaternionSqrMagnitude = 1e-8f;
        private const float MaximumQuaternionUnitError = 1e-3f;
        private static readonly Encoding StrictUtf8 = new UTF8Encoding(
            encoderShouldEmitUTF8Identifier: false,
            throwOnInvalidBytes: true);

        public static void WriteMigrationState(this INetWriter writer, in ActorMigrationState state)
        {
            if (writer == null)
            {
                throw new ArgumentNullException(nameof(writer));
            }

            ValidateMigrationState(in state);

            writer.WriteFloat(state.Position.X);
            writer.WriteFloat(state.Position.Y);
            writer.WriteFloat(state.Position.Z);
            writer.WriteFloat(state.Rotation.X);
            writer.WriteFloat(state.Rotation.Y);
            writer.WriteFloat(state.Rotation.Z);
            writer.WriteFloat(state.Rotation.W);
            writer.WriteFloat(state.Scale.X);
            writer.WriteFloat(state.Scale.Y);
            writer.WriteFloat(state.Scale.Z);
            WriteString(writer, state.PrefabDefinitionId, MaxPrefabDefinitionIdUtf8Bytes);
            writer.WriteFloat(state.RemainingLifeSpan);
            writer.WriteByte((byte)(state.CanBeDamaged ? 1 : 0));
            writer.WriteByte((byte)(state.Hidden ? 1 : 0));
            writer.WriteByte((byte)(state.HasBegunPlay ? 1 : 0));

            int tagCount = state.TagCount;
            if (tagCount > MaxWireTagCount)
            {
                throw new InvalidOperationException("Actor migration tag count exceeds the wire format limit.");
            }

            writer.WriteUShort((ushort)tagCount);
            for (int i = 0; i < tagCount; i++)
            {
                WriteString(writer, state.GetTag(i), MaxTagUtf8Bytes);
            }

            writer.WriteInt(state.OwnerConnectionId);
            writer.WriteInt(state.InstigatorActorId);
            WriteString(writer, state.ActorName, MaxActorNameUtf8Bytes);
        }

        public static ActorMigrationState ReadMigrationState(
            this INetReader reader,
            int maxRuntimeTagCount = DefaultMaxRuntimeTagCount)
        {
            if (reader == null)
            {
                throw new ArgumentNullException(nameof(reader));
            }

            int effectiveTagLimit = Math.Min(
                maxRuntimeTagCount > 0 ? maxRuntimeTagCount : DefaultMaxRuntimeTagCount,
                ActorTagLimits.MaximumTagCount);

            float px = ReadFiniteFloat(reader, "Position.x");
            float py = ReadFiniteFloat(reader, "Position.y");
            float pz = ReadFiniteFloat(reader, "Position.z");
            float rx = ReadFiniteFloat(reader, "Rotation.x");
            float ry = ReadFiniteFloat(reader, "Rotation.y");
            float rz = ReadFiniteFloat(reader, "Rotation.z");
            float rw = ReadFiniteFloat(reader, "Rotation.w");
            float sx = ReadFiniteFloat(reader, "Scale.x");
            float sy = ReadFiniteFloat(reader, "Scale.y");
            float sz = ReadFiniteFloat(reader, "Scale.z");

            string prefabDefinitionId = ReadString(reader, MaxPrefabDefinitionIdUtf8Bytes);
            float lifeSpan = ReadFiniteFloat(reader, "RemainingLifeSpan");
            bool canBeDamaged = reader.ReadByte() != 0;
            bool hidden = reader.ReadByte() != 0;
            bool hasBegunPlay = reader.ReadByte() != 0;

            int tagCount = reader.ReadUShort();
            if (tagCount > effectiveTagLimit)
            {
                throw new InvalidOperationException("Actor migration tag count exceeds the runtime safety limit.");
            }

            string[] tags = tagCount == 0 ? Array.Empty<string>() : new string[tagCount];
            for (int i = 0; i < tagCount; i++)
            {
                tags[i] = ReadString(reader, MaxTagUtf8Bytes);
            }

            int ownerConnectionId = reader.ReadInt();
            int instigatorActorId = reader.ReadInt();
            string actorName = ReadString(reader, MaxActorNameUtf8Bytes);

            var state = new ActorMigrationState(
                new NetworkVector3(px, py, pz),
                new NetworkQuaternion(rx, ry, rz, rw),
                new NetworkVector3(sx, sy, sz),
                prefabDefinitionId,
                lifeSpan,
                canBeDamaged,
                hidden,
                tags,
                ownerConnectionId,
                instigatorActorId,
                actorName,
                hasBegunPlay);

            return state;
        }

        /// <summary>Validates the complete snapshot before serialization or Unity state mutation.</summary>
        public static void ValidateMigrationState(in ActorMigrationState state)
        {
            if (!state.Position.IsFinite() || !state.Scale.IsFinite() || !state.Rotation.IsFinite())
            {
                throw new InvalidOperationException("Actor migration transform contains a non-finite value.");
            }

            float rotationSqrMagnitude =
                state.Rotation.X * state.Rotation.X +
                state.Rotation.Y * state.Rotation.Y +
                state.Rotation.Z * state.Rotation.Z +
                state.Rotation.W * state.Rotation.W;
            if (rotationSqrMagnitude < MinimumQuaternionSqrMagnitude ||
                Math.Abs(rotationSqrMagnitude - 1f) > MaximumQuaternionUnitError)
            {
                throw new InvalidOperationException("Actor migration rotation must be a normalized quaternion.");
            }

            if (state.RemainingLifeSpan < 0f ||
                float.IsNaN(state.RemainingLifeSpan) ||
                float.IsInfinity(state.RemainingLifeSpan))
            {
                throw new InvalidOperationException("Actor migration lifespan is invalid.");
            }

            int tagCount = state.TagCount;
            if (tagCount > ActorTagLimits.MaximumTagCount)
            {
                throw new InvalidOperationException(
                    $"Actor migration tags exceed the runtime limit ({ActorTagLimits.MaximumTagCount}).");
            }

            if (string.IsNullOrWhiteSpace(state.PrefabDefinitionId))
            {
                throw new InvalidOperationException("Actor migration requires a PrefabDefinitionId.");
            }

            ValidateUtf8Length(
                state.PrefabDefinitionId,
                MaxPrefabDefinitionIdUtf8Bytes,
                nameof(state.PrefabDefinitionId));
            ValidateUtf8Length(state.ActorName, MaxActorNameUtf8Bytes, nameof(state.ActorName));
            for (int i = 0; i < tagCount; i++)
            {
                string tag = state.GetTag(i);
                if (string.IsNullOrWhiteSpace(tag))
                {
                    throw new InvalidOperationException("Actor migration tags cannot be null, empty, or whitespace.");
                }

                if (tag.Length > ActorTagLimits.MaximumTagLength)
                {
                    throw new InvalidOperationException(
                        $"Actor migration tags cannot exceed {ActorTagLimits.MaximumTagLength} characters.");
                }

                ValidateUtf8Length(tag, MaxTagUtf8Bytes, "Tag");
                for (int previousIndex = 0; previousIndex < i; previousIndex++)
                {
                    if (string.Equals(state.GetTag(previousIndex), tag, StringComparison.Ordinal))
                    {
                        throw new InvalidOperationException("Actor migration tags cannot contain duplicates.");
                    }
                }
            }
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

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void ThrowNotFinite(string field)
        {
            throw new InvalidOperationException("Actor migration field '" + field + "' is not finite.");
        }

        private static void WriteString(INetWriter writer, string value, int maxUtf8Bytes)
        {
            value ??= string.Empty;
            int byteCount = GetStrictUtf8ByteCount(value, "Actor migration string");
            if (byteCount > maxUtf8Bytes || byteCount > MaxWireStringBytes)
            {
                throw new InvalidOperationException("Actor migration string exceeds its safety limit.");
            }

            writer.WriteUShort((ushort)byteCount);
            if (byteCount == 0)
            {
                return;
            }

            if (byteCount <= 256)
            {
                Span<byte> buffer = stackalloc byte[byteCount];
                StrictUtf8.GetBytes(value, buffer);
                writer.WriteBytes(buffer);
                return;
            }

            byte[] rented = ArrayPool<byte>.Shared.Rent(byteCount);
            try
            {
                int written = StrictUtf8.GetBytes(value, 0, value.Length, rented, 0);
                writer.WriteBytes(new ReadOnlySpan<byte>(rented, 0, written));
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(rented, clearArray: false);
            }
        }

        private static string ReadString(INetReader reader, int maxUtf8Bytes)
        {
            int byteCount = reader.ReadUShort();
            if (byteCount > maxUtf8Bytes)
            {
                throw new InvalidOperationException("Actor migration string exceeds its runtime safety limit.");
            }

            if (byteCount == 0)
            {
                return string.Empty;
            }

            if (byteCount <= 256)
            {
                Span<byte> buffer = stackalloc byte[byteCount];
                reader.ReadBytes(buffer, byteCount);
                return StrictUtf8.GetString(buffer);
            }

            byte[] rented = ArrayPool<byte>.Shared.Rent(byteCount);
            try
            {
                reader.ReadBytes(new Span<byte>(rented, 0, byteCount), byteCount);
                return StrictUtf8.GetString(rented, 0, byteCount);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(rented, clearArray: false);
            }
        }

        private static void ValidateUtf8Length(string value, int maxBytes, string field)
        {
            int length = string.IsNullOrEmpty(value) ? 0 : GetStrictUtf8ByteCount(value, field);
            if (length > maxBytes)
            {
                throw new ArgumentException($"{field} exceeds {maxBytes} UTF-8 bytes.", field);
            }
        }

        private static int GetStrictUtf8ByteCount(string value, string field)
        {
            try
            {
                return StrictUtf8.GetByteCount(value);
            }
            catch (EncoderFallbackException exception)
            {
                throw new ArgumentException($"{field} contains invalid Unicode text.", field, exception);
            }
        }
    }
}
