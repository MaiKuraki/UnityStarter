using System;
using System.Threading;

namespace CycloneGames.DeviceFeedback.Runtime
{
    public readonly struct DeviceFeedbackLimits
    {
        public const int DefaultMaximumDurationMilliseconds = 300_000;
        public const int DefaultMaximumWaveformSampleCount = 4_096;
        public const int DefaultMaximumHapticEventCount = 2_048;
        public const int DefaultMaximumPatternSegmentCount = 4_096;

        public const int HardMaximumDurationMilliseconds = 3_600_000;
        public const int HardMaximumWaveformSampleCount = 65_536;
        public const int HardMaximumHapticEventCount = 16_384;
        public const int HardMaximumPatternSegmentCount = 65_536;

        public DeviceFeedbackLimits(
            int maximumDurationMilliseconds,
            int maximumWaveformSampleCount,
            int maximumHapticEventCount,
            int maximumPatternSegmentCount)
        {
            ValidateRange(
                maximumDurationMilliseconds,
                HardMaximumDurationMilliseconds,
                nameof(maximumDurationMilliseconds));
            ValidateRange(
                maximumWaveformSampleCount,
                HardMaximumWaveformSampleCount,
                nameof(maximumWaveformSampleCount));
            ValidateRange(
                maximumHapticEventCount,
                HardMaximumHapticEventCount,
                nameof(maximumHapticEventCount));
            ValidateRange(
                maximumPatternSegmentCount,
                HardMaximumPatternSegmentCount,
                nameof(maximumPatternSegmentCount));

            MaximumDurationMilliseconds = maximumDurationMilliseconds;
            MaximumWaveformSampleCount = maximumWaveformSampleCount;
            MaximumHapticEventCount = maximumHapticEventCount;
            MaximumPatternSegmentCount = maximumPatternSegmentCount;
        }

        public int MaximumDurationMilliseconds { get; }
        public int MaximumWaveformSampleCount { get; }
        public int MaximumHapticEventCount { get; }
        public int MaximumPatternSegmentCount { get; }

        public static DeviceFeedbackLimits Default => new DeviceFeedbackLimits(
            DefaultMaximumDurationMilliseconds,
            DefaultMaximumWaveformSampleCount,
            DefaultMaximumHapticEventCount,
            DefaultMaximumPatternSegmentCount);

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
                    $"Value must be between 1 and the hard safety limit of {hardMaximum}.");
            }
        }
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
            long capacityRejectedOperationCount)
        {
            RetainedBufferElementBytes = retainedBufferElementBytes;
            PeakWaveformSampleCount = peakWaveformSampleCount;
            PeakHapticEventCount = peakHapticEventCount;
            PeakPatternSegmentCount = peakPatternSegmentCount;
            PeakDurationMilliseconds = peakDurationMilliseconds;
            AcceptedOperationCount = acceptedOperationCount;
            InvalidRejectedOperationCount = invalidRejectedOperationCount;
            CapacityRejectedOperationCount = capacityRejectedOperationCount;
        }

        public long RetainedBufferElementBytes { get; }
        public int PeakWaveformSampleCount { get; }
        public int PeakHapticEventCount { get; }
        public int PeakPatternSegmentCount { get; }
        public long PeakDurationMilliseconds { get; }
        public long AcceptedOperationCount { get; }
        public long InvalidRejectedOperationCount { get; }
        public long CapacityRejectedOperationCount { get; }
    }

    /// <summary>
    /// Package-owned, allocation-free diagnostics for bounded feedback admission and retained static buffers.
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
                Interlocked.Read(ref s_capacityRejectedOperationCount));
        }

        internal static void RecordAccepted(
            long durationMilliseconds,
            int waveformSampleCount = 0,
            int hapticEventCount = 0,
            int patternSegmentCount = 0)
        {
            Interlocked.Increment(ref s_acceptedOperationCount);
            UpdatePeak(ref s_peakDurationMilliseconds, durationMilliseconds);
            UpdatePeak(ref s_peakWaveformSampleCount, waveformSampleCount);
            UpdatePeak(ref s_peakHapticEventCount, hapticEventCount);
            UpdatePeak(ref s_peakPatternSegmentCount, patternSegmentCount);
        }

        internal static void RecordInvalidRejected()
        {
            Interlocked.Increment(ref s_invalidRejectedOperationCount);
        }

        internal static void RecordCapacityRejected()
        {
            Interlocked.Increment(ref s_capacityRejectedOperationCount);
        }

        internal static void SetRetainedBufferElementBytes(long value)
        {
            Interlocked.Exchange(ref s_retainedBufferElementBytes, Math.Max(0L, value));
        }

        private static void UpdatePeak(ref int target, int candidate)
        {
            int current = Volatile.Read(ref target);
            while (candidate > current)
            {
                int observed = Interlocked.CompareExchange(ref target, candidate, current);
                if (observed == current)
                {
                    return;
                }

                current = observed;
            }
        }

        private static void UpdatePeak(ref long target, long candidate)
        {
            long current = Interlocked.Read(ref target);
            while (candidate > current)
            {
                long observed = Interlocked.CompareExchange(ref target, candidate, current);
                if (observed == current)
                {
                    return;
                }

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

    internal static class DeviceFeedbackAdmission
    {
        public static DeviceFeedbackAdmissionFailure ValidateDurationSeconds(
            float durationSeconds,
            in DeviceFeedbackLimits limits,
            out long durationMilliseconds)
        {
            durationMilliseconds = 0L;
            if (float.IsNaN(durationSeconds) || float.IsInfinity(durationSeconds) || durationSeconds <= 0f)
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
            if (durationMilliseconds <= 0L)
            {
                return DeviceFeedbackAdmissionFailure.Invalid;
            }

            return durationMilliseconds <= limits.MaximumDurationMilliseconds
                ? DeviceFeedbackAdmissionFailure.None
                : DeviceFeedbackAdmissionFailure.Capacity;
        }

        public static DeviceFeedbackAdmissionFailure CalculateSampleCount(
            float durationSeconds,
            int sampleIntervalMilliseconds,
            in DeviceFeedbackLimits limits,
            out long durationMilliseconds,
            out int sampleCount)
        {
            sampleCount = 0;
            DeviceFeedbackAdmissionFailure durationFailure = ValidateDurationSeconds(
                durationSeconds,
                in limits,
                out durationMilliseconds);
            if (durationFailure != DeviceFeedbackAdmissionFailure.None)
            {
                return durationFailure;
            }

            if (sampleIntervalMilliseconds <= 0)
            {
                return DeviceFeedbackAdmissionFailure.Invalid;
            }

            long count = Math.Max(1L, (long)Math.Ceiling(
                durationMilliseconds / (double)sampleIntervalMilliseconds));
            if (count > limits.MaximumWaveformSampleCount)
            {
                return DeviceFeedbackAdmissionFailure.Capacity;
            }

            sampleCount = (int)count;
            return DeviceFeedbackAdmissionFailure.None;
        }

        public static DeviceFeedbackAdmissionFailure ValidateEvents(
            HapticEvent[] events,
            in DeviceFeedbackLimits limits,
            bool requireWaveformCapacity,
            out long durationMilliseconds)
        {
            durationMilliseconds = 0L;
            if (events == null || events.Length == 0)
            {
                return DeviceFeedbackAdmissionFailure.Invalid;
            }

            if (events.Length > limits.MaximumHapticEventCount ||
                (requireWaveformCapacity && events.Length > limits.MaximumWaveformSampleCount))
            {
                return DeviceFeedbackAdmissionFailure.Capacity;
            }

            double maximumEndSeconds = 0d;
            for (int index = 0; index < events.Length; index++)
            {
                ref readonly HapticEvent hapticEvent = ref events[index];
                if (float.IsNaN(hapticEvent.time) ||
                    float.IsInfinity(hapticEvent.time) ||
                    hapticEvent.time < 0f ||
                    float.IsNaN(hapticEvent.duration) ||
                    float.IsInfinity(hapticEvent.duration) ||
                    hapticEvent.duration < 0f)
                {
                    return DeviceFeedbackAdmissionFailure.Invalid;
                }

                double eventDuration = hapticEvent.type == HapticEventType.Transient
                    ? 0.03d
                    : hapticEvent.duration;
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

        public static void RecordRejected(DeviceFeedbackAdmissionFailure failure)
        {
            if (failure == DeviceFeedbackAdmissionFailure.Capacity)
            {
                DeviceFeedbackDiagnostics.RecordCapacityRejected();
            }
            else if (failure == DeviceFeedbackAdmissionFailure.Invalid)
            {
                DeviceFeedbackDiagnostics.RecordInvalidRejected();
            }
        }
    }
}
