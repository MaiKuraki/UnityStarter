using System;

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

        private int _count;
        private int _nextWriteIndex;
        private long _lastTimestampUtcTicks;
        private long _nextSequence = 1L;
        private long _totalRecorded;

        public AssetRuntimeTelemetryRecorder(AssetRuntimeTelemetryOptions options = default)
        {
            if (options.Capacity <= 0)
            {
                options = AssetRuntimeTelemetryOptions.Default;
            }

            _options = options;
            _samples = new AssetRuntimeTelemetrySample[options.Capacity];
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
            if (!_options.IncludeZeroActivitySamples && IsZeroActivity(snapshot))
            {
                return false;
            }

            if (timestampUtcTicks == 0L)
            {
                timestampUtcTicks = DateTime.UtcNow.Ticks;
            }

            lock (_gate)
            {
                if (!CanRecordAt(timestampUtcTicks))
                {
                    return false;
                }

                var sample = new AssetRuntimeTelemetrySample(_nextSequence, timestampUtcTicks, snapshot);
                _samples[_nextWriteIndex] = sample;
                _nextSequence++;
                _totalRecorded++;
                _lastTimestampUtcTicks = timestampUtcTicks;

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
                _lastTimestampUtcTicks = 0L;
                _nextSequence = 1L;
                _totalRecorded = 0L;
            }
        }

        private bool CanRecordAt(long timestampUtcTicks)
        {
            long minimumInterval = _options.MinimumSampleIntervalTicks;
            if (minimumInterval <= 0L || _lastTimestampUtcTicks == 0L)
            {
                return true;
            }

            return timestampUtcTicks - _lastTimestampUtcTicks >= minimumInterval;
        }

        private static bool IsZeroActivity(AssetRuntimeCacheSnapshot snapshot)
        {
            return snapshot.ActiveCount == 0
                && snapshot.IdleCount == 0
                && snapshot.IdleBytesApprox == 0L;
        }
    }
}
