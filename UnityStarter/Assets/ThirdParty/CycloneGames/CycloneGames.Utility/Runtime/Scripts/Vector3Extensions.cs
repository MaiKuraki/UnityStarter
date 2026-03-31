using System.Runtime.CompilerServices;
using UnityEngine;
using Unity.Mathematics;

namespace CycloneGames.Utility.Runtime
{
    public static class Vector3Extensions
    {
        /// <summary>
        /// Quantizes the vector components to the nearest multiple of the quantization value.
        /// Uses math.round for optimal performance and cross-platform compatibility.
        /// Returns the original vector if quantization is zero or near-zero to avoid division by zero.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 Quantize(this Vector3 v, float quantization)
        {
            if (math.abs(quantization) < 1e-6f)
                return v;
            float3 q = new float3(quantization);
            float3 result = math.round(new float3(v.x, v.y, v.z) / q) * q;
            return new Vector3(result.x, result.y, result.z);
        }

        /// <summary>
        /// Returns the squared distance between two vectors. Avoids square root for better performance.
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
        /// Checks if the vector is approximately zero within a small epsilon.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsZero(this Vector3 v, float epsilon = 1e-5f)
        {
            return (v.x * v.x + v.y * v.y + v.z * v.z) < epsilon * epsilon;
        }

        /// <summary>
        /// Clamps the magnitude of the vector to the specified maximum length.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 ClampMagnitude(this Vector3 v, float maxLength)
        {
            float sqrMag = v.x * v.x + v.y * v.y + v.z * v.z;
            if (sqrMag > maxLength * maxLength)
            {
                float invMag = maxLength * math.rsqrt(sqrMag);
                return new Vector3(v.x * invMag, v.y * invMag, v.z * invMag);
            }
            return v;
        }

        /// <summary>
        /// Projects the vector onto a plane defined by its normal.
        /// The planeNormal must be a unit vector (not normalized internally for performance).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 ProjectOnPlane(this Vector3 v, Vector3 planeNormal)
        {
            float dot = v.x * planeNormal.x + v.y * planeNormal.y + v.z * planeNormal.z;
            return new Vector3(v.x - dot * planeNormal.x, v.y - dot * planeNormal.y, v.z - dot * planeNormal.z);
        }

        /// <summary>
        /// Returns the angle in degrees between two vectors.
        /// Returns 0 if either vector is zero-length.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Angle(this Vector3 from, Vector3 to)
        {
            float sqrMagFrom = from.x * from.x + from.y * from.y + from.z * from.z;
            float sqrMagTo = to.x * to.x + to.y * to.y + to.z * to.z;
            float denominator = math.sqrt(sqrMagFrom * sqrMagTo);
            if (denominator < 1e-15f)
                return 0f;
            float dot = (from.x * to.x + from.y * to.y + from.z * to.z) / denominator;
            return math.degrees(math.acos(math.clamp(dot, -1f, 1f)));
        }

        /// <summary>
        /// Rotates the vector around an axis by the specified angle in degrees.
        /// The axis must be a unit vector (not normalized internally for performance).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 RotateAround(this Vector3 v, Vector3 axis, float angleDegrees)
        {
            float3 rotated = math.mul(quaternion.AxisAngle(new float3(axis.x, axis.y, axis.z), math.radians(angleDegrees)), new float3(v.x, v.y, v.z));
            return new Vector3(rotated.x, rotated.y, rotated.z);
        }

        /// <summary>
        /// Returns a vector with the absolute values of each component.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 Abs(this Vector3 v)
        {
            return new Vector3(math.abs(v.x), math.abs(v.y), math.abs(v.z));
        }

        /// <summary>
        /// Returns a vector with the sign (-1, 0, or 1) of each component.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 Sign(this Vector3 v)
        {
            return new Vector3(math.sign(v.x), math.sign(v.y), math.sign(v.z));
        }

        /// <summary>
        /// Returns a vector with the minimum components of two vectors.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 Min(this Vector3 a, Vector3 b)
        {
            return new Vector3(math.min(a.x, b.x), math.min(a.y, b.y), math.min(a.z, b.z));
        }

