#if CYCLONEGAMES_HAS_FACTORY
using System;
using CycloneGames.Factory.Runtime;
using CycloneGames.Scheduling.Core;

namespace CycloneGames.Scheduling.Integrations.Factory
{
    /// <summary>Trims inactive entries to an absolute target without reclaiming active entries.</summary>
    public sealed class PoolTrimInactiveWorkItem : IWorkItem
    {
        private IBoundedPoolMaintenance pool;
        private readonly int batchSize;

        public int TargetInactiveCount { get; }
        public int ProcessedCount { get; private set; }
        public bool TargetReached { get; private set; }
        public WorkTerminationReason? TerminationReason { get; private set; }

        public PoolTrimInactiveWorkItem(
            IBoundedPoolMaintenance pool,
            int targetInactiveCount,
            int batchSize = 1)
        {
            this.pool = pool ?? throw new ArgumentNullException(nameof(pool));
            if (targetInactiveCount < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(targetInactiveCount));
            }

            if (batchSize <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(batchSize));
            }

            TargetInactiveCount = targetInactiveCount;
            this.batchSize = batchSize;
            TargetReached = pool.CountInactive <= targetInactiveCount;
        }

        public WorkStepResult Step()
        {
            ThrowIfTerminated();
            if (pool.CountInactive <= TargetInactiveCount)
            {
                TargetReached = true;
                return WorkStepResult.Completed;
            }

            int processed = pool.TrimInactiveStep(TargetInactiveCount, batchSize);
            ProcessedCount += processed;
            if (pool.CountInactive <= TargetInactiveCount)
            {
                TargetReached = true;
                return WorkStepResult.Completed;
            }

            return processed == 0 ? WorkStepResult.Completed : WorkStepResult.Running;
        }

        public void OnTerminated(WorkTerminationReason reason)
        {
            TerminationReason = reason;
            pool = null;
        }

        private void ThrowIfTerminated()
        {
            if (pool == null)
            {
                throw new ObjectDisposedException(nameof(PoolTrimInactiveWorkItem));
            }
        }
    }
}
#endif
