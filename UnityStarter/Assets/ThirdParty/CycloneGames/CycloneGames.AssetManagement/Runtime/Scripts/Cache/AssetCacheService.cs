using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

namespace CycloneGames.AssetManagement.Runtime.Cache
{
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

            public CacheNode Next;
            public CacheNode Prev;

            public bool IsInMainPool;
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

        private readonly ReaderWriterLockSlim _rwLock = new ReaderWriterLockSlim(LockRecursionPolicy.NoRecursion);
        private int _disposed;

        /// <summary>
        /// Creates a cache service. Pass 0/0 to auto-size based on device memory.
        /// </summary>
        public AssetCacheService(IAssetPackage package, int maxTrialEntries = 0, int maxMainEntries = 0)
        {
            _package = package ?? throw new ArgumentNullException(nameof(package));

            if (maxTrialEntries <= 0 || maxMainEntries <= 0)
            {
                // Adaptive sizing: scale cache to available device RAM.
                int ramMB = SystemInfo.systemMemorySize;
                if (ramMB >= 4096) { maxTrialEntries = 64; maxMainEntries = 512; }
                else if (ramMB >= 2048) { maxTrialEntries = 32; maxMainEntries = 256; }
                else { maxTrialEntries = 16; maxMainEntries = 128; }
            }

            _maxTrialEntries = Math.Max(1, maxTrialEntries);
            _maxMainEntries = Math.Max(1, maxMainEntries);

            _activeMap = new Dictionary<string, CacheNode>(128, StringComparer.Ordinal);
            _idleMap = new Dictionary<string, CacheNode>(_maxTrialEntries + _maxMainEntries, StringComparer.Ordinal);
            _bucketIndex = new Dictionary<string, HashSet<CacheNode>>(16, StringComparer.Ordinal);

#if UNITY_EDITOR
            lock (_globalInstancesLock) { GlobalInstances.Add(this); }
#endif
        }

        /// <summary>
        /// Builds a cache key that includes type information to prevent cross-type collisions.
        /// E.g. LoadAssetAsync<Texture2D>("atlas") and LoadAssetAsync<Sprite>("atlas")
        /// must map to different cache entries.
        /// Results are cached so that repeated loads of the same (location, type) pair
        /// produce zero string allocations after the first call.
        /// </summary>
        private static readonly Dictionary<(string, System.Type), string> _cacheKeyPool =
            new Dictionary<(string, System.Type), string>(128);
        private static readonly object _cacheKeyPoolLock = new object();

        internal static string BuildCacheKey(string location, System.Type assetType)
        {
            // For the common single-type case, avoid allocation by returning location directly
            // when the caller passes null (e.g. RawFile which has no type ambiguity).
            if (assetType == null) return location;

            var tuple = (location, assetType);
            lock (_cacheKeyPoolLock)
            {
                if (_cacheKeyPool.TryGetValue(tuple, out var cached)) return cached;
            }

            var result = string.Concat(location, "|", assetType.FullName);
            lock (_cacheKeyPoolLock)
            {
                _cacheKeyPool[tuple] = result;
            }
            return result;
        }

        /// <summary>
        /// Registers a freshly-loaded handle (RefCount == 1) into the active map.
        /// The cacheKey must be built via <see cref="BuildCacheKey"/> by the caller.
        /// </summary>
        internal void RegisterNew(string cacheKey, string bucket, string tag, string owner, IReferenceCounted handle)
        {
            if (Volatile.Read(ref _disposed) != 0 || string.IsNullOrEmpty(cacheKey)) return;

            _rwLock.EnterWriteLock();
            try
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
            finally
            {
                _rwLock.ExitWriteLock();
            }
        }

