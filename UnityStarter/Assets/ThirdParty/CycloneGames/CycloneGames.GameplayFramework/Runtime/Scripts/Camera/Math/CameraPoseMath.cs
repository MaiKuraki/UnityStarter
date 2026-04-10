using System.Runtime.CompilerServices;
using Unity.Burst;
using Unity.Mathematics;

namespace CycloneGames.GameplayFramework.Runtime
{
    /// <summary>
    /// Pure math helpers for camera pose calculations.
    ///
    /// Methods here avoid UnityEngine.Object access so they are friendly to future Burst use
    /// (for example when moved into jobs or function-pointer based pipelines).
    /// </summary>
    public static class CameraPoseMath
    {
        [BurstCompile(FloatMode = FloatMode.Fast, FloatPrecision = FloatPrecision.Standard)]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float ExponentialDecayT(float speed, float deltaTime)
        {
            float clampedSpeed = math.max(0f, speed);
            float clampedDt = math.max(0f, deltaTime);
            return 1f - math.exp(-clampedSpeed * clampedDt);
        }

        [BurstCompile(FloatMode = FloatMode.Fast, FloatPrecision = FloatPrecision.Standard)]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static quaternion LookRotationSafe(float3 direction, quaternion fallback)
        {
            float lenSq = math.lengthsq(direction);
            if (lenSq < 1e-8f)
            {
                return fallback;
            }

            return quaternion.LookRotationSafe(math.normalize(direction), math.up());
        }

        [BurstCompile(FloatMode = FloatMode.Fast, FloatPrecision = FloatPrecision.Standard)]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsInsideAngularDeadZone(quaternion referenceRotation, float3 lookDirection, float halfAngleXDeg, float halfAngleYDeg)
        {
            float lenSq = math.lengthsq(lookDirection);
            if (lenSq < 1e-8f)
            {
                return true;
            }

            float3 local = math.mul(math.inverse(referenceRotation), math.normalize(lookDirection));
            if (local.z <= 0f)
            {
                return false;
            }

            float angX = math.degrees(math.atan2(local.x, local.z));
            float angY = math.degrees(math.atan2(local.y, local.z));

            return math.abs(angX) <= math.max(0f, halfAngleXDeg)
                && math.abs(angY) <= math.max(0f, halfAngleYDeg);
        }
    }
}
