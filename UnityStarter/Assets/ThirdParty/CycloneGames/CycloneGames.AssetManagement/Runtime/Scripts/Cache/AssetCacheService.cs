using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;

using UnityEngine;

using Cysharp.Threading.Tasks;

namespace CycloneGames.AssetManagement.Runtime.Cache
{
    internal readonly struct AssetCacheKey : IEquatable<AssetCacheKey>
    {
        public readonly string Location;
        public readonly Type AssetType;
        public readonly AssetCacheEntryKind Kind;

        private readonly int _hashCode;

        public AssetCacheKey(string location, Type assetType, AssetCacheEntryKind kind)
        {
            Location = location;
            AssetType = assetType;
            Kind = kind;

            unchecked
            {
                int hash = StringComparer.Ordinal.GetHashCode(location ?? string.Empty);
                hash = (hash * 397) ^ (assetType?.GetHashCode() ?? 0);
                _hashCode = (hash * 397) ^ (int)kind;
            }
        }

        public bool IsValid => !string.IsNullOrEmpty(Location);

        public bool Equals(AssetCacheKey other)
        {
            return Kind == other.Kind &&
                   ReferenceEquals(AssetType, other.AssetType) &&
                   string.Equals(Location, other.Location, StringComparison.Ordinal);
        }

        public override bool Equals(object obj)
        {
            return obj is AssetCacheKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            return _hashCode;
        }

        public string ToDiagnosticString()
        {
            return string.Concat(
                ((byte)Kind).ToString(),
                "|",
                Location,
                "|",
                AssetType?.FullName ?? string.Empty);
        }
    }

    internal interface IAssetCacheClock
    {
        long Timestamp { get; }
        TimeSpan GetElapsed(long startTimestamp, long endTimestamp);
    }

    internal sealed class StopwatchAssetCacheClock : IAssetCacheClock
    {
        public static readonly StopwatchAssetCacheClock Instance = new StopwatchAssetCacheClock();

        private StopwatchAssetCacheClock()
        {
        }

        public long Timestamp => Stopwatch.GetTimestamp();

        public TimeSpan GetElapsed(long startTimestamp, long endTimestamp)
        {
            long delta = endTimestamp - startTimestamp;
            if (delta <= 0L)
            {
                return TimeSpan.Zero;
            }

            return TimeSpan.FromSeconds(delta / (double)Stopwatch.Frequency);
        }
    }

    /// <summary>
    /// Bounded segmented-LRU asset cache:
    /// - Active: handles with RefCount > 0 are pinned and never evicted.
    /// - Probation: zero-refcount handles on first idle entry; guards the protected segment from scan bursts.
    /// - Protected: handles promoted after reuse; eviction is recency-based.
    /// - Bucket: logical lifetime groups for deterministic mass-eviction (e.g. per-scene).
    /// </summary>
    internal sealed class AssetCacheService : IDisposable
    {
        private const int MAX_METADATA_VALUES_PER_KIND = 8;
        private const int MAX_IDLE_ENTRIES_PER_SEGMENT = 131_072;
        private const long MAX_ESTIMATED_ENTRY_BYTES = 1L * 1024 * 1024 * 1024 * 1024;

        private enum EvictionReason : byte
        {
            Capacity,
            MemoryBudget,
            Retention,
            Explicit
        }

        private sealed class CacheNode
        {
#if UNITY_EDITOR
            public long DiagnosticId;
            public string DiagnosticKey;
            public string DiagnosticAssetType;
            public string DiagnosticProviderType;
#endif
            public AssetCacheKey Key;
            public string Bucket;
            public string Tag;
            public string Owner;
            public List<string> AdditionalBuckets;
            public List<string> AdditionalTags;
            public List<string> AdditionalOwners;
            public bool MetadataOverflow;
            public IReferenceCounted Handle;
            public int AccessCount;
            // Approximate runtime footprint in bytes, computed when the node enters the idle pool.
            public long EstimatedBytes;
            // Monotonic timestamp captured when the node enters the idle pool (RefCount == 0). 0 while active.
            public long IdleSinceTimestamp;

            public CacheNode Next;
            public CacheNode Prev;

            public bool IsInMainPool;
        }

        private sealed class HandleReferenceComparer : IEqualityComparer<IReferenceCounted>
        {
            public static readonly HandleReferenceComparer Instance = new HandleReferenceComparer();

            private HandleReferenceComparer()
            {
            }

            public bool Equals(IReferenceCounted x, IReferenceCounted y)
            {
                return ReferenceEquals(x, y);
            }

            public int GetHashCode(IReferenceCounted obj)
            {
                return RuntimeHelpers.GetHashCode(obj);
            }
        }

        private static class NodePool
        {
            private const int MAX_POOL_SIZE = 512;
            private static readonly Stack<CacheNode> _pool = new Stack<CacheNode>(128);
            private static readonly object _lock = new object();

            public static CacheNode Get()
            {
                CacheNode node;
                lock (_lock)
                {
                    node = _pool.Count > 0 ? _pool.Pop() : new CacheNode();
                }

#if UNITY_EDITOR
                node.DiagnosticId = Interlocked.Increment(ref _nextDiagnosticId);
#endif
                return node;
            }

            public static void Release(CacheNode node)
            {
#if UNITY_EDITOR
                node.DiagnosticKey = null;
                node.DiagnosticAssetType = null;
                node.DiagnosticProviderType = null;
#endif
                node.Key = default;
                node.Bucket = null;
                node.Tag = null;
                node.Owner = null;
                node.AdditionalBuckets?.Clear();
                node.AdditionalTags?.Clear();
                node.AdditionalOwners?.Clear();
                node.MetadataOverflow = false;
                node.Handle = null;
                node.Next = null;
                node.Prev = null;
                node.AccessCount = 0;
                node.EstimatedBytes = 0;
                node.IdleSinceTimestamp = 0;
                node.IsInMainPool = false;

                lock (_lock)
                {
                    if (_pool.Count < MAX_POOL_SIZE) _pool.Push(node);
                }
            }
        }

        private readonly IAssetCacheClock _clock;

        private int _maxTrialEntries;
        private int _maxMainEntries;
        private bool _clearIdleOnLowMemory;

        // Handles currently held by at least one consumer (RefCount > 0).
        private readonly Dictionary<AssetCacheKey, CacheNode> _activeMap;
        // Handles detached from keyed lookup by a provider generation change. They remain caller-owned and
        // must stay enumerable until their final release or package shutdown.
        private readonly Dictionary<IReferenceCounted, CacheNode> _generationDetachedMap;
        // Handles idle (RefCount == 0), partitioned into trial and main LRU lists.
        private readonly Dictionary<AssetCacheKey, CacheNode> _idleMap;
        // Reverse index: bucket name to CacheNodes in that bucket (idle only).
        // Enables O(1) bucket lookup in ClearBucket instead of O(N) linked-list scan.
        private readonly Dictionary<string, HashSet<CacheNode>> _bucketIndex;
        private readonly List<CacheNode> _nodesToClearScratch;
        private readonly HashSet<CacheNode> _nodesToClearSetScratch;
        private readonly List<string> _matchedBucketsScratch;

