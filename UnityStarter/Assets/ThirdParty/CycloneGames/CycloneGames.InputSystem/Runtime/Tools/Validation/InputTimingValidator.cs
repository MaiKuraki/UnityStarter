using System;
using System.Collections.Generic;
using UnityEngine;

namespace CycloneGames.InputSystem.Runtime
{
    public sealed class TimingDeviationSample
    {
        public float ExpectedTimeSec { get; }
        public float ActualTimeSec { get; }
        public float DeviationMs { get; }
        public string ActionName { get; }

        internal TimingDeviationSample(float expectedTimeSec, float actualTimeSec, string actionName)
        {
            ExpectedTimeSec = expectedTimeSec;
            ActualTimeSec = actualTimeSec;
            DeviationMs = (actualTimeSec - expectedTimeSec) * 1000f;
            ActionName = actionName;
        }
    }

    public sealed class TimingValidationResult
    {
        public IReadOnlyList<TimingDeviationSample> Samples { get; }
        public float MeanDeviationMs { get; }
        public float StdDeviationMs { get; }
        public float MinDeviationMs { get; }
        public float MaxDeviationMs { get; }

        public bool IsHumanLikely { get; }
        public bool IsDefinitivelyBot { get; }
        public bool IsSuspiciousPerfect { get; }
        public bool IsSuspiciousSubFrame { get; }
        public bool IsSuspiciousUniform { get; }
        public bool IsSuspiciousRandom { get; }
        public bool IsSuspiciousDriftless { get; }
        public bool IsSuspiciousZeroMean { get; }

        public float HumanLikenessScore { get; }
        public float AutocorrelationLag1 { get; }
        public float DriftSlopeMsPerBeat { get; }

        internal TimingValidationResult(
            List<TimingDeviationSample> samples,
            float meanDevMs, float stdDevMs,
            float minDevMs, float maxDevMs,
            float humanLikenessScore,
            float autocorrelation, float driftSlope,
            bool definitivelyBot,
            bool suspiciousPerfect, bool suspiciousSubFrame, bool suspiciousUniform,
            bool suspiciousRandom, bool suspiciousDriftless, bool suspiciousZeroMean)
        {
            Samples = samples;
            MeanDeviationMs = meanDevMs;
            StdDeviationMs = stdDevMs;
            MinDeviationMs = minDevMs;
            MaxDeviationMs = maxDevMs;
            HumanLikenessScore = humanLikenessScore;
            AutocorrelationLag1 = autocorrelation;
            DriftSlopeMsPerBeat = driftSlope;
            IsDefinitivelyBot = definitivelyBot;
            IsSuspiciousPerfect = suspiciousPerfect;
            IsSuspiciousSubFrame = suspiciousSubFrame;
            IsSuspiciousUniform = suspiciousUniform;
            IsSuspiciousRandom = suspiciousRandom;
            IsSuspiciousDriftless = suspiciousDriftless;
            IsSuspiciousZeroMean = suspiciousZeroMean;
            IsHumanLikely = !definitivelyBot && humanLikenessScore > 0.5f;
        }
    }

    public static class InputTimingValidator
    {

        public static TimingValidationResult ValidateTiming(
            InputRecording recording,
            float[] expectedBeatTimingsSec,
            string actionMapName,
            string actionName,
            float timingWindowMs = 100f)
        {
            return ValidateTiming(recording, expectedBeatTimingsSec, actionMapName, actionName,
                TimingValidationConfig.Normal, timingWindowMs);
        }

        public static TimingValidationResult ValidateTiming(
            InputRecording recording,
            float[] expectedBeatTimingsSec,
            string actionMapName,
            string actionName,
            TimingValidationConfig config,
            float timingWindowMs = 100f)
        {
            var samples = new List<TimingDeviationSample>();
            if (recording == null || expectedBeatTimingsSec == null || expectedBeatTimingsSec.Length == 0)
                return BuildResult(samples, config);

            var frames = recording.Frames;
            int beatIndex = 0;

            for (int i = 0; i < frames.Count && beatIndex < expectedBeatTimingsSec.Length; i++)
            {
                var frame = frames[i];
                if (!frame.HasUnitEvent) continue;

                float expectedTime = expectedBeatTimingsSec[beatIndex];
                float deviation = Mathf.Abs(frame.TimeSinceStart - expectedTime);

                if (deviation <= timingWindowMs / 1000f)
                {
                    samples.Add(new TimingDeviationSample(expectedTime, frame.TimeSinceStart, actionName));
                    beatIndex++;
                }
            }

            return BuildResult(samples, config);
        }

