using System;
using System.Buffers;
using System.Runtime.CompilerServices;
using System.Text;
using CycloneGames.GameplayFramework.Runtime;
using CycloneGames.Networking.Serialization;
using UnityEngine;

namespace CycloneGames.GameplayFramework.Networking
{
    /// <summary>
    /// Version-1 actor migration wire DTO owned by the GameplayFramework Networking integration.
    /// Protocol identity is defined by <see cref="GameplayFrameworkNetworkProtocol"/>, not by the CLR type name.
    /// </summary>
    public readonly struct ActorMigrationState
    {
        public const int SchemaVersion = 1;

        public ActorMigrationState(
            Vector3 position,
            Quaternion rotation,
            Vector3 scale,
            string prefabAssetPath,
            float remainingLifeSpan,
            bool canBeDamaged,
            bool hidden,
            string[] tags,
            int ownerConnectionId,
            int instigatorActorId,
            string actorName,
            bool hasBegunPlay)
        {
            Position = position;
            Rotation = rotation;
            Scale = scale;
            PrefabAssetPath = prefabAssetPath;
            RemainingLifeSpan = remainingLifeSpan;
            CanBeDamaged = canBeDamaged;
            Hidden = hidden;
            Tags = tags;
            OwnerConnectionId = ownerConnectionId;
            InstigatorActorId = instigatorActorId;
            ActorName = actorName;
            HasBegunPlay = hasBegunPlay;
        }

        public Vector3 Position { get; }
        public Quaternion Rotation { get; }
        public Vector3 Scale { get; }
        public string PrefabAssetPath { get; }
        public float RemainingLifeSpan { get; }
        public bool CanBeDamaged { get; }
        public bool Hidden { get; }
        public string[] Tags { get; }
        public int OwnerConnectionId { get; }
        public int InstigatorActorId { get; }
        public string ActorName { get; }
        public bool HasBegunPlay { get; }
    }

    /// <summary>
    /// Version-1 Actor migration wire codec and Unity adapter. The byte layout and message ID
    /// remain stable while ownership of migration behavior stays outside the base runtime.
    /// </summary>
    public static class ActorMigrationNetworkingExtensions
    {
        public const int DefaultMaxRuntimeTagCount = Actor.MaxActorTags;
        public const int MaxPrefabDefinitionIdUtf8Bytes = 1024;
        public const int MaxActorNameUtf8Bytes = 256;
        public const int MaxTagUtf8Bytes = Actor.MaxActorTagLength * 3;

        private const int MaxWireStringBytes = ushort.MaxValue;
        private const int MaxWireTagCount = ushort.MaxValue;

        /// <summary>
        /// Captures an explicit network migration snapshot. The caller supplies a stable content
        /// definition ID; Unity object names and runtime instance IDs are not accepted as identity.
        /// </summary>
        public static ActorMigrationState CaptureMigrationState(
            this Actor actor,
            string prefabDefinitionId,
            int ownerConnectionId,
            int instigatorActorId)
        {
            if (actor == null)
            {
                throw new ArgumentNullException(nameof(actor));
            }

            ValidateUtf8Length(prefabDefinitionId, MaxPrefabDefinitionIdUtf8Bytes, nameof(prefabDefinitionId));

            int tagCount = actor.TagCount;
            string[] tags = tagCount == 0 ? Array.Empty<string>() : new string[tagCount];
            if (tagCount > 0)
            {
                actor.CopyTagsTo(tags);
            }

            return new ActorMigrationState(
                actor.GetActorLocation(),
                actor.GetActorRotation(),
                actor.GetActorScale(),
                prefabDefinitionId,
                actor.GetRemainingLifeSpan(),
                actor.CanBeDamaged(),
                actor.IsHidden(),
                tags,
                ownerConnectionId,
                instigatorActorId,
                actor.GetName(),
                actor.HasBegunPlay);
        }

        /// <summary>
        /// Applies Unity-facing state after the target World has spawned and registered the Actor.
        /// Owner/instigator identifiers are returned in the DTO for the network layer to resolve.
        /// </summary>
        public static void ApplyMigrationState(this Actor actor, in ActorMigrationState state)
        {
            if (actor == null)
            {
                throw new ArgumentNullException(nameof(actor));
            }

            ValidateSnapshot(in state);
            actor.SetActorLocationAndRotation(state.Position, state.Rotation);
            actor.SetActorScale(state.Scale);
            actor.SetCanBeDamaged(state.CanBeDamaged);
            actor.ReplaceTags(state.Tags);
            actor.SetActorHiddenInGame(state.Hidden);
            actor.SetLifeSpan(Mathf.Max(0f, state.RemainingLifeSpan));
            if (!string.IsNullOrEmpty(state.ActorName))
            {
                actor.gameObject.name = state.ActorName;
            }
        }

