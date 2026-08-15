using System;
using UnityEngine;

namespace CycloneGames.GameplayFramework.Runtime
{
    /// <summary>
    /// Default Actor lifetime backed by <see cref="UnityEngine.Object.Instantiate(UnityEngine.Object)"/>
    /// and Unity object destruction.
    /// </summary>
    public sealed class UnityActorLifetime : IActorLifetime
    {
        public T Create<T>(T prefab) where T : Actor
        {
            if (prefab == null)
            {
                throw new ArgumentNullException(nameof(prefab));
            }

            return UnityEngine.Object.Instantiate(prefab);
        }

        public void Release(Actor actor)
        {
            ReleaseUnityActor(actor);
        }

        internal static void ReleaseUnityActor(Actor actor)
        {
            if (ReferenceEquals(actor, null) || actor == null)
            {
                return;
            }

            GameObject actorObject = actor.gameObject;
            if (Application.isPlaying)
            {
                UnityEngine.Object.Destroy(actorObject);
            }
            else
            {
                UnityEngine.Object.DestroyImmediate(actorObject);
            }
        }
    }
}
