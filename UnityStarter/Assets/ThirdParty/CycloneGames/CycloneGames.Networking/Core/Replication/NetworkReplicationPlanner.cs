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

    public sealed class NetworkReplicationPlanner
    {
        private const float FULL_STATE_SCORE_BONUS = 1000000f;
        private const float OWNER_SCORE_BONUS = 10000f;
        private const float ALWAYS_SCORE_BONUS = 5000f;
        private const float TEAM_SCORE_BONUS = 1000f;
        private const float AREA_SCORE_BONUS = 100f;
        private const float OVERDUE_SCORE_PER_TICK = 0.01f;

        private readonly INetworkInterestEvaluator _interestEvaluator;

        public NetworkReplicationPlanner()
            : this(DefaultNetworkInterestEvaluator.Instance)
        {
        }

        public NetworkReplicationPlanner(INetworkInterestEvaluator interestEvaluator)
        {
            _interestEvaluator = interestEvaluator ?? throw new ArgumentNullException(nameof(interestEvaluator));
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

            if (serverTick < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(serverTick));
            }

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

                InsertCandidate(
                    selection,
                    results,
                    ref candidateCount);
            }

            int acceptedCount = 0;
            for (int i = 0; i < candidateCount; i++)
            {
                NetworkReplicationSelection selection = results[i];
                if (!budget.TryConsume(selection.EstimatedPayloadBytes))
                {
                    continue;
                }

                results[acceptedCount++] = selection;
            }

            return acceptedCount;
        }

        private static bool ShouldConsider(in NetworkReplicatedObject replicatedObject, int serverTick)
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

            return serverTick - replicatedObject.LastSentTick >= replicatedObject.Policy.MinIntervalTicks;
        }

        private static float CalculateScore(
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
                score += MathF.Max(0f, serverTick - replicatedObject.LastSentTick) * OVERDUE_SCORE_PER_TICK;
            }

            return score;
        }

        private static void InsertCandidate(
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
    }
}
