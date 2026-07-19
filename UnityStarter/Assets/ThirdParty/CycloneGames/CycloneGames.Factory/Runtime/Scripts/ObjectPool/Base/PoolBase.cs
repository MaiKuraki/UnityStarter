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
    public abstract class PoolBase<TValue> : IDespawnableMemoryPool<TValue>, IDisposable where TValue : class
    {
        private readonly List<TValue> _inactiveItems;
        private readonly List<TValue> _activeItems;
        private readonly Dictionary<TValue, int> _activeItemIndices;
        private readonly PoolCapacitySettings _capacitySettings;

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

        private int _lifecycleCallbackDepth;
        private int _structuralVersion;
        private PoolLifecycleState _lifecycleState;

        protected PoolBase(PoolCapacitySettings capacitySettings)
        {
            int initialTrackingCapacity = capacitySettings.SoftCapacity;
            _inactiveItems = new List<TValue>(initialTrackingCapacity);
            _activeItems = new List<TValue>(initialTrackingCapacity);
            _activeItemIndices = new Dictionary<TValue, int>(
                initialTrackingCapacity,
                ReferenceIdentityComparer<TValue>.Instance);
            _capacitySettings = capacitySettings;
            _lifecycleState = PoolLifecycleState.Ready;
        }

        public int CountAll => CountActive + CountInactive;
        public int CountActive => _activeItems.Count;
        public int CountInactive => _inactiveItems.Count;
        public Type ItemType => typeof(TValue);
        public PoolLifecycleState LifecycleState => _lifecycleState;
        public PoolCapacitySettings CapacitySettings => _capacitySettings;
        public int SoftCapacity => _capacitySettings.SoftCapacity;
        public int MaxCapacity => _capacitySettings.HardCapacity;
        public PoolOverflowPolicy OverflowPolicy => _capacitySettings.OverflowPolicy;
        public PoolTrimPolicy TrimPolicy => _capacitySettings.TrimPolicy;

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
            _invalidatedInactiveItems);

        public PoolProfile Profile => new PoolProfile(
            CountAll,
            CountActive,
            CountInactive,
            _lifecycleState,
            _capacitySettings,
            Diagnostics);

        public bool Despawn(TValue item)
        {
            if (item == null || _lifecycleState != PoolLifecycleState.Ready)
            {
                _invalidDespawns++;
                return false;
            }

            ThrowIfLifecycleCallbackIsRunning();

            if (!_activeItemIndices.ContainsKey(item))
            {
                _invalidDespawns++;
                return false;
            }

            DespawnOwnedItem(item);
            return true;
        }

        public bool Contains(TValue item)
        {
            return item != null
                && _lifecycleState == PoolLifecycleState.Ready
                && _activeItemIndices.ContainsKey(item);
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
            while (_inactiveItems.Count > targetInactiveCount)
            {
                TValue item = PopInactive();
                if (!TryValidateItem(item, out Exception validationFailure))
                {
                    _invalidatedInactiveItems++;
                    AddFailure(ref failures, validationFailure);
                    continue;
                }

                AddFailure(ref failures, TryDestroyOwnedItem(item));
            }

            ThrowFailures("One or more inactive pool items could not be destroyed.", failures);
        }

        public void DespawnAll()
        {
            ThrowIfNotReady();
            ThrowIfLifecycleCallbackIsRunning();

            while (_activeItems.Count > 0)
            {
                DespawnOwnedItem(_activeItems[_activeItems.Count - 1]);
            }
        }

        public int DespawnStep(int maxItems)
        {
            ThrowIfNotReady();
            ThrowIfLifecycleCallbackIsRunning();

            if (maxItems <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxItems));
            }

            int processed = 0;
            while (processed < maxItems && _activeItems.Count > 0)
            {
                DespawnOwnedItem(_activeItems[_activeItems.Count - 1]);
                processed++;
            }

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

            try
            {
                _activeItemIndices.Add(item, index);
            }
            catch
            {
                _activeItems.RemoveAt(index);
                _inactiveItems.Add(item);
                throw;
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
                && !_activeItemIndices.ContainsKey(item);

            if (!removedCurrentItemOnly)
            {
                throw new InvalidOperationException(
                    "Active iteration may only despawn the item currently being visited.");
            }
        }

        private void DespawnOwnedItem(TValue item)
        {
            if (!RemoveActive(item))
            {
                _invalidDespawns++;
                return;
            }

            _totalDespawned++;
            Exception callbackFailure = InvokeDespawnCallback(item);
            Exception validationFailure = null;
            bool isValid = callbackFailure == null && TryValidateItem(item, out validationFailure);

            Exception cleanupFailure = null;
            if (callbackFailure != null || validationFailure != null || !isValid)
            {
                _quarantinedItems++;
                cleanupFailure = TryDestroyOwnedItem(item);
            }
            else if (ShouldTrimReturnedItem())
            {
                cleanupFailure = TryDestroyOwnedItem(item);
            }
            else
            {
                _inactiveItems.Add(item);
                _structuralVersion++;
            }

            ThrowCombined(callbackFailure ?? validationFailure, cleanupFailure);
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
                    Exception destroyFailure = TryDestroyOwnedItem(item);
                    ThrowCombined(validationFailure, destroyFailure);
                }
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

            if (_activeItemIndices.ContainsKey(item) || ContainsInactiveReference(item))
            {
                throw new InvalidOperationException(
                    $"Pool factory for {typeof(TValue).Name} returned an item already owned by this pool.");
            }

            _totalCreated++;
            return item;
        }

        private bool ContainsInactiveReference(TValue item)
        {
            for (int i = 0; i < _inactiveItems.Count; i++)
            {
                if (ReferenceEquals(_inactiveItems[i], item))
                {
                    return true;
                }
            }

            return false;
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
                    DespawnOwnedItem(item);
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
