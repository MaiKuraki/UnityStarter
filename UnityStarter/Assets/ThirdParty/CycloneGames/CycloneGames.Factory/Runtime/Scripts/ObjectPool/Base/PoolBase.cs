using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;

namespace CycloneGames.Factory.Runtime
{
    /// <summary>
    /// Single-owner base for bounded managed object pools.
    /// Structural operations and lifecycle callbacks must run on the owning execution context.
    /// </summary>
    public abstract class PoolBase<TValue> : IDespawnableMemoryPool<TValue>, IBoundedPoolMaintenance, IDisposable where TValue : class
    {
        private readonly List<TValue> _inactiveItems;
        private readonly List<TValue> _activeItems;
        private readonly PoolCapacitySettings _capacitySettings;

        /// <summary>Owned item to active-lease index, or -1 while the item is inactive.</summary>
        private readonly ReferenceOwnershipMap<TValue> _ownership;

        /// <summary>When false the index holds active leases only and misses duplicate inactive instances.</summary>
        private readonly bool _trackAllOwned;

        private int _peakActive;
        private int _peakCountAll;
        private long _totalCreated;
        private long _totalSpawned;
        private long _totalDespawned;
        private long _failedSpawnRollbacks;
        private long _rejectedSpawns;
        private long _invalidDespawns;
        private long _totalDestroyed;
        private long _callbackFailures;
        private long _quarantinedItems;
        private long _invalidatedInactiveItems;
        private long _invalidatedActiveItems;

        private int _lifecycleCallbackDepth;
        private int _structuralVersion;
        private PoolLifecycleState _lifecycleState;

        /// <param name="capacitySettings">Capacity, overflow and trim policy for this pool.</param>
        /// <param name="validateUniqueOwnership">
        /// When true (the default) the pool tracks every owned item in a reference-identity index, so
        /// detecting a factory that returns an item this pool already owns costs O(1) instead of an O(n)
        /// scan. This is what keeps bulk warmup linear. When false, the pool only indexes active leases,
        /// which is cheaper in memory but leaves duplicate inactive instances undetected.
        /// </param>
        protected PoolBase(PoolCapacitySettings capacitySettings, bool validateUniqueOwnership = true)
        {
            int initialTrackingCapacity = Math.Max(capacitySettings.PrewarmCount, capacitySettings.SoftCapacity);
            _inactiveItems = new List<TValue>(initialTrackingCapacity);
            _activeItems = new List<TValue>(initialTrackingCapacity);
            _ownership = new ReferenceOwnershipMap<TValue>(initialTrackingCapacity);
            _trackAllOwned = validateUniqueOwnership;
            _capacitySettings = capacitySettings;
            _lifecycleState = PoolLifecycleState.Ready;
        }

        public int CountAll => CountActive + CountInactive;
        public int CountActive => _activeItems.Count;
        public int CountInactive => _inactiveItems.Count;
        public Type ItemType => typeof(TValue);
        public PoolLifecycleState LifecycleState => _lifecycleState;
        public PoolCapacitySettings CapacitySettings => _capacitySettings;

        /// <summary>
        /// Number of items held by the ownership index; equals <see cref="CountAll"/> unless a destruction
        /// failed, in which case the item stays tracked so a factory cannot re-introduce it.
        /// </summary>
        public int TrackedItemCount => _trackAllOwned ? _ownership.Count : CountAll;

        /// <summary>Item count the tracking storage holds without allocating.</summary>
        public int TrackingCapacity => Math.Min(Math.Min(_activeItems.Capacity, _inactiveItems.Capacity), _ownership.Capacity);

        public PoolDiagnostics Diagnostics => new PoolDiagnostics(
            _peakActive,
            _peakCountAll,
            _totalCreated,
            _totalSpawned,
            _totalDespawned,
            _failedSpawnRollbacks,
            _rejectedSpawns,
            _invalidDespawns,
            _totalDestroyed,
            _callbackFailures,
            _quarantinedItems,
            _invalidatedInactiveItems,
            _invalidatedActiveItems);

        public PoolProfile Profile => new PoolProfile(
            CountAll,
            CountActive,
            CountInactive,
            _lifecycleState,
            _capacitySettings,
            Diagnostics);