        private static TimingValidationResult BuildResult(List<TimingDeviationSample> samples, TimingValidationConfig config)
        {
            if (samples.Count < 5)
            {
                return new TimingValidationResult(samples, 0f, 0f, 0f, 0f, 0f,
                    0f, 0f, false, false, false, false, false, false, false);
            }

            int n = samples.Count;
            float sumAbs = 0f, sumSq = 0f;
            float min = float.MaxValue, max = float.MinValue;

            for (int i = 0; i < n; i++)
            {
                float dev = Mathf.Abs(samples[i].DeviationMs);
                sumAbs += dev;
                sumSq += dev * dev;
                if (dev < min) min = dev;
                if (dev > max) max = dev;
            }

            float mean = sumAbs / n;
            float variance = (sumSq / n) - (mean * mean);
            float stdDev = Mathf.Sqrt(Mathf.Max(0f, variance));

            // Tier 1: Hard blocks (physically impossible for any human → near-zero false positive)
            bool suspiciousPerfect = IsSuspiciousPerfectConsistency(samples, mean, config.PerfectConsistencyThresholdMs);
            bool suspiciousSubFrame = HasSubFramePrecision(samples, config.SubFrameThresholdMs);
            bool suspiciousUniform = IsSuspiciousUniformDistribution(samples, mean);
            bool definitivelyBot = suspiciousPerfect || suspiciousSubFrame || suspiciousUniform;

            // Tier 2: Soft scoring indicators (statistical anomalies with possible false positives)
            float autocorrelation = ComputeLag1Autocorrelation(samples);
            float driftSlope = ComputeDriftSlope(samples);
            float signedMean = ComputeSignedMean(samples);

            bool suspiciousRandom = autocorrelation < config.AutocorrelationSuspiciousThreshold;
            bool suspiciousDriftless = Mathf.Abs(driftSlope) < config.DriftSuspiciousThresholdMs;
            bool suspiciousZeroMean = Mathf.Abs(signedMean) < config.ZeroMeanThresholdMs && stdDev > 10f;

            float humanScore = CalculateHumanLikenessWeighted(
                mean, stdDev, autocorrelation, driftSlope, signedMean,
                suspiciousRandom, suspiciousDriftless, suspiciousZeroMean, config);

            return new TimingValidationResult(
                samples, mean, stdDev, min, max, humanScore,
                autocorrelation, driftSlope,
                definitivelyBot,
                suspiciousPerfect, suspiciousSubFrame, suspiciousUniform,
                suspiciousRandom, suspiciousDriftless, suspiciousZeroMean);
        }

        /// <summary>
        /// Lag-1 autocorrelation of the signed deviation sequence.
        /// Human timing: r ≈ 0.3~0.7 (consecutive deviations are correlated — 
        /// internal clock has inertia). Random cheat: r ≈ 0 (independent draws).
        /// </summary>
        private static float ComputeLag1Autocorrelation(List<TimingDeviationSample> samples)
        {
            int n = samples.Count;
            float sum = 0f;
            for (int i = 0; i < n; i++)
                sum += samples[i].DeviationMs;
            float meanDev = sum / n;

            float numerator = 0f, denominator = 0f;
            for (int i = 0; i < n - 1; i++)
            {
                float d1 = samples[i].DeviationMs - meanDev;
                float d2 = samples[i + 1].DeviationMs - meanDev;
                numerator += d1 * d2;
            }
            for (int i = 0; i < n; i++)
            {
                float d = samples[i].DeviationMs - meanDev;
                denominator += d * d;
            }

            if (denominator < 0.001f) return 0f;
            float r = numerator / denominator;
            return Mathf.Clamp(r, -1f, 1f);
        }

        /// <summary>
        /// Linear regression slope on the signed deviation sequence.
        /// Detects systematic drift over time. Human: typically has non-zero drift
        /// (anticipating or lagging progressively). Random cheat: slope ≈ 0.
        /// </summary>
        private static float ComputeDriftSlope(List<TimingDeviationSample> samples)
        {
            int n = samples.Count;
            float sumX = 0f, sumY = 0f, sumXY = 0f, sumX2 = 0f;

            for (int i = 0; i < n; i++)
            {
                float x = i;
                float y = samples[i].DeviationMs;
                sumX += x;
                sumY += y;
                sumXY += x * y;
                sumX2 += x * x;
            }

            float denominator = n * sumX2 - sumX * sumX;
            if (Mathf.Abs(denominator) < 0.001f) return 0f;

            return (n * sumXY - sumX * sumY) / denominator;
        }

