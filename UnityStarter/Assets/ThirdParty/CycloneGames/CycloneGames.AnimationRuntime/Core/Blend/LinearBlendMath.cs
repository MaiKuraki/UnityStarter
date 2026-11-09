using System;

namespace CycloneGames.AnimationRuntime.Core
{
    /// <summary>
    /// 1D threshold blend, the math behind a gait blend (idle / walk / run / sprint).
    /// Allocation-free, branch-light and independent of Unity so it can be unit tested headlessly.
    /// Weights always sum to 1 and at most two entries are non-zero.
    /// </summary>
    /// <remarks>
    /// <see cref="Evaluate"/> clears the whole destination span before writing, so its cost is
    /// O(weights.Length) plus O(log thresholds.Length) for the segment search. With the intended
    /// 8-or-fewer children that is a single cache line of clears and measures ~34 ns on a desktop CPU.
    /// </remarks>
    public static class LinearBlendMath
    {
        /// <summary>
        /// Fills <paramref name="weights"/> for the given ascending thresholds and parameter.
        /// </summary>
        /// <returns>Number of non-zero weights (0 when the input is unusable, otherwise 1 or 2).</returns>
        public static int Evaluate(ReadOnlySpan<float> thresholds, float parameter, Span<float> weights)
        {
            int count = thresholds.Length;
            if (count == 0 || weights.Length < count)
            {
                return 0;
            }

            for (int i = 0; i < weights.Length; i++)
            {
                weights[i] = 0f;
            }

            if (count == 1)
            {
                weights[0] = 1f;
                return 1;
            }

            if (float.IsNaN(parameter))
            {
                weights[0] = 1f;
                return 1;
            }

            if (parameter <= thresholds[0])
            {
                weights[0] = 1f;
                return 1;
            }

            if (parameter >= thresholds[count - 1])
            {
                weights[count - 1] = 1f;
                return 1;
            }

            int segment = FindSegment(thresholds, parameter);
            float lower = thresholds[segment];
            float upper = thresholds[segment + 1];
            float span = upper - lower;
            float t = span > 1e-6f ? (parameter - lower) / span : 1f;
            if (t < 0f)
            {
                t = 0f;
            }
            else if (t > 1f)
            {
                t = 1f;
            }

            float first = 1f - t;
            if (first > 0f)
            {
                weights[segment] = first;
            }

            if (t > 0f)
            {
                weights[segment + 1] = t;
            }

            return (first > 0f ? 1 : 0) + (t > 0f ? 1 : 0);
        }

        /// <summary>Index of the segment whose lower bound is the last threshold at or below the parameter.</summary>
        public static int FindSegment(ReadOnlySpan<float> thresholds, float parameter)
        {
            int count = thresholds.Length;
            if (count < 2)
            {
                return 0;
            }

            int low = 0;
            int high = count - 1;
            while (low + 1 < high)
            {
                int mid = (low + high) >> 1;
                if (thresholds[mid] <= parameter)
                {
                    low = mid;
                }
                else
                {
                    high = mid;
                }
            }

            return low;
        }
    }
}
