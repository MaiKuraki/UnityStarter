using CycloneGames.DeterministicMath;
using CycloneGames.RPGFoundation.Projectile.Core;

namespace CycloneGames.RPGFoundation.Projectile.Integrations.DeterministicMath
{
    public readonly struct DeterministicProjectileDefinition
    {
        public readonly ProjectileDefinitionId DefinitionId;
        public readonly ProjectileGuidanceMode GuidanceMode;
        public readonly ProjectileLifecycleFlags LifecycleFlags;
        public readonly FPInt64 InitialSpeed;
        public readonly FPInt64 MaxSpeed;
        public readonly FPInt64 Acceleration;
        public readonly FPInt64 Radius;
        public readonly FPInt64 MaxLifetime;
        public readonly FPInt64 TurnRate;
        public readonly FPInt64 LeadPredictionTime;
        public readonly FPVector3 Gravity;
        public readonly int PierceCount;
        public readonly int BounceCount;
        public readonly int EffectPayloadId;

        public DeterministicProjectileDefinition(
            ProjectileDefinitionId definitionId,
            ProjectileGuidanceMode guidanceMode,
            ProjectileLifecycleFlags lifecycleFlags,
            FPInt64 initialSpeed,
            FPInt64 maxSpeed,
            FPInt64 acceleration,
            FPInt64 radius,
            FPInt64 maxLifetime,
            FPInt64 turnRate,
            FPInt64 leadPredictionTime,
            FPVector3 gravity,
            int pierceCount,
            int bounceCount,
            int effectPayloadId)
        {
            DefinitionId = definitionId;
            GuidanceMode = guidanceMode;
            LifecycleFlags = lifecycleFlags;
            InitialSpeed = initialSpeed;
            MaxSpeed = maxSpeed;
            Acceleration = acceleration;
            Radius = radius;
            MaxLifetime = maxLifetime;
            TurnRate = turnRate;
            LeadPredictionTime = leadPredictionTime;
            Gravity = gravity;
            PierceCount = pierceCount;
            BounceCount = bounceCount;
            EffectPayloadId = effectPayloadId;
        }

        public static DeterministicProjectileDefinition FromFloats(
            int definitionId,
            ProjectileGuidanceMode guidanceMode,
            ProjectileLifecycleFlags lifecycleFlags,
            float initialSpeed,
            float maxSpeed,
            float acceleration,
            float radius,
            float maxLifetime,
            float turnRateRadiansPerSecond,
            float leadPredictionTime,
            float gravityX,
            float gravityY,
            float gravityZ,
            int pierceCount = 0,
            int bounceCount = 0,
            int effectPayloadId = 0)
        {
            return new DeterministicProjectileDefinition(
                new ProjectileDefinitionId(definitionId),
                guidanceMode,
                lifecycleFlags,
                FPInt64.FromFloat(initialSpeed),
                FPInt64.FromFloat(maxSpeed),
                FPInt64.FromFloat(acceleration),
                FPInt64.FromFloat(radius),
                FPInt64.FromFloat(maxLifetime),
                FPInt64.FromFloat(turnRateRadiansPerSecond),
                FPInt64.FromFloat(leadPredictionTime),
                new FPVector3(
                    FPInt64.FromFloat(gravityX),
                    FPInt64.FromFloat(gravityY),
                    FPInt64.FromFloat(gravityZ)),
                pierceCount,
                bounceCount,
                effectPayloadId);
        }
    }

    public readonly struct DeterministicProjectileInput
    {
        public readonly FPInt64 DeltaTime;
        public readonly bool HasTarget;
        public readonly FPVector3 TargetPosition;
        public readonly FPVector3 TargetVelocity;

        public DeterministicProjectileInput(
            FPInt64 deltaTime,
            bool hasTarget,
            FPVector3 targetPosition,
            FPVector3 targetVelocity)
        {
            DeltaTime = deltaTime;
            HasTarget = hasTarget;
            TargetPosition = targetPosition;
            TargetVelocity = targetVelocity;
        }
    }

    public readonly struct DeterministicProjectileState
    {
        public readonly ulong NetworkEntityId;
        public readonly ulong OwnerEntityId;
        public readonly ulong TargetEntityId;
        public readonly ProjectileDefinitionId DefinitionId;
        public readonly ProjectileLifecycleFlags LifecycleFlags;
        public readonly int SpawnTick;
        public readonly int CurrentTick;
        public readonly int PredictionKey;
        public readonly uint Seed;
        public readonly FPInt64 Age;
        public readonly FPInt64 Radius;
        public readonly FPVector3 Position;
        public readonly FPVector3 PreviousPosition;
        public readonly FPVector3 Velocity;

        public DeterministicProjectileState(
            ulong networkEntityId,
            ulong ownerEntityId,
            ulong targetEntityId,
            ProjectileDefinitionId definitionId,
            ProjectileLifecycleFlags lifecycleFlags,
            int spawnTick,
            int currentTick,
            int predictionKey,
            uint seed,
            FPInt64 age,
            FPInt64 radius,
            FPVector3 position,
            FPVector3 previousPosition,
            FPVector3 velocity)
        {
            NetworkEntityId = networkEntityId;
            OwnerEntityId = ownerEntityId;
            TargetEntityId = targetEntityId;
            DefinitionId = definitionId;
            LifecycleFlags = lifecycleFlags;
            SpawnTick = spawnTick;
            CurrentTick = currentTick;
            PredictionKey = predictionKey;
            Seed = seed;
            Age = age;
            Radius = radius;
            Position = position;
            PreviousPosition = previousPosition;
            Velocity = velocity;
        }

        public static DeterministicProjectileState Create(
            ulong networkEntityId,
            ulong ownerEntityId,
            ulong targetEntityId,
            int spawnTick,
            int predictionKey,
            uint seed,
            FPVector3 position,
            FPVector3 direction,
            in DeterministicProjectileDefinition definition)
        {
            FPVector3 velocity = direction.Normalized * definition.InitialSpeed;
            return new DeterministicProjectileState(
                networkEntityId,
                ownerEntityId,
                targetEntityId,
                definition.DefinitionId,
                definition.LifecycleFlags,
                spawnTick,
                spawnTick,
                predictionKey,
                seed,
                FPInt64.Zero,
                definition.Radius,
                position,
                position,
                velocity);
        }
    }
}