        private static float ComputeSignedMean(List<TimingDeviationSample> samples)
        {
            float sum = 0f;
            for (int i = 0; i < samples.Count; i++)
                sum += samples[i].DeviationMs;
            return sum / samples.Count;
        }

        private static bool IsSuspiciousPerfectConsistency(List<TimingDeviationSample> samples, float mean, float thresholdMs)
        {
            if (mean > thresholdMs) return false;

            float variance = 0f;
            for (int i = 0; i < samples.Count; i++)
            {
                float diff = samples[i].DeviationMs - mean;
                variance += diff * diff;
            }
            variance /= samples.Count;
            float stdDev = Mathf.Sqrt(variance);

            return stdDev < thresholdMs;
        }

        private static bool HasSubFramePrecision(List<TimingDeviationSample> samples, float thresholdMs)
        {
            int subFrameCount = 0;
            for (int i = 0; i < samples.Count; i++)
            {
                if (Mathf.Abs(samples[i].DeviationMs) < thresholdMs)
                    subFrameCount++;
            }
            return subFrameCount >= samples.Count * 0.8f;
        }

        private static bool IsSuspiciousUniformDistribution(List<TimingDeviationSample> samples, float mean)
        {
            float[] deviations = new float[samples.Count];
            for (int i = 0; i < samples.Count; i++)
                deviations[i] = Mathf.Abs(samples[i].DeviationMs);

            System.Array.Sort(deviations);

            float range = deviations[samples.Count - 1] - deviations[0];
            if (range < 1f) return false;

            float expectedBucket = range / 4f;
            int[] buckets = new int[4];
            for (int i = 0; i < deviations.Length; i++)
            {
                int bucket = Mathf.Clamp((int)((deviations[i] - deviations[0]) / expectedBucket), 0, 3);
                buckets[bucket]++;
            }

            float expectedPerBucket = deviations.Length / 4f;
            float chiSquare = 0f;
            for (int b = 0; b < 4; b++)
            {
                float diff = buckets[b] - expectedPerBucket;
                chiSquare += (diff * diff) / expectedPerBucket;
            }

            return chiSquare < 1f;
        }

        private static float CalculateHumanLikenessWeighted(
            float meanAbsDev, float stdDev,
            float autocorrelation, float driftSlope, float signedMean,
            bool suspiciousRandom, bool suspiciousDriftless, bool suspiciousZeroMean,
            TimingValidationConfig config)
        {
            float score = 0f;

            // Base: timing distribution quality
            if (stdDev > 5f && stdDev < 60f)
                score += config.DistributionWeight;
            else if (stdDev >= 3f && stdDev <= 80f)
                score += config.DistributionWeight * 0.6f;
            else
                score += config.DistributionWeight * 0.25f;

            // Autocorrelation: human internal clock inertia
            if (autocorrelation > 0.5f)
                score += config.AutocorrelationWeight;
            else if (autocorrelation > 0.3f)
                score += config.AutocorrelationWeight * 0.875f;
            else if (autocorrelation > 0.15f)
                score += config.AutocorrelationWeight * 0.5f;
            else if (autocorrelation > config.AutocorrelationSuspiciousThreshold)
                score += config.AutocorrelationWeight * 0.2f;

            // Drift: natural timing evolution — "innocent until proven guilty"
            float absDrift = Mathf.Abs(driftSlope);
            if (absDrift > 0.1f)
                score += config.DriftWeight;
            else if (absDrift > 0.05f)
                score += config.DriftWeight * 0.8f;
            else if (absDrift > config.DriftSuspiciousThresholdMs)
                score += config.DriftWeight * 0.6f;
            else
                score += config.DriftWeight * 0.3f;

            // Mean bias: personal playstyle signature — "innocent until proven guilty"
            float absMean = Mathf.Abs(signedMean);
            if (absMean > 20f)
                score += config.MeanBiasWeight;
            else if (absMean > 8f)
                score += config.MeanBiasWeight * 0.9f;
            else if (absMean > 3f)
                score += config.MeanBiasWeight * 0.8f;
            else if (absMean > config.ZeroMeanThresholdMs)
                score += config.MeanBiasWeight * 0.6f;
            else
                score += config.MeanBiasWeight * 0.4f;

            // Penalty for multiple suspicious indicators (but NOT hard failure)
            int suspiciousCount = (suspiciousRandom ? 1 : 0) + (suspiciousDriftless ? 1 : 0) + (suspiciousZeroMean ? 1 : 0);
            if (suspiciousCount >= 3)
                score *= config.SuspiciousPenalty3;
            else if (suspiciousCount == 2)
                score *= config.SuspiciousPenalty2;
            else if (suspiciousCount == 1)
                score *= config.SuspiciousPenalty1;

            float totalWeight = config.DistributionWeight + config.AutocorrelationWeight
                              + config.DriftWeight + config.MeanBiasWeight;
            return Mathf.Clamp01(score / totalWeight);
        }

