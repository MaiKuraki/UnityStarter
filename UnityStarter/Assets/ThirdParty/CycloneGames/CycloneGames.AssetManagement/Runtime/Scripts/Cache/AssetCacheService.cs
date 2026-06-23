using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

namespace CycloneGames.AssetManagement.Runtime.Cache
{
    internal enum AssetCacheOperationKind : byte
    {
        Asset = 0,
        AllAssets = 1,
        RawFile = 2,
    }

    /// <summary>
    /// Multi-tier asset cache implementing a W-TinyLFU-inspired strategy:
    /// - Active (ARC): handles with RefCount > 0 are pinned and never evicted.
    /// - Trial (W-LRU): zero-refcount handles on first idle entry; guards against scan-burst pollution.
    /// - Main (LFU+LRU): handles promoted after multiple accesses; evicts by recency within frequency tier.
    /// - Bucket: logical lifetime groups for deterministic mass-eviction (e.g. per-scene).
    /// - Aging: periodic right-shift of AccessCount prevents stale history from blocking promotion.
    /// </summary>
    public sealed class AssetCacheService : IDisposable
    {
        private sealed class CacheNode
        {
            public string Location;
            public string Bucket;
            public string Tag;
            public string Owner;
            public IReferenceCounted Handle;
            public int AccessCount;
            // Approximate runtime footprint in bytes, computed when the node enters the idle pool.
            public long EstimatedBytes;

            public CacheNode Next;
            public CacheNode Prev;

            public bool IsInMainPool;
        }

        private sealed class CacheKeyPoolNode
        {
            public (string Location, Type AssetType, AssetCacheOperationKind OperationKind) Key;
            public string CacheKey;
            public CacheKeyPoolNode Prev;
            public CacheKeyPoolNode Next;
        }

        private static class NodePool
        {
            private const int MAX_POOL_SIZE = 512;
            private static readonly Stack<CacheNode> _pool = new Stack<CacheNode>(128);
            private static readonly object _lock = new object();

            public static CacheNode Get()
            {
                lock (_lock) return _pool.Count > 0 ? _pool.Pop() : new CacheNode();
            }

            public static void Release(CacheNode node)
            {
                node.Location = null;
                node.Bucket = null;
                node.Tag = null;
                node.Owner = null;
                node.Handle = null;
                node.Next = null;
                node.Prev = null;
                node.AccessCount = 0;
                node.EstimatedBytes = 0;
                node.IsInMainPool = false;

                lock (_lock)
                {
                    if (_pool.Count < MAX_POOL_SIZE) _pool.Push(node);
                }
            }
        }

        private readonly IAssetPackage _package;

        private readonly int _maxTrialEntries;
        private readonly int _maxMainEntries;

        // Handles currently held by at least one consumer (RefCount > 0).
        private readonly Dictionary<string, CacheNode> _activeMap;
        // Handles idle (RefCount == 0), partitioned into trial and main LRU lists.
        private readonly Dictionary<string, CacheNode> _idleMap;
        // Reverse index: bucket name → set of CacheNodes in that bucket (idle only).
        // Enables O(1) bucket lookup in ClearBucket instead of O(N) linked-list scan.
        private readonly Dictionary<string, HashSet<CacheNode>> _bucketIndex;
        private readonly List<CacheNode> _nodesToClearScratch;
        private readonly List<string> _matchedBucketsScratch;

        private CacheNode _trialHead;
        private CacheNode _trialTail;
        private int _trialCount;

        private CacheNode _mainHead;
        private CacheNode _mainTail;
        private int _mainCount;

        // Aging: every N release operations, halve all AccessCounts in the main pool to prevent
        // stale hot-history from blocking newly-popular assets from being promoted.
        private int _releaseOpCount;
        private const int AGING_INTERVAL = 512;

        // Memory-budget eviction: idle (RefCount == 0) handles are evicted not only by entry count
        // but also when their aggregate estimated footprint exceeds this budget. This prevents a few
        // large assets (e.g. 4K textures) from silently blowing the memory ceiling on low-end devices.
        private long _idleBytes;
        private long _maxIdleBytes;

        // The cache is main-thread-affine in practice (Unity asset/scene/instantiate APIs are main-thread
        // only). A plain monitor is used instead of ReaderWriterLockSlim: it is far cheaper when uncontended,
        // avoids the upgradeable-lock serialization that throttled the hot hit path, and still guarantees
        // correctness if a background thread ever touches the cache (e.g. diagnostics).
        private readonly object _gate = new object();
        private int _disposed;

