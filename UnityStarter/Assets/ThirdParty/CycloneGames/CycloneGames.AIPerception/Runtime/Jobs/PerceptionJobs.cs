using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace CycloneGames.AIPerception.Runtime.Jobs
{
    /// <summary>
    /// Burst-compiled job for sight cone detection (pre-filter without LOS).
    /// LOS checks are performed on main thread after job completion.
    /// </summary>
    [BurstCompile(FloatPrecision.Standard, FloatMode.Fast)]
    public struct SightConeQueryJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<PerceptibleData> Targets;
        [ReadOnly] public float3 Origin;
        [ReadOnly] public float3 Forward;
        [ReadOnly] public float MaxDistanceSq;
        [ReadOnly] public float CosHalfAngle;
        [ReadOnly] public int TargetTypeId;
        [ReadOnly] public bool FilterByType;

        // Output: 1 = passed pre-filter, 0 = failed
        [WriteOnly] public NativeArray<int> PassedFilter;

        public void Execute(int index)
        {
            PassedFilter[index] = 0;

            var target = Targets[index];

            if (!target.IsDetectable) return;
            if (FilterByType && target.TypeId != TargetTypeId) return;

            float3 toTarget = target.Position - Origin;
            float distSq = math.lengthsq(toTarget);

            if (distSq > MaxDistanceSq) return;
            if (distSq < 0.000001f) return;

            float invDist = math.rsqrt(distSq);
            float3 dirToTarget = toTarget * invDist;
            float dot = math.dot(Forward, dirToTarget);

            if (dot < CosHalfAngle) return;

            PassedFilter[index] = 1;
        }
    }

    /// <summary>
    /// Burst-compiled job for sphere/hearing detection.
    /// </summary>
    [BurstCompile(FloatPrecision.Standard, FloatMode.Fast)]
    public struct SphereQueryJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<PerceptibleData> Targets;
        [ReadOnly] public float3 Origin;
        [ReadOnly] public float Radius;
        [ReadOnly] public int TargetTypeId;
        [ReadOnly] public bool FilterByType;

        // Output: audibility value (0 = not heard, >0 = audibility strength)
        [WriteOnly] public NativeArray<float> Audibility;

        public void Execute(int index)
        {
            Audibility[index] = 0f;

            var target = Targets[index];

            if (!target.IsDetectable) return;
            if (FilterByType && target.TypeId != TargetTypeId) return;

            float3 toTarget = target.Position - Origin;
            float distSq = math.lengthsq(toTarget);

            float effectiveRadius = Radius * target.Loudness + target.DetectionRadius;
            float effectiveRadiusSq = effectiveRadius * effectiveRadius;

            if (distSq > effectiveRadiusSq) return;

            float dist = math.sqrt(distSq);
            float audibility = 1f - (dist / effectiveRadius);
            audibility = math.clamp(audibility, 0f, 1f);

            Audibility[index] = audibility;
        }
    }
}
