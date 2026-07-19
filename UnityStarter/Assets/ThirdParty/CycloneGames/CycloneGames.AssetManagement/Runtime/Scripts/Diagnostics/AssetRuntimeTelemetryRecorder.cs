using System;
using System.Diagnostics;

namespace CycloneGames.AssetManagement.Runtime
{
    /// <summary>
    /// Caller-owned, bounded recorder for runtime asset telemetry snapshots.
    /// Recording does not allocate after construction; export paths can choose their own scratch buffers.
    /// </summary>
    public sealed class AssetRuntimeTelemetryRecorder
    {
        private readonly object _gate = new object();
        private readonly AssetRuntimeTelemetrySample[] _samples;
        private readonly AssetRuntimeTelemetryOptions _options;
        private readonly Func<long> _monotonicTimestampProvider;
        private readonly long _minimumSampleIntervalMonotonicTicks;

        private int _count;
        private int _nextWriteIndex;
        private long _lastMonotonicTimestamp;
        private bool _hasRecordedTimestamp;
        private bool _hasActivityBaseline;
        private AssetRuntimeCacheSnapshot _activityBaseline;
        private long _nextSequence = 1L;
        private long _totalRecorded;

        public AssetRuntimeTelemetryRecorder(AssetRuntimeTelemetryOptions options = default)
            : this(options, Stopwatch.GetTimestamp, Stopwatch.Frequency)
        {
        }

        internal AssetRuntimeTelemetryRecorder(
            AssetRuntimeTelemetryOptions options,
            Func<long> monotonicTimestampProvider,
            long monotonicFrequency)
        {
            if (options.Capacity <= 0)
            {
                options = AssetRuntimeTelemetryOptions.Default;
            }

            _options = options;
            _samples = new AssetRuntimeTelemetrySample[options.Capacity];
            _monotonicTimestampProvider = monotonicTimestampProvider ?? throw new ArgumentNullException(
                nameof(monotonicTimestampProvider));
            if (monotonicFrequency <= 0L)
            {
                throw new ArgumentOutOfRangeException(nameof(monotonicFrequency));
            }

            _minimumSampleIntervalMonotonicTicks = options.MinimumSampleIntervalTicks <= 0L
                ? 0L
                : Math.Max(
                    1L,
                    (long)Math.Ceiling(
                        options.MinimumSampleIntervalTicks *
                        (double)monotonicFrequency /
                        TimeSpan.TicksPerSecond));
        }

        public int Capacity => _samples.Length;

        public AssetRuntimeTelemetryOptions Options => _options;

        public int Count
        {
            get
            {
                lock (_gate)
                {
                    return _count;
                }
            }
        }

        public long TotalRecorded
        {
            get
            {
                lock (_gate)
                {
                    return _totalRecorded;
                }
            }
        }

        public long OverwrittenSampleCount
        {
            get
            {
                lock (_gate)
                {
                    long overwritten = _totalRecorded - _samples.Length;
                    return overwritten > 0L ? overwritten : 0L;
                }
            }
        }

        public bool TryRecord(IAssetRuntimeDiagnostics diagnostics, long timestampUtcTicks = 0L)
        {
            if (diagnostics == null)
            {
                throw new ArgumentNullException(nameof(diagnostics));
            }

            AssetRuntimeCacheSnapshot snapshot = diagnostics.GetRuntimeCacheSnapshot();
            return TryRecord(snapshot, timestampUtcTicks);
        }

        public bool TryRecord(AssetRuntimeCacheSnapshot snapshot, long timestampUtcTicks = 0L)
        {
            if (timestampUtcTicks == 0L)
            {
                timestampUtcTicks = DateTime.UtcNow.Ticks;
            }

            lock (_gate)
            {
                long monotonicTimestamp = _monotonicTimestampProvider();
                if (!CanRecordAt(monotonicTimestamp))
                {
                    return false;
                }

                if (!_options.IncludeZeroActivitySamples)
                {
                    bool hasActivity = HasIntervalActivity(snapshot);
                    _activityBaseline = snapshot;
                    _hasActivityBaseline = true;
                    if (!hasActivity)
                    {
                        return false;
                    }
                }

                var sample = new AssetRuntimeTelemetrySample(_nextSequence, timestampUtcTicks, snapshot);
                _samples[_nextWriteIndex] = sample;
                _nextSequence++;
                _totalRecorded++;
                _lastMonotonicTimestamp = monotonicTimestamp;
                _hasRecordedTimestamp = true;

                _nextWriteIndex++;
                if (_nextWriteIndex == _samples.Length)
                {
                    _nextWriteIndex = 0;
                }

                if (_count < _samples.Length)
                {
                    _count++;
                }

                return true;
            }
        }

        public int CopyTo(AssetRuntimeTelemetrySample[] destination)
        {
            if (destination == null)
            {
                throw new ArgumentNullException(nameof(destination));
            }

            lock (_gate)
            {
                int copyCount = _count <= destination.Length ? _count : destination.Length;
                int skipCount = _count - copyCount;
                int oldestIndex = _count == _samples.Length ? _nextWriteIndex : 0;

                for (int i = 0; i < copyCount; i++)
                {
                    int sourceIndex = oldestIndex + skipCount + i;
                    if (sourceIndex >= _samples.Length)
                    {
                        sourceIndex -= _samples.Length;
                    }

                    destination[i] = _samples[sourceIndex];
                }

                return copyCount;
            }
        }

