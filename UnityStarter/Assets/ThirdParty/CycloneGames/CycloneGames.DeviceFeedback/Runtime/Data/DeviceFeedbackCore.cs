using System;
using System.Threading;

namespace CycloneGames.DeviceFeedback.Runtime
{
    /// <summary>
    /// Structural hard ceilings. Tunable policies are on DeviceFeedbackProfile;
    /// per-call budgets are on DeviceFeedbackLimits.
    /// </summary>
    public static class DeviceFeedbackConstants
    {
        public const int MaximumHardWaveformSampleCount = 65_536;
        public const int MaximumHardHapticEventCount = 16_384;
        public const int MaximumHardPatternSegmentCount = 65_536;
    }

    public enum FeedbackStatus : byte
    {
        Accepted = 0,
        RejectedInvalid = 1,
        RejectedCapacity = 2,
        RejectedInactive = 3,
        RejectedUnavailable = 4,
        RejectedDisposed = 5,
        RejectedUnsupported = 6,
        RejectedNativeFailure = 7,
        Degraded = 8,
        RejectedThreadAffinity = 9,
        Queued = 10
    }

    /// <summary>
    /// Outcome of a feedback operation. Void methods publish the result through
    /// <c>LastResult</c>; <c>*Checked</c> methods return it directly.
    /// </summary>
    public readonly struct FeedbackResult
    {
        public FeedbackResult(
            FeedbackStatus status,
            long durationMilliseconds = 0L,
            int waveformSampleCount = 0,
            int hapticEventCount = 0,
            int patternSegmentCount = 0)
        {
            Status = status;
            DurationMilliseconds = durationMilliseconds;
            WaveformSampleCount = waveformSampleCount;
            HapticEventCount = hapticEventCount;
            PatternSegmentCount = patternSegmentCount;
        }

        public FeedbackStatus Status { get; }
        public long DurationMilliseconds { get; }
        public int WaveformSampleCount { get; }
        public int HapticEventCount { get; }
        public int PatternSegmentCount { get; }

        public bool IsAccepted => Status == FeedbackStatus.Accepted || Status == FeedbackStatus.Degraded;
        public bool IsRejected => !IsAccepted;

        public static FeedbackResult Accepted(
            long durationMilliseconds,
            int waveformSampleCount = 0,
            int hapticEventCount = 0,
            int patternSegmentCount = 0)
        {
            return new FeedbackResult(
                FeedbackStatus.Accepted,
                durationMilliseconds,
                waveformSampleCount,
                hapticEventCount,
                patternSegmentCount);
        }

        public static FeedbackResult Degraded(
            long durationMilliseconds,
            int waveformSampleCount = 0,
            int hapticEventCount = 0,
            int patternSegmentCount = 0)
        {
            return new FeedbackResult(
                FeedbackStatus.Degraded,
                durationMilliseconds,
                waveformSampleCount,
                hapticEventCount,
                patternSegmentCount);
        }

        public static FeedbackResult Rejected(FeedbackStatus status)
        {
            return new FeedbackResult(status);
        }
    }

    /// <summary>
    /// Hardware capabilities of the active route. Callers can select a
    /// fallback when a capability is unavailable.
    /// </summary>
    public readonly struct DeviceCapability
    {
        public DeviceCapability(
            bool supportsHaptics,
            bool supportsAmplitude,
            bool supportsSharpness,
            bool supportsCoreHaptics,
            bool supportsPattern,
            bool supportsContinuousModulation,
            int platformApiLevel)
        {
            SupportsHaptics = supportsHaptics;
            SupportsAmplitude = supportsAmplitude;
            SupportsSharpness = supportsSharpness;
            SupportsCoreHaptics = supportsCoreHaptics;
            SupportsPattern = supportsPattern;
            SupportsContinuousModulation = supportsContinuousModulation;
            PlatformApiLevel = platformApiLevel;
        }

        public bool SupportsHaptics { get; }
        public bool SupportsAmplitude { get; }
        public bool SupportsSharpness { get; }
        public bool SupportsCoreHaptics { get; }
        public bool SupportsPattern { get; }
        public bool SupportsContinuousModulation { get; }
        public int PlatformApiLevel { get; }

        public static DeviceCapability None => default(DeviceCapability);
    }

