using System;
using CycloneGames.Networking.Serialization;

namespace CycloneGames.Networking.Unity.Runtime.Serialization
{
    public sealed class UnityJsonSerializerAdapter : INetSerializer
    {
        public static readonly UnityJsonSerializerAdapter Instance = new UnityJsonSerializerAdapter();

        public void Serialize<T>(in T value, byte[] buffer, int offset, out int writtenBytes) where T : struct
        {
            string json = UnityEngine.JsonUtility.ToJson(value);
            byte[] bytes = System.Text.Encoding.UTF8.GetBytes(json);
            if (offset + bytes.Length > buffer.Length)
                throw new IndexOutOfRangeException("Buffer too small");

            Array.Copy(bytes, 0, buffer, offset, bytes.Length);
            writtenBytes = bytes.Length;
        }

        public void Serialize<T>(in T value, INetWriter writer) where T : struct
        {
            string json = UnityEngine.JsonUtility.ToJson(value);
            byte[] bytes = System.Text.Encoding.UTF8.GetBytes(json);
            writer.WriteBytes(bytes);
        }

        public T Deserialize<T>(ReadOnlySpan<byte> data) where T : struct
        {
            byte[] bytes = data.ToArray();
            string json = System.Text.Encoding.UTF8.GetString(bytes);
            return UnityEngine.JsonUtility.FromJson<T>(json);
        }

        public T Deserialize<T>(INetReader reader) where T : struct
        {
            var span = reader.ReadBytesSpan(reader.Remaining);
            return Deserialize<T>(span);
        }
    }
}