        /// <summary>
        /// Ends an active lease and either returns the item to the inactive set or destroys it.
        /// </summary>
        /// <remarks>
        /// Validity is delegated to <see cref="IsValid"/>: with a <c>where TValue : class</c> constraint
        /// <c>== null</c> is a reference comparison, so engine objects destroyed outside the pool still
        /// look alive here and only an adapter can detect them.
        /// </remarks>
        public bool Despawn(TValue item)
        {
            if (ReferenceEquals(item, null) || _lifecycleState != PoolLifecycleState.Ready)
            {
                _invalidDespawns++;
                return false;
            }

            ThrowIfLifecycleCallbackIsRunning();

            // Single probe: RemoveActive reports both "not an active lease" and success.
            return DespawnOwnedItem(item);
        }

        public bool Contains(TValue item)
        {
            return !ReferenceEquals(item, null)
                && _lifecycleState == PoolLifecycleState.Ready
                && IsActiveLease(item);
        }

        /// <summary>
        /// Reserves tracking room for <paramref name="capacity"/> items without creating any, and clamps
        /// to the declared hard capacity. Call once with a measured peak so the hot path never resizes.
        /// </summary>
        public void EnsureCapacity(int capacity)
        {
            ThrowIfNotReady();
            ThrowIfLifecycleCallbackIsRunning();

            if (capacity < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity));
            }

            if (_capacitySettings.HardCapacity > 0 && capacity > _capacitySettings.HardCapacity)
            {
                capacity = _capacitySettings.HardCapacity;
            }

            if (_activeItems.Capacity < capacity)
            {
                _activeItems.Capacity = capacity;
            }

            if (_inactiveItems.Capacity < capacity)
            {
                _inactiveItems.Capacity = capacity;
            }

