using System;
using UnityEngine;

namespace CycloneGames.Factory.Runtime
{
    /// <summary>
    /// A default implementation of <see cref="IUnityObjectLifetime"/> backed by Unity object APIs.
    /// This is safe for DI or manual wiring and generates no GC allocations beyond creation and
    /// Unity's destruction bookkeeping.
    /// </summary>
    public sealed class DefaultUnityObjectSpawner : IUnityObjectLifetime
    {
        public T Create<T>(T origin) where T : UnityEngine.Object
        {
            if (origin == null)
            {
                throw new ArgumentNullException(nameof(origin));
            }

            return UnityEngine.Object.Instantiate(origin);
        }

        public T Create<T>(T origin, UnityEngine.Transform parent) where T : UnityEngine.Object
        {
            if (origin == null)
            {
                throw new ArgumentNullException(nameof(origin));
            }

            return UnityEngine.Object.Instantiate(origin, parent);
        }

        /// <summary>
        /// Permanently releases an instance previously produced by <see cref="Create{T}(T)"/>.
        /// Never pass a persistent asset: the Edit Mode path uses
        /// <see cref="UnityEngine.Object.DestroyImmediate(UnityEngine.Object)"/> on the release
        /// target, which permanently deletes a persistent asset instead of a scene instance.
        /// </summary>
        public void Release(UnityEngine.Object instance)
        {
            if (ReferenceEquals(instance, null) || instance == null)
            {
                return;
            }

            UnityEngine.Object releaseTarget = instance is Component component
                ? component.gameObject
                : instance;
            if (Application.isPlaying)
            {
                UnityEngine.Object.Destroy(releaseTarget);
            }
            else
            {
                UnityEngine.Object.DestroyImmediate(releaseTarget);
            }
        }
    }
}
