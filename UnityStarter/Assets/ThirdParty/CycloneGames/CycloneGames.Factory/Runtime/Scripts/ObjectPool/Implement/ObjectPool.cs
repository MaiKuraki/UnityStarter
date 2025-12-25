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

        private readonly float _expansionFactor;
        private readonly float _shrinkBufferFactor;
        private readonly int _shrinkCooldownTicks;
        private int _ticksSinceLastShrink;
        private int _maxActiveSinceLastShrink;

        private int _numActive;
        private int _numInactive;

        public int MaxCapacity { get; set; } = -1;
        public int MinCapacity { get; set; } = 16;

        private const int kCheckInterval = 64;
        private int _despawnCounter = 0;

        private bool _disposed;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private long _totalGets = 0;
        private long _totalReturns = 0;
        private long _totalDiscards = 0;
        private int _peakActive = 0;
#endif

        public int NumTotal => NumActive + NumInactive;
        public int NumActive => Volatile.Read(ref _numActive);
        public int NumInactive => Volatile.Read(ref _numInactive);
        public Type ItemType => typeof(TValue);

        /// <summary>
        /// Initializes a new instance of the ObjectPool with auto-scaling parameters.
        /// </summary>
        /// <param name="factory">The factory used to create new pool items.</param>
        /// <param name="initialCapacity">The number of items to pre-warm the pool with.</param>
        /// <param name="expansionFactor">The factor by which to expand the pool when empty (e.g., 0.5f for 50%).</param>
        /// <param name="shrinkBufferFactor">The buffer to maintain above the high-water mark (e.g., 0.2f for 20%).</param>
        /// <param name="shrinkCooldownTicks">The number of maintenance checks (via Despawn or Maintenance call) before shrinking.</param>
        public ObjectPool(
            IFactory<TValue> factory,
            int initialCapacity = 0,
            float expansionFactor = 0.5f,
            float shrinkBufferFactor = 0.2f,
            int shrinkCooldownTicks = 2000) // Reduced default slightly since we check more often now
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
                if (initialCapacity > 16) MinCapacity = initialCapacity;
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
                    // Expansion logic
                    int currentTotal = _activeItems.Count + _inactivePool.Count;
                    int expansionAmount = Math.Max(1, (int)(currentTotal * _expansionFactor));
                    ExpandPoolInternal(expansionAmount);
                }

                TValue item = _inactivePool.Pop();
                Interlocked.Decrement(ref _numInactive);
                
                int index = _activeItems.Count;
                _activeItems.Add(item);
                _activeItemIndices[item] = index;
                Interlocked.Increment(ref _numActive);

#if UNITY_EDITOR || DEVELOPMENT_BUILD
                Interlocked.Increment(ref _totalGets);
                int active = _activeItems.Count;
                int peak = Volatile.Read(ref _peakActive);
                if (active > peak)
                {
                    Interlocked.CompareExchange(ref _peakActive, active, peak);
                }
