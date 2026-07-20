using System;
using System.Buffers;
using VYaml.Emitter;
using VYaml.Serialization;

namespace CycloneGames.Services.Unity
{
    /// <summary>
    /// VYaml adapter for the serializer-neutral settings core.
    /// </summary>
    public sealed class VYamlSettingsCodec<T> : ISettingsCodec<T> where T : struct
    {
        private readonly YamlSerializerOptions _serializerOptions;
        private readonly bool _clearTemporaryBuffers;

        public VYamlSettingsCodec(
            IYamlFormatterResolver primaryResolver,
            bool clearTemporaryBuffers = true)
        {
            if (primaryResolver == null)
            {
                throw new ArgumentNullException(nameof(primaryResolver));
            }

            _serializerOptions = new YamlSerializerOptions
            {
                Resolver = new VYamlSettingsResolver(primaryResolver)
            };
            _clearTemporaryBuffers = clearTemporaryBuffers;
        }

        public byte[] Serialize(in T settings, int maxByteCount)
        {
            if (maxByteCount <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxByteCount));
            }

            using (var bufferWriter = new ClearableBufferWriter(
                       maxByteCount,
                       _clearTemporaryBuffers))
            {
                var emitter = new Utf8YamlEmitter(bufferWriter);
                YamlSerializer.Serialize(ref emitter, settings, _serializerOptions);
                return NormalizeLineEndings(bufferWriter.WrittenSpan);
            }
        }

        public T Deserialize(ReadOnlyMemory<byte> payload)
        {
            return YamlSerializer.Deserialize<T>(payload, _serializerOptions);
        }

        private static byte[] NormalizeLineEndings(ReadOnlySpan<byte> source)
        {
            int carriageReturnCount = 0;
            int crLfPairCount = 0;
            for (int index = 0; index < source.Length; index++)
            {
                if (source[index] == (byte)'\r')
                {
                    carriageReturnCount++;
                    if (index + 1 < source.Length && source[index + 1] == (byte)'\n')
                    {
                        crLfPairCount++;
                        index++;
                    }
                }
            }

            if (carriageReturnCount == 0)
            {
                return source.ToArray();
            }

            byte[] normalized = new byte[source.Length - crLfPairCount];
            int destinationIndex = 0;
            for (int sourceIndex = 0; sourceIndex < source.Length; sourceIndex++)
            {
                byte value = source[sourceIndex];
                if (value == (byte)'\r')
                {
                    normalized[destinationIndex++] = (byte)'\n';
                    if (sourceIndex + 1 < source.Length && source[sourceIndex + 1] == (byte)'\n')
                    {
                        sourceIndex++;
                    }
                }
                else
                {
                    normalized[destinationIndex++] = value;
                }
            }

            return normalized;
        }

        private sealed class ClearableBufferWriter : IBufferWriter<byte>, IDisposable
        {
            private const int InitialCapacity = 256;

            private readonly int _maxByteCount;
            private readonly bool _clearOnReturn;
            private byte[] _buffer;
            private int _writtenCount;

            public ClearableBufferWriter(int maxByteCount, bool clearOnReturn)
            {
                _maxByteCount = maxByteCount;
                _clearOnReturn = clearOnReturn;
                _buffer = ArrayPool<byte>.Shared.Rent(Math.Min(InitialCapacity, maxByteCount));
            }

            public ReadOnlySpan<byte> WrittenSpan
            {
                get
                {
                    ThrowIfDisposed();
                    return new ReadOnlySpan<byte>(_buffer, 0, _writtenCount);
                }
            }

            public void Advance(int count)
            {
                ThrowIfDisposed();
                if (count < 0 || count > _buffer.Length - _writtenCount)
                {
                    throw new ArgumentOutOfRangeException(nameof(count));
                }

                if (count > _maxByteCount - _writtenCount)
                {
                    throw new SettingsPayloadBudgetExceededException(_maxByteCount);
                }

                _writtenCount += count;
            }

            public Memory<byte> GetMemory(int sizeHint = 0)
            {
                EnsureCapacity(sizeHint);
                return new Memory<byte>(
                    _buffer,
                    _writtenCount,
                    WritableLength());
            }

            public Span<byte> GetSpan(int sizeHint = 0)
            {
                EnsureCapacity(sizeHint);
                return new Span<byte>(
                    _buffer,
                    _writtenCount,
                    WritableLength());
            }

            public void Dispose()
            {
                byte[] buffer = _buffer;
                if (buffer == null)
                {
                    return;
                }

                _buffer = null;
                _writtenCount = 0;
                ArrayPool<byte>.Shared.Return(buffer, _clearOnReturn);
            }

            private void EnsureCapacity(int sizeHint)
            {
                ThrowIfDisposed();
                if (sizeHint < 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(sizeHint));
                }

                int requiredLength = checked(_writtenCount + Math.Max(sizeHint, 1));
                if (requiredLength > _maxByteCount)
                {
                    throw new SettingsPayloadBudgetExceededException(_maxByteCount);
                }

                if (requiredLength <= _buffer.Length)
                {
                    return;
                }

                byte[] next = ArrayPool<byte>.Shared.Rent(requiredLength);
                Buffer.BlockCopy(_buffer, 0, next, 0, _writtenCount);
                ArrayPool<byte>.Shared.Return(_buffer, _clearOnReturn);
                _buffer = next;
            }

            private int WritableLength()
            {
                return Math.Min(
                    _buffer.Length - _writtenCount,
                    _maxByteCount - _writtenCount);
            }

            private void ThrowIfDisposed()
            {
                if (_buffer == null)
                {
                    throw new ObjectDisposedException(nameof(ClearableBufferWriter));
                }
            }
        }
    }
}