        public static void WriteMigrationState(this INetWriter writer, in ActorMigrationState state)
        {
            if (writer == null) throw new ArgumentNullException(nameof(writer));
            ValidateSnapshot(in state);

            writer.WriteFloat(state.Position.x);
            writer.WriteFloat(state.Position.y);
            writer.WriteFloat(state.Position.z);
            writer.WriteFloat(state.Rotation.x);
            writer.WriteFloat(state.Rotation.y);
            writer.WriteFloat(state.Rotation.z);
            writer.WriteFloat(state.Rotation.w);
            writer.WriteFloat(state.Scale.x);
            writer.WriteFloat(state.Scale.y);
            writer.WriteFloat(state.Scale.z);
            WriteString(writer, state.PrefabAssetPath, MaxPrefabDefinitionIdUtf8Bytes);
            writer.WriteFloat(state.RemainingLifeSpan);
            writer.WriteByte((byte)(state.CanBeDamaged ? 1 : 0));
            writer.WriteByte((byte)(state.Hidden ? 1 : 0));
            writer.WriteByte((byte)(state.HasBegunPlay ? 1 : 0));

            int tagCount = state.Tags?.Length ?? 0;
            if (tagCount > MaxWireTagCount)
            {
                throw new InvalidOperationException("Actor migration tag count exceeds the wire format limit.");
            }

            writer.WriteUShort((ushort)tagCount);
            for (int i = 0; i < tagCount; i++)
            {
                WriteString(writer, state.Tags[i], MaxTagUtf8Bytes);
            }

            writer.WriteInt(state.OwnerConnectionId);
            writer.WriteInt(state.InstigatorActorId);
            WriteString(writer, state.ActorName, MaxActorNameUtf8Bytes);
        }

        public static ActorMigrationState ReadMigrationState(
            this INetReader reader,
            int maxRuntimeTagCount = DefaultMaxRuntimeTagCount)
        {
            if (reader == null) throw new ArgumentNullException(nameof(reader));
            int effectiveTagLimit = Math.Min(
                maxRuntimeTagCount > 0 ? maxRuntimeTagCount : DefaultMaxRuntimeTagCount,
                Actor.MaxActorTags);

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

            string prefabPath = ReadString(reader, MaxPrefabDefinitionIdUtf8Bytes);
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
                new Vector3(px, py, pz),
                new Quaternion(rx, ry, rz, rw),
                new Vector3(sx, sy, sz),
                prefabPath,
                lifeSpan,
                canBeDamaged,
                hidden,
                tags,
                ownerConnectionId,
                instigatorActorId,
                actorName,
                hasBegunPlay);

            ValidateSnapshot(in state);
            return state;
        }

        private static void ValidateSnapshot(in ActorMigrationState state)
        {
            if (!IsFinite(state.Position) || !IsFinite(state.Scale) || !IsFinite(state.Rotation))
            {
                throw new InvalidOperationException("Actor migration transform contains a non-finite value.");
            }

            if (state.RemainingLifeSpan < 0f || float.IsNaN(state.RemainingLifeSpan) || float.IsInfinity(state.RemainingLifeSpan))
            {
                throw new InvalidOperationException("Actor migration lifespan is invalid.");
            }

            int tagCount = state.Tags?.Length ?? 0;
            if (tagCount > Actor.MaxActorTags)
            {
                throw new InvalidOperationException($"Actor migration tags exceed the runtime limit ({Actor.MaxActorTags}).");
            }

            ValidateUtf8Length(state.PrefabAssetPath, MaxPrefabDefinitionIdUtf8Bytes, nameof(state.PrefabAssetPath));
            ValidateUtf8Length(state.ActorName, MaxActorNameUtf8Bytes, nameof(state.ActorName));
            for (int i = 0; i < tagCount; i++)
            {
                ValidateUtf8Length(state.Tags[i], MaxTagUtf8Bytes, "Tag");
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
            int byteCount = Encoding.UTF8.GetByteCount(value);
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
                Encoding.UTF8.GetBytes(value, buffer);
                writer.WriteBytes(buffer);
                return;
            }

            byte[] rented = ArrayPool<byte>.Shared.Rent(byteCount);
            try
            {
                int written = Encoding.UTF8.GetBytes(value, 0, value.Length, rented, 0);
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
                return Encoding.UTF8.GetString(buffer);
            }

            byte[] rented = ArrayPool<byte>.Shared.Rent(byteCount);
            try
            {
                reader.ReadBytes(new Span<byte>(rented, 0, byteCount), byteCount);
                return Encoding.UTF8.GetString(rented, 0, byteCount);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(rented, clearArray: false);
            }
        }

        private static void ValidateUtf8Length(string value, int maxBytes, string field)
        {
            int length = string.IsNullOrEmpty(value) ? 0 : Encoding.UTF8.GetByteCount(value);
            if (length > maxBytes)
            {
                throw new ArgumentException($"{field} exceeds {maxBytes} UTF-8 bytes.", field);
            }
        }

        private static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
        }

        private static bool IsFinite(Quaternion value)
        {
            return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z) && IsFinite(value.w);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