#endif

                try
                {
                    item.OnSpawned(param, this);
                }
                catch (Exception ex)
                {
                    _activeItems.RemoveAt(_activeItems.Count - 1);
                    _activeItemIndices.Remove(item);
                    _inactivePool.Push(item);
                    Interlocked.Decrement(ref _numActive);
                    Interlocked.Increment(ref _numInactive);
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
                _rwLock.ExitWriteLock();
            }
        }

        public void Despawn(TValue item)
        {
            if (item == null) return;

            // Deadlock prevention
            if (_rwLock.IsReadLockHeld)
            {
                _pendingDespawns.Enqueue(item);
                return;
            }

            if (_rwLock.IsWriteLockHeld)
            {
                DespawnWithoutLock(item);
                return;
            }

            _rwLock.EnterWriteLock();
            try
            {
                if (MaxCapacity > 0 && _inactivePool.Count >= MaxCapacity)
                {
                    DespawnAndDestroyWithoutLock(item);
                }
                else
                {
                    DespawnWithoutLock(item);
                }

                _despawnCounter++;
                if (_despawnCounter >= kCheckInterval)
                {
                    _despawnCounter = 0;

                    DrainPendingDespawnsWithoutLock();

                    UpdateShrinkLogic();
                }
            }
            finally
            {
                if (_rwLock.IsWriteLockHeld) _rwLock.ExitWriteLock();
            }
        }

        /// <summary>
        /// Explicitly performs maintenance tasks.
        /// Now largely optional due to auto-maintenance in Despawn, but useful for forced cleanup.
        /// </summary>
        public void Maintenance()
        {
            if (_pendingDespawns.IsEmpty && _shrinkCooldownTicks <= 0) return;

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
                if (MaxCapacity > 0 && _inactivePool.Count >= MaxCapacity)
                {
                    DespawnAndDestroyWithoutLock(item);
                }
                else
                {
                    DespawnWithoutLock(item);
                }
            }
        }

        private void DespawnWithoutLock(TValue item)
        {
            if (!RemoveFromActiveList(item)) return;

            try
            {
                item.OnDespawned();
            }
            catch (Exception ex)
            {
#if DEVELOPMENT_BUILD || UNITY_EDITOR
                UnityEngine.Debug.LogError($"[CycloneGames.Factory] OnDespawned failed. Error: {ex.Message}");
#endif
            }
            finally
            {
                _inactivePool.Push(item);
                Interlocked.Increment(ref _numInactive);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                Interlocked.Increment(ref _totalReturns);
#endif
            }
        }

        private void DespawnAndDestroyWithoutLock(TValue item)
        {
            if (!RemoveFromActiveList(item)) return;

            try
            {
                item.OnDespawned();
            }
            catch { /* Ignore errors during destroy phase */ }
            finally
            {
                (item as IDisposable)?.Dispose();
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                Interlocked.Increment(ref _totalDiscards);
#endif
            }
        }

        private bool RemoveFromActiveList(TValue item)
        {
            if (!_activeItemIndices.TryGetValue(item, out int index)) return false;

            TValue lastItem = _activeItems[_activeItems.Count - 1];
            _activeItems[index] = lastItem;
            _activeItemIndices[lastItem] = index;
            _activeItems.RemoveAt(_activeItems.Count - 1);
            _activeItemIndices.Remove(item);
            Interlocked.Decrement(ref _numActive);
            return true;
        }

        private void UpdateShrinkLogic()
        {
            if (_shrinkCooldownTicks <= 0) return;

            _ticksSinceLastShrink++;
            if (_ticksSinceLastShrink < _shrinkCooldownTicks) return;

            int desiredSize = (int)(_maxActiveSinceLastShrink * (1 + _shrinkBufferFactor));
            desiredSize = Math.Max(desiredSize, MinCapacity);

            int prewarmedSize = _activeItems.Count + _inactivePool.Count;

            if (prewarmedSize > desiredSize)
            {
                int itemsToRemove = prewarmedSize - desiredSize;
                ShrinkPoolInternal(itemsToRemove);
            }

            _maxActiveSinceLastShrink = Math.Max(_activeItems.Count, (int)(_maxActiveSinceLastShrink * 0.5f));

            _ticksSinceLastShrink = 0;
        }

        public void Despawn(object obj)
        {
            if (obj is TValue value) Despawn(value);
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
                _rwLock.ExitWriteLock();
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
                _rwLock.ExitWriteLock();
            }
        }

        private void ExpandPoolInternal(int count)
        {
            for (int i = 0; i < count; i++)
            {
                var created = _factory.Create();
                if (created != null)
                {
                    try
                    {
                        created.OnDespawned();
                    }
                    catch { }
                    _inactivePool.Push(created);
                    Interlocked.Increment(ref _numInactive);
                }
            }
        }

        private void ShrinkPoolInternal(int count)
        {
            int numToActuallyRemove = Math.Min(count, _inactivePool.Count);
            for (int i = 0; i < numToActuallyRemove; i++)
            {
                var item = _inactivePool.Pop();
                Interlocked.Decrement(ref _numInactive);
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
                    try { item.OnDespawned(); }
                    finally { (item as IDisposable)?.Dispose(); }
                }
                _activeItems.Clear();
                _activeItemIndices.Clear();

                foreach (var item in _inactivePool)
                {
                    (item as IDisposable)?.Dispose();
                }
                _inactivePool.Clear();
                _pendingDespawns.Clear();

                Interlocked.Exchange(ref _numActive, 0);
                Interlocked.Exchange(ref _numInactive, 0);
                ResetShrinkTracker();
            }
            finally
            {
                _rwLock.ExitWriteLock();
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
                _rwLock.ExitWriteLock();
            }
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        public PoolStatistics GetStatistics()
        {
            return new PoolStatistics
            {
                NumActive = Volatile.Read(ref _numActive),
                NumInactive = Volatile.Read(ref _numInactive),
                TotalGets = Interlocked.Read(ref _totalGets),
                TotalReturns = Interlocked.Read(ref _totalReturns),
                TotalDiscards = Interlocked.Read(ref _totalDiscards),
                PeakActive = Volatile.Read(ref _peakActive)
            };
        }

        public void ResetStatistics()
        {
            Interlocked.Exchange(ref _totalGets, 0);
            Interlocked.Exchange(ref _totalReturns, 0);
            Interlocked.Exchange(ref _totalDiscards, 0);
            Interlocked.Exchange(ref _peakActive, 0);
        }

        public struct PoolStatistics
        {
            public int NumActive;
            public int NumInactive;
            public long TotalGets;
            public long TotalReturns;
            public long TotalDiscards;
            public int PeakActive;
        }
#endif
    }
}