        /// <summary>
        /// Creates a cache service. Pass 0 for any sizing argument to auto-size based on device memory and platform.
        /// </summary>
        public AssetCacheService(IAssetPackage package, int maxTrialEntries = 0, int maxMainEntries = 0, long maxIdleBytes = 0)
        {
            _package = package ?? throw new ArgumentNullException(nameof(package));

            int ramMB = SystemInfo.systemMemorySize;
            if (maxTrialEntries <= 0 || maxMainEntries <= 0)
            {
                // Adaptive sizing: scale cache to available device RAM.
                if (ramMB >= 4096) { maxTrialEntries = 64; maxMainEntries = 512; }
                else if (ramMB >= 2048) { maxTrialEntries = 32; maxMainEntries = 256; }
                else { maxTrialEntries = 16; maxMainEntries = 128; }

#if UNITY_WEBGL && !UNITY_EDITOR
                // WebGL has a hard browser memory ceiling and no real threads; keep the idle pool small.
                maxTrialEntries = Math.Min(maxTrialEntries, 16);
                maxMainEntries = Math.Min(maxMainEntries, 96);
#endif
            }

            _maxTrialEntries = Math.Max(1, maxTrialEntries);
            _maxMainEntries = Math.Max(1, maxMainEntries);
            _maxIdleBytes = ResolveIdleBudget(maxIdleBytes);

            _activeMap = new Dictionary<string, CacheNode>(128, StringComparer.Ordinal);
            _idleMap = new Dictionary<string, CacheNode>(_maxTrialEntries + _maxMainEntries, StringComparer.Ordinal);
            _bucketIndex = new Dictionary<string, HashSet<CacheNode>>(16, StringComparer.Ordinal);
            _nodesToClearScratch = new List<CacheNode>(16);
            _matchedBucketsScratch = new List<string>(8);

            // On memory pressure (iOS/Android), drain the entire idle pool immediately. Active handles
            // (RefCount > 0) are never touched, so in-use assets remain valid.
            Application.lowMemory += HandleLowMemory;

#if UNITY_EDITOR
            lock (_globalInstancesLock) { GlobalInstances.Add(this); }
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
                int ramMB = SystemInfo.systemMemorySize;
                if (ramMB >= 4096) bytes = 512L * 1024 * 1024;
                else if (ramMB >= 2048) bytes = 256L * 1024 * 1024;
                else bytes = 96L * 1024 * 1024;

#if UNITY_WEBGL && !UNITY_EDITOR
                bytes = Math.Min(bytes, 96L * 1024 * 1024);
#endif
            }
            return Math.Max(1L * 1024 * 1024, bytes);
        }

        /// <summary>
        /// Overrides the idle (RefCount == 0) memory budget at runtime. Pass a positive byte value to set an
        /// explicit budget, or 0 to restore the automatic platform-aware default. Immediately runs an
        /// eviction pass so the idle pool is brought back under the new budget. Thread-safe.
        /// </summary>
        public void SetIdleMemoryBudget(long maxIdleBytes)
        {
            if (Volatile.Read(ref _disposed) != 0) return;
            lock (_gate)
            {
                if (Volatile.Read(ref _disposed) != 0) return;
                _maxIdleBytes = ResolveIdleBudget(maxIdleBytes);
                EvictIfNeeded();
            }
        }

        private void HandleLowMemory()
        {
            if (Volatile.Read(ref _disposed) != 0) return;
            ClearAll();
        }

        /// <summary>
        /// Builds a cache key that includes type information to prevent cross-type collisions.
        /// E.g. LoadAssetAsync<Texture2D>("atlas") and LoadAssetAsync<Sprite>("atlas")
        /// must map to different cache entries.
        /// Results are cached so that repeated loads of the same (location, type) pair
        /// produce zero string allocations after the first call.
        /// A bounded LRU is used instead of an unbounded dictionary so long-lived sessions
        /// do not accumulate metadata forever.
        /// </summary>
        private static readonly Dictionary<(string, Type, AssetCacheOperationKind), CacheKeyPoolNode> _cacheKeyPool =
            new Dictionary<(string, Type, AssetCacheOperationKind), CacheKeyPoolNode>(128);
        private static readonly object _cacheKeyPoolLock = new object();
        private const int MAX_CACHE_KEY_POOL_SIZE = 4096;
        private static CacheKeyPoolNode _cacheKeyPoolHead;
        private static CacheKeyPoolNode _cacheKeyPoolTail;
        private static int _cacheKeyPoolCount;

