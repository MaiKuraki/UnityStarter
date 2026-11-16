using System;

namespace CycloneGames.RPGFoundation.Projectile.Core
{
    /// <summary>
    /// Checks a collision adapter against the contract the world assumes.
    ///
    /// The world calls <see cref="IProjectileCollisionWorld.Cast"/> with the segment a projectile
    /// travelled, and expects the adapter to report what that segment crossed. An adapter that only
    /// tests the segment's end point will miss anything thinner than a step, and no sub-step budget
    /// fixes that for fast projectiles.
    ///
    /// A generic test is impossible because only the caller knows the geometry. What can be shared
    /// is the check itself: supply a query whose segment is known to cross your thinnest collider,
    /// and this verifies that the adapter reports it.
    /// </summary>
    public static class ProjectileCollisionWorldConformance
    {
        /// <summary>
        /// True when <paramref name="world"/> reports a hit for a segment known to cross geometry.
        ///
        /// <paramref name="crossingQuery"/> must start on the near side of a collider and end beyond
        /// it. Build it from your thinnest collider: the thinner the better, because that is the case
        /// an endpoint test fails.
        /// </summary>
        public static bool VerifySweep(
            IProjectileCollisionWorld world,
            in ProjectileCollisionQuery crossingQuery,
            out string failure)
        {
            if (world == null)
            {
                failure = "collision world is null";
                return false;
            }

            var results = new ProjectileCollisionHit[8];
            int count = world.Cast(in crossingQuery, results, results.Length);

            if (count <= 0)
            {
                failure = "no hit reported for a segment that crosses geometry: the adapter is not "
                          + "sweeping, or the layer mask is wrong";
                return false;
            }

            float segmentLength = (crossingQuery.To - crossingQuery.From).Length;
            for (int i = 0; i < count && i < results.Length; i++)
            {
                ProjectileCollisionHit hit = results[i];
                if (hit.TargetObjectId == 0UL && hit.TargetEntityId == 0UL)
                {
                    failure = "hit carries neither a target object id nor a target entity id, so the "
                              + "world cannot suppress repeated contacts";
                    return false;
                }

                if (!hit.IsValid)
                {
                    continue;
                }

                if (hit.Distance < 0f || hit.Distance > segmentLength + 0.001f)
                {
                    failure = "hit distance " + hit.Distance + " lies outside the swept segment of "
                              + segmentLength;
                    return false;
                }

                failure = null;
                return true;
            }

            failure = "hits were reported but none were valid";
            return false;
        }
    }
}
