using System;

namespace CycloneGames.Factory.Runtime
{
    /// <summary>
    /// Thread-safe wrapper around any <see cref="IMemoryPool{TValue}"/>.
    /// Uses a monitor lock to serialize all pool operations.
    /// Suitable for loading threads or other scenarios that require cross-thread pool access.
    /// <para>
    /// <b>WARNING:</b> Do not hold references to spawned items across threads without
    /// your own synchronization — the lock only protects pool operations, not the items themselves.
    /// </para>
    /// </summary>
    public sealed class ConcurrentMemoryPool<TValue> : IMemoryPool<TValue>, IDisposable where TValue : class
    {
        private readonly IMemoryPool<TValue> _inner;
        private readonly object _lock = new object();

        public ConcurrentMemoryPool(IMemoryPool<TValue> inner)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        }

        public int CountAll { get { lock (_lock) return _inner.CountAll; } }
        public int CountActive { get { lock (_lock) return _inner.CountActive; } }
        public int CountInactive { get { lock (_lock) return _inner.CountInactive; } }
        public Type ItemType => _inner.ItemType;
        public PoolCapacitySettings CapacitySettings { get { lock (_lock) return _inner.CapacitySettings; } }
        public PoolDiagnostics Diagnostics { get { lock (_lock) return _inner.Diagnostics; } }
        public PoolProfile Profile { get { lock (_lock) return _inner.Profile; } }

        public TValue Spawn()
        {
            lock (_lock) return _inner.Spawn();
        }

        public bool TrySpawn(out TValue item)
        {
            lock (_lock) return _inner.TrySpawn(out item);
        }

        public bool Despawn(TValue item)
        {
            lock (_lock) return _inner.Despawn(item);
        }

        public bool Contains(TValue item)
        {
            lock (_lock) return _inner.Contains(item);
        }

        public void ForEachActive(Action<TValue> action)
        {
            lock (_lock) _inner.ForEachActive(action);
        }

        public void ForEachActive<TState>(TState state, Action<TValue, TState> action)
        {
            lock (_lock) _inner.ForEachActive(state, action);
        }

        public void DespawnAll()
        {
            lock (_lock) _inner.DespawnAll();
        }

        public int DespawnStep(int maxItems)
        {
            lock (_lock) return _inner.DespawnStep(maxItems);
        }

        public void Clear()
        {
            lock (_lock) _inner.Clear();
        }

        public void Prewarm(int count)
        {
            lock (_lock) _inner.Prewarm(count);
        }

        public int WarmupStep(int maxItems)
        {
            lock (_lock) return _inner.WarmupStep(maxItems);
        }

        public void TrimInactive(int targetInactiveCount)
        {
            lock (_lock) _inner.TrimInactive(targetInactiveCount);
        }

        public void Dispose()
        {
            lock (_lock)
            {
                if (_inner is IDisposable disposable)
                {
                    disposable.Dispose();
                }
            }
        }
    }
}
