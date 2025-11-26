#if VCONTAINER_PRESENT
using UnityEngine;
using VContainer;
using VContainer.Unity;
using CycloneGames.Factory.Runtime;

namespace CycloneGames.Factory.Runtime.Integrations.VContainer
{
    /// <summary>
    /// An implementation of IUnityObjectSpawner that uses VContainer's IObjectResolver
    /// to instantiate objects with dependency injection.
    /// </summary>
    public class VContainerObjectSpawner : IUnityObjectSpawner
    {
        private readonly IObjectResolver _resolver;

        [Inject]
        public VContainerObjectSpawner(IObjectResolver resolver)
        {
            _resolver = resolver;
        }

        public T Create<T>(T origin) where T : Object
        {
            if (origin == null) return null;

            // VContainer extension methods are specific to GameObject and Component.
            // We need to cast to use the correct overload.
            if (origin is GameObject go)
            {
                var instance = _resolver.Instantiate(go);
                return instance as T;
            }
            else if (origin is Component component)
            {
                var instance = _resolver.Instantiate(component);
                return instance as T;
            }

            // Fallback for non-injectable Unity Objects (ScriptableObjects, Materials, etc.)
            return Object.Instantiate(origin);
        }

        public T Create<T>(T origin, Transform parent) where T : Object
        {
            if (origin == null) return null;

            if (origin is GameObject go)
            {
                var instance = _resolver.Instantiate(go, parent);
                return instance as T;
            }
            else if (origin is Component component)
            {
                var instance = _resolver.Instantiate(component, parent);
                return instance as T;
            }

            return Object.Instantiate(origin, parent);
        }
    }
}
#endif