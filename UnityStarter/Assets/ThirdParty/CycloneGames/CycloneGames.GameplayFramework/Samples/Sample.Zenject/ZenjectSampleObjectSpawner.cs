using Zenject;
using UnityEngine;
using CycloneGames.Logging;
using CycloneGames.Factory.Runtime;
using CycloneGames.GameplayFramework.Runtime;

namespace CycloneGames.GameplayFramework.Sample.Zenject
{
    public class ZenjectSampleObjectSpawner : IUnityObjectSpawner
    {
        private static readonly LogChannel Log = ZenjectSampleLog.Channel;

        [Inject] DiContainer diContainer;

        public T Create<T>(T origin) where T : Object
        {
            if (origin == null)
            {
                Log.Error("Invalid prefab to spawn");
                return null;
            }

            return diContainer.InstantiatePrefabForComponent<T>(origin);
        }

        public T Create<T>(T origin, Transform parent) where T : Object
        {
            if (origin == null)
            {
                Log.Error("Invalid prefab to spawn");
                return null;
            }

            return diContainer.InstantiatePrefabForComponent<T>(origin, parent);
        }
    }

    /// <summary>
    /// Bridges the container-bound <see cref="IUnityObjectSpawner"/> onto the framework's
    /// <see cref="IActorLifetime"/> seam, so the World creates Actors through DI without depending on
    /// Zenject types.
    /// </summary>
    public sealed class ZenjectSampleActorLifetime : IActorLifetime
    {
        private readonly IUnityObjectSpawner spawner;

        // Teardown keeps the framework's play-mode aware release instead of duplicating it here.
        private readonly UnityActorLifetime releaseLifetime = new UnityActorLifetime();

        public ZenjectSampleActorLifetime(IUnityObjectSpawner spawner)
        {
            this.spawner = spawner ?? throw new System.ArgumentNullException(nameof(spawner));
        }

        public T Create<T>(T prefab) where T : Actor
        {
            return spawner.Create(prefab);
        }

        public void Release(Actor actor)
        {
            releaseLifetime.Release(actor);
        }
    }
}
