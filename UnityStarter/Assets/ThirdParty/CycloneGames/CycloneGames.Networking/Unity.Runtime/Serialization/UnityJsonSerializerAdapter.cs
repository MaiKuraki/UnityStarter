using System;
using System.Buffers;
using System.Text;
using CycloneGames.Networking.Serialization;

namespace CycloneGames.Networking.Unity.Runtime.Serialization
{
    public sealed class UnityJsonSerializerAdapter : INetSerializer
    {
        private const int STACKALLOC_THRESHOLD = 512;

        public static readonly UnityJsonSerializerAdapter Instance = new UnityJsonSerializerAdapter();

        private static readonly Encoding Utf8 = Encoding.UTF8;

        public void Serialize<T>(in T value, byte[] buffer, int offset, out int writtenBytes) where T : struct
        {
            if (buffer == null)
            {
                throw new ArgumentNullException(nameof(buffer));
            }

            if (offset < 0 || offset > buffer.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(offset));
            }

            string json = UnityEngine.JsonUtility.ToJson(value);
            int byteCount = Utf8.GetByteCount(json);
            if (byteCount > buffer.Length - offset)
            {
                throw new ArgumentException(
                    $"Buffer too small. Need {byteCount} bytes, have {buffer.Length - offset}");
            }

            writtenBytes = Utf8.GetBytes(json, 0, json.Length, buffer, offset);
        }

        public void Serialize<T>(in T value, INetWriter writer) where T : struct
        {
            if (writer == null)
            {
                throw new ArgumentNullException(nameof(writer));
            }

            string json = UnityEngine.JsonUtility.ToJson(value);
            WriteUtf8String(json, writer);
        }

        public T Deserialize<T>(ReadOnlySpan<byte> data) where T : struct
        {
            string json = Utf8.GetString(data);
            return UnityEngine.JsonUtility.FromJson<T>(json);
        }

        public T Deserialize<T>(INetReader reader) where T : struct
        {
            var span = reader.ReadBytesSpan(reader.Remaining);
            return Deserialize<T>(span);
        }

        private static void WriteUtf8String(string json, INetWriter writer)
        {
            int byteCount = Utf8.GetByteCount(json);
            if (byteCount == 0)
            {
                writer.WriteBytes(ReadOnlySpan<byte>.Empty);
                return;
            }

            if (byteCount <= STACKALLOC_THRESHOLD)
            {
                Span<byte> bytes = stackalloc byte[byteCount];
                int writtenBytes = Utf8.GetBytes(json.AsSpan(), bytes);
                writer.WriteBytes(bytes.Slice(0, writtenBytes));
                return;
            }

            byte[] rented = ArrayPool<byte>.Shared.Rent(byteCount);
            try
            {
                int writtenBytes = Utf8.GetBytes(json, 0, json.Length, rented, 0);
                writer.WriteBytes(new ReadOnlySpan<byte>(rented, 0, writtenBytes));
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(rented);
            }
        }
    }
}
