using System.Threading;

namespace CycloneGames.BehaviorTree.Core
{
    /// <summary>
    /// Selects when a composite re-evaluates its conditional abort and reactive branches.
    /// PollEveryTick keeps the unconditional scan; OnChange scans only after a reevaluation
    /// request; Hybrid additionally scans on a bounded poll cadence.
    /// </summary>
    public enum RuntimeReevaluationMode : byte
    {
        PollEveryTick = 0,
        OnChange = 1,
        Hybrid = 2,
    }

    /// <summary>
    /// Owns the monotone reevaluation epoch of one tree.
    /// Producers may request reevaluation from any thread; the owner thread consumes the
    /// epoch during tick. Requests coalesce into one counter, so bursty event streams
    /// create no queue, no allocation, and no unbounded backlog.
    /// </summary>
    public sealed class RuntimeBTEvaluationController
    {
        private int _epoch;

        public int Epoch => Volatile.Read(ref _epoch);

        public int RequestReevaluation()
        {
            return Interlocked.Increment(ref _epoch);
        }

        /// <summary>
        /// lastSeenEpoch and pollCounter are per-composite state owned by the tree's owner thread.
        /// </summary>
        public bool ShouldReevaluate(
            ref int lastSeenEpoch,
            ref int pollCounter,
            RuntimeReevaluationMode mode,
            int pollInterval)
        {
            if (mode == RuntimeReevaluationMode.PollEveryTick)
            {
                return true;
            }

            int current = Volatile.Read(ref _epoch);
            if (current != lastSeenEpoch)
            {
                lastSeenEpoch = current;
                pollCounter = 0;
                return true;
            }

            if (mode == RuntimeReevaluationMode.OnChange)
            {
                return false;
            }

            int interval = pollInterval > 0 ? pollInterval : 1;
            pollCounter++;
            if (pollCounter < interval)
            {
                return false;
            }

            pollCounter = 0;
            return true;
        }
    }
}
