namespace CycloneGames.RPGFoundation.Projectile.Core
{
    public readonly struct ProjectileCollisionQuery
    {
        public readonly ProjectileHandle Handle;
        public readonly ulong OwnerEntityId;
        public readonly ulong NetworkEntityId;
        public readonly int CollisionLayerMask;
        public readonly float Radius;
        public readonly ProjectileVector3 From;
        public readonly ProjectileVector3 To;

        public ProjectileCollisionQuery(
            ProjectileHandle handle,
            ulong ownerEntityId,
            ulong networkEntityId,
            int collisionLayerMask,
            float radius,
            ProjectileVector3 from,
            ProjectileVector3 to)
        {
            Handle = handle;
            OwnerEntityId = ownerEntityId;
            NetworkEntityId = networkEntityId;
            CollisionLayerMask = collisionLayerMask;
            Radius = radius;
            From = from;
            To = to;
        }
    }
}
