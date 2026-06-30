namespace CycloneGames.RPGFoundation.Projectile.Core
{
    public readonly struct ProjectileHitEvent
    {
        public readonly ProjectileHandle Handle;
        public readonly ulong ProjectileEntityId;
        public readonly ulong OwnerEntityId;
        public readonly ulong TargetEntityId;
        public readonly int TargetObjectId;
        public readonly ProjectileDefinitionId DefinitionId;
        public readonly int EffectPayloadId;
        public readonly int Tick;
        public readonly int PredictionKey;
        public readonly bool IsTerminal;
        public readonly ProjectileVector3 Position;
        public readonly ProjectileVector3 Normal;
        public readonly ProjectileVector3 Velocity;

        public ProjectileHitEvent(
            ProjectileHandle handle,
            ulong projectileEntityId,
            ulong ownerEntityId,
            ulong targetEntityId,
            int targetObjectId,
            ProjectileDefinitionId definitionId,
            int effectPayloadId,
            int tick,
            int predictionKey,
            bool isTerminal,
            ProjectileVector3 position,
            ProjectileVector3 normal,
            ProjectileVector3 velocity)
        {
            Handle = handle;
            ProjectileEntityId = projectileEntityId;
            OwnerEntityId = ownerEntityId;
            TargetEntityId = targetEntityId;
            TargetObjectId = targetObjectId;
            DefinitionId = definitionId;
            EffectPayloadId = effectPayloadId;
            Tick = tick;
            PredictionKey = predictionKey;
            IsTerminal = isTerminal;
            Position = position;
            Normal = normal;
            Velocity = velocity;
        }
    }
}
