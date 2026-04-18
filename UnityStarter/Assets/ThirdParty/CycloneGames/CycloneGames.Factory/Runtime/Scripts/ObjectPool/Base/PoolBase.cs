using System;
using System.Collections;
using System.Collections.Generic;

namespace CycloneGames.Factory.Runtime
{
    /// <summary>
    /// Abstract base class providing shared tracking, capacity management, diagnostics,
    /// and lifecycle infrastructure for main-thread object pools.
    /// Subclasses provide item creation, spawn/despawn hooks, and specific spawn signatures.
    /// </summary>
    public abstract class PoolBase<TValue> : IDespawnableMemoryPool<TValue>, IDisposable where TValue : class
    {
        private readonly Stack<TValue> _inactiveItems;
        private readonly List<TValue> _activeItems;
        private readonly Dictionary<TValue, int> _activeItemIndices;

        private int _softCapacity;
        private int _hardCapacity;
        private PoolOverflowPolicy _overflowPolicy;
        private PoolTrimPolicy _trimPolicy;

        private int _peakActive;
        private int _peakCountAll;
        private int _totalCreated;
        private int _totalSpawned;
        private int _totalDespawned;
        private int _failedSpawnRollbacks;
        private int _rejectedSpawns;
        private int _invalidDespawns;
        private int _destroyedOnTrim;

        private bool _disposed;

        public int CountAll => CountActive + CountInactive;
        public int CountActive => _activeItems.Count;
        public int CountInactive => _inactiveItems.Count;
        public Type ItemType => typeof(TValue);

        public PoolCapacitySettings CapacitySettings =>
            new(_softCapacity, _hardCapacity, _overflowPolicy, _trimPolicy);

        public PoolDiagnostics Diagnostics => new(
            _peakActive,
            _peakCountAll,
            _totalCreated,
            _totalSpawned,
            _totalDespawned,
            _failedSpawnRollbacks,
            _rejectedSpawns,
            _invalidDespawns,
            _destroyedOnTrim);

        public PoolProfile Profile =>
            new(CountAll, CountActive, CountInactive, CapacitySettings, Diagnostics);

        public int SoftCapacity
        {
            get => _softCapacity;
            set => _softCapacity = ValidateSoftCapacity(value, _hardCapacity);
        }

        public int MaxCapacity
        {
            get => _hardCapacity;
            set => _hardCapacity = ValidateHardCapacity(value, _softCapacity);
        }

        public PoolOverflowPolicy OverflowPolicy
        {
            get => _overflowPolicy;
            set => _overflowPolicy = value;
        }

        public PoolTrimPolicy TrimPolicy
        {
            get => _trimPolicy;
            set => _trimPolicy = value;
        }

        /// <summary>
        /// Initializes shared pool state. Does NOT call Prewarm — subclasses must call it
        /// after their own fields are initialized to avoid virtual calls on uninitialized state.
        /// </summary>
        protected PoolBase(PoolCapacitySettings capacitySettings)
        {
            _inactiveItems = new Stack<TValue>(capacitySettings.SoftCapacity);
            _activeItems = new List<TValue>(capacitySettings.SoftCapacity);
            _activeItemIndices = new Dictionary<TValue, int>(capacitySettings.SoftCapacity);
            _softCapacity = capacitySettings.SoftCapacity;
            _hardCapacity = capacitySettings.HardCapacity;
            _overflowPolicy = capacitySettings.OverflowPolicy;
            _trimPolicy = capacitySettings.TrimPolicy;
        }

        public bool Despawn(TValue item)
        {
            if (item == null || _disposed)
            {
                _invalidDespawns++;
                return false;
            }

            if (!RemoveActive(item))
            {
                _invalidDespawns++;
                return false;
            }

            try
            {
                OnDespawn(item);
            }
            finally
            {
                _totalDespawned++;
                if (ShouldTrimReturnedItem())
                {
                    DestroyItem(item);
                }
                else
                {
                    _inactiveItems.Push(item);
                }
            }

            return true;
        }