        private CacheNode _trialHead;
        private CacheNode _trialTail;
        private int _trialCount;

        private CacheNode _mainHead;
        private CacheNode _mainTail;
        private int _mainCount;

        // Memory-budget eviction: idle (RefCount == 0) handles are evicted not only by entry count
        // but also when their aggregate estimated footprint exceeds this budget. This prevents a few
        // large assets (e.g. 4K textures) from silently blowing the memory ceiling on low-end devices.
        private long _idleBytes;
        private long _maxIdleBytes;

        // Lifetime activity counters. All writes happen while _gate is held. Snapshot reads copy the same coherent
        // state without enumerating entries or allocating. These are cumulative for the lifetime of this cache.
        private long _activeHitCount;
        private long _idleHitCount;
        private long _cacheMissCount;
        private long _idleAdmissionCount;
        private long _failedOperationRejectionCount;
        private long _metadataOverflowRejectionCount;
        private long _unknownFootprintRejectionCount;
        private long _oversizeRejectionCount;
        private long _footprintEstimationFailureCount;
        private long _evictionCount;
        private long _capacityEvictionCount;
        private long _memoryBudgetEvictionCount;
        private long _retentionEvictionCount;
        private long _explicitEvictionCount;
        private long _evictedBytesApprox;
        private long _providerReleaseFailureCount;
        private int _peakActiveCount;
        private int _peakIdleCount;
        private long _peakIdleBytesApprox;

        // Mutations are main-thread-affine because eviction releases Unity/provider resources. The monitor only
        // protects cold diagnostic snapshots and does not make provider operations safe on worker threads.
        private readonly object _gate = new object();
        private int _disposed;
        private int _evaluatingRetentionRules;

        /// <summary>
        /// Creates a cache service. Pass 0 for any sizing argument to auto-size based on device memory and platform.
        /// </summary>
        public AssetCacheService(IAssetPackage package, int maxTrialEntries = 0, int maxMainEntries = 0, long maxIdleBytes = 0)
            : this(package, maxTrialEntries, maxMainEntries, maxIdleBytes, StopwatchAssetCacheClock.Instance)
        {
        }

        internal AssetCacheService(IAssetPackage package, int maxTrialEntries, int maxMainEntries, long maxIdleBytes, IAssetCacheClock clock)
        {
            if (package == null)
            {
                throw new ArgumentNullException(nameof(package));
            }

            _clock = clock ?? throw new ArgumentNullException(nameof(clock));

            ValidateEntryLimit(maxTrialEntries, nameof(maxTrialEntries));
            ValidateEntryLimit(maxMainEntries, nameof(maxMainEntries));
            if (maxIdleBytes < 0L)
            {
                throw new ArgumentOutOfRangeException(nameof(maxIdleBytes));
            }

            if (maxTrialEntries <= 0 || maxMainEntries <= 0)
            {
                AssetCacheTuning tuning = AssetPlatformDefaults.CacheTuning;
                if (maxTrialEntries <= 0) maxTrialEntries = tuning.ProbationEntryLimit;
                if (maxMainEntries <= 0) maxMainEntries = tuning.ProtectedEntryLimit;
            }

            _maxTrialEntries = Math.Max(1, maxTrialEntries);
            _maxMainEntries = Math.Max(1, maxMainEntries);
            _maxIdleBytes = ResolveIdleBudget(maxIdleBytes);
            _clearIdleOnLowMemory = true;

            _activeMap = new Dictionary<AssetCacheKey, CacheNode>(128);
            _generationDetachedMap = new Dictionary<IReferenceCounted, CacheNode>(
                16,
                HandleReferenceComparer.Instance);
            int idleWarmCapacity = (int)Math.Min(
                1_024L,
                (long)_maxTrialEntries + _maxMainEntries);
            _idleMap = new Dictionary<AssetCacheKey, CacheNode>(idleWarmCapacity);
            _bucketIndex = new Dictionary<string, HashSet<CacheNode>>(16, StringComparer.Ordinal);
            _nodesToClearScratch = new List<CacheNode>(16);
            _nodesToClearSetScratch = new HashSet<CacheNode>();
            _matchedBucketsScratch = new List<string>(8);

            // On memory pressure (iOS/Android), drain the entire idle pool immediately. Active handles
            // (RefCount > 0) are never touched, so in-use assets remain valid. Editor uses a weak global
            // subscription so disabled domain reload cannot make diagnostics retain an abandoned service.
#if UNITY_EDITOR
            RegisterEditorInstance(this);
#else
            Application.lowMemory += HandleLowMemory;
#endif
        }

        /// <summary>
        /// Resolves the effective idle memory budget. A positive value is used verbatim (floored to 1 MB);
        /// 0 or negative falls back to the automatic, platform-aware default derived from device RAM.
        /// </summary>
        private static long ResolveIdleBudget(long requested)
        {
            long bytes = requested;
            if (bytes <= 0)
            {
                bytes = AssetPlatformDefaults.CacheTuning.IdleByteBudget;
            }
            return Math.Max(1L * 1024 * 1024, bytes);
        }

        private static void ValidateEntryLimit(int value, string parameterName)
        {
            if (value < 0 || value > MAX_IDLE_ENTRIES_PER_SEGMENT)
            {
                throw new ArgumentOutOfRangeException(parameterName);
            }
        }

        /// <summary>
        /// Overrides the idle (RefCount == 0) memory budget at runtime. Pass a positive byte value to set an
        /// explicit budget, or 0 to restore the automatic platform-aware default. Immediately runs an
        /// eviction pass so the idle pool is brought back under the new budget. Main-thread only.
        /// </summary>
        public void SetIdleMemoryBudget(long maxIdleBytes)
        {
            AssetRuntimeGuard.EnsureMainThread();
            ThrowIfEvaluatingRetentionRules();
            if (maxIdleBytes < 0L)
            {
                throw new ArgumentOutOfRangeException(nameof(maxIdleBytes));
            }

            if (Volatile.Read(ref _disposed) != 0) return;
            List<Exception> failures = null;
            lock (_gate)
            {
                if (Volatile.Read(ref _disposed) != 0) return;
                _maxIdleBytes = ResolveIdleBudget(maxIdleBytes);
                EvictIfNeeded(ref failures);
            }

            ThrowReleaseFailures(
                failures,
                "One or more idle provider handles failed to release while applying the cache memory budget.");
        }

        internal void Configure(AssetCacheTuning tuning)
        {
            AssetRuntimeGuard.EnsureMainThread();
            ThrowIfEvaluatingRetentionRules();
            if (Volatile.Read(ref _disposed) != 0) return;

            tuning = tuning.Normalized();
            List<Exception> failures = null;
            lock (_gate)
            {
                if (Volatile.Read(ref _disposed) != 0) return;
                _maxTrialEntries = tuning.ProbationEntryLimit;
                _maxMainEntries = tuning.ProtectedEntryLimit;
                _maxIdleBytes = tuning.IdleByteBudget;
                _clearIdleOnLowMemory = tuning.ClearIdleOnLowMemory;
                EvictIfNeeded(ref failures);
            }

            ThrowReleaseFailures(
                failures,
                "One or more idle provider handles failed to release while applying cache tuning.");
        }

