using System;
using CycloneGames.Factory.Runtime;
using CycloneGames.RPGFoundation.Projectile.Core;
using UnityEngine;

namespace CycloneGames.RPGFoundation.Projectile.Runtime
{
    public sealed class ProjectileViewPool : IDisposable
    {
        private readonly MonoFastPool<ProjectileView> _pool;

        public ProjectileViewPool(
            ProjectileView prefab,
            int initialCapacity,
            Transform root = null)
        {
            _pool = new MonoFastPool<ProjectileView>(
                prefab,
                initialCapacity,
                root,
                autoSetActive: true);
        }

        public bool TrySpawn(
            ProjectileHandle handle,
            in ProjectileSnapshot snapshot,
            out ProjectileView view)
        {
            if (!_pool.TrySpawn(out view))
            {
                return false;
            }

            view.Initialize(handle, in snapshot);
            return true;
        }

        public bool Despawn(ProjectileView view)
        {
            if (view == null)
            {
                return false;
            }

            view.ResetView();
            return _pool.Despawn(view);
        }

        public void Dispose()
        {
            _pool.Dispose();
        }
    }
}