        public bool TryGetLatest(out AssetRuntimeTelemetrySample sample)
        {
            lock (_gate)
            {
                if (_count == 0)
                {
                    sample = default;
                    return false;
                }

                int latestIndex = _nextWriteIndex - 1;
                if (latestIndex < 0)
                {
                    latestIndex = _samples.Length - 1;
                }

                sample = _samples[latestIndex];
                return true;
            }
        }

        public void Clear()
        {
            lock (_gate)
            {
                Array.Clear(_samples, 0, _samples.Length);
                _count = 0;
                _nextWriteIndex = 0;
                _lastMonotonicTimestamp = 0L;
                _hasRecordedTimestamp = false;
                _hasActivityBaseline = false;
                _activityBaseline = default;
                _nextSequence = 1L;
                _totalRecorded = 0L;
            }
        }

        private bool CanRecordAt(long monotonicTimestamp)
        {
            if (_minimumSampleIntervalMonotonicTicks <= 0L || !_hasRecordedTimestamp)
            {
                return true;
            }

            long elapsed = monotonicTimestamp - _lastMonotonicTimestamp;
            return elapsed >= _minimumSampleIntervalMonotonicTicks || elapsed < 0L;
        }

        private bool HasIntervalActivity(AssetRuntimeCacheSnapshot snapshot)
        {
            if (snapshot.ActiveCount != 0 || snapshot.IdleCount != 0 || snapshot.IdleBytesApprox != 0L)
            {
                return true;
            }

            if (!_hasActivityBaseline)
            {
                // The first observation uses a zero counter baseline. Static configuration such as the
                // idle-byte budget does not make an otherwise idle first observation active.
                return HasNonZeroCumulativeCounter(snapshot);
            }

            AssetRuntimeCacheSnapshot previous = _activityBaseline;
            return !string.Equals(snapshot.PackageName, previous.PackageName, StringComparison.Ordinal)
                || !string.Equals(snapshot.ProviderName, previous.ProviderName, StringComparison.Ordinal)
                || snapshot.ActiveCount != previous.ActiveCount
                || snapshot.IdleCount != previous.IdleCount
                || snapshot.IdleBytesApprox != previous.IdleBytesApprox
                || snapshot.IdleBytesBudget != previous.IdleBytesBudget
                || snapshot.ActiveHitCount != previous.ActiveHitCount
                || snapshot.IdleHitCount != previous.IdleHitCount
                || snapshot.CacheMissCount != previous.CacheMissCount
                || snapshot.IdleAdmissionCount != previous.IdleAdmissionCount
                || snapshot.FailedOperationRejectionCount != previous.FailedOperationRejectionCount
                || snapshot.MetadataOverflowRejectionCount != previous.MetadataOverflowRejectionCount
                || snapshot.UnknownFootprintRejectionCount != previous.UnknownFootprintRejectionCount
                || snapshot.OversizeRejectionCount != previous.OversizeRejectionCount
                || snapshot.FootprintEstimationFailureCount != previous.FootprintEstimationFailureCount
                || snapshot.EvictionCount != previous.EvictionCount
                || snapshot.CapacityEvictionCount != previous.CapacityEvictionCount
                || snapshot.MemoryBudgetEvictionCount != previous.MemoryBudgetEvictionCount
                || snapshot.RetentionEvictionCount != previous.RetentionEvictionCount
                || snapshot.ExplicitEvictionCount != previous.ExplicitEvictionCount
                || snapshot.EvictedBytesApprox != previous.EvictedBytesApprox
                || snapshot.ProviderReleaseFailureCount != previous.ProviderReleaseFailureCount
                || snapshot.PeakActiveCount != previous.PeakActiveCount
                || snapshot.PeakIdleCount != previous.PeakIdleCount
                || snapshot.PeakIdleBytesApprox != previous.PeakIdleBytesApprox;
        }

        private static bool HasNonZeroCumulativeCounter(AssetRuntimeCacheSnapshot snapshot)
        {
            return snapshot.ActiveHitCount != 0L
                || snapshot.IdleHitCount != 0L
                || snapshot.CacheMissCount != 0L
                || snapshot.IdleAdmissionCount != 0L
                || snapshot.FailedOperationRejectionCount != 0L
                || snapshot.MetadataOverflowRejectionCount != 0L
                || snapshot.UnknownFootprintRejectionCount != 0L
                || snapshot.OversizeRejectionCount != 0L
                || snapshot.FootprintEstimationFailureCount != 0L
                || snapshot.EvictionCount != 0L
                || snapshot.CapacityEvictionCount != 0L
                || snapshot.MemoryBudgetEvictionCount != 0L
                || snapshot.RetentionEvictionCount != 0L
                || snapshot.ExplicitEvictionCount != 0L
                || snapshot.EvictedBytesApprox != 0L
                || snapshot.ProviderReleaseFailureCount != 0L
                || snapshot.PeakActiveCount != 0
                || snapshot.PeakIdleCount != 0
                || snapshot.PeakIdleBytesApprox != 0L;
        }
    }
}