        /// <summary>
        /// Returns an existing handle, auto-retaining it. Returns null on cache miss.
        /// The cacheKey must be built via <see cref="BuildCacheKey"/> by the caller.
        /// </summary>
        internal IReferenceCounted Get(string cacheKey, string bucket, string tag, string owner)
        {
            if (Volatile.Read(ref _disposed) != 0 || string.IsNullOrEmpty(cacheKey)) return null;

            _rwLock.EnterUpgradeableReadLock();
            try
            {
                if (_activeMap.TryGetValue(cacheKey, out var activeNode))
                {
                    activeNode.Handle.Retain();
                    activeNode.AccessCount++;
                    if (!string.IsNullOrEmpty(tag)) activeNode.Tag = tag;
                    if (!string.IsNullOrEmpty(owner)) activeNode.Owner = owner;
                    return activeNode.Handle;
                }

                if (_idleMap.TryGetValue(cacheKey, out _))
                {
                    _rwLock.EnterWriteLock();
                    try
                    {
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
                    }
                    finally
                    {
                        _rwLock.ExitWriteLock();
                    }
                }

                return null;
            }
            finally
            {
                _rwLock.ExitUpgradeableReadLock();
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

            _rwLock.EnterWriteLock();
            try
            {
                if (!_activeMap.TryGetValue(cacheKey, out var node) || node.Handle != handle)
                {
                    ForceDisposeHandle(handle);
                    return;
                }

                _activeMap.Remove(cacheKey);
                _idleMap[cacheKey] = node;
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
            finally
            {
                _rwLock.ExitWriteLock();
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

            _rwLock.EnterWriteLock();
            try
            {
                ClearBucketsInternal(bucket, includeChildren: false);
            }
            finally
            {
                _rwLock.ExitWriteLock();
            }
        }

        public void ClearBucketsByPrefix(string bucketPrefix)
        {
            if (Volatile.Read(ref _disposed) != 0 || string.IsNullOrEmpty(bucketPrefix)) return;

            _rwLock.EnterWriteLock();
            try
            {
                ClearBucketsInternal(bucketPrefix, includeChildren: true);
            }
            finally
            {
                _rwLock.ExitWriteLock();
            }
        }

        public void ClearAll()
        {
            _rwLock.EnterWriteLock();
            try
            {
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
            }
            finally
            {
                _rwLock.ExitWriteLock();
            }
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

            var nodesToClear = new List<CacheNode>(8);
            var matchedBuckets = new List<string>(4);

            foreach (var kvp in _bucketIndex)
            {
                bool matches = includeChildren
                    ? AssetBucketPath.IsPrefixMatch(kvp.Key, bucketOrPrefix)
                    : string.Equals(kvp.Key, bucketOrPrefix, StringComparison.Ordinal);

                if (!matches) continue;

                matchedBuckets.Add(kvp.Key);
                foreach (var node in kvp.Value)
                {
                    nodesToClear.Add(node);
                }
            }

            if (matchedBuckets.Count == 0) return;

            for (int i = 0; i < matchedBuckets.Count; i++)
            {
                _bucketIndex.Remove(matchedBuckets[i]);
            }

            for (int i = 0; i < nodesToClear.Count; i++)
            {
                var node = nodesToClear[i];
                RemoveFromLru(node);
                _idleMap.Remove(node.Location);
                ForceDisposeHandle(node.Handle);
                NodePool.Release(node);
            }
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
                var victim = _trialTail;
                RemoveFromLru(victim);
                RemoveFromBucketIndex(victim);
                _idleMap.Remove(victim.Location);
                ForceDisposeHandle(victim.Handle);
                NodePool.Release(victim);
            }

            while (_mainCount > _maxMainEntries && _mainTail != null)
            {
                var victim = _mainTail;
                RemoveFromLru(victim);
                RemoveFromBucketIndex(victim);
                _idleMap.Remove(victim.Location);
                ForceDisposeHandle(victim.Handle);
                NodePool.Release(victim);
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
        }

        private void AddToMainHead(CacheNode node)
        {
            node.Prev = null;
            node.Next = _mainHead;
            if (_mainHead != null) _mainHead.Prev = node;
            _mainHead = node;
            if (_mainTail == null) _mainTail = node;
            _mainCount++;
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
            ClearAll();
            _rwLock.Dispose();

#if UNITY_EDITOR
            lock (_globalInstancesLock) { GlobalInstances.Remove(this); }
#endif
        }

#if UNITY_EDITOR
        private static readonly object _globalInstancesLock = new object();
        public static readonly List<AssetCacheService> GlobalInstances = new List<AssetCacheService>();

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
        }

        /// <summary>
        /// Splits a composite cache key ("location|TypeFullName") into a clean location and short type name.
        /// </summary>
        internal static void ParseCacheKey(string cacheKey, out string location, out string assetTypeName)
        {
            if (string.IsNullOrEmpty(cacheKey))
            {
                location = cacheKey;
                assetTypeName = null;
                return;
            }

            int sep = cacheKey.LastIndexOf('|');
            if (sep < 0)
            {
                location = cacheKey;
                assetTypeName = null;
            }
            else
            {
                location = cacheKey.Substring(0, sep);
                string fullName = cacheKey.Substring(sep + 1);
                int dot = fullName.LastIndexOf('.');
                assetTypeName = dot >= 0 ? fullName.Substring(dot + 1) : fullName;
            }
        }

        public void GetDiagnostics(List<CacheDiagnosticEntry> active, List<CacheDiagnosticEntry> trial, List<CacheDiagnosticEntry> main)
        {
            active?.Clear();
            trial?.Clear();
            main?.Clear();

            _rwLock.EnterReadLock();
            try
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
                            ProviderType = GetProviderType(kvp.Value.Handle)
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
                            ProviderType = GetProviderType(node.Handle)
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
                            ProviderType = GetProviderType(node.Handle)
                        });
                        node = node.Next;
                    }
                }
            }
            finally
            {
                _rwLock.ExitReadLock();
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
