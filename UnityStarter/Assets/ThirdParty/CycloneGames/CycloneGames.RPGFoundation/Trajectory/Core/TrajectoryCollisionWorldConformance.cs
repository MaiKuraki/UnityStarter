using System;

namespace CycloneGames.RPGFoundation.Trajectory.Core
{
    /// <summary>
    /// Checks a collision adapter against the contract <see cref="TrajectorySolver"/> assumes.
    ///
    /// A generic test is impossible because only the caller knows the geometry. What can be shared is
    /// the check itself: supply a query whose segment is known to cross your thinnest collider, and
    /// this verifies that the adapter reports it with a usable payload. Mirrors the role
    /// <c>ProjectileCollisionWorldConformance</c> plays for the projectile module.
    /// </summary>
    public static class TrajectoryCollisionWorldConformance
    {
        private const int ScratchCapacity = 8;
        private const float DistanceTolerance = 0.001f;

        /// <summary>
        /// True when <paramref name="world"/> reports at least one valid hit for a segment known to
        /// cross geometry, and every reported hit satisfies the payload contract.
        ///
        /// <paramref name="crossingQuery"/> must start on the near side of a collider and end beyond
        /// it. Build it from your thinnest collider: the thinner it is, the more certain you are that
        /// the adapter sweeps rather than sampling end points.
        /// </summary>
        public static bool VerifySweep(
            ITrajectoryCollisionWorld world,
            in TrajectoryCastQuery crossingQuery,
            out string failure)
        {
            if (world == null)
            {
                failure = "collision world is null";
                return false;
            }

            Span<TrajectoryHit> results = stackalloc TrajectoryHit[ScratchCapacity];
            int count = world.Cast(in crossingQuery, results);

            if (count < 0)
            {
                failure = "adapter returned a negative hit count: " + count;
                return false;
            }

            if (count > results.Length)
            {
                failure = "adapter reported " + count + " hits into a buffer of " + results.Length;
                return false;
            }

            if (count == 0)
            {
                failure = "no hit reported for a segment that crosses geometry: the adapter is not "
                          + "sweeping, or the layer mask is wrong";
                return false;
            }

            float segmentLength = QueryLength(in crossingQuery);
            int validHits = 0;

            for (int i = 0; i < count; i++)
            {
                TrajectoryHit hit = results[i];
                if (!IsKnownResponse(hit.Response))
                {
                    failure = "hit[" + i + "] carries an undefined response value";
                    return false;
                }

                if (!hit.IsValid)
                {
                    continue;
                }

                validHits++;

                if (hit.Distance < -DistanceTolerance || hit.Distance > segmentLength + DistanceTolerance)
                {
                    failure = "hit distance " + hit.Distance + " lies outside the swept segment of "
                              + segmentLength;
                    return false;
                }

                if (!hit.Position.IsFinite || !hit.Normal.IsFinite)
                {
                    failure = "hit[" + i + "] carries a non-finite position or normal";
                    return false;
                }
            }

            if (validHits == 0)
            {
                failure = "hits were reported but none carried a target id or layer mask";
                return false;
            }

            failure = null;
            return true;
        }

        private static float QueryLength(in TrajectoryCastQuery query)
        {
            TrajectoryVector3 delta = query.To - query.From;
            return (float)Math.Sqrt(delta.LengthSquared);
        }

        private static bool IsKnownResponse(TrajectoryHitResponse response)
        {
            return (uint)response <= (uint)TrajectoryHitResponse.Pierce;
        }
    }
}
