namespace CycloneGames.RPGFoundation.Projectile.Core
{
    public interface IProjectileCollisionWorld
    {
        int Cast(
            in ProjectileCollisionQuery query,
            ProjectileCollisionHit[] results,
            int maxResults);
    }
}