        private void HandleLowMemory()
        {
            if (Volatile.Read(ref _disposed) != 0 || !_clearIdleOnLowMemory) return;
            ThrowIfEvaluatingRetentionRules();
            try
            {
                ClearAll();
            }
            catch (Exception ex) when (AssetRuntimeGuard.IsRecoverableException(ex))
            {
                // Unity invokes low-memory subscribers as a multicast event. A provider release failure must not
                // prevent other packages or systems from receiving the same pressure notification.
                UnityEngine.Debug.LogException(ex);
            }
        }

        /// <summary>
        /// Builds an allocation-free value key that includes operation kind and asset type.
        /// </summary>
        internal static AssetCacheKey BuildCacheKey(string location, Type assetType)
        {
            return BuildCacheKey(
                location,
                assetType,
                assetType == null ? AssetCacheEntryKind.RawFile : AssetCacheEntryKind.Asset);
        }

        internal static AssetCacheKey BuildCacheKey(
            string location,
            Type assetType,
            AssetCacheEntryKind operationKind)
        {
            return new AssetCacheKey(location, assetType, operationKind);
        }

        /// <summary>
        /// Registers a freshly-loaded handle (RefCount == 1) into the active map.
        /// The cacheKey must be built via <see cref="BuildCacheKey"/> by the caller.
        /// </summary>
        internal IReferenceCounted RegisterNew(
            AssetCacheKey cacheKey,
            string bucket,
            string tag,
            string owner,
            IReferenceCounted handle)
        {
            AssetRuntimeGuard.EnsureMainThread();
            ThrowIfEvaluatingRetentionRules();
            if (handle == null) throw new ArgumentNullException(nameof(handle));
            if (!cacheKey.IsValid) throw new ArgumentException("Cache key is invalid.", nameof(cacheKey));
            if (handle is IOperation operation)
            {
                // Custom providers may expose a faulted memoized task directly instead of using the built-in
                // broadcast helper. Observe it once at the ownership boundary so an abandoned lease cannot report
                // the same provider failure later from a completion-source finalizer.
                AssetOperationBroadcast.Observe(operation.Task);
            }

            if (Volatile.Read(ref _disposed) != 0)
            {
                ForceDisposeHandle(handle);
                throw new ObjectDisposedException(nameof(AssetCacheService));
            }

            lock (_gate)
            {
                if (_activeMap.TryGetValue(cacheKey, out CacheNode activeNode))
                {
                    activeNode.Handle.Retain();
                    IncrementAccessCount(activeNode);
                    AddMetadata(activeNode, bucket, tag, owner);
                    ForceDisposeHandle(handle);
                    return activeNode.Handle;
                }

                if (_idleMap.Remove(cacheKey, out CacheNode idleNode))
                {
                    RemoveFromLru(idleNode);
                    RemoveFromBucketIndex(idleNode);
                    idleNode.Handle.Retain();
                    MarkTrackedHandleActive(idleNode.Handle);
                    IncrementAccessCount(idleNode);
                    AddMetadata(idleNode, bucket, tag, owner);
                    _activeMap[cacheKey] = idleNode;
                    UpdatePeaks();
                    ForceDisposeHandle(handle);
                    return idleNode.Handle;
                }

                var node = NodePool.Get();
                node.Key = cacheKey;
                node.Bucket = bucket;
                node.Tag = tag;
                node.Owner = owner;
                node.Handle = handle;
                node.AccessCount = 1;
                AddMetadata(node, bucket, tag, owner);

                _activeMap[cacheKey] = node;
                UpdatePeaks();
                return handle;
            }
        }

        /// <summary>
        /// Returns an existing handle, auto-retaining it. Returns null on cache miss.
        /// The cacheKey must be built via <see cref="BuildCacheKey"/> by the caller.
        /// </summary>
        internal IReferenceCounted Get(AssetCacheKey cacheKey, string bucket, string tag, string owner)
        {
            AssetRuntimeGuard.EnsureMainThread();
            ThrowIfEvaluatingRetentionRules();
            if (Volatile.Read(ref _disposed) != 0 || !cacheKey.IsValid) return null;

            lock (_gate)
            {
                if (_activeMap.TryGetValue(cacheKey, out var activeNode))
                {
                    _activeHitCount++;
                    activeNode.Handle.Retain();
                    IncrementAccessCount(activeNode);
                    AddMetadata(activeNode, bucket, tag, owner);
                    return activeNode.Handle;
                }

                if (_idleMap.TryGetValue(cacheKey, out var idleNode))
                {
                    _idleHitCount++;
                    _idleMap.Remove(cacheKey);
                    RemoveFromLru(idleNode);
                    RemoveFromBucketIndex(idleNode);

                    IncrementAccessCount(idleNode);
                    idleNode.Handle.Retain();
                    MarkTrackedHandleActive(idleNode.Handle);
                    AddMetadata(idleNode, bucket, tag, owner);

                    _activeMap[cacheKey] = idleNode;
                    UpdatePeaks();
                    return idleNode.Handle;
                }

                _cacheMissCount++;
                return null;
            }
        }

        /// <summary>
        /// Returns true if a handle for the given cache key is currently retained (active) or pooled (idle).
        /// Does not load anything and does not affect LRU ordering or reference counts.
        /// </summary>
        internal bool Contains(AssetCacheKey cacheKey)
        {
            AssetRuntimeGuard.EnsureMainThread();
            if (Volatile.Read(ref _disposed) != 0 || !cacheKey.IsValid) return false;
            lock (_gate)
            {
                return _activeMap.ContainsKey(cacheKey) || _idleMap.ContainsKey(cacheKey);
            }
        }

        /// <summary>
        /// Invalidates every key mapping after a provider catalog or manifest commit. Idle handles are disposed
        /// immediately. Active handles remain valid for their current owners, but are detached from lookup so a
        /// subsequent load resolves against the committed provider generation; their final release disposes them.
        /// </summary>
        internal void AdvanceGeneration()
        {
            AssetRuntimeGuard.EnsureMainThread();
            ThrowIfEvaluatingRetentionRules();
            if (Volatile.Read(ref _disposed) != 0)
            {
                throw new ObjectDisposedException(nameof(AssetCacheService));
            }

            List<Exception> failures = null;
            lock (_gate)
            {
                if (Volatile.Read(ref _disposed) != 0)
                {
                    throw new ObjectDisposedException(nameof(AssetCacheService));
                }

                ClearIdleInternalBestEffort(EvictionReason.Explicit, ref failures);
                foreach (KeyValuePair<AssetCacheKey, CacheNode> pair in _activeMap)
                {
                    // The caller still owns the handle. Detach it from keyed lookup so the next load resolves
                    // against the new provider generation, while retaining enumerable shutdown ownership.
                    _generationDetachedMap[pair.Value.Handle] = pair.Value;
                }

                _activeMap.Clear();
            }

            if (failures != null)
            {
                throw new AggregateException(
                    "One or more idle provider handles failed to release while advancing the cache generation.",
                    failures);
            }
        }

