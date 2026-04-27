using System.Diagnostics;

namespace CycloneGames.GameplayAbilities.Networking
{
    public sealed class StopwatchGASNetTimeProvider : IGASNetTimeProvider
    {
        public static readonly StopwatchGASNetTimeProvider Instance = new StopwatchGASNetTimeProvider();

        private readonly Stopwatch _stopwatch;

        private StopwatchGASNetTimeProvider()
        {
            _stopwatch = Stopwatch.StartNew();
        }

        public double CurrentTimeSeconds => _stopwatch.Elapsed.TotalSeconds;
    }
}
