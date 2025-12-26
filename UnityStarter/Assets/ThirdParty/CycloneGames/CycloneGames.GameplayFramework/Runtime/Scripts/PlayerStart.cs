using System.Collections.Generic;
using UnityEngine;

namespace CycloneGames.GameplayFramework.Runtime
{
    /// <summary>
    /// Spawn point for players. Uses static registry pattern for zero-GC lookup.
    /// PlayerStart instances automatically register/unregister on enable/disable.
    /// </summary>
    public class PlayerStart : Actor
    {
        // Static registry for all active PlayerStart instances (zero-GC lookup)
        private static readonly List<PlayerStart> _registry = new List<PlayerStart>(16);
        private static bool _registryDirty;

        /// <summary>
        /// Returns a read-only view of all active PlayerStart instances.
        /// This is the recommended way to query PlayerStarts without GC allocation.
        /// </summary>
        public static IReadOnlyList<PlayerStart> GetAllPlayerStarts() => _registry;

        /// <summary>
        /// Returns true if the registry has changed since last query.
        /// Use this to implement cache invalidation in GameMode if needed.
        /// </summary>
        public static bool IsRegistryDirty => _registryDirty;

        /// <summary>
        /// Clears the dirty flag. Call after refreshing any dependent caches.
        /// </summary>
        public static void ClearRegistryDirtyFlag() => _registryDirty = false;

        [SerializeField] private Transform Arrow;

        protected override void Awake()
        {
            base.Awake();

            if (Arrow && Arrow.gameObject && Arrow.gameObject.activeInHierarchy) Arrow.gameObject.SetActive(false);
        }

        protected virtual void OnEnable()
        {
            if (!_registry.Contains(this))
            {
                _registry.Add(this);
                _registryDirty = true;
            }
        }

        protected virtual void OnDisable()
        {
            if (_registry.Remove(this))
            {
                _registryDirty = true;
            }
        }
    }
}