        /// <summary>
        /// Called via the keyed release callback when a handle's RefCount reaches zero.
        /// O(1): the key is passed directly by the handle, eliminating the previous O(N) scan.
        /// </summary>
        internal void OnHandleReleased(AssetCacheKey cacheKey, IReferenceCounted handle)
        {
            AssetRuntimeGuard.EnsureMainThread();
            ThrowIfEvaluatingRetentionRules();
            if (Volatile.Read(ref _disposed) != 0)
            {
                ForceDisposeHandle(handle);
                return;
            }

            // Handles that are not registered in the cache (e.g. Scene, Instantiate) pass an invalid
            // cacheKey. They must be disposed directly; no map lookup is needed.
            if (!cacheKey.IsValid)
            {
                ForceDisposeHandle(handle);
                return;
            }

            List<Exception> failures = null;
            lock (_gate)
            {
                if (!_activeMap.TryGetValue(cacheKey, out var node) || node.Handle != handle)
                {
                    if (!_generationDetachedMap.TryGetValue(handle, out node))
                    {
                        ForceDisposeHandle(handle);
                        return;
                    }

                    // A detached handle is never admitted back into the keyed cache. Its last caller release
                    // retires it directly, while a concurrent revival keeps the detached ownership record alive.
                    if (handle.RefCount != 0)
                    {
                        return;
                    }

                    _generationDetachedMap.Remove(handle);
                    DisposeNodeBestEffort(node, ref failures);
                    node = null;
                }

                if (node != null)
                {
                    // Revival guard: the refcount drop to zero happens before this lock is taken, so another
                    // caller may have re-acquired (Retained) the same key in between. If so the handle is live
                    // again and must stay in the active map. Never sink a still-referenced handle into the idle
                    // pool, where it could be evicted and disposed out from under its owner.
                    if (handle.RefCount != 0)
                    {
                        return;
                    }

                    // Only a successfully completed provider operation has a stable value that can enter the idle cache.
                    // Pending, faulted, and canceled operations are disposed even when diagnostic Error text is empty.
                    if (handle is IOperation operation &&
                        operation.Task.Status != UniTaskStatus.Succeeded)
                    {
                        _failedOperationRejectionCount++;
                        _activeMap.Remove(cacheKey);
                        DisposeNodeBestEffort(node, ref failures);
                    }
                    else if (node.MetadataOverflow)
                    {
                        _metadataOverflowRejectionCount++;
                        _activeMap.Remove(cacheKey);
                        DisposeNodeBestEffort(node, ref failures);
                    }
                    else
                    {
                        _activeMap.Remove(cacheKey);
                        bool hasKnownFootprint = false;
                        try
                        {
                            hasKnownFootprint = TryEstimateHandleBytes(handle, out node.EstimatedBytes);
                        }
                        catch (Exception ex) when (AssetRuntimeGuard.IsRecoverableException(ex))
                        {
                            _footprintEstimationFailureCount++;
                            AddReleaseFailure(ref failures, ex);
                            node.EstimatedBytes = 0L;
                        }

                        if (!hasKnownFootprint)
                        {
                            // Unknown footprints cannot be governed by the byte budget. A single entry larger than the
                            // complete idle budget also cannot become resident; reject it before admission so it cannot
                            // churn an existing hot set while the eviction pass eventually removes the candidate itself.
                            _unknownFootprintRejectionCount++;
                            DisposeNodeBestEffort(node, ref failures);
                        }
                        else if (node.EstimatedBytes > _maxIdleBytes)
                        {
                            _oversizeRejectionCount++;
                            DisposeNodeBestEffort(node, ref failures);
                        }
                        else
                        {
                            _idleMap[cacheKey] = node;
                            node.IdleSinceTimestamp = _clock.Timestamp;
                            AddToBucketIndex(node);

                            // Reused entries enter the protected segment; first-use idle entries enter probation.
                            if (node.AccessCount > 1 || node.IsInMainPool)
                            {
                                node.IsInMainPool = true;
                                AddToMainHead(node);
                            }
                            else
                            {
                                node.IsInMainPool = false;
                                AddToTrialHead(node);
                            }

                            _idleAdmissionCount++;
                            UpdatePeaks();
                            EvictIfNeeded(ref failures);
                        }
                    }
                }
            }

            ThrowReleaseFailures(
                failures,
                "One or more failures occurred while returning an asset to the cache.");
        }

        /// <summary>
        /// Disposes a handle that is not (or no longer) tracked by the cache.
        /// All handles must implement IInternalCacheable; reflection is intentionally not used.
        /// </summary>
        private static void ForceDisposeHandle(IReferenceCounted handle)
        {
            if (handle is IInternalCacheable cacheable)
                cacheable.ForceDispose();
            else
                handle.Dispose();
        }

        public void ClearBucket(string bucket)
        {
            AssetRuntimeGuard.EnsureMainThread();
            ThrowIfEvaluatingRetentionRules();
            if (Volatile.Read(ref _disposed) != 0 || string.IsNullOrEmpty(bucket)) return;

            List<Exception> failures = null;
            lock (_gate)
            {
                ClearBucketsInternal(bucket, false, ref failures);
            }

            ThrowReleaseFailures(
                failures,
                "One or more idle provider handles failed to release while clearing the cache bucket.");
        }

        public void ClearBucketsByPrefix(string bucketPrefix)
        {
            AssetRuntimeGuard.EnsureMainThread();
            ThrowIfEvaluatingRetentionRules();
            if (Volatile.Read(ref _disposed) != 0 || string.IsNullOrEmpty(bucketPrefix)) return;

            List<Exception> failures = null;
            lock (_gate)
            {
                ClearBucketsInternal(bucketPrefix, true, ref failures);
            }

            ThrowReleaseFailures(
                failures,
                "One or more idle provider handles failed to release while clearing the cache bucket hierarchy.");
        }

        public void ClearAll()
        {
            AssetRuntimeGuard.EnsureMainThread();
            ThrowIfEvaluatingRetentionRules();
            List<Exception> failures = null;
            lock (_gate)
            {
                ClearIdleInternalBestEffort(EvictionReason.Explicit, ref failures);
            }

            if (failures != null)
            {
                throw new AggregateException(
                    "One or more idle provider handles failed to release while clearing the cache.",
                    failures);
            }
        }