    public readonly struct DeviceFeedbackLimits
    {
        public const int DefaultMaximumDurationMilliseconds = 300_000;
        public const int DefaultMaximumWaveformSampleCount = 4_096;
        public const int DefaultMaximumHapticEventCount = 2_048;
        public const int DefaultMaximumPatternSegmentCount = 4_096;

        public const int HardMaximumDurationMilliseconds = 3_600_000;
        public const int HardMaximumWaveformSampleCount = DeviceFeedbackConstants.MaximumHardWaveformSampleCount;
        public const int HardMaximumHapticEventCount = DeviceFeedbackConstants.MaximumHardHapticEventCount;
        public const int HardMaximumPatternSegmentCount = DeviceFeedbackConstants.MaximumHardPatternSegmentCount;

        public DeviceFeedbackLimits(
            int maximumDurationMilliseconds,
            int maximumWaveformSampleCount,
            int maximumHapticEventCount,
            int maximumPatternSegmentCount)
        {
            ValidateRange(maximumDurationMilliseconds, HardMaximumDurationMilliseconds, nameof(maximumDurationMilliseconds));
            ValidateRange(maximumWaveformSampleCount, HardMaximumWaveformSampleCount, nameof(maximumWaveformSampleCount));
            ValidateRange(maximumHapticEventCount, HardMaximumHapticEventCount, nameof(maximumHapticEventCount));
            ValidateRange(maximumPatternSegmentCount, HardMaximumPatternSegmentCount, nameof(maximumPatternSegmentCount));

            MaximumDurationMilliseconds = maximumDurationMilliseconds;
            MaximumWaveformSampleCount = maximumWaveformSampleCount;
            MaximumHapticEventCount = maximumHapticEventCount;
            MaximumPatternSegmentCount = maximumPatternSegmentCount;
        }

        public int MaximumDurationMilliseconds { get; }
        public int MaximumWaveformSampleCount { get; }
        public int MaximumHapticEventCount { get; }
        public int MaximumPatternSegmentCount { get; }
        private static readonly DeviceFeedbackLimits s_default = new DeviceFeedbackLimits(
            DefaultMaximumDurationMilliseconds,
            DefaultMaximumWaveformSampleCount,
            DefaultMaximumHapticEventCount,
            DefaultMaximumPatternSegmentCount);

        /// <summary>
        /// Shared immutable default.
        /// </summary>
        public static DeviceFeedbackLimits Default => s_default;

        internal DeviceFeedbackLimits Normalize()
        {
            return MaximumDurationMilliseconds > 0 &&
                   MaximumWaveformSampleCount > 0 &&
                   MaximumHapticEventCount > 0 &&
                   MaximumPatternSegmentCount > 0
                ? this
                : Default;
        }

        private static void ValidateRange(int value, int hardMaximum, string parameterName)
        {
            if (value < 1 || value > hardMaximum)
            {
                throw new ArgumentOutOfRangeException(
                    parameterName,
                    value,
                    "Value must be between 1 and the hard safety limit of " + hardMaximum + ".");
            }
        }
    }