        /// <summary>
        /// Returns a vector with the maximum components of two vectors.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 Max(this Vector3 a, Vector3 b)
        {
            return new Vector3(math.max(a.x, b.x), math.max(a.y, b.y), math.max(a.z, b.z));
        }

        /// <summary>
        /// Linearly interpolates between two vectors without clamping t.
        /// Allows overshoot for elastic/spring animations.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 LerpUnclamped(this Vector3 a, Vector3 b, float t)
        {
            return new Vector3(
                a.x + (b.x - a.x) * t,
                a.y + (b.y - a.y) * t,
                a.z + (b.z - a.z) * t);
        }

        /// <summary>
        /// Moves the vector towards a target vector by a maximum distance.
        /// If maxDistanceDelta is negative or zero, the current vector is returned unchanged.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 MoveTowards(this Vector3 current, Vector3 target, float maxDistanceDelta)
        {
            if (maxDistanceDelta <= 0f)
                return current;
            float dx = target.x - current.x;
            float dy = target.y - current.y;
            float dz = target.z - current.z;
            float sqrDist = dx * dx + dy * dy + dz * dz;
            if (sqrDist <= maxDistanceDelta * maxDistanceDelta)
                return target;
            float invDist = maxDistanceDelta * math.rsqrt(sqrDist);
            return new Vector3(current.x + dx * invDist, current.y + dy * invDist, current.z + dz * invDist);
        }

        // --- Component Swizzle ---

        /// <summary>
        /// Returns a copy of the vector with the X component replaced.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 WithX(this Vector3 v, float x)
        {
            return new Vector3(x, v.y, v.z);
        }

        /// <summary>
        /// Returns a copy of the vector with the Y component replaced.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 WithY(this Vector3 v, float y)
        {
            return new Vector3(v.x, y, v.z);
        }

        /// <summary>
        /// Returns a copy of the vector with the Z component replaced.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 WithZ(this Vector3 v, float z)
        {
            return new Vector3(v.x, v.y, z);
        }

        // --- Common Game Dev Utilities ---

        /// <summary>
        /// Returns the XZ flat projection (Y = 0). Common for top-down gameplay.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 Flat(this Vector3 v)
        {
            return new Vector3(v.x, 0f, v.z);
        }

        /// <summary>
        /// Returns the normalized direction from this vector to the target.
        /// Returns Vector3.zero if the two positions are approximately equal.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 DirectionTo(this Vector3 from, Vector3 to)
        {
            float dx = to.x - from.x;
            float dy = to.y - from.y;
            float dz = to.z - from.z;
            float sqrMag = dx * dx + dy * dy + dz * dz;
            if (sqrMag < 1e-10f)
                return Vector3.zero;
            float invMag = math.rsqrt(sqrMag);
            return new Vector3(dx * invMag, dy * invMag, dz * invMag);
        }

        /// <summary>
        /// Remaps each component of the vector from one range to another.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 Remap(this Vector3 v, float fromMin, float fromMax, float toMin, float toMax)
        {
            float invRange = 1f / (fromMax - fromMin);
            float range = toMax - toMin;
            return new Vector3(
                (v.x - fromMin) * invRange * range + toMin,
                (v.y - fromMin) * invRange * range + toMin,
                (v.z - fromMin) * invRange * range + toMin);
        }

        /// <summary>
        /// Clamps each component of the vector independently between min and max.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 ClampComponents(this Vector3 v, float min, float max)
        {
            return new Vector3(
                math.clamp(v.x, min, max),
                math.clamp(v.y, min, max),
                math.clamp(v.z, min, max));
        }

        /// <summary>
        /// Returns the largest component value of the vector.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float MaxComponent(this Vector3 v)
        {
            return math.max(v.x, math.max(v.y, v.z));
        }

        /// <summary>
        /// Returns the smallest component value of the vector.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float MinComponent(this Vector3 v)
        {
            return math.min(v.x, math.min(v.y, v.z));
        }
    }
}
