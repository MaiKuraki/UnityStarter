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

            // Prewarm
            if (initialCapacity > 0) ExpandBy(initialCapacity);
        }

        protected override T CreateNew()
        {
            T instance = Object.Instantiate(_prefab, _root);
            // Initially false if autoSetActive is true, because OnDespawn logic puts it there, 
            // but here we just created it.
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

            // Reset parent to root if it was moved
            if (_root != null && item.transform.parent != _root)
            {
                item.transform.SetParent(_root, false);
            }
        }

        /// <summary>
        /// Override IsValid to handle Unity's "Fake Null" for destroyed objects.
        /// This prevents accessing destroyed GameObjects and causing MissingReferenceException.
        /// </summary>
        protected override bool IsValid(T item)
        {
            // Unity overloads != null to check if the native C++ object is alive.
            // We also check gameObject access safety implicitly by checking the Component itself.
            return item != null;
        }

        protected override void DestroyItem(T item)
        {
            if (item != null)
            {
                Object.Destroy(item.gameObject);
            }
        }
    }
}