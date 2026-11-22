using System;

namespace CycloneGames.Networking.Replication
{
    public readonly struct NetworkReplicationSelection
    {
        private const NetworkInterestReason KnownReasons =
            NetworkInterestReason.Always
            | NetworkInterestReason.Owner
            | NetworkInterestReason.Team
            | NetworkInterestReason.Area
            | NetworkInterestReason.Manual;

        public readonly ulong ObjectId;
        public readonly int SourceIndex;
        public readonly NetworkChannel Channel;
        public readonly NetworkInterestReason Reason;
        public readonly int EstimatedPayloadBytes;
        public readonly float Score;
        public readonly bool RequiresFullState;

        public NetworkReplicationSelection(
            ulong objectId,
            int sourceIndex,
            NetworkChannel channel,
            NetworkInterestReason reason,
            int estimatedPayloadBytes,
            float score,
            bool requiresFullState)
        {
            if (objectId == 0UL)
            {
                throw new ArgumentOutOfRangeException(nameof(objectId));
            }

            if (sourceIndex < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(sourceIndex));
            }

            if (channel < NetworkChannel.Reliable || channel > NetworkChannel.UnreliableSequenced)
            {
                throw new ArgumentOutOfRangeException(nameof(channel));
            }

            if (reason == NetworkInterestReason.None || (reason & ~KnownReasons) != 0)
            {
                throw new ArgumentOutOfRangeException(nameof(reason));
            }

            if (estimatedPayloadBytes < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(estimatedPayloadBytes));
            }

            if (score < 0f || !float.IsFinite(score))
            {
                throw new ArgumentOutOfRangeException(nameof(score));
            }

