using System;

namespace CycloneGames.AssetManagement.Runtime
{
    /// <summary>
    /// Immutable runtime telemetry sample captured from an aggregate asset cache snapshot.
    /// </summary>
    public readonly struct AssetRuntimeTelemetrySample
    {
        public readonly long Sequence;
        public readonly long TimestampUtcTicks;
        public readonly AssetRuntimeCacheSnapshot Snapshot;

        public AssetRuntimeTelemetrySample(long sequence, long timestampUtcTicks, AssetRuntimeCacheSnapshot snapshot)
        {
            Sequence = sequence;
            TimestampUtcTicks = timestampUtcTicks;
            Snapshot = snapshot;
        }

        public DateTime TimestampUtc => new DateTime(TimestampUtcTicks, DateTimeKind.Utc);
    }
}