        internal static string BuildCacheKey(string location, Type assetType)
        {
            return BuildCacheKey(location, assetType, assetType == null ? AssetCacheOperationKind.RawFile : AssetCacheOperationKind.Asset);
        }

        internal static string BuildCacheKey(string location, Type assetType, AssetCacheOperationKind operationKind)
        {
            var key = (location, assetType, operationKind);
            lock (_cacheKeyPoolLock)
            {
                if (_cacheKeyPool.TryGetValue(key, out var node))
                {
                    MoveCacheKeyNodeToHead(node);
                    return node.CacheKey;
                }
            }

            // Build the composite key without boxing the enum (string.Concat(object[]) would box + allocate
            // an object[]). Use a precomputed string prefix so the all-string Concat overload is selected.
            string kindPrefix = operationKind == AssetCacheOperationKind.Asset ? "0|"
                : operationKind == AssetCacheOperationKind.AllAssets ? "1|"
                : "2|";
            var result = string.Concat(kindPrefix, location, "|", assetType?.FullName ?? string.Empty);
            lock (_cacheKeyPoolLock)
            {
                if (_cacheKeyPool.TryGetValue(key, out var existingNode))
                {
                    MoveCacheKeyNodeToHead(existingNode);
                    return existingNode.CacheKey;
                }

                var node = new CacheKeyPoolNode
                {
                    Key = key,
                    CacheKey = result
                };

                AddCacheKeyNodeToHead(node);
                _cacheKeyPool[key] = node;
                _cacheKeyPoolCount++;

                if (_cacheKeyPoolCount > MAX_CACHE_KEY_POOL_SIZE)
                {
                    EvictOldestCacheKeyNode();
                }
            }
            return result;
        }

        private static void AddCacheKeyNodeToHead(CacheKeyPoolNode node)
        {
            node.Prev = null;
            node.Next = _cacheKeyPoolHead;

            if (_cacheKeyPoolHead != null) _cacheKeyPoolHead.Prev = node;
            _cacheKeyPoolHead = node;

            if (_cacheKeyPoolTail == null) _cacheKeyPoolTail = node;
        }

        private static void MoveCacheKeyNodeToHead(CacheKeyPoolNode node)
        {
            if (ReferenceEquals(node, _cacheKeyPoolHead)) return;

            RemoveCacheKeyNode(node);
            AddCacheKeyNodeToHead(node);
        }

        private static void RemoveCacheKeyNode(CacheKeyPoolNode node)
        {
            if (node.Prev != null) node.Prev.Next = node.Next;
            else _cacheKeyPoolHead = node.Next;

            if (node.Next != null) node.Next.Prev = node.Prev;
            else _cacheKeyPoolTail = node.Prev;

            node.Prev = null;
            node.Next = null;
        }

        private static void EvictOldestCacheKeyNode()
        {
            var victim = _cacheKeyPoolTail;
            if (victim == null) return;

            RemoveCacheKeyNode(victim);
            _cacheKeyPool.Remove(victim.Key);
            _cacheKeyPoolCount--;
        }

        /// <summary>
        /// Registers a freshly-loaded handle (RefCount == 1) into the active map.
        /// The cacheKey must be built via <see cref="BuildCacheKey"/> by the caller.
        /// </summary>
        internal void RegisterNew(string cacheKey, string bucket, string tag, string owner, IReferenceCounted handle)
        {
            if (Volatile.Read(ref _disposed) != 0 || string.IsNullOrEmpty(cacheKey)) return;

            lock (_gate)
            {
                if (_idleMap.Remove(cacheKey, out var oldIdle))
                {
                    RemoveFromLru(oldIdle);
                    RemoveFromBucketIndex(oldIdle);
                    ForceDisposeHandle(oldIdle.Handle);
                    NodePool.Release(oldIdle);
                }

                if (_activeMap.Remove(cacheKey, out var oldActive))
                {
                    ForceDisposeHandle(oldActive.Handle);
                    NodePool.Release(oldActive);
                }

                var node = NodePool.Get();
                node.Location = cacheKey;
                node.Bucket = bucket;
                node.Tag = tag;
                node.Owner = owner;
                node.Handle = handle;
                node.AccessCount = 1;

                _activeMap[cacheKey] = node;
            }
        }

