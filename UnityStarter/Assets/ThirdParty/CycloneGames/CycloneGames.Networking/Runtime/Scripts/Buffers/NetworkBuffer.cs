using System;
using System.Buffers;
using System.Runtime.CompilerServices;
using CycloneGames.Networking.Serialization;

namespace CycloneGames.Networking.Buffers
{
    /// <summary>
    /// Pooled buffer implementing both INetWriter and INetReader for zero-allocation network I/O.
    /// Acquire via NetworkBufferPool.Get() and return via Dispose() or NetworkBufferPool.Return().
    /// </summary>
    public sealed class NetworkBuffer : INetWriter, INetReader, IDisposable
    {
        private const int DefaultCapacity = 1500; // MTU-sized default
        private const int MaxCapacity = 65535;

        private byte[] _buffer;
        private int _position;
        private int _length; // For reading: how much data is valid

        public int Position
        {
            get => _position;
            set => _position = value;
        }

        public int Capacity => _buffer?.Length ?? 0;
        public int Remaining => _length - _position;

        internal NetworkBuffer()
        {
            _buffer = ArrayPool<byte>.Shared.Rent(DefaultCapacity);
        }

        internal void SetBuffer(ArraySegment<byte> data)
        {
            EnsureCapacity(data.Count);
            Buffer.BlockCopy(data.Array!, data.Offset, _buffer, 0, data.Count);
            _length = data.Count;
            _position = 0;
        }

        // INetWriter Implementation

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteByte(byte value)
        {
            EnsureCapacity(_position + 1);
            _buffer[_position++] = value;
        }

        public void WriteBytes(ReadOnlySpan<byte> data)
        {
            EnsureCapacity(_position + data.Length);
            data.CopyTo(_buffer.AsSpan(_position));
            _position += data.Length;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteUShort(ushort value)
        {
            EnsureCapacity(_position + 2);
            _buffer[_position++] = (byte)value;
            _buffer[_position++] = (byte)(value >> 8);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteInt(int value)
        {
            EnsureCapacity(_position + 4);
            _buffer[_position++] = (byte)value;
            _buffer[_position++] = (byte)(value >> 8);
            _buffer[_position++] = (byte)(value >> 16);
            _buffer[_position++] = (byte)(value >> 24);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteUInt(uint value)
        {
            EnsureCapacity(_position + 4);
            _buffer[_position++] = (byte)value;
            _buffer[_position++] = (byte)(value >> 8);
            _buffer[_position++] = (byte)(value >> 16);
            _buffer[_position++] = (byte)(value >> 24);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe void WriteFloat(float value)
        {
            WriteUInt(*(uint*)&value);
        }

        public unsafe void WriteBlittable<T>(in T value) where T : unmanaged
        {
            int size = sizeof(T);
            EnsureCapacity(_position + size);

            fixed (byte* ptr = &_buffer[_position])
            {
                // Use memcpy for ARM alignment safety
                T* valuePtr = stackalloc T[1];
                valuePtr[0] = value;
                Buffer.MemoryCopy(valuePtr, ptr, size, size);
            }
            _position += size;
        }

        public ArraySegment<byte> ToArraySegment()
        {
            return new ArraySegment<byte>(_buffer, 0, _position);
        }

        public void Reset()
        {
            _position = 0;
            _length = 0;
        }

        // INetReader Implementation

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public byte ReadByte()
        {
            if (_position >= _length)
                throw new InvalidOperationException("Buffer underflow");
            return _buffer[_position++];
        }

        public void ReadBytes(Span<byte> destination, int count)
        {
            if (_position + count > _length)
                throw new InvalidOperationException("Buffer underflow");
            _buffer.AsSpan(_position, count).CopyTo(destination);
            _position += count;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ushort ReadUShort()
        {
            if (_position + 2 > _length)
                throw new InvalidOperationException("Buffer underflow");
            ushort value = (ushort)(_buffer[_position] | (_buffer[_position + 1] << 8));
            _position += 2;
            return value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int ReadInt()
        {
            if (_position + 4 > _length)
                throw new InvalidOperationException("Buffer underflow");
            int value = _buffer[_position]
                      | (_buffer[_position + 1] << 8)
                      | (_buffer[_position + 2] << 16)
                      | (_buffer[_position + 3] << 24);
            _position += 4;
            return value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public uint ReadUInt()
        {
            return (uint)ReadInt();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe float ReadFloat()
        {
            uint intValue = ReadUInt();
            return *(float*)&intValue;
        }

        public unsafe T ReadBlittable<T>() where T : unmanaged
        {
            int size = sizeof(T);
            if (_position + size > _length)
                throw new InvalidOperationException("Buffer underflow");

            T result;
            fixed (byte* ptr = &_buffer[_position])
            {
                // Use memcpy for ARM alignment safety
                T* resultPtr = stackalloc T[1];
                Buffer.MemoryCopy(ptr, resultPtr, size, size);
                result = resultPtr[0];
            }
            _position += size;
            return result;
        }

        public ReadOnlySpan<byte> ReadBytesSpan(int count)
        {
            if (_position + count > _length)
                throw new InvalidOperationException("Buffer underflow");
            var span = _buffer.AsSpan(_position, count);
            _position += count;
            return span;
        }

        // Buffer Management

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void EnsureCapacity(int required)
        {
            if (_buffer.Length >= required) return;

            int newCapacity = Math.Max(required, _buffer.Length * 2);
            if (newCapacity > MaxCapacity) newCapacity = MaxCapacity;
            if (required > MaxCapacity)
                throw new InvalidOperationException($"Buffer capacity exceeded maximum of {MaxCapacity}");

            byte[] newBuffer = ArrayPool<byte>.Shared.Rent(newCapacity);
            Buffer.BlockCopy(_buffer, 0, newBuffer, 0, _position);
            ArrayPool<byte>.Shared.Return(_buffer);
            _buffer = newBuffer;
        }

        public void Dispose()
        {
            NetworkBufferPool.Return(this);
        }

        internal void ReturnToPool()
        {
            Reset();
        }

        internal void ReleaseBuffer()
        {
            if (_buffer != null)
            {
                ArrayPool<byte>.Shared.Return(_buffer);
                _buffer = null!;
            }
        }
    }
}
