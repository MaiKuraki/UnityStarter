using CycloneGames.Networking;
using CycloneGames.RPGFoundation.Projectile.Core;

namespace CycloneGames.RPGFoundation.Projectile.Networking
{
    public struct ProjectileSnapshotMessage
    {
        public ulong ProjectileEntityId;
        public ulong OwnerEntityId;
        public ulong TargetEntityId;
        public int DefinitionId;
        public int ServerTick;
        public ushort Sequence;
        public int PredictionKey;
        public uint LifecycleFlags;
        public float Age;
        public float Radius;
        public NetworkVector3 Position;
        public NetworkVector3 PreviousPosition;
        public NetworkVector3 Velocity;

        public ProjectileSnapshotMessage(
            ulong projectileEntityId,
            ulong ownerEntityId,
            ulong targetEntityId,
            int definitionId,
            int serverTick,
            ushort sequence,
            int predictionKey,
            uint lifecycleFlags,
            float age,
            float radius,
            NetworkVector3 position,
            NetworkVector3 previousPosition,
            NetworkVector3 velocity)
        {
            ProjectileEntityId = projectileEntityId;
            OwnerEntityId = ownerEntityId;
            TargetEntityId = targetEntityId;
            DefinitionId = definitionId;
            ServerTick = serverTick;
            Sequence = sequence;
            PredictionKey = predictionKey;
            LifecycleFlags = lifecycleFlags;
            Age = age;
            Radius = radius;
            Position = position;
            PreviousPosition = previousPosition;
            Velocity = velocity;
        }

        public bool IsValid
        {
            get
            {
                return ProjectileEntityId != 0UL
                       && DefinitionId != 0
                       && ServerTick >= 0
                       && IsFinite(Age)
                       && IsFinite(Radius)
                       && Age >= 0f
                       && Radius >= 0f
                       && Position.IsFinite()
                       && PreviousPosition.IsFinite()
                       && Velocity.IsFinite();
            }
        }

        public static ProjectileSnapshotMessage FromSnapshot(
            in ProjectileSnapshot snapshot,
            ushort sequence)
        {
            return new ProjectileSnapshotMessage(
                snapshot.NetworkEntityId,
                snapshot.OwnerEntityId,
                snapshot.TargetEntityId,
                snapshot.DefinitionId.Value,
                snapshot.Tick,
                sequence,
                snapshot.PredictionKey,
                (uint)snapshot.LifecycleFlags,
                snapshot.Age,
                snapshot.Radius,
                snapshot.Position.ToNetworkVector3(),
                snapshot.PreviousPosition.ToNetworkVector3(),
                snapshot.Velocity.ToNetworkVector3());
        }

        public ProjectileSnapshot ToProjectileSnapshot()
        {
            return new ProjectileSnapshot(
                ProjectileEntityId,
                OwnerEntityId,
                TargetEntityId,
                new ProjectileDefinitionId(DefinitionId),
                (ProjectileLifecycleFlags)LifecycleFlags,
                ServerTick,
                PredictionKey,
                Age,
                Radius,
                Position.ToProjectileVector3(),
                PreviousPosition.ToProjectileVector3(),
                Velocity.ToProjectileVector3());
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