        /// <summary>
        /// Evicts idle (RefCount == 0) handles matched by <paramref name="policy"/> and disposes them.
        /// Active (RefCount &gt; 0) handles are never touched. Returns the number of handles evicted.
        /// The cache carries no timer and no frame driver; callers own the scheduling policy.
        /// Main-thread only.
        /// </summary>
        public int TrimIdle(AssetCacheRetentionPolicy policy)
        {
            AssetRuntimeGuard.EnsureMainThread();
            ThrowIfEvaluatingRetentionRules();
            if (Volatile.Read(ref _disposed) != 0) return 0;

            List<Exception> failures = null;
            int evicted;
            lock (_gate)
            {
                if (Volatile.Read(ref _disposed) != 0) return 0;
                if (_trialHead == null && _mainHead == null) return 0;

                long nowTimestamp = _clock.Timestamp;

                _nodesToClearScratch.Clear();
                _evaluatingRetentionRules = 1;
                try
                {
                    CollectMatching(_trialHead, nowTimestamp, policy, _nodesToClearScratch);
                    CollectMatching(_mainHead, nowTimestamp, policy, _nodesToClearScratch);
                }
                finally
                {
                    _evaluatingRetentionRules = 0;
                }

                evicted = _nodesToClearScratch.Count;
                for (int i = 0; i < evicted; i++)
                {
                    EvictNodeBestEffort(_nodesToClearScratch[i], EvictionReason.Retention, ref failures);
                }

                _nodesToClearScratch.Clear();
            }

            ThrowReleaseFailures(
                failures,
                "One or more idle provider handles failed to release while trimming the cache.");
            return evicted;
        }

        private void ThrowIfEvaluatingRetentionRules()
        {
            if (_evaluatingRetentionRules != 0)
            {
                throw new InvalidOperationException(
                    "Cache mutation is not allowed from an asset retention rule callback.");
            }
        }

        // Eviction must not happen while iterating the list (EvictNode rewires links), so matching nodes are
        // collected first, then evicted by the caller.
        private void CollectMatching(CacheNode head, long nowTimestamp, AssetCacheRetentionPolicy policy, List<CacheNode> outList)
        {
            var current = head;
            while (current != null)
            {
                var entry = new AssetCacheEntryInfo(
                    current.Key,
                    current.Bucket,
                    current.Tag,
                    current.Owner,
                    current.AdditionalBuckets,
                    current.AdditionalTags,
                    current.AdditionalOwners,
                    current.AccessCount,
                    current.EstimatedBytes,
                    _clock.GetElapsed(current.IdleSinceTimestamp, nowTimestamp),
                    current.IsInMainPool ? AssetCacheIdleTier.Protected : AssetCacheIdleTier.Probation);

                if (policy.ShouldEvict(in entry))
                {
                    outList.Add(current);
                }

                current = current.Next;
            }
        }

        private void ClearIdleInternalBestEffort(EvictionReason reason, ref List<Exception> failures)
        {
            CacheNode current = _trialHead;
            while (current != null)
            {
                CacheNode next = current.Next;
                RecordEviction(current, reason);
                DisposeNodeBestEffort(current, ref failures);
                current = next;
            }

            current = _mainHead;
            while (current != null)
            {
                CacheNode next = current.Next;
                RecordEviction(current, reason);
                DisposeNodeBestEffort(current, ref failures);
                current = next;
            }

            _idleMap.Clear();
            _bucketIndex.Clear();

            _trialHead = null;
            _trialTail = null;
            _trialCount = 0;

            _mainHead = null;
            _mainTail = null;
            _mainCount = 0;

            _idleBytes = 0;
        }

        private void ClearAllInternalBestEffort(out List<Exception> failures)
        {
            failures = null;
            foreach (KeyValuePair<AssetCacheKey, CacheNode> pair in _activeMap)
            {
                DisposeNodeBestEffort(pair.Value, ref failures);
            }

            _activeMap.Clear();
            foreach (KeyValuePair<IReferenceCounted, CacheNode> pair in _generationDetachedMap)
            {
                DisposeNodeBestEffort(pair.Value, ref failures);
            }

            _generationDetachedMap.Clear();
            ClearIdleInternalBestEffort(EvictionReason.Explicit, ref failures);
        }

        private void DisposeNodeBestEffort(CacheNode node, ref List<Exception> failures)
        {
            try
            {
                ForceDisposeHandle(node.Handle);
            }
            catch (Exception ex) when (AssetRuntimeGuard.IsRecoverableException(ex))
            {
                _providerReleaseFailureCount++;
                AddReleaseFailure(ref failures, ex);
            }
            finally
            {
                NodePool.Release(node);
            }
        }

        private static void AddReleaseFailure(ref List<Exception> failures, Exception exception)
        {
            failures ??= new List<Exception>();
            failures.Add(exception);
        }

        private static void ThrowReleaseFailures(List<Exception> failures, string message)
        {
            if (failures != null)
            {
                throw new AggregateException(message, failures);
            }
        }

        private void ClearBucketsInternal(
            string bucketOrPrefix,
            bool includeChildren,
            ref List<Exception> failures)
        {
            if (_bucketIndex.Count == 0) return;

            _nodesToClearScratch.Clear();
            _nodesToClearSetScratch.Clear();
            _matchedBucketsScratch.Clear();

            foreach (var kvp in _bucketIndex)
            {
                bool matches = includeChildren
                    ? AssetBucketPath.IsPrefixMatch(kvp.Key, bucketOrPrefix)
                    : string.Equals(kvp.Key, bucketOrPrefix, StringComparison.Ordinal);

                if (!matches) continue;

                _matchedBucketsScratch.Add(kvp.Key);
                foreach (var node in kvp.Value)
                {
                    if (_nodesToClearSetScratch.Add(node))
                    {
                        _nodesToClearScratch.Add(node);
                    }
                }
            }

            if (_matchedBucketsScratch.Count == 0) return;

            for (int i = 0; i < _nodesToClearScratch.Count; i++)
            {
                EvictNodeBestEffort(_nodesToClearScratch[i], EvictionReason.Explicit, ref failures);
            }

            _nodesToClearScratch.Clear();
            _nodesToClearSetScratch.Clear();
            _matchedBucketsScratch.Clear();
        }

        private void EvictIfNeeded(ref List<Exception> failures)
        {
            while (_mainCount > _maxMainEntries && _mainTail != null)
            {
                CacheNode demoted = _mainTail;
                RemoveFromLru(demoted);
                demoted.IsInMainPool = false;
                AddToTrialHead(demoted);
            }

            while (_trialCount > _maxTrialEntries && _trialTail != null)
            {
                EvictNodeBestEffort(_trialTail, EvictionReason.Capacity, ref failures);
            }

            // Memory-budget pass: even within entry-count limits, a few large assets can blow the byte
            // budget. Evict idle handles (trial/probation first, then main) until back under budget.
            while (_idleBytes > _maxIdleBytes && (_trialTail != null || _mainTail != null))
            {
                EvictNodeBestEffort(_trialTail ?? _mainTail, EvictionReason.MemoryBudget, ref failures);
            }
        }

