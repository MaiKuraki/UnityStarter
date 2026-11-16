using CycloneGames.RPGFoundation.Projectile.Core;

namespace CycloneGames.RPGFoundation.Projectile.Networking
{
    /// <summary>
    /// Receives replicated hit reports for presentation only (impact VFX, audio, hit markers).
    ///
    /// Why this exists as a separate contract rather than a callback into
    /// <see cref="ProjectileWorld"/>: on a client a hit is a *prediction artifact*, not a
    /// result. The authority owns damage, crowd control and destruction; a replica that applied
    /// a hit locally could kill an entity the server never damaged, and the divergence would
    /// only surface later as an unexplainable health snap. So the message layer never mutates
    /// world state for a hit. It forwards, and the composition root decides what "show an
    /// impact" means.
    ///
    /// The parameter is the wire message, not <see cref="ProjectileHitEvent"/>: the event type
    /// carries a <see cref="ProjectileHandle"/>, a resolved effect payload and a target object
    /// id, and a wire hit has none of those. Synthesising them would require looking into the
    /// local world, which is exactly what this path must not do.
    /// </summary>
    public interface IProjectileHitReceiver
    {
        /// <summary>
        /// Called once per accepted hit message. Implementations must not allocate on the hot
        /// path and must not mutate the projectile world.
        /// </summary>
        void OnProjectileHit(in ProjectileHitMessage message);
    }
}