    /// <summary>
    /// Observable state of an instance-owned buffer lease.
    /// </summary>
    public readonly struct DeviceFeedbackBufferStats
    {
        public DeviceFeedbackBufferStats(
            int capacity,
            long growthCount,
            long exactArrayAllocationCount,
            long exactArrayReuseCount,
            int exactArrayCacheEntries,
            long retainedElementBytes)
        {
            Capacity = capacity;
            GrowthCount = growthCount;
            ExactArrayAllocationCount = exactArrayAllocationCount;
            ExactArrayReuseCount = exactArrayReuseCount;
            ExactArrayCacheEntries = exactArrayCacheEntries;
            RetainedElementBytes = retainedElementBytes;
        }

        public int Capacity { get; }
        public long GrowthCount { get; }
        public long ExactArrayAllocationCount { get; }
        public long ExactArrayReuseCount { get; }
        public int ExactArrayCacheEntries { get; }
        public long RetainedElementBytes { get; }

        public static DeviceFeedbackBufferStats Empty => default(DeviceFeedbackBufferStats);
    }

    public readonly struct DeviceFeedbackMemoryStats
    {
        public DeviceFeedbackMemoryStats(
            long retainedBufferElementBytes,
            int peakWaveformSampleCount,
            int peakHapticEventCount,
            int peakPatternSegmentCount,
            long peakDurationMilliseconds,
            long acceptedOperationCount,
            long invalidRejectedOperationCount,
            long capacityRejectedOperationCount,
            long inactiveRejectedOperationCount,
            long unavailableRejectedOperationCount,
            long unsupportedRejectedOperationCount,
            long nativeFailureCount,
            long bufferGrowthCount,
            int exactArrayCacheEntries,
            long exactArrayAllocationCount,
            long exactArrayReuseCount)
        {
            RetainedBufferElementBytes = retainedBufferElementBytes;
            PeakWaveformSampleCount = peakWaveformSampleCount;
            PeakHapticEventCount = peakHapticEventCount;
            PeakPatternSegmentCount = peakPatternSegmentCount;
            PeakDurationMilliseconds = peakDurationMilliseconds;
            AcceptedOperationCount = acceptedOperationCount;
            InvalidRejectedOperationCount = invalidRejectedOperationCount;
            CapacityRejectedOperationCount = capacityRejectedOperationCount;
            InactiveRejectedOperationCount = inactiveRejectedOperationCount;
            UnavailableRejectedOperationCount = unavailableRejectedOperationCount;
            UnsupportedRejectedOperationCount = unsupportedRejectedOperationCount;
            NativeFailureCount = nativeFailureCount;
            BufferGrowthCount = bufferGrowthCount;
            ExactArrayCacheEntries = exactArrayCacheEntries;
            ExactArrayAllocationCount = exactArrayAllocationCount;
            ExactArrayReuseCount = exactArrayReuseCount;
        }

        public long RetainedBufferElementBytes { get; }
        public int PeakWaveformSampleCount { get; }
        public int PeakHapticEventCount { get; }
        public int PeakPatternSegmentCount { get; }
        public long PeakDurationMilliseconds { get; }
        public long AcceptedOperationCount { get; }
        public long InvalidRejectedOperationCount { get; }
        public long CapacityRejectedOperationCount { get; }
        public long InactiveRejectedOperationCount { get; }
        public long UnavailableRejectedOperationCount { get; }
        public long UnsupportedRejectedOperationCount { get; }
        public long NativeFailureCount { get; }
        public long BufferGrowthCount { get; }
        public int ExactArrayCacheEntries { get; }
        public long ExactArrayAllocationCount { get; }
        public long ExactArrayReuseCount { get; }

        public long TotalRejectedOperationCount
        {
            get
            {
                return InvalidRejectedOperationCount + CapacityRejectedOperationCount +
                       InactiveRejectedOperationCount + UnavailableRejectedOperationCount +
                       UnsupportedRejectedOperationCount + NativeFailureCount;
            }
        }
    }

    /// <summary>
    /// Per-service counters. Services are owner-thread confined, so these are
    /// plain fields; the process-wide aggregate uses interlocked operations.
    /// </summary>
    public sealed class DeviceFeedbackCounters
    {
        private long _accepted;
        private long _invalid;
        private long _capacity;
        private long _inactive;
        private long _unavailable;
        private long _unsupported;
        private long _nativeFailures;
        private long _peakDuration;
        private int _peakWaveform;
        private int _peakEvents;
        private int _peakPattern;

