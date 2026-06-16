using CycloneGames.Networking.Serialization;

namespace CycloneGames.GameplayFramework.Runtime.Integrations.Networking
{
    /// <summary>
    /// Extension methods for serializing <see cref="ActorMigrationState"/> through
    /// <see cref="INetWriter"/> and <see cref="INetReader"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// These methods live in the Integrations.Networking asmdef so the base
    /// GameplayFramework.Runtime has no dependency on Networking types.
    /// </para>
    /// <para>
    /// String fields (PrefabAssetPath, ActorName, Tags) use a length-prefixed
    /// encoding. Callers must ensure the combined payload fits within the
    /// transport MTU for the target channel.
    /// </para>
    /// </remarks>
    public static class ActorMigrationNetworkingExtensions
    {
        private const int MaxUtf8StringBytes = ushort.MaxValue;
        private const int MaxTagCount = ushort.MaxValue;

        public static void WriteMigrationState(this INetWriter writer, in ActorMigrationState state)
        {
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

            WriteString(writer, state.PrefabAssetPath ?? string.Empty);

            writer.WriteFloat(state.RemainingLifeSpan);
            writer.WriteByte((byte)(state.CanBeDamaged ? 1 : 0));
            writer.WriteByte((byte)(state.Hidden ? 1 : 0));
            writer.WriteByte((byte)(state.HasBegunPlay ? 1 : 0));

            int tagCount = state.Tags?.Length ?? 0;
            if (tagCount > MaxTagCount)
                throw new System.InvalidOperationException("Actor migration tag count exceeds the wire format limit.");

            writer.WriteUShort((ushort)tagCount);
            for (int i = 0; i < tagCount; i++)
            {
                WriteString(writer, state.Tags[i]);
            }

            writer.WriteInt(state.OwnerConnectionId);
            writer.WriteInt(state.InstigatorActorId);

            WriteString(writer, state.ActorName ?? string.Empty);
        }

        public static ActorMigrationState ReadMigrationState(this INetReader reader)
        {
            float px = reader.ReadFloat();
            float py = reader.ReadFloat();
            float pz = reader.ReadFloat();

            float rx = reader.ReadFloat();
            float ry = reader.ReadFloat();
            float rz = reader.ReadFloat();
            float rw = reader.ReadFloat();

            float sx = reader.ReadFloat();
            float sy = reader.ReadFloat();
            float sz = reader.ReadFloat();

            string prefabPath = ReadString(reader);
            float lifeSpan = reader.ReadFloat();
            bool canBeDamaged = reader.ReadByte() != 0;
            bool hidden = reader.ReadByte() != 0;
            bool hasBegunPlay = reader.ReadByte() != 0;

            int tagCount = reader.ReadUShort();
            string[] tags = null;
            if (tagCount > 0)
            {
                tags = new string[tagCount];
                for (int i = 0; i < tagCount; i++)
                {
                    tags[i] = ReadString(reader);
                }
            }

            int ownerConnId = reader.ReadInt();
            int instigatorId = reader.ReadInt();
            string actorName = ReadString(reader);

            return new ActorMigrationState(
                new UnityEngine.Vector3(px, py, pz),
                new UnityEngine.Quaternion(rx, ry, rz, rw),
                new UnityEngine.Vector3(sx, sy, sz),
                prefabPath,
                lifeSpan,
                canBeDamaged,
                hidden,
                tags,
                ownerConnId,
                instigatorId,
                actorName,
                hasBegunPlay
            );
        }

        private static void WriteString(INetWriter writer, string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                writer.WriteUShort(0);
                return;
            }

            int charCount = value.Length;
            // Worst-case UTF-8 expansion is 3 bytes per char (BMP).
            // Short strings use stack allocation to avoid ArrayPool overhead.
            const int stackCharThreshold = 64;
            if (charCount <= stackCharThreshold)
            {
                int maxBytes = charCount * 3;
                System.Span<byte> stackBuf = stackalloc byte[maxBytes];
                int actualBytes = System.Text.Encoding.UTF8.GetBytes(value, stackBuf);
                if (actualBytes > MaxUtf8StringBytes)
                    throw new System.InvalidOperationException("Actor migration string exceeds the wire format limit.");
                writer.WriteUShort((ushort)actualBytes);
                writer.WriteBytes(stackBuf.Slice(0, actualBytes));
            }
            else
            {
                int byteCount = System.Text.Encoding.UTF8.GetByteCount(value);
                if (byteCount > MaxUtf8StringBytes)
                    throw new System.InvalidOperationException("Actor migration string exceeds the wire format limit.");

                writer.WriteUShort((ushort)byteCount);
                byte[] rented = System.Buffers.ArrayPool<byte>.Shared.Rent(byteCount);
                try
                {
                    System.Text.Encoding.UTF8.GetBytes(value, 0, value.Length, rented, 0);
                    writer.WriteBytes(new System.ReadOnlySpan<byte>(rented, 0, byteCount));
                }
                finally
                {
                    System.Buffers.ArrayPool<byte>.Shared.Return(rented);
                }
            }
        }

        private static string ReadString(INetReader reader)
        {
            int byteCount = reader.ReadUShort();
            if (byteCount == 0) return string.Empty;

            const int stackThreshold = 256;
            if (byteCount <= stackThreshold)
            {
                System.Span<byte> stackBuf = stackalloc byte[byteCount];
                reader.ReadBytes(stackBuf, byteCount);
                return System.Text.Encoding.UTF8.GetString(stackBuf);
            }
            else
            {
                byte[] rented = System.Buffers.ArrayPool<byte>.Shared.Rent(byteCount);
                try
                {
                    reader.ReadBytes(new System.Span<byte>(rented, 0, byteCount), byteCount);
                    return System.Text.Encoding.UTF8.GetString(rented, 0, byteCount);
                }
                finally
                {
                    System.Buffers.ArrayPool<byte>.Shared.Return(rented);
                }
            }
        }
    }
}