        private void EvictNodeBestEffort(
            CacheNode victim,
            EvictionReason reason,
            ref List<Exception> failures)
        {
            RecordEviction(victim, reason);
            RemoveFromLru(victim);
            RemoveFromBucketIndex(victim);
            _idleMap.Remove(victim.Key);
            DisposeNodeBestEffort(victim, ref failures);
        }

        private void RecordEviction(CacheNode victim, EvictionReason reason)
        {
            _evictionCount++;
            switch (reason)
            {
                case EvictionReason.Capacity:
                    _capacityEvictionCount++;
                    break;
                case EvictionReason.MemoryBudget:
                    _memoryBudgetEvictionCount++;
                    break;
                case EvictionReason.Retention:
                    _retentionEvictionCount++;
                    break;
                default:
                    _explicitEvictionCount++;
                    break;
            }

            long bytes = victim.EstimatedBytes;
            if (bytes > 0L)
            {
                _evictedBytesApprox = _evictedBytesApprox > long.MaxValue - bytes
                    ? long.MaxValue
                    : _evictedBytesApprox + bytes;
            }
        }

        private void UpdatePeaks()
        {
            int activeCount = _activeMap.Count + _generationDetachedMap.Count;
            if (activeCount > _peakActiveCount)
            {
                _peakActiveCount = activeCount;
            }

            int idleCount = _trialCount + _mainCount;
            if (idleCount > _peakIdleCount)
            {
                _peakIdleCount = idleCount;
            }

            if (_idleBytes > _peakIdleBytesApprox)
            {
                _peakIdleBytesApprox = _idleBytes;
            }
        }

        private void AddToTrialHead(CacheNode node)
        {
            node.Prev = null;
            node.Next = _trialHead;
            if (_trialHead != null) _trialHead.Prev = node;
            _trialHead = node;
            if (_trialTail == null) _trialTail = node;
            _trialCount++;
            _idleBytes += node.EstimatedBytes;
        }

        private void AddToMainHead(CacheNode node)
        {
            node.Prev = null;
            node.Next = _mainHead;
            if (_mainHead != null) _mainHead.Prev = node;
            _mainHead = node;
            if (_mainTail == null) _mainTail = node;
            _mainCount++;
            _idleBytes += node.EstimatedBytes;
        }

        private void RemoveFromLru(CacheNode node)
        {
            if (node.IsInMainPool)
            {
                if (node.Prev != null) node.Prev.Next = node.Next; else _mainHead = node.Next;
                if (node.Next != null) node.Next.Prev = node.Prev; else _mainTail = node.Prev;
                _mainCount--;
            }
            else
            {
                if (node.Prev != null) node.Prev.Next = node.Next; else _trialHead = node.Next;
                if (node.Next != null) node.Next.Prev = node.Prev; else _trialTail = node.Prev;
                _trialCount--;
            }

            _idleBytes -= node.EstimatedBytes;
            if (_idleBytes < 0) _idleBytes = 0;

            node.Next = null;
            node.Prev = null;
        }

        private static void AddMetadata(
            CacheNode node,
            string bucket,
            string tag,
            string owner)
        {
            if (!AddMetadataValue(ref node.Bucket, ref node.AdditionalBuckets, bucket) ||
                !AddMetadataValue(ref node.Tag, ref node.AdditionalTags, tag) ||
                !AddMetadataValue(ref node.Owner, ref node.AdditionalOwners, owner))
            {
                node.MetadataOverflow = true;
            }
        }

        private static void IncrementAccessCount(CacheNode node)
        {
            if (node.AccessCount < int.MaxValue)
            {
                node.AccessCount++;
            }
        }

        private static bool TryEstimateHandleBytes(IReferenceCounted handle, out long estimatedBytes)
        {
            estimatedBytes = 0L;
            if (handle is not IAssetMemoryFootprint footprint)
            {
                return false;
            }

            estimatedBytes = footprint.EstimateRuntimeBytes();
            if (estimatedBytes <= 0L)
            {
                estimatedBytes = 0L;
                return false;
            }

            estimatedBytes = Math.Min(estimatedBytes, MAX_ESTIMATED_ENTRY_BYTES);
            return true;
        }

        private static bool AddMetadataValue(
            ref string primary,
            ref List<string> additional,
            string value)
        {
            if (string.IsNullOrEmpty(value) || string.Equals(primary, value, StringComparison.Ordinal))
            {
                return true;
            }

            if (string.IsNullOrEmpty(primary))
            {
                primary = value;
                return true;
            }

            if (additional != null)
            {
                for (int i = 0; i < additional.Count; i++)
                {
                    if (string.Equals(additional[i], value, StringComparison.Ordinal))
                    {
                        return true;
                    }
                }
            }

            int currentCount = 1 + (additional?.Count ?? 0);
            if (currentCount >= MAX_METADATA_VALUES_PER_KIND)
            {
                return false;
            }

            additional ??= new List<string>(2);
            additional.Add(value);
            return true;
        }

        private void AddToBucketIndex(CacheNode node)
        {
            AddBucketIndexValue(node, node.Bucket);
            List<string> additionalBuckets = node.AdditionalBuckets;
            int count = additionalBuckets?.Count ?? 0;
            for (int i = 0; i < count; i++)
            {
                AddBucketIndexValue(node, additionalBuckets[i]);
            }
        }

        private void RemoveFromBucketIndex(CacheNode node)
        {
            RemoveBucketIndexValue(node, node.Bucket);
            List<string> additionalBuckets = node.AdditionalBuckets;
            int count = additionalBuckets?.Count ?? 0;
            for (int i = 0; i < count; i++)
            {
                RemoveBucketIndexValue(node, additionalBuckets[i]);
            }
        }

        private void AddBucketIndexValue(CacheNode node, string bucket)
        {
            if (string.IsNullOrEmpty(bucket)) return;
            if (!_bucketIndex.TryGetValue(bucket, out HashSet<CacheNode> set))
            {
                set = new HashSet<CacheNode>();
                _bucketIndex[bucket] = set;
            }

            set.Add(node);
        }

        private void RemoveBucketIndexValue(CacheNode node, string bucket)
        {
            if (string.IsNullOrEmpty(bucket)) return;
            if (_bucketIndex.TryGetValue(bucket, out HashSet<CacheNode> set))
            {
                set.Remove(node);
                if (set.Count == 0) _bucketIndex.Remove(bucket);
            }
        }

        // --- Runtime-visible diagnostics ---
        // Lightweight, allocation-free counters usable in any build (not Editor-only). Intended for retention
        // schedulers, in-game memory HUDs, or telemetry. Per-entry enumeration stays Editor-only (see below).

        /// <summary>Approximate aggregate footprint (bytes) of all idle (RefCount == 0) handles. Thread-safe.</summary>
        public long IdleBytesApprox { get { lock (_gate) { return _idleBytes; } } }

        /// <summary>The idle memory budget (bytes) above which idle handles are evicted regardless of count.</summary>
        public long MaxIdleBytesBudget => Volatile.Read(ref _maxIdleBytes);

