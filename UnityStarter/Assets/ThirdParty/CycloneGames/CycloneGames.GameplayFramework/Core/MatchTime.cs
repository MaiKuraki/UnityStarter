using System;

namespace CycloneGames.GameplayFramework.Core
{
    /// <summary>
    /// A monotonic timestamp bound to one explicit clock epoch.
    /// </summary>
    public readonly struct MatchTimestamp : IEquatable<MatchTimestamp>
    {
        public MatchTimestamp(Guid clockEpoch, double seconds)
        {
            if (clockEpoch == Guid.Empty)
            {
                throw new ArgumentException("Clock epoch cannot be empty.", nameof(clockEpoch));
            }

            if (!IsValidSeconds(seconds))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(seconds),
                    "Timestamp seconds must be finite and non-negative.");
            }

            ClockEpoch = clockEpoch;
            Seconds = seconds;
        }

        public Guid ClockEpoch { get; }
        public double Seconds { get; }

        public bool IsValid =>
            ClockEpoch != Guid.Empty && IsValidSeconds(Seconds);

        public bool Equals(MatchTimestamp other)
        {
            return ClockEpoch == other.ClockEpoch && Seconds.Equals(other.Seconds);
        }

        public override bool Equals(object obj)
        {
            return obj is MatchTimestamp other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return (ClockEpoch.GetHashCode() * 397) ^ Seconds.GetHashCode();
            }
        }

        public static bool operator ==(MatchTimestamp left, MatchTimestamp right) =>
            left.Equals(right);

        public static bool operator !=(MatchTimestamp left, MatchTimestamp right) =>
            !left.Equals(right);

        internal static bool IsValidSeconds(double seconds)
        {
            return seconds >= 0d && !double.IsNaN(seconds) && !double.IsInfinity(seconds);
        }
    }

    /// <summary>
    /// Supplies a monotonic match timestamp. Implementations own epoch creation and persistence.
    /// </summary>
    public interface IMatchClock
    {
        MatchTimestamp CurrentTimestamp { get; }
    }
}
