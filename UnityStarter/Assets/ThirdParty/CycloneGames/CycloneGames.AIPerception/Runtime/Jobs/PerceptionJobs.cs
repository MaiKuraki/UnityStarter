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
    [BurstCompile(FloatPrecision.Standard, FloatMode.Strict)]
    public struct SightConeQueryJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<PerceptibleData> Targets;
        [ReadOnly] public NativeArray<int> CandidateIndices;
        [ReadOnly] public float3 Origin;
        [ReadOnly] public float3 Forward;
        [ReadOnly] public float MaxDistance;
        [ReadOnly] public float CosHalfAngle;
        [ReadOnly] public int TargetTypeId;
        [ReadOnly] public bool FilterByType;
        [ReadOnly] public PerceptibleHandle IgnoredTarget;

        // Output: 1 = passed pre-filter, 0 = failed
        [WriteOnly] public NativeArray<int> PassedFilter;

        public void Execute(int index)
        {
            PassedFilter[index] = 0;

            var target = Targets[CandidateIndices[index]];

            if (!target.IsDetectable) return;
            if (target.ToHandle() == IgnoredTarget) return;
            if (FilterByType && target.TypeId != TargetTypeId) return;

            float3 toTarget = target.Position - Origin;
            float distSq = math.lengthsq(toTarget);
            float effectiveDistance = MaxDistance + target.DetectionRadius;
            float effectiveDistanceSq = effectiveDistance * effectiveDistance;

            if (!math.all(math.isfinite(target.Position)) || !math.all(math.isfinite(Origin)) ||
                !math.isfinite(MaxDistance) || !math.isfinite(target.DetectionRadius)) return;

            if (!math.isfinite(distSq) || !math.isfinite(effectiveDistance) ||
                !math.isfinite(effectiveDistanceSq))
            {
                if (!PerceptionNumerics.TryGetFiniteDirectionAndDistance(
                        in Origin,
                        in target.Position,
                        out float3 preciseDirection,
                        out float preciseDistance))
                {
                    PassedFilter[index] = -1;
                    return;
                }

                double preciseEffectiveDistance = (double)MaxDistance + target.DetectionRadius;
                if (preciseEffectiveDistance <= 0d || preciseDistance > preciseEffectiveDistance)
                {
                    return;
                }

                if (preciseDistance < 0.001f || math.dot(Forward, preciseDirection) >= CosHalfAngle)
                {
                    PassedFilter[index] = 1;
                }

                return;
            }

            if (effectiveDistance <= 0f || distSq > effectiveDistanceSq) return;
            if (distSq < 0.000001f)
            {
                PassedFilter[index] = 1;
                return;
            }

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
    [BurstCompile(FloatPrecision.Standard, FloatMode.Strict)]
    public struct SphereQueryJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<PerceptibleData> Targets;
        [ReadOnly] public NativeArray<int> CandidateIndices;
        [ReadOnly] public float3 Origin;
        [ReadOnly] public float Radius;
        [ReadOnly] public int TargetTypeId;
        [ReadOnly] public bool FilterByType;
        [ReadOnly] public PerceptibleHandle IgnoredTarget;

        // Output: audibility value (0 = not heard, >0 = audibility strength)
        [WriteOnly] public NativeArray<float> Audibility;

        public void Execute(int index)
        {
            Audibility[index] = 0f;

            var target = Targets[CandidateIndices[index]];

            if (!target.IsDetectable) return;
            if (!target.IsSoundSource) return;
            if (target.ToHandle() == IgnoredTarget) return;
            if (FilterByType && target.TypeId != TargetTypeId) return;

            float3 toTarget = target.Position - Origin;
            float distSq = math.lengthsq(toTarget);

            float effectiveRadius = Radius * target.Loudness + target.DetectionRadius;
            float effectiveRadiusSq = effectiveRadius * effectiveRadius;
            if (!math.all(math.isfinite(target.Position)) || !math.all(math.isfinite(Origin)) ||
                !math.isfinite(Radius) || !math.isfinite(target.Loudness) ||
                !math.isfinite(target.DetectionRadius)) return;

            if (!math.isfinite(distSq) || !math.isfinite(effectiveRadius) ||
                !math.isfinite(effectiveRadiusSq))
            {
                if (!PerceptionNumerics.TryGetFiniteDistance(
                        in Origin,
                        in target.Position,
                        out _,
                        out float preciseDistance))
                {
                    Audibility[index] = -1f;
                    return;
                }

                double preciseEffectiveRadius = ((double)Radius * target.Loudness) + target.DetectionRadius;
                if (preciseEffectiveRadius <= 0d || preciseDistance > preciseEffectiveRadius)
                {
                    return;
                }

                Audibility[index] = math.clamp((float)(1d - (preciseDistance / preciseEffectiveRadius)), 0f, 1f);
                return;
            }

            if (effectiveRadius <= 0f) return;

            if (distSq > effectiveRadiusSq) return;

            float dist = math.sqrt(distSq);
            float audibility = 1f - (dist / effectiveRadius);
            audibility = math.clamp(audibility, 0f, 1f);

            Audibility[index] = audibility;
        }
    }

    /// <summary>
    /// Burst-compiled job for proximity detection (pure sphere without occlusion).
    /// Uses DetectionRadius from perceptibles for combined trigger zone calculation.
    /// </summary>
    [BurstCompile(FloatPrecision.Standard, FloatMode.Strict)]
    public struct ProximityQueryJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<PerceptibleData> Targets;
        [ReadOnly] public NativeArray<int> CandidateIndices;
        [ReadOnly] public float3 Origin;
        [ReadOnly] public float Radius;
        [ReadOnly] public int TargetTypeId;
        [ReadOnly] public bool FilterByType;
        [ReadOnly] public PerceptibleHandle IgnoredTarget;

        // Output: proximity intensity (0 = outside range, 1 = at center)
        [WriteOnly] public NativeArray<float> Proximity;

        public void Execute(int index)
        {
            Proximity[index] = 0f;

            var target = Targets[CandidateIndices[index]];

            if (!target.IsDetectable) return;
            if (target.ToHandle() == IgnoredTarget) return;
            if (FilterByType && target.TypeId != TargetTypeId) return;

            float3 toTarget = target.Position - Origin;
            float distSq = math.lengthsq(toTarget);

            float effectiveRadius = Radius + target.DetectionRadius;
            float effectiveRadiusSq = effectiveRadius * effectiveRadius;
            if (!math.all(math.isfinite(target.Position)) || !math.all(math.isfinite(Origin)) ||
                !math.isfinite(Radius) || !math.isfinite(target.DetectionRadius)) return;

            if (!math.isfinite(distSq) || !math.isfinite(effectiveRadius) ||
                !math.isfinite(effectiveRadiusSq))
            {
                if (!PerceptionNumerics.TryGetFiniteDistance(
                        in Origin,
                        in target.Position,
                        out _,
                        out float preciseDistance))
                {
                    Proximity[index] = -1f;
                    return;
                }

                double preciseEffectiveRadius = (double)Radius + target.DetectionRadius;
                if (preciseEffectiveRadius <= 0d || preciseDistance > preciseEffectiveRadius)
                {
                    return;
                }

                Proximity[index] = math.clamp((float)(1d - (preciseDistance / preciseEffectiveRadius)), 0f, 1f);
                return;
            }

            if (effectiveRadius <= 0f) return;

            if (distSq > effectiveRadiusSq) return;

            float dist = math.sqrt(distSq);
            float proximity = 1f - (dist / effectiveRadius);
            proximity = math.clamp(proximity, 0f, 1f);

            Proximity[index] = proximity;
        }
    }
}
