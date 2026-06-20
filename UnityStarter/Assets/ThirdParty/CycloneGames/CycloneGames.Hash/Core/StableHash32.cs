using System;
using System.Runtime.CompilerServices;

namespace CycloneGames.Hash.Core
{
    /// <summary>
    /// Stable non-zero 32-bit hash helpers for deterministic ids, manifests and protocol-compatibility checks.
    /// Guarantees a non-zero digest so callers can use 0 as an "unset" sentinel. 32-bit width suits identifier
    /// spaces with up to a few hundred thousand distinct keys; use <see cref="StableHash64"/> for large or
    /// security-adjacent spaces.
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

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint EnsureNonZero(uint value)
        {
            return value == 0u ? NonZeroFallback : value;
        }
    }
}
