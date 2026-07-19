using UnityEngine;

namespace CycloneGames.Factory.Runtime
{
    /// <summary>
    /// A specialized FastObjectPool for Unity Components.
    /// Automatically handles Instantiate, SetActive, and transform parenting.
    /// </summary>
    public sealed class MonoFastPool<T> : FastObjectPool<T> where T : Component
    {
        private readonly IUnityObjectSpawner _spawner;
        private readonly T _prefab;
        private readonly Transform _root;
        private readonly bool _autoSetActive;

        public MonoFastPool(T prefab, int initialCapacity = 0, Transform root = null, bool autoSetActive = true, int maxCapacity = -1)
            : this(
                new DefaultUnityObjectSpawner(),
                prefab,
                new PoolCapacitySettings(initialCapacity, maxCapacity),
                root,
                autoSetActive)
        {
        }

        public MonoFastPool(T prefab, PoolCapacitySettings capacitySettings, Transform root = null, bool autoSetActive = true)
            : this(new DefaultUnityObjectSpawner(), prefab, capacitySettings, root, autoSetActive)
        {
        }

        public MonoFastPool(
            IUnityObjectSpawner spawner,
            T prefab,
            PoolCapacitySettings capacitySettings,
            Transform root = null,
            bool autoSetActive = true)
            : base(capacitySettings, deferInitialPrewarm: true)
        {
            _spawner = spawner ?? throw new System.ArgumentNullException(nameof(spawner));
            _prefab = prefab != null
                ? prefab
                : throw new System.ArgumentNullException(nameof(prefab));
            _root = root;
            _autoSetActive = autoSetActive;

            if (capacitySettings.SoftCapacity > 0)
            {
                Prewarm(capacitySettings.SoftCapacity);
            }
        }

        protected override T CreateNew()
        {
            if (_prefab == null)
            {
                throw new System.InvalidOperationException(
                    "Prefab has been destroyed. The pool cannot create new items.");
            }

            T instance = _root != null
                ? _spawner.Create(_prefab, _root)
                : _spawner.Create(_prefab);
            if (_autoSetActive)
            {
                instance.gameObject.SetActive(false);
            }

            return instance;
        }

        protected override void OnSpawn(T item)
        {
            if (_autoSetActive)
            {
                item.gameObject.SetActive(true);
            }
        }

        protected override void OnDespawn(T item)
        {
            if (_autoSetActive)
            {
                item.gameObject.SetActive(false);
            }

            if (_root != null && item.transform.parent != _root)
            {
                item.transform.SetParent(_root, false);
            }
        }

        protected override bool IsValid(T item)
        {
            return item != null;
        }

        protected override void DestroyItem(T item)
        {
            if (item != null)
            {
                Object.Destroy(item.gameObject);
            }
            base.DestroyItem(item);
        }
    }
}