        /// <summary>
        /// Returns an existing handle, auto-retaining it. Returns null on cache miss.
        /// The cacheKey must be built via <see cref="BuildCacheKey"/> by the caller.
        /// </summary>
        internal IReferenceCounted Get(string cacheKey, string bucket, string tag, string owner)
        {
            if (Volatile.Read(ref _disposed) != 0 || string.IsNullOrEmpty(cacheKey)) return null;

            lock (_gate)
            {
                if (_activeMap.TryGetValue(cacheKey, out var activeNode))
                {
                    activeNode.Handle.Retain();
                    activeNode.AccessCount++;
                    if (!string.IsNullOrEmpty(tag)) activeNode.Tag = tag;
                    if (!string.IsNullOrEmpty(owner)) activeNode.Owner = owner;
                    return activeNode.Handle;
                }

                if (_idleMap.TryGetValue(cacheKey, out var idleNode))
                {
                    _idleMap.Remove(cacheKey);
                    RemoveFromLru(idleNode);
                    RemoveFromBucketIndex(idleNode);

                    idleNode.AccessCount++;
                    idleNode.Handle.Retain();

                    if (!string.IsNullOrEmpty(bucket)) idleNode.Bucket = bucket;
                    if (!string.IsNullOrEmpty(tag)) idleNode.Tag = tag;
                    if (!string.IsNullOrEmpty(owner)) idleNode.Owner = owner;

                    _activeMap[cacheKey] = idleNode;
                    return idleNode.Handle;
                }

                return null;
            }
        }

        /// <summary>
        /// Returns true if a handle for the given cache key is currently retained (active) or pooled (idle).
        /// Does not load anything and does not affect LRU ordering or reference counts.
        /// </summary>
        internal bool Contains(string cacheKey)
        {
            if (Volatile.Read(ref _disposed) != 0 || string.IsNullOrEmpty(cacheKey)) return false;
            lock (_gate)
            {
                return _activeMap.ContainsKey(cacheKey) || _idleMap.ContainsKey(cacheKey);
            }
        }

        /// <summary>
        /// Called via the keyed release callback when a handle's RefCount reaches zero.
        /// O(1): the key is passed directly by the handle, eliminating the previous O(N) scan.
        /// </summary>
        internal void OnHandleReleased(string cacheKey, IReferenceCounted handle)
        {
            if (Volatile.Read(ref _disposed) != 0)
            {
                ForceDisposeHandle(handle);
                return;
            }

            // Handles that are not registered in the cache (e.g. Scene, Instantiate) pass null as
            // cacheKey. They must be disposed directly — no map lookup needed.
            if (string.IsNullOrEmpty(cacheKey))
            {
                ForceDisposeHandle(handle);
                return;
            }

            lock (_gate)
            {
                if (!_activeMap.TryGetValue(cacheKey, out var node) || node.Handle != handle)
                {
                    ForceDisposeHandle(handle);
                    return;
                }

                // Revival guard: the refcount drop to zero happens before this lock is taken, so another
                // caller may have re-acquired (Retained) the same key in between. If so the handle is live
                // again and must stay in the active map — never sink a still-referenced handle into the idle
                // pool, where it could be evicted and disposed out from under its owner.
                if (handle.RefCount != 0)
                {
                    return;
                }

                _activeMap.Remove(cacheKey);
                _idleMap[cacheKey] = node;
                node.EstimatedBytes = (handle as IAssetMemoryFootprint)?.EstimateRuntimeBytes() ?? 0;
                AddToBucketIndex(node);

                // Promotion: multiple-access or previously-promoted → main pool; first-time idle → trial pool.
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

                MaybeAge();
                EvictIfNeeded();
            }
        }

        /// <summary>
        /// Disposes a handle that is not (or no longer) tracked by the cache.
        /// All handles must implement IInternalCacheable — reflection is intentionally not used.
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
            if (Volatile.Read(ref _disposed) != 0 || string.IsNullOrEmpty(bucket)) return;

