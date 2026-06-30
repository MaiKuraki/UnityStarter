namespace CycloneGames.RPGFoundation.Projectile.Core
{
    public interface IProjectileTargetProvider
    {
        bool TryGetTargetPosition(ulong targetEntityId, out ProjectileVector3 position);

        bool TryGetTargetVelocity(ulong targetEntityId, out ProjectileVector3 velocity);
    }
}
