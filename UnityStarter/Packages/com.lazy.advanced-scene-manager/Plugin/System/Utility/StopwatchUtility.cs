using System;
using System.Diagnostics;

namespace AdvancedSceneManager.Utility
{

    /// <summary>Provides utility functions for working with stopwatches.</summary>
    public static class StopwatchUtility
    {
        private const long TicksPerMillisecond = 10000;
        private const long TicksPerSecond = TicksPerMillisecond * 1000;
        private static readonly long Frequency = Stopwatch.Frequency;
        private static readonly double s_tickFrequency = (double)TicksPerSecond / Frequency;


        public static TimeSpan GetElapsedTime(long startingTimestamp) =>
            GetElapsedTime(startingTimestamp, Stopwatch.GetTimestamp());

        private static TimeSpan GetElapsedTime(long startingTimestamp, long endingTimestamp) =>
            new((long)((endingTimestamp - startingTimestamp) * s_tickFrequency));
    }

}