        /// <summary>Number of idle (RefCount == 0) handles currently pooled across the trial and main tiers. Thread-safe.</summary>
        public int IdleCount { get { lock (_gate) { return _trialCount + _mainCount; } } }

        /// <summary>Number of active (RefCount &gt; 0) handles currently pinned and never evictable. Thread-safe.</summary>
        public int ActiveCount { get { lock (_gate) { return _activeMap.Count + _generationDetachedMap.Count; } } }

        /// <summary>
        /// Creates a compact runtime snapshot without enumerating cache entries.
        /// Intended for telemetry, stress HUDs, and automatic memory governance.
        /// </summary>
        internal CycloneGames.AssetManagement.Runtime.AssetRuntimeCacheSnapshot CreateRuntimeSnapshot(string packageName, string providerName)
        {
            lock (_gate)
            {
                return new CycloneGames.AssetManagement.Runtime.AssetRuntimeCacheSnapshot(
                    packageName,
                    providerName,
                    _activeMap.Count + _generationDetachedMap.Count,
                    _trialCount + _mainCount,
                    _idleBytes,
                    _maxIdleBytes,
                    _activeHitCount,
                    _idleHitCount,
                    _cacheMissCount,
                    _idleAdmissionCount,
                    _failedOperationRejectionCount,
                    _metadataOverflowRejectionCount,
                    _unknownFootprintRejectionCount,
                    _oversizeRejectionCount,
                    _footprintEstimationFailureCount,
                    _evictionCount,
                    _capacityEvictionCount,
                    _memoryBudgetEvictionCount,
                    _retentionEvictionCount,
                    _explicitEvictionCount,
                    _evictedBytesApprox,
                    _providerReleaseFailureCount,
                    _peakActiveCount,
                    _peakIdleCount,
                    _peakIdleBytesApprox);
            }
        }

        public void Dispose()
        {
            AssetRuntimeGuard.EnsureMainThread();
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            try
            {
#if !UNITY_EDITOR
                Application.lowMemory -= HandleLowMemory;
#endif
                List<Exception> failures;
                lock (_gate)
                {
                    ClearAllInternalBestEffort(out failures);
                }

                if (failures != null)
                {
                    throw new AggregateException(
                        "One or more cached provider handles failed to release.",
                        failures);
                }
            }
            finally
            {
#if UNITY_EDITOR
                UnregisterEditorInstance(this);
#endif
            }
        }

        private static void MarkTrackedHandleActive(IReferenceCounted handle)
        {
            if (handle is ITrackedAssetHandle tracked)
            {
                HandleTracker.MarkActive(tracked.DiagnosticHandleId);
            }
        }

#if UNITY_EDITOR
        private static long _nextDiagnosticId;
        private static readonly object _globalInstancesLock = new object();
        private static readonly List<WeakReference<AssetCacheService>> _globalInstances =
            new List<WeakReference<AssetCacheService>>();
        private static readonly List<AssetCacheService> _lowMemoryTargets = new List<AssetCacheService>(4);
        private static bool _globalLowMemorySubscribed;

        private static void RegisterEditorInstance(AssetCacheService service)
        {
            lock (_globalInstancesLock)
            {
                PruneDeadEditorInstancesUnderLock();
                _globalInstances.Add(new WeakReference<AssetCacheService>(service));
                if (!_globalLowMemorySubscribed)
                {
                    Application.lowMemory += HandleGlobalLowMemory;
                    _globalLowMemorySubscribed = true;
                }
            }
        }

        private static void UnregisterEditorInstance(AssetCacheService service)
        {
            lock (_globalInstancesLock)
            {
                for (int i = _globalInstances.Count - 1; i >= 0; i--)
                {
                    if (!_globalInstances[i].TryGetTarget(out AssetCacheService target) ||
                        target == null ||
                        ReferenceEquals(target, service))
                    {
                        _globalInstances.RemoveAt(i);
                    }
                }

                UnsubscribeGlobalLowMemoryWhenEmptyUnderLock();
            }
        }

        private static void HandleGlobalLowMemory()
        {
            lock (_globalInstancesLock)
            {
                _lowMemoryTargets.Clear();
                PruneDeadEditorInstancesUnderLock();
                for (int i = 0; i < _globalInstances.Count; i++)
                {
                    if (_globalInstances[i].TryGetTarget(out AssetCacheService service) && service != null)
                    {
                        _lowMemoryTargets.Add(service);
                    }
                }
            }

            try
            {
                for (int i = 0; i < _lowMemoryTargets.Count; i++)
                {
                    _lowMemoryTargets[i].HandleLowMemory();
                }
            }
            finally
            {
                _lowMemoryTargets.Clear();
            }
        }

        internal static void CopyGlobalInstancesTo(List<AssetCacheService> destination)
        {
            if (destination == null)
            {
                throw new ArgumentNullException(nameof(destination));
            }

            lock (_globalInstancesLock)
            {
                destination.Clear();
                PruneDeadEditorInstancesUnderLock();
                for (int i = 0; i < _globalInstances.Count; i++)
                {
                    if (_globalInstances[i].TryGetTarget(out AssetCacheService service) && service != null)
                    {
                        destination.Add(service);
                    }
                }
            }
        }

        /// <summary>
        /// Begins an Editor diagnostics epoch and returns the number of externally retained cache services that
        /// survived subsystem registration. The weak registry does not own those services, keeps their low-memory
        /// handling active, and leaves them visible for diagnosis when domain reload is disabled. Identity counters
        /// remain monotonic so a survivor cannot collide with a later Play Mode observation epoch.
        /// </summary>
        internal static int BeginEditorDiagnosticsEpoch()
        {
            lock (_globalInstancesLock)
            {
                PruneDeadEditorInstancesUnderLock();
                return _globalInstances.Count;
            }
        }

        private static void PruneDeadEditorInstancesUnderLock()
        {
            for (int i = _globalInstances.Count - 1; i >= 0; i--)
            {
                if (!_globalInstances[i].TryGetTarget(out AssetCacheService service) ||
                    service == null ||
                    Volatile.Read(ref service._disposed) != 0)
                {
                    _globalInstances.RemoveAt(i);
                }
            }

            UnsubscribeGlobalLowMemoryWhenEmptyUnderLock();
        }

        private static void UnsubscribeGlobalLowMemoryWhenEmptyUnderLock()
        {
            if (_globalInstances.Count == 0 && _globalLowMemorySubscribed)
            {
                Application.lowMemory -= HandleGlobalLowMemory;
                _globalLowMemorySubscribed = false;
            }
        }

        public struct CacheDiagnosticEntry
        {
            /// <summary>Unique row identity for the current Editor session.</summary>
            public long DiagnosticId;
            /// <summary>Exact built-in provider handle identity, or 0 when a custom handle does not expose one.</summary>
            public long HandleId;
            /// <summary>Clean asset path for display (without type suffix).</summary>
            public string Location;
            /// <summary>Short asset type name (e.g. "Texture2D"), or null for typeless entries.</summary>
            public string AssetType;
            /// <summary>Composite cache key for internal matching across tiers.</summary>
            public string CacheKey;
            public string Bucket;
            public string Tag;
            public string Owner;
            public int BucketAssociationCount;
            public int TagAssociationCount;
            public int OwnerAssociationCount;
            public int RefCount;
            public int AccessCount;
            public string ProviderType;
            public bool IsGenerationDetached;
            /// <summary>Approximate runtime footprint (bytes) of the underlying asset.</summary>
            public long EstimatedBytes;
        }

