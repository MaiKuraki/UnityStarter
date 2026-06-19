using System;
using System.Collections.Generic;

namespace CycloneGames.Networking.Session
{
    public enum NetworkMatchmakingPlanAction : byte
    {
        None,
        JoinSession,
        CreateSession,
        EnterQueue
    }

    public enum NetworkMatchmakingPlanReason : byte
    {
        None,
        CompatibleSessionFound,
        NoCompatibleSessionCreateAllowed,
        NoCompatibleSessionQueueAllowed,
        NoCompatibleSession
    }

    public readonly struct NetworkMatchmakingOptions
    {
        public static readonly NetworkMatchmakingOptions Default = new NetworkMatchmakingOptions(
            allowCreateSession: true,
            allowQueue: true,
            minimumJoinScore: 0f);

        public readonly bool AllowCreateSession;
        public readonly bool AllowQueue;
        public readonly float MinimumJoinScore;

        public NetworkMatchmakingOptions(bool allowCreateSession, bool allowQueue, float minimumJoinScore = 0f)
        {
            AllowCreateSession = allowCreateSession;
            AllowQueue = allowQueue;
            MinimumJoinScore = minimumJoinScore;
        }
    }

    public readonly struct NetworkMatchmakingPlan
    {
        public readonly NetworkMatchmakingPlanAction Action;
        public readonly NetworkMatchmakingPlanReason Reason;
        public readonly NetworkSessionDescriptor SelectedSession;
        public readonly float Score;

        public NetworkMatchmakingPlan(
            NetworkMatchmakingPlanAction action,
            NetworkMatchmakingPlanReason reason,
            in NetworkSessionDescriptor selectedSession,
            float score)
        {
            Action = action;
            Reason = reason;
            SelectedSession = selectedSession;
            Score = score;
        }

        public bool HasSession
        {
            get
            {
                return SelectedSession.IsValid;
            }
        }

        public static NetworkMatchmakingPlan Join(in NetworkSessionDescriptor session, float score)
        {
            return new NetworkMatchmakingPlan(
                NetworkMatchmakingPlanAction.JoinSession,
                NetworkMatchmakingPlanReason.CompatibleSessionFound,
                session,
                score);
        }

        public static NetworkMatchmakingPlan Create()
        {
            return new NetworkMatchmakingPlan(
                NetworkMatchmakingPlanAction.CreateSession,
                NetworkMatchmakingPlanReason.NoCompatibleSessionCreateAllowed,
                default,
                0f);
        }

        public static NetworkMatchmakingPlan Queue()
        {
            return new NetworkMatchmakingPlan(
                NetworkMatchmakingPlanAction.EnterQueue,
                NetworkMatchmakingPlanReason.NoCompatibleSessionQueueAllowed,
                default,
                0f);
        }

        public static NetworkMatchmakingPlan None()
        {
            return new NetworkMatchmakingPlan(
                NetworkMatchmakingPlanAction.None,
                NetworkMatchmakingPlanReason.NoCompatibleSession,
                default,
                0f);
        }
    }

    public sealed class NetworkMatchmakingCoordinator
    {
        private readonly List<NetworkSessionDescriptor> _scratch;

        public NetworkMatchmakingCoordinator(int candidateCapacity = 64)
        {
            if (candidateCapacity < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(candidateCapacity));
            }

            _scratch = new List<NetworkSessionDescriptor>(candidateCapacity);
        }

        public NetworkMatchmakingPlan BuildPlan(
            NetworkSessionDirectory directory,
            NetworkSessionSearchCriteria criteria,
            in NetworkMatchmakingOptions options)
        {
            if (directory == null)
            {
                throw new ArgumentNullException(nameof(directory));
            }

            criteria ??= new NetworkSessionSearchCriteria();
            _scratch.Clear();
            directory.Search(criteria, _scratch);

            if (_scratch.Count > 0)
            {
                NetworkSessionDescriptor session = _scratch[0];
                float score = NetworkSessionDirectory.Score(session, criteria);
                _scratch.Clear();

                if (score >= options.MinimumJoinScore)
                {
                    return NetworkMatchmakingPlan.Join(session, score);
                }
            }

            _scratch.Clear();

            if (options.AllowCreateSession)
            {
                return NetworkMatchmakingPlan.Create();
            }

            return options.AllowQueue
                ? NetworkMatchmakingPlan.Queue()
                : NetworkMatchmakingPlan.None();
        }

        public NetworkMatchmakingPlan BuildPlan(
            IReadOnlyList<NetworkSessionDescriptor> sessions,
            NetworkSessionSearchCriteria criteria,
            in NetworkMatchmakingOptions options)
        {
            if (sessions == null)
            {
                throw new ArgumentNullException(nameof(sessions));
            }

            criteria ??= new NetworkSessionSearchCriteria();
            bool found = false;
            NetworkSessionDescriptor best = default;
            float bestScore = float.MinValue;

            for (int i = 0; i < sessions.Count; i++)
            {
                NetworkSessionDescriptor session = sessions[i];
                if (!NetworkSessionDirectory.Matches(session, criteria))
                {
                    continue;
                }

                float score = NetworkSessionDirectory.Score(session, criteria);
                if (!found
                    || score > bestScore
                    || (Math.Abs(score - bestScore) <= float.Epsilon
                        && string.CompareOrdinal(session.SessionId, best.SessionId) < 0))
                {
                    best = session;
                    bestScore = score;
                    found = true;
                }
            }

            if (found && bestScore >= options.MinimumJoinScore)
            {
                return NetworkMatchmakingPlan.Join(best, bestScore);
            }

            if (options.AllowCreateSession)
            {
                return NetworkMatchmakingPlan.Create();
            }

            return options.AllowQueue
                ? NetworkMatchmakingPlan.Queue()
                : NetworkMatchmakingPlan.None();
        }
    }
}