            ObjectId = objectId;
            SourceIndex = sourceIndex;
            Channel = channel;
            Reason = reason;
            EstimatedPayloadBytes = estimatedPayloadBytes;
            Score = score;
            RequiresFullState = requiresFullState;
        }

        private NetworkReplicationSelection(
            ulong objectId,
            int sourceIndex,
            NetworkChannel channel,
            NetworkInterestReason reason,
            int estimatedPayloadBytes,
            float score,
            bool requiresFullState,
            byte validatedPlannerInput)
        {
            _ = validatedPlannerInput;
            ObjectId = objectId;
            SourceIndex = sourceIndex;
            Channel = channel;
            Reason = reason;
            EstimatedPayloadBytes = estimatedPayloadBytes;
            Score = score;
            RequiresFullState = requiresFullState;
        }

        internal static bool TryCreateFromPlannerInput(
            ulong objectId,
            int sourceIndex,
            NetworkChannel channel,
            NetworkInterestReason reason,
            int estimatedPayloadBytes,
            float score,
            bool requiresFullState,
            out NetworkReplicationSelection selection)
        {
            if (objectId == 0UL
                || sourceIndex < 0
                || channel < NetworkChannel.Reliable
                || channel > NetworkChannel.UnreliableSequenced
                || reason == NetworkInterestReason.None
                || (reason & ~KnownReasons) != 0
                || estimatedPayloadBytes < 0
                || score < 0f
                || !float.IsFinite(score))
            {
                selection = default;
                return false;
            }

            selection = new NetworkReplicationSelection(
                objectId,
                sourceIndex,
                channel,
                reason,
                estimatedPayloadBytes,
                score,
                requiresFullState,
                validatedPlannerInput: 0);
            return true;
        }
    }

    /// <summary>
    /// Top-K selection implementation. SortedInsert is cache-friendly and optimal
    /// for small result capacities; BinaryHeap bounds the worst case at O(N log K)
    /// when both the object count and the result capacity are large. Both produce
    /// exactly the same ordered selection sequence.
    /// </summary>
    public enum NetworkReplicationSelectionStrategy : byte
    {
        SortedInsert = 0,
        BinaryHeap = 1,
        Auto = 2
    }

    public sealed class NetworkReplicationPlanner
    {
        private const float FULL_STATE_SCORE_BONUS = 1000000f;
        private const float OWNER_SCORE_BONUS = 10000f;
        private const float ALWAYS_SCORE_BONUS = 5000f;
        private const float TEAM_SCORE_BONUS = 1000f;
        private const float AREA_SCORE_BONUS = 100f;
        private const float OVERDUE_SCORE_PER_TICK = 0.01f;

        private readonly INetworkInterestEvaluator _interestEvaluator;
        private readonly NetworkReplicationSelectionStrategy _strategy;

        public NetworkReplicationPlanner()
            : this(DefaultNetworkInterestEvaluator.Instance, NetworkReplicationSelectionStrategy.Auto)
        {
        }

        public NetworkReplicationPlanner(INetworkInterestEvaluator interestEvaluator)
            : this(interestEvaluator, NetworkReplicationSelectionStrategy.Auto)
        {
        }

        public NetworkReplicationPlanner(NetworkReplicationSelectionStrategy strategy)
            : this(DefaultNetworkInterestEvaluator.Instance, strategy)
        {
        }

        public NetworkReplicationPlanner(
            INetworkInterestEvaluator interestEvaluator,
            NetworkReplicationSelectionStrategy strategy)
        {
            _interestEvaluator = interestEvaluator ?? throw new ArgumentNullException(nameof(interestEvaluator));
            if (strategy > NetworkReplicationSelectionStrategy.Auto)
                throw new ArgumentOutOfRangeException(nameof(strategy));
            _strategy = strategy;
        }

        public int BuildPlan(
            in NetworkReplicationObserver observer,
            ReadOnlySpan<NetworkReplicatedObject> objects,
            int serverTick,
            ref NetworkSendBudget budget,
            Span<NetworkReplicationSelection> results)
        {
            if (!observer.IsValid)
            {
                throw new ArgumentException("Replication observer is invalid.", nameof(observer));
            }

            // serverTick is a 32-bit modular counter and may legitimately be
            // negative after the int.MaxValue wrap. Every elapsed-time comparison
            // goes through NetworkTickMath instead of a raw int subtraction.
            bool useHeap = ShouldUseHeap(_strategy);
            int candidateCount = 0;
            for (int i = 0; i < objects.Length; i++)
            {
                NetworkReplicatedObject replicatedObject = objects[i];
                if (!ShouldConsider(replicatedObject, serverTick))
                {
                    continue;
                }

                if (!_interestEvaluator.IsInterested(observer, replicatedObject, out NetworkInterestReason reason))
                {
                    continue;
                }

                float score = CalculateScore(replicatedObject, reason, serverTick);
                if (!NetworkReplicationSelection.TryCreateFromPlannerInput(
                        replicatedObject.ObjectId,
                        i,
                        replicatedObject.Policy.Channel,
                        reason,
                        replicatedObject.EstimatedPayloadBytes,
                        score,
                        replicatedObject.RequiresFullState,
                        out NetworkReplicationSelection selection))
                {
                    // Evaluators are replaceable extension points. Invalid evaluator output must not
                    // escape as a selection or consume the observer's send budget.
                    continue;
                }

                if (useHeap)
                    InsertCandidateHeap(selection, results, ref candidateCount);
                else
                    InsertCandidate(selection, results, ref candidateCount);
            }

            return FinalizePlan(results, candidateCount, useHeap, ref budget);
        }

        internal static bool ShouldConsider(in NetworkReplicatedObject replicatedObject, int serverTick)
        {
            if (replicatedObject.Policy.Interest == NetworkReplicationInterest.None)
            {
                return false;
            }

            if (!replicatedObject.RequiresFullState
                && !replicatedObject.IsDirty
                && !replicatedObject.Policy.SendUnchanged)
            {
                return false;
            }

            if (replicatedObject.RequiresFullState || replicatedObject.LastSentTick == NetworkReplicatedObject.NEVER_SENT)
            {
                return true;
            }

            return NetworkTickMath.HasElapsed(
                serverTick,
                replicatedObject.LastSentTick,
                replicatedObject.Policy.MinIntervalTicks);
        }

        internal static float CalculateScore(
            in NetworkReplicatedObject replicatedObject,
            NetworkInterestReason reason,
            int serverTick)
        {
            float score = replicatedObject.Policy.Priority;
            if (replicatedObject.RequiresFullState)
            {
                score += FULL_STATE_SCORE_BONUS;
            }

            if ((reason & NetworkInterestReason.Owner) != 0)
            {
                score += OWNER_SCORE_BONUS;
            }

            if ((reason & NetworkInterestReason.Always) != 0)
            {
                score += ALWAYS_SCORE_BONUS;
            }

            if ((reason & NetworkInterestReason.Team) != 0)
            {
                score += TEAM_SCORE_BONUS;
            }

            if ((reason & NetworkInterestReason.Area) != 0)
            {
                score += AREA_SCORE_BONUS;
            }

            if (replicatedObject.LastSentTick != NetworkReplicatedObject.NEVER_SENT)
            {
                score += NetworkTickMath.ElapsedNonNegative(
                    serverTick, replicatedObject.LastSentTick) * OVERDUE_SCORE_PER_TICK;
            }

            return score;
        }

        internal static void InsertCandidate(
            in NetworkReplicationSelection selection,
            Span<NetworkReplicationSelection> results,
            ref int count)
        {
            if (results.Length == 0)
            {
                return;
            }

            int insertIndex = count;
            if (count == results.Length)
            {
                int lastIndex = results.Length - 1;
                if (selection.Score <= results[lastIndex].Score)
                {
                    return;
                }

                insertIndex = lastIndex;
            }
            else
            {
                count++;
            }

            while (insertIndex > 0 && selection.Score > results[insertIndex - 1].Score)
            {
                results[insertIndex] = results[insertIndex - 1];
                insertIndex--;
            }

            results[insertIndex] = selection;
        }

        internal static bool ShouldUseHeap(NetworkReplicationSelectionStrategy strategy)
        {
            // Auto uses insertion sort. BinaryHeap is an explicit opt-in because
            // its cost depends on the candidate distribution, which (N, K) alone
            // does not describe. Both strategies produce the same ordered sequence.
            return strategy == NetworkReplicationSelectionStrategy.BinaryHeap;
        }

        /// <summary>
        /// Shared finalization for both the per-connection planner and the shared
        /// frame planner, guaranteeing an identical ordered/budgeted output.
        /// </summary>
        internal static int FinalizePlan(
            Span<NetworkReplicationSelection> results,
            int candidateCount,
            bool useHeap,
            ref NetworkSendBudget budget)
        {
            if (useHeap)
            {
                if (candidateCount < results.Length)
                    SortSparseDescending(results, candidateCount);
                else
                    SortHeapDescending(results, candidateCount);
            }

            int acceptedCount = 0;
            for (int i = 0; i < candidateCount; i++)
            {
                NetworkReplicationSelection selection = results[i];
                if (!budget.TryConsume(selection.EstimatedPayloadBytes))
                    continue;

                results[acceptedCount++] = selection;
            }

            return acceptedCount;
        }

        // Ordering key: higher score first, then earlier source index for ties.
        // This reproduces the insertion-sort path's stable ordering exactly.
        internal static bool Precedes(in NetworkReplicationSelection a, in NetworkReplicationSelection b)
        {
            if (a.Score != b.Score)
                return a.Score > b.Score;
            return a.SourceIndex < b.SourceIndex;
        }

        // Min-heap predicate: true when a is WORSE than b and belongs nearer the root.
        internal static bool IsWorse(in NetworkReplicationSelection a, in NetworkReplicationSelection b)
        {
            return Precedes(b, a);
        }

        internal static void InsertCandidateHeap(
            in NetworkReplicationSelection selection,
            Span<NetworkReplicationSelection> results,
            ref int count)
        {
            if (results.Length == 0)
                return;

            if (count < results.Length)
            {
                // Sparse candidate sets pay only an O(1) append. The min-heap is
                // built once when the buffer first fills (or once at the end when
                // it never fills), so the heap path is never worse than the
                // insertion-sort path on small or sparse inputs.
                results[count] = selection;
                count++;
                if (count == results.Length)
                    BuildMinHeap(results, count);
                return;
            }

            // The root is the current worst candidate. Replace only when the new
            // candidate is strictly better (ties keep the earlier source index).
            if (!Precedes(selection, results[0]))
                return;

            results[0] = selection;
            SiftDown(results, 0, count);
        }

        internal static void BuildMinHeap(Span<NetworkReplicationSelection> heap, int count)
        {
            // Floyd heap construction: O(K), in place, no allocation.
            for (int i = (count >> 1) - 1; i >= 0; i--)
                SiftDown(heap, i, count);
        }

        internal static void SiftDown(Span<NetworkReplicationSelection> heap, int root, int count)
        {
            while (true)
            {
                int left = (root << 1) + 1;
                if (left >= count)
                    break;

                int right = left + 1;
                int worst = left;
                if (right < count && IsWorse(heap[right], heap[left]))
                    worst = right;

                if (!IsWorse(heap[worst], heap[root]))
                    break;

                NetworkReplicationSelection temp = heap[root];
                heap[root] = heap[worst];
                heap[worst] = temp;
                root = worst;
            }
        }

        internal static void SortSparseDescending(Span<NetworkReplicationSelection> items, int count)
        {
            // Used when the candidate buffer never filled; O(C^2) struct moves
            // are cheaper than heap construction plus heapsort for small C.
            for (int i = 1; i < count; i++)
            {
                NetworkReplicationSelection value = items[i];
                int j = i - 1;
                while (j >= 0 && Precedes(value, items[j]))
                {
                    items[j + 1] = items[j];
                    j--;
                }
                items[j + 1] = value;
            }
        }

        internal static void SortHeapDescending(Span<NetworkReplicationSelection> heap, int count)
        {
            // Repeatedly move the worst root to the tail. With a min-heap this
            // leaves the array ordered best-first without a second buffer.
            for (int end = count - 1; end > 0; end--)
            {
                NetworkReplicationSelection temp = heap[0];
                heap[0] = heap[end];
                heap[end] = temp;
                SiftDown(heap, 0, end);
            }
        }
    }
}
