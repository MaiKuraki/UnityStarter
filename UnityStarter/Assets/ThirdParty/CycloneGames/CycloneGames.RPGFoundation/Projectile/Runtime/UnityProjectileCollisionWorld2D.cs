using System;
using CycloneGames.RPGFoundation.Projectile.Core;
using UnityEngine;
using CycloneGames.Compat;

namespace CycloneGames.RPGFoundation.Projectile.Runtime
{
    public sealed class UnityProjectileCollisionWorld2D : IProjectileCollisionWorld
    {
        private readonly RaycastHit2D[] _hits;
        private ContactFilter2D _filter;

        public UnityProjectileCollisionWorld2D(int capacity)
        {
            _hits = new RaycastHit2D[Math.Max(1, capacity)];
            _filter = new ContactFilter2D
            {
                useTriggers = Physics2D.queriesHitTriggers,
                useLayerMask = true,
                layerMask = 0
            };
        }

        public int Cast(
            in ProjectileCollisionQuery query,
            ProjectileCollisionHit[] results,
            int maxResults)
        {
            ProjectileVector3 delta3 = query.To - query.From;
            float deltaX = delta3.X;
            float deltaY = delta3.Y;
            float distance = (float)Math.Sqrt(deltaX * deltaX + deltaY * deltaY);
            if (distance <= 0.000001f || results == null || maxResults <= 0)
            {
                return 0;
            }

            Vector2 origin = new Vector2(query.From.X, query.From.Y);
            Vector2 direction = new Vector2(deltaX / distance, deltaY / distance);
            _filter.layerMask = query.CollisionLayerMask;
            int hitCount = Physics2D.CircleCast(
                origin,
                query.Radius,
                direction,
                _filter,
                _hits,
                distance);

            int written = 0;
            int limit = Math.Min(Math.Min(hitCount, _hits.Length), Math.Min(maxResults, results.Length));
            for (int i = 0; i < limit; i++)
            {
                RaycastHit2D hit = _hits[i];
                if (hit.collider == null)
                {
                    continue;
                }

                results[written] = new ProjectileCollisionHit(
                    targetEntityId: 0UL,
                    targetObjectId: UnityObjectId.Of(hit.collider),
                    hitLayerMask: 1 << hit.collider.gameObject.layer,
                    distance: hit.distance,
                    position: new ProjectileVector3(hit.point.x, hit.point.y, query.To.Z),
                    normal: new ProjectileVector3(hit.normal.x, hit.normal.y, 0f));
                written++;
            }

            return written;
        }
    }
}
