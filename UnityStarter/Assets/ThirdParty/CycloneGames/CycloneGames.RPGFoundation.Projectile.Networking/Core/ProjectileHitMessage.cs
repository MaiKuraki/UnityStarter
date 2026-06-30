using CycloneGames.Networking;
using CycloneGames.RPGFoundation.Projectile.Core;

namespace CycloneGames.RPGFoundation.Projectile.Networking
{
    public struct ProjectileHitMessage
    {
        public ulong ProjectileEntityId;
        public ulong OwnerEntityId;
        public ulong TargetEntityId;
        public int DefinitionId;
        public int EffectPayloadId;
        public int ServerTick;
        public int PredictionKey;
        public bool IsTerminal;
        public NetworkVector3 Position;
        public NetworkVector3 Normal;
        public NetworkVector3 Velocity;

        public ProjectileHitMessage(
            ulong projectileEntityId,
            ulong ownerEntityId,
            ulong targetEntityId,
            int definitionId,
            int effectPayloadId,
            int serverTick,
            int predictionKey,
            bool isTerminal,
            NetworkVector3 position,
            NetworkVector3 normal,
            NetworkVector3 velocity)
        {
            ProjectileEntityId = projectileEntityId;
            OwnerEntityId = ownerEntityId;
            TargetEntityId = targetEntityId;
            DefinitionId = definitionId;
            EffectPayloadId = effectPayloadId;
            ServerTick = serverTick;
            PredictionKey = predictionKey;
            IsTerminal = isTerminal;
            Position = position;
            Normal = normal;
            Velocity = velocity;
        }

        public bool IsValid
        {
            get
            {
                return ProjectileEntityId != 0UL
                       && DefinitionId != 0
                       && ServerTick >= 0
                       && Position.IsFinite()
                       && Normal.IsFinite()
                       && Velocity.IsFinite();
            }
        }

        public static ProjectileHitMessage FromHitEvent(in ProjectileHitEvent hitEvent)
        {
            return new ProjectileHitMessage(
                hitEvent.ProjectileEntityId,
                hitEvent.OwnerEntityId,
                hitEvent.TargetEntityId,
                hitEvent.DefinitionId.Value,
                hitEvent.EffectPayloadId,
                hitEvent.Tick,
                hitEvent.PredictionKey,
                hitEvent.IsTerminal,
                hitEvent.Position.ToNetworkVector3(),
                hitEvent.Normal.ToNetworkVector3(),
                hitEvent.Velocity.ToNetworkVector3());
        }
    }
}