        #region DTW Timing Alignment (Fair Scoring — not anti-cheat)

        public sealed class DtwTimingResult
        {
            public float DtwDistance { get; }
            public float NormalizedScore { get; }
            public float AverageWarpMs { get; }
            public int WarpPathLength { get; }

            internal DtwTimingResult(float distance, float normalizedScore, float avgWarp, int pathLen)
            {
                DtwDistance = distance;
                NormalizedScore = normalizedScore;
                AverageWarpMs = avgWarp;
                WarpPathLength = pathLen;
            }
        }

        /// <summary>
        /// DTW-based timing comparison. Tolerates consistent offsets and tempo variations.
        /// A human playing slightly ahead of the beat for the entire song gets a HIGH score,
        /// whereas the statistical validator would penalize the bias.
        /// Uses Sakoe-Chiba band constraint for O(n×w) complexity.
        /// </summary>
        /// <param name="actualTimingsSec">Player's actual input timestamps (sorted ascending)</param>
        /// <param name="expectedTimingsSec">Expected beat timestamps (sorted ascending)</param>
        /// <param name="maxWarpMs">Maximum allowed time warping per step (Sakoe-Chiba window)</param>
        public static DtwTimingResult ComputeDtwScore(
            float[] actualTimingsSec,
            float[] expectedTimingsSec,
            float maxWarpMs = 200f)
        {
            if (actualTimingsSec == null || expectedTimingsSec == null ||
                actualTimingsSec.Length == 0 || expectedTimingsSec.Length == 0)
                return new DtwTimingResult(float.MaxValue, 0f, 0f, 0);

            int n = actualTimingsSec.Length;
            int m = expectedTimingsSec.Length;
            float maxWarpSec = maxWarpMs / 1000f;

            // Sakoe-Chiba band: only compute within this window
            int bandWidth = Mathf.Max(1, Mathf.CeilToInt(maxWarpSec * Mathf.Max(n, m)));

            // DTW cost matrix (sparse: only band region)
            // Use two rows rolling buffer for O(min(n,m)) memory
            float[] prevRow = new float[m + 1];
            float[] currRow = new float[m + 1];

            for (int j = 0; j <= m; j++)
                prevRow[j] = float.MaxValue;
            prevRow[0] = 0f;

            int warpsUsed = 0;

            for (int i = 1; i <= n; i++)
            {
                currRow[0] = float.MaxValue;

                // Compute band bounds for row i
                int jStart = Mathf.Max(1, i - bandWidth);
                int jEnd = Mathf.Min(m, i + bandWidth);

                for (int j = 1; j < jStart; j++)
                    currRow[j] = float.MaxValue;

                for (int j = jStart; j <= jEnd; j++)
                {
                    float cost = Mathf.Abs((actualTimingsSec[i - 1] - expectedTimingsSec[j - 1]) * 1000f);

                    float min = prevRow[j];       // insertion
                    if (prevRow[j - 1] < min) min = prevRow[j - 1]; // match
                    if (currRow[j - 1] < min) min = currRow[j - 1]; // deletion

                    if (min < float.MaxValue)
                    {
                        currRow[j] = cost + min;
                        if (j != i) warpsUsed++; // track non-diagonal warps
                    }
                    else
                    {
                        currRow[j] = float.MaxValue;
                    }
                }

                for (int j = jEnd + 1; j <= m; j++)
                    currRow[j] = float.MaxValue;

                var temp = prevRow;
                prevRow = currRow;
                currRow = temp;
            }

            float dtwDistance = prevRow[m];
            if (dtwDistance >= float.MaxValue * 0.5f)
                return new DtwTimingResult(float.MaxValue, 0f, 0f, 0);

            int warpPathLen = Mathf.Max(n, m);
            float avgWarpMs = warpsUsed > 0 ? dtwDistance / warpsUsed : 0f;

            // Normalize: perfect alignment = 1.0, >2×maxWarp = approaches 0
            float maxExpectedCost = maxWarpMs * warpPathLen;
            float normalizedScore = maxExpectedCost > 0f
                ? Mathf.Clamp01(1f - (dtwDistance / maxExpectedCost))
                : 1f;

            return new DtwTimingResult(dtwDistance, normalizedScore, avgWarpMs, warpPathLen);
        }

        #endregion
    }

    #region Configurable Validation Weights