            lock (_gate)
            {
                ClearBucketsInternal(bucket, includeChildren: false);
            }
        }

        public void ClearBucketsByPrefix(string bucketPrefix)
        {
            if (Volatile.Read(ref _disposed) != 0 || string.IsNullOrEmpty(bucketPrefix)) return;

            lock (_gate)
            {
                ClearBucketsInternal(bucketPrefix, includeChildren: true);
            }
        }

        public void ClearAll()
        {
            lock (_gate)
            {
                ClearAllInternal(includeActive: false);
            }
        }

        private void ClearAllInternal(bool includeActive)
        {
            if (includeActive)
            {
                foreach (var kvp in _activeMap)
                {
                    ForceDisposeHandle(kvp.Value.Handle);
                    NodePool.Release(kvp.Value);
                }

                _activeMap.Clear();
            }

            DrainList(_trialHead);
            DrainList(_mainHead);

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

        // Static to avoid delegate allocation on every ClearAll call.
        private static void DrainList(CacheNode head)
        {
            var current = head;
            while (current != null)
            {
                var next = current.Next;
                ForceDisposeHandle(current.Handle);
                NodePool.Release(current);
                current = next;
            }
        }

        private void ClearBucketsInternal(string bucketOrPrefix, bool includeChildren)
        {
            if (_bucketIndex.Count == 0) return;

            _nodesToClearScratch.Clear();
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
                    _nodesToClearScratch.Add(node);
                }
            }

            if (_matchedBucketsScratch.Count == 0) return;

            for (int i = 0; i < _matchedBucketsScratch.Count; i++)
            {
                _bucketIndex.Remove(_matchedBucketsScratch[i]);
            }

            for (int i = 0; i < _nodesToClearScratch.Count; i++)
            {
                var node = _nodesToClearScratch[i];
                RemoveFromLru(node);
                _idleMap.Remove(node.Location);
                ForceDisposeHandle(node.Handle);
                NodePool.Release(node);
            }

            _nodesToClearScratch.Clear();
            _matchedBucketsScratch.Clear();
        }

        /// <summary>
        /// Periodically halves all AccessCount values in the main pool.
        /// Prevents long-lived historical hot-assets from permanently locking the main pool
        /// and blocking newly-frequent assets from being promoted.
        /// Must be called inside the write lock.
        /// </summary>
        private void MaybeAge()
        {
            if (++_releaseOpCount % AGING_INTERVAL != 0) return;

            var node = _mainHead;
            while (node != null)
            {
                if (node.AccessCount > 1) node.AccessCount >>= 1;
                node = node.Next;
            }
        }

        private void EvictIfNeeded()
        {
            while (_trialCount > _maxTrialEntries && _trialTail != null)
            {
                EvictNode(_trialTail);
            }

            while (_mainCount > _maxMainEntries && _mainTail != null)
            {
                EvictNode(_mainTail);
            }

            // Memory-budget pass: even within entry-count limits, a few large assets can blow the byte
            // budget. Evict idle handles (trial/probation first, then main) until back under budget.
            while (_idleBytes > _maxIdleBytes && (_trialTail != null || _mainTail != null))
            {
                EvictNode(_trialTail ?? _mainTail);
            }
        }

        private void EvictNode(CacheNode victim)
        {
            RemoveFromLru(victim);
            RemoveFromBucketIndex(victim);
            _idleMap.Remove(victim.Location);
            ForceDisposeHandle(victim.Handle);
            NodePool.Release(victim);
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

        private void AddToBucketIndex(CacheNode node)
        {
            if (string.IsNullOrEmpty(node.Bucket)) return;
            if (!_bucketIndex.TryGetValue(node.Bucket, out var set))
            {
                set = new HashSet<CacheNode>();
                _bucketIndex[node.Bucket] = set;
            }
            set.Add(node);
        }

        private void RemoveFromBucketIndex(CacheNode node)
        {
            if (string.IsNullOrEmpty(node.Bucket)) return;
            if (_bucketIndex.TryGetValue(node.Bucket, out var set))
            {
                set.Remove(node);
                if (set.Count == 0) _bucketIndex.Remove(node.Bucket);
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            Application.lowMemory -= HandleLowMemory;
            lock (_gate)
            {
                ClearAllInternal(includeActive: true);
            }

#if UNITY_EDITOR
            lock (_globalInstancesLock) { GlobalInstances.Remove(this); }
#endif
        }

#if UNITY_EDITOR
        private static readonly object _globalInstancesLock = new object();
        public static readonly List<AssetCacheService> GlobalInstances = new List<AssetCacheService>();

        /// <summary>Approximate aggregate footprint (bytes) of all idle (RefCount == 0) handles.</summary>
        public long IdleBytesApprox { get { lock (_gate) { return _idleBytes; } } }
        /// <summary>The idle memory budget (bytes) above which idle handles are evicted regardless of count.</summary>
        public long MaxIdleBytesBudget => _maxIdleBytes;

        public struct CacheDiagnosticEntry
        {
            /// <summary>Clean asset path for display (without type suffix).</summary>
            public string Location;
            /// <summary>Short asset type name (e.g. "Texture2D"), or null for typeless entries.</summary>
            public string AssetType;
            /// <summary>Composite cache key for internal matching across tiers.</summary>
            public string CacheKey;
            public string Bucket;
            public string Tag;
            public string Owner;
            public int RefCount;
            public int AccessCount;
            public string ProviderType;
            /// <summary>Approximate runtime footprint (bytes) of the underlying asset.</summary>
            public long EstimatedBytes;
        }

        /// <summary>
        /// Splits a composite cache key into a clean location and short type name.
        /// </summary>
        internal static void ParseCacheKey(string cacheKey, out string location, out string assetTypeName)
        {
            if (string.IsNullOrEmpty(cacheKey))
            {
                location = cacheKey;
                assetTypeName = null;
                return;
            }

            int firstSep = cacheKey.IndexOf('|');
            int lastSep = cacheKey.LastIndexOf('|');
            if (firstSep <= 0 || lastSep <= firstSep)
            {
                location = cacheKey;
                assetTypeName = null;
            }
            else
            {
                location = cacheKey.Substring(firstSep + 1, lastSep - firstSep - 1);
                string fullName = cacheKey.Substring(lastSep + 1);
                if (string.IsNullOrEmpty(fullName))
                {
                    assetTypeName = null;
                    return;
                }

                int dot = fullName.LastIndexOf('.');
                assetTypeName = dot >= 0 ? fullName.Substring(dot + 1) : fullName;
            }
        }

        public void GetDiagnostics(List<CacheDiagnosticEntry> active, List<CacheDiagnosticEntry> trial, List<CacheDiagnosticEntry> main)
        {
            active?.Clear();
            trial?.Clear();
            main?.Clear();

            lock (_gate)
            {
                if (active != null)
                {
                    foreach (var kvp in _activeMap)
                    {
                        ParseCacheKey(kvp.Value.Location, out var loc, out var typeName);
                        active.Add(new CacheDiagnosticEntry
                        {
                            Location = loc,
                            AssetType = typeName,
                            CacheKey = kvp.Value.Location,
                            Bucket = kvp.Value.Bucket,
                            Tag = kvp.Value.Tag,
                            Owner = kvp.Value.Owner,
                            RefCount = kvp.Value.Handle.RefCount,
                            AccessCount = kvp.Value.AccessCount,
                            ProviderType = GetProviderType(kvp.Value.Handle),
                            EstimatedBytes = (kvp.Value.Handle as IAssetMemoryFootprint)?.EstimateRuntimeBytes() ?? 0
                        });
                    }
                }

                if (trial != null)
                {
                    var node = _trialHead;
                    while (node != null)
                    {
                        ParseCacheKey(node.Location, out var loc, out var typeName);
                        trial.Add(new CacheDiagnosticEntry
                        {
                            Location = loc,
                            AssetType = typeName,
                            CacheKey = node.Location,
                            Bucket = node.Bucket,
                            Tag = node.Tag,
                            Owner = node.Owner,
                            RefCount = node.Handle?.RefCount ?? 0,
                            AccessCount = node.AccessCount,
                            ProviderType = GetProviderType(node.Handle),
                            EstimatedBytes = node.EstimatedBytes
                        });
                        node = node.Next;
                    }
                }

                if (main != null)
                {
                    var node = _mainHead;
                    while (node != null)
                    {
                        ParseCacheKey(node.Location, out var loc, out var typeName);
                        main.Add(new CacheDiagnosticEntry
                        {
                            Location = loc,
                            AssetType = typeName,
                            CacheKey = node.Location,
                            Bucket = node.Bucket,
                            Tag = node.Tag,
                            Owner = node.Owner,
                            RefCount = node.Handle?.RefCount ?? 0,
                            AccessCount = node.AccessCount,
                            ProviderType = GetProviderType(node.Handle),
                            EstimatedBytes = node.EstimatedBytes
                        });
                        node = node.Next;
                    }
                }
            }
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
#endif
    }
}
