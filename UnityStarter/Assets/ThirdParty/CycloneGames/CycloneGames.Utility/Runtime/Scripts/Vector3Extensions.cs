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
        public static float DistanceSquared(this Vector3 a, Vector3 b)
        {
            float3 diff = new float3(a.x - b.x, a.y - b.y, a.z - b.z);
            return math.lengthsq(diff);
        }

        /// <summary>
        /// Checks if the vector is approximately zero within a small epsilon.
        /// </summary>
        public static bool IsZero(this Vector3 v, float epsilon = 1e-5f)
        {
            return math.lengthsq(new float3(v.x, v.y, v.z)) < epsilon * epsilon;
        }

        /// <summary>
        /// Clamps the magnitude of the vector to the specified maximum length.
        /// </summary>
        public static Vector3 ClampMagnitude(this Vector3 v, float maxLength)
        {
            float sqrMag = v.sqrMagnitude;
            if (sqrMag > maxLength * maxLength)
            {
                float3 normalized = math.normalize(new float3(v.x, v.y, v.z));
                return new Vector3(normalized.x * maxLength, normalized.y * maxLength, normalized.z * maxLength);
            }
            return v;
        }

        /// <summary>
        /// Projects the vector onto a plane defined by its normal.
        /// </summary>
        public static Vector3 ProjectOnPlane(this Vector3 v, Vector3 planeNormal)
        {
            float3 normal = math.normalize(new float3(planeNormal.x, planeNormal.y, planeNormal.z));
            float3 vec = new float3(v.x, v.y, v.z);
            float3 projection = vec - math.dot(vec, normal) * normal;
            return new Vector3(projection.x, projection.y, projection.z);
        }

        /// <summary>
        /// Returns the angle in degrees between two vectors.
        /// </summary>
        public static float Angle(this Vector3 from, Vector3 to)
        {
            float3 a = math.normalize(new float3(from.x, from.y, from.z));
            float3 b = math.normalize(new float3(to.x, to.y, to.z));
            return math.degrees(math.acos(math.clamp(math.dot(a, b), -1f, 1f)));
        }

        /// <summary>
        /// Rotates the vector around an axis by the specified angle in degrees.
        /// </summary>
        public static Vector3 RotateAround(this Vector3 v, Vector3 axis, float angleDegrees)
        {
            float3 vec = new float3(v.x, v.y, v.z);
            float3 ax = math.normalize(new float3(axis.x, axis.y, axis.z));
            float angleRad = math.radians(angleDegrees);
            float3 rotated = math.mul(quaternion.AxisAngle(ax, angleRad), vec);
            return new Vector3(rotated.x, rotated.y, rotated.z);
        }

        /// <summary>
        /// Returns a vector with the absolute values of each component.
        /// </summary>
        public static Vector3 Abs(this Vector3 v)
        {
            return new Vector3(math.abs(v.x), math.abs(v.y), math.abs(v.z));
        }

        /// <summary>
        /// Returns a vector with the sign (-1, 0, or 1) of each component.
        /// </summary>
        public static Vector3 Sign(this Vector3 v)
        {
            return new Vector3(math.sign(v.x), math.sign(v.y), math.sign(v.z));
        }

        /// <summary>
        /// Returns a vector with the minimum components of two vectors.
        /// </summary>
        public static Vector3 Min(this Vector3 a, Vector3 b)
        {
            return new Vector3(math.min(a.x, b.x), math.min(a.y, b.y), math.min(a.z, b.z));
        }

        /// <summary>
        /// Returns a vector with the maximum components of two vectors.
        /// </summary>
        public static Vector3 Max(this Vector3 a, Vector3 b)
        {
            return new Vector3(math.max(a.x, b.x), math.max(a.y, b.y), math.max(a.z, b.z));
        }

        /// <summary>
        /// Linearly interpolates between two vectors without clamping.
        /// </summary>
        public static Vector3 LerpUnclamped(this Vector3 a, Vector3 b, float t)
        {
            return new Vector3(math.lerp(a.x, b.x, t), math.lerp(a.y, b.y, t), math.lerp(a.z, b.z, t));
        }

        /// <summary>
        /// Moves the vector towards a target vector by a maximum distance.
        /// </summary>
        public static Vector3 MoveTowards(this Vector3 current, Vector3 target, float maxDistanceDelta)
        {
            float3 diff = new float3(target.x - current.x, target.y - current.y, target.z - current.z);
            float sqrDist = math.lengthsq(diff);
            if (sqrDist <= maxDistanceDelta * maxDistanceDelta)
                return target;
            float dist = math.sqrt(sqrDist);
            float3 direction = diff / dist;
            return new Vector3(current.x + direction.x * maxDistanceDelta,
                              current.y + direction.y * maxDistanceDelta,
                              current.z + direction.z * maxDistanceDelta);
        }
    }
}
