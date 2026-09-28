using CycloneGames.Factory.Runtime;

namespace CycloneGames.Factory.Tests.Performance
{
    /// <summary>
    /// Minimal poolable stub owned by this performance assembly.
    /// <see cref="Id"/> is assigned in creation order by <see cref="PerfPoolableFactory"/> so that
    /// eviction/reuse ordering can be locked deterministically without relying on timers.
    /// </summary>
    internal sealed class PerfPoolable : IPoolable<int, PerfPoolable>
    {
        public int Id { get; set; }

        public int Payload { get; set; }

        public void OnSpawned(int data, IDespawnableMemoryPool<PerfPoolable> pool)
        {
            Payload = data;
        }

        public void OnDespawned()
        {
            Payload = 0;
        }
    }

    /// <summary>
    /// Factory stub that stamps each created item with a monotonic <see cref="PerfPoolable.Id"/>.
    /// <see cref="CreatedCount"/> mirrors the number of items produced and is used to cross-check
    /// the pool's own deterministic <c>Diagnostics.TotalCreated</c> counter.
    /// </summary>
    internal sealed class PerfPoolableFactory : IFactory<PerfPoolable>
    {
        private int _nextId;

        public int CreatedCount => _nextId;

        public PerfPoolable Create()
        {
            return new PerfPoolable { Id = _nextId++ };
        }
    }

    /// <summary>
    /// Plain item used by the parameterless <see cref="FastObjectPool{T}"/> throughput measurement.
    /// </summary>
    internal sealed class PerfFastPoolable
    {
        public bool IsActive { get; set; }
    }

    /// <summary>
    /// <see cref="FastObjectPool{T}"/> stub: no spawn parameters, no ownership callback channel.
    /// </summary>
    internal sealed class PerfFastObjectPool : FastObjectPool<PerfFastPoolable>
    {
        public PerfFastObjectPool(PoolCapacitySettings capacitySettings)
            : base(capacitySettings)
        {
        }

        protected override PerfFastPoolable CreateNew()
        {
            return new PerfFastPoolable();
        }

        protected override void OnSpawn(PerfFastPoolable item)
        {
            item.IsActive = true;
        }

        protected override void OnDespawn(PerfFastPoolable item)
        {
            item.IsActive = false;
        }
    }
}
