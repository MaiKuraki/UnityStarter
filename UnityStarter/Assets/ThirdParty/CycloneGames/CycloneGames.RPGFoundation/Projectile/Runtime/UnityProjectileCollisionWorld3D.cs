using System;
using CycloneGames.RPGFoundation.Projectile.Core;
using UnityEngine;

namespace CycloneGames.RPGFoundation.Projectile.Runtime
{
    public sealed class UnityProjectileCollisionWorld3D : IProjectileCollisionWorld
    {
        private readonly RaycastHit[] _hits;

        public UnityProjectileCollisionWorld3D(int capacity)
        {
            _hits = new RaycastHit[Math.Max(1, capacity)];
        }

        public int Cast(
            in ProjectileCollisionQuery query,
            ProjectileCollisionHit[] results,
            int maxResults)
        {
            ProjectileVector3 delta = query.To - query.From;
            float distance = delta.Length;
            if (distance <= 0.000001f || results == null || maxResults <= 0)
            {
                return 0;
            }

            Vector3 origin = ToVector3(query.From);
            Vector3 direction = ToVector3(delta / distance);
            int hitCount = Physics.SphereCastNonAlloc(
                origin,
                query.Radius,
                direction,
                _hits,
                distance,
                query.CollisionLayerMask,
                QueryTriggerInteraction.Ignore);

            int written = 0;
            int limit = Math.Min(Math.Min(hitCount, _hits.Length), Math.Min(maxResults, results.Length));
            for (int i = 0; i < limit; i++)
            {
                RaycastHit hit = _hits[i];
                if (hit.collider == null)
                {
                    continue;
                }

                results[written] = new ProjectileCollisionHit(
                    targetEntityId: 0UL,
                    targetObjectId: hit.collider.GetInstanceID(),
                    hitLayerMask: 1 << hit.collider.gameObject.layer,
                    distance: hit.distance,
                    position: ToProjectileVector3(hit.point),
                    normal: ToProjectileVector3(hit.normal));
                written++;
            }

            return written;
        }

        private static Vector3 ToVector3(ProjectileVector3 value)
        {
            return new Vector3(value.X, value.Y, value.Z);
        }

        private static ProjectileVector3 ToProjectileVector3(Vector3 value)
        {
            return new ProjectileVector3(value.x, value.y, value.z);
        }
    }
}
