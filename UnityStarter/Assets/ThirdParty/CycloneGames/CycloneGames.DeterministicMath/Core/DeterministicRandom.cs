using System;
using System.Runtime.CompilerServices;

namespace CycloneGames.DeterministicMath
{
    public readonly struct DeterministicRandomState : IEquatable<DeterministicRandomState>
    {
        public readonly ulong S0;
        public readonly ulong S1;
        public readonly ulong S2;
        public readonly ulong S3;

        public DeterministicRandomState(ulong s0, ulong s1, ulong s2, ulong s3)
        {
            S0 = s0;
            S1 = s1;
            S2 = s2;
            S3 = s3;
        }

        public bool Equals(DeterministicRandomState other) =>
            S0 == other.S0 && S1 == other.S1 && S2 == other.S2 && S3 == other.S3;

        public override bool Equals(object obj) => obj is DeterministicRandomState state && Equals(state);
        public override int GetHashCode() => S0.GetHashCode() ^ S1.GetHashCode() ^ S2.GetHashCode() ^ S3.GetHashCode();
    }

    /// <summary>
    /// Deterministic seeded PRNG using xoshiro256** algorithm.
    /// Produces identical sequences on all platforms given the same seed.
    /// Supports state save/restore for rollback and replay.
    /// <para>
    /// This is a struct - zero heap allocation. Create via <see cref="Create"/>.
    /// </para>
    /// </summary>
    public struct DeterministicRandom
    {
        private ulong _s0, _s1, _s2, _s3;

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
            ulong result = RotateLeft(_s1 * 5, 7) * 9;
            ulong t = _s1 << 17;

            _s2 ^= _s0;
            _s3 ^= _s1;
            _s1 ^= _s2;
            _s0 ^= _s3;

            _s2 ^= t;
            _s3 = RotateLeft(_s3, 45);

            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int NextInt(int max)
        {
            if (max <= 0) return 0;
            ulong range = (ulong)max;
            ulong limit = ulong.MaxValue - (ulong.MaxValue % range);
            ulong value;

            do
            {
                value = NextULong();
            }
            while (value >= limit);

            return (int)(value % range);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int NextInt(int min, int max)
        {
            if (max <= min) return min;
            return min + NextInt(max - min);
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
            return min + NextFP() * (max - min);
        }

        public DeterministicRandomState SaveState() => new DeterministicRandomState(_s0, _s1, _s2, _s3);

        public void RestoreState(DeterministicRandomState state)
        {
            _s0 = state.S0;
            _s1 = state.S1;
            _s2 = state.S2;
            _s3 = state.S3;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ulong RotateLeft(ulong x, int k) => (x << k) | (x >> (64 - k));

        private static ulong SplitMix64(ref ulong state)
        {
            ulong z = (state += 0x9E3779B97F4A7C15UL);
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }
    }
}
