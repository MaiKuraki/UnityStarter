using UnityEngine;
using strange.extensions.injector.api;
using CycloneGames.Factory.Runtime;
using CycloneGames.Logging;
using CycloneGames.GameplayFramework.Runtime;

namespace CycloneGames.GameplayFramework.Sample.StrangeIoC
{
    public class StrangeIoCSampleObjectSpawner : IUnityObjectSpawner
    {
        private static readonly LogChannel Log = StrangeIoCSampleLog.Channel;

        [Inject] public IInjectionBinder injectionBinder { get; set; }

        public T Create<T>(T origin) where T : Object
        {
            if (origin == null)
            {
                Log.Error("Invalid Prefab to spawn");
                return null;
            }

            var obj = UnityEngine.Object.Instantiate(origin);
            injectionBinder.injector.Inject(obj);
            return obj;
        }

        public T Create<T>(T origin, Transform parent) where T : Object
        {
            if (origin == null)
            {
                Log.Error("Invalid Prefab to spawn");
                return null;
            }

            var obj = UnityEngine.Object.Instantiate(origin, parent);
            injectionBinder.injector.Inject(obj);
            return obj;
        }
    }

    /// <summary>
    /// Bridges the StrangeIoC-bound <see cref="IUnityObjectSpawner"/> onto the framework's
    /// <see cref="IActorLifetime"/> seam. The world creates Actors through a lifetime, so the spawner
    /// stays a container concern while creation semantics stay injectable.
    /// </summary>
    public sealed class StrangeIoCSampleActorLifetime : IActorLifetime
    {
        private readonly IUnityObjectSpawner spawner;

        // Teardown keeps the framework's play-mode aware release instead of duplicating it here.
        private readonly UnityActorLifetime releaseLifetime = new UnityActorLifetime();

        public StrangeIoCSampleActorLifetime(IUnityObjectSpawner spawner)
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
