using System;
using System.Runtime.CompilerServices;

namespace CycloneGames.Hash.Core
{
    /// <summary>
    /// Deterministic FNV-1a 32-bit hash helpers for compact stable identifiers, network ids and
    /// desync-detection checksums that must stay 32-bit on the wire.
    /// This is a non-cryptographic hash and must not be used for tamper-proof security.
    /// Prefer <see cref="Fnv1a64"/> or <see cref="XxHash64"/> when a wider digest is acceptable; the
    /// 32-bit width exists for formats and ids that are constrained to 32 bits.
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
