using System.Diagnostics;

namespace CycloneGames.Scheduling.Core
{
    internal sealed class StopwatchSchedulerClock : ISchedulerClock
    {
        public static readonly StopwatchSchedulerClock Instance = new StopwatchSchedulerClock();

        private StopwatchSchedulerClock()
        {
        }

        public long GetTimestamp()
        {
            return Stopwatch.GetTimestamp();
        }

        public double ToMilliseconds(long ticks)
        {
            return ticks * 1000.0 / Stopwatch.Frequency;
        }
    }
}
