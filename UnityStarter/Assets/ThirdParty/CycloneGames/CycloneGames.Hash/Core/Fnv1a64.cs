using System;
using System.Runtime.CompilerServices;

namespace CycloneGames.Hash.Core
{
    /// <summary>
    /// Deterministic FNV-1a 64-bit helpers for byte-oriented contracts and legacy UTF-16 ordinal IDs.
    /// This is a non-cryptographic hash and must not be used for tamper-proof security.
    /// </summary>
    public static class Fnv1a64
    {
        public const ulong OffsetBasis = 14695981039346656037UL;
        public const ulong Prime = 1099511628211UL;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ulong Compute(ReadOnlySpan<byte> data)
        {
            return Compute(data, OffsetBasis);
        }

        /// <summary>
        /// Continues a byte-wise FNV-1a computation from <paramref name="seed"/>. The seed is the
        /// current FNV state, not an independently mixed random seed.
        /// </summary>
        public static ulong Compute(ReadOnlySpan<byte> data, ulong seed)
        {
            unchecked
            {
                ulong hash = seed;
                for (int i = 0; i < data.Length; i++)
                {
                    hash ^= data[i];
                    hash *= Prime;
                }

                return hash;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ulong ComputeUtf16Ordinal(ReadOnlySpan<char> text)
        {
            return ComputeUtf16Ordinal(text, OffsetBasis);
        }

        /// <summary>
        /// Continues the legacy ordinal string contract by folding each UTF-16 code unit once.
        /// This is intentionally not the FNV-1a hash of a UTF-16LE or UTF-8 byte encoding.
        /// </summary>
        public static ulong ComputeUtf16Ordinal(ReadOnlySpan<char> text, ulong seed)
        {
            unchecked
            {
                ulong hash = seed;
                for (int i = 0; i < text.Length; i++)
                {
                    hash ^= text[i];
                    hash *= Prime;
                }

                return hash;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ulong CombineUInt64LittleEndian(ulong hash, ulong value)
        {
            unchecked
            {
                hash = CombineUInt32LittleEndian(hash, (uint)value);
                return CombineUInt32LittleEndian(hash, (uint)(value >> 32));
            }
        }

        /// <summary>
        /// Folds the four bytes of a 32-bit <paramref name="value"/> into the 64-bit hash in little-endian
        /// order (low byte first). Use to accumulate 32-bit fields into a 64-bit checksum without zero padding.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ulong CombineUInt32LittleEndian(ulong hash, uint value)
        {
            unchecked
            {
                hash ^= value & 0xFFu;
                hash *= Prime;
                hash ^= (value >> 8) & 0xFFu;
                hash *= Prime;
                hash ^= (value >> 16) & 0xFFu;
                hash *= Prime;
                hash ^= (value >> 24) & 0xFFu;
                hash *= Prime;
                return hash;
            }
        }
    }
}
