namespace CycloneGames.DeviceFeedback.Runtime
{
    /// <summary>
    /// Immutable runtime representation of a HapticClip. Sampling, event
    /// ordering, silence-gap expansion and amplitude conversion run once at
    /// bake time. Arrays have the exact lengths required by the platform APIs
    /// and are never mutated after construction.
    /// </summary>
    public sealed class HapticClipSnapshot
    {
        internal readonly float[] Times;
        internal readonly float[] Intensities;
        internal readonly float[] Sharpnesses;
        internal readonly int[] Types;
        internal readonly float[] Durations;
        internal readonly long[] Timings;
        internal readonly int[] Amplitudes;

        internal HapticClipSnapshot(
            float[] times,
            float[] intensities,
            float[] sharpnesses,
            int[] types,
            float[] durations,
            long[] timings,
            int[] amplitudes,
            long durationMilliseconds,
            int sampleCount,
            int eventCount,
            int segmentCount,
            long totalVibrationMilliseconds,
            bool isEventMode)
        {
            Times = times;
            Intensities = intensities;
            Sharpnesses = sharpnesses;
            Types = types;
            Durations = durations;
            Timings = timings;
            Amplitudes = amplitudes;
            DurationMilliseconds = durationMilliseconds;
            SampleCount = sampleCount;
            EventCount = eventCount;
            SegmentCount = segmentCount;
            TotalVibrationMilliseconds = totalVibrationMilliseconds;
            IsEventMode = isEventMode;
        }

        public long DurationMilliseconds { get; }
        public int SampleCount { get; }
        public int EventCount { get; }
        public int SegmentCount { get; }
        public long TotalVibrationMilliseconds { get; }
        public bool IsEventMode { get; }
        public bool IsValid => (IsEventMode ? EventCount : SampleCount) > 0;
    }
}
