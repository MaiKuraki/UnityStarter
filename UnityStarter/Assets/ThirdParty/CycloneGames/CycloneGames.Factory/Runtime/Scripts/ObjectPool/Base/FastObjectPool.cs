using System;

namespace CycloneGames.Factory.Runtime
{
    /// <summary>
    /// Abstract pool for main-thread objects that do not require spawn parameters.
    /// Provides parameterless Spawn/TrySpawn. Subclasses implement creation and lifecycle hooks.
    /// All tracking, capacity, diagnostics, and lifecycle infrastructure is in <see cref="PoolBase{TValue}"/>.
    /// </summary>
    /// <remarks>
    /// The constructor never creates items: a base constructor runs before derived fields are assigned and
    /// cannot safely call the virtual <see cref="PoolBase{TValue}.CreateNew"/>. A subclass that wants
    /// construction-time prewarm calls <c>Prewarm(CapacitySettings.PrewarmCount)</c> at the end of its own
    /// constructor.
    /// </remarks>
    public abstract class FastObjectPool<T> : PoolBase<T>, IMemoryPool<T> where T : class
    {
        protected FastObjectPool(int initialCapacity = 0, int maxCapacity = -1)
            : this(new PoolCapacitySettings(initialCapacity, maxCapacity))
        {
        }

        protected FastObjectPool(PoolCapacitySettings capacitySettings, bool validateUniqueOwnership = true)
            : base(capacitySettings, validateUniqueOwnership)
        {
        }

        public T Spawn()
        {
            TrySpawnInternal(throwOnFailure: true, out var item);
            return item;
        }

        public bool TrySpawn(out T item)
        {
            return TrySpawnInternal(throwOnFailure: false, out item);
        }

        /// <summary>
        /// Called when an item is being activated (spawned) from the pool.
        /// </summary>
        protected abstract void OnSpawn(T item);

        private bool TrySpawnInternal(bool throwOnFailure, out T item)
        {
            if (!TryAcquireAndTrack(throwOnFailure, out item))
            {
                return false;
            }

            try
            {
                BeginLifecycleCallback();
                try
                {
                    OnSpawn(item);
                }
                finally
                {
                    EndLifecycleCallback();
                }

                return true;
            }
            catch (Exception spawnFailure)
            {
                Exception cleanupFailure = RollbackSpawn(item);
                RethrowSpawnFailure(spawnFailure, cleanupFailure);
                return false;
            }
        }
    }
}
