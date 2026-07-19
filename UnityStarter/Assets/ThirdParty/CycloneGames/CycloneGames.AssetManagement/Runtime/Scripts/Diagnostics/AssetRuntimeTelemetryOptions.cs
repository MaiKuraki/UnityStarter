using System;

namespace CycloneGames.AssetManagement.Runtime
{
    /// <summary>
    /// Controls bounded runtime asset telemetry sampling.
    /// </summary>
    public readonly struct AssetRuntimeTelemetryOptions
    {
        public const int DEFAULT_CAPACITY = 256;
        public const int MAX_CAPACITY = 65_536;

        public readonly int Capacity;
        public readonly long MinimumSampleIntervalTicks;
        public readonly bool IncludeZeroActivitySamples;

        public AssetRuntimeTelemetryOptions(
            int capacity = DEFAULT_CAPACITY,
            TimeSpan minimumSampleInterval = default,
            bool includeZeroActivitySamples = true)
        {
            if (capacity <= 0 || capacity > MAX_CAPACITY)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(capacity),
                    $"Telemetry capacity must be between 1 and {MAX_CAPACITY}.");
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
