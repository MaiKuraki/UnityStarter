using System;

namespace CycloneGames.DataTable
{
    /// <summary>Hard limits for one incremental payload-release call.</summary>
    public readonly struct DataTableBytesCacheReleaseBudget :
        IEquatable<DataTableBytesCacheReleaseBudget>
    {
        public static readonly DataTableBytesCacheReleaseBudget Unlimited =
            new DataTableBytesCacheReleaseBudget(int.MaxValue, long.MaxValue);

        public DataTableBytesCacheReleaseBudget(int maxPayloads, long maxBytesToClear)
        {
            if (maxPayloads < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxPayloads));
            }

            if (maxBytesToClear < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxBytesToClear));
            }

            MaxPayloads = maxPayloads;
            MaxBytesToClear = maxBytesToClear;
        }

        /// <summary>Maximum distinct payload cursors visited by one call.</summary>
        public int MaxPayloads { get; }

        /// <summary>
        /// Maximum bytes cleared by one call when secure byte clearing is enabled. Dropping an
        /// array reference without clearing is constant work and consumes only payload budget.
        /// </summary>
        public long MaxBytesToClear { get; }

        public bool Equals(DataTableBytesCacheReleaseBudget other)
        {
            return MaxPayloads == other.MaxPayloads &&
                   MaxBytesToClear == other.MaxBytesToClear;
        }

        public override bool Equals(object obj)
        {
            return obj is DataTableBytesCacheReleaseBudget other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return (MaxPayloads * 397) ^ MaxBytesToClear.GetHashCode();
            }
        }

        public static bool operator ==(
            DataTableBytesCacheReleaseBudget left,
            DataTableBytesCacheReleaseBudget right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(
            DataTableBytesCacheReleaseBudget left,
            DataTableBytesCacheReleaseBudget right)
        {
            return !left.Equals(right);
        }
    }

    /// <summary>Allocation-free result of one bounded release call.</summary>
    public readonly struct DataTableBytesCacheReleaseResult
    {
        internal DataTableBytesCacheReleaseResult(
            int processedPayloads,
            long clearedBytes,
            int releasedPayloads,
            long releasedBytes,
            int remainingPayloads,
            long remainingBytes,
            bool isComplete)
        {
            ProcessedPayloads = processedPayloads;
            ClearedBytes = clearedBytes;
            ReleasedPayloads = releasedPayloads;
            ReleasedBytes = releasedBytes;
            RemainingPayloads = remainingPayloads;
            RemainingBytes = remainingBytes;
            IsComplete = isComplete;
        }

        public int ProcessedPayloads { get; }

        public long ClearedBytes { get; }

        public int ReleasedPayloads { get; }

        public long ReleasedBytes { get; }

        public int RemainingPayloads { get; }

        public long RemainingBytes { get; }

        public bool IsComplete { get; }
    }
}
