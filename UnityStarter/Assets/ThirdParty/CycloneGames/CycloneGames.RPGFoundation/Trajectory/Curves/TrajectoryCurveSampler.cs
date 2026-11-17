using System;
using CycloneGames.RPGFoundation.Trajectory.Core;

namespace CycloneGames.RPGFoundation.Trajectory.Curves
{
    /// <summary>
    /// Samples analytic curve shapes into a point list that
    /// <see cref="TrajectoryPolylineSolver.Trace"/> can solve.
    ///
    /// The split is deliberate: curve math lives here, collision lives in Core. Adding a new curve
    /// type never touches the solver, and the solver never gains a dependency on trigonometry.
    ///
    /// Sampling is uniform in the curve parameter, not in arc length. Dense curves therefore advance
    /// slower per point near their ends. Use more points when the chord error matters.
    ///
    /// Uses <see cref="Math.Sin"/> and <see cref="Math.Exp"/>, so results are reproducible on a given
    /// runtime but not guaranteed bit-identical across platforms. When bit-exact replay is required,
    /// sample through a fixed-point layer instead.
    /// </summary>
    public static class TrajectoryCurveSampler
    {
        /// <summary>
        /// Samples a quadratic Bezier curve. Writes <c>destination.Length</c> points including both
        /// endpoints and returns the count written, or 0 when the destination is too small.
        /// </summary>
        public static int SampleBezier2(
            TrajectoryVector3 p0,
            TrajectoryVector3 p1,
            TrajectoryVector3 p2,
            Span<TrajectoryVector3> destination)
        {
            int count = destination.Length;
            if (count < 2)
            {
                return 0;
            }

            for (int i = 0; i < count; i++)
            {
                float t = (float)i / (count - 1);
                float u = 1f - t;
                destination[i] = p0 * (u * u)
                    + p1 * (2f * u * t)
                    + p2 * (t * t);
            }

            return count;
        }

        /// <summary>
        /// Samples a cubic Bezier curve. Writes <c>destination.Length</c> points including both
        /// endpoints and returns the count written, or 0 when the destination is too small.
        /// </summary>
        public static int SampleBezier3(
            TrajectoryVector3 p0,
            TrajectoryVector3 p1,
            TrajectoryVector3 p2,
            TrajectoryVector3 p3,
            Span<TrajectoryVector3> destination)
        {
            int count = destination.Length;
            if (count < 2)
            {
                return 0;
            }

            for (int i = 0; i < count; i++)
            {
                float t = (float)i / (count - 1);
                float u = 1f - t;
                float uu = u * u;
                float tt = t * t;
                destination[i] = p0 * (uu * u)
                    + p1 * (3f * uu * t)
                    + p2 * (3f * u * tt)
                    + p3 * (tt * t);
            }

            return count;
        }

        /// <summary>
        /// Samples a sine wave travelling along <paramref name="forward"/> with lateral offset along
        /// <paramref name="up"/>.
        ///
        /// <paramref name="forward"/> is normalized internally; <paramref name="length"/> is the
        /// travelled distance. <paramref name="cycles"/> is the number of full periods over that
        /// distance. <paramref name="up"/> should not be parallel to the travel direction.
        /// </summary>
        public static int SampleSine(
            TrajectoryVector3 origin,
            TrajectoryVector3 forward,
            TrajectoryVector3 up,
            float length,
            float amplitude,
            float cycles,
            Span<TrajectoryVector3> destination)
        {
            return SampleWave(origin, forward, up, length, amplitude, cycles, 0f, destination);
        }

        /// <summary>
        /// Samples a sine wave whose amplitude decays as <c>exp(-decay * t)</c> with <c>t</c> in
        /// <c>[0,1]</c>. A <paramref name="decay"/> of zero is identical to <see cref="SampleSine"/>.
        /// </summary>
        public static int SampleDampedSine(
            TrajectoryVector3 origin,
            TrajectoryVector3 forward,
            TrajectoryVector3 up,
            float length,
            float amplitude,
            float cycles,
            float decay,
            Span<TrajectoryVector3> destination)
        {
            return SampleWave(origin, forward, up, length, amplitude, cycles, decay, destination);
        }

        /// <summary>
        /// Samples a ballistic parabola <c>p(t) = origin + v*t + 0.5*g*t^2</c> over
        /// <c>t in [0, duration]</c>.
        ///
        /// This lives in the curve layer on purpose: Core solves collision for a path it is given and
        /// has no notion of gravity. Sampling here keeps ballistic prediction composable with
        /// <see cref="TrajectoryPolylineSolver"/> while leaving flight simulation to the projectile
        /// module.
        /// </summary>
        public static int SampleParabolic(
            TrajectoryVector3 origin,
            TrajectoryVector3 initialVelocity,
            TrajectoryVector3 gravity,
            float duration,
            Span<TrajectoryVector3> destination)
        {
            int count = destination.Length;
            if (count < 2 || duration <= 0f)
            {
                return 0;
            }

            for (int i = 0; i < count; i++)
            {
                float t = duration * i / (count - 1);
                float halfSquare = 0.5f * t * t;
                destination[i] = origin + initialVelocity * t + gravity * halfSquare;
            }

            return count;
        }

        private static int SampleWave(
            TrajectoryVector3 origin,
            TrajectoryVector3 forward,
            TrajectoryVector3 up,
            float length,
            float amplitude,
            float cycles,
            float decay,
            Span<TrajectoryVector3> destination)
        {
            int count = destination.Length;
            if (count < 2 || length <= 0f)
            {
                return 0;
            }

            TrajectoryVector3 travel = forward.NormalizedOrZero();
            if (travel.LengthSquared <= 0f)
            {
                return 0;
            }

            const double twoPi = 6.283185307179586476925286766559;

            for (int i = 0; i < count; i++)
            {
                float t = (float)i / (count - 1);
                double phase = twoPi * cycles * t;
                double envelope = decay > 0f ? Math.Exp(-decay * t) : 1.0;
                float offset = (float)(Math.Sin(phase) * envelope) * amplitude;

                // The last point lands exactly on the travel axis: sin(2*pi*cycles) is zero only for
                // integer cycle counts, so clamp the endpoint explicitly to keep it predictable.
                if (i == count - 1 && decay <= 0f)
                {
                    offset = 0f;
                }

                destination[i] = origin + travel * (length * t) + up * offset;
            }

            return count;
        }
    }
}
