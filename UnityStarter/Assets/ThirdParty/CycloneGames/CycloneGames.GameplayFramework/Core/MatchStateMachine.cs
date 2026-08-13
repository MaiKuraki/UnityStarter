using System;
using System.Threading;

namespace CycloneGames.GameplayFramework.Core
{
    public enum MatchState : byte
    {
        EnteringMap = 0,
        WaitingToStart = 1,
        InProgress = 2,
        WaitingPostMatch = 3,
        LeavingMap = 4,
        Aborted = 5,
    }

    public enum MatchStateTransitionResult : byte
    {
        Success = 0,
        Unchanged = 1,
        IllegalTransition = 2,
        InvalidState = 3,
        InvalidTimestamp = 4,
    }

    /// <summary>
    /// Pure match transition policy and elapsed clock. Callers supply a monotonic timestamp;
    /// the state machine does not depend on an engine clock or scheduler. Successful elapsed
    /// reads, state changes, and unchanged transitions all advance the observed time high-water.
    /// </summary>
    public sealed class MatchStateMachine
    {
        private MatchState state;
        private readonly int ownerThreadId;
        private double accumulatedMatchSeconds;
        private double activeSince;
        private double lastObservedTimestamp;
        private bool matchClockRunning;

        public MatchStateMachine(
            MatchState initialState = MatchState.EnteringMap,
            double timestamp = 0d)
        {
            ValidateState(initialState);
            ValidateTimestamp(timestamp);
            ownerThreadId = Thread.CurrentThread.ManagedThreadId;
            state = initialState;
            lastObservedTimestamp = timestamp;
            if (initialState == MatchState.InProgress)
            {
                activeSince = timestamp;
                matchClockRunning = true;
            }
        }

        public MatchState State
        {
            get
            {
                AssertOwnerThread();
                return state;
            }
        }

        public bool IsMatchClockRunning
        {
            get
            {
                AssertOwnerThread();
                return matchClockRunning;
            }
        }

        public MatchStateTransitionResult TryTransition(MatchState nextState, double timestamp)
        {
            AssertOwnerThread();
            if (!IsDefined(nextState))
            {
                return MatchStateTransitionResult.InvalidState;
            }

            if (!IsValidTimestamp(timestamp) || timestamp < lastObservedTimestamp)
            {
                return MatchStateTransitionResult.InvalidTimestamp;
            }

            if (state == nextState)
            {
                lastObservedTimestamp = timestamp;
                return MatchStateTransitionResult.Unchanged;
            }

            if (!IsLegalTransition(state, nextState))
            {
                return MatchStateTransitionResult.IllegalTransition;
            }

            MatchState previousState = state;
            if (previousState == MatchState.InProgress && matchClockRunning)
            {
                accumulatedMatchSeconds += timestamp - activeSince;
                matchClockRunning = false;
            }

            if (previousState == MatchState.WaitingPostMatch &&
                nextState == MatchState.WaitingToStart)
            {
                accumulatedMatchSeconds = 0d;
            }

            if (nextState == MatchState.InProgress)
            {
                activeSince = timestamp;
                matchClockRunning = true;
            }

            state = nextState;
            lastObservedTimestamp = timestamp;
            return MatchStateTransitionResult.Success;
        }

        public double GetElapsedSeconds(double timestamp)
        {
            AssertOwnerThread();
            ValidateTimestamp(timestamp);
            if (timestamp < lastObservedTimestamp)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(timestamp),
                    "Timestamp cannot precede the last successful match-state observation.");
            }

            if (!matchClockRunning)
            {
                lastObservedTimestamp = timestamp;
                return accumulatedMatchSeconds;
            }

            if (timestamp < activeSince)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(timestamp),
                    "Timestamp cannot move backwards while the match clock is running.");
            }

            double elapsedSeconds = accumulatedMatchSeconds + timestamp - activeSince;
            lastObservedTimestamp = timestamp;
            return elapsedSeconds;
        }

        public static bool IsLegalTransition(MatchState currentState, MatchState nextState)
        {
            if (!IsDefined(currentState) || !IsDefined(nextState))
            {
                return false;
            }

            switch (currentState)
            {
                case MatchState.EnteringMap:
                    return nextState == MatchState.WaitingToStart ||
                           nextState == MatchState.LeavingMap ||
                           nextState == MatchState.Aborted;
                case MatchState.WaitingToStart:
                    return nextState == MatchState.InProgress ||
                           nextState == MatchState.LeavingMap ||
                           nextState == MatchState.Aborted;
                case MatchState.InProgress:
                    return nextState == MatchState.WaitingPostMatch ||
                           nextState == MatchState.LeavingMap ||
                           nextState == MatchState.Aborted;
                case MatchState.WaitingPostMatch:
                    return nextState == MatchState.WaitingToStart ||
                           nextState == MatchState.LeavingMap ||
                           nextState == MatchState.Aborted;
                case MatchState.LeavingMap:
                case MatchState.Aborted:
                default:
                    return false;
            }
        }

        private static bool IsDefined(MatchState stateValue)
        {
            return stateValue >= MatchState.EnteringMap && stateValue <= MatchState.Aborted;
        }

        private static void ValidateState(MatchState stateValue)
        {
            if (!IsDefined(stateValue))
            {
                throw new ArgumentOutOfRangeException(nameof(stateValue));
            }
        }

        private static bool IsValidTimestamp(double timestamp)
        {
            return timestamp >= 0d && !double.IsInfinity(timestamp) && !double.IsNaN(timestamp);
        }

        private static void ValidateTimestamp(double timestamp)
        {
            if (!IsValidTimestamp(timestamp))
            {
                throw new ArgumentOutOfRangeException(nameof(timestamp));
            }
        }

        private void AssertOwnerThread()
        {
            if (Thread.CurrentThread.ManagedThreadId != ownerThreadId)
            {
                throw new InvalidOperationException(
                    "MatchStateMachine may only be accessed by its owner thread.");
            }
        }
    }
}
