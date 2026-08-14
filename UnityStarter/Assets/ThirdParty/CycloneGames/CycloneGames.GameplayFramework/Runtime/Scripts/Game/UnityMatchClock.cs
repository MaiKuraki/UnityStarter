using System;
using CycloneGames.GameplayFramework.Core;
using UnityEngine;

namespace CycloneGames.GameplayFramework.Runtime
{
    /// <summary>
    /// Unity-backed match clocks. Scaled and unscaled clocks use independent process-local epochs
    /// so their timestamps cannot be combined or restored across a domain reload by accident.
    /// </summary>
    public sealed class UnityMatchClock : IMatchClock
    {
        private static readonly UnityMatchClock ScaledInstance =
            new UnityMatchClock(useUnscaledTime: false, Guid.NewGuid());
        private static readonly UnityMatchClock UnscaledInstance =
            new UnityMatchClock(useUnscaledTime: true, Guid.NewGuid());

        private readonly Guid clockEpoch;
        private readonly bool useUnscaledTime;

        private UnityMatchClock(bool useUnscaledTime, Guid clockEpoch)
        {
            this.useUnscaledTime = useUnscaledTime;
            this.clockEpoch = clockEpoch;
        }

        public static IMatchClock Scaled => ScaledInstance;
        public static IMatchClock Unscaled => UnscaledInstance;

        public Guid ClockEpoch => clockEpoch;

        /// <summary>
        /// Creates a clock bound to a previously persisted epoch so snapshots captured in an
        /// earlier process or domain can be restored. The seconds domain remains
        /// <see cref="Time.timeAsDouble"/> or <see cref="Time.unscaledTimeAsDouble"/>, which is
        /// process-local; adapters that restore across a full restart must also supply a seconds
        /// domain consistent with the snapshot (for example through a custom IMatchClock with a
        /// persisted wall-clock offset) so the restore timestamp does not precede the snapshot.
        /// </summary>
        public static UnityMatchClock WithEpoch(Guid clockEpoch, bool useUnscaledTime = false)
        {
            if (clockEpoch == Guid.Empty)
            {
                throw new ArgumentException("Clock epoch cannot be empty.", nameof(clockEpoch));
            }

            return new UnityMatchClock(useUnscaledTime, clockEpoch);
        }

        public MatchTimestamp CurrentTimestamp => new MatchTimestamp(
            clockEpoch,
            useUnscaledTime ? Time.unscaledTimeAsDouble : Time.timeAsDouble);
    }
}
