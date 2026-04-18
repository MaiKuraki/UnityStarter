namespace CycloneGames.Factory.Runtime
{
    /// <summary>
    /// Abstract pool for main-thread objects that do not require spawn parameters.
    /// Provides parameterless Spawn/TrySpawn. Subclasses implement creation and lifecycle hooks.
    /// All tracking, capacity, diagnostics, and lifecycle infrastructure is in <see cref="PoolBase{TValue}"/>.
    /// </summary>
    public abstract class FastObjectPool<T> : PoolBase<T>, IMemoryPool<T> where T : class
    {
        protected FastObjectPool(int initialCapacity = 0, int maxCapacity = -1)
            : this(new PoolCapacitySettings(initialCapacity, maxCapacity))
        {
        }

        protected FastObjectPool(PoolCapacitySettings capacitySettings)
            : this(capacitySettings, deferInitialPrewarm: false)
        {
        }

        protected FastObjectPool(PoolCapacitySettings capacitySettings, bool deferInitialPrewarm)
            : base(capacitySettings)
        {
            if (!deferInitialPrewarm && capacitySettings.SoftCapacity > 0)
            {
                Prewarm(capacitySettings.SoftCapacity);
            }
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
                OnSpawn(item);
                return true;
            }
            catch
            {
                RollbackSpawn(item);
                throw;
            }
        }
    }
}
