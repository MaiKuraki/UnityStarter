using System;
using UnityEngine;

namespace CycloneGames.Factory.Runtime
{
    public sealed class MonoPrefabFactory<T> : IFactory<T> where T : MonoBehaviour
    {
        private readonly IUnityObjectSpawner _spawner;
        private readonly T _prefab;
        private readonly Transform _parent;

        public MonoPrefabFactory(IUnityObjectSpawner spawner, T prefab, Transform parent = null)
        {
            _spawner = spawner ?? throw new ArgumentNullException(nameof(spawner));
            _prefab = prefab != null ? prefab : throw new ArgumentNullException(nameof(prefab));
            _parent = parent;
        }

        public T Create()
        {
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
                throw new InvalidOperationException(
                    $"The Unity object spawner returned null for prefab {typeof(T).Name}.");
            }

            instance.gameObject.SetActive(false);
            return instance;
        }
    }
}
