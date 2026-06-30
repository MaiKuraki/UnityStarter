using CycloneGames.Networking;
using CycloneGames.RPGFoundation.Projectile.Core;

namespace CycloneGames.RPGFoundation.Projectile.Networking
{
    public struct ProjectileSpawnMessage
    {
        public ulong ProjectileEntityId;
        public ulong OwnerEntityId;
        public ulong TargetEntityId;
        public int DefinitionId;
        public int ServerTick;
        public int PredictionKey;
        public uint Seed;
        public uint LifecycleFlags;
        public NetworkVector3 Position;
        public NetworkVector3 Direction;
        public NetworkVector3 InitialVelocity;

        public ProjectileSpawnMessage(
            ulong projectileEntityId,
            ulong ownerEntityId,
            ulong targetEntityId,
            int definitionId,
            int serverTick,
            int predictionKey,
            uint seed,
            uint lifecycleFlags,
            NetworkVector3 position,
            NetworkVector3 direction,
            NetworkVector3 initialVelocity)
        {
            ProjectileEntityId = projectileEntityId;
            OwnerEntityId = ownerEntityId;
            TargetEntityId = targetEntityId;
            DefinitionId = definitionId;
            ServerTick = serverTick;
            PredictionKey = predictionKey;
            Seed = seed;
            LifecycleFlags = lifecycleFlags;
            Position = position;
            Direction = direction;
            InitialVelocity = initialVelocity;
        }

        public bool IsValid
        {
            get
            {
                return ProjectileEntityId != 0UL
                       && DefinitionId != 0
                       && ServerTick >= 0
                       && Position.IsFinite()
                       && Direction.IsFinite()
                       && InitialVelocity.IsFinite();
            }
        }

        public static ProjectileSpawnMessage FromRequest(in ProjectileSpawnRequest request)
        {
            return new ProjectileSpawnMessage(
                request.NetworkEntityId,
                request.OwnerEntityId,
                request.TargetEntityId,
                request.Definition.DefinitionId.Value,
                request.SpawnTick,
                request.PredictionKey,
                request.Seed,
                (uint)request.Definition.LifecycleFlags,
                request.Position.ToNetworkVector3(),
                request.Direction.ToNetworkVector3(),
                request.InitialVelocity.ToNetworkVector3());
        }
    }
}
