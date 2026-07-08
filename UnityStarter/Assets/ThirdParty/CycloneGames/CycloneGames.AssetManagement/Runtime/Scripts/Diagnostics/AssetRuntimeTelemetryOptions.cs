using System;

namespace CycloneGames.AssetManagement.Runtime
{
    /// <summary>
    /// Controls bounded runtime asset telemetry sampling.
    /// </summary>
    public readonly struct AssetRuntimeTelemetryOptions
    {
        public const int DEFAULT_CAPACITY = 256;

        public readonly int Capacity;
        public readonly long MinimumSampleIntervalTicks;
        public readonly bool IncludeZeroActivitySamples;

        public AssetRuntimeTelemetryOptions(
            int capacity = DEFAULT_CAPACITY,
            TimeSpan minimumSampleInterval = default,
            bool includeZeroActivitySamples = true)
        {
            if (capacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity), "Telemetry capacity must be greater than zero.");
            }

            if (minimumSampleInterval.Ticks < 0L)
            {
                throw new ArgumentOutOfRangeException(nameof(minimumSampleInterval), "Telemetry sample interval cannot be negative.");
            }

            Capacity = capacity;
            MinimumSampleIntervalTicks = minimumSampleInterval.Ticks;
            IncludeZeroActivitySamples = includeZeroActivitySamples;
        }

        public static AssetRuntimeTelemetryOptions Default => new AssetRuntimeTelemetryOptions();
    }
}
