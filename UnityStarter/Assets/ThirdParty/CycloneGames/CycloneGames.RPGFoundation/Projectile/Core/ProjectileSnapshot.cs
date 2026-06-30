namespace CycloneGames.RPGFoundation.Projectile.Core
{
    public readonly struct ProjectileSnapshot
    {
        public readonly ulong NetworkEntityId;
        public readonly ulong OwnerEntityId;
        public readonly ulong TargetEntityId;
        public readonly ProjectileDefinitionId DefinitionId;
        public readonly ProjectileLifecycleFlags LifecycleFlags;
        public readonly int Tick;
        public readonly int PredictionKey;
        public readonly float Age;
        public readonly float Radius;
        public readonly ProjectileVector3 Position;
        public readonly ProjectileVector3 PreviousPosition;
        public readonly ProjectileVector3 Velocity;

        public ProjectileSnapshot(
            ulong networkEntityId,
            ulong ownerEntityId,
            ulong targetEntityId,
            ProjectileDefinitionId definitionId,
            ProjectileLifecycleFlags lifecycleFlags,
            int tick,
            int predictionKey,
            float age,
            float radius,
            ProjectileVector3 position,
            ProjectileVector3 previousPosition,
            ProjectileVector3 velocity)
        {
            NetworkEntityId = networkEntityId;
            OwnerEntityId = ownerEntityId;
            TargetEntityId = targetEntityId;
            DefinitionId = definitionId;
            LifecycleFlags = lifecycleFlags;
            Tick = tick;
            PredictionKey = predictionKey;
            Age = age;
            Radius = radius;
            Position = position;
            PreviousPosition = previousPosition;
            Velocity = velocity;
        }

        public static ProjectileSnapshot FromState(in ProjectileState state)
        {
            return new ProjectileSnapshot(
                state.NetworkEntityId,
                state.OwnerEntityId,
                state.TargetEntityId,
                state.DefinitionId,
                state.LifecycleFlags,
                state.CurrentTick,
                state.PredictionKey,
                state.Age,
                state.Radius,
                state.Position,
                state.PreviousPosition,
                state.Velocity);
        }
    }
}
