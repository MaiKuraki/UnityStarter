using CycloneGames.Networking.Simulation;

namespace CycloneGames.RPGFoundation.Projectile.Networking
{
    public interface IProjectileNetworkMessageValidator
    {
        NetworkActionResult ValidateSpawn(
            in ProjectileSpawnMessage message,
            in ProjectileNetworkValidationContext context);

        NetworkActionResult ValidateSnapshot(
            in ProjectileSnapshotMessage message,
            in ProjectileNetworkValidationContext context);

        NetworkActionResult ValidateHit(
            in ProjectileHitMessage message,
            in ProjectileNetworkValidationContext context);

        NetworkActionResult ValidateDespawn(
            in ProjectileDespawnMessage message,
            in ProjectileNetworkValidationContext context);

        NetworkActionResult ValidateFullStateRequest(
            in ProjectileFullStateRequestMessage message,
            in ProjectileNetworkValidationContext context);
    }
}
