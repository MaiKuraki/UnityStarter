namespace CycloneGames.RPGFoundation.Projectile.Core
{
    /// <summary>
    /// Maps a replicated definition id to local kinematics.
    ///
    /// Core owns this contract rather than Networking because a batch restore has to create
    /// projectiles, and a snapshot carries only the id. The networking layer's
    /// <c>IProjectileDefinitionResolver</c> extends this interface, so one implementation
    /// satisfies both.
    /// </summary>
    public interface IProjectileDefinitionCatalog
    {
        bool TryGetDefinition(int definitionId, out ProjectileDefinition definition);
    }
}
