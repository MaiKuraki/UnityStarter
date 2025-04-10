using UnityEngine;

namespace CycloneGames.Factory
{
    // Unity-specific factory implementation 
    public class MonoPoolableFactory<T> : IFactory<T> where T : MonoBehaviour
    {
        private readonly T _prefab;
        private readonly Transform _parent;

        public MonoPoolableFactory(T prefab, in Transform parent = null)
        {
            _prefab = prefab;
            _parent = parent;
        }

        public T Create()
        {
            var instance = UnityEngine.Object.Instantiate(_prefab, _parent);
            instance.gameObject.SetActive(false);
            return instance;
        }
    }

    // Factory for parameterized MonoBehaviours 
    public class MonoPoolableFactory<TParam, T> : IFactory<TParam, T> where T : MonoBehaviour, IPoolable<TParam, IMemoryPool>
    {
        private readonly T _prefab;
        private readonly Transform _parent;

        public MonoPoolableFactory(T prefab, in Transform parent = null)
        {
            _prefab = prefab;
            _parent = parent;
        }

        public T Create(TParam param)
        {
            var instance = UnityEngine.Object.Instantiate(_prefab, _parent);
            instance.gameObject.SetActive(false);
            return instance;
        }
    }
}