        public void RecordAccepted(long durationMilliseconds, int waveform = 0, int events = 0, int pattern = 0)
        {
            _accepted++;
            UpdatePeak(ref _peakDuration, durationMilliseconds);
            UpdatePeak(ref _peakWaveform, waveform);
            UpdatePeak(ref _peakEvents, events);
            UpdatePeak(ref _peakPattern, pattern);
            DeviceFeedbackDiagnostics.RecordAccepted(durationMilliseconds, waveform, events, pattern);
        }

        public void RecordRejected(FeedbackStatus status)
        {
            switch (status)
            {
                case FeedbackStatus.RejectedInvalid: _invalid++; break;
                case FeedbackStatus.RejectedCapacity: _capacity++; break;
                case FeedbackStatus.RejectedInactive: _inactive++; break;
                case FeedbackStatus.RejectedUnavailable: _unavailable++; break;
                case FeedbackStatus.RejectedUnsupported: _unsupported++; break;
                case FeedbackStatus.RejectedNativeFailure: _nativeFailures++; break;
                case FeedbackStatus.RejectedDisposed: _unavailable++; break;
                default: _invalid++; break;
            }
            DeviceFeedbackDiagnostics.RecordRejected(status);
        }

        public void RecordDegraded(long durationMilliseconds, int waveform = 0, int events = 0, int pattern = 0)
        {
            _accepted++;
            _unsupported++;
            UpdatePeak(ref _peakDuration, durationMilliseconds);
            UpdatePeak(ref _peakWaveform, waveform);
            UpdatePeak(ref _peakEvents, events);
            UpdatePeak(ref _peakPattern, pattern);
            DeviceFeedbackDiagnostics.RecordAccepted(durationMilliseconds, waveform, events, pattern);
        }

        public DeviceFeedbackMemoryStats GetStats(DeviceFeedbackBufferStats buffers)
        {
            return new DeviceFeedbackMemoryStats(
                buffers.RetainedElementBytes,
                _peakWaveform,
                _peakEvents,
                _peakPattern,
                _peakDuration,
                _accepted,
                _invalid,
                _capacity,
                _inactive,
                _unavailable,
                _unsupported,
                _nativeFailures,
                buffers.GrowthCount,
                buffers.ExactArrayCacheEntries,
                buffers.ExactArrayAllocationCount,
                buffers.ExactArrayReuseCount);
        }

        public void Reset()
        {
            _accepted = _invalid = _capacity = _inactive = _unavailable = _unsupported = 0L;
            _nativeFailures = _peakDuration = 0L;
            _peakWaveform = _peakEvents = _peakPattern = 0;
        }

        private static void UpdatePeak(ref long target, long candidate)
        {
            if (candidate > target) target = candidate;
        }

        private static void UpdatePeak(ref int target, int candidate)
        {
            if (candidate > target) target = candidate;
        }
    }

    /// <summary>
    /// Process-wide diagnostic aggregate for hosts that do not hold a service
    /// instance. Disabled by default; enable it with GlobalAggregationEnabled.
    /// Each service also exposes per-instance counters through GetMemoryStats().
    /// </summary>
    public static class DeviceFeedbackDiagnostics
    {
        private static long s_retainedBufferElementBytes;
        private static int s_peakWaveformSampleCount;
        private static int s_peakHapticEventCount;
        private static int s_peakPatternSegmentCount;
        private static long s_peakDurationMilliseconds;
        private static long s_acceptedOperationCount;
        private static long s_invalidRejectedOperationCount;
        private static long s_capacityRejectedOperationCount;
        private static long s_otherRejectedOperationCount;
        private static int s_enabled;

        public static bool GlobalAggregationEnabled
        {
            get => Volatile.Read(ref s_enabled) != 0;
            set => Volatile.Write(ref s_enabled, value ? 1 : 0);
        }

