namespace CycloneGames.RPGFoundation.Projectile.Core
{
    public struct ProjectileState
    {
        public ProjectileHandle Handle;
        public ProjectileDefinitionId DefinitionId;
        public ProjectileGuidanceMode GuidanceMode;
        public ProjectileLifecycleFlags LifecycleFlags;
        public ulong OwnerEntityId;
        public ulong NetworkEntityId;
        public ulong TargetEntityId;
        public int SpawnTick;
        public int CurrentTick;
        public int PredictionKey;
        public uint Seed;
        public float Age;
        public float Radius;
        public int RemainingPierceCount;
        public int RemainingBounceCount;
        public ProjectileVector3 Position;
        public ProjectileVector3 PreviousPosition;
        public ProjectileVector3 Velocity;

        public bool IsAlive
        {
            get
            {
                return Handle.IsValid && NetworkEntityId != 0UL;
            }
        }

        public static ProjectileState Create(
            ProjectileHandle handle,
            in ProjectileSpawnRequest request,
            in ProjectileSpaceProfile space)
        {
            ProjectileDefinition definition = request.Definition;
            ProjectileVector3 direction = space.ProjectDirection(request.Direction);
            ProjectileVector3 initialVelocity = space.ProjectVector(request.InitialVelocity);
            if (initialVelocity.LengthSquared <= 0.000001f)
            {
                initialVelocity = direction * definition.InitialSpeed;
            }

            return new ProjectileState
            {
                Handle = handle,
                DefinitionId = definition.DefinitionId,
                GuidanceMode = definition.GuidanceMode,
                LifecycleFlags = definition.LifecycleFlags,
                OwnerEntityId = request.OwnerEntityId,
                NetworkEntityId = request.NetworkEntityId,
                TargetEntityId = request.TargetEntityId,
                SpawnTick = request.SpawnTick,
                CurrentTick = request.SpawnTick,
                PredictionKey = request.PredictionKey,
                Seed = request.Seed,
                Age = 0f,
                Radius = definition.Radius,
                RemainingPierceCount = definition.PierceCount,
                RemainingBounceCount = definition.BounceCount,
                Position = space.ProjectPosition(request.Position),
                PreviousPosition = space.ProjectPosition(request.Position),
                Velocity = space.ProjectVector(initialVelocity)
            };
        }
    }
}
