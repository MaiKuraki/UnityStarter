using System;
using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace CycloneGames.Hash.Core
{
    /// <summary>
    /// Pure C# xxHash64 implementation for fast non-cryptographic content hashing.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct XxHash64
    {
        private const ulong PRIME64_1 = 0x9E3779B185EBCA87UL;
        private const ulong PRIME64_2 = 0xC2B2AE3D27D4EB4FUL;
        private const ulong PRIME64_3 = 0x165667B19E3779F9UL;
        private const ulong PRIME64_4 = 0x85EBCA77C2B2AE63UL;
        private const ulong PRIME64_5 = 0x27D4EB2F165667C5UL;

        private ulong _v1;
        private ulong _v2;
        private ulong _v3;
        private ulong _v4;
        private ulong _totalLength;
        private int _memorySize;
        private ulong _buf0;
        private ulong _buf1;
        private ulong _buf2;
        private ulong _buf3;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static XxHash64 Create(ulong seed = 0UL)
        {
            XxHash64 state = default;
            state._v1 = seed + PRIME64_1 + PRIME64_2;
            state._v2 = seed + PRIME64_2;
            state._v3 = seed;
            state._v4 = seed - PRIME64_1;
            return state;
        }

        public void Append(ReadOnlySpan<byte> data)
        {
            int length = data.Length;
            _totalLength += (ulong)length;

            int offset = 0;
            if (_memorySize > 0)
            {
                int fillLength = 32 - _memorySize;
                if (length < fillLength)
                {
                    data.CopyTo(BufferSpan.Slice(_memorySize));
                    _memorySize += length;
                    return;
                }

                data.Slice(0, fillLength).CopyTo(BufferSpan.Slice(_memorySize));
                ProcessStripe(BufferSpan);
                offset = fillLength;
                _memorySize = 0;
            }

            if (length - offset >= 32)
            {
                int limit = length - 32;
                do
                {
                    _v1 = Round(_v1, BinaryPrimitives.ReadUInt64LittleEndian(data.Slice(offset)));
                    _v2 = Round(_v2, BinaryPrimitives.ReadUInt64LittleEndian(data.Slice(offset + 8)));
                    _v3 = Round(_v3, BinaryPrimitives.ReadUInt64LittleEndian(data.Slice(offset + 16)));
                    _v4 = Round(_v4, BinaryPrimitives.ReadUInt64LittleEndian(data.Slice(offset + 24)));
                    offset += 32;
                }
                while (offset <= limit);
            }

            if (offset < length)
            {
                data.Slice(offset).CopyTo(BufferSpan);
                _memorySize = length - offset;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Append(byte[] data, int offset, int count)
        {
            Append(new ReadOnlySpan<byte>(data, offset, count));
        }

        public ulong GetDigest()
        {
            ulong h64;

            if (_totalLength >= 32)
            {
                h64 = RotateLeft(_v1, 1) + RotateLeft(_v2, 7) + RotateLeft(_v3, 12) + RotateLeft(_v4, 18);
                h64 = MergeRound(h64, _v1);
                h64 = MergeRound(h64, _v2);
                h64 = MergeRound(h64, _v3);
                h64 = MergeRound(h64, _v4);
            }
            else
            {
                h64 = _v3 + PRIME64_5;
            }

            h64 += _totalLength;

            ReadOnlySpan<byte> remaining = BufferSpan.Slice(0, _memorySize);
            int offset = 0;

            while (offset + 8 <= _memorySize)
            {
                h64 ^= Round(0UL, BinaryPrimitives.ReadUInt64LittleEndian(remaining.Slice(offset)));
                h64 = RotateLeft(h64, 27) * PRIME64_1 + PRIME64_4;
                offset += 8;
            }

            if (offset + 4 <= _memorySize)
            {
                h64 ^= (ulong)BinaryPrimitives.ReadUInt32LittleEndian(remaining.Slice(offset)) * PRIME64_1;
                h64 = RotateLeft(h64, 23) * PRIME64_2 + PRIME64_3;
                offset += 4;
            }

            while (offset < _memorySize)
            {
                h64 ^= remaining[offset] * PRIME64_5;
                h64 = RotateLeft(h64, 11) * PRIME64_1;
                offset++;
            }

            h64 ^= h64 >> 33;
            h64 *= PRIME64_2;
            h64 ^= h64 >> 29;
            h64 *= PRIME64_3;
            h64 ^= h64 >> 32;

            return h64;
        }

        public bool TryWriteHash(Span<byte> destination)
        {
            if (destination.Length < 8)
            {
                return false;
            }

            BinaryPrimitives.WriteUInt64BigEndian(destination, GetDigest());
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ulong Compute(ReadOnlySpan<byte> data, ulong seed = 0UL)
        {
            XxHash64 state = Create(seed);
            state.Append(data);
            return state.GetDigest();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ulong HashToUInt64(ReadOnlySpan<byte> data, ulong seed = 0UL)
        {
            return Compute(data, seed);
        }

        private Span<byte> BufferSpan
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => MemoryMarshal.AsBytes(MemoryMarshal.CreateSpan(ref _buf0, 4));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ProcessStripe(ReadOnlySpan<byte> stripe)
        {
            _v1 = Round(_v1, BinaryPrimitives.ReadUInt64LittleEndian(stripe));
            _v2 = Round(_v2, BinaryPrimitives.ReadUInt64LittleEndian(stripe.Slice(8)));
            _v3 = Round(_v3, BinaryPrimitives.ReadUInt64LittleEndian(stripe.Slice(16)));
            _v4 = Round(_v4, BinaryPrimitives.ReadUInt64LittleEndian(stripe.Slice(24)));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ulong Round(ulong accumulator, ulong input)
        {
            accumulator += input * PRIME64_2;
            accumulator = RotateLeft(accumulator, 31);
            accumulator *= PRIME64_1;
            return accumulator;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ulong MergeRound(ulong accumulator, ulong value)
        {
            accumulator ^= Round(0UL, value);
            accumulator = accumulator * PRIME64_1 + PRIME64_4;
            return accumulator;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ulong RotateLeft(ulong value, int count)
        {
            return (value << count) | (value >> (64 - count));
        }
    }
}
