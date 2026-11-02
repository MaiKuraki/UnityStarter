using System;
using System.Diagnostics;

namespace CycloneGames.Scheduling.Core
{
    internal sealed class StopwatchSchedulerClock : ISchedulerClock
    {
        public static readonly StopwatchSchedulerClock Instance = new StopwatchSchedulerClock();

        // Frequency is constant for the process lifetime, so both conversions are prepared once.
        private readonly double _millisecondsPerTick;
        private readonly double _ticksPerMillisecond;

        private StopwatchSchedulerClock()
        {
            long frequency = Stopwatch.Frequency;
            _millisecondsPerTick = 1000.0 / frequency;
            _ticksPerMillisecond = frequency / 1000.0;
        }

        /// <summary>Smallest interval this clock can resolve, in milliseconds.</summary>
        public double ResolutionMilliseconds
        {
            get { return _millisecondsPerTick; }
        }

        public long GetTimestamp()
        {
            return Stopwatch.GetTimestamp();
        }

        public double ToMilliseconds(long ticks)
        {
            return ticks * _millisecondsPerTick;
        }

        public long ToTicks(double milliseconds)
        {
            if (milliseconds <= 0)
            {
                return 0;
            }

            double ticks = milliseconds * _ticksPerMillisecond;
            if (ticks >= long.MaxValue)
            {
                return long.MaxValue;
            }

            return (long)Math.Ceiling(ticks);
        }
    }
}
