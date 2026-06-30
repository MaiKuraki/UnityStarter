using System;
using CycloneGames.RPGFoundation.Projectile.Core;
using UnityEngine;

namespace CycloneGames.RPGFoundation.Projectile.Runtime
{
    [CreateAssetMenu(
        fileName = "ProjectileDefinition",
        menuName = "CycloneGames/RPGFoundation/Projectile/Definition")]
    public class ProjectileDefinitionAsset : ScriptableObject
    {
        [SerializeField] private int DefinitionId = 1;
        [SerializeField] private ProjectileGuidanceMode GuidanceMode = ProjectileGuidanceMode.Direction;
        [SerializeField] private ProjectileLifecycleFlags LifecycleFlags =
            ProjectileLifecycleFlags.DespawnOnHit | ProjectileLifecycleFlags.IgnoreOwner;
        [SerializeField] private float InitialSpeed = 12f;
        [SerializeField] private float MaxSpeed = 12f;
        [SerializeField] private float Acceleration;
        [SerializeField] private float GravityScale;
        [SerializeField] private float Radius = 0.1f;
        [SerializeField] private float MaxLifetime = 5f;
        [SerializeField] private float TurnRateDegreesPerSecond = 360f;
        [SerializeField] private float LeadPredictionTime;
        [SerializeField] private int PierceCount;
        [SerializeField] private int BounceCount;
        [SerializeField] private LayerMask CollisionLayerMask = ~0;
        [SerializeField] private int EffectPayloadId;

        public virtual ProjectileDefinition BuildDefinition()
        {
            return CreateDefinition(sanitize: true);
        }

        public virtual ProjectileDefinition BuildAuthoringDefinition()
        {
            return CreateDefinition(sanitize: false);
        }

        private ProjectileDefinition CreateDefinition(bool sanitize)
        {
            return new ProjectileDefinition(
                new ProjectileDefinitionId(DefinitionId),
                GuidanceMode,
                LifecycleFlags,
                sanitize ? Math.Max(0f, InitialSpeed) : InitialSpeed,
                sanitize ? Math.Max(0f, MaxSpeed) : MaxSpeed,
                Acceleration,
                GravityScale,
                sanitize ? Math.Max(0f, Radius) : Radius,
                sanitize ? Math.Max(0.001f, MaxLifetime) : MaxLifetime,
                DegreesToRadians(sanitize ? Math.Max(0f, TurnRateDegreesPerSecond) : TurnRateDegreesPerSecond),
                sanitize ? Math.Max(0f, LeadPredictionTime) : LeadPredictionTime,
                sanitize ? Math.Max(0, PierceCount) : PierceCount,
                sanitize ? Math.Max(0, BounceCount) : BounceCount,
                CollisionLayerMask.value,
                EffectPayloadId);
        }

        private static float DegreesToRadians(float degrees)
        {
            return degrees * ((float)Math.PI / 180f);
        }
    }
}