        internal readonly struct CacheDiagnosticCapture
        {
            public readonly int ActiveTotal;
            public readonly int ProbationTotal;
            public readonly int ProtectedTotal;
            public readonly int ActiveCaptured;
            public readonly int ProbationCaptured;
            public readonly int ProtectedCaptured;

            public CacheDiagnosticCapture(
                int activeTotal,
                int probationTotal,
                int protectedTotal,
                int activeCaptured,
                int probationCaptured,
                int protectedCaptured)
            {
                ActiveTotal = activeTotal;
                ProbationTotal = probationTotal;
                ProtectedTotal = protectedTotal;
                ActiveCaptured = activeCaptured;
                ProbationCaptured = probationCaptured;
                ProtectedCaptured = protectedCaptured;
            }

            public bool IsTruncated =>
                ActiveCaptured < ActiveTotal ||
                ProbationCaptured < ProbationTotal ||
                ProtectedCaptured < ProtectedTotal;
        }

        public void GetDiagnostics(List<CacheDiagnosticEntry> active, List<CacheDiagnosticEntry> trial, List<CacheDiagnosticEntry> main)
        {
            GetDiagnostics(active, trial, main, int.MaxValue, int.MaxValue, int.MaxValue);
        }

        internal CacheDiagnosticCapture GetDiagnostics(
            List<CacheDiagnosticEntry> active,
            List<CacheDiagnosticEntry> trial,
            List<CacheDiagnosticEntry> main,
            int maxActiveEntries,
            int maxProbationEntries,
            int maxProtectedEntries)
        {
            if (maxActiveEntries < 0) throw new ArgumentOutOfRangeException(nameof(maxActiveEntries));
            if (maxProbationEntries < 0) throw new ArgumentOutOfRangeException(nameof(maxProbationEntries));
            if (maxProtectedEntries < 0) throw new ArgumentOutOfRangeException(nameof(maxProtectedEntries));

            active?.Clear();
            trial?.Clear();
            main?.Clear();

            lock (_gate)
            {
                int activeTotal = _activeMap.Count + _generationDetachedMap.Count;
                int probationTotal = _trialCount;
                int protectedTotal = _mainCount;

                if (active != null && maxActiveEntries > 0)
                {
                    // Detached generations are operationally important and normally rare. Capture them first so a
                    // large keyed set cannot hide stale-generation ownership from a bounded Editor snapshot.
                    foreach (var kvp in _generationDetachedMap)
                    {
                        if (active.Count >= maxActiveEntries)
                        {
                            break;
                        }

                        active.Add(CreateDiagnosticEntry(kvp.Value, isGenerationDetached: true));
                    }

                    foreach (var kvp in _activeMap)
                    {
                        if (active.Count >= maxActiveEntries)
                        {
                            break;
                        }

                        active.Add(CreateDiagnosticEntry(kvp.Value, isGenerationDetached: false));
                    }
                }

                if (trial != null && maxProbationEntries > 0)
                {
                    var node = _trialHead;
                    while (node != null && trial.Count < maxProbationEntries)
                    {
                        trial.Add(CreateDiagnosticEntry(node, isGenerationDetached: false));
                        node = node.Next;
                    }
                }

                if (main != null && maxProtectedEntries > 0)
                {
                    var node = _mainHead;
                    while (node != null && main.Count < maxProtectedEntries)
                    {
                        main.Add(CreateDiagnosticEntry(node, isGenerationDetached: false));
                        node = node.Next;
                    }
                }

                return new CacheDiagnosticCapture(
                    activeTotal,
                    probationTotal,
                    protectedTotal,
                    active?.Count ?? 0,
                    trial?.Count ?? 0,
                    main?.Count ?? 0);
            }
        }

        private static CacheDiagnosticEntry CreateDiagnosticEntry(
            CacheNode node,
            bool isGenerationDetached)
        {
            AssetCacheKey key = node.Key;
            return new CacheDiagnosticEntry
            {
                DiagnosticId = node.DiagnosticId,
                HandleId = (node.Handle as ITrackedAssetHandle)?.DiagnosticHandleId ?? 0L,
                Location = key.Location,
                AssetType = GetDiagnosticAssetType(node, key.AssetType),
                CacheKey = GetDiagnosticKey(node, key),
                Bucket = node.Bucket,
                Tag = node.Tag,
                Owner = node.Owner,
                BucketAssociationCount = GetAssociationCount(node.Bucket, node.AdditionalBuckets),
                TagAssociationCount = GetAssociationCount(node.Tag, node.AdditionalTags),
                OwnerAssociationCount = GetAssociationCount(node.Owner, node.AdditionalOwners),
                RefCount = node.Handle?.RefCount ?? 0,
                AccessCount = node.AccessCount,
                ProviderType = GetDiagnosticProviderType(node, node.Handle),
                IsGenerationDetached = isGenerationDetached,
                // Active handles use their last cached idle estimate (or 0 before first release). Diagnostics
                // must not issue a native memory query per asset on every refresh.
                EstimatedBytes = node.EstimatedBytes
            };
        }

        private static string GetProviderType(IReferenceCounted handle)
        {
            if (handle == null) return "Unknown";
            var name = handle.GetType().Name;
            if (name.StartsWith("Yoo")) return "YooAsset";
            if (name.StartsWith("Addressable")) return "Addressables";
            if (name.StartsWith("Resources")) return "Resources";
            return name;
        }

        private static string GetDiagnosticKey(CacheNode node, AssetCacheKey key)
        {
            string diagnosticKey = node.DiagnosticKey;
            if (diagnosticKey != null) return diagnosticKey;

            diagnosticKey = key.ToDiagnosticString();
            node.DiagnosticKey = diagnosticKey;
            return diagnosticKey;
        }

        private static string GetDiagnosticAssetType(CacheNode node, Type assetType)
        {
            if (assetType == null) return null;

            string typeName = node.DiagnosticAssetType;
            if (typeName != null) return typeName;

            typeName = assetType.Name;
            node.DiagnosticAssetType = typeName;
            return typeName;
        }

        private static string GetDiagnosticProviderType(CacheNode node, IReferenceCounted handle)
        {
            string providerType = node.DiagnosticProviderType;
            if (providerType != null) return providerType;

            providerType = GetProviderType(handle);
            node.DiagnosticProviderType = providerType;
            return providerType;
        }

        private static int GetAssociationCount(string primary, List<string> additional)
        {
            return (string.IsNullOrEmpty(primary) ? 0 : 1) + (additional?.Count ?? 0);
        }
#endif
    }
}
