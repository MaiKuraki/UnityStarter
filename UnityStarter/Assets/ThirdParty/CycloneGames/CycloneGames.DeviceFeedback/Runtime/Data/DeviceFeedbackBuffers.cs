using System;

namespace CycloneGames.DeviceFeedback.Runtime
{
    /// <summary>
    /// Cache of exact-length arrays for JNI / native interop. Platform waveform
    /// APIs require an array whose Length equals the sample count. Lookup is a
    /// last-hit check followed by a bounded linear scan; entries and byte budget
    /// are fixed by DeviceFeedbackProfile. Lengths above the cacheable ceiling
    /// bypass the cache so a single call cannot retain a large array.
    /// </summary>
    internal sealed class ExactLengthArrayCache
    {
        private sealed class Entry
        {
            public int Length;
            public long[] Timings;
            public int[] Amplitudes;
            public long[] OnOff;
            public long LastUse;
            public long Bytes;
        }

        private readonly Entry[] _entries;
        private readonly int _cacheBudgetBytes;
        private readonly int _cacheableMaximumLength;
        private Entry _lastHit;
        private int _count;
        private long _bytes;
        private long _tick;

        public ExactLengthArrayCache(in DeviceFeedbackProfile profile)
        {
            _entries = new Entry[profile.ExactArrayCacheMaxEntries];
            _cacheBudgetBytes = profile.ExactArrayCacheBudgetBytes;
            _cacheableMaximumLength = profile.CacheableMaximumLength;
        }

        public long AllocationCount { get; private set; }
        public long ReuseCount { get; private set; }
        public int Count => _count;

        public void Get(int length, out long[] timings, out int[] amplitudes, out long[] onOff)
        {
            _tick++;

            // Alternating sample depths hit this path without scanning the entries.
            Entry last = _lastHit;
            if (last != null && last.Length == length)
            {
                last.LastUse = _tick;
                timings = last.Timings;
                amplitudes = last.Amplitudes;
                onOff = last.OnOff;
                ReuseCount++;
                return;
            }

            for (int i = 0; i < _count; i++)
            {
                Entry existing = _entries[i];
                if (existing.Length == length)
                {
                    existing.LastUse = _tick;
                    _lastHit = existing;
                    timings = existing.Timings;
                    amplitudes = existing.Amplitudes;
                    onOff = existing.OnOff;
                    ReuseCount++;
                    return;
                }
            }

            var newTimings = new long[length];
            var newAmplitudes = new int[length];
            var newOnOff = new long[length];
            AllocationCount++;

            if (length > _cacheableMaximumLength)
            {
                // Not cached: never pin a large array through _lastHit.
                timings = newTimings;
                amplitudes = newAmplitudes;
                onOff = newOnOff;
                return;
            }

            long entryBytes = (long)length * (sizeof(long) + sizeof(int) + sizeof(long));
            EvictUntil(entryBytes);

            var entry = new Entry
            {
                Length = length,
                Timings = newTimings,
                Amplitudes = newAmplitudes,
                OnOff = newOnOff,
                LastUse = _tick,
                Bytes = entryBytes
            };

            if (_count < _entries.Length)
            {
                _entries[_count++] = entry;
            }
            else
            {
                int replace = LeastRecentlyUsedIndex();
                _bytes -= _entries[replace].Bytes;
                _entries[replace] = entry;
            }

            _bytes += entryBytes;
            _lastHit = entry;
            PruneLastHit();
            timings = newTimings;
            amplitudes = newAmplitudes;
            onOff = newOnOff;
        }

        public void Clear()
        {
            for (int i = 0; i < _count; i++) _entries[i] = null;
            _count = 0;
            _bytes = 0L;
            _lastHit = null;
        }

        private void EvictUntil(long incomingBytes)
        {
            while (_count > 0 && _bytes + incomingBytes > _cacheBudgetBytes)
            {
                int index = LeastRecentlyUsedIndex();
                _bytes -= _entries[index].Bytes;
                _entries[index] = _entries[_count - 1];
                _entries[_count - 1] = null;
                _count--;
            }
        }

        private void PruneLastHit()
        {
            if (_lastHit == null) return;
            for (int i = 0; i < _count; i++)
            {
                if (ReferenceEquals(_entries[i], _lastHit)) return;
            }
            _lastHit = null;
        }

