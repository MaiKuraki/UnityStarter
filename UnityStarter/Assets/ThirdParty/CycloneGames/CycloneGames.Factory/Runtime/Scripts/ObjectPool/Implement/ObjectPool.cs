using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Threading;

namespace CycloneGames.Factory.Runtime
{
    /// <summary>
    /// A generic, thread-safe object pool with automatic scaling capabilities.
    /// It dynamically expands when empty and shrinks based on a high-water mark of usage
    /// to balance performance and memory consumption.
    /// This implementation is designed for high-performance scenarios, minimizing GC pressure.
    /// </summary>
    /// <typeparam name="TParam1">The type of parameter used to initialize a spawned object.</typeparam>
    /// <typeparam name="TValue">The type of object in the pool. Must implement IPoolable.</typeparam>
    public sealed class ObjectPool<TParam1, TValue> : IMemoryPool<TParam1, TValue>, IDisposable
        where TValue : class, IPoolable<TParam1, IMemoryPool>
    {
        private readonly IFactory<TValue> _factory;
        private readonly Stack<TValue> _inactivePool;
        private readonly List<TValue> _activeItems;
        private readonly Dictionary<TValue, int> _activeItemIndices;
        private readonly ReaderWriterLockSlim _rwLock = new ReaderWriterLockSlim();
        private readonly ConcurrentQueue<TValue> _pendingDespawns = new ConcurrentQueue<TValue>();

        // --- Auto-Scaling Fields ---
        private readonly float _expansionFactor;
        private readonly float _shrinkBufferFactor;
        private readonly int _shrinkCooldownTicks;
        private int _ticksSinceLastShrink;
        private int _maxActiveSinceLastShrink;

        private bool _disposed;

        public int NumTotal => NumActive + NumInactive;
        public int NumActive
        {
            get
            {
                _rwLock.EnterReadLock();
                try
                {
                    return _activeItems.Count;
                }
                finally
                {
                    if (_rwLock.IsReadLockHeld) _rwLock.ExitReadLock();
                }

            }
        }
        public int NumInactive
        {
            get
            {
                _rwLock.EnterReadLock();
                try
                {
                    return _inactivePool.Count;
                }
                finally
                {
                    if (_rwLock.IsReadLockHeld) _rwLock.ExitReadLock();
                }
            }
        }
        public Type ItemType => typeof(TValue);

        /// <summary>
        /// Initializes a new instance of the ObjectPool with auto-scaling parameters.
        /// </summary>
        /// <param name="factory">The factory used to create new pool items.</param>
        /// <param name="initialCapacity">The number of items to pre-warm the pool with.</param>
        /// <param name="expansionFactor">The factor by which to expand the pool when empty (e.g., 0.5f for 50%).</param>
        /// <param name="shrinkBufferFactor">The buffer to maintain above the high-water mark (e.g., 0.2f for 20%).</param>
        /// <param name="shrinkCooldownTicks">The number of Maintenance calls of inactivity before the pool considers shrinking.</param>
        public ObjectPool(
            IFactory<TValue> factory,
            int initialCapacity = 0,
            float expansionFactor = 0.5f,
            float shrinkBufferFactor = 0.2f,
            int shrinkCooldownTicks = 6000)
        {
            _factory = factory ?? throw new ArgumentNullException(nameof(factory));
            _inactivePool = new Stack<TValue>(initialCapacity);
            _activeItems = new List<TValue>(initialCapacity);
            _activeItemIndices = new Dictionary<TValue, int>(initialCapacity);

            _expansionFactor = Math.Max(0, expansionFactor);
            _shrinkBufferFactor = Math.Max(0, shrinkBufferFactor);
            _shrinkCooldownTicks = Math.Max(0, shrinkCooldownTicks);

            if (initialCapacity > 0)
            {
                Resize(initialCapacity);
            }

            ResetShrinkTracker();
        }

        public TValue Spawn(TParam1 param)
        {
            _rwLock.EnterWriteLock();
            try
            {
                if (_inactivePool.Count == 0)
                {
                    // Expansion logic is sound. It correctly calculates the amount inside the lock
                    // to prevent race conditions from multiple threads trying to expand simultaneously.
                    int currentTotal = _activeItems.Count + _inactivePool.Count;
                    int expansionAmount = Math.Max(1, (int)(currentTotal * _expansionFactor));
                    ExpandPoolInternal(expansionAmount);
                }

                TValue item = _inactivePool.Pop();
                int index = _activeItems.Count;
                _activeItems.Add(item);
                _activeItemIndices[item] = index;

                try
                {
                    item.OnSpawned(param, this);
                }
                catch (Exception ex)
                {
                    _activeItems.RemoveAt(_activeItems.Count - 1);
                    _activeItemIndices.Remove(item);
                    _inactivePool.Push(item);
#if DEVELOPMENT_BUILD || UNITY_EDITOR
                    UnityEngine.Debug.LogError($"[CycloneGames.Factory] OnSpawned failed for item of type {typeof(TValue).Name}. Reverting spawn. Error: {ex.Message}");
#endif
                    throw;
                }

                _maxActiveSinceLastShrink = Math.Max(_maxActiveSinceLastShrink, _activeItems.Count);
                return item;
            }
            finally
            {
                if (_rwLock.IsWriteLockHeld) _rwLock.ExitWriteLock();
            }
        }

        public void Despawn(TValue item)
        {
            if (item == null) return;

            // If current thread holds a read lock, avoid deadlock and queue the request.
            if (_rwLock.IsReadLockHeld)
            {
                _pendingDespawns.Enqueue(item);
                return;
            }

            // If current thread already holds the write lock (e.g., re-entrant call from within Spawn or other write-locked code),
            // perform the despawn immediately without attempting to acquire the lock again.
            if (_rwLock.IsWriteLockHeld)
            {
                DespawnWithoutLock(item);
                return;
            }

            _rwLock.EnterWriteLock();
            try
            {
                DespawnWithoutLock(item);
            }
            finally
            {
                if (_rwLock.IsWriteLockHeld) _rwLock.ExitWriteLock();
            }
        }

        /// <summary>
        /// Performs maintenance tasks such as processing pending despawns and updating auto-scaling logic.
        /// Should be called periodically (e.g., every frame or every few seconds).
        /// </summary>
        public void Maintenance()
        {
            if (_pendingDespawns.IsEmpty && _shrinkCooldownTicks <= 0)
            {
                return;
            }

            _rwLock.EnterWriteLock();
            try
            {
                DrainPendingDespawnsWithoutLock();
                UpdateShrinkLogic();
            }
            finally
            {
                if (_rwLock.IsWriteLockHeld) _rwLock.ExitWriteLock();
            }
        }

        /// <summary>
        /// Iterates over all active items and executes the provided action.
        /// This method holds a read lock during iteration, so the action should be fast and thread-safe.
        /// </summary>
        /// <param name="action">The action to perform on each active item.</param>
        public void UpdateActiveItems(Action<TValue> action)
        {
            if (action == null) return;

            _rwLock.EnterReadLock();
            try
            {
                for (int i = _activeItems.Count - 1; i >= 0; i--)
                {
                    action(_activeItems[i]);
                }
            }
            finally
            {
                if (_rwLock.IsReadLockHeld) _rwLock.ExitReadLock();
            }
        }

        private void DrainPendingDespawnsWithoutLock()
        {
            while (_pendingDespawns.TryDequeue(out var item))
            {
                DespawnWithoutLock(item);
            }
        }

        private void DespawnWithoutLock(TValue item)
        {
            if (item == null) return;

            if (!_activeItemIndices.TryGetValue(item, out int index))
            {
                return;
            }

            TValue lastItem = _activeItems[_activeItems.Count - 1];
            _activeItems[index] = lastItem;
            _activeItemIndices[lastItem] = index;
            _activeItems.RemoveAt(_activeItems.Count - 1);
            _activeItemIndices.Remove(item);

            try
            {
                item.OnDespawned();
            }
            catch (Exception ex)
            {

#if DEVELOPMENT_BUILD || UNITY_EDITOR
                UnityEngine.Debug.LogError($"[CycloneGames.Factory] OnDespawned failed for item of type {typeof(TValue).Name}. The item will still be returned to the pool. Error: {ex.Message}");
#endif
            }
            finally
            {
                _inactivePool.Push(item);
            }
        }

        private void UpdateShrinkLogic()
        {
            if (_shrinkCooldownTicks <= 0) return;

            _ticksSinceLastShrink++;
            if (_ticksSinceLastShrink < _shrinkCooldownTicks) return;

            // If the cooldown has passed, check if we should shrink the pool.
            // The desired size is the peak active count plus a configurable buffer.
            int desiredSize = (int)(_maxActiveSinceLastShrink * (1 + _shrinkBufferFactor));
            int prewarmedSize = _activeItems.Count + _inactivePool.Count;

            if (prewarmedSize > desiredSize)
            {
                int itemsToRemove = prewarmedSize - desiredSize;
                ShrinkPoolInternal(itemsToRemove);
            }

            ResetShrinkTracker();
        }

        public void Despawn(object obj)
        {
            if (obj is TValue value)
            {
                Despawn(value);
            }
        }

        public void Resize(int desiredPoolSize)
        {
            _rwLock.EnterWriteLock();
            try
            {
                int currentInactiveCount = _inactivePool.Count;
                if (currentInactiveCount < desiredPoolSize)
                {
                    ExpandPoolInternal(desiredPoolSize - currentInactiveCount);
                }
                else if (currentInactiveCount > desiredPoolSize)
                {
                    ShrinkPoolInternal(currentInactiveCount - desiredPoolSize);
                }
                ResetShrinkTracker();
            }
            finally
            {
                if (_rwLock.IsWriteLockHeld) _rwLock.ExitWriteLock();
            }
        }

        public void ExpandBy(int numToAdd)
        {
            if (numToAdd <= 0) return;

            _rwLock.EnterWriteLock();
            try
            {
                ExpandPoolInternal(numToAdd);
                ResetShrinkTracker();
            }
            finally
            {
                if (_rwLock.IsWriteLockHeld) _rwLock.ExitWriteLock();
            }
        }

        public void ShrinkBy(int numToRemove)
        {
            if (numToRemove <= 0) return;

            _rwLock.EnterWriteLock();
            try
            {
                ShrinkPoolInternal(numToRemove);
                ResetShrinkTracker();
            }
            finally
            {
                if (_rwLock.IsWriteLockHeld) _rwLock.ExitWriteLock();
            }
        }

        private void ExpandPoolInternal(int count)
        {
            for (int i = 0; i < count; i++)
            {
                var created = _factory.Create();
                if (created != null)
                {
                    _inactivePool.Push(created);
                }
            }
        }

        private void ShrinkPoolInternal(int count)
        {
            int numToActuallyRemove = Math.Min(count, _inactivePool.Count);
            for (int i = 0; i < numToActuallyRemove; i++)
            {
                var item = _inactivePool.Pop();
                (item as IDisposable)?.Dispose();
            }
        }

        private void ResetShrinkTracker()
        {
            _ticksSinceLastShrink = 0;
            _maxActiveSinceLastShrink = _activeItems.Count;
        }

        public void Clear()
        {
            _rwLock.EnterWriteLock();
            try
            {
                // Ensure active items receive proper shutdown callbacks before disposal
                foreach (var item in _activeItems)
                {
                    try
                    {
                        item.OnDespawned();
                    }
                    finally
                    {
                        (item as IDisposable)?.Dispose();
                    }
                }
                _activeItems.Clear();
                _activeItemIndices.Clear();

                foreach (var item in _inactivePool)
                {
                    (item as IDisposable)?.Dispose();
                }
                _inactivePool.Clear();

                ResetShrinkTracker();
            }
            finally
            {
                if (_rwLock.IsWriteLockHeld) _rwLock.ExitWriteLock();
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            Clear();
            _rwLock.Dispose();
        }

        public void DespawnAllActive()
        {
            _rwLock.EnterWriteLock();
            try
            {
                // Despawn from the back to avoid shifting costs
                for (int i = _activeItems.Count - 1; i >= 0; i--)
                {
                    var item = _activeItems[_activeItems.Count - 1];
                    DespawnWithoutLock(item);
                }
                DrainPendingDespawnsWithoutLock();
                ResetShrinkTracker();
            }
            finally
            {
                if (_rwLock.IsWriteLockHeld) _rwLock.ExitWriteLock();
            }
        }
    }
}