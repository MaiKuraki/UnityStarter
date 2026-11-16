using System;

namespace CycloneGames.RPGFoundation.Projectile.Core
{
    public readonly struct ProjectileDefinition
    {
        public readonly ProjectileDefinitionId DefinitionId;
        public readonly ProjectileGuidanceMode GuidanceMode;
        public readonly ProjectileLifecycleFlags LifecycleFlags;
        public readonly float InitialSpeed;
        public readonly float MaxSpeed;
        public readonly float Acceleration;
        public readonly float GravityScale;
        public readonly float Radius;
        public readonly float MaxLifetime;
        public readonly float TurnRateRadiansPerSecond;
        public readonly float LeadPredictionTime;
        public readonly int PierceCount;
        public readonly int BounceCount;
        public readonly int CollisionLayerMask;
        public readonly int EffectPayloadId;

        public ProjectileDefinition(
            ProjectileDefinitionId definitionId,
            ProjectileGuidanceMode guidanceMode,
            ProjectileLifecycleFlags lifecycleFlags,
            float initialSpeed,
            float maxSpeed,
            float acceleration,
            float gravityScale,
            float radius,
            float maxLifetime,
            float turnRateRadiansPerSecond,
            float leadPredictionTime,
            int pierceCount,
            int bounceCount,
            int collisionLayerMask,
            int effectPayloadId)
        {
            DefinitionId = definitionId;
            GuidanceMode = guidanceMode;
            LifecycleFlags = lifecycleFlags;
            InitialSpeed = initialSpeed;
            MaxSpeed = maxSpeed;
            Acceleration = acceleration;
            GravityScale = gravityScale;
            Radius = radius;
            MaxLifetime = maxLifetime;
            TurnRateRadiansPerSecond = turnRateRadiansPerSecond;
            LeadPredictionTime = leadPredictionTime;
            PierceCount = pierceCount;
            BounceCount = bounceCount;
            CollisionLayerMask = collisionLayerMask;
            EffectPayloadId = effectPayloadId;
        }

        public bool IsValid
        {
            get
            {
                return DefinitionId.IsValid
                       && IsFinite(InitialSpeed)
                       && IsFinite(MaxSpeed)
                       && IsFinite(Acceleration)
                       && IsFinite(GravityScale)
                       && IsFinite(Radius)
                       && IsFinite(MaxLifetime)
                       && IsFinite(TurnRateRadiansPerSecond)
                       && IsFinite(LeadPredictionTime)
                       && InitialSpeed >= 0f
                       && MaxSpeed >= 0f
                       && Radius >= 0f
                       && MaxLifetime > 0f
                       && TurnRateRadiansPerSecond >= 0f
                       && LeadPredictionTime >= 0f
                       && PierceCount >= 0
                       && BounceCount >= 0;
            }
        }

        public static ProjectileDefinition CreateKinematic(
            int definitionId,
            float speed,
            float radius,
            float maxLifetime,
            int collisionLayerMask)
        {
            return new ProjectileDefinition(
                new ProjectileDefinitionId(definitionId),
                ProjectileGuidanceMode.Direction,
                ProjectileLifecycleFlags.DespawnOnHit | ProjectileLifecycleFlags.IgnoreOwner,
                speed,
                speed,
                0f,
                0f,
                radius,
                maxLifetime,
                0f,
                0f,
                0,
                0,
                collisionLayerMask,
                0);
        }

        /// <summary>
        /// True when speed, acceleration and lifetime could push a component past the float
        /// range. The bound assumes all three push the same way and uses a gravity ceiling above
        /// any authored value; anything reachable below 1e25 cannot overflow, so those
        /// definitions skip the per-step finiteness check.
        /// </summary>
        public bool MayOverflow
        {
            get
            {
                const double GravityCeiling = 1.0e5;
                double lifetime = MaxLifetime;
                double speed = Math.Abs((double)InitialSpeed);
                double acceleration = Math.Abs((double)Acceleration);
                double reach = (speed + acceleration * lifetime + GravityCeiling * lifetime) * lifetime;
                return double.IsNaN(reach) || double.IsInfinity(reach) || reach > 1.0e25;
            }
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
