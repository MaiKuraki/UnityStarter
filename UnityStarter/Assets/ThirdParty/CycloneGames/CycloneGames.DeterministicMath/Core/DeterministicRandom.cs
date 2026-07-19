using System;
using System.Runtime.CompilerServices;

namespace CycloneGames.DeterministicMath
{
    /// <summary>
    /// Deterministic seeded PRNG using xoshiro256** algorithm.
    /// Supports state save/restore for rollback and replay.
    /// <para>
    /// The state is held inline in this value type. Create an initialized instance via <see cref="Create"/>.
    /// </para>
    /// </summary>
    public struct DeterministicRandom
    {
        public const int ALGORITHM_VERSION = 1;
        public const string ALGORITHM_ID = "xoshiro256**";

        private ulong _s0, _s1, _s2, _s3;

        public bool IsInitialized => (_s0 | _s1 | _s2 | _s3) != 0;

        /// <summary>Create a new PRNG from a seed.</summary>
        public static DeterministicRandom Create(ulong seed)
        {
            var rng = default(DeterministicRandom);
            rng._s0 = SplitMix64(ref seed);
            rng._s1 = SplitMix64(ref seed);
            rng._s2 = SplitMix64(ref seed);
            rng._s3 = SplitMix64(ref seed);
            return rng;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ulong NextULong()
        {
            if (!IsInitialized)
            {
                throw new InvalidOperationException(
                    "The random generator is not initialized. Create it with DeterministicRandom.Create or restore a valid state.");
            }

            unchecked
            {
                ulong result = RotateLeft(_s1 * 5UL, 7) * 9UL;
                ulong t = _s1 << 17;

                _s2 ^= _s0;
                _s3 ^= _s1;
                _s1 ^= _s2;
                _s0 ^= _s3;

                _s2 ^= t;
                _s3 = RotateLeft(_s3, 45);

                return result;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int NextInt(int max)
        {
            if (max <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(max), max, "Maximum must be greater than zero.");
            }

            return (int)NextULongBounded((ulong)max);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryNextInt(int max, out int result)
        {
            if (!IsInitialized || max <= 0)
            {
                result = default;
                return false;
            }

            result = (int)NextULongBounded((ulong)max);
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int NextInt(int min, int max)
        {
            if (max <= min)
            {
                throw new ArgumentOutOfRangeException(nameof(max), max, "Maximum must be greater than minimum.");
            }

            ulong range = (ulong)((long)max - min);
            long offset = (long)NextULongBounded(range);
            return (int)(min + offset);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryNextInt(int min, int max, out int result)
        {
            if (!IsInitialized || max <= min)
            {
                result = default;
                return false;
            }

            ulong range = (ulong)((long)max - min);
            long offset = (long)NextULongBounded(range);
            result = (int)(min + offset);
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public FPInt64 NextFP()
        {
            long raw = (long)(NextULong() >> 32);
            return FPInt64.FromRaw(raw);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public FPInt64 NextFP(FPInt64 min, FPInt64 max)
        {
            if (!IsInitialized)
            {
                throw new InvalidOperationException(
                    "The random generator is not initialized. Create it before sampling.");
            }

            if (!TryNextFP(min, max, out FPInt64 result))
            {
                throw new ArgumentOutOfRangeException(nameof(max), "Maximum must be greater than minimum.");
            }

            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryNextFP(FPInt64 min, FPInt64 max, out FPInt64 result)
        {
            if (!IsInitialized || max.RawValue <= min.RawValue)
            {
                result = default;
                return false;
            }

            ulong fraction = NextULong() >> 32;
            ulong range = unchecked((ulong)max.RawValue - (ulong)min.RawValue);
            ulong rangeHigh = range >> 32;
            ulong rangeLow = unchecked((uint)range);
            ulong offset = unchecked(fraction * rangeHigh + ((fraction * rangeLow) >> 32));
            ulong raw = unchecked((ulong)min.RawValue + offset);
            result = FPInt64.FromRaw(unchecked((long)raw));
            return true;
        }

        public DeterministicRandomState SaveState()
        {
            if (!IsInitialized)
            {
                throw new InvalidOperationException(
                    "The random generator is not initialized. Create it before saving state.");
            }

            return new DeterministicRandomState(_s0, _s1, _s2, _s3);
        }

        public void RestoreState(DeterministicRandomState state)
        {
            if (!TryRestoreState(state))
            {
                throw new ArgumentException("The all-zero state is invalid for xoshiro256**.", nameof(state));
            }
        }

        public bool TryRestoreState(DeterministicRandomState state)
        {
            if (!state.IsValid)
            {
                return false;
            }

            _s0 = state.S0;
            _s1 = state.S1;
            _s2 = state.S2;
            _s3 = state.S3;
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ulong RotateLeft(ulong x, int k) => (x << k) | (x >> (64 - k));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private ulong NextULongBounded(ulong range)
        {
            ulong threshold = unchecked(0UL - range) % range;
            ulong value;

            do
            {
                value = NextULong();
            }
            while (value < threshold);

            return value % range;
        }

        private static ulong SplitMix64(ref ulong state)
        {
            unchecked
            {
                ulong z = (state += 0x9E3779B97F4A7C15UL);
                z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
                z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
                return z ^ (z >> 31);
            }
        }
    }
}