    public struct TimingValidationConfig
    {
        public float DistributionWeight;
        public float AutocorrelationWeight;
        public float DriftWeight;
        public float MeanBiasWeight;

        public float AutocorrelationSuspiciousThreshold;
        public float DriftSuspiciousThresholdMs;
        public float ZeroMeanThresholdMs;
        public float PerfectConsistencyThresholdMs;
        public float SubFrameThresholdMs;

        public float SuspiciousPenalty1;
        public float SuspiciousPenalty2;
        public float SuspiciousPenalty3;

        public float PassThreshold;

        public static TimingValidationConfig Casual => new()
        {
            DistributionWeight = 10f, AutocorrelationWeight = 20f,
            DriftWeight = 5f, MeanBiasWeight = 5f,
            AutocorrelationSuspiciousThreshold = 0.05f,
            DriftSuspiciousThresholdMs = 0.01f,
            ZeroMeanThresholdMs = 1f,
            PerfectConsistencyThresholdMs = 0.5f,
            SubFrameThresholdMs = 1f,
            SuspiciousPenalty1 = 0.8f, SuspiciousPenalty2 = 0.6f, SuspiciousPenalty3 = 0.4f,
            PassThreshold = 0.35f
        };

        public static TimingValidationConfig Normal => new()
        {
            DistributionWeight = 15f, AutocorrelationWeight = 30f,
            DriftWeight = 10f, MeanBiasWeight = 10f,
            AutocorrelationSuspiciousThreshold = 0.10f,
            DriftSuspiciousThresholdMs = 0.02f,
            ZeroMeanThresholdMs = 2f,
            PerfectConsistencyThresholdMs = 0.5f,
            SubFrameThresholdMs = 1f,
            SuspiciousPenalty1 = 0.7f, SuspiciousPenalty2 = 0.5f, SuspiciousPenalty3 = 0.3f,
            PassThreshold = 0.45f
        };

        public static TimingValidationConfig Hard => new()
        {
            DistributionWeight = 18f, AutocorrelationWeight = 35f,
            DriftWeight = 12f, MeanBiasWeight = 12f,
            AutocorrelationSuspiciousThreshold = 0.12f,
            DriftSuspiciousThresholdMs = 0.03f,
            ZeroMeanThresholdMs = 3f,
            PerfectConsistencyThresholdMs = 0.5f,
            SubFrameThresholdMs = 1f,
            SuspiciousPenalty1 = 0.6f, SuspiciousPenalty2 = 0.4f, SuspiciousPenalty3 = 0.2f,
            PassThreshold = 0.50f
        };

        public static TimingValidationConfig Pro => new()
        {
            DistributionWeight = 20f, AutocorrelationWeight = 40f,
            DriftWeight = 16f, MeanBiasWeight = 16f,
            AutocorrelationSuspiciousThreshold = 0.15f,
            DriftSuspiciousThresholdMs = 0.04f,
            ZeroMeanThresholdMs = 4f,
            PerfectConsistencyThresholdMs = 0.5f,
            SubFrameThresholdMs = 2f,
            SuspiciousPenalty1 = 0.5f, SuspiciousPenalty2 = 0.3f, SuspiciousPenalty3 = 0.15f,
            PassThreshold = 0.55f
        };

        public static TimingValidationConfig Tournament => new()
        {
            DistributionWeight = 20f, AutocorrelationWeight = 40f,
            DriftWeight = 20f, MeanBiasWeight = 20f,
            AutocorrelationSuspiciousThreshold = 0.15f,
            DriftSuspiciousThresholdMs = 0.05f,
            ZeroMeanThresholdMs = 5f,
            PerfectConsistencyThresholdMs = 0.3f,
            SubFrameThresholdMs = 1f,
            SuspiciousPenalty1 = 0.4f, SuspiciousPenalty2 = 0.2f, SuspiciousPenalty3 = 0.1f,
            PassThreshold = 0.60f
        };

        public static TimingValidationConfig AntiCheatFocus => new()
        {
            DistributionWeight = 10f, AutocorrelationWeight = 50f,
            DriftWeight = 25f, MeanBiasWeight = 15f,
            AutocorrelationSuspiciousThreshold = 0.20f,
            DriftSuspiciousThresholdMs = 0.02f,
            ZeroMeanThresholdMs = 3f,
            PerfectConsistencyThresholdMs = 0.5f,
            SubFrameThresholdMs = 1f,
            SuspiciousPenalty1 = 0.3f, SuspiciousPenalty2 = 0.1f, SuspiciousPenalty3 = 0.05f,
            PassThreshold = 0.50f
        };
    }

    #endregion
}
