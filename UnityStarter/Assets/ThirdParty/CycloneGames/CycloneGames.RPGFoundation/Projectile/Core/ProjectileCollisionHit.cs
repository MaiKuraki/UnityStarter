namespace CycloneGames.RPGFoundation.Projectile.Core
{
    public readonly struct ProjectileCollisionHit
    {
        public readonly ulong TargetEntityId;
        public readonly int TargetObjectId;
        public readonly int HitLayerMask;
        public readonly float Distance;
        public readonly ProjectileVector3 Position;
        public readonly ProjectileVector3 Normal;

        public ProjectileCollisionHit(
            ulong targetEntityId,
            int targetObjectId,
            int hitLayerMask,
            float distance,
            ProjectileVector3 position,
            ProjectileVector3 normal)
        {
            TargetEntityId = targetEntityId;
            TargetObjectId = targetObjectId;
            HitLayerMask = hitLayerMask;
            Distance = distance;
            Position = position;
            Normal = normal;
        }

        public bool IsValid
        {
            get
            {
                return TargetEntityId != 0UL || TargetObjectId != 0;
            }
        }
    }
}
