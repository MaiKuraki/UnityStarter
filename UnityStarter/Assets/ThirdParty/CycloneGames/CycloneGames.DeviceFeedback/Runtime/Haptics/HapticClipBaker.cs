using UnityEngine;

namespace CycloneGames.DeviceFeedback.Runtime
{
    /// <summary>
    /// Converts a HapticClip authoring asset into an immutable HapticClipSnapshot.
    /// The asset is never mutated.
    /// </summary>
    public static class HapticClipBaker
    {
        public static bool TryBake(
            HapticClip clip,
            in DeviceFeedbackLimits limits,
            in DeviceFeedbackProfile profile,
            out HapticClipSnapshot snapshot,
            out FeedbackResult result)
        {
            snapshot = null;
            if (clip == null)
            {
                result = FeedbackResult.Rejected(FeedbackStatus.RejectedInvalid);
                return false;
            }

            if (clip.HasEvents)
            {
                return TryBakeEvents(clip.events, in limits, out snapshot, out result);
            }

            if (clip.intensityCurve == null)
            {
                result = FeedbackResult.Rejected(FeedbackStatus.RejectedInvalid);
                return false;
            }

            DeviceFeedbackAdmissionFailure failure = DeviceFeedbackAdmission.CalculateSampleCount(
                clip.duration, DeviceFeedbackProfile.Default.MinimumSampleIntervalMilliseconds,
                in limits, in profile, out long durationMilliseconds, out int sampleCount);
            if (failure != DeviceFeedbackAdmissionFailure.None)
            {
                result = FeedbackResult.Rejected(DeviceFeedbackAdmission.ToStatus(failure));
                return false;
            }

            int effectiveInterval = DeviceFeedbackAdmission.EffectiveIntervalMilliseconds(
                durationMilliseconds, sampleCount, in profile);
            var times = new float[sampleCount];
            var intensities = new float[sampleCount];
            var sharpnesses = new float[sampleCount];
            var timings = new long[sampleCount];
            var amplitudes = new int[sampleCount];
            long totalVibrationMilliseconds = 0L;

            for (int i = 0; i < sampleCount; i++)
            {
                float t = sampleCount == 1 ? 1f : (float)i / (sampleCount - 1);
                float intensity = Mathf.Clamp01(clip.intensityCurve.Evaluate(t));
                if (!DeviceFeedbackAdmission.IsFinite(intensity)) intensity = 0f;
                float sharpness = clip.sharpnessCurve != null
                    ? Mathf.Clamp01(clip.sharpnessCurve.Evaluate(t))
                    : 0.5f;
                if (!DeviceFeedbackAdmission.IsFinite(sharpness)) sharpness = 0.5f;

                times[i] = t * clip.duration;
                intensities[i] = intensity;
                sharpnesses[i] = sharpness;
                timings[i] = effectiveInterval;
                amplitudes[i] = Mathf.Clamp((int)(intensity * 255f), 0, 255);
                if (amplitudes[i] > 0) totalVibrationMilliseconds += effectiveInterval;
            }

            snapshot = new HapticClipSnapshot(
                times, intensities, sharpnesses,
                System.Array.Empty<int>(), System.Array.Empty<float>(),
                timings, amplitudes,
                durationMilliseconds, sampleCount, 0, sampleCount,
                totalVibrationMilliseconds, false);
            result = FeedbackResult.Accepted(durationMilliseconds, sampleCount);
            return true;
        }

        private static bool TryBakeEvents(
            HapticEvent[] source,
            in DeviceFeedbackLimits limits,
            out HapticClipSnapshot snapshot,
            out FeedbackResult result)
        {
            snapshot = null;
            DeviceFeedbackAdmissionFailure failure = DeviceFeedbackAdmission.ValidateEvents(
                source, in limits, false, out long durationMilliseconds);
            if (failure != DeviceFeedbackAdmissionFailure.None)
            {
                result = FeedbackResult.Rejected(DeviceFeedbackAdmission.ToStatus(failure));
                return false;
            }

            int count = source.Length;
            var ordered = new HapticEvent[count];
            System.Array.Copy(source, ordered, count);
            bool alreadyOrdered = true;
            for (int i = 1; i < count; i++)
            {
                if (ordered[i].time < ordered[i - 1].time) { alreadyOrdered = false; break; }
            }
            if (!alreadyOrdered) System.Array.Sort(ordered, 0, count);

            var times = new float[count];
            var intensities = new float[count];
            var sharpnesses = new float[count];
            var types = new int[count];
            var durations = new float[count];
            var timings = new long[count * 2];
            var amplitudes = new int[count * 2];

            int segment = 0;
            double currentEndSeconds = 0d;
            long totalVibrationMilliseconds = 0L;
            for (int i = 0; i < count; i++)
            {
                ref readonly HapticEvent e = ref ordered[i];
                double start = e.time;
                double length = e.type == HapticEventType.Transient ? 0.03d : System.Math.Max(0.01d, e.duration);
                double gapMilliseconds = (start - currentEndSeconds) * 1000d;
                if (gapMilliseconds >= 1d)
                {
                    timings[segment] = (long)gapMilliseconds;
                    amplitudes[segment] = 0;
                    segment++;
                }

                int amplitude = Mathf.Clamp((int)(e.intensity * 255f), 0, 255);
                long eventMilliseconds = System.Math.Max(1L, (long)(length * 1000d));
                timings[segment] = eventMilliseconds;
                amplitudes[segment] = amplitude;
                segment++;
                if (amplitude > 0) totalVibrationMilliseconds += eventMilliseconds;

                times[i] = (float)start;
                intensities[i] = Mathf.Clamp01(e.intensity);
                sharpnesses[i] = Mathf.Clamp01(e.sharpness);
                types[i] = (int)e.type;
                durations[i] = (float)length;
                currentEndSeconds = System.Math.Max(currentEndSeconds, start + length);
            }

            // Exact-length arrays: Android createWaveform must not see trailing
            // capacity. Trim once at bake time, never on the dispatch thread.
            var exactTimings = new long[segment];
            var exactAmplitudes = new int[segment];
            System.Array.Copy(timings, exactTimings, segment);
            System.Array.Copy(amplitudes, exactAmplitudes, segment);

            snapshot = new HapticClipSnapshot(
                times, intensities, sharpnesses, types, durations,
                exactTimings, exactAmplitudes,
                durationMilliseconds, 0, count, segment,
                totalVibrationMilliseconds, true);
            result = FeedbackResult.Accepted(durationMilliseconds, segment, count);
            return true;
        }
    }
}
