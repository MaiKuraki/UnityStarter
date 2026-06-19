using System;
using System.Runtime.CompilerServices;

namespace CycloneGames.Hash.Core
{
    /// <summary>
    /// Deterministic FNV-1a 64-bit hash helpers for stable identifiers and manifests.
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

        public static ulong CombineUInt64LittleEndian(ulong hash, ulong value)
        {
            unchecked
            {
                for (int i = 0; i < 8; i++)
                {
                    hash ^= (byte)(value >> (i * 8));
                    hash *= Prime;
                }

                return hash;
            }
        }
    }
}
