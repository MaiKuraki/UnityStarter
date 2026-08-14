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
            new UnityMatchClock(useUnscaledTime: false);
        private static readonly UnityMatchClock UnscaledInstance =
            new UnityMatchClock(useUnscaledTime: true);

        private readonly Guid clockEpoch;
        private readonly bool useUnscaledTime;

        private UnityMatchClock(bool useUnscaledTime)
        {
            this.useUnscaledTime = useUnscaledTime;
            clockEpoch = Guid.NewGuid();
        }

        public static IMatchClock Scaled => ScaledInstance;
        public static IMatchClock Unscaled => UnscaledInstance;

        public MatchTimestamp CurrentTimestamp => new MatchTimestamp(
            clockEpoch,
            useUnscaledTime ? Time.unscaledTimeAsDouble : Time.timeAsDouble);
    }
}
