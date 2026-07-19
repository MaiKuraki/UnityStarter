using System;
using System.Collections.Generic;
using System.Threading;

using CycloneGames.Logger;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace CycloneGames.GameplayAbilities.Runtime
{
    /// <summary>
    /// Immutable statistics snapshot for one asset pool.
    /// </summary>
    public readonly struct PoolStatistics
    {
        public string AssetKey { get; }
        public int CurrentPoolSize { get; }
        public int ActiveCount { get; }
        public int PendingCount { get; }
        public int PeakActive { get; }
        public long TotalGets { get; }
        public long TotalReturns { get; }
        public long TotalHits { get; }
        public long TotalCreated { get; }
        public long TotalDestroyed { get; }
        public float HitRate => TotalGets > 0 ? (float)TotalHits / TotalGets : 0f;

        internal PoolStatistics(
            string assetKey,
            int currentPoolSize,
            int activeCount,
            int pendingCount,
            int peakActive,
            long totalGets,
            long totalReturns,
            long totalHits,
            long totalCreated,
            long totalDestroyed)
        {
            AssetKey = assetKey;
            CurrentPoolSize = currentPoolSize;
            ActiveCount = activeCount;
            PendingCount = pendingCount;
            PeakActive = peakActive;
            TotalGets = totalGets;
            TotalReturns = totalReturns;
            TotalHits = totalHits;
            TotalCreated = totalCreated;
            TotalDestroyed = totalDestroyed;
        }
    }

    /// <summary>
    /// Bounded, main-thread-owned GameObject pool manager.
    /// Unity objects and all mutable pool state have main-thread affinity. Resource loading may complete on
    /// another thread, but every continuation returns to the Unity main thread before touching pool state.
    /// </summary>
    public sealed class GameObjectPoolManager : IGameObjectPoolManager, IGameObjectLeaseAuthority, IDisposable
    {
        /// <summary>
        /// Explicit capacity and retention policy. Values are hardware-profile inputs supplied by composition;
        /// the pool does not infer capacity from platform compiler symbols.
        /// </summary>
        public readonly struct PoolConfig
        {
            public int MaxAssetPools { get; }
            public int MaxActiveLeases { get; }
            public int MaxActiveLeasesPerPool { get; }
            public int MaxRetainedInstancesPerPool { get; }
            public int MaxTotalRetainedInstances { get; }
            public int MinRetainedInstancesPerPool { get; }
            public float IdleExpirationTime { get; }

            public PoolConfig(
                int maxAssetPools,
                int maxActiveLeases,
                int maxActiveLeasesPerPool,
                int maxRetainedInstancesPerPool,
                int minRetainedInstancesPerPool,
                float idleExpirationTime,
                int maxTotalRetainedInstances = 0)
            {
                MaxAssetPools = maxAssetPools;
                MaxActiveLeases = maxActiveLeases;
                MaxActiveLeasesPerPool = maxActiveLeasesPerPool;
                MaxRetainedInstancesPerPool = maxRetainedInstancesPerPool;
                MaxTotalRetainedInstances = maxTotalRetainedInstances > 0
                    ? maxTotalRetainedInstances
                    : maxActiveLeases;
                MinRetainedInstancesPerPool = minRetainedInstancesPerPool;
                IdleExpirationTime = idleExpirationTime;
                Validate(nameof(PoolConfig));
            }

            internal void Validate(string parameterName)
            {
                if (MaxAssetPools <= 0)
                {
                    throw new ArgumentOutOfRangeException(parameterName, "MaxAssetPools must be greater than zero.");
                }

                if (MaxActiveLeases <= 0)
                {
                    throw new ArgumentOutOfRangeException(parameterName, "MaxActiveLeases must be greater than zero.");
                }

                if (MaxActiveLeasesPerPool <= 0 || MaxActiveLeasesPerPool > MaxActiveLeases)
                {
                    throw new ArgumentOutOfRangeException(parameterName, "MaxActiveLeasesPerPool must be positive and no greater than MaxActiveLeases.");
                }

                if (MaxRetainedInstancesPerPool < 0)
                {
                    throw new ArgumentOutOfRangeException(parameterName, "MaxRetainedInstancesPerPool cannot be negative.");
                }

                if (MaxTotalRetainedInstances <= 0)
                {
                    throw new ArgumentOutOfRangeException(parameterName, "MaxTotalRetainedInstances must be greater than zero.");
                }

                if (MinRetainedInstancesPerPool < 0 || MinRetainedInstancesPerPool > MaxRetainedInstancesPerPool)
                {
                    throw new ArgumentOutOfRangeException(parameterName, "MinRetainedInstancesPerPool must be between zero and MaxRetainedInstancesPerPool.");
                }

                if (float.IsNaN(IdleExpirationTime) || float.IsInfinity(IdleExpirationTime) || IdleExpirationTime < 0f)
                {
                    throw new ArgumentOutOfRangeException(parameterName, "IdleExpirationTime must be finite and non-negative. Zero disables idle expiration.");
                }
            }
        }

        private readonly struct RetainedInstance
        {
            public readonly int InstanceId;
            public readonly GameObject Instance;
            public readonly Vector3 InitialLocalScale;
            public readonly IGameObjectPoolLifecycle[] LifecycleHandlers;

            public RetainedInstance(
                int instanceId,
                GameObject instance,
                Vector3 initialLocalScale,
                IGameObjectPoolLifecycle[] lifecycleHandlers)
            {
                InstanceId = instanceId;
                Instance = instance;
                InitialLocalScale = initialLocalScale;
                LifecycleHandlers = lifecycleHandlers;
            }
        }

        private readonly struct ActiveLeaseRecord
        {
            public readonly GameObject Instance;
            public readonly PoolData Owner;
            public readonly long Generation;
            public readonly Vector3 InitialLocalScale;
            public readonly IGameObjectPoolLifecycle[] LifecycleHandlers;

            public ActiveLeaseRecord(
                GameObject instance,
                PoolData owner,
                long generation,
                Vector3 initialLocalScale,
                IGameObjectPoolLifecycle[] lifecycleHandlers)
            {
                Instance = instance;
                Owner = owner;
                Generation = generation;
                InitialLocalScale = initialLocalScale;
                LifecycleHandlers = lifecycleHandlers;
            }
        }

        private sealed class PoolData
        {
            public readonly string AssetKey;
            public readonly Stack<RetainedInstance> Retained;
            public IResourceHandle<GameObject> Handle;
            public UniTaskCompletionSource<IResourceHandle<GameObject>> PendingLoad;
            public CancellationTokenSource PendingLoadCancellation;
            public int PendingLoadWaiterCount;
            public bool PrewarmInProgress;
            public int PendingLeaseRequestCount;
            public int ActiveCount;
            public int PeakActive;
            public int RecentPeakActive;
            public int ReturnCounter;
            public double LastAccessTime;
            public long TotalGets;
            public long TotalReturns;
            public long TotalHits;
            public long TotalCreated;
            public long TotalDestroyed;

            public PoolData(string assetKey, int retainedCapacity, double now)
            {
                AssetKey = assetKey;
                Retained = new Stack<RetainedInstance>(retainedCapacity);
                LastAccessTime = now;
            }
        }

        private const int ShrinkCheckInterval = 32;
        private const int MaxShrinkPerCheck = 4;
        private const int PrewarmBatchSize = 32;
        private const float RetentionBufferRatio = 1.5f;
        private const float RecentPeakDecayFactor = 0.6f;
        private const double MaintenanceIntervalSeconds = 5d;
        private const int MaxAssetReferenceLength = 1024;
        private const int MaxMetadataLength = 256;

        private IResourceLocator resourceLocator;
        private readonly PoolConfig config;
        private readonly Dictionary<string, PoolData> poolRegistry;
        private readonly Dictionary<int, PoolData> instanceOwners;
        private readonly Dictionary<int, ActiveLeaseRecord> activeLeases;
        private readonly List<string> poolRemovalScratch;
        private readonly List<int> leaseRemovalScratch;
        private readonly List<MonoBehaviour> lifecycleDiscoveryScratch;
        private readonly Transform poolRoot;
        private readonly CancellationTokenSource shutdownCancellation;
        private readonly CancellationToken shutdownToken;

        private double lastMaintenanceTime;
        private long nextLeaseGeneration;
        private int pendingLeaseRequestCount;
        private int retainedInstanceCount;
        private int reservedRetainedInstanceCount;
        private bool isShutdown;

        /// <summary>
        /// Resource provider used by cue implementations for non-GameObject companion assets.
        /// </summary>
        public IResourceLocator ResourceLocator
        {
            get
            {
                AssertMainThread();
                ThrowIfShutdown();
                return resourceLocator;
            }
        }

        public GameObjectPoolManager(IResourceLocator locator, PoolConfig poolConfig)
        {
            AssertMainThread();
            resourceLocator = locator ?? throw new ArgumentNullException(nameof(locator));
            poolConfig.Validate(nameof(poolConfig));
            config = poolConfig;

            poolRegistry = new Dictionary<string, PoolData>(Math.Min(poolConfig.MaxAssetPools, 64), StringComparer.Ordinal);
            instanceOwners = new Dictionary<int, PoolData>(Math.Min(poolConfig.MaxActiveLeases, 256));
            activeLeases = new Dictionary<int, ActiveLeaseRecord>(Math.Min(poolConfig.MaxActiveLeases, 256));
            poolRemovalScratch = new List<string>(Math.Min(poolConfig.MaxAssetPools, 16));
            leaseRemovalScratch = new List<int>(Math.Min(poolConfig.MaxActiveLeases, 16));
            lifecycleDiscoveryScratch = new List<MonoBehaviour>(16);

            shutdownCancellation = new CancellationTokenSource();
            shutdownToken = shutdownCancellation.Token;
            poolRoot = new GameObject("GameplayCuePool_Root").transform;
            if (Application.isPlaying)
            {
                UnityEngine.Object.DontDestroyOnLoad(poolRoot.gameObject);
            }
            lastMaintenanceTime = GetCurrentTime();
        }

        public async UniTask<GameObjectLease> GetAsync(
            string assetRef,
            Vector3 position,
            Quaternion rotation,
            Transform parent = null,
            string bucket = null,
            string cacheTag = null,
            string cacheOwner = null,
            CancellationToken cancellationToken = default)
        {
            AssertMainThread();
            ThrowIfShutdown();
            cancellationToken.ThrowIfCancellationRequested();
            ValidateAssetReference(assetRef);
            ValidateOptionalMetadata(bucket, nameof(bucket));
            ValidateOptionalMetadata(cacheTag, nameof(cacheTag));
            ValidateOptionalMetadata(cacheOwner, nameof(cacheOwner));
            TickMaintenance();

            PoolData poolData = GetOrCreatePoolData(assetRef);
            poolData.LastAccessTime = GetCurrentTime();
            ReserveLeaseRequest(poolData);
            try
            {
                while (poolData.Retained.Count > 0)
                {
                    RetainedInstance retained = poolData.Retained.Pop();
                    retainedInstanceCount--;
                    if (retained.Instance == null)
                    {
                        instanceOwners.Remove(retained.InstanceId);
                        poolData.TotalDestroyed++;
                        continue;
                    }

                    poolData.TotalHits++;
                    return ActivateLease(
                        poolData,
                        retained.InstanceId,
                        retained.Instance,
                        retained.InitialLocalScale,
                        retained.LifecycleHandlers,
                        position,
                        rotation,
                        parent);
                }

                IResourceHandle<GameObject> handle = await EnsureHandleAsync(
                    poolData,
                    bucket,
                    cacheTag,
                    cacheOwner,
                    cancellationToken);

                await UniTask.SwitchToMainThread(PlayerLoopTiming.Update, cancellationToken);
                ThrowIfShutdown();
                cancellationToken.ThrowIfCancellationRequested();

                if (handle == null || handle.Asset == null)
                {
                    return default;
                }

                GameObject instance = UnityEngine.Object.Instantiate(handle.Asset, position, rotation, parent);
                poolData.TotalCreated++;
                int instanceId = instance.GetInstanceID();
                Vector3 initialLocalScale = instance.transform.localScale;
                IGameObjectPoolLifecycle[] lifecycleHandlers;
                try
                {
                    lifecycleHandlers = DiscoverLifecycleHandlers(instance);
                }
                catch
                {
                    DestroyUnityObject(instance);
                    poolData.TotalDestroyed++;
                    throw;
                }
                instanceOwners.Add(instanceId, poolData);
                return ActivateLease(
                    poolData,
                    instanceId,
                    instance,
                    initialLocalScale,
                    lifecycleHandlers,
                    position,
                    rotation,
                    parent);
            }
            finally
            {
                await UniTask.SwitchToMainThread();
                ReleaseLeaseRequest(poolData);
            }
        }

        public void Release(GameObjectLease lease)
        {
            AssertMainThread();
            ThrowIfShutdown();

            if (!lease.IsValid)
            {
                throw new ArgumentException("The GameObject lease is invalid.", nameof(lease));
            }

            if (!ReferenceEquals(lease.OwnerIdentity, this))
            {
                throw new ArgumentException("The GameObject lease is owned by another pool manager.", nameof(lease));
            }

            int instanceId = lease.InstanceId;
            if (!instanceOwners.TryGetValue(instanceId, out PoolData owner))
            {
                throw new InvalidOperationException("The GameObject lease no longer has an owned instance.");
            }

            if (!activeLeases.TryGetValue(instanceId, out ActiveLeaseRecord activeLease))
            {
                throw new InvalidOperationException("The GameObject lease has already been released.");
            }

            if (activeLease.Generation != lease.Generation ||
                !ReferenceEquals(activeLease.Instance, lease.RawInstance))
            {
                throw new InvalidOperationException("The GameObject lease generation is stale.");
            }

            activeLeases.Remove(instanceId);
            owner.ActiveCount--;
            owner.TotalReturns++;
            owner.LastAccessTime = GetCurrentTime();

            GameObject instance = activeLease.Instance;
            if (instance == null)
            {
                instanceOwners.Remove(instanceId);
                owner.TotalDestroyed++;
                return;
            }

            if (owner.Retained.Count >= config.MaxRetainedInstancesPerPool ||
                retainedInstanceCount + reservedRetainedInstanceCount >= config.MaxTotalRetainedInstances)
            {
                DestroyOwnedInstance(owner, instanceId, instance);
                return;
            }

            try
            {
                InvokeReturnLifecycle(activeLease.LifecycleHandlers);
                ThrowIfShutdown();
                instance.SetActive(false);
                ThrowIfShutdown();
                instance.transform.SetParent(poolRoot, false);
                instance.transform.localScale = activeLease.InitialLocalScale;
                owner.Retained.Push(new RetainedInstance(
                    instanceId,
                    instance,
                    activeLease.InitialLocalScale,
                    activeLease.LifecycleHandlers));
                retainedInstanceCount++;
            }
            catch
            {
                DestroyOwnedInstance(owner, instanceId, instance);
                throw;
            }

            owner.ReturnCounter++;
            if (owner.ReturnCounter >= ShrinkCheckInterval)
            {
                owner.ReturnCounter = 0;
                PerformSmartShrink(owner);
            }
        }

        public bool IsLeaseOutstanding(GameObjectLease lease)
        {
            AssertMainThread();
            if (isShutdown || !lease.IsValid || !ReferenceEquals(lease.OwnerIdentity, this))
            {
                return false;
            }

            return activeLeases.TryGetValue(lease.InstanceId, out ActiveLeaseRecord activeLease) &&
                   activeLease.Instance != null &&
                   activeLease.Generation == lease.Generation &&
                   ReferenceEquals(activeLease.Instance, lease.RawInstance);
        }

        GameObject IGameObjectLeaseAuthority.ResolveOutstandingInstance(
            object ownerIdentity,
            int instanceId,
            long generation,
            GameObject rawInstance)
        {
            AssertMainThread();
            ThrowIfShutdown();

            if (!ReferenceEquals(ownerIdentity, this))
            {
                throw new InvalidOperationException("The GameObject lease authority is foreign.");
            }

            if (instanceId == 0 || generation <= 0 || rawInstance == null)
            {
                throw new InvalidOperationException("The GameObject lease no longer resolves to a live instance.");
            }

            if (!activeLeases.TryGetValue(instanceId, out ActiveLeaseRecord activeLease))
            {
                throw new InvalidOperationException("The GameObject lease has already been released.");
            }

            if (activeLease.Instance == null ||
                activeLease.Generation != generation ||
                !ReferenceEquals(activeLease.Instance, rawInstance))
            {
                throw new InvalidOperationException("The GameObject lease generation is stale.");
            }

            return activeLease.Instance;
        }

        public async UniTask PrewarmPoolAsync(
            string assetRef,
            int count,
            string bucket = null,
            string cacheTag = null,
            string cacheOwner = null,
            CancellationToken cancellationToken = default)
        {
            AssertMainThread();
            ThrowIfShutdown();
            cancellationToken.ThrowIfCancellationRequested();
            ValidateAssetReference(assetRef);
            ValidateOptionalMetadata(bucket, nameof(bucket));
            ValidateOptionalMetadata(cacheTag, nameof(cacheTag));
            ValidateOptionalMetadata(cacheOwner, nameof(cacheOwner));

            if (count < 0 || count > config.MaxRetainedInstancesPerPool)
            {
                throw new ArgumentOutOfRangeException(nameof(count), $"Prewarm count must be between zero and {config.MaxRetainedInstancesPerPool}.");
            }

            if (count == 0)
            {
                return;
            }

            TickMaintenance();
            PoolData poolData = GetOrCreatePoolData(assetRef);
            poolData.LastAccessTime = GetCurrentTime();

            if (poolData.Retained.Count >= count)
            {
                return;
            }

            if (poolData.PrewarmInProgress)
            {
                throw new InvalidOperationException($"A prewarm operation is already in progress for '{assetRef}'.");
            }

            int retainedReservation = Math.Max(0, count - poolData.Retained.Count);
            if (retainedReservation > config.MaxTotalRetainedInstances - retainedInstanceCount - reservedRetainedInstanceCount)
            {
                throw new InvalidOperationException(
                    $"Gameplay Cue global retained-instance capacity ({config.MaxTotalRetainedInstances}) is exhausted.");
            }
            poolData.PrewarmInProgress = true;
            reservedRetainedInstanceCount += retainedReservation;

            try
            {
                IResourceHandle<GameObject> handle = await EnsureHandleAsync(
                    poolData,
                    bucket,
                    cacheTag,
                    cacheOwner,
                    cancellationToken);

                await UniTask.SwitchToMainThread(PlayerLoopTiming.Update, cancellationToken);
                ThrowIfShutdown();
                if (handle == null || handle.Asset == null)
                {
                    return;
                }

                int createdInBatch = 0;
                while (poolData.Retained.Count < count)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    ThrowIfShutdown();
                    if (retainedReservation == 0)
                    {
                        if (retainedInstanceCount + reservedRetainedInstanceCount >= config.MaxTotalRetainedInstances)
                        {
                            throw new InvalidOperationException(
                                $"Gameplay Cue global retained-instance capacity ({config.MaxTotalRetainedInstances}) is exhausted.");
                        }
                        retainedReservation++;
                        reservedRetainedInstanceCount++;
                    }

                    GameObject instance = UnityEngine.Object.Instantiate(handle.Asset, poolRoot);
                    poolData.TotalCreated++;
                    try
                    {
                        int instanceId = instance.GetInstanceID();
                        Vector3 initialLocalScale = instance.transform.localScale;
                        IGameObjectPoolLifecycle[] lifecycleHandlers = DiscoverLifecycleHandlers(instance);
                        InvokeReturnLifecycle(lifecycleHandlers);
                        ThrowIfShutdown();
                        instance.SetActive(false);
                        ThrowIfShutdown();
                        instanceOwners.Add(instanceId, poolData);
                        poolData.Retained.Push(new RetainedInstance(
                            instanceId,
                            instance,
                            initialLocalScale,
                            lifecycleHandlers));
                        retainedInstanceCount++;
                        retainedReservation--;
                        reservedRetainedInstanceCount--;
                    }
                    catch
                    {
                        if (instance != null)
                        {
                            instanceOwners.Remove(instance.GetInstanceID());
                            DestroyUnityObject(instance);
                            poolData.TotalDestroyed++;
                        }
                        throw;
                    }

                    createdInBatch++;
                    if (createdInBatch >= PrewarmBatchSize && poolData.Retained.Count < count)
                    {
                        createdInBatch = 0;
                        await UniTask.Yield(PlayerLoopTiming.Update, cancellationToken);
                        ThrowIfShutdown();
                    }
                }
            }
            finally
            {
                await UniTask.SwitchToMainThread();
                poolData.PrewarmInProgress = false;
                if (!isShutdown)
                {
                    reservedRetainedInstanceCount = Math.Max(
                        0,
                        reservedRetainedInstanceCount - retainedReservation);
                }
            }
        }

        /// <summary>
        /// Shrinks every retained pool to the configured minimum. Active leases remain valid.
        /// </summary>
        public void AggressiveShrink()
        {
            AssertMainThread();
            ThrowIfShutdown();

            foreach (PoolData poolData in poolRegistry.Values)
            {
                if (poolData.PrewarmInProgress)
                {
                    continue;
                }

                ShrinkRetained(poolData, config.MinRetainedInstancesPerPool);
                poolData.RecentPeakActive = poolData.ActiveCount;
                ReleaseIdleHandleIfEmpty(poolData);
            }
        }

        public void AggressiveShrink(string assetKey)
        {
            AssertMainThread();
            ThrowIfShutdown();
            ValidateAssetReference(assetKey);

            if (!poolRegistry.TryGetValue(assetKey, out PoolData poolData))
            {
                return;
            }

            if (poolData.PrewarmInProgress)
            {
                throw new InvalidOperationException($"Cannot shrink '{assetKey}' while its prewarm operation is in progress.");
            }

            ShrinkRetained(poolData, config.MinRetainedInstancesPerPool);
            poolData.RecentPeakActive = poolData.ActiveCount;
            ReleaseIdleHandleIfEmpty(poolData);
        }

        /// <summary>
        /// Destroys retained objects for one asset. Active leases remain owned and valid.
        /// </summary>
        public void ClearPool(string assetKey)
        {
            AssertMainThread();
            ThrowIfShutdown();
            ValidateAssetReference(assetKey);

            if (!poolRegistry.TryGetValue(assetKey, out PoolData poolData))
            {
                return;
            }

            if (poolData.PrewarmInProgress)
            {
                throw new InvalidOperationException($"Cannot clear '{assetKey}' while its prewarm operation is in progress.");
            }

            DestroyRetained(poolData);
            ReleaseIdleHandleIfEmpty(poolData);
        }

        public PoolStatistics GetStatistics(string assetKey)
        {
            AssertMainThread();
            ThrowIfShutdown();
            ValidateAssetReference(assetKey);

            return poolRegistry.TryGetValue(assetKey, out PoolData poolData)
                ? CreateStatistics(poolData)
                : new PoolStatistics(assetKey, 0, 0, 0, 0, 0, 0, 0, 0, 0);
        }

        public List<PoolStatistics> GetAllStatistics()
        {
            AssertMainThread();
            ThrowIfShutdown();

            var result = new List<PoolStatistics>(poolRegistry.Count);
            foreach (PoolData poolData in poolRegistry.Values)
            {
                result.Add(CreateStatistics(poolData));
            }

            return result;
        }

        [System.Diagnostics.Conditional("UNITY_EDITOR"), System.Diagnostics.Conditional("DEVELOPMENT_BUILD")]
        public void LogStatistics()
        {
            AssertMainThread();
            ThrowIfShutdown();

            List<PoolStatistics> statistics = GetAllStatistics();
            var builder = new System.Text.StringBuilder();
            builder.AppendLine("=== Gameplay Cue GameObject Pool Statistics ===");

            for (int i = 0; i < statistics.Count; i++)
            {
                PoolStatistics entry = statistics[i];
                builder.AppendLine($"[{entry.AssetKey}] Retained:{entry.CurrentPoolSize} Active:{entry.ActiveCount} Pending:{entry.PendingCount} Peak:{entry.PeakActive} HitRate:{entry.HitRate:P1} Gets:{entry.TotalGets} Created:{entry.TotalCreated}");
            }

            GASLog.Info(builder.ToString());
        }

        public void ResetStatistics()
        {
            AssertMainThread();
            ThrowIfShutdown();

            foreach (PoolData poolData in poolRegistry.Values)
            {
                poolData.TotalGets = 0;
                poolData.TotalReturns = 0;
                poolData.TotalHits = 0;
                poolData.TotalCreated = 0;
                poolData.TotalDestroyed = 0;
                poolData.PeakActive = poolData.ActiveCount;
                poolData.RecentPeakActive = poolData.ActiveCount;
            }
        }

        public void Shutdown()
        {
            AssertMainThread();
            if (isShutdown)
            {
                return;
            }

            isShutdown = true;
            Exception cleanupFailure = null;
            try { shutdownCancellation.Cancel(); }
            catch (Exception exception) { cleanupFailure = exception; }

            foreach (ActiveLeaseRecord activeLease in activeLeases.Values)
            {
                if (activeLease.Instance != null)
                {
                    try { InvokeReturnLifecycle(activeLease.LifecycleHandlers); }
                    catch (Exception exception) { cleanupFailure ??= exception; }
                    try
                    {
                        DestroyUnityObject(activeLease.Instance);
                        activeLease.Owner.TotalDestroyed++;
                    }
                    catch (Exception exception) { cleanupFailure ??= exception; }
                }
            }

            activeLeases.Clear();

            foreach (PoolData poolData in poolRegistry.Values)
            {
                while (poolData.Retained.Count > 0)
                {
                    try { DestroyRetainedInstance(poolData, poolData.Retained.Pop()); }
                    catch (Exception exception) { cleanupFailure ??= exception; }
                }
                poolData.ActiveCount = 0;
                try { poolData.Handle?.Dispose(); }
                catch (Exception exception) { cleanupFailure ??= exception; }
                finally { poolData.Handle = null; }
            }

            poolRegistry.Clear();
            instanceOwners.Clear();
            poolRemovalScratch.Clear();
            leaseRemovalScratch.Clear();
            lifecycleDiscoveryScratch.Clear();
            pendingLeaseRequestCount = 0;
            retainedInstanceCount = 0;
            reservedRetainedInstanceCount = 0;

            if (poolRoot != null)
            {
                try { DestroyUnityObject(poolRoot.gameObject); }
                catch (Exception exception) { cleanupFailure ??= exception; }
            }

            try { shutdownCancellation.Dispose(); }
            catch (Exception exception) { cleanupFailure ??= exception; }
            resourceLocator = null;
            if (cleanupFailure != null)
            {
                GASLog.Error($"GameObjectPoolManager shutdown completed with cleanup failures: {cleanupFailure.Message}");
            }
        }

        public void Dispose()
        {
            Shutdown();
        }

        private GameObjectLease ActivateLease(
            PoolData poolData,
            int instanceId,
            GameObject instance,
            Vector3 initialLocalScale,
            IGameObjectPoolLifecycle[] lifecycleHandlers,
            Vector3 position,
            Quaternion rotation,
            Transform parent)
        {
            try
            {
                if (!instanceOwners.TryGetValue(instanceId, out PoolData owner) || !ReferenceEquals(owner, poolData))
                {
                    throw new InvalidOperationException("Gameplay Cue pool ownership tracking is inconsistent.");
                }

                instance.transform.SetParent(parent, false);
                instance.transform.SetPositionAndRotation(position, rotation);
                instance.transform.localScale = initialLocalScale;
                instance.SetActive(true);
                InvokeRentLifecycle(lifecycleHandlers);
                ThrowIfShutdown();

                long generation = GetNextLeaseGeneration();
                activeLeases.Add(instanceId, new ActiveLeaseRecord(
                    instance,
                    poolData,
                    generation,
                    initialLocalScale,
                    lifecycleHandlers));
                poolData.ActiveCount++;
                poolData.TotalGets++;
                poolData.PeakActive = Math.Max(poolData.PeakActive, poolData.ActiveCount);
                poolData.RecentPeakActive = Math.Max(poolData.RecentPeakActive, poolData.ActiveCount);
                return new GameObjectLease(this, instanceId, instance, generation);
            }
            catch
            {
                if (!activeLeases.ContainsKey(instanceId))
                {
                    DestroyOwnedInstance(poolData, instanceId, instance);
                }

                throw;
            }
        }

        private async UniTask<IResourceHandle<GameObject>> EnsureHandleAsync(
            PoolData poolData,
            string bucket,
            string cacheTag,
            string cacheOwner,
            CancellationToken cancellationToken)
        {
            if (poolData.Handle != null)
            {
                return poolData.Handle;
            }

            UniTaskCompletionSource<IResourceHandle<GameObject>> completion = poolData.PendingLoad;
            if (completion == null)
            {
                completion = new UniTaskCompletionSource<IResourceHandle<GameObject>>();
                poolData.PendingLoad = completion;
                var loadCancellation = CancellationTokenSource.CreateLinkedTokenSource(shutdownToken);
                poolData.PendingLoadCancellation = loadCancellation;
                LoadHandleAsync(poolData, completion, loadCancellation, bucket, cacheTag, cacheOwner).Forget();
            }

            poolData.PendingLoadWaiterCount++;
            try
            {
                IResourceHandle<GameObject> handle = cancellationToken.CanBeCanceled
                    ? await completion.Task.AttachExternalCancellation(cancellationToken)
                    : await completion.Task;

                await UniTask.SwitchToMainThread(PlayerLoopTiming.Update, cancellationToken);
                ThrowIfShutdown();
                return handle;
            }
            finally
            {
                await UniTask.SwitchToMainThread();
                if (poolData.PendingLoadWaiterCount > 0)
                {
                    poolData.PendingLoadWaiterCount--;
                }

                if (cancellationToken.IsCancellationRequested &&
                    poolData.PendingLoadWaiterCount == 0 &&
                    ReferenceEquals(poolData.PendingLoad, completion))
                {
                    poolData.PendingLoadCancellation?.Cancel();
                }
            }
        }

        private async UniTaskVoid LoadHandleAsync(
            PoolData poolData,
            UniTaskCompletionSource<IResourceHandle<GameObject>> completion,
            CancellationTokenSource loadCancellation,
            string bucket,
            string cacheTag,
            string cacheOwner)
        {
            IResourceHandle<GameObject> loadedHandle = null;
            try
            {
                loadedHandle = await resourceLocator.LoadAssetAsync<GameObject>(
                    poolData.AssetKey,
                    bucket,
                    cacheTag,
                    cacheOwner,
                    loadCancellation.Token);

                await UniTask.SwitchToMainThread();
                if (isShutdown || shutdownToken.IsCancellationRequested)
                {
                    Exception cleanupFailure = null;
                    DisposeResourceHandleSafely(loadedHandle, ref cleanupFailure);
                    loadedHandle = null;
                    poolData.PendingLoad = null;
                    completion.TrySetCanceled(shutdownToken);
                    if (cleanupFailure != null)
                    {
                        GASLog.Error($"Gameplay Cue resource cleanup failed after shutdown cancellation: {cleanupFailure.Message}");
                    }
                    return;
                }

                if (loadedHandle == null || loadedHandle.Asset == null)
                {
                    Exception cleanupFailure = null;
                    DisposeResourceHandleSafely(loadedHandle, ref cleanupFailure);
                    loadedHandle = null;
                    if (cleanupFailure != null)
                    {
                        poolData.PendingLoad = null;
                        SetExceptionAndMarkObserved(completion, cleanupFailure);
                        return;
                    }
                }

                poolData.Handle = loadedHandle;
                poolData.PendingLoad = null;
                completion.TrySetResult(loadedHandle);
            }
            catch (OperationCanceledException cancellation)
            {
                await UniTask.SwitchToMainThread();
                Exception cleanupFailure = null;
                DisposeResourceHandleSafely(loadedHandle, ref cleanupFailure);
                loadedHandle = null;
                poolData.PendingLoad = null;
                completion.TrySetCanceled(cancellation.CancellationToken);
                if (cleanupFailure != null)
                {
                    GASLog.Error($"Gameplay Cue resource cleanup failed after load cancellation: {cleanupFailure.Message}");
                }
            }
            catch (Exception exception)
            {
                await UniTask.SwitchToMainThread();
                Exception cleanupFailure = exception;
                DisposeResourceHandleSafely(loadedHandle, ref cleanupFailure);
                loadedHandle = null;
                poolData.PendingLoad = null;
                SetExceptionAndMarkObserved(completion, cleanupFailure);
            }
            finally
            {
                await UniTask.SwitchToMainThread();
                if (ReferenceEquals(poolData.PendingLoad, completion))
                {
                    poolData.PendingLoad = null;
                }
                if (ReferenceEquals(poolData.PendingLoadCancellation, loadCancellation))
                {
                    poolData.PendingLoadCancellation = null;
                }
                loadCancellation.Dispose();
            }
        }

        private static void DisposeResourceHandleSafely<T>(
            IResourceHandle<T> handle,
            ref Exception cleanupFailure)
            where T : UnityEngine.Object
        {
            if (handle == null)
            {
                return;
            }

            try
            {
                handle.Dispose();
            }
            catch (Exception exception)
            {
                cleanupFailure ??= exception;
            }
        }

        private PoolData GetOrCreatePoolData(string assetKey)
        {
            if (poolRegistry.TryGetValue(assetKey, out PoolData existing))
            {
                return existing;
            }

            if (poolRegistry.Count >= config.MaxAssetPools && !TryEvictOldestIdlePool())
            {
                throw new InvalidOperationException($"Gameplay Cue asset pool capacity ({config.MaxAssetPools}) is exhausted.");
            }

            var poolData = new PoolData(
                assetKey,
                Math.Min(config.MinRetainedInstancesPerPool, 64),
                GetCurrentTime());
            poolRegistry.Add(assetKey, poolData);
            return poolData;
        }

        private bool TryEvictOldestIdlePool()
        {
            PoolData candidate = null;
            foreach (PoolData poolData in poolRegistry.Values)
            {
                if (poolData.ActiveCount != 0 ||
                    poolData.PendingLeaseRequestCount != 0 ||
                    (poolData.PendingLoad != null &&
                     !(poolData.PendingLoadCancellation?.IsCancellationRequested ?? false)) ||
                    poolData.PrewarmInProgress)
                {
                    continue;
                }

                if (candidate == null || poolData.LastAccessTime < candidate.LastAccessTime)
                {
                    candidate = poolData;
                }
            }

            if (candidate == null)
            {
                return false;
            }

            DestroyPoolData(candidate);
            poolRegistry.Remove(candidate.AssetKey);
            return true;
        }

        private void ReserveLeaseRequest(PoolData poolData)
        {
            if (activeLeases.Count + pendingLeaseRequestCount >= config.MaxActiveLeases)
            {
                throw new InvalidOperationException($"Gameplay Cue active or pending lease capacity ({config.MaxActiveLeases}) is exhausted.");
            }

            if (poolData.ActiveCount + poolData.PendingLeaseRequestCount >= config.MaxActiveLeasesPerPool)
            {
                throw new InvalidOperationException($"Gameplay Cue active or pending lease capacity for '{poolData.AssetKey}' ({config.MaxActiveLeasesPerPool}) is exhausted.");
            }

            pendingLeaseRequestCount++;
            poolData.PendingLeaseRequestCount++;
        }

        private void ReleaseLeaseRequest(PoolData poolData)
        {
            if (pendingLeaseRequestCount > 0) pendingLeaseRequestCount--;
            if (poolData != null && poolData.PendingLeaseRequestCount > 0) poolData.PendingLeaseRequestCount--;
        }

        private void TickMaintenance()
        {
            double now = GetCurrentTime();
            if (now - lastMaintenanceTime < MaintenanceIntervalSeconds)
            {
                return;
            }

            lastMaintenanceTime = now;
            PurgeDestroyedActiveLeases();

            if (config.IdleExpirationTime <= 0f)
            {
                return;
            }

            poolRemovalScratch.Clear();
            foreach (PoolData poolData in poolRegistry.Values)
            {
                if (poolData.ActiveCount == 0 &&
                    poolData.PendingLeaseRequestCount == 0 &&
                    poolData.PendingLoad == null &&
                    !poolData.PrewarmInProgress &&
                    now - poolData.LastAccessTime >= config.IdleExpirationTime)
                {
                    poolRemovalScratch.Add(poolData.AssetKey);
                }
            }

            for (int i = 0; i < poolRemovalScratch.Count; i++)
            {
                string assetKey = poolRemovalScratch[i];
                if (poolRegistry.TryGetValue(assetKey, out PoolData poolData))
                {
                    DestroyPoolData(poolData);
                    poolRegistry.Remove(assetKey);
                }
            }
        }

        private void PurgeDestroyedActiveLeases()
        {
            leaseRemovalScratch.Clear();
            foreach (KeyValuePair<int, ActiveLeaseRecord> pair in activeLeases)
            {
                if (pair.Value.Instance == null)
                {
                    leaseRemovalScratch.Add(pair.Key);
                }
            }

            for (int i = 0; i < leaseRemovalScratch.Count; i++)
            {
                int instanceId = leaseRemovalScratch[i];
                if (!activeLeases.TryGetValue(instanceId, out ActiveLeaseRecord lease))
                {
                    continue;
                }

                activeLeases.Remove(instanceId);
                instanceOwners.Remove(instanceId);
                lease.Owner.ActiveCount--;
                lease.Owner.TotalDestroyed++;
            }
        }

        private void PerformSmartShrink(PoolData poolData)
        {
            int targetCapacity = Math.Max(
                config.MinRetainedInstancesPerPool,
                (int)Math.Ceiling(poolData.RecentPeakActive * RetentionBufferRatio));
            int currentTotal = poolData.ActiveCount + poolData.Retained.Count;
            int excess = Math.Max(0, currentTotal - targetCapacity);
            int removable = Math.Max(0, poolData.Retained.Count - config.MinRetainedInstancesPerPool);
            int removeCount = Math.Min(Math.Min(excess, removable), MaxShrinkPerCheck);

            for (int i = 0; i < removeCount; i++)
            {
                DestroyRetainedInstance(poolData, poolData.Retained.Pop());
            }

            poolData.RecentPeakActive = Math.Max(
                poolData.ActiveCount,
                (int)(poolData.RecentPeakActive * RecentPeakDecayFactor));
        }

        private void ShrinkRetained(PoolData poolData, int targetCount)
        {
            while (poolData.Retained.Count > targetCount)
            {
                DestroyRetainedInstance(poolData, poolData.Retained.Pop());
            }
        }

        private void DestroyRetained(PoolData poolData)
        {
            while (poolData.Retained.Count > 0)
            {
                DestroyRetainedInstance(poolData, poolData.Retained.Pop());
            }
        }

        private void DestroyRetainedInstance(PoolData poolData, RetainedInstance retained)
        {
            if (retainedInstanceCount > 0) retainedInstanceCount--;
            instanceOwners.Remove(retained.InstanceId);
            if (retained.Instance != null)
            {
                DestroyUnityObject(retained.Instance);
            }

            poolData.TotalDestroyed++;
        }

        private void DestroyOwnedInstance(PoolData poolData, int instanceId, GameObject instance)
        {
            instanceOwners.Remove(instanceId);
            if (instance != null)
            {
                DestroyUnityObject(instance);
            }

            poolData.TotalDestroyed++;
        }

        private void DestroyPoolData(PoolData poolData)
        {
            CancellationTokenSource pendingLoadCancellation = poolData.PendingLoadCancellation;
            poolData.PendingLoadCancellation = null;
            pendingLoadCancellation?.Cancel();
            pendingLoadCancellation?.Dispose();
            DestroyRetained(poolData);
            try { poolData.Handle?.Dispose(); }
            finally { poolData.Handle = null; }
        }

        private void ReleaseIdleHandleIfEmpty(PoolData poolData)
        {
            if (poolData.ActiveCount == 0 &&
                poolData.PendingLeaseRequestCount == 0 &&
                poolData.Retained.Count == 0 &&
                poolData.PendingLoad == null &&
                !poolData.PrewarmInProgress)
            {
                try { poolData.Handle?.Dispose(); }
                finally { poolData.Handle = null; }
            }
        }

        private static PoolStatistics CreateStatistics(PoolData poolData)
        {
            return new PoolStatistics(
                poolData.AssetKey,
                poolData.Retained.Count,
                poolData.ActiveCount,
                poolData.PendingLeaseRequestCount,
                poolData.PeakActive,
                poolData.TotalGets,
                poolData.TotalReturns,
                poolData.TotalHits,
                poolData.TotalCreated,
                poolData.TotalDestroyed);
        }

        private long GetNextLeaseGeneration()
        {
            if (nextLeaseGeneration == long.MaxValue)
            {
                throw new InvalidOperationException("Gameplay Cue lease generation space is exhausted.");
            }

            nextLeaseGeneration++;
            return nextLeaseGeneration;
        }

        private static void SetExceptionAndMarkObserved(
            UniTaskCompletionSource<IResourceHandle<GameObject>> completion,
            Exception exception)
        {
            if (!completion.TrySetException(exception))
            {
                return;
            }

            try
            {
                completion.GetResult(0);
            }
            catch
            {
                // The shared completion remains faulted for every awaiting caller. This read only prevents
                // an abandoned caller view from publishing the same exception from a finalizer.
            }
        }

        private IGameObjectPoolLifecycle[] DiscoverLifecycleHandlers(GameObject instance)
        {
            lifecycleDiscoveryScratch.Clear();
            instance.GetComponentsInChildren(true, lifecycleDiscoveryScratch);

            int handlerCount = 0;
            for (int i = 0; i < lifecycleDiscoveryScratch.Count; i++)
            {
                if (lifecycleDiscoveryScratch[i] is IGameObjectPoolLifecycle)
                {
                    handlerCount++;
                }
            }

            if (handlerCount == 0)
            {
                lifecycleDiscoveryScratch.Clear();
                return Array.Empty<IGameObjectPoolLifecycle>();
            }

            var handlers = new IGameObjectPoolLifecycle[handlerCount];
            int destination = 0;
            for (int i = 0; i < lifecycleDiscoveryScratch.Count; i++)
            {
                if (lifecycleDiscoveryScratch[i] is IGameObjectPoolLifecycle handler)
                {
                    handlers[destination++] = handler;
                }
            }

            lifecycleDiscoveryScratch.Clear();
            return handlers;
        }

        private static void InvokeRentLifecycle(IGameObjectPoolLifecycle[] handlers)
        {
            if (handlers == null) return;

            for (int i = 0; i < handlers.Length; i++)
            {
                try
                {
                    handlers[i]?.OnRentFromPool();
                }
                catch
                {
                    for (int rollbackIndex = i - 1; rollbackIndex >= 0; rollbackIndex--)
                    {
                        try { handlers[rollbackIndex]?.OnReturnToPool(); }
                        catch (Exception cleanupException)
                        {
                            GASLog.Error($"Pooled GameObject rent rollback failed: {cleanupException.Message}");
                        }
                    }
                    throw;
                }
            }
        }

        private static void InvokeReturnLifecycle(IGameObjectPoolLifecycle[] handlers)
        {
            if (handlers == null) return;

            Exception firstFailure = null;
            for (int i = handlers.Length - 1; i >= 0; i--)
            {
                try { handlers[i]?.OnReturnToPool(); }
                catch (Exception exception) { firstFailure ??= exception; }
            }

            if (firstFailure != null)
            {
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(firstFailure).Throw();
            }
        }

        private static void DestroyUnityObject(GameObject instance)
        {
            if (Application.isPlaying)
            {
                UnityEngine.Object.Destroy(instance);
            }
            else
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }
        }

        private void ThrowIfShutdown()
        {
            if (isShutdown)
            {
                throw new ObjectDisposedException(nameof(GameObjectPoolManager));
            }
        }

        private static void ValidateAssetReference(string assetRef)
        {
            if (string.IsNullOrWhiteSpace(assetRef))
            {
                throw new ArgumentException("Asset reference must be a non-empty string.", nameof(assetRef));
            }

            if (assetRef.Length > MaxAssetReferenceLength)
            {
                throw new ArgumentOutOfRangeException(nameof(assetRef), assetRef.Length, $"Asset references cannot exceed {MaxAssetReferenceLength} characters.");
            }
        }

        private static void ValidateOptionalMetadata(string value, string parameterName)
        {
            if (value != null && value.Length > MaxMetadataLength)
            {
                throw new ArgumentOutOfRangeException(parameterName, value.Length, $"Resource metadata cannot exceed {MaxMetadataLength} characters.");
            }
        }

        private static void AssertMainThread()
        {
            if (!PlayerLoopHelper.IsMainThread)
            {
                throw new InvalidOperationException("GameObjectPoolManager must be accessed from the Unity main thread.");
            }
        }

        private static double GetCurrentTime()
        {
            return Time.realtimeSinceStartupAsDouble;
        }
    }
}
