using System;
using System.Buffers;
using System.Runtime.CompilerServices;
using System.Threading;
using CycloneGames.Networking.Serialization;

namespace CycloneGames.Networking.Buffers
{
    /// <summary>
    /// Generation-checked lease over pooled network-buffer storage.
    /// </summary>
    /// <remarks>
    /// A lease and any spans or array segments obtained from it are valid only until the
    /// lease is disposed. Copies share the same lease token; disposing any copy invalidates
    /// every copy. Buffer operations are single-owner and are not safe to run concurrently.
    /// </remarks>
    public readonly struct NetworkBuffer : INetWriter, INetReader, IDisposable
    {
        private readonly NetworkBufferStorage _storage;
        private readonly long _leaseToken;

        internal NetworkBuffer(NetworkBufferStorage storage, long leaseToken)
        {
            _storage = storage ?? throw new ArgumentNullException(nameof(storage));
            _leaseToken = leaseToken;
        }

        public int Position
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => GetStorage().Position;
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set => GetStorage().SetPosition(value);
        }

        public int Capacity
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => GetStorage().Capacity;
        }

        public int Remaining
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => GetStorage().Remaining;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteByte(byte value) => GetStorage().WriteByte(value);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteBytes(ReadOnlySpan<byte> data) => GetStorage().WriteBytes(data);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteUShort(ushort value) => GetStorage().WriteUShort(value);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteInt(int value) => GetStorage().WriteInt(value);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteUInt(uint value) => GetStorage().WriteUInt(value);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteLong(long value) => GetStorage().WriteLong(value);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteULong(ulong value) => GetStorage().WriteULong(value);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteFloat(float value) => GetStorage().WriteFloat(value);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ArraySegment<byte> ToArraySegment() => GetStorage().ToArraySegment();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Reset() => GetStorage().Reset();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void FlipForRead() => GetStorage().FlipForRead();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public byte ReadByte() => GetStorage().ReadByte();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void ReadBytes(Span<byte> destination, int count) => GetStorage().ReadBytes(destination, count);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ushort ReadUShort() => GetStorage().ReadUShort();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int ReadInt() => GetStorage().ReadInt();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public uint ReadUInt() => GetStorage().ReadUInt();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public long ReadLong() => GetStorage().ReadLong();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ulong ReadULong() => GetStorage().ReadULong();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float ReadFloat() => GetStorage().ReadFloat();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ReadOnlySpan<byte> ReadBytesSpan(int count) => GetStorage().ReadBytesSpan(count);

        internal void SetBuffer(ArraySegment<byte> data) => GetStorage().SetBuffer(data);

        internal void SetBuffer(ReadOnlySpan<byte> data) => GetStorage().SetBuffer(data);

        public void Dispose()
        {
            NetworkBufferPool.ReturnLease(_storage, _leaseToken);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private NetworkBufferStorage GetStorage()
        {
            NetworkBufferStorage storage = _storage;
            if (storage == null || !storage.IsLeaseActive(_leaseToken))
            {
                throw new ObjectDisposedException(
                    nameof(NetworkBuffer),
                    "The network-buffer lease is default, disposed, stale, or has already been returned.");
            }

            return storage;
        }
    }

    /// <summary>
    /// Mutable state behind a <see cref="NetworkBuffer"/> lease. Instances are owned by
    /// <see cref="NetworkBufferPool"/> and never escape the assembly.
    /// </summary>
    internal sealed class NetworkBufferStorage
    {
        private const int DefaultCapacity = 1500;
        private const int MaxCapacity = 65535;

        private byte[] _buffer;
        private int _position;
        private int _length;
        private int _initializedLength;
        private bool _readMode;

        // Even values are available; odd values identify one active lease. The entire
        // generation/state transition is one atomic value, so an old lease cannot return
        // storage while a newer lease is being published.
        private long _leaseToken;

        internal int Position => _position;
        internal int Capacity => _buffer == null ? 0 : Math.Min(_buffer.Length, MaxCapacity);
        internal int Remaining => _length - _position;

        internal long BeginLease()
        {
            long availableToken = Volatile.Read(ref _leaseToken);
            if ((availableToken & 1L) != 0L)
            {
                throw new InvalidOperationException("Network-buffer pool corruption: storage is already leased.");
            }

            long activeToken = unchecked(availableToken + 1L);
            if (Interlocked.CompareExchange(ref _leaseToken, activeToken, availableToken) != availableToken)
            {
                throw new InvalidOperationException("Network-buffer pool corruption: storage was leased concurrently.");
            }

            try
            {
                EnsureCapacity(0);
                Reset();
                return activeToken;
            }
            catch
            {
                Interlocked.CompareExchange(ref _leaseToken, unchecked(activeToken + 1L), activeToken);
                throw;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal bool IsLeaseActive(long leaseToken)
        {
            return leaseToken != 0L
                && (leaseToken & 1L) != 0L
                && Volatile.Read(ref _leaseToken) == leaseToken;
        }

        internal bool TryEndLease(long leaseToken)
        {
            if (leaseToken == 0L || (leaseToken & 1L) == 0L)
            {
                return false;
            }

            long availableToken = unchecked(leaseToken + 1L);
            return Interlocked.CompareExchange(ref _leaseToken, availableToken, leaseToken) == leaseToken;
        }

        internal void SetPosition(int value)
        {
            if (value < 0 || value > MaxCapacity)
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }

            if (_readMode)
            {
                if (value > _length)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(value),
                        value,
                        "Read position cannot move beyond the available payload.");
                }

                _position = value;
                return;
            }

            EnsureCapacity(value);
            if (value > _initializedLength)
            {
                // ArrayPool does not guarantee cleared storage. A forward seek reserves
                // bytes for the current lease, so initialize the newly exposed range
                // before ToArraySegment can publish it.
                _buffer.AsSpan(_initializedLength, value - _initializedLength).Clear();
                _initializedLength = value;
            }

            _position = value;
        }

        internal void SetBuffer(ArraySegment<byte> data)
        {
            if (data.Array == null)
            {
                throw new ArgumentException("ArraySegment must reference a valid array.", nameof(data));
            }

            if (data.Offset < 0
                || data.Count < 0
                || data.Offset > data.Array.Length
                || data.Count > data.Array.Length - data.Offset)
            {
                throw new ArgumentOutOfRangeException(nameof(data));
            }

            EnsureCapacity(data.Count);
            Buffer.BlockCopy(data.Array, data.Offset, _buffer, 0, data.Count);
            _length = data.Count;
            _initializedLength = data.Count;
            _position = 0;
            _readMode = true;
        }

        internal void SetBuffer(ReadOnlySpan<byte> data)
        {
            EnsureCapacity(data.Length);
            data.CopyTo(_buffer.AsSpan(0, data.Length));
            _length = data.Length;
            _initializedLength = data.Length;
            _position = 0;
            _readMode = true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void WriteByte(byte value)
        {
            EnsureWritable(1);
            _buffer[_position++] = value;
            MarkWritten();
        }

        internal void WriteBytes(ReadOnlySpan<byte> data)
        {
            EnsureWritable(data.Length);
            data.CopyTo(_buffer.AsSpan(_position, data.Length));
            _position += data.Length;
            MarkWritten();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void WriteUShort(ushort value)
        {
            EnsureWritable(2);
            _buffer[_position++] = (byte)value;
            _buffer[_position++] = (byte)(value >> 8);
            MarkWritten();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void WriteInt(int value)
        {
            EnsureWritable(4);
            _buffer[_position++] = (byte)value;
            _buffer[_position++] = (byte)(value >> 8);
            _buffer[_position++] = (byte)(value >> 16);
            _buffer[_position++] = (byte)(value >> 24);
            MarkWritten();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void WriteUInt(uint value)
        {
            EnsureWritable(4);
            _buffer[_position++] = (byte)value;
            _buffer[_position++] = (byte)(value >> 8);
            _buffer[_position++] = (byte)(value >> 16);
            _buffer[_position++] = (byte)(value >> 24);
            MarkWritten();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void WriteLong(long value)
        {
            WriteULong(unchecked((ulong)value));
        }

        internal void WriteULong(ulong value)
        {
            EnsureWritable(8);
            _buffer[_position++] = (byte)value;
            _buffer[_position++] = (byte)(value >> 8);
            _buffer[_position++] = (byte)(value >> 16);
            _buffer[_position++] = (byte)(value >> 24);
            _buffer[_position++] = (byte)(value >> 32);
            _buffer[_position++] = (byte)(value >> 40);
            _buffer[_position++] = (byte)(value >> 48);
            _buffer[_position++] = (byte)(value >> 56);
            MarkWritten();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void WriteFloat(float value)
        {
            WriteInt(BitConverter.SingleToInt32Bits(value));
        }

        internal ArraySegment<byte> ToArraySegment()
        {
            return new ArraySegment<byte>(_buffer, 0, _position);
        }

        internal void Reset()
        {
            _position = 0;
            _length = 0;
            _initializedLength = 0;
            _readMode = false;
        }

        internal void FlipForRead()
        {
            _length = _position;
            _position = 0;
            _readMode = true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal byte ReadByte()
        {
            if (_position >= _length)
            {
                throw new InvalidOperationException("Buffer underflow.");
            }

            return _buffer[_position++];
        }

        internal void ReadBytes(Span<byte> destination, int count)
        {
            if (count < 0 || count > destination.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(count));
            }

            EnsureReadable(count);
            _buffer.AsSpan(_position, count).CopyTo(destination);
            _position += count;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal ushort ReadUShort()
        {
            EnsureReadable(2);
            ushort value = (ushort)(_buffer[_position] | (_buffer[_position + 1] << 8));
            _position += 2;
            return value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal int ReadInt()
        {
            EnsureReadable(4);
            int value = _buffer[_position]
                | (_buffer[_position + 1] << 8)
                | (_buffer[_position + 2] << 16)
                | (_buffer[_position + 3] << 24);
            _position += 4;
            return value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal uint ReadUInt() => unchecked((uint)ReadInt());

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal long ReadLong() => unchecked((long)ReadULong());

        internal ulong ReadULong()
        {
            EnsureReadable(8);
            ulong value = (ulong)_buffer[_position]
                | ((ulong)_buffer[_position + 1] << 8)
                | ((ulong)_buffer[_position + 2] << 16)
                | ((ulong)_buffer[_position + 3] << 24)
                | ((ulong)_buffer[_position + 4] << 32)
                | ((ulong)_buffer[_position + 5] << 40)
                | ((ulong)_buffer[_position + 6] << 48)
                | ((ulong)_buffer[_position + 7] << 56);
            _position += 8;
            return value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal float ReadFloat()
        {
            return BitConverter.Int32BitsToSingle(ReadInt());
        }

        internal ReadOnlySpan<byte> ReadBytesSpan(int count)
        {
            if (count < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(count));
            }

            EnsureReadable(count);
            ReadOnlySpan<byte> span = _buffer.AsSpan(_position, count);
            _position += count;
            return span;
        }

        internal void PrepareForPooling(bool clearBuffer)
        {
            if (clearBuffer && _buffer != null)
            {
                Array.Clear(_buffer, 0, _buffer.Length);
            }

            Reset();
        }

        internal void ReleaseBuffer(bool clearBuffer)
        {
            byte[] buffer = _buffer;
            _buffer = null;
            Reset();
            if (buffer != null)
            {
                ArrayPool<byte>.Shared.Return(buffer, clearBuffer);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void EnsureWritable(int count)
        {
            if (_readMode)
            {
                throw new InvalidOperationException("Reset the buffer before writing after it enters read mode.");
            }

            if (count < 0 || _position > MaxCapacity - count)
            {
                throw new InvalidOperationException($"Buffer capacity exceeded maximum of {MaxCapacity}.");
            }

            EnsureCapacity(_position + count);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void EnsureReadable(int count)
        {
            if (count < 0 || _position > _length - count)
            {
                throw new InvalidOperationException("Buffer underflow.");
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void MarkWritten()
        {
            if (_position > _initializedLength)
            {
                _initializedLength = _position;
            }
        }

        private void EnsureCapacity(int required)
        {
            if (required < 0 || required > MaxCapacity)
            {
                throw new InvalidOperationException($"Buffer capacity exceeded maximum of {MaxCapacity}.");
            }

            if (_buffer == null)
            {
                _buffer = ArrayPool<byte>.Shared.Rent(Math.Max(required, DefaultCapacity));
            }

            if (_buffer.Length >= required)
            {
                return;
            }

            int doubledCapacity = _buffer.Length <= MaxCapacity / 2
                ? _buffer.Length * 2
                : MaxCapacity;
            int newCapacity = Math.Min(Math.Max(required, doubledCapacity), MaxCapacity);
            byte[] newBuffer = ArrayPool<byte>.Shared.Rent(newCapacity);
            int bytesToCopy = Math.Max(_initializedLength, Math.Max(_length, _position));
            if (bytesToCopy > 0)
            {
                Buffer.BlockCopy(_buffer, 0, newBuffer, 0, bytesToCopy);
            }

            ArrayPool<byte>.Shared.Return(_buffer, NetworkBufferPool.ClearBuffersOnReturn);
            _buffer = newBuffer;
        }
    }
}