            _ownership.EnsureCapacity(capacity);
        }

        public void ForEachActive(Action<TValue> action)
        {
            if (action == null)
            {
                throw new ArgumentNullException(nameof(action));
            }

            ThrowIfNotReady();
            IterateActive(action);
        }

        public void ForEachActive<TState>(TState state, Action<TValue, TState> action)
        {
            if (action == null)
            {
                throw new ArgumentNullException(nameof(action));
            }

            ThrowIfNotReady();

            for (int index = _activeItems.Count - 1; index >= 0; index--)
            {
                TValue item = _activeItems[index];
                int versionBeforeCallback = _structuralVersion;
                action(item, state);
                ValidateIterationMutation(item, index, versionBeforeCallback);
            }
        }

        public void Prewarm(int count)
        {
            ThrowIfNotReady();
            ThrowIfLifecycleCallbackIsRunning();

            if (count <= 0)
            {
                return;
            }

            int itemsToCreate = ClampToRemainingCapacity(count);
            for (int i = 0; i < itemsToCreate; i++)
            {
                _inactiveItems.Add(CreateNewItem());
                _structuralVersion++;
                UpdatePeaks();
            }
        }

        public int WarmupStep(int maxItems)
        {
            ThrowIfNotReady();
            ThrowIfLifecycleCallbackIsRunning();

            if (maxItems <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxItems));
            }

            int itemsToCreate = ClampToRemainingCapacity(maxItems);
            for (int i = 0; i < itemsToCreate; i++)
            {
                _inactiveItems.Add(CreateNewItem());
                _structuralVersion++;
            }

            UpdatePeaks();
            return itemsToCreate;
        }

        public IEnumerator WarmupCoroutine(int count, int batchSize = 8)
        {
            if (count < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(count));
            }

            if (batchSize <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(batchSize));
            }

            int created = 0;
            while (created < count && _lifecycleState == PoolLifecycleState.Ready)
            {
                int requested = Math.Min(batchSize, count - created);
                int completed = WarmupStep(requested);
                if (completed == 0)
                {
                    yield break;
                }

                created += completed;
                if (created < count)
                {
                    yield return null;
                }
            }
        }

        public void TrimInactive(int targetInactiveCount)
        {
            ThrowIfNotReady();
            ThrowIfLifecycleCallbackIsRunning();

            if (targetInactiveCount < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(targetInactiveCount));
            }

            List<Exception> failures = null;
            TrimInactiveCore(targetInactiveCount, int.MaxValue, ref failures);
            ReleaseTrackingStorageIfEmpty();

            ThrowFailures("One or more inactive pool items could not be destroyed.", failures);
        }

        public int TrimInactiveStep(int targetInactiveCount, int maxWork)
        {
            ThrowIfNotReady();
            ThrowIfLifecycleCallbackIsRunning();

            if (targetInactiveCount < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(targetInactiveCount));
            }

            if (maxWork <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxWork));
            }

            List<Exception> failures = null;
            int processed = TrimInactiveCore(targetInactiveCount, maxWork, ref failures);
            ReleaseTrackingStorageIfEmpty();
            ThrowFailures("One or more inactive pool items could not be destroyed.", failures);
            return processed;
        }

        public void DespawnAll()
        {
            ThrowIfNotReady();
            ThrowIfLifecycleCallbackIsRunning();

            // Keep going after a failing reset so a single bad item cannot strand the remaining leases.
            // Failures are aggregated, matching Clear and Dispose.
            List<Exception> failures = null;
            while (_activeItems.Count > 0)
            {
                TValue item = _activeItems[_activeItems.Count - 1];
                try
                {
                    if (!DespawnOwnedItem(item))
                    {
                        break;
                    }
                }
                catch (Exception exception)
                {
                    AddFailure(ref failures, exception);
                }
            }

            ThrowFailures("One or more active pool items could not be despawned.", failures);
        }

        public int DespawnStep(int maxItems)
        {
            ThrowIfNotReady();
            ThrowIfLifecycleCallbackIsRunning();

            if (maxItems <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxItems));
            }

            // Same contract as TrimInactiveStep: process the whole batch, then report failures.
            List<Exception> failures = null;
            int processed = 0;
            while (processed < maxItems && _activeItems.Count > 0)
            {
                TValue item = _activeItems[_activeItems.Count - 1];
                try
                {
                    if (!DespawnOwnedItem(item))
                    {
                        break;
                    }
                }
                catch (Exception exception)
                {
                    AddFailure(ref failures, exception);
                }

                processed++;
            }

            ThrowFailures("One or more active pool items could not be despawned.", failures);
            return processed;
        }

        public IEnumerator DespawnAllCoroutine(int batchSize = 8)
        {
            if (batchSize <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(batchSize));
            }

            while (_activeItems.Count > 0 && _lifecycleState == PoolLifecycleState.Ready)
            {
                DespawnStep(Math.Min(batchSize, _activeItems.Count));
                if (_activeItems.Count > 0)
                {
                    yield return null;
                }
            }
        }

        public void Clear()
        {
            ThrowIfNotReady();
            ThrowIfLifecycleCallbackIsRunning();
            List<Exception> failures = ClearOwnedItems();
            ReleaseTrackingStorageIfEmpty();
            ThrowFailures("One or more pool items could not be cleared.", failures);
        }

        public void Dispose()
        {
            if (_lifecycleState == PoolLifecycleState.Disposed
                || _lifecycleState == PoolLifecycleState.Disposing)
            {
                return;
            }

            ThrowIfLifecycleCallbackIsRunning();
            _lifecycleState = PoolLifecycleState.Disposing;
            List<Exception> failures;

            try
            {
                failures = ClearOwnedItems();
            }
            finally
            {
                _lifecycleState = PoolLifecycleState.Disposed;
                _structuralVersion++;
            }

            ThrowFailures("One or more pool items could not be disposed.", failures);
        }

        protected abstract TValue CreateNew();

        protected abstract void OnDespawn(TValue item);

        /// <summary>
        /// Reports whether an item can still be reused; false or a thrown exception quarantines it.
        /// Adapters override this to detect engine objects destroyed by another owner.
        /// </summary>
        protected virtual bool IsValid(TValue item)
        {
            return item != null;
        }

        protected virtual void DestroyItem(TValue item)
        {
            if (item is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }

        protected bool IsDisposed => _lifecycleState == PoolLifecycleState.Disposed;

        protected bool TryAcquireAndTrack(bool throwOnFailure, out TValue item)
        {
            ThrowIfNotReady();
            ThrowIfLifecycleCallbackIsRunning();

            if (!TryGetOrCreateItem(throwOnFailure, out item))
            {
                return false;
            }

            int index = _activeItems.Count;
            _activeItems.Add(item);

            bool tracked = _trackAllOwned
                ? _ownership.TrySetValue(item, index)
                : _ownership.TryAdd(item, index);

            if (!tracked)
            {
                _activeItems.RemoveAt(index);
                _inactiveItems.Add(item);
                throw new InvalidOperationException(
                    $"Pool for {typeof(TValue).Name} lost ownership tracking for a spawned item.");
            }

            _structuralVersion++;
            _totalSpawned++;
            UpdatePeaks();
            return true;
        }

        protected Exception RollbackSpawn(TValue item)
        {
            if (!RemoveActive(item))
            {
                return new InvalidOperationException("A failed spawn lost active ownership tracking.");
            }

            _failedSpawnRollbacks++;

            // Keeps TotalSpawned - TotalDespawned == CountActive exact for rolled-back borrows.
            _totalSpawned--;

            Exception resetFailure = InvokeDespawnCallback(item);
            Exception validationFailure = null;
            if (resetFailure == null && TryValidateItem(item, out validationFailure))
            {
                if (ShouldTrimReturnedItem())
                {
                    return TryDestroyOwnedItem(item);
                }

                _inactiveItems.Add(item);
                _structuralVersion++;
                return null;
            }

            if (resetFailure == null)
            {
                resetFailure = validationFailure;
            }

            _quarantinedItems++;
            Exception destroyFailure = TryDestroyOwnedItem(item);
            return CombineFailures(resetFailure, destroyFailure);
        }

        protected void BeginLifecycleCallback()
        {
            if (_lifecycleCallbackDepth != 0)
            {
                throw new InvalidOperationException("Pool lifecycle callbacks cannot be re-entered.");
            }

            _lifecycleCallbackDepth = 1;
        }

        protected void EndLifecycleCallback()
        {
            _lifecycleCallbackDepth = 0;
        }

        protected static void RethrowSpawnFailure(Exception spawnFailure, Exception cleanupFailure)
        {
            if (cleanupFailure != null)
            {
                throw new AggregateException(
                    "An item failed to spawn and its rollback also failed.",
                    spawnFailure,
                    cleanupFailure);
            }

            ExceptionDispatchInfo.Capture(spawnFailure).Throw();
        }

        private void IterateActive(Action<TValue> action)
        {
            for (int index = _activeItems.Count - 1; index >= 0; index--)
            {
                TValue item = _activeItems[index];
                int versionBeforeCallback = _structuralVersion;
                action(item);
                ValidateIterationMutation(item, index, versionBeforeCallback);
            }
        }

        private void ValidateIterationMutation(TValue item, int index, int versionBeforeCallback)
        {
            if (_structuralVersion == versionBeforeCallback)
            {
                return;
            }

            bool removedCurrentItemOnly = _structuralVersion == versionBeforeCallback + 2
                && _activeItems.Count == index
                && !IsActiveLease(item);

            if (!removedCurrentItemOnly)
            {
                throw new InvalidOperationException(
                    "Active iteration may only despawn the item currently being visited.");
            }
        }

        private bool DespawnOwnedItem(TValue item)
        {
            if (!RemoveActive(item))
            {
                _invalidDespawns++;
                return false;
            }

            _totalDespawned++;

            // Validate first: a destroyed item cannot run reset logic, and its own reset would throw.
            Exception validationFailure = null;
            if (!TryValidateItem(item, out validationFailure))
            {
                _invalidatedActiveItems++;
                _quarantinedItems++;
                Exception invalidCleanupFailure = TryDestroyOwnedItem(item);
                ThrowCombined(validationFailure, invalidCleanupFailure);
                return true;
            }

            Exception callbackFailure = InvokeDespawnCallback(item);
            if (callbackFailure != null)
            {
                _quarantinedItems++;
                Exception callbackCleanupFailure = TryDestroyOwnedItem(item);
                ThrowCombined(callbackFailure, callbackCleanupFailure);
                return true;
            }

            if (ShouldTrimReturnedItem())
            {
                Exception trimCleanupFailure = TryDestroyOwnedItem(item);
                ThrowCombined(null, trimCleanupFailure);
                return true;
            }

            _inactiveItems.Add(item);
            _structuralVersion++;
            return true;
        }

        private Exception InvokeDespawnCallback(TValue item)
        {
            BeginLifecycleCallback();
            try
            {
                OnDespawn(item);
                return null;
            }
            catch (Exception exception)
            {
                _callbackFailures++;
                return exception;
            }
            finally
            {
                EndLifecycleCallback();
            }
        }

        private bool TryGetOrCreateItem(bool throwOnFailure, out TValue item)
        {
            while (_inactiveItems.Count > 0)
            {
                item = PopInactive();
                if (TryValidateItem(item, out Exception validationFailure))
                {
                    return true;
                }

                _invalidatedInactiveItems++;
                if (validationFailure != null)
                {
                    _callbackFailures++;
                }

                // The item has left the logical collection, but the pool still owns its cleanup.
                // Always use the same destruction path so ownership tracking matches disposal.
                Exception destroyFailure = TryDestroyOwnedItem(item);
                ThrowCombined(validationFailure, destroyFailure);
            }

            if (_capacitySettings.HardCapacity > 0 && CountAll >= _capacitySettings.HardCapacity)
            {
                _rejectedSpawns++;
                item = null;

                if (throwOnFailure && _capacitySettings.OverflowPolicy == PoolOverflowPolicy.Throw)
                {
                    throw new InvalidOperationException(
                        $"Pool for {typeof(TValue).Name} reached hard capacity {_capacitySettings.HardCapacity}.");
                }

                return false;
            }

            item = CreateNewItem();
            return true;
        }

        private TValue CreateNewItem()
        {
            BeginLifecycleCallback();
            TValue item;

            try
            {
                item = CreateNew();
            }
            finally
            {
                EndLifecycleCallback();
            }

            if (!TryValidateItem(item, out Exception validationFailure))
            {
                if (validationFailure != null)
                {
                    ExceptionDispatchInfo.Capture(validationFailure).Throw();
                }

                throw new InvalidOperationException(
                    $"Pool factory for {typeof(TValue).Name} returned an invalid item.");
            }

            if (!TryRegisterOwnership(item))
            {
                throw new InvalidOperationException(
                    $"Pool factory for {typeof(TValue).Name} returned an item already owned by this pool.");
            }

            _totalCreated++;
            return item;
        }

        /// <summary>Registers a created item as inactive, or refuses an item already indexed as active.</summary>
        private bool TryRegisterOwnership(TValue item)
        {
            return _trackAllOwned ? _ownership.TryAdd(item, -1) : !_ownership.ContainsKey(item);
        }

        private bool IsActiveLease(TValue item)
        {
            return _ownership.TryGetValue(item, out int index) && index >= 0;
        }

        private TValue PopInactive()
        {
            int lastIndex = _inactiveItems.Count - 1;
            TValue item = _inactiveItems[lastIndex];
            _inactiveItems.RemoveAt(lastIndex);
            _structuralVersion++;
            return item;
        }

        private bool RemoveActive(TValue item)
        {
            if (!_ownership.TryGetValue(item, out int index) || index < 0)
            {
                return false;
            }

            int lastIndex = _activeItems.Count - 1;
            TValue lastItem = _activeItems[lastIndex];
            _activeItems[index] = lastItem;
            _activeItems.RemoveAt(lastIndex);

            if (index != lastIndex)
            {
                _ownership.TrySetValue(lastItem, index);
            }

            if (_trackAllOwned)
            {
                // The item stays owned; only its lease ends.
                _ownership.TrySetValue(item, -1);
            }
            else
            {
                _ownership.Remove(item);
            }

            _structuralVersion++;
            return true;
        }

        private bool TryValidateItem(TValue item, out Exception failure)
        {
            try
            {
                failure = null;
                return IsValid(item);
            }
            catch (Exception exception)
            {
                failure = exception;
                return false;
            }
        }

        private Exception TryDestroyOwnedItem(TValue item)
        {
            BeginLifecycleCallback();
            try
            {
                DestroyItem(item);
                _totalDestroyed++;
                _ownership.Remove(item);
                return null;
            }
            catch (Exception exception)
            {
                _callbackFailures++;
                return exception;
            }
            finally
            {
                EndLifecycleCallback();
                _structuralVersion++;
            }
        }

        private List<Exception> ClearOwnedItems()
        {
            List<Exception> failures = null;

            while (_activeItems.Count > 0)
            {
                TValue item = _activeItems[_activeItems.Count - 1];
                try
                {
                    if (!DespawnOwnedItem(item))
                    {
                        break;
                    }
                }
                catch (Exception exception)
                {
                    AddFailure(ref failures, exception);
                }
            }

            while (_inactiveItems.Count > 0)
            {
                TValue item = PopInactive();
                if (!TryValidateItem(item, out Exception validationFailure))
                {
                    _invalidatedInactiveItems++;
                    AddFailure(ref failures, validationFailure);
                    AddFailure(ref failures, TryDestroyOwnedItem(item));
                    continue;
                }

                AddFailure(ref failures, TryDestroyOwnedItem(item));
            }

            return failures;
        }

        private int ClampToRemainingCapacity(int requested)
        {
            if (_capacitySettings.HardCapacity <= 0)
            {
                return requested;
            }

            return Math.Min(requested, Math.Max(0, _capacitySettings.HardCapacity - CountAll));
        }

        private int TrimInactiveCore(
            int targetInactiveCount,
            int maxWork,
            ref List<Exception> failures)
        {
            int processed = 0;
            while (processed < maxWork && _inactiveItems.Count > targetInactiveCount)
            {
                TValue item = PopInactive();
                processed++;
                if (!TryValidateItem(item, out Exception validationFailure))
                {
                    _invalidatedInactiveItems++;
                    AddFailure(ref failures, validationFailure);
                    AddFailure(ref failures, TryDestroyOwnedItem(item));
                    continue;
                }

                AddFailure(ref failures, TryDestroyOwnedItem(item));
            }

            return processed;
        }

        /// <summary>
        /// An empty pool keeps no tracking storage: a single spike must not raise the permanent footprint
        /// of an otherwise idle pool.
        /// </summary>
        private void ReleaseTrackingStorageIfEmpty()
        {
            if (_activeItems.Count != 0 || _inactiveItems.Count != 0 || _ownership.Count != 0)
            {
                return;
            }

            _activeItems.Capacity = 0;
            _inactiveItems.Capacity = 0;
            _ownership.ReleaseCapacity();
        }

        private bool ShouldTrimReturnedItem()
        {
            return _capacitySettings.TrimPolicy == PoolTrimPolicy.TrimOnDespawn
                && CountInactive >= _capacitySettings.SoftCapacity;
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

        private void ThrowIfNotReady()
        {
            if (_lifecycleState != PoolLifecycleState.Ready)
            {
                throw new ObjectDisposedException(GetType().Name);
            }
        }

        private void ThrowIfLifecycleCallbackIsRunning()
        {
            if (_lifecycleCallbackDepth != 0)
            {
                throw new InvalidOperationException(
                    "Pool mutation is not allowed from create, spawn, despawn, or destroy callbacks.");
            }
        }

        private static Exception CombineFailures(Exception first, Exception second)
        {
            if (first == null)
            {
                return second;
            }

            if (second == null)
            {
                return first;
            }

            return new AggregateException(first, second);
        }

        private static void ThrowCombined(Exception primary, Exception cleanup)
        {
            if (primary != null && cleanup != null)
            {
                throw new AggregateException(primary, cleanup);
            }

            Exception failure = primary ?? cleanup;
            if (failure != null)
            {
                ExceptionDispatchInfo.Capture(failure).Throw();
            }
        }

        private static void AddFailure(ref List<Exception> failures, Exception failure)
        {
            if (failure == null)
            {
                return;
            }

            if (failures == null)
            {
                failures = new List<Exception>();
            }

            failures.Add(failure);
        }

        private static void ThrowFailures(string message, List<Exception> failures)
        {
            if (failures == null || failures.Count == 0)
            {
                return;
            }

            throw new AggregateException(message, failures);
        }
    }
}
