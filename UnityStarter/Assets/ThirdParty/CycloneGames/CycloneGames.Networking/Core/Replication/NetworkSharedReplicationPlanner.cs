using System;

namespace CycloneGames.Networking.Replication
{
    /// <summary>
    /// Computes ShouldConsider once per server tick for the whole object set,
    /// then lets each connection evaluate only the resulting candidate set.
    /// Prepare is O(N); EmitFor is O(D + D * K) where D is the candidate count.
    ///
    /// The prepared frame holds a contiguous copy of each surviving object and
    /// its original global source index, so EmitFor reads sequentially. Only
    /// ShouldConsider and source ordering are shared. Interest evaluation,
    /// reason-dependent score bonuses and send-budget consumption stay per
    /// connection. EmitFor reuses NetworkReplicationPlanner helpers and returns
    /// the same ordered selection sequence as BuildPlan.
    ///
    /// Single owner thread. The frame is bound to one server tick; EmitFor
    /// rejects a stale tick. Candidate storage is allocated once at construction
    /// and result/budget storage comes from the caller, so Prepare and EmitFor
    /// allocate nothing. Use NetworkReplicationPlanner when D approaches N.
    /// </summary>
    public sealed class NetworkSharedReplicationPlanner
    {
        private struct Candidate
        {
            public NetworkReplicatedObject Object;
            public int SourceIndex;
        }

        private readonly Candidate[] _candidates;
        private readonly INetworkInterestEvaluator _interestEvaluator;
        private readonly NetworkReplicationSelectionStrategy _strategy;

        private int _candidateCount;
        private int _preparedObjectCount;
        private int _preparedTick;
        private bool _prepared;

        public NetworkSharedReplicationPlanner(
            int maxObjects,
            INetworkInterestEvaluator interestEvaluator = null,
            NetworkReplicationSelectionStrategy strategy = NetworkReplicationSelectionStrategy.Auto)
        {
            if (maxObjects <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxObjects));
            if (strategy > NetworkReplicationSelectionStrategy.Auto)
                throw new ArgumentOutOfRangeException(nameof(strategy));

            _candidates = new Candidate[maxObjects];
            _interestEvaluator = interestEvaluator ?? DefaultNetworkInterestEvaluator.Instance;
            _strategy = strategy;
        }

        public int MaxObjects => _candidates.Length;

        public int CandidateCount => _candidateCount;

        public bool IsPrepared => _prepared;

        public int PreparedObjectCount => _preparedObjectCount;

        public int PreparedTick => _preparedTick;

        /// <summary>
        /// Builds a compact shared candidate set for one server tick. Candidates
        /// keep their ORIGINAL global source indices in ascending order, so
        /// per-connection output ordering and SourceIndex values match BuildPlan.
        /// The frame owns its data; the source span is not retained.
        /// </summary>
        public int Prepare(ReadOnlySpan<NetworkReplicatedObject> objects, int serverTick)
        {
            // serverTick is a 32-bit modular counter; negative values are valid
            // after the int.MaxValue wrap (see NetworkTickMath).
            if (objects.Length > _candidates.Length)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(objects),
                    "Object count exceeds the shared planner capacity. Construct the planner with the maximum expected object count.");
            }

            _candidateCount = 0;
            for (int i = 0; i < objects.Length; i++)
            {
                NetworkReplicatedObject replicatedObject = objects[i];
                if (!NetworkReplicationPlanner.ShouldConsider(replicatedObject, serverTick))
                    continue;

                _candidates[_candidateCount].Object = replicatedObject;
                _candidates[_candidateCount].SourceIndex = i;
                _candidateCount++;
            }

            _preparedObjectCount = objects.Length;
            _preparedTick = serverTick;
            _prepared = true;
            return _candidateCount;
        }

        /// <summary>
        /// Emits one connection's plan from the prepared compact frame. The tick
        /// must match the prepared tick. No source object span is needed or read.
        /// </summary>
        public int EmitFor(
            in NetworkReplicationObserver observer,
            int serverTick,
            ref NetworkSendBudget budget,
            Span<NetworkReplicationSelection> results)
        {
            if (!_prepared)
                throw new InvalidOperationException("The shared replication frame has not been prepared for this tick.");
            if (!observer.IsValid)
                throw new ArgumentException("Replication observer is invalid.", nameof(observer));
            // serverTick is a 32-bit modular counter; negative values are valid
            // after the int.MaxValue wrap (see NetworkTickMath).
            if (serverTick != _preparedTick)
            {
                throw new InvalidOperationException(
                    "The shared replication frame is stale; call Prepare for the current tick before emitting.");
            }

            bool useHeap = NetworkReplicationPlanner.ShouldUseHeap(_strategy);
            int candidateCount = 0;

            for (int c = 0; c < _candidateCount; c++)
            {
                NetworkReplicatedObject replicatedObject = _candidates[c].Object;
                int index = _candidates[c].SourceIndex;

                if (!_interestEvaluator.IsInterested(
                        observer, replicatedObject, out NetworkInterestReason reason))
                {
                    continue;
                }

                float score = NetworkReplicationPlanner.CalculateScore(
                    replicatedObject, reason, serverTick);

                if (!NetworkReplicationSelection.TryCreateFromPlannerInput(
                        replicatedObject.ObjectId,
                        index,
                        replicatedObject.Policy.Channel,
                        reason,
                        replicatedObject.EstimatedPayloadBytes,
                        score,
                        replicatedObject.RequiresFullState,
                        out NetworkReplicationSelection selection))
                {
                    continue;
                }

                if (useHeap)
                    NetworkReplicationPlanner.InsertCandidateHeap(selection, results, ref candidateCount);
                else
                    NetworkReplicationPlanner.InsertCandidate(selection, results, ref candidateCount);
            }

            return NetworkReplicationPlanner.FinalizePlan(
                results, candidateCount, useHeap, ref budget);
        }

        public void Reset()
        {
            _candidateCount = 0;
            _preparedObjectCount = 0;
            _preparedTick = 0;
            _prepared = false;
        }
    }
}
