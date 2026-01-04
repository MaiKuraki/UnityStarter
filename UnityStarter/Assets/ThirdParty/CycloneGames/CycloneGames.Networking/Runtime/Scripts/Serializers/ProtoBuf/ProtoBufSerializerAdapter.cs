#if PROTOBUF
using System;
using CycloneGames.Networking.Serialization;
using Google.Protobuf;

namespace CycloneGames.Networking.Serializer.ProtoBuf
{
    /// <summary>
    /// Google Protocol Buffers serializer adapter.
    /// Message types must implement IMessage from Google.Protobuf.
    /// Note: ProtoBuf requires code generation - use protoc compiler.
    /// </summary>
    public sealed class ProtoBufSerializerAdapter : INetSerializer
    {
        public void Serialize<T>(in T value, byte[] buffer, int offset, out int writtenBytes) where T : struct
        {
            // ProtoBuf requires IMessage interface, so we need boxing
            // For true zero-GC, consider using proto-generated classes
            if (value is IMessage message)
            {
                var span = new Span<byte>(buffer, offset, buffer.Length - offset);
                message.WriteTo(span);
                writtenBytes = message.CalculateSize();
            }
            else
            {
                throw new InvalidOperationException(
                    $"ProtoBufSerializerAdapter requires types implementing IMessage. Type {typeof(T).Name} does not implement IMessage.");
            }
        }

        public void Serialize<T>(in T value, INetWriter writer) where T : struct
        {
            if (value is IMessage message)
            {
                int size = message.CalculateSize();
                Span<byte> temp = stackalloc byte[size];
                message.WriteTo(temp);
                writer.WriteBytes(temp);
            }
            else
            {
                throw new InvalidOperationException(
                    $"ProtoBufSerializerAdapter requires types implementing IMessage.");
            }
        }

        public T Deserialize<T>(ReadOnlySpan<byte> data) where T : struct
        {
            // ProtoBuf deserialization requires a parser, typically accessed via MessageParser<T>
            // This is a limitation of ProtoBuf's design - consider using generated parsers directly
            throw new NotSupportedException(
                "ProtoBuf deserialization requires MessageParser<T>. Use ProtoBufSerializerAdapter<T> with specific type or access parser directly.");
        }

        public T Deserialize<T>(INetReader reader) where T : struct
        {
            var span = reader.ReadBytesSpan(reader.Remaining);
            return Deserialize<T>(span);
        }
    }

    /// <summary>
    /// Typed ProtoBuf serializer adapter for specific message types.
    /// Use this when you know the exact message type at compile time.
    /// </summary>
    public sealed class ProtoBufSerializerAdapter<TMessage> : INetSerializer 
        where TMessage : IMessage<TMessage>, new()
    {
        private readonly MessageParser<TMessage> _parser;

        public ProtoBufSerializerAdapter()
        {
            _parser = new MessageParser<TMessage>(() => new TMessage());
        }

        public void Serialize<T>(in T value, byte[] buffer, int offset, out int writtenBytes) where T : struct
        {
            if (value is TMessage message)
            {
                var span = new Span<byte>(buffer, offset, buffer.Length - offset);
                message.WriteTo(span);
                writtenBytes = message.CalculateSize();
            }
            else
            {
                throw new InvalidOperationException($"Expected type {typeof(TMessage).Name}, got {typeof(T).Name}");
            }
        }

        public void Serialize<T>(in T value, INetWriter writer) where T : struct
        {
            if (value is TMessage message)
            {
                int size = message.CalculateSize();
                Span<byte> temp = stackalloc byte[size];
                message.WriteTo(temp);
                writer.WriteBytes(temp);
            }
            else
            {
                throw new InvalidOperationException($"Expected type {typeof(TMessage).Name}");
            }
        }

        public T Deserialize<T>(ReadOnlySpan<byte> data) where T : struct
        {
            var message = _parser.ParseFrom(data);
            if (message is T result)
            {
                return result;
            }
            throw new InvalidOperationException($"Cannot convert {typeof(TMessage).Name} to {typeof(T).Name}");
        }

        public T Deserialize<T>(INetReader reader) where T : struct
        {
            var span = reader.ReadBytesSpan(reader.Remaining);
            return Deserialize<T>(span);
        }
    }
}
#endif
