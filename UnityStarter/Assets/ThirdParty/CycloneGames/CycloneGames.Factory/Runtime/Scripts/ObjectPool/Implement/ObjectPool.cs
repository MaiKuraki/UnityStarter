using System;
using System.Collections.Generic;
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
    /// <typeparam name="TValue">The type of object in the pool. Must implement IPoolable and ITickable.</typeparam>
    public sealed class ObjectPool<TParam1, TValue> : IMemoryPool<TParam1, TValue>, ITickable, IDisposable
        where TValue : class, IPoolable<TParam1, IMemoryPool>, ITickable
    {
        private readonly IFactory<TValue> _factory;
        private readonly Stack<TValue> _inactivePool;
        private readonly List<TValue> _activeItems;
        private readonly Dictionary<TValue, int> _activeItemIndices;
        private readonly ReaderWriterLockSlim _rwLock = new ReaderWriterLockSlim();
        
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
        /// <param name="shrinkCooldownTicks">The number of ticks of inactivity before the pool considers shrinking.</param>
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
                    // Auto-expansion logic.
                    // When the pool is exhausted, expand by a factor of the current total size.
                    // This handles sudden high demand more gracefully than creating one item at a time.
                    int expansionAmount = Math.Max(1, (int)(NumTotal * _expansionFactor));
                    ExpandPoolInternal(expansionAmount);
                }

                TValue item = _inactivePool.Pop();
                
                int index = _activeItems.Count;
                _activeItems.Add(item);
                _activeItemIndices[item] = index;

                // Update high-water mark for auto-shrinking logic.
                _maxActiveSinceLastShrink = Math.Max(_maxActiveSinceLastShrink, _activeItems.Count);
                
                item.OnSpawned(param, this);
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

            _rwLock.EnterWriteLock();
            try
            {
                if (!_activeItemIndices.TryGetValue(item, out int index))
                {
                    // Item is not currently active, possibly already despawned.
                    return;
                }

                // Efficiently remove from active list using swap-and-pop.
                // This avoids shifting list elements, making despawn an O(1) operation.
                TValue lastItem = _activeItems[_activeItems.Count - 1];
                _activeItems[index] = lastItem;
                _activeItemIndices[lastItem] = index;
                _activeItems.RemoveAt(_activeItems.Count - 1);
                _activeItemIndices.Remove(item);

                item.OnDespawned();
                _inactivePool.Push(item);
            }
            finally
            {
                if (_rwLock.IsWriteLockHeld) _rwLock.ExitWriteLock();
            }
        }

        public void Tick()
        {
            // First, tick all active items under a read lock to allow for concurrent reads.
            _rwLock.EnterReadLock();
            try
            {
                // Iterate backwards to safely handle cases where an item despawns itself during its Tick.
                for (int i = _activeItems.Count - 1; i >= 0; i--)
                {
                    // Boundary check in case another thread modified the collection after the loop started.
                    if (i < _activeItems.Count)
                    {
                        _activeItems[i].Tick();
                    }
                }
            }
            finally
            {
                if (_rwLock.IsReadLockHeld) _rwLock.ExitReadLock();
            }

            // Second, perform auto-shrink check under a write lock.
            // This is separated because lock escalation (Read->Write) is not permitted.
            _rwLock.EnterWriteLock();
            try
            {
                UpdateShrinkLogic();
            }
            finally
            {
                if (_rwLock.IsWriteLockHeld) _rwLock.ExitWriteLock();
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
                _inactivePool.Push(_factory.Create());
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
                foreach (var item in _activeItems)
                {
                    (item as IDisposable)?.Dispose();
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
    }
}