        public static DeviceFeedbackMemoryStats GetMemoryStats()
        {
            return new DeviceFeedbackMemoryStats(
                Interlocked.Read(ref s_retainedBufferElementBytes),
                Volatile.Read(ref s_peakWaveformSampleCount),
                Volatile.Read(ref s_peakHapticEventCount),
                Volatile.Read(ref s_peakPatternSegmentCount),
                Interlocked.Read(ref s_peakDurationMilliseconds),
                Interlocked.Read(ref s_acceptedOperationCount),
                Interlocked.Read(ref s_invalidRejectedOperationCount),
                Interlocked.Read(ref s_capacityRejectedOperationCount),
                Interlocked.Read(ref s_otherRejectedOperationCount),
                0L, 0L, 0L, 0L, 0, 0L, 0L);
        }

        public static void Reset()
        {
            Interlocked.Exchange(ref s_acceptedOperationCount, 0L);
            Interlocked.Exchange(ref s_invalidRejectedOperationCount, 0L);
            Interlocked.Exchange(ref s_capacityRejectedOperationCount, 0L);
            Interlocked.Exchange(ref s_otherRejectedOperationCount, 0L);
            Interlocked.Exchange(ref s_peakDurationMilliseconds, 0L);
            Interlocked.Exchange(ref s_retainedBufferElementBytes, 0L);
            Volatile.Write(ref s_peakWaveformSampleCount, 0);
            Volatile.Write(ref s_peakHapticEventCount, 0);
            Volatile.Write(ref s_peakPatternSegmentCount, 0);
        }

        internal static void RecordAccepted(long durationMilliseconds, int waveform, int events, int pattern)
        {
            if (Volatile.Read(ref s_enabled) == 0) return;
            Interlocked.Increment(ref s_acceptedOperationCount);
            UpdatePeakLong(ref s_peakDurationMilliseconds, durationMilliseconds);
            UpdatePeakInt(ref s_peakWaveformSampleCount, waveform);
            UpdatePeakInt(ref s_peakHapticEventCount, events);
            UpdatePeakInt(ref s_peakPatternSegmentCount, pattern);
        }

        internal static void RecordRejected(FeedbackStatus status)
        {
            if (Volatile.Read(ref s_enabled) == 0) return;
            if (status == FeedbackStatus.RejectedInvalid) Interlocked.Increment(ref s_invalidRejectedOperationCount);
            else if (status == FeedbackStatus.RejectedCapacity) Interlocked.Increment(ref s_capacityRejectedOperationCount);
            else Interlocked.Increment(ref s_otherRejectedOperationCount);
        }

        internal static void SetRetainedBufferElementBytes(long value)
        {
            if (Volatile.Read(ref s_enabled) == 0) return;
            // Keep the process-wide value monotonic when several services
            // report their own retained byte counts.
            if (value < 0L) value = 0L;
            UpdatePeakLong(ref s_retainedBufferElementBytes, value);
        }

        private static void UpdatePeakInt(ref int target, int candidate)
        {
            int current = Volatile.Read(ref target);
            while (candidate > current)
            {
                int observed = Interlocked.CompareExchange(ref target, candidate, current);
                if (observed == current) return;
                current = observed;
            }
        }

        private static void UpdatePeakLong(ref long target, long candidate)
        {
            long current = Interlocked.Read(ref target);
            while (candidate > current)
            {
                long observed = Interlocked.CompareExchange(ref target, candidate, current);
                if (observed == current) return;
                current = observed;
            }
        }
    }

    internal enum DeviceFeedbackAdmissionFailure : byte
    {
        None = 0,
        Invalid = 1,
        Capacity = 2
    }

