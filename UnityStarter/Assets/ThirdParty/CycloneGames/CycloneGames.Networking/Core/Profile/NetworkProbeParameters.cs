using System;
using CycloneGames.Hash.Core;

namespace CycloneGames.Networking
{
    /// <summary>
    /// Immutable probe configuration. A score is comparable to a threshold
    /// table only when the exact parameters that produced it travel with it.
    /// </summary>
    public readonly struct NetworkProbeParameters : IEquatable<NetworkProbeParameters>
    {
        public readonly int Rounds;
        public readonly int RoundBudgetMilliseconds;
        public readonly int WarmupMilliseconds;
        public readonly int MinIterationsPerWindow;
        public readonly int SmallPayloadSize;
        public readonly int LargePayloadSize;

        public NetworkProbeParameters(
            int rounds,
            int roundBudgetMilliseconds,
            int warmupMilliseconds,
            int minIterationsPerWindow,
            int smallPayloadSize,
            int largePayloadSize)
        {
            if (rounds <= 0) throw new ArgumentOutOfRangeException(nameof(rounds));
            if (roundBudgetMilliseconds <= 0) throw new ArgumentOutOfRangeException(nameof(roundBudgetMilliseconds));
            if (warmupMilliseconds < 0) throw new ArgumentOutOfRangeException(nameof(warmupMilliseconds));
            if (minIterationsPerWindow <= 0) throw new ArgumentOutOfRangeException(nameof(minIterationsPerWindow));
            if (smallPayloadSize <= 0) throw new ArgumentOutOfRangeException(nameof(smallPayloadSize));
            if (largePayloadSize <= smallPayloadSize) throw new ArgumentOutOfRangeException(nameof(largePayloadSize));

            Rounds = rounds;
            RoundBudgetMilliseconds = roundBudgetMilliseconds;
            WarmupMilliseconds = warmupMilliseconds;
            MinIterationsPerWindow = minIterationsPerWindow;
            SmallPayloadSize = smallPayloadSize;
            LargePayloadSize = largePayloadSize;
        }

        /// <summary>
        /// Calibrated default. A score classified with a threshold table must use
        /// exactly these parameters; see NetworkTierThresholds.ExpectedProbeParametersId.
        /// </summary>
        public static NetworkProbeParameters Standard =>
            new NetworkProbeParameters(5, 8, 3, 64, NetworkConstants.DefaultMTU, 64 * 1024);

        /// <summary>
        /// Stable identity of this parameter set, computed with FNV-1a64. No reflection,
        /// no string formatting, no allocation.
        /// </summary>
        public ulong ParametersId
        {
            get
            {
                ulong hash = Fnv1a64.OffsetBasis;
                hash = Fold(hash, (ulong)(uint)Rounds);
                hash = Fold(hash, (ulong)(uint)RoundBudgetMilliseconds);
                hash = Fold(hash, (ulong)(uint)WarmupMilliseconds);
                hash = Fold(hash, (ulong)(uint)MinIterationsPerWindow);
                hash = Fold(hash, (ulong)(uint)SmallPayloadSize);
                hash = Fold(hash, (ulong)(uint)LargePayloadSize);
                return hash == 0UL ? Fnv1a64.OffsetBasis : hash;
            }
        }

        private static ulong Fold(ulong hash, ulong value)
        {
            unchecked
            {
                for (int i = 0; i < 8; i++)
                {
                    hash ^= (byte)(value >> (i * 8));
                    hash *= Fnv1a64.Prime;
                }
                return hash;
            }
        }

        public bool Equals(NetworkProbeParameters other)
        {
            return Rounds == other.Rounds
                   && RoundBudgetMilliseconds == other.RoundBudgetMilliseconds
                   && WarmupMilliseconds == other.WarmupMilliseconds
                   && MinIterationsPerWindow == other.MinIterationsPerWindow
                   && SmallPayloadSize == other.SmallPayloadSize
                   && LargePayloadSize == other.LargePayloadSize;
        }

        public override bool Equals(object obj) => obj is NetworkProbeParameters other && Equals(other);

        public override int GetHashCode() => ParametersId.GetHashCode();
    }

    /// <summary>
    /// A measured throughput result carrying the identity of the parameters that
    /// produced it. Classification must reject a score whose ParametersId does not
    /// match the threshold table, otherwise the same device can be assigned
    /// different tiers merely because the probe was called with different budgets.
    /// </summary>
    public readonly struct NetworkCapabilityScore
    {
        public readonly double ThroughputMBps;
        public readonly ulong ParametersId;
        public readonly int Rounds;
        public readonly long ElapsedTicks;
        public readonly long Iterations;

        public NetworkCapabilityScore(
            double throughputMBps,
            ulong parametersId,
            int rounds,
            long elapsedTicks,
            long iterations)
        {
            ThroughputMBps = throughputMBps;
            ParametersId = parametersId;
            Rounds = rounds;
            ElapsedTicks = elapsedTicks;
            Iterations = iterations;
        }

        public bool IsUsable => ThroughputMBps > 0d && ParametersId != 0UL && Iterations > 0L;
    }
}
