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
        private const int INITIAL_BUFFER_SIZE = 256;
        private const int MAX_RETAINED_BUFFER_SIZE = NetworkConstants.MaxMTU;

        [ThreadStatic] private static PooledBufferWriter _threadWriter;

        private readonly MessagePackSerializerOptions _options;

        public MessagePackSerializerAdapter()
            : this(null)
        {
        }

        public MessagePackSerializerAdapter(MessagePackSerializerOptions options)
        {
            _options = options ?? MessagePackSerializerOptions.Standard;
        }

        public void Serialize<T>(in T value, byte[] buffer, int offset, out int writtenBytes) where T : struct
        {
            if (buffer == null)
            {
                throw new ArgumentNullException(nameof(buffer));
            }

            if ((uint)offset > (uint)buffer.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(offset));
            }

            PooledBufferWriter bufferWriter = GetThreadWriter();
            try
            {
                MessagePackSerializer.Serialize(bufferWriter, value, _options);

                ReadOnlySpan<byte> written = bufferWriter.WrittenSpan;
                if (written.Length > buffer.Length - offset)
                {
                    throw new ArgumentException($"Buffer too small. Need {written.Length} bytes, have {buffer.Length - offset}");
                }

                written.CopyTo(buffer.AsSpan(offset));
                writtenBytes = written.Length;
            }
            finally
            {
                bufferWriter.TrimOversizedBuffer();
            }
        }

        public void Serialize<T>(in T value, INetWriter writer) where T : struct
        {
            if (writer == null)
            {
                throw new ArgumentNullException(nameof(writer));
            }

            PooledBufferWriter bufferWriter = GetThreadWriter();
            try
            {
                MessagePackSerializer.Serialize(bufferWriter, value, _options);
                writer.WriteBytes(bufferWriter.WrittenSpan);
            }
            finally
            {
                bufferWriter.TrimOversizedBuffer();
            }
        }

        public T Deserialize<T>(ReadOnlySpan<byte> data) where T : struct
        {
            if (data.Length == 0)
            {
                return MessagePackSerializer.Deserialize<T>(new ReadOnlyMemory<byte>(Array.Empty<byte>()), _options);
            }

            byte[] rented = ArrayPool<byte>.Shared.Rent(data.Length);
            try
            {
                data.CopyTo(rented);
                return MessagePackSerializer.Deserialize<T>(new ReadOnlyMemory<byte>(rented, 0, data.Length), _options);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(rented);
            }
        }

        public T Deserialize<T>(INetReader reader) where T : struct
        {
            if (reader == null)
            {
                throw new ArgumentNullException(nameof(reader));
            }

            ReadOnlySpan<byte> span = reader.ReadBytesSpan(reader.Remaining);
            return Deserialize<T>(span);
        }

        private static PooledBufferWriter GetThreadWriter()
        {
            PooledBufferWriter writer = _threadWriter;
            if (writer == null)
            {
                writer = new PooledBufferWriter(INITIAL_BUFFER_SIZE);
                _threadWriter = writer;
            }

            writer.Reset();
            return writer;
        }

        private sealed class PooledBufferWriter : IBufferWriter<byte>
        {
            private byte[] _buffer;
            private int _written;

            public PooledBufferWriter(int initialCapacity)
            {
                _buffer = ArrayPool<byte>.Shared.Rent(initialCapacity);
            }

            public ReadOnlySpan<byte> WrittenSpan => new ReadOnlySpan<byte>(_buffer, 0, _written);

            public void Reset()
            {
                _written = 0;
                if (_buffer == null)
                {
                    _buffer = ArrayPool<byte>.Shared.Rent(INITIAL_BUFFER_SIZE);
                }
            }

            public void Advance(int count)
            {
                if (count < 0 || count > _buffer.Length - _written)
                {
                    throw new ArgumentOutOfRangeException(nameof(count));
                }

                _written += count;
            }

            public Memory<byte> GetMemory(int sizeHint = 0)
            {
                EnsureCapacity(sizeHint);
                return new Memory<byte>(_buffer, _written, _buffer.Length - _written);
            }

            public Span<byte> GetSpan(int sizeHint = 0)
            {
                EnsureCapacity(sizeHint);
                return new Span<byte>(_buffer, _written, _buffer.Length - _written);
            }

            public void TrimOversizedBuffer()
            {
                if (_buffer == null || _buffer.Length <= MAX_RETAINED_BUFFER_SIZE)
                {
                    return;
                }

                ArrayPool<byte>.Shared.Return(_buffer);
                _buffer = ArrayPool<byte>.Shared.Rent(INITIAL_BUFFER_SIZE);
            }

            private void EnsureCapacity(int sizeHint)
            {
                if (sizeHint < 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(sizeHint));
                }

                if (sizeHint == 0)
                {
                    sizeHint = 1;
                }

                if (_buffer == null)
                {
                    _buffer = ArrayPool<byte>.Shared.Rent(Math.Max(INITIAL_BUFFER_SIZE, sizeHint));
                    return;
                }

                if (sizeHint <= _buffer.Length - _written)
                {
                    return;
                }

                int required = checked(_written + sizeHint);
                int newCapacity = Math.Max(required, _buffer.Length * 2);
                byte[] newBuffer = ArrayPool<byte>.Shared.Rent(newCapacity);
                Buffer.BlockCopy(_buffer, 0, newBuffer, 0, _written);
                ArrayPool<byte>.Shared.Return(_buffer);
                _buffer = newBuffer;
            }
        }
    }
}
#endif
