using System;
using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace CycloneGames.Utility.Runtime
{
    /// <summary>
    /// Pure C# implementation of xxHash64, a non-cryptographic hash algorithm.
    ///
    /// <para>
    /// <b>Key Properties:</b>
    /// <list type="bullet">
    ///   <item>Struct-based: zero heap allocation when used on the stack.</item>
    ///   <item>Supports both one-shot and incremental (streaming) hash computation.</item>
    ///   <item>Produces deterministic 64-bit hashes identical across all platforms.</item>
    ///   <item>Thread-safe by value semantics: each copy is independent.</item>
    /// </list>
    /// </para>
    ///
    /// <para>
    /// <b>When to use:</b> Local file change detection, content deduplication, hash tables,
    /// build pipeline incremental checks — any scenario where cryptographic strength is unnecessary.
    /// For tamper-proof integrity verification (hot update, CDN downloads), use SHA256 instead.
    /// </para>
    ///
    /// <para>
    /// Algorithm: xxHash64 by Yann Collet.
    /// Specification: https://github.com/Cyan4973/xxHash/blob/dev/doc/xxhash_spec.md
    /// Original C implementation (BSD 2-Clause): https://github.com/Cyan4973/xxHash
    /// </para>
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct XxHash64
    {
        // xxHash64 prime constants (from official specification)
        private const ulong PRIME64_1 = 0x9E3779B185EBCA87UL;
        private const ulong PRIME64_2 = 0xC2B2AE3D27D4EB4FUL;
        private const ulong PRIME64_3 = 0x165667B19E3779F9UL;
        private const ulong PRIME64_4 = 0x85EBCA77C2B2AE63UL;
        private const ulong PRIME64_5 = 0x27D4EB2F165667C5UL;

        // Official test vector (seed = 0):
        //   XXH64("") = 0xEF46DB3751D8E999

        private ulong _v1, _v2, _v3, _v4;      // Accumulators (32 bytes)
        private ulong _totalLength;              // Total bytes consumed
        private int _memorySize;                 // Bytes buffered (0–31)

        // 32-byte inline buffer for partial stripes (4 × ulong).
        // [StructLayout(LayoutKind.Sequential)] guarantees these are contiguous in memory.
        private ulong _buf0, _buf1, _buf2, _buf3;

        /// <summary>
        /// Creates a new XxHash64 state initialized with the given seed.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static XxHash64 Create(ulong seed = 0)
        {
            var state = default(XxHash64);
            state._v1 = seed + PRIME64_1 + PRIME64_2;
            state._v2 = seed + PRIME64_2;
            state._v3 = seed;
            state._v4 = seed - PRIME64_1;
            return state;
        }

        /// <summary>
        /// Appends data to the ongoing hash computation.
        /// Can be called multiple times for incremental (streaming) hashing.
        /// </summary>
        public void Append(ReadOnlySpan<byte> data)
        {
            int length = data.Length;
            _totalLength += (ulong)length;

            int offset = 0;

            // If we have buffered data, try to fill a complete 32-byte stripe
            if (_memorySize > 0)
            {
                int fillLen = 32 - _memorySize;
                if (length < fillLen)
                {
                    data.CopyTo(BufferSpan.Slice(_memorySize));
                    _memorySize += length;
                    return;
                }

                data.Slice(0, fillLen).CopyTo(BufferSpan.Slice(_memorySize));
                ProcessStripe(BufferSpan);
                offset = fillLen;
                _memorySize = 0;
            }

            // Process full 32-byte stripes directly from input
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
                } while (offset <= limit);
            }

            // Buffer remaining bytes
            if (offset < length)
            {
                data.Slice(offset).CopyTo(BufferSpan);
                _memorySize = length - offset;
            }
        }

        /// <summary>
        /// Appends data from a byte array. Convenience overload to avoid Span in async callers.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Append(byte[] data, int offset, int count)
        {
            Append(new ReadOnlySpan<byte>(data, offset, count));
        }

        /// <summary>
        /// Computes and returns the final 64-bit hash value.
        /// Does not modify state; can be called multiple times safely.
        /// </summary>
        public ulong GetDigest()
        {
            ulong h64;

            if (_totalLength >= 32)
            {
                // Merge accumulators
                h64 = RotateLeft(_v1, 1) + RotateLeft(_v2, 7) +
                      RotateLeft(_v3, 12) + RotateLeft(_v4, 18);
                h64 = MergeRound(h64, _v1);
                h64 = MergeRound(h64, _v2);
                h64 = MergeRound(h64, _v3);
                h64 = MergeRound(h64, _v4);
            }
            else
            {
                // Total input was less than 32 bytes
                h64 = _v3 + PRIME64_5; // _v3 == seed when no full stripe was processed
            }

            h64 += _totalLength;

            // Process remaining bytes in the buffer
            ReadOnlySpan<byte> remaining = BufferSpan.Slice(0, _memorySize);
            int off = 0;

            // 8-byte chunks
            while (off + 8 <= _memorySize)
            {
                h64 ^= Round(0, BinaryPrimitives.ReadUInt64LittleEndian(remaining.Slice(off)));
                h64 = RotateLeft(h64, 27) * PRIME64_1 + PRIME64_4;
                off += 8;
            }

            // 4-byte chunk
            if (off + 4 <= _memorySize)
            {
                h64 ^= (ulong)BinaryPrimitives.ReadUInt32LittleEndian(remaining.Slice(off)) * PRIME64_1;
                h64 = RotateLeft(h64, 23) * PRIME64_2 + PRIME64_3;
                off += 4;
            }

            // Remaining individual bytes
            while (off < _memorySize)
            {
                h64 ^= remaining[off] * PRIME64_5;
                h64 = RotateLeft(h64, 11) * PRIME64_1;
                off++;
            }

            // Final avalanche
            h64 ^= h64 >> 33;
            h64 *= PRIME64_2;
            h64 ^= h64 >> 29;
            h64 *= PRIME64_3;
            h64 ^= h64 >> 32;

            return h64;
        }

        /// <summary>
        /// Writes the 8-byte hash into the destination buffer (big-endian, matching conventional display order).
        /// Returns false if the buffer is too small.
        /// </summary>
        public bool TryWriteHash(Span<byte> destination)
        {
            if (destination.Length < 8) return false;
            BinaryPrimitives.WriteUInt64BigEndian(destination, GetDigest());
            return true;
        }

        /// <summary>
        /// Computes the xxHash64 of the given data in a single call. Zero heap allocation.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ulong HashToUInt64(ReadOnlySpan<byte> data, ulong seed = 0)
        {
            var state = Create(seed);
            state.Append(data);
            return state.GetDigest();
        }

        // --- Internal helpers ---

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
        private static ulong Round(ulong acc, ulong input)
        {
            acc += input * PRIME64_2;
            acc = RotateLeft(acc, 31);
            acc *= PRIME64_1;
            return acc;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ulong MergeRound(ulong acc, ulong val)
        {
            acc ^= Round(0, val);
            acc = acc * PRIME64_1 + PRIME64_4;
            return acc;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ulong RotateLeft(ulong value, int count)
        {
            return (value << count) | (value >> (64 - count));
        }
    }
}