        public bool Contains(TValue item)
        {
            return item != null && _activeItemIndices.ContainsKey(item);
        }

        public void ForEachActive(Action<TValue> action)
        {
            if (action == null) throw new ArgumentNullException(nameof(action));

            for (int i = _activeItems.Count - 1; i >= 0; i--)
            {
                action(_activeItems[i]);
            }
        }

        public void ForEachActive<TState>(TState state, Action<TValue, TState> action)
        {
            if (action == null) throw new ArgumentNullException(nameof(action));

            for (int i = _activeItems.Count - 1; i >= 0; i--)
            {
                action(_activeItems[i], state);
            }
        }

        public void Prewarm(int count)
        {
            ThrowIfDisposed();
            if (count <= 0) return;

            int capacityLeft = GetRemainingCapacity();
            if (capacityLeft == 0) return;

            int itemsToCreate = _hardCapacity > 0 ? Math.Min(count, capacityLeft) : count;
            for (int i = 0; i < itemsToCreate; i++)
            {
                _inactiveItems.Push(CreateNewItem());
                UpdatePeaks();
            }
        }

        public void TrimInactive(int targetInactiveCount)
        {
            if (targetInactiveCount < 0) throw new ArgumentOutOfRangeException(nameof(targetInactiveCount));

            while (_inactiveItems.Count > targetInactiveCount)
            {
                TValue item = _inactiveItems.Pop();
                if (IsValid(item))
                {
                    DestroyItem(item);
                }
            }
        }

        public void DespawnAll()
        {
            Exception firstException = null;
            while (_activeItems.Count > 0)
            {
                int countBefore = _activeItems.Count;
                TValue item = _activeItems[countBefore - 1];
                try
                {
                    Despawn(item);
                }
                catch (Exception ex) when (firstException == null)
                {
                    firstException = ex;
                }
                catch
                {
                }

                // Guarantee progress: if Despawn failed to remove the item, force-remove it.
                if (_activeItems.Count >= countBefore)
                {
                    int last = _activeItems.Count - 1;
                    TValue staleItem = _activeItems[last];
                    _activeItems.RemoveAt(last);
                    try { _activeItemIndices.Remove(staleItem); } catch { }
                }
            }

            if (firstException != null)
            {
                throw firstException;
            }
        }

        /// <summary>
        /// Non-alloc frame-sliced despawn. Processes up to <paramref name="maxItems"/> active items.
        /// Returns the number processed in this step.
        /// </summary>
        public int DespawnStep(int maxItems)
        {
            ThrowIfDisposed();
            if (maxItems <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxItems));
            }

            int processed = 0;
            Exception firstException = null;
            while (processed < maxItems && _activeItems.Count > 0)
            {
                int countBefore = _activeItems.Count;
                TValue item = _activeItems[countBefore - 1];
                try
                {
                    Despawn(item);
                }
                catch (Exception ex) when (firstException == null)
                {
                    firstException = ex;
                }
                catch
                {
                    // Keep progressing in frame-sliced mode.
                }

                if (_activeItems.Count >= countBefore)
                {
                    int last = _activeItems.Count - 1;
                    TValue staleItem = _activeItems[last];
                    _activeItems.RemoveAt(last);
                    try { _activeItemIndices.Remove(staleItem); } catch { }
                }

                processed++;
            }

            if (firstException != null)
            {
                throw firstException;
            }

