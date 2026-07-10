using System;
using System.Runtime.CompilerServices;

namespace CycloneGames.Hash.Core
{
    /// <summary>
    /// Deterministic FNV-1a 32-bit helpers for byte-oriented contracts and legacy UTF-16 ordinal IDs.
    /// This is a non-cryptographic hash and must not be used for tamper-proof security. The 32-bit
    /// width has a material collision risk even for thousands of distinct keys, so persistent ID
    /// registries must detect collisions instead of assuming uniqueness.
    /// </summary>
    public static class Fnv1a32
    {
        public const uint OffsetBasis = 2166136261u;
        public const uint Prime = 16777619u;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint Compute(ReadOnlySpan<byte> data)
        {
            return Compute(data, OffsetBasis);
        }

        /// <summary>
        /// Continues a byte-wise FNV-1a computation from <paramref name="seed"/>. The seed is the
        /// current FNV state, not an independently mixed random seed.
        /// </summary>
        public static uint Compute(ReadOnlySpan<byte> data, uint seed)
        {
            unchecked
            {
                uint hash = seed;
                for (int i = 0; i < data.Length; i++)
                {
                    hash ^= data[i];
                    hash *= Prime;
                }

                return hash;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint ComputeUtf16Ordinal(ReadOnlySpan<char> text)
        {
            return ComputeUtf16Ordinal(text, OffsetBasis);
        }

        /// <summary>
        /// Continues the legacy ordinal string contract by folding each UTF-16 code unit once.
        /// This is intentionally not the FNV-1a hash of a UTF-16LE or UTF-8 byte encoding.
        /// </summary>
        public static uint ComputeUtf16Ordinal(ReadOnlySpan<char> text, uint seed)
        {
            unchecked
            {
                uint hash = seed;
                for (int i = 0; i < text.Length; i++)
                {
                    hash ^= text[i];
                    hash *= Prime;
                }

                return hash;
            }
        }

        /// <summary>
        /// Folds the four bytes of <paramref name="value"/> into the hash in little-endian order
        /// (low byte first), equivalent to a byte-wise FNV-1a over the value's little-endian encoding.
        /// </summary>
        public static uint CombineUInt32LittleEndian(uint hash, uint value)
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
