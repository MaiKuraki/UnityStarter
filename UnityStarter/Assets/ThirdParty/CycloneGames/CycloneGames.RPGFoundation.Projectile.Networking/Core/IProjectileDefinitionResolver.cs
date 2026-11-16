using CycloneGames.RPGFoundation.Projectile.Core;

namespace CycloneGames.RPGFoundation.Projectile.Networking
{
    /// <summary>
    /// Resolves a replicated definition id to local kinematics.
    /// A snapshot carries only the id, so the receiver must own the catalog: this is the
    /// contract that keeps the wire format free of per-projectile tuning data.
    /// </summary>
    /// <remarks>
    /// Extends the Core catalog contract so a single implementation satisfies both the
    /// networking sink and <see cref="ProjectileWorld.TryRestoreStates"/>.
    /// </remarks>
    public interface IProjectileDefinitionResolver : IProjectileDefinitionCatalog
    {
    }
}
