using System.Runtime.CompilerServices;
using Unity.Mathematics;

namespace CycloneGames.AIPerception.Runtime
{
    internal static class PerceptionNumerics
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TryGetFiniteDistance(
            in float3 origin,
            in float3 target,
            out float3 offset,
            out float distance)
        {
            offset = target - origin;
            distance = 0f;
            if (!math.all(math.isfinite(origin)) || !math.all(math.isfinite(target)))
            {
                return false;
            }

            float distanceSquared = math.lengthsq(offset);
            if (math.isfinite(distanceSquared))
            {
                distance = math.sqrt(distanceSquared);
                return math.isfinite(distance);
            }

            double x = (double)target.x - origin.x;
            double y = (double)target.y - origin.y;
            double z = (double)target.z - origin.z;
            double preciseDistance = math.sqrt((x * x) + (y * y) + (z * z));
            if (!math.isfinite(preciseDistance) || preciseDistance > float.MaxValue)
            {
                return false;
            }

            offset = new float3((float)x, (float)y, (float)z);
            distance = (float)preciseDistance;
            return math.all(math.isfinite(offset)) && math.isfinite(distance);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TryGetFiniteDirectionAndDistance(
            in float3 origin,
            in float3 target,
            out float3 direction,
            out float distance)
        {
            if (!TryGetFiniteDistance(in origin, in target, out float3 offset, out distance))
            {
                direction = float3.zero;
                return false;
            }

            if (distance <= 0.000001f)
            {
                direction = float3.zero;
                return true;
            }

            direction = offset / distance;
            return math.all(math.isfinite(direction));
        }
    }
}
