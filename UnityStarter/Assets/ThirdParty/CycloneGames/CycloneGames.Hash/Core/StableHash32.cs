using System;
using System.Runtime.CompilerServices;

namespace CycloneGames.Hash.Core
{
    /// <summary>
    /// Stable non-zero 32-bit hash helpers for deterministic ids, manifests and protocol-compatibility checks.
    /// Maps a zero digest to <see cref="NonZeroFallback"/> so callers can reserve 0 as an unset sentinel.
    /// This mapping introduces an additional collision with the fallback value and does not make hashes unique.
    /// Use only for explicitly 32-bit contracts with collision detection; prefer <see cref="StableHash64"/>
    /// when the format permits a wider value.
    /// </summary>
    public static class StableHash32
    {
        public const uint NonZeroFallback = Fnv1a32.OffsetBasis;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint ComputeBytes(ReadOnlySpan<byte> data)
        {
            return EnsureNonZero(Fnv1a32.Compute(data));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint ComputeBytes(ReadOnlySpan<byte> data, uint seed)
        {
            return EnsureNonZero(Fnv1a32.Compute(data, seed));
        }

        public static uint ComputeUtf16Ordinal(string text)
        {
            if (text == null)
            {
                throw new ArgumentNullException(nameof(text));
            }

            return ComputeUtf16Ordinal(text.AsSpan());
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint ComputeUtf16Ordinal(ReadOnlySpan<char> text)
        {
            return EnsureNonZero(Fnv1a32.ComputeUtf16Ordinal(text));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint CombineUInt32LittleEndian(uint hash, uint value)
        {
            return Fnv1a32.CombineUInt32LittleEndian(hash, value);
        }

        /// <summary>
        /// Maps 0 to <see cref="NonZeroFallback"/>. Call this on a final digest, not every intermediate state.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint EnsureNonZero(uint value)
        {
            return value == 0u ? NonZeroFallback : value;
        }
    }
}
