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
            if (_spawner == null || _prefab == null)
            {
                return null;
            }

            var instance = _spawner.Create(_prefab);
            if (instance == null)
            {
                return null;
            }

            if (_parent)
            {
                instance.transform.SetParent(_parent);
            }

            instance.gameObject.SetActive(false);
            return instance;
        }
    }
}