    /// <summary>
    /// Pure validation logic. No Unity API is used. Every float crossing a
    /// platform or backend boundary must pass through this class first.
    /// </summary>
    internal static class DeviceFeedbackAdmission
    {
        public static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        public static DeviceFeedbackAdmissionFailure ValidateDurationSeconds(
            float durationSeconds,
            in DeviceFeedbackLimits limits,
            out long durationMilliseconds)
        {
            durationMilliseconds = 0L;
            if (!IsFinite(durationSeconds) || durationSeconds <= 0f)
            {
                return DeviceFeedbackAdmissionFailure.Invalid;
            }

            double milliseconds = durationSeconds * 1000d;
            if (milliseconds > limits.MaximumDurationMilliseconds)
            {
                return DeviceFeedbackAdmissionFailure.Capacity;
            }

            durationMilliseconds = (long)milliseconds;
            return durationMilliseconds > 0L
                ? DeviceFeedbackAdmissionFailure.None
                : DeviceFeedbackAdmissionFailure.Invalid;
        }

        public static DeviceFeedbackAdmissionFailure ValidateDurationMilliseconds(
            long durationMilliseconds,
            in DeviceFeedbackLimits limits)
        {
            if (durationMilliseconds <= 0L) return DeviceFeedbackAdmissionFailure.Invalid;
            return durationMilliseconds <= limits.MaximumDurationMilliseconds
                ? DeviceFeedbackAdmissionFailure.None
                : DeviceFeedbackAdmissionFailure.Capacity;
        }

        public static int SanitizeSampleInterval(int sampleIntervalMilliseconds)
        {
            return SanitizeSampleInterval(sampleIntervalMilliseconds, in DeviceFeedbackProfile.Default);
        }

        public static int SanitizeSampleInterval(int sampleIntervalMilliseconds, in DeviceFeedbackProfile profile)
        {
            int minimum = profile.MinimumSampleIntervalMilliseconds;
            return sampleIntervalMilliseconds < minimum ? minimum : sampleIntervalMilliseconds;
        }

        public static DeviceFeedbackAdmissionFailure CalculateSampleCount(
            float durationSeconds,
            int sampleIntervalMilliseconds,
            in DeviceFeedbackLimits limits,
            out long durationMilliseconds,
            out int sampleCount)
        {
            return CalculateSampleCount(
                durationSeconds, sampleIntervalMilliseconds,
                in limits, in DeviceFeedbackProfile.Default,
                out durationMilliseconds, out sampleCount);
        }

        public static DeviceFeedbackAdmissionFailure CalculateSampleCount(
            float durationSeconds,
            int sampleIntervalMilliseconds,
            in DeviceFeedbackLimits limits,
            in DeviceFeedbackProfile profile,
            out long durationMilliseconds,
            out int sampleCount)
        {
            sampleCount = 0;
            DeviceFeedbackAdmissionFailure durationFailure = ValidateDurationSeconds(
                durationSeconds, in limits, out durationMilliseconds);
            if (durationFailure != DeviceFeedbackAdmissionFailure.None) return durationFailure;

            int interval = SanitizeSampleInterval(sampleIntervalMilliseconds, in profile);
            // Integer ceiling division avoids double conversion, Math.Ceiling and
            // a division on the dispatch path; both operands are non-negative.
            long rawCount = (durationMilliseconds + interval - 1L) / interval;
            if (rawCount < 1L) rawCount = 1L;
            if (rawCount > limits.MaximumWaveformSampleCount) return DeviceFeedbackAdmissionFailure.Capacity;

            // Quantize DOWN to a power of two in O(1). Native waveform APIs
            // require an array whose Length equals the sample count; a small,
            // stable key space keeps the exact-length array cache warm.
            // Rounding down guarantees grid <= rawCount, so the effective
            // interval can stretch to preserve total duration.
            sampleCount = FloorPowerOfTwo(rawCount);
            if (sampleCount > limits.MaximumWaveformSampleCount)
            {
                sampleCount = limits.MaximumWaveformSampleCount;
            }
            return DeviceFeedbackAdmissionFailure.None;
        }

