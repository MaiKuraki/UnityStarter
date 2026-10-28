using System;
using CycloneGames.Factory.Runtime;
using CycloneGames.Scheduling.Core;

namespace CycloneGames.Scheduling.Integrations.Factory
{
    /// <summary>Returns active entries to their owner pool in bounded batches.</summary>
    public sealed class PoolDespawnWorkItem : IWorkItem
    {
        private IMemoryPool pool;
        private readonly int batchSize;

        public int ProcessedCount { get; private set; }
        public bool TargetReached { get; private set; }
        public WorkTerminationReason? TerminationReason { get; private set; }

        public PoolDespawnWorkItem(IMemoryPool pool, int batchSize = 1)
        {
            this.pool = pool ?? throw new ArgumentNullException(nameof(pool));
            if (batchSize <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(batchSize));
            }

            this.batchSize = batchSize;
            TargetReached = pool.CountActive == 0;
        }

        public WorkStepResult Step()
        {
            ThrowIfTerminated();
            if (pool.CountActive == 0)
            {
                TargetReached = true;
                return WorkStepResult.Completed;
            }

            int processed = pool.DespawnStep(batchSize);
            ProcessedCount += processed;
            if (pool.CountActive == 0)
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
                throw new ObjectDisposedException(nameof(PoolDespawnWorkItem));
            }
        }
    }
}
