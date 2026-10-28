#if CYCLONEGAMES_HAS_FACTORY
using System;
using CycloneGames.Factory.Runtime;
using CycloneGames.Scheduling.Core;

namespace CycloneGames.Scheduling.Integrations.Factory
{
    /// <summary>Creates up to a requested number of additional pool entries in bounded batches.</summary>
    public sealed class PoolWarmupWorkItem : IWorkItem
    {
        private IMemoryPool pool;
        private readonly int batchSize;
        private int remainingCount;

        public int RequestedCount { get; }
        public int CreatedCount { get; private set; }
        public bool TargetReached { get; private set; }
        public WorkTerminationReason? TerminationReason { get; private set; }

        public PoolWarmupWorkItem(IMemoryPool pool, int count, int batchSize = 1)
        {
            this.pool = pool ?? throw new ArgumentNullException(nameof(pool));
            if (count < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(count));
            }

            if (batchSize <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(batchSize));
            }

            RequestedCount = count;
            remainingCount = count;
            this.batchSize = batchSize;
            TargetReached = count == 0;
        }

        public WorkStepResult Step()
        {
            ThrowIfTerminated();
            if (remainingCount == 0)
            {
                TargetReached = true;
                return WorkStepResult.Completed;
            }

            int requested = remainingCount < batchSize ? remainingCount : batchSize;
            int created = pool.WarmupStep(requested);

            // A pool is free to report more or fewer entries than requested. Clamp so
            // remainingCount can never go negative and CreatedCount can never exceed
            // RequestedCount, both of which would otherwise leak into the next Step as a
            // negative argument or silently break the caller's memory budget.
            if (created > requested)
            {
                created = requested;
            }
            else if (created < 0)
            {
                created = 0;
            }

            CreatedCount += created;
            remainingCount -= created;

            if (remainingCount == 0)
            {
                TargetReached = true;
                return WorkStepResult.Completed;
            }

            return created == 0 ? WorkStepResult.Completed : WorkStepResult.Running;
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
                throw new ObjectDisposedException(nameof(PoolWarmupWorkItem));
            }
        }
    }
}
#endif
