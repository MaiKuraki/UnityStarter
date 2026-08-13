using System;
using CycloneGames.Factory.Runtime;

namespace CycloneGames.GameplayFramework.Runtime.Integrations.Factory
{
    /// <summary>
    /// Adapts a symmetric Factory Unity object lifetime to the GameplayFramework Actor lifetime
    /// seam. Release is terminal and never returns an Actor to a pool.
    /// </summary>
    public sealed class FactoryActorLifetime : IActorLifetime
    {
        private readonly IUnityObjectLifetime unityObjectLifetime;

        public FactoryActorLifetime(IUnityObjectLifetime unityObjectLifetime)
        {
            this.unityObjectLifetime = unityObjectLifetime ??
                throw new ArgumentNullException(nameof(unityObjectLifetime));
        }

        public T Create<T>(T prefab) where T : Actor
        {
            if (prefab == null)
            {
                throw new ArgumentNullException(nameof(prefab));
            }

            T instance = unityObjectLifetime.Create(prefab);
            if (instance == null)
            {
                throw new InvalidOperationException(
                    $"The Factory Unity object lifetime returned null for Actor prefab '{prefab.name}'.");
            }

            return instance;
        }

        public void Release(Actor actor)
        {
            if (ReferenceEquals(actor, null))
            {
                return;
            }

            unityObjectLifetime.Release(actor);
        }
    }
}
