// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using UnityEngine;
using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine.Serialization;
using UnityEngine.Networking;
using System.Collections.Generic;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace CycloneGames.Audio.Runtime
{
    public interface IAudioClipHandle : IDisposable
    {
        bool IsDone { get; }
        bool IsSuccess { get; }
        AudioClip Clip { get; }
        string Error { get; }
        int RefCount { get; }
        void Retain();
        void Release();
    }

    public interface IAudioClipProvider
    {
        string Name { get; }
        int Priority { get; }
        bool CanLoad(AudioClipReference reference);
        UniTask<IAudioClipHandle> LoadAsync(AudioClipReference reference, CancellationToken cancellationToken);
    }

    public delegate UniTask<IAudioClipHandle> AudioClipReferenceLoader(AudioClipReference reference, CancellationToken cancellationToken);
    public delegate UniTask<ManagedAudioClipLoadResult> ManagedAudioClipReferenceLoader(AudioClipReference reference, CancellationToken cancellationToken);

    public readonly struct ExternalAudioClipCacheStats
    {
        public readonly int EntryCount;
        public readonly int LoadingCount;
        public readonly int LoadedCount;
        public readonly int FailedCount;
        public readonly int TotalRefCount;
        public readonly int TotalLoadRequests;
        public readonly int CacheHitCount;
        public readonly int CacheMissCount;
        public readonly int TotalFailureCount;

        public ExternalAudioClipCacheStats(
            int entryCount,
            int loadingCount,
            int loadedCount,
            int failedCount,
            int totalRefCount,
            int totalLoadRequests,
            int cacheHitCount,
            int cacheMissCount,
            int totalFailureCount)
        {
            EntryCount = entryCount;
            LoadingCount = loadingCount;
            LoadedCount = loadedCount;
            FailedCount = failedCount;
            TotalRefCount = totalRefCount;
            TotalLoadRequests = totalLoadRequests;
            CacheHitCount = cacheHitCount;
            CacheMissCount = cacheMissCount;
            TotalFailureCount = totalFailureCount;
        }
    }

    public readonly struct ManagedAudioClipLoadResult
    {
        public readonly AudioClip Clip;
        public readonly Action ReleaseAction;

        public ManagedAudioClipLoadResult(AudioClip clip, Action releaseAction = null)
        {
            Clip = clip;
            ReleaseAction = releaseAction;
        }

        public bool IsValid => Clip != null;
    }

    public readonly struct ExternalAudioClipCacheEntryInfo
    {
        public readonly string Location;
        public readonly string ClipName;
        public readonly bool IsDone;
        public readonly bool IsSuccess;
        public readonly int RefCount;
        public readonly string Error;

        public ExternalAudioClipCacheEntryInfo(string location, string clipName, bool isDone, bool isSuccess, int refCount, string error)
        {
            Location = location;
            ClipName = clipName;
            IsDone = isDone;
            IsSuccess = isSuccess;
            RefCount = refCount;
            Error = error;
        }
    }

    public static class AudioClipResolver
    {
        private const int ReferenceLoaderProviderPriority = 300;
        private const int LocationKindLoaderProviderPriority = 200;
        private const int BuiltInExternalProviderPriority = 100;

        private static readonly Dictionary<int, AudioClipReferenceLoader> runtimeReferenceLoaders = new Dictionary<int, AudioClipReferenceLoader>();
        private static readonly Dictionary<AudioLocationKind, AudioClipReferenceLoader> runtimeLocationKindLoaders = new Dictionary<AudioLocationKind, AudioClipReferenceLoader>();
        private static readonly object runtimeReferenceLoaderLock = new object();
        private static readonly List<IAudioClipProvider> providers = new List<IAudioClipProvider>();
        private static readonly object providerLock = new object();
        private static readonly IAudioClipProvider referenceLoaderProvider = new ReferenceLoaderAudioClipProvider();
        private static readonly IAudioClipProvider locationKindLoaderProvider = new LocationKindAudioClipProvider();
        private static readonly IAudioClipProvider builtInExternalProvider = new BuiltInExternalAudioClipProvider();
        private static IAudioClipProvider[] cachedProviderSnapshot;
        private static bool providerSnapshotDirty = true;

        static AudioClipResolver()
        {
            RegisterProviderInternal(referenceLoaderProvider);
            RegisterProviderInternal(locationKindLoaderProvider);
            RegisterProviderInternal(builtInExternalProvider);
        }

        public static IAudioClipHandle CreateEmbedded(AudioClip clip)
        {
            return clip != null ? new EmbeddedAudioClipHandle(clip) : null;
        }

        public static IAudioClipHandle CreateManaged(AudioClip clip, Action releaseAction)
        {
            return clip != null ? new ManagedAudioClipHandle(clip, releaseAction) : null;
        }

        public static async UniTask<IAudioClipHandle> LoadExternalAsync(AudioClipReference reference, CancellationToken cancellationToken)
        {
            if (reference == null || string.IsNullOrWhiteSpace(reference.Location))
                return null;

            IAudioClipProvider[] providerSnapshot = GetProviderSnapshot();
            for (int i = 0; i < providerSnapshot.Length; i++)
            {
                IAudioClipProvider provider = providerSnapshot[i];
                if (provider == null || !provider.CanLoad(reference))
                    continue;

                IAudioClipHandle handle = await provider.LoadAsync(reference, cancellationToken);
                if (handle != null)
                    return handle;
            }

            return null;
        }

        public static void ClearExternalCache()
        {
            ExternalAudioClipHandle.ClearCache();
        }

        public static void RegisterReferenceLoader(AudioClipReference reference, AudioClipReferenceLoader loader)
        {
            if (reference == null || loader == null) return;

            lock (runtimeReferenceLoaderLock)
            {
                runtimeReferenceLoaders[reference.GetInstanceID()] = loader;
            }
        }

        public static void RegisterManagedReferenceLoader(AudioClipReference reference, ManagedAudioClipReferenceLoader loader)
        {
            if (reference == null || loader == null) return;

            RegisterReferenceLoader(reference, async (clipReference, cancellationToken) =>
            {
                ManagedAudioClipLoadResult result = await loader(clipReference, cancellationToken);
                if (!result.IsValid)
                    return null;

                return CreateManaged(result.Clip, result.ReleaseAction);
            });
        }

        public static void RegisterLocationKindLoader(AudioLocationKind locationKind, AudioClipReferenceLoader loader)
        {
            if (loader == null) return;

            lock (runtimeReferenceLoaderLock)
            {
                runtimeLocationKindLoaders[locationKind] = loader;
            }
        }

        public static void RegisterManagedLocationKindLoader(AudioLocationKind locationKind, ManagedAudioClipReferenceLoader loader)
        {
            if (loader == null) return;

            RegisterLocationKindLoader(locationKind, async (clipReference, cancellationToken) =>
            {
                ManagedAudioClipLoadResult result = await loader(clipReference, cancellationToken);
                if (!result.IsValid)
                    return null;

                return CreateManaged(result.Clip, result.ReleaseAction);
            });
        }

        public static void UnregisterReferenceLoader(AudioClipReference reference)
        {
            if (reference == null) return;

            lock (runtimeReferenceLoaderLock)
            {
                runtimeReferenceLoaders.Remove(reference.GetInstanceID());
            }
        }

        public static void UnregisterLocationKindLoader(AudioLocationKind locationKind)
        {
            lock (runtimeReferenceLoaderLock)
            {
                runtimeLocationKindLoaders.Remove(locationKind);
            }
        }

        public static void ClearReferenceLoaders()
        {
            lock (runtimeReferenceLoaderLock)
            {
                runtimeReferenceLoaders.Clear();
                runtimeLocationKindLoaders.Clear();
            }
        }

        public static void RegisterProvider(IAudioClipProvider provider)
        {
            if (provider == null) return;

            lock (providerLock)
            {
                RegisterProviderInternal(provider);
                providerSnapshotDirty = true;
            }
        }

        public static void UnregisterProvider(IAudioClipProvider provider)
        {
            if (provider == null) return;

            lock (providerLock)
            {
                providers.Remove(provider);
                providerSnapshotDirty = true;
            }
        }

        public static void ClearCustomProviders()
        {
            lock (providerLock)
            {
                providers.Clear();
                RegisterProviderInternal(referenceLoaderProvider);
                RegisterProviderInternal(locationKindLoaderProvider);
                RegisterProviderInternal(builtInExternalProvider);
                providerSnapshotDirty = true;
            }
        }

        public static void GetProviders(List<IAudioClipProvider> results)
        {
            if (results == null) return;

            lock (providerLock)
            {
                results.Clear();
                results.AddRange(providers);
            }
        }

        public static ExternalAudioClipCacheStats GetExternalCacheStats()
        {
            return ExternalAudioClipHandle.GetCacheStats();
        }

        public static void GetExternalCacheEntries(List<ExternalAudioClipCacheEntryInfo> results)
        {
            if (results == null) return;
            ExternalAudioClipHandle.FillCacheEntries(results);
        }

        private static AudioClipReferenceLoader GetRegisteredLoader(AudioClipReference reference)
        {
            if (reference == null) return null;

            lock (runtimeReferenceLoaderLock)
            {
                runtimeReferenceLoaders.TryGetValue(reference.GetInstanceID(), out AudioClipReferenceLoader loader);
                return loader;
            }
        }

        private static AudioClipReferenceLoader GetRegisteredLocationKindLoader(AudioLocationKind locationKind)
        {
            lock (runtimeReferenceLoaderLock)
            {
                runtimeLocationKindLoaders.TryGetValue(locationKind, out AudioClipReferenceLoader loader);
                return loader;
            }
        }

        private static void RegisterProviderInternal(IAudioClipProvider provider)
        {
            if (provider == null || providers.Contains(provider))
                return;

            int insertIndex = providers.Count;
            for (int i = 0; i < providers.Count; i++)
            {
                IAudioClipProvider existing = providers[i];
                if (existing == null || provider.Priority > existing.Priority)
                {
                    insertIndex = i;
                    break;
                }
            }

            providers.Insert(insertIndex, provider);
            providerSnapshotDirty = true;
        }

        private static IAudioClipProvider[] GetProviderSnapshot()
        {
            lock (providerLock)
            {
                if (providerSnapshotDirty || cachedProviderSnapshot == null)
                {
                    cachedProviderSnapshot = providers.ToArray();
                    providerSnapshotDirty = false;
                }
                return cachedProviderSnapshot;
            }
        }

        private sealed class ReferenceLoaderAudioClipProvider : IAudioClipProvider
        {
            public string Name => "Registered Reference Loader";
            public int Priority => ReferenceLoaderProviderPriority;

            public bool CanLoad(AudioClipReference reference)
            {
                return GetRegisteredLoader(reference) != null;
            }

            public UniTask<IAudioClipHandle> LoadAsync(AudioClipReference reference, CancellationToken cancellationToken)
            {
                AudioClipReferenceLoader loader = GetRegisteredLoader(reference);
                return loader != null ? loader(reference, cancellationToken) : UniTask.FromResult<IAudioClipHandle>(null);
            }
        }

        private sealed class LocationKindAudioClipProvider : IAudioClipProvider
        {
            public string Name => "Registered LocationKind Loader";
            public int Priority => LocationKindLoaderProviderPriority;

            public bool CanLoad(AudioClipReference reference)
            {
                return reference != null && GetRegisteredLocationKindLoader(reference.LocationKind) != null;
            }

            public UniTask<IAudioClipHandle> LoadAsync(AudioClipReference reference, CancellationToken cancellationToken)
            {
                AudioClipReferenceLoader loader = reference != null ? GetRegisteredLocationKindLoader(reference.LocationKind) : null;
                return loader != null ? loader(reference, cancellationToken) : UniTask.FromResult<IAudioClipHandle>(null);
            }
        }

        private sealed class BuiltInExternalAudioClipProvider : IAudioClipProvider
        {
            public string Name => "Built-in External Audio Provider";
            public int Priority => BuiltInExternalProviderPriority;

            public bool CanLoad(AudioClipReference reference)
            {
                return reference != null && reference.LocationKind != AudioLocationKind.AssetAddress;
            }

            public async UniTask<IAudioClipHandle> LoadAsync(AudioClipReference reference, CancellationToken cancellationToken)
            {
                if (reference == null || reference.LocationKind == AudioLocationKind.AssetAddress)
                    return null;

                string resolvedLocation = reference.ResolveLocation();
                return await ExternalAudioClipHandle.LoadAsync(resolvedLocation, cancellationToken);
            }
        }
    }

    internal sealed class EmbeddedAudioClipHandle : IAudioClipHandle
    {
        private AudioClip clip;
        private int refCount;

        public EmbeddedAudioClipHandle(AudioClip clip)
        {
            this.clip = clip;
            refCount = 1;
        }

        public bool IsDone => true;
        public bool IsSuccess => clip != null;
        public AudioClip Clip => clip;
        public string Error => clip == null ? "Embedded AudioClip is null." : string.Empty;
        public int RefCount => Interlocked.CompareExchange(ref refCount, 0, 0);

        public void Retain()
        {
            if (clip == null) return;
            Interlocked.Increment(ref refCount);
        }

        public void Release()
        {
            Interlocked.Decrement(ref refCount);
        }

        public void Dispose() => Release();
    }

    internal sealed class ManagedAudioClipHandle : IAudioClipHandle
    {
        private AudioClip clip;
        private Action releaseAction;
        private int refCount;
        private int disposed;

        public ManagedAudioClipHandle(AudioClip clip, Action releaseAction)
        {
            this.clip = clip;
            this.releaseAction = releaseAction;
            refCount = 1;
            disposed = 0;
        }

        public bool IsDone => true;
        public bool IsSuccess => clip != null;
        public AudioClip Clip => clip;
        public string Error => clip == null ? "Managed AudioClip is null." : string.Empty;
        public int RefCount => Interlocked.CompareExchange(ref refCount, 0, 0);

        public void Retain()
        {
            if (clip == null) return;
            Interlocked.Increment(ref refCount);
        }

        public void Release()
        {
            int newCount = Interlocked.Decrement(ref refCount);
            if (newCount > 0) return;

            // Ensure only one thread invokes the release action
            if (Interlocked.CompareExchange(ref disposed, 1, 0) != 0) return;

            try
            {
                releaseAction?.Invoke();
            }
            finally
            {
                releaseAction = null;
                clip = null;
            }
        }

        public void Dispose() => Release();
    }

    internal sealed class ExternalAudioClipHandle : IAudioClipHandle
    {
        private sealed class CacheEntry
        {
            public readonly string Location;
            public AudioClip Clip;
            public string Error = string.Empty;
            public bool IsDone;
            public bool IsSuccess;
            public int RefCount;
            public UniTask LoadTask;
            public bool LoadStarted;

            // Memory budget & eviction tracking
            public int AccessCount;
            public float LastAccessTime;
            public long EstimatedMemoryBytes;

            public CacheEntry(string location)
            {
                Location = location;
                LastAccessTime = Time.realtimeSinceStartup;
            }
        }

        private static readonly object cacheLock = new object();
        private static readonly Dictionary<string, CacheEntry> cache = new Dictionary<string, CacheEntry>(StringComparer.Ordinal);
        private static int totalLoadRequests;
        private static int cacheHitCount;
        private static int cacheMissCount;
        private static int totalFailureCount;

        private CacheEntry entry;
        private bool released;

        private ExternalAudioClipHandle(CacheEntry entry)
        {
            this.entry = entry;
        }

        public bool IsDone => entry != null && entry.IsDone;
        public bool IsSuccess => entry != null && entry.IsSuccess;
        public AudioClip Clip => entry?.Clip;
        public string Error => entry?.Error ?? "Audio clip handle released.";
        public int RefCount => entry?.RefCount ?? 0;

        public static async UniTask<IAudioClipHandle> LoadAsync(string location, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(location))
                return null;

            CacheEntry entry = AcquireEntry(location);
            var handle = new ExternalAudioClipHandle(entry);

            try
            {
                await entry.LoadTask.AttachExternalCancellation(cancellationToken);
                return handle;
            }
            catch
            {
                handle.Release();
                throw;
            }
        }

        public void Retain()
        {
            if (released || entry == null) return;

            lock (cacheLock)
            {
                if (released || entry == null) return;
                entry.RefCount++;
            }
        }

        public void Release()
        {
            if (released || entry == null) return;

            lock (cacheLock)
            {
                if (released || entry == null) return;

                released = true;
                entry.RefCount = Mathf.Max(0, entry.RefCount - 1);

                if (entry.RefCount == 0)
                {
                    // Don't destroy immediately — let the eviction system handle TTL-based cleanup.
                    // For failed loads that nobody holds, remove right away to avoid leaking error entries.
                    if (entry.IsDone && !entry.IsSuccess)
                    {
                        DestroyAndRemoveEntry(entry);
                    }
                    else
                    {
                        entry.LastAccessTime = Time.realtimeSinceStartup;
                    }
                }

                entry = null;
            }
        }

        public void Dispose() => Release();

        private static CacheEntry AcquireEntry(string location)
        {
            lock (cacheLock)
            {
                totalLoadRequests++;
                if (!cache.TryGetValue(location, out CacheEntry entry))
                {
                    cacheMissCount++;
                    entry = new CacheEntry(location);
                    entry.RefCount = 1;
                    entry.LoadStarted = true;
                    entry.LoadTask = LoadEntryAsync(entry);
                    cache[location] = entry;
                    return entry;
                }

                cacheHitCount++;
                entry.RefCount++;
                entry.AccessCount++;
                entry.LastAccessTime = Time.realtimeSinceStartup;
                if (!entry.LoadStarted)
                {
                    entry.LoadStarted = true;
                    entry.LoadTask = LoadEntryAsync(entry);
                }

                return entry;
            }
        }

        private static async UniTask LoadEntryAsync(CacheEntry entry)
        {
            try
            {
                AudioType audioType = GetAudioType(entry.Location);
                using (var www = UnityWebRequestMultimedia.GetAudioClip(entry.Location, audioType))
                {
                    await www.SendWebRequest();

                    if (www.result == UnityWebRequest.Result.ConnectionError ||
                        www.result == UnityWebRequest.Result.ProtocolError)
                    {
                        lock (cacheLock)
                        {
                            entry.Error = www.error;
                            entry.IsDone = true;
                            entry.IsSuccess = false;
                            totalFailureCount++;
                            if (entry.RefCount == 0) DestroyAndRemoveEntry(entry);
                        }
                        return;
                    }

                    AudioClip clip = DownloadHandlerAudioClip.GetContent(www);
                    lock (cacheLock)
                    {
                        entry.Clip = clip;
                        if (entry.Clip == null || entry.Clip.length <= 0f)
                        {
                            entry.Error = "Loaded AudioClip is invalid.";
                            entry.IsDone = true;
                            entry.IsSuccess = false;
                            totalFailureCount++;
                            if (entry.RefCount == 0) DestroyAndRemoveEntry(entry);
                            return;
                        }

                        entry.Clip.name = System.IO.Path.GetFileNameWithoutExtension(entry.Location);
                        entry.IsSuccess = true;
                        entry.IsDone = true;
                        // Estimate PCM memory: samples * channels * sizeof(float)
                        entry.EstimatedMemoryBytes = (long)entry.Clip.samples * entry.Clip.channels * 4;

                        if (entry.RefCount == 0)
                        {
                            // Clip loaded but nobody is waiting — keep in cache for future use.
                            // The eviction system will clean it up after TTL expires.
                            entry.LastAccessTime = Time.realtimeSinceStartup;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                lock (cacheLock)
                {
                    entry.Error = ex is OperationCanceledException
                        ? "Audio clip load cancelled."
                        : ex.Message;
                    entry.IsDone = true;
                    entry.IsSuccess = false;
                    totalFailureCount++;
                    if (entry.RefCount == 0) DestroyAndRemoveEntry(entry);
                }
            }
        }

        private static void DestroyAndRemoveEntry(CacheEntry entry)
        {
            if (entry == null) return;

            if (entry.Clip != null)
            {
                UnityEngine.Object.Destroy(entry.Clip);
                entry.Clip = null;
            }

            cache.Remove(entry.Location);
            entry.IsDone = true;
            entry.IsSuccess = false;
        }

        public static void ClearCache()
        {
            lock (cacheLock)
            {
                foreach (var pair in cache)
                {
                    if (pair.Value?.Clip != null)
                    {
                        UnityEngine.Object.Destroy(pair.Value.Clip);
                        pair.Value.Clip = null;
                    }
                }

                cache.Clear();
                totalLoadRequests = 0;
                cacheHitCount = 0;
                cacheMissCount = 0;
                totalFailureCount = 0;
            }
        }

        /// <summary>
        /// Returns the total estimated memory (in bytes) of all cached external clips.
        /// </summary>
        public static long GetTotalCachedMemoryBytes()
        {
            lock (cacheLock)
            {
                long total = 0;
                var enumerator = cache.GetEnumerator();
                while (enumerator.MoveNext())
                {
                    if (enumerator.Current.Value.IsSuccess)
                        total += enumerator.Current.Value.EstimatedMemoryBytes;
                }
                enumerator.Dispose();
                return total;
            }
        }

        /// <summary>
        /// Evicts unused (refCount == 0) external clips that exceed the specified TTL.
        /// Uses frequency-weighted scoring: clips accessed more frequently are retained longer.
        /// Returns the number of entries evicted.
        /// </summary>
        /// <param name="maxIdleSeconds">Base TTL in seconds. Clips idle longer than this are candidates.</param>
        /// <param name="memoryBudgetBytes">If total cached memory exceeds this, evict more aggressively (0 = no budget).</param>
        public static int EvictExpiredEntries(float maxIdleSeconds = 30f, long memoryBudgetBytes = 0)
        {
            lock (cacheLock)
            {
                if (cache.Count == 0) return 0;

                float now = Time.realtimeSinceStartup;
                bool overBudget = memoryBudgetBytes > 0 && GetTotalCachedMemoryBytesUnsafe() > memoryBudgetBytes;

                // Collect eviction candidates: refCount == 0, loaded successfully, past TTL
                evictionCandidates.Clear();
                var enumerator = cache.GetEnumerator();
                while (enumerator.MoveNext())
                {
                    CacheEntry entry = enumerator.Current.Value;
                    if (entry.RefCount > 0 || !entry.IsDone || !entry.IsSuccess) continue;

                    float idleTime = now - entry.LastAccessTime;
                    // Frequency-weighted TTL: more accessed clips get proportionally longer TTL
                    float effectiveTTL = maxIdleSeconds * Mathf.Max(1f, entry.AccessCount * 0.5f);

                    if (overBudget)
                        effectiveTTL *= 0.25f; // Aggressively shorten TTL when over budget

                    if (idleTime >= effectiveTTL)
                    {
                        // Score for prioritized eviction: lower = evict first
                        float score = entry.AccessCount / Mathf.Max(idleTime, 0.01f);
                        evictionCandidates.Add((entry, score));
                    }
                }
                enumerator.Dispose();

                if (evictionCandidates.Count == 0) return 0;

                // Sort: lowest score (least valuable) first
                evictionCandidates.Sort((a, b) => a.score.CompareTo(b.score));

                int evictedCount = 0;
                for (int i = 0; i < evictionCandidates.Count; i++)
                {
                    DestroyAndRemoveEntry(evictionCandidates[i].entry);
                    evictedCount++;

                    // If we're under budget again, stop evicting
                    if (memoryBudgetBytes > 0 && GetTotalCachedMemoryBytesUnsafe() <= memoryBudgetBytes)
                        break;
                }

                evictionCandidates.Clear();
                return evictedCount;
            }
        }

        private static long GetTotalCachedMemoryBytesUnsafe()
        {
            long total = 0;
            var enumerator = cache.GetEnumerator();
            while (enumerator.MoveNext())
            {
                if (enumerator.Current.Value.IsSuccess)
                    total += enumerator.Current.Value.EstimatedMemoryBytes;
            }
            enumerator.Dispose();
            return total;
        }

        private static readonly List<(CacheEntry entry, float score)> evictionCandidates = new List<(CacheEntry, float)>(32);

        public static ExternalAudioClipCacheStats GetCacheStats()
        {
            lock (cacheLock)
            {
                int entryCount = cache.Count;
                int loadingCount = 0;
                int loadedCount = 0;
                int failedCount = 0;
                int totalRefCount = 0;

                foreach (var pair in cache)
                {
                    CacheEntry entry = pair.Value;
                    if (entry == null) continue;

                    totalRefCount += entry.RefCount;
                    if (!entry.IsDone) loadingCount++;
                    else if (entry.IsSuccess) loadedCount++;
                    else failedCount++;
                }

                return new ExternalAudioClipCacheStats(
                    entryCount,
                    loadingCount,
                    loadedCount,
                    failedCount,
                    totalRefCount,
                    totalLoadRequests,
                    cacheHitCount,
                    cacheMissCount,
                    totalFailureCount);
            }
        }

        public static void FillCacheEntries(List<ExternalAudioClipCacheEntryInfo> results)
        {
            if (results == null) return;

            lock (cacheLock)
            {
                results.Clear();
                foreach (var pair in cache)
                {
                    CacheEntry entry = pair.Value;
                    if (entry == null) continue;

                    results.Add(new ExternalAudioClipCacheEntryInfo(
                        entry.Location,
                        entry.Clip != null ? entry.Clip.name : string.Empty,
                        entry.IsDone,
                        entry.IsSuccess,
                        entry.RefCount,
                        entry.Error));
                }
            }
        }

        private static AudioType GetAudioType(string path)
        {
            if (string.IsNullOrEmpty(path)) return AudioType.UNKNOWN;

            // Find extension start from the end without allocating a substring
            int dotIndex = path.LastIndexOf('.');
            if (dotIndex < 0 || dotIndex >= path.Length - 1) return AudioType.UNKNOWN;

            int extLen = path.Length - dotIndex - 1;

            if (MatchExtension(path, dotIndex, "mp3")) return AudioType.MPEG;
            if (MatchExtension(path, dotIndex, "wav")) return AudioType.WAV;
            if (MatchExtension(path, dotIndex, "ogg")) return AudioType.OGGVORBIS;
            if (MatchExtension(path, dotIndex, "aiff") || MatchExtension(path, dotIndex, "aif")) return AudioType.AIFF;
#if !UNITY_WEBGL
            if (MatchExtension(path, dotIndex, "m4a") || MatchExtension(path, dotIndex, "mp4") || MatchExtension(path, dotIndex, "aac")) return AudioType.ACC;
#endif
            if (MatchExtension(path, dotIndex, "webm")) return AudioType.OGGVORBIS; // WebM audio uses Vorbis codec
            if (MatchExtension(path, dotIndex, "flac")) return AudioType.UNKNOWN; // Unity does not natively support FLAC via UnityWebRequest

            return AudioType.UNKNOWN;
        }

        private static bool MatchExtension(string path, int dotIndex, string ext)
        {
            int extLen = path.Length - dotIndex - 1;
            if (extLen != ext.Length) return false;

            for (int i = 0; i < ext.Length; i++)
            {
                char c = path[dotIndex + 1 + i];
                if (c >= 'A' && c <= 'Z') c = (char)(c + 32);
                if (c != ext[i]) return false;
            }
            return true;
        }
    }

    /// <summary>
    /// An AudioNode containing a reference to an AudioClip
    /// </summary>
    public class AudioFile : AudioNode
    {
        public enum AudioFileSourceMode
        {
            EmbeddedClip = 0,
            ExternalReference = 1
        }

        /// <summary>
        /// The audio clip to be set on the AudioSource if this node is processed
        /// </summary>
        [SerializeField, FormerlySerializedAs("file")]
        private AudioClip embeddedClip = null;

        [SerializeField]
        private AudioFileSourceMode sourceMode = AudioFileSourceMode.EmbeddedClip;

        [SerializeField]
        private AudioClipReference externalReference = null;

        public AudioClip File
        {
            get { return this.embeddedClip; }
            set
            {
                this.embeddedClip = value;
                if (value != null)
                {
                    this.sourceMode = AudioFileSourceMode.EmbeddedClip;
                    this.externalReference = null;
                }
            }
        }

        public AudioClipReference ExternalReference
        {
            get { return this.externalReference; }
            set
            {
                this.externalReference = value;
                if (value != null)
                {
                    this.sourceMode = AudioFileSourceMode.ExternalReference;
                    this.embeddedClip = null;
                }
            }
        }

        public AudioFileSourceMode SourceMode
        {
            get { return this.sourceMode; }
            set
            {
                if (this.sourceMode == value) return;
                this.sourceMode = value;

                if (value == AudioFileSourceMode.EmbeddedClip)
                {
                    this.externalReference = null;
                }
                else
                {
                    this.embeddedClip = null;
                }
            }
        }

        /// <summary>
        /// The amount of volume change to apply if this node is processed
        /// </summary>
        [SerializeField, Range(-1, 1)]
        private float volumeOffset = 0;
        /// <summary>
        /// The amount of pitch change to apply if this node is processed
        /// </summary>
        [SerializeField, Range(-1, 1)]
        private float pitchOffset = 0;
        /// <summary>
        /// The minimum start position of the node
        /// </summary>
        [Range(0, 1)]
        public float minStartTime = 0;
        /// <summary>
        /// The maximum start position of the node 
        /// </summary>
        [Range(0, 1)]
        public float maxStartTime = 0;
        /// <summary> 
        /// The Start time for the audio file to stay playing at 
        /// </summary>
        public float startTime { get; private set; }

        /// <summary>
        /// Apply all modifications to the ActiveEvent before it gets played
        /// </summary>
        /// <param name="activeEvent">The runtime event being prepared for playback</param>
        public override void ProcessNode(ActiveEvent activeEvent)
        {
            activeEvent.ModulateVolume(this.volumeOffset);
            activeEvent.ModulatePitch(this.pitchOffset);

            AudioFileSourceMode effectiveMode = GetEffectiveSourceMode();

            if (effectiveMode == AudioFileSourceMode.EmbeddedClip && this.embeddedClip != null)
            {
                if (this.embeddedClip.length <= 0)
                {
                    Debug.LogWarningFormat("Invalid file length for node {0}, Event: {1}", this.name, activeEvent.rootEvent.name);
                    return;
                }
                CalculateStartTime(this.embeddedClip);
                activeEvent.AddEventSource(this.embeddedClip, null, null, startTime, AudioClipResolver.CreateEmbedded(this.embeddedClip));
            }
            else if (effectiveMode == AudioFileSourceMode.ExternalReference && this.externalReference != null)
            {
                activeEvent.isAsync = true;
                LoadClipAsync(activeEvent).Forget();
            }
            else
            {
                Debug.LogWarningFormat("No file or path in node {0}, Event: {1}", this.name, activeEvent.rootEvent.name);
            }
        }

        private async UniTaskVoid LoadClipAsync(ActiveEvent activeEvent)
        {
            try
            {
                IAudioClipHandle handle = await AudioClipResolver.LoadExternalAsync(this.externalReference, activeEvent.GetCancellationToken());

                if (handle == null)
                {
                    string errorMessage = this.externalReference != null &&
                                          this.externalReference.LocationKind == AudioLocationKind.AssetAddress
                        ? $"No runtime loader registered for AudioClipReference '{this.externalReference.name}' using AssetAddress mode."
                        : $"Error loading audio reference for node '{this.name}'.";
                    Debug.LogError(errorMessage);
                    activeEvent.StopImmediate();
                    return;
                }

                if (!handle.IsSuccess || handle.Clip == null || handle.Clip.length <= 0f)
                {
                    Debug.LogError($"Error loading audio '{GetDisplayLocation()}': {handle.Error}");
                    handle.Release();
                    activeEvent.StopImmediate();
                    return;
                }

                CalculateStartTime(handle.Clip);
                if (!activeEvent.AddEventSource(handle.Clip, null, null, startTime, handle))
                {
                    handle.Release();
                    return;
                }

                activeEvent.OnAsyncLoadCompleted();
            }
            catch (OperationCanceledException)
            {
                // This is expected if the event is stopped while loading
            }
            catch (Exception e)
            {
                Debug.LogError($"Exception while loading audio clip from path '{GetDisplayLocation()}': {e.Message}");
                activeEvent.StopImmediate();
            }
        }

        /// <summary>
        /// If the min and max start time are not the same, then generate a random value between min and max start time.
        /// </summary>
        /// <returns></returns>
        public void CalculateStartTime(AudioClip clip)
        {
            if (clip == null)
            {
                this.startTime = 0;
                return;
            }

            float startTimeRatio = 0;
            if (this.minStartTime != this.maxStartTime)
            {
                startTimeRatio = UnityEngine.Random.Range(this.minStartTime, this.maxStartTime);
            }
            else
            {
                startTimeRatio = this.minStartTime;
            }

            this.startTime = clip.length * startTimeRatio;
        }

        private string GetDisplayLocation()
        {
            return externalReference != null ? externalReference.ResolveLocation() : string.Empty;
        }

        private AudioFileSourceMode GetEffectiveSourceMode()
        {
            if (sourceMode == AudioFileSourceMode.ExternalReference)
                return AudioFileSourceMode.ExternalReference;

            if (embeddedClip != null)
                return AudioFileSourceMode.EmbeddedClip;

            if (externalReference != null)
                return AudioFileSourceMode.ExternalReference;

            return sourceMode;
        }

#if UNITY_EDITOR

        /// <summary>
        /// The width in pixels for the node's window in the graph
        /// </summary>
        private const float NodeWidth = 300;
        private const float NodeHeight = 130;

        /// <summary>
        /// EDITOR: Initialize the node's properties when it is first created
        /// </summary>
        /// <param name="position">The position of the new node in the graph</param>
        public override void InitializeNode(Vector2 position)
        {
            this.name = "Audio File";
            this.nodeRect.position = position;
            this.nodeRect.width = NodeWidth;
            this.nodeRect.height = NodeHeight;
            AddOutput();
            EditorUtility.SetDirty(this);
        }

        /// <summary>
        /// EDITOR: Display the node's properties in the graph
        /// </summary>
        protected override void DrawProperties()
        {
            AudioFileSourceMode displayedMode = GetEffectiveSourceMode();
            var newMode = (AudioFileSourceMode)EditorGUILayout.EnumPopup("Source", displayedMode);
            if (newMode != this.sourceMode)
            {
                SourceMode = newMode;
            }

            if (newMode == AudioFileSourceMode.EmbeddedClip)
            {
                this.embeddedClip = EditorGUILayout.ObjectField("Audio Clip", this.embeddedClip, typeof(AudioClip), false) as AudioClip;
                this.externalReference = null;

                if (this.embeddedClip != null && this.name != this.embeddedClip.name)
                {
                    this.name = this.embeddedClip.name;
                }
            }
            else
            {
                this.externalReference = EditorGUILayout.ObjectField("Audio Reference", this.externalReference, typeof(AudioClipReference), false) as AudioClipReference;
                this.embeddedClip = null;

                if (this.externalReference != null)
                {
                    EditorGUILayout.LabelField("Location Kind", this.externalReference.LocationKind.ToString());

                    switch (this.externalReference.LocationKind)
                    {
                        case AudioLocationKind.AssetAddress:
                            EditorGUILayout.LabelField("Address / Location", this.externalReference.GetDisplayLocation(), EditorStyles.wordWrappedLabel);
                            if (this.externalReference.HasEditorAssetLink)
                            {
                                EditorGUILayout.LabelField("Editor Asset Link", this.externalReference.GUID, EditorStyles.wordWrappedLabel);
                            }
                            else
                            {
                                EditorGUILayout.HelpBox("No editor asset link stored. Runtime loading depends on the registered AssetAddress loader.", MessageType.Info);
                            }
                            break;
                        case AudioLocationKind.Url:
                            EditorGUILayout.LabelField("URL", this.externalReference.GetDisplayLocation(), EditorStyles.wordWrappedLabel);
                            break;
                        case AudioLocationKind.StreamingAssetsPath:
                            EditorGUILayout.LabelField("StreamingAssets Path", this.externalReference.GetDisplayLocation(), EditorStyles.wordWrappedLabel);
                            EditorGUILayout.LabelField("Resolved Path", this.externalReference.ResolveLocation(), EditorStyles.wordWrappedLabel);
                            break;
                        case AudioLocationKind.PersistentDataPath:
                            EditorGUILayout.LabelField("PersistentData Path", this.externalReference.GetDisplayLocation(), EditorStyles.wordWrappedLabel);
                            EditorGUILayout.LabelField("Resolved Path", this.externalReference.ResolveLocation(), EditorStyles.wordWrappedLabel);
                            break;
                        default:
                            EditorGUILayout.LabelField("File Path", this.externalReference.GetDisplayLocation(), EditorStyles.wordWrappedLabel);
                            break;
                    }
                }
            }
            this.volumeOffset = EditorGUILayout.Slider("Volume Offset", this.volumeOffset, -1, 1);
            this.pitchOffset = EditorGUILayout.Slider("Pitch Offset", this.pitchOffset, -1, 1);
            EditorGUILayout.MinMaxSlider("Start Time", ref this.minStartTime, ref this.maxStartTime, 0, 1);
        }

#endif
    }
}
