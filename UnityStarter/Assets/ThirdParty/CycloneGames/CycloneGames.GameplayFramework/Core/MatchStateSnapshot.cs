using System;

namespace CycloneGames.GameplayFramework.Core
{
    /// <summary>
    /// Engine-independent match state captured in one clock domain. Wire and persistence schema
    /// ownership belongs to the adapter that serializes this value.
    /// </summary>
    public readonly struct MatchStateSnapshot : IEquatable<MatchStateSnapshot>
    {
        public MatchStateSnapshot(
            MatchState state,
            double elapsedSeconds,
            double capturedTimestamp,
            Guid clockEpoch)
        {
            if (!MatchStateMachine.IsDefined(state))
            {
                throw new ArgumentOutOfRangeException(nameof(state));
            }

            if (!MatchTimestamp.IsValidSeconds(elapsedSeconds))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(elapsedSeconds),
                    "Elapsed seconds must be finite and non-negative.");
            }

            if (!MatchTimestamp.IsValidSeconds(capturedTimestamp))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(capturedTimestamp),
                    "Captured timestamp must be finite and non-negative.");
            }

            if (elapsedSeconds > capturedTimestamp)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(elapsedSeconds),
                    "Elapsed seconds cannot exceed the captured clock timestamp.");
            }

            if (clockEpoch == Guid.Empty)
            {
                throw new ArgumentException("Clock epoch cannot be empty.", nameof(clockEpoch));
            }

            State = state;
            ElapsedSeconds = elapsedSeconds;
            CapturedTimestamp = capturedTimestamp;
            ClockEpoch = clockEpoch;
        }

        public MatchState State { get; }
        public double ElapsedSeconds { get; }
        public double CapturedTimestamp { get; }
        public Guid ClockEpoch { get; }

        public bool IsValid =>
            MatchStateMachine.IsDefined(State) &&
            MatchTimestamp.IsValidSeconds(ElapsedSeconds) &&
            MatchTimestamp.IsValidSeconds(CapturedTimestamp) &&
            ElapsedSeconds <= CapturedTimestamp &&
            ClockEpoch != Guid.Empty;

        public bool Equals(MatchStateSnapshot other)
        {
            return State == other.State &&
                   ElapsedSeconds.Equals(other.ElapsedSeconds) &&
                   CapturedTimestamp.Equals(other.CapturedTimestamp) &&
                   ClockEpoch == other.ClockEpoch;
        }

        public override bool Equals(object obj)
        {
            return obj is MatchStateSnapshot other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hashCode = (int)State;
                hashCode = (hashCode * 397) ^ ElapsedSeconds.GetHashCode();
                hashCode = (hashCode * 397) ^ CapturedTimestamp.GetHashCode();
                hashCode = (hashCode * 397) ^ ClockEpoch.GetHashCode();
                return hashCode;
            }
        }

        public static bool operator ==(MatchStateSnapshot left, MatchStateSnapshot right) =>
            left.Equals(right);

        public static bool operator !=(MatchStateSnapshot left, MatchStateSnapshot right) =>
            !left.Equals(right);
    }
}
