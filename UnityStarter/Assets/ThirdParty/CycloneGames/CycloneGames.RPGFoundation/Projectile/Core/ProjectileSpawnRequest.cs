namespace CycloneGames.RPGFoundation.Projectile.Core
{
    public readonly struct ProjectileSpawnRequest
    {
        public readonly ProjectileDefinition Definition;
        public readonly ulong OwnerEntityId;
        public readonly ulong NetworkEntityId;
        public readonly ulong TargetEntityId;
        public readonly int SpawnTick;
        public readonly int PredictionKey;
        public readonly uint Seed;
        public readonly ProjectileVector3 Position;
        public readonly ProjectileVector3 Direction;
        public readonly ProjectileVector3 InitialVelocity;

        public ProjectileSpawnRequest(
            ProjectileDefinition definition,
            ulong ownerEntityId,
            ulong networkEntityId,
            ulong targetEntityId,
            int spawnTick,
            int predictionKey,
            uint seed,
            ProjectileVector3 position,
            ProjectileVector3 direction,
            ProjectileVector3 initialVelocity)
        {
            Definition = definition;
            OwnerEntityId = ownerEntityId;
            NetworkEntityId = networkEntityId;
            TargetEntityId = targetEntityId;
            SpawnTick = spawnTick;
            PredictionKey = predictionKey;
            Seed = seed;
            Position = position;
            Direction = direction;
            InitialVelocity = initialVelocity;
        }

        public bool IsValid
        {
            get
            {
                return Definition.IsValid
                       && NetworkEntityId != 0UL
                       && SpawnTick >= 0
                       && Position.IsFinite
                       && Direction.IsFinite
                       && InitialVelocity.IsFinite;
            }
        }

        public static ProjectileSpawnRequest Create(
            ProjectileDefinition definition,
            ulong ownerEntityId,
            ulong networkEntityId,
            int spawnTick,
            ProjectileVector3 position,
            ProjectileVector3 direction)
        {
            return new ProjectileSpawnRequest(
                definition,
                ownerEntityId,
                networkEntityId,
                targetEntityId: 0UL,
                spawnTick,
                predictionKey: 0,
                seed: 0u,
                position,
                direction,
                ProjectileVector3.Zero);
        }
    }
}
