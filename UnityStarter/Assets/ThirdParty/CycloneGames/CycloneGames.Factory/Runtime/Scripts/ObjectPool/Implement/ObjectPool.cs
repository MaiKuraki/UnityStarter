using System;

namespace CycloneGames.Factory.Runtime
{
    /// <summary>
    /// Main-thread object pool with deterministic lifecycle and ownership tracking.
    /// Requires a factory for item creation and spawn parameters via IPoolable.
    /// All tracking, capacity, diagnostics, and lifecycle infrastructure is in <see cref="PoolBase{TValue}"/>.
    /// </summary>
    public sealed class ObjectPool<TParam1, TValue> : PoolBase<TValue>, IMemoryPool<TParam1, TValue>
        where TValue : class, IPoolable<TParam1, TValue>
    {
        private readonly IFactory<TValue> _factory;

        public ObjectPool(IFactory<TValue> factory, int initialCapacity = 0, int maxCapacity = -1)
            : this(factory, new PoolCapacitySettings(initialCapacity, maxCapacity))
        {
        }

        public ObjectPool(IFactory<TValue> factory, PoolCapacitySettings capacitySettings)
            : base(capacitySettings)
        {
            _factory = factory ?? throw new ArgumentNullException(nameof(factory));

            if (capacitySettings.SoftCapacity > 0)
            {
                Prewarm(capacitySettings.SoftCapacity);
            }
        }

        public TValue Spawn(TParam1 param)
        {
            TrySpawnInternal(param, throwOnFailure: true, out var item);
            return item;
        }

        public bool TrySpawn(TParam1 param, out TValue item)
        {
            return TrySpawnInternal(param, throwOnFailure: false, out item);
        }

        protected override TValue CreateNew()
        {
            TValue created = _factory.Create();
            if (created == null)
            {
                throw new InvalidOperationException($"Factory for {typeof(TValue).Name} returned null.");
            }

            return created;
        }

        protected override void OnDespawn(TValue item)
        {
            item.OnDespawned();
        }

        private bool TrySpawnInternal(TParam1 param, bool throwOnFailure, out TValue item)
        {
            if (!TryAcquireAndTrack(throwOnFailure, out item))
            {
                return false;
            }

            try
            {
                item.OnSpawned(param, this);
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
