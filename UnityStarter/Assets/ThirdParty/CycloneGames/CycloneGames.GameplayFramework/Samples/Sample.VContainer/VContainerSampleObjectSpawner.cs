using UnityEngine;
using VContainer;
using CycloneGames.Logging;
using CycloneGames.Factory.Runtime;
using CycloneGames.GameplayFramework.Runtime;

namespace CycloneGames.GameplayFramework.Sample.VContainer
{
    public class VContainerSampleObjectSpawner : IUnityObjectSpawner
    {
        private static readonly LogChannel Log = VContainerSampleLog.Channel;

        [Inject] IObjectResolver objectResolver;

        public T Create<T>(T origin) where T : UnityEngine.Object
        {
            if (origin == null)
            {
                Log.Error("Invalid prefab to spawn");
                return null;
            }

            var obj = UnityEngine.Object.Instantiate(origin);
            objectResolver.Inject(obj);
            return obj;
        }

        public T Create<T>(T origin, UnityEngine.Transform parent) where T : UnityEngine.Object
        {
            if (origin == null)
            {
                Log.Error("Invalid prefab to spawn");
                return null;
            }

            var obj = UnityEngine.Object.Instantiate(origin, parent);
            objectResolver.Inject(obj);
            return obj;
        }
    }

    /// <summary>
    /// Bridges the container-supplied <see cref="IUnityObjectSpawner"/> onto the framework's
    /// <see cref="IActorLifetime"/> seam. <see cref="GameInstance"/> takes a lifetime, not a spawner, so
    /// this keeps the spawner injectable without making the world depend on a factory type.
    /// </summary>
    public sealed class VContainerActorLifetime : IActorLifetime
    {
        private readonly IUnityObjectSpawner spawner;

        // Creation goes through the container; teardown keeps the framework's own release semantics
        // instead of duplicating play-mode aware destruction in a sample.
        private readonly UnityActorLifetime releaseLifetime = new UnityActorLifetime();

        public VContainerActorLifetime(IUnityObjectSpawner spawner)
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
