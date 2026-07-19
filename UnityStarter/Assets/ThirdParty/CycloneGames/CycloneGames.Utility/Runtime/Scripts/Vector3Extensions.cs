using System;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace CycloneGames.Utility.Runtime
{
    public static class Vector3Extensions
    {
        private const float NormalizationEpsilon = 1e-15f;

        /// <summary>
        /// Quantizes each component to the nearest multiple of <paramref name="quantization"/>.
        /// A negative step is treated as its absolute value. A near-zero step returns the input unchanged.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 Quantize(this Vector3 v, float quantization)
        {
            if (!IsFinite(quantization))
            {
                throw new ArgumentOutOfRangeException(nameof(quantization), "Quantization must be finite.");
            }

            float step = Mathf.Abs(quantization);
            if (step < 1e-6f)
            {
                return v;
            }

            return new Vector3(
                Mathf.Round(v.x / step) * step,
                Mathf.Round(v.y / step) * step,
                Mathf.Round(v.z / step) * step);
        }

        /// <summary>
        /// Returns the squared distance between two vectors. The result may overflow for extreme inputs.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float DistanceSquared(this Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x;
            float dy = a.y - b.y;
            float dz = a.z - b.z;
            return dx * dx + dy * dy + dz * dz;
        }

        /// <summary>
        /// Checks if the vector magnitude is smaller than the absolute epsilon.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsZero(this Vector3 v, float epsilon = 1e-5f)
        {
            if (float.IsNaN(epsilon))
            {
                return false;
            }

            epsilon = Mathf.Abs(epsilon);
            return v.x * v.x + v.y * v.y + v.z * v.z < epsilon * epsilon;
        }

        /// <summary>
        /// Clamps the magnitude of the vector to a non-negative maximum length.
        /// </summary>
        public static Vector3 ClampMagnitude(this Vector3 v, float maxLength)
        {
            if (float.IsNaN(maxLength) || maxLength < 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(maxLength), "Maximum length must be non-negative and not NaN.");
            }
            if (maxLength == 0f)
            {
                return Vector3.zero;
            }
            if (float.IsPositiveInfinity(maxLength))
            {
                return v;
            }
            if (!TryGetNormalized(v, out Vector3 normalized, out float maxComponent, out float scaledLength))
            {
                return v;
            }

            // Compare before multiplying to avoid overflowing the actual magnitude.
            if (maxComponent <= maxLength / scaledLength)
            {
                return v;
            }

            return normalized * maxLength;
        }

        /// <summary>
        /// Projects the vector onto a plane. The normal does not need to be normalized.
        /// A zero or non-finite normal leaves the vector unchanged.
        /// </summary>
        public static Vector3 ProjectOnPlane(this Vector3 v, Vector3 planeNormal)
        {
            if (!TryGetNormalized(planeNormal, out Vector3 normalized, out _, out _))
            {
                return v;
            }

            return v - Vector3.Dot(v, normalized) * normalized;
        }

        /// <summary>
        /// Returns the angle in degrees between two vectors, or zero if either vector cannot be normalized.
        /// </summary>
        public static float Angle(this Vector3 from, Vector3 to)
        {
            if (!TryGetNormalized(from, out Vector3 normalizedFrom, out _, out _) ||
                !TryGetNormalized(to, out Vector3 normalizedTo, out _, out _))
            {
                return 0f;
            }

            float dot = Mathf.Clamp(Vector3.Dot(normalizedFrom, normalizedTo), -1f, 1f);
            return Mathf.Acos(dot) * Mathf.Rad2Deg;
        }

        /// <summary>
        /// Rotates the vector around an axis by the specified angle in degrees.
        /// Unity normalizes a non-zero axis internally.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 RotateAround(this Vector3 v, Vector3 axis, float angleDegrees)
        {
            return Quaternion.AngleAxis(angleDegrees, axis) * v;
        }

        /// <summary>Returns a vector with the absolute value of each component.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 Abs(this Vector3 v)
        {
            return new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));
        }

        /// <summary>Returns a vector with the sign (-1, 0, or 1) of each component.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 Sign(this Vector3 v)
        {
            return new Vector3(SignComponent(v.x), SignComponent(v.y), SignComponent(v.z));
        }

        /// <summary>Returns a vector with the minimum components of two vectors.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 Min(this Vector3 a, Vector3 b)
        {
            return new Vector3(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Min(a.z, b.z));
        }

        /// <summary>Returns a vector with the maximum components of two vectors.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 Max(this Vector3 a, Vector3 b)
        {
            return new Vector3(Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y), Mathf.Max(a.z, b.z));
        }

        /// <summary>Linearly interpolates between two vectors without clamping t.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 LerpUnclamped(this Vector3 a, Vector3 b, float t)
        {
            return new Vector3(
                a.x + (b.x - a.x) * t,
                a.y + (b.y - a.y) * t,
                a.z + (b.z - a.z) * t);
        }

        /// <summary>
        /// Moves the vector towards a target by at most <paramref name="maxDistanceDelta"/>.
        /// A negative or zero delta leaves the current vector unchanged.
        /// </summary>
        public static Vector3 MoveTowards(this Vector3 current, Vector3 target, float maxDistanceDelta)
        {
            if (float.IsNaN(maxDistanceDelta))
            {
                throw new ArgumentOutOfRangeException(nameof(maxDistanceDelta), "Maximum distance delta must not be NaN.");
            }
            if (maxDistanceDelta <= 0f)
            {
                return current;
            }

            Vector3 delta = target - current;
            if (!TryGetNormalized(delta, out Vector3 direction, out float maxComponent, out float scaledLength))
            {
                return target;
            }
            if (float.IsPositiveInfinity(maxDistanceDelta) || maxComponent <= maxDistanceDelta / scaledLength)
            {
                return target;
            }

            return current + direction * maxDistanceDelta;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 WithX(this Vector3 v, float x) => new Vector3(x, v.y, v.z);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 WithY(this Vector3 v, float y) => new Vector3(v.x, y, v.z);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 WithZ(this Vector3 v, float z) => new Vector3(v.x, v.y, z);

        /// <summary>Returns the XZ projection with Y set to zero.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 Flat(this Vector3 v) => new Vector3(v.x, 0f, v.z);

        /// <summary>Returns the normalized direction from this position to the target.</summary>
        public static Vector3 DirectionTo(this Vector3 from, Vector3 to)
        {
            return TryGetNormalized(to - from, out Vector3 direction, out _, out _)
                ? direction
                : Vector3.zero;
        }

        /// <summary>
        /// Remaps each component from one finite range to another.
        /// </summary>
        /// <exception cref="ArgumentException">
        /// The vector or range is non-finite, the source range has zero length, or the result cannot be represented as finite floats.
        /// </exception>
        public static Vector3 Remap(this Vector3 v, float fromMin, float fromMax, float toMin, float toMax)
        {
            if (!TryRemap(v, fromMin, fromMax, toMin, toMax, out Vector3 result))
            {
                throw new ArgumentException(
                    "Remap inputs must be finite, the source range must have non-zero length, and the result must fit finite floats.");
            }
            return result;
        }

        /// <summary>
        /// Attempts to remap each component from one finite range to another.
        /// </summary>
        public static bool TryRemap(
            this Vector3 v,
            float fromMin,
            float fromMax,
            float toMin,
            float toMax,
            out Vector3 result)
        {
            if (!IsFinite(v.x) || !IsFinite(v.y) || !IsFinite(v.z) ||
                !IsFinite(fromMin) || !IsFinite(fromMax) ||
                !IsFinite(toMin) || !IsFinite(toMax) ||
                fromMin == fromMax)
            {
                result = default;
                return false;
            }

            double sourceRange = (double)fromMax - fromMin;
            double targetRange = (double)toMax - toMin;
            double scale = targetRange / sourceRange;
            double x = ((double)v.x - fromMin) * scale + toMin;
            double y = ((double)v.y - fromMin) * scale + toMin;
            double z = ((double)v.z - fromMin) * scale + toMin;
            if (!CanRepresentAsFiniteFloat(x) ||
                !CanRepresentAsFiniteFloat(y) ||
                !CanRepresentAsFiniteFloat(z))
            {
                result = default;
                return false;
            }

            result = new Vector3((float)x, (float)y, (float)z);
            return true;
        }

        /// <summary>Clamps each component independently between finite ordered bounds.</summary>
        public static Vector3 ClampComponents(this Vector3 v, float min, float max)
        {
            if (!IsFinite(min) || !IsFinite(max) || min > max)
            {
                throw new ArgumentException("Clamp bounds must be finite and min must not exceed max.");
            }

            return new Vector3(
                Mathf.Clamp(v.x, min, max),
                Mathf.Clamp(v.y, min, max),
                Mathf.Clamp(v.z, min, max));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float MaxComponent(this Vector3 v) => Mathf.Max(v.x, Mathf.Max(v.y, v.z));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float MinComponent(this Vector3 v) => Mathf.Min(v.x, Mathf.Min(v.y, v.z));

        private static bool TryGetNormalized(
            Vector3 value,
            out Vector3 normalized,
            out float maxComponent,
            out float scaledLength)
        {
            maxComponent = Mathf.Max(Mathf.Abs(value.x), Mathf.Max(Mathf.Abs(value.y), Mathf.Abs(value.z)));
            if (!IsFinite(maxComponent) || maxComponent <= NormalizationEpsilon)
            {
                normalized = Vector3.zero;
                scaledLength = 0f;
                return false;
            }

            float x = value.x / maxComponent;
            float y = value.y / maxComponent;
            float z = value.z / maxComponent;
            scaledLength = Mathf.Sqrt(x * x + y * y + z * z);
            if (!IsFinite(scaledLength) || scaledLength <= NormalizationEpsilon)
            {
                normalized = Vector3.zero;
                return false;
            }

            float inverseLength = 1f / scaledLength;
            normalized = new Vector3(x * inverseLength, y * inverseLength, z * inverseLength);
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float SignComponent(float value)
        {
            return value > 0f ? 1f : value < 0f ? -1f : 0f;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool CanRepresentAsFiniteFloat(double value)
        {
            return !double.IsNaN(value) &&
                   !double.IsInfinity(value) &&
                   value >= -float.MaxValue &&
                   value <= float.MaxValue;
        }
    }
}