        /// <summary>
        /// Interval that keeps grid * interval approximately equal to the
        /// requested duration after grid quantization.
        /// </summary>
        public static int EffectiveIntervalMilliseconds(long durationMilliseconds, int sampleCount)
        {
            return EffectiveIntervalMilliseconds(durationMilliseconds, sampleCount,
                                                 in DeviceFeedbackProfile.Default);
        }

        public static int EffectiveIntervalMilliseconds(
            long durationMilliseconds, int sampleCount, in DeviceFeedbackProfile profile)
        {
            int minimum = profile.MinimumSampleIntervalMilliseconds;
            if (sampleCount < 1) return minimum;
            long interval = (durationMilliseconds + sampleCount - 1L) / sampleCount;
            if (interval < minimum) interval = minimum;
            if (interval > int.MaxValue) interval = int.MaxValue;
            return (int)interval;
        }

        public static DeviceFeedbackAdmissionFailure ValidateEvents(
            HapticEvent[] events,
            in DeviceFeedbackLimits limits,
            bool requireWaveformCapacity,
            out long durationMilliseconds)
        {
            durationMilliseconds = 0L;
            if (events == null || events.Length == 0) return DeviceFeedbackAdmissionFailure.Invalid;
            if (events.Length > limits.MaximumHapticEventCount ||
                (requireWaveformCapacity && events.Length > limits.MaximumWaveformSampleCount))
            {
                return DeviceFeedbackAdmissionFailure.Capacity;
            }

            double maximumEndSeconds = 0d;
            for (int index = 0; index < events.Length; index++)
            {
                ref readonly HapticEvent hapticEvent = ref events[index];
                if (hapticEvent.type != HapticEventType.Transient && hapticEvent.type != HapticEventType.Continuous)
                {
                    return DeviceFeedbackAdmissionFailure.Invalid;
                }

                if (!IsFinite(hapticEvent.time) || hapticEvent.time < 0f ||
                    !IsFinite(hapticEvent.duration) || hapticEvent.duration < 0f ||
                    !IsFinite(hapticEvent.intensity) || !IsFinite(hapticEvent.sharpness))
                {
                    return DeviceFeedbackAdmissionFailure.Invalid;
                }

                double eventDuration = hapticEvent.type == HapticEventType.Transient ? 0.03d : hapticEvent.duration;
                double eventEnd = hapticEvent.time + eventDuration;
                if (eventEnd * 1000d > limits.MaximumDurationMilliseconds)
                {
                    return DeviceFeedbackAdmissionFailure.Capacity;
                }

                maximumEndSeconds = Math.Max(maximumEndSeconds, eventEnd);
            }

            durationMilliseconds = Math.Max(1L, (long)(maximumEndSeconds * 1000d));
            return DeviceFeedbackAdmissionFailure.None;
        }

        /// <summary>
        /// Highest power of two less than or equal to a positive value, in a
        /// fixed number of bit operations (no loop, no floating point).
        /// </summary>
        public static int FloorPowerOfTwo(long value)
        {
            if (value <= 1L) return 1;
            int v = (int)value;
            v |= v >> 1;
            v |= v >> 2;
            v |= v >> 4;
            v |= v >> 8;
            v |= v >> 16;
            return (v + 1) >> 1;
        }

        public static FeedbackStatus ToStatus(DeviceFeedbackAdmissionFailure failure)
        {
            return failure == DeviceFeedbackAdmissionFailure.Capacity
                ? FeedbackStatus.RejectedCapacity
                : FeedbackStatus.RejectedInvalid;
        }
    }

    /// <summary>
    /// Deterministic outcomes, per-instance diagnostics, limits and hardware
    /// capability for DI consumers without a cast to a concrete service.
    /// </summary>
    public interface IFeedbackResultProvider
    {
        FeedbackResult LastResult { get; }
        DeviceFeedbackLimits Limits { get; }
        DeviceCapability Capability { get; }
        DeviceFeedbackMemoryStats GetMemoryStats();
        void RefreshCapabilities();
        void ResetDiagnostics();
    }
}
