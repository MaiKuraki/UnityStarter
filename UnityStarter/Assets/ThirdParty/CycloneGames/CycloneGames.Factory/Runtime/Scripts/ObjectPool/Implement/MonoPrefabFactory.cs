using System;
using UnityEngine;

namespace CycloneGames.Factory.Runtime
{
    public class MonoPrefabFactory<T> : IFactory<T> where T : MonoBehaviour
    {
        private readonly IUnityObjectSpawner _spawner;
        private readonly T _prefab;
        private readonly Transform _parent;

        public MonoPrefabFactory(IUnityObjectSpawner spawner, T prefab, Transform parent = null)
        {
            _spawner = spawner;
            _prefab = prefab;
            _parent = parent;
        }

        public T Create()
        {
            if (_spawner == null)
            {
                throw new InvalidOperationException("IUnityObjectSpawner is null. The factory has not been properly initialized.");
            }
            if (_prefab == null)
            {
                throw new InvalidOperationException("Prefab is null. The factory cannot create an instance from a null prefab.");
            }

            T instance;
            if (_parent)
            {
                instance = _spawner.Create(_prefab, _parent);
            }
            else
            {
                instance = _spawner.Create(_prefab);
            }

            if (instance == null)
            {
                // This can happen if the spawner implementation fails or if Object.Instantiate returns null
                // (e.g., during application shutdown). Returning null here is acceptable, but the primary
                // checks above are more critical for configuration errors.
                return null;
            }

            instance.gameObject.SetActive(false);
            return instance;
        }
    }
}
