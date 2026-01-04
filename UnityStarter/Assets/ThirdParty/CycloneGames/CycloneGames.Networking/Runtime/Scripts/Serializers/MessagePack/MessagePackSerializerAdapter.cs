#if MESSAGEPACK
using System;
using System.Buffers;
using CycloneGames.Networking.Serialization;
using MessagePack;

namespace CycloneGames.Networking.Serializer.MessagePack
{
    /// <summary>
    /// MessagePack-CSharp provides excellent performance with minimal allocations.
    /// Use [MessagePackObject] and [Key] attributes on your message structs.
    /// </summary>
    public sealed class MessagePackSerializerAdapter : INetSerializer
    {
        private readonly MessagePackSerializerOptions _options;

        public MessagePackSerializerAdapter() : this(null) { }

        public MessagePackSerializerAdapter(MessagePackSerializerOptions options)
        {
            _options = options ?? MessagePackSerializerOptions.Standard;
        }

        public void Serialize<T>(in T value, byte[] buffer, int offset, out int writtenBytes) where T : struct
        {
            // Use ArrayBufferWriter for IBufferWriter interface
            var bufferWriter = new ArrayBufferWriter<byte>(256);
            MessagePackSerializer.Serialize(bufferWriter, value, _options);

            var written = bufferWriter.WrittenSpan;
            if (offset + written.Length > buffer.Length)
                throw new ArgumentException($"Buffer too small. Need {written.Length} bytes, have {buffer.Length - offset}");

            written.CopyTo(buffer.AsSpan(offset));
            writtenBytes = written.Length;
        }

        public void Serialize<T>(in T value, INetWriter writer) where T : struct
        {
            // Use ArrayBufferWriter to collect serialized bytes
            var bufferWriter = new ArrayBufferWriter<byte>(256);
            MessagePackSerializer.Serialize(bufferWriter, value, _options);
            writer.WriteBytes(bufferWriter.WrittenSpan);
        }

        public T Deserialize<T>(ReadOnlySpan<byte> data) where T : struct
        {
            // Create ReadOnlyMemory from span for MessagePack
            var memory = new ReadOnlyMemory<byte>(data.ToArray());
            return MessagePackSerializer.Deserialize<T>(memory, _options);
        }

        public T Deserialize<T>(INetReader reader) where T : struct
        {
            var span = reader.ReadBytesSpan(reader.Remaining);
            return Deserialize<T>(span);
        }
    }
}
#endif
