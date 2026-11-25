using System;
using System.Buffers;

namespace CycloneGames.Persistence
{
    // Pooled, budget-capped IBufferWriter<byte>. One Store reuses one instance: Reset acquires a
    // buffer for the operation and Release returns it to the shared pool. Buffers are cleared on
    // growth and on return so serialized payload bytes never remain in the pool.
    internal sealed class BoundedByteBufferWriter : IBufferWriter<byte>, IDisposable
    {
        private readonly int _initialBytes;
        private readonly int _maximumBytes;
        private byte[] _buffer;
        private int _writtenCount;
        private int _lastGrantedLength;

        internal BoundedByteBufferWriter(int initialBytes, int maximumBytes)
        {
            if (initialBytes <= 0 || initialBytes > maximumBytes)
            {
                throw new ArgumentOutOfRangeException(nameof(initialBytes));
            }

            _initialBytes = initialBytes;
            _maximumBytes = maximumBytes;
        }

        internal int WrittenCount => _writtenCount;

        internal ReadOnlySpan<byte> WrittenSpan
        {
            get
            {
                ThrowIfDisposed();
                return new ReadOnlySpan<byte>(_buffer, 0, _writtenCount);
            }
        }

        internal void Reset()
        {
            if (_buffer == null)
            {
                _buffer = ArrayPool<byte>.Shared.Rent(_initialBytes);
            }

            Array.Clear(_buffer, 0, _buffer.Length);
            _writtenCount = 0;
            _lastGrantedLength = 0;
        }

        internal void Release()
        {
            byte[] buffer = _buffer;
            if (buffer == null)
            {
                return;
            }

            _buffer = null;
            _writtenCount = 0;
            _lastGrantedLength = 0;
            ArrayPool<byte>.Shared.Return(buffer, clearArray: true);
        }

        // IBufferWriter contract: count must not exceed the most recent GetSpan/GetMemory grant.
        public void Advance(int count)
        {
            ThrowIfDisposed();
            if (count < 0 || count > _lastGrantedLength)
            {
                throw new ArgumentOutOfRangeException(nameof(count));
            }

            _writtenCount += count;
            _lastGrantedLength = 0;
        }

        public Memory<byte> GetMemory(int sizeHint = 0)
        {
            EnsureCapacity(sizeHint);
            _lastGrantedLength = WritableBytes();
            return new Memory<byte>(_buffer, _writtenCount, _lastGrantedLength);
        }

        public Span<byte> GetSpan(int sizeHint = 0)
        {
            EnsureCapacity(sizeHint);
            _lastGrantedLength = WritableBytes();
            return new Span<byte>(_buffer, _writtenCount, _lastGrantedLength);
        }

        public void Dispose()
        {
            Release();
        }

        // Growth doubles up to the configured maximum. A sizeHint beyond the remaining budget
        // throws before the codec can write past the payload limit.
        private void EnsureCapacity(int sizeHint)
        {
            ThrowIfDisposed();
            _lastGrantedLength = 0;
            if (sizeHint < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(sizeHint));
            }

            int requested = Math.Max(sizeHint, 1);
            if (requested > _maximumBytes - _writtenCount)
            {
                throw new PersistencePayloadBudgetExceededException(_maximumBytes);
            }

            if (requested <= _buffer.Length - _writtenCount)
            {
                return;
            }

            int required = checked(_writtenCount + requested);
            int doubled = _buffer.Length <= _maximumBytes / 2
                ? _buffer.Length * 2
                : _maximumBytes;
            int requestedCapacity = Math.Min(_maximumBytes, Math.Max(required, doubled));
            byte[] next = ArrayPool<byte>.Shared.Rent(requestedCapacity);
            Array.Clear(next, 0, next.Length);
            Buffer.BlockCopy(_buffer, 0, next, 0, _writtenCount);
            ArrayPool<byte>.Shared.Return(_buffer, clearArray: true);
            _buffer = next;
        }

        private int WritableBytes()
        {
            return Math.Min(
                _buffer.Length - _writtenCount,
                _maximumBytes - _writtenCount);
        }

        private void ThrowIfDisposed()
        {
            if (_buffer == null)
            {
                throw new ObjectDisposedException(nameof(BoundedByteBufferWriter));
            }
        }
    }
}
