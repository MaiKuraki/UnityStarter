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
        ClockEpochMismatch = 5,
    }

    public enum MatchStateRestoreResult : byte
    {
        Success = 0,
        InvalidSnapshot = 1,
        InvalidTimestamp = 2,
        ClockEpochMismatch = 3,
        RestoreTimestampPrecedesSnapshot = 4,
    }

    /// <summary>
    /// Pure match transition policy and elapsed clock. Time-domain ownership is explicit through
    /// <see cref="MatchTimestamp"/> so values from unrelated clocks cannot be mixed accidentally.
    /// All mutable access is confined to the thread that created or restored the state machine.
    /// </summary>
    public sealed class MatchStateMachine
    {
        private MatchState state;
        private readonly int ownerThreadId;
        private readonly Guid clockEpoch;
        private double accumulatedMatchSeconds;
        private double activeSince;
        private double lastObservedTimestamp;
        private bool matchClockRunning;

        public MatchStateMachine(MatchState initialState, in MatchTimestamp timestamp)
        {
            ValidateState(initialState);
            ValidateTimestamp(in timestamp);

            ownerThreadId = Thread.CurrentThread.ManagedThreadId;
            clockEpoch = timestamp.ClockEpoch;
            state = initialState;
            lastObservedTimestamp = timestamp.Seconds;
            if (initialState == MatchState.InProgress)
            {
                activeSince = timestamp.Seconds;
                matchClockRunning = true;
            }
        }

        private MatchStateMachine(
            in MatchStateSnapshot snapshot,
            in MatchTimestamp restoreTimestamp)
        {
            ownerThreadId = Thread.CurrentThread.ManagedThreadId;
            clockEpoch = restoreTimestamp.ClockEpoch;
            state = snapshot.State;
            lastObservedTimestamp = restoreTimestamp.Seconds;
            accumulatedMatchSeconds = snapshot.ElapsedSeconds;

            if (snapshot.State == MatchState.InProgress)
            {
                accumulatedMatchSeconds +=
                    restoreTimestamp.Seconds - snapshot.CapturedTimestamp;
                activeSince = restoreTimestamp.Seconds;
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

        public Guid ClockEpoch
        {
            get
            {
                AssertOwnerThread();
                return clockEpoch;
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

        public MatchStateTransitionResult TryTransition(
            MatchState nextState,
            in MatchTimestamp timestamp)
        {
            AssertOwnerThread();
            if (!IsDefined(nextState))
            {
                return MatchStateTransitionResult.InvalidState;
            }

            if (!timestamp.IsValid)
            {
                return MatchStateTransitionResult.InvalidTimestamp;
            }

            if (timestamp.ClockEpoch != clockEpoch)
            {
                return MatchStateTransitionResult.ClockEpochMismatch;
            }

            if (timestamp.Seconds < lastObservedTimestamp)
            {
                return MatchStateTransitionResult.InvalidTimestamp;
            }

            if (state == nextState)
            {
                lastObservedTimestamp = timestamp.Seconds;
                return MatchStateTransitionResult.Unchanged;
            }

            if (!IsLegalTransition(state, nextState))
            {
                return MatchStateTransitionResult.IllegalTransition;
            }

            MatchState previousState = state;
            if (previousState == MatchState.InProgress && matchClockRunning)
            {
                accumulatedMatchSeconds += timestamp.Seconds - activeSince;
                matchClockRunning = false;
            }

            if (previousState == MatchState.WaitingPostMatch &&
                nextState == MatchState.WaitingToStart)
            {
                accumulatedMatchSeconds = 0d;
            }

            if (nextState == MatchState.InProgress)
            {
                activeSince = timestamp.Seconds;
                matchClockRunning = true;
            }

            state = nextState;
            lastObservedTimestamp = timestamp.Seconds;
            return MatchStateTransitionResult.Success;
        }

        public double GetElapsedSeconds(in MatchTimestamp timestamp)
        {
            AssertOwnerThread();
            ValidateCompatibleTimestamp(in timestamp);
            if (timestamp.Seconds < lastObservedTimestamp)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(timestamp),
                    "Timestamp cannot precede the last successful match-state observation.");
            }

            if (!matchClockRunning)
            {
                lastObservedTimestamp = timestamp.Seconds;
                return accumulatedMatchSeconds;
            }

            if (timestamp.Seconds < activeSince)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(timestamp),
                    "Timestamp cannot move backwards while the match clock is running.");
            }

            double elapsedSeconds =
                accumulatedMatchSeconds + timestamp.Seconds - activeSince;
            lastObservedTimestamp = timestamp.Seconds;
            return elapsedSeconds;
        }

        /// <summary>
        /// Pure elapsed-seconds read that does not advance the monotonic observation watermark.
        /// Use this for diagnostics, UI, or deterministic replay where a read must not invalidate
        /// later transitions. <see cref="GetElapsedSeconds"/> remains the committing read.
        /// </summary>
        public double PeekElapsedSeconds(in MatchTimestamp timestamp)
        {
            AssertOwnerThread();
            ValidateCompatibleTimestamp(in timestamp);
            if (!matchClockRunning)
            {
                return accumulatedMatchSeconds;
            }

            if (timestamp.Seconds < activeSince)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(timestamp),
                    "Timestamp cannot move backwards while the match clock is running.");
            }

            return accumulatedMatchSeconds + timestamp.Seconds - activeSince;
        }

        public MatchStateSnapshot CaptureSnapshot(in MatchTimestamp timestamp)
        {
            double elapsedSeconds = GetElapsedSeconds(in timestamp);
            return new MatchStateSnapshot(
                state,
                elapsedSeconds,
                timestamp.Seconds,
                clockEpoch);
        }

        public static MatchStateRestoreResult TryRestore(
            in MatchStateSnapshot snapshot,
            in MatchTimestamp restoreTimestamp,
            out MatchStateMachine stateMachine)
        {
            stateMachine = null;
            if (!snapshot.IsValid)
            {
                return MatchStateRestoreResult.InvalidSnapshot;
            }

            if (!restoreTimestamp.IsValid)
            {
                return MatchStateRestoreResult.InvalidTimestamp;
            }

            if (snapshot.ClockEpoch != restoreTimestamp.ClockEpoch)
            {
                return MatchStateRestoreResult.ClockEpochMismatch;
            }

            if (restoreTimestamp.Seconds < snapshot.CapturedTimestamp)
            {
                return MatchStateRestoreResult.RestoreTimestampPrecedesSnapshot;
            }

            stateMachine = new MatchStateMachine(in snapshot, in restoreTimestamp);
            return MatchStateRestoreResult.Success;
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

        internal static bool IsDefined(MatchState stateValue)
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

        private static void ValidateTimestamp(in MatchTimestamp timestamp)
        {
            if (!timestamp.IsValid)
            {
                throw new ArgumentOutOfRangeException(nameof(timestamp));
            }
        }

        private void ValidateCompatibleTimestamp(in MatchTimestamp timestamp)
        {
            ValidateTimestamp(in timestamp);
            if (timestamp.ClockEpoch != clockEpoch)
            {
                throw new ArgumentException(
                    "Timestamp belongs to a different match-clock epoch.",
                    nameof(timestamp));
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
