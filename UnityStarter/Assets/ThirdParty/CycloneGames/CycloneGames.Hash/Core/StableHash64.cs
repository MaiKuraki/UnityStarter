using System;
using System.Runtime.CompilerServices;

namespace CycloneGames.Hash.Core
{
    /// <summary>
    /// Stable non-zero 64-bit hash helpers for deterministic ids, manifests, and protocol compatibility checks.
    /// Maps a zero digest to <see cref="NonZeroFallback"/> so callers can reserve 0 as an unset sentinel.
    /// This mapping does not provide uniqueness or collision detection.
    /// </summary>
    public static class StableHash64
    {
        public const ulong NonZeroFallback = Fnv1a64.OffsetBasis;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ulong ComputeBytes(ReadOnlySpan<byte> data)
        {
            return EnsureNonZero(Fnv1a64.Compute(data));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ulong ComputeBytes(ReadOnlySpan<byte> data, ulong seed)
        {
            return EnsureNonZero(Fnv1a64.Compute(data, seed));
        }

        public static ulong ComputeUtf16Ordinal(string text)
        {
            if (text == null)
            {
                throw new ArgumentNullException(nameof(text));
            }

            return ComputeUtf16Ordinal(text.AsSpan());
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ulong ComputeUtf16Ordinal(ReadOnlySpan<char> text)
        {
            return EnsureNonZero(Fnv1a64.ComputeUtf16Ordinal(text));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ulong CombineUInt64LittleEndian(ulong hash, ulong value)
        {
            return Fnv1a64.CombineUInt64LittleEndian(hash, value);
        }

        /// <summary>
        /// Maps 0 to <see cref="NonZeroFallback"/>. Call this on a final digest, not every intermediate state.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ulong EnsureNonZero(ulong value)
        {
            return value == 0UL ? NonZeroFallback : value;
        }
    }
}