        private int LeastRecentlyUsedIndex()
        {
            int index = 0;
            long oldest = _entries[0].LastUse;
            for (int i = 1; i < _count; i++)
            {
                if (_entries[i].LastUse < oldest)
                {
                    oldest = _entries[i].LastUse;
                    index = i;
                }
            }
            return index;
        }
    }

    /// <summary>
    /// Instance-owned struct-of-arrays workspace. The owning service disposes
    /// the lease; no other service, thread or static field can replace its arrays.
    /// </summary>
    internal sealed class DeviceFeedbackBufferLease : IDisposable
    {
        public long[] Timing = Array.Empty<long>();
        public int[] Amplitude = Array.Empty<int>();
        public float[] FloatTime = Array.Empty<float>();
        public float[] Intensity = Array.Empty<float>();
        public float[] Sharpness = Array.Empty<float>();
        public int[] Type = Array.Empty<int>();
        public float[] Duration = Array.Empty<float>();
        public long[] Build = Array.Empty<long>();

        private readonly ExactLengthArrayCache _exact;
        private int _capacity;
        private long _retainedBytes;

        public DeviceFeedbackBufferLease(in DeviceFeedbackProfile profile)
        {
            _exact = new ExactLengthArrayCache(in profile);
        }

        public int Capacity => _capacity;
        public long GrowthCount { get; private set; }
        public long ExactArrayAllocationCount => _exact.AllocationCount;
        public long ExactArrayReuseCount => _exact.ReuseCount;
        public int ExactArrayCacheEntries => _exact.Count;
        public long RetainedElementBytes => _retainedBytes;

        /// <summary>Ensures the arrays can hold count samples. Growth is amortized.</summary>
        public void Ensure(int count)
        {
            if (count < 1 || count > DeviceFeedbackConstants.MaximumHardWaveformSampleCount)
            {
                throw new ArgumentOutOfRangeException(nameof(count));
            }

            if (_capacity >= count) return;

            int capacity = 64;
            while (capacity < count && capacity < DeviceFeedbackConstants.MaximumHardWaveformSampleCount)
            {
                capacity <<= 1;
            }
            if (capacity < count) capacity = count;

            Timing = new long[capacity];
            Amplitude = new int[capacity];
            FloatTime = new float[capacity];
            Intensity = new float[capacity];
            Sharpness = new float[capacity];
            Type = new int[capacity];
            Duration = new float[capacity];
            Build = new long[capacity * 2 + 1];
            _capacity = capacity;
            GrowthCount++;
            RecalculateRetainedBytes();
        }

        /// <summary>Returns arrays of exactly length. Repeated lengths are reused.</summary>
        public void GetExactArrays(int length, out long[] timings, out int[] amplitudes, out long[] onOff)
        {
            _exact.Get(length, out timings, out amplitudes, out onOff);
        }


        public DeviceFeedbackBufferStats GetStats()
        {
            return new DeviceFeedbackBufferStats(
                _capacity,
                GrowthCount,
                _exact.AllocationCount,
                _exact.ReuseCount,
                _exact.Count,
                _retainedBytes);
        }

        public void Dispose()
        {
            Timing = Array.Empty<long>();
            Amplitude = Array.Empty<int>();
            FloatTime = Array.Empty<float>();
            Intensity = Array.Empty<float>();
            Sharpness = Array.Empty<float>();
            Type = Array.Empty<int>();
            Duration = Array.Empty<float>();
            Build = Array.Empty<long>();
            _exact.Clear();
            _capacity = 0;
            _retainedBytes = 0L;
        }

        private void RecalculateRetainedBytes()
        {
            long bytes =
                Timing.LongLength * sizeof(long) +
                Amplitude.LongLength * sizeof(int) +
                FloatTime.LongLength * sizeof(float) +
                Intensity.LongLength * sizeof(float) +
                Sharpness.LongLength * sizeof(float) +
                Type.LongLength * sizeof(int) +
                Duration.LongLength * sizeof(float) +
                Build.LongLength * sizeof(long);
            _retainedBytes = bytes;
            DeviceFeedbackDiagnostics.SetRetainedBufferElementBytes(bytes);
        }
    }
}
