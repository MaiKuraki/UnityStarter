using System;

namespace CycloneGames.AnimationRuntime.Core
{
    /// <summary>
    /// Polar (direction + magnitude) blend used by 8-way strafe and aim offsets.
    /// Each child has an angle in degrees; the parameter picks the two children bracketing that angle.
    /// Below the dead zone the center child wins, which is what keeps idle stable when the stick is released.
    /// Allocation-free and engine-independent.
    /// </summary>
    /// <remarks>
    /// Precondition: <c>angles</c> are normalized into [0, 360) and strictly ascending.
    /// Not validated per call; <see cref="DirectionalBlendNode"/> checks it once at construction.
    /// </remarks>
    public static class DirectionalBlendMath
    {
        public const float FullCircle = 360f;
        public const float HalfCircle = 180f;

        /// <summary>Normalizes an angle into [0, 360).</summary>
        public static float NormalizeAngle(float degrees)
        {
            if (float.IsNaN(degrees) || float.IsInfinity(degrees))
            {
                return 0f;
            }

            degrees %= FullCircle;
            if (degrees < 0f)
            {
                degrees += FullCircle;
            }

            return degrees;
        }

        /// <summary>
        /// Fills <paramref name="weights"/> for the given child angles (ascending, in [0, 360)) and parameter.
        /// </summary>
        /// <param name="angles">Child angles in degrees, ascending. Length must equal the logical child count.</param>
        /// <param name="centerIndex">Child used below the dead zone, or -1 when there is no center child.</param>
        /// <param name="angle">Parameter direction in degrees.</param>
        /// <param name="magnitude">Parameter length in [0, 1].</param>
        /// <param name="deadZone">Magnitude at or below which the center child wins.</param>
        /// <param name="weights">Destination buffer; must be at least as long as <paramref name="angles"/>.</param>
        /// <returns>Number of non-zero weights.</returns>
        public static int Evaluate(
            ReadOnlySpan<float> angles,
            int centerIndex,
            float angle,
            float magnitude,
            float deadZone,
            Span<float> weights)
        {
            int count = angles.Length;
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

            if (magnitude <= deadZone && centerIndex >= 0 && centerIndex < count)
            {
                weights[centerIndex] = 1f;
                return 1;
            }

            float target = NormalizeAngle(angle);

            int lower = -1;
            for (int i = 0; i < count; i++)
            {
                if (angles[i] <= target)
                {
                    lower = i;
                }
                else
                {
                    break;
                }
            }

            int indexA;
            int indexB;
            float angleA;
            float angleB;

            if (lower < 0)
            {
                indexA = count - 1;
                indexB = 0;
                angleA = angles[count - 1] - FullCircle;
                angleB = angles[0];
            }
            else if (lower == count - 1)
            {
                indexA = count - 1;
                indexB = 0;
                angleA = angles[count - 1];
                angleB = angles[0] + FullCircle;
            }
            else
            {
                indexA = lower;
                indexB = lower + 1;
                angleA = angles[indexA];
                angleB = angles[indexB];
            }

            float span = angleB - angleA;
            float t = span > 1e-4f ? (target - angleA) / span : 0f;
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
                weights[indexA] = first;
            }

            if (t > 0f)
            {
                weights[indexB] = t;
            }

            return (first > 0f ? 1 : 0) + (t > 0f ? 1 : 0);
        }

        /// <summary>Shortest signed difference from <paramref name="from"/> to <paramref name="to"/>, in (-180, 180].</summary>
        public static float DeltaAngle(float from, float to)
        {
            float delta = NormalizeAngle(to) - NormalizeAngle(from);
            if (delta > HalfCircle)
            {
                delta -= FullCircle;
            }
            else if (delta <= -HalfCircle)
            {
                delta += FullCircle;
            }

            return delta;
        }
    }
}
