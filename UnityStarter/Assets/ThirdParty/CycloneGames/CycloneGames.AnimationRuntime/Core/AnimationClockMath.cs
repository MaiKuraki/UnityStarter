using System;

namespace CycloneGames.AnimationRuntime.Core
{
    /// <summary>Finite-value guards for the clip clock.</summary>
    /// <remarks>A finite clock multiplied by a finite speed can overflow to infinity.</remarks>
    public static class AnimationClockMath
    {
        private const long ExponentMask = 0x7FF0000000000000L;

        /// <summary>True for a finite double.</summary>
        /// <remarks>Exponent bits 0x7FF identify NaN and both infinities.</remarks>
        public static bool IsFinite(double value)
        {
            return (BitConverter.DoubleToInt64Bits(value) & ExponentMask) != ExponentMask;
        }

        /// <summary>True when a float is a finite real number.</summary>
        public static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        /// <summary>Computes the unfolded clip clock; false on overflow.</summary>
        public static bool TryUnwrapped(
            double startLocal,
            double startTick,
            double tick,
            double speed,
            out double unwrapped)
        {
            // One check on the result covers the subtraction, product and addition.
            double time = startLocal + ((tick - startTick) * speed);
            if (!IsFinite(time))
            {
                unwrapped = 0d;
                return false;
            }

            unwrapped = time;
            return true;
        }

        /// <summary>Fallback pose for a rejected clock step, clamped to clip bounds.</summary>
        public static double ClampFinite(double value, float length)
        {
            if (!IsFinite(value) || value <= 0d)
            {
                return 0d;
            }

            return value > length ? length : value;
        }
    }
}