            return processed;
        }

        /// <summary>
        /// Non-alloc frame-sliced warmup. Creates up to <paramref name="maxItems"/> inactive items.
        /// Returns the number created in this step.
        /// </summary>
        public int WarmupStep(int maxItems)
        {
            ThrowIfDisposed();
            if (maxItems <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxItems));
            }

            int capacityLeft = GetRemainingCapacity();
            if (capacityLeft == 0)
            {
                return 0;
            }

            int itemsToCreate = _hardCapacity > 0 ? Math.Min(maxItems, capacityLeft) : maxItems;
            for (int i = 0; i < itemsToCreate; i++)
            {
                _inactiveItems.Push(CreateNewItem());
            }
            UpdatePeaks();
            return itemsToCreate;
        }

        /// <summary>
        /// Coroutine-friendly DespawnAll that yields every <paramref name="batchSize"/> items
        /// to spread the cost across multiple frames.
        /// </summary>
        public IEnumerator DespawnAllCoroutine(int batchSize = 8)
        {
            if (batchSize <= 0)
            {
                batchSize = 1;
            }

            int remainingUntilYield = batchSize;
            while (_activeItems.Count > 0)
            {
                int countBefore = _activeItems.Count;
                TValue item = _activeItems[countBefore - 1];
                try
                {
                    Despawn(item);
                }
                catch
                {
                    // Swallow – coroutines cannot propagate exceptions usefully.
                }

                if (_activeItems.Count >= countBefore)
                {
                    int last = _activeItems.Count - 1;
                    TValue staleItem = _activeItems[last];
                    _activeItems.RemoveAt(last);
                    try { _activeItemIndices.Remove(staleItem); } catch { }
                }

                remainingUntilYield--;
                if (remainingUntilYield <= 0)
                {
                    remainingUntilYield = batchSize;
                    yield return null;
                }
            }
        }

        public void Clear()
        {
            Exception firstException = null;

            try
            {
                DespawnAll();
            }
            catch (Exception ex)
            {
                firstException = ex;
            }

            try
            {
                TrimInactive(0);
            }
            catch (Exception ex) when (firstException == null)
            {
                firstException = ex;
            }

            if (firstException != null)
            {
                throw firstException;
            }
        }

        /// <summary>
        /// Coroutine-friendly Prewarm that yields every <paramref name="batchSize"/> items.
        /// </summary>
        public IEnumerator WarmupCoroutine(int count, int batchSize = 8)
        {
            if (batchSize <= 0)
            {
                batchSize = 1;
            }

            int remainingUntilYield = batchSize;
            for (int i = 0; i < count; i++)
            {
                if (_disposed) yield break;
                if (_hardCapacity > 0 && CountAll >= _hardCapacity) yield break;

                _inactiveItems.Push(CreateNewItem());
                UpdatePeaks();

                remainingUntilYield--;
                if (remainingUntilYield <= 0)
                {
                    remainingUntilYield = batchSize;
                    yield return null;
                }
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            Clear();
            _disposed = true;
        }

        /// <summary>
        /// Creates a new poolable item. Must return a valid (non-null) item.
        /// </summary>
        protected abstract TValue CreateNew();

        /// <summary>
        /// Called when an item is being despawned (returned to the pool or destroyed).
        /// </summary>
        protected abstract void OnDespawn(TValue item);

        /// <summary>
        /// Determines whether an item is still valid (e.g., not destroyed).
        /// </summary>
        protected virtual bool IsValid(TValue item)
        {
            return item != null;
        }

        /// <summary>
        /// Destroys an item permanently. Called when trimming excess capacity.
        /// </summary>
        protected virtual void DestroyItem(TValue item)
        {
            if (item is IDisposable disposable)
            {
                disposable.Dispose();
            }

            _destroyedOnTrim++;
        }

        /// <summary>
        /// The inactive item stack. Exposed for subclasses that need direct access.
        /// </summary>
        protected Stack<TValue> InactiveItems => _inactiveItems;

        protected bool IsDisposed => _disposed;

        /// <summary>
        /// Acquires an item from the inactive stack or creates a new one,
        /// then tracks it as active. Does NOT call spawn hooks.
        /// Caller must invoke the appropriate spawn hook and handle rollback on failure.
        /// </summary>
        protected bool TryAcquireAndTrack(bool throwOnFailure, out TValue item)
        {
            ThrowIfDisposed();

            if (!TryGetOrCreateItem(throwOnFailure, out item))
            {
                return false;
            }

            TrackAsActive(item);
            return true;
        }

        /// <summary>
        /// Rolls back a spawn that failed during the hook call.
        /// Untracks the item and returns it to the pool or destroys it.
        /// </summary>
        protected void RollbackSpawn(TValue item)
        {
            RemoveActive(item);
            _failedSpawnRollbacks++;
            TryResetAfterFailedSpawn(item);
            if (ShouldTrimReturnedItem())
            {
                DestroyItem(item);
            }
            else
            {
                _inactiveItems.Push(item);
            }
        }

        private TValue CreateNewItem()
        {
            TValue item = CreateNew();
            if (!IsValid(item))
            {
                throw new InvalidOperationException($"Pool factory for {typeof(TValue).Name} returned an invalid item.");
            }

            _totalCreated++;
            return item;
        }

        private void TrackAsActive(TValue item)
        {
            int index = _activeItems.Count;
            _activeItems.Add(item);
            _activeItemIndices[item] = index;
            _totalSpawned++;
            UpdatePeaks();
        }

        private bool RemoveActive(TValue item)
        {
            if (!_activeItemIndices.TryGetValue(item, out int index))
            {
                return false;
            }

            int lastIndex = _activeItems.Count - 1;
            TValue lastItem = _activeItems[lastIndex];

            _activeItems[index] = lastItem;
            _activeItems.RemoveAt(lastIndex);
            _activeItemIndices.Remove(item);

            if (index != lastIndex)
            {
                _activeItemIndices[lastItem] = index;
            }

            return true;
        }

        private bool TryGetOrCreateItem(bool throwOnFailure, out TValue item)
        {
            while (_inactiveItems.Count > 0)
            {
                item = _inactiveItems.Pop();
                if (IsValid(item))
                {
                    return true;
                }
            }

            if (_hardCapacity > 0 && CountAll >= _hardCapacity)
            {
                _rejectedSpawns++;
                if (_overflowPolicy == PoolOverflowPolicy.ReturnNull && !throwOnFailure)
                {
                    item = null;
                    return false;
                }

                throw new InvalidOperationException($"Pool for {typeof(TValue).Name} reached max capacity {_hardCapacity}.");
            }

            item = CreateNewItem();
            return true;
        }

        private bool ShouldTrimReturnedItem()
        {
            return _trimPolicy == PoolTrimPolicy.TrimOnDespawn
                && _softCapacity >= 0
                && CountInactive >= _softCapacity;
        }

        private int GetRemainingCapacity()
        {
            if (_hardCapacity <= 0)
            {
                return int.MaxValue;
            }

            return Math.Max(0, _hardCapacity - CountAll);
        }

        private void UpdatePeaks()
        {
            if (CountActive > _peakActive)
            {
                _peakActive = CountActive;
            }

            if (CountAll > _peakCountAll)
            {
                _peakCountAll = CountAll;
            }
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(GetType().Name);
            }
        }

        private void TryResetAfterFailedSpawn(TValue item)
        {
            try
            {
                OnDespawn(item);
            }
            catch
            {
            }
        }

        private static int ValidateSoftCapacity(int softCapacity, int hardCapacity)
        {
            if (softCapacity < 0) throw new ArgumentOutOfRangeException(nameof(softCapacity));
            if (hardCapacity > 0 && softCapacity > hardCapacity) throw new ArgumentException("Soft capacity cannot exceed hard capacity.", nameof(softCapacity));
            return softCapacity;
        }

        private static int ValidateHardCapacity(int hardCapacity, int softCapacity)
        {
            if (hardCapacity == 0 || hardCapacity < -1) throw new ArgumentOutOfRangeException(nameof(hardCapacity));
            if (hardCapacity > 0 && softCapacity > hardCapacity) throw new ArgumentException("Hard capacity cannot be smaller than soft capacity.", nameof(hardCapacity));
            return hardCapacity;
        }
    }
}
