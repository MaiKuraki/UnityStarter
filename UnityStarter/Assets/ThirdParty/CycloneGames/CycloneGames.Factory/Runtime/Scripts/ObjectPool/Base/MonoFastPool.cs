using UnityEngine;

namespace CycloneGames.Factory.Runtime
{
    /// <summary>
    /// A specialized FastObjectPool for Unity Components.
    /// Automatically handles Instantiate, SetActive, and transform parenting.
    /// </summary>
    public class MonoFastPool<T> : FastObjectPool<T> where T : Component
    {
        private readonly T _prefab;
        private readonly Transform _root;
        private readonly bool _autoSetActive;

        public MonoFastPool(T prefab, int initialCapacity = 16, Transform root = null, bool autoSetActive = true)
            : base(initialCapacity)
        {
            _prefab = prefab;
            _root = root;
            _autoSetActive = autoSetActive;

            if (initialCapacity > 0) ExpandBy(initialCapacity);
        }

        protected override T CreateNew()
        {
            T instance = _root != null
                ? Object.Instantiate(_prefab, _root)
                : Object.Instantiate(_prefab);
            if (_autoSetActive) instance.gameObject.SetActive(false);
            return instance;
        }

        protected override void OnSpawn(T item)
        {
            if (_autoSetActive) item.gameObject.SetActive(true);
        }

        protected override void OnDespawn(T item)
        {
            if (_autoSetActive) item.gameObject.SetActive(false);

            if (_root != null && item.transform.parent != _root)
            {
                item.transform.SetParent(_root, false);
            }
        }

        /// <summary>
        /// Handles Unity's "Fake Null" for destroyed objects (e.g. from scene unload).
        /// </summary>
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
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                // Destroy all pooled GameObjects before clearing the pool
                foreach (var item in _pool)
                {
                    if (item != null) Object.Destroy(item.gameObject);
                }
            }
            base.Dispose(disposing);
        }
    }
}