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

        private readonly struct LoaderRegistration
        {
            public readonly long Id;
            public readonly AudioClipReferenceLoader Loader;

            public LoaderRegistration(long id, AudioClipReferenceLoader loader)
            {
                Id = id;
                Loader = loader;
            }
        }

        private static readonly Dictionary<int, List<LoaderRegistration>> runtimeReferenceLoaders = new Dictionary<int, List<LoaderRegistration>>();
        private static readonly Dictionary<AudioLocationKind, List<LoaderRegistration>> runtimeLocationKindLoaders = new Dictionary<AudioLocationKind, List<LoaderRegistration>>();
        private static readonly object runtimeReferenceLoaderLock = new object();
        private static readonly List<IAudioClipProvider> providers = new List<IAudioClipProvider>();
        private static readonly Dictionary<IAudioClipProvider, int> providerRegistrationCounts = new Dictionary<IAudioClipProvider, int>();
        private static readonly object providerLock = new object();
        private static readonly IAudioClipProvider referenceLoaderProvider = new ReferenceLoaderAudioClipProvider();
        private static readonly IAudioClipProvider locationKindLoaderProvider = new LocationKindAudioClipProvider();
        private static readonly IAudioClipProvider builtInExternalProvider = new BuiltInExternalAudioClipProvider();
        private static IAudioClipProvider[] cachedProviderSnapshot;
        private static bool providerSnapshotDirty = true;
        private static long nextLoaderRegistrationId;

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
            AudioRuntimeThreadGuard.EnsureMainThread(nameof(AudioClipResolver) + ".LoadExternalAsync");
            if (reference == null || string.IsNullOrWhiteSpace(reference.Location))
                return null;

            IAudioClipProvider[] providerSnapshot = GetProviderSnapshot();
            for (int i = 0; i < providerSnapshot.Length; i++)
            {
                IAudioClipProvider provider = providerSnapshot[i];
                if (provider == null || !provider.CanLoad(reference))
                    continue;

                IAudioClipHandle handle;
                try
                {
                    handle = await provider.LoadAsync(reference, cancellationToken);
                }
                catch
                {
                    await UniTask.SwitchToMainThread();
                    throw;
                }

                await UniTask.SwitchToMainThread();
                if (handle != null)
                    return handle;
            }

            return null;
        }

        public static void ClearExternalCache()
        {
            AudioRuntimeThreadGuard.EnsureMainThread(nameof(AudioClipResolver) + ".ClearExternalCache");
            ExternalAudioClipHandle.ClearCache();
        }

        [Obsolete("Use RegisterReferenceLoaderScoped and dispose the returned registration lease.")]
        public static void RegisterReferenceLoader(AudioClipReference reference, AudioClipReferenceLoader loader)
        {
            RegisterReferenceLoaderScoped(reference, loader);
        }

        public static IDisposable RegisterReferenceLoaderScoped(
            AudioClipReference reference,
            AudioClipReferenceLoader loader)
        {
            AudioRuntimeThreadGuard.EnsureMainThread(nameof(AudioClipResolver) + ".RegisterReferenceLoaderScoped");
            if (reference == null || loader == null) return EmptyRegistration.Instance;

            long registrationId = GetNextLoaderRegistrationId();
            int referenceId = reference.GetInstanceID();
            lock (runtimeReferenceLoaderLock)
            {
                if (!runtimeReferenceLoaders.TryGetValue(referenceId, out List<LoaderRegistration> registrations))
                {
                    registrations = new List<LoaderRegistration>(2);
                    runtimeReferenceLoaders.Add(referenceId, registrations);
                }

                registrations.Add(new LoaderRegistration(registrationId, loader));
            }

            return new ResolverRegistration(() => UnregisterReferenceLoader(referenceId, registrationId));
        }

        [Obsolete("Use RegisterManagedReferenceLoaderScoped and dispose the returned registration lease.")]
        public static void RegisterManagedReferenceLoader(
            AudioClipReference reference,
            ManagedAudioClipReferenceLoader loader)
        {
            RegisterManagedReferenceLoaderScoped(reference, loader);
        }

        public static IDisposable RegisterManagedReferenceLoaderScoped(
            AudioClipReference reference,
            ManagedAudioClipReferenceLoader loader)
        {
            if (reference == null || loader == null) return EmptyRegistration.Instance;

            return RegisterReferenceLoaderScoped(reference, async (clipReference, cancellationToken) =>
            {
                ManagedAudioClipLoadResult result = await loader(clipReference, cancellationToken);
                await UniTask.SwitchToMainThread();
                if (!result.IsValid)
                    return null;

                return CreateManaged(result.Clip, result.ReleaseAction);
            });
        }

        [Obsolete("Use RegisterLocationKindLoaderScoped and dispose the returned registration lease.")]
        public static void RegisterLocationKindLoader(AudioLocationKind locationKind, AudioClipReferenceLoader loader)
        {
            RegisterLocationKindLoaderScoped(locationKind, loader);
        }

        public static IDisposable RegisterLocationKindLoaderScoped(
            AudioLocationKind locationKind,
            AudioClipReferenceLoader loader)
        {
            AudioRuntimeThreadGuard.EnsureMainThread(nameof(AudioClipResolver) + ".RegisterLocationKindLoaderScoped");
            if (loader == null) return EmptyRegistration.Instance;

            long registrationId = GetNextLoaderRegistrationId();
            lock (runtimeReferenceLoaderLock)
            {
                if (!runtimeLocationKindLoaders.TryGetValue(locationKind, out List<LoaderRegistration> registrations))
                {
                    registrations = new List<LoaderRegistration>(2);
                    runtimeLocationKindLoaders.Add(locationKind, registrations);
                }

                registrations.Add(new LoaderRegistration(registrationId, loader));
            }

            return new ResolverRegistration(() => UnregisterLocationKindLoader(locationKind, registrationId));
        }

        [Obsolete("Use RegisterManagedLocationKindLoaderScoped and dispose the returned registration lease.")]
        public static void RegisterManagedLocationKindLoader(
            AudioLocationKind locationKind,
            ManagedAudioClipReferenceLoader loader)
        {
            RegisterManagedLocationKindLoaderScoped(locationKind, loader);
        }

        public static IDisposable RegisterManagedLocationKindLoaderScoped(
            AudioLocationKind locationKind,
            ManagedAudioClipReferenceLoader loader)
        {
            if (loader == null) return EmptyRegistration.Instance;

            return RegisterLocationKindLoaderScoped(locationKind, async (clipReference, cancellationToken) =>
            {
                ManagedAudioClipLoadResult result = await loader(clipReference, cancellationToken);
                await UniTask.SwitchToMainThread();
                if (!result.IsValid)
                    return null;

                return CreateManaged(result.Clip, result.ReleaseAction);
            });
        }

        public static void UnregisterReferenceLoader(AudioClipReference reference)
        {
            if (reference == null) return;
            AudioRuntimeThreadGuard.EnsureMainThread(nameof(AudioClipResolver) + ".UnregisterReferenceLoader");

            lock (runtimeReferenceLoaderLock)
            {
                runtimeReferenceLoaders.Remove(reference.GetInstanceID());
            }
        }

        public static void UnregisterLocationKindLoader(AudioLocationKind locationKind)
        {
            AudioRuntimeThreadGuard.EnsureMainThread(nameof(AudioClipResolver) + ".UnregisterLocationKindLoader");
            lock (runtimeReferenceLoaderLock)
            {
                runtimeLocationKindLoaders.Remove(locationKind);
            }
        }

        public static void ClearReferenceLoaders()
        {
            AudioRuntimeThreadGuard.EnsureMainThread(nameof(AudioClipResolver) + ".ClearReferenceLoaders");
            lock (runtimeReferenceLoaderLock)
            {
                runtimeReferenceLoaders.Clear();
                runtimeLocationKindLoaders.Clear();
            }
        }

        [Obsolete("Use RegisterProviderScoped and dispose the returned registration lease.")]
        public static void RegisterProvider(IAudioClipProvider provider)
        {
            RegisterProviderScoped(provider);
        }

        public static IDisposable RegisterProviderScoped(IAudioClipProvider provider)
        {
            AudioRuntimeThreadGuard.EnsureMainThread(nameof(AudioClipResolver) + ".RegisterProviderScoped");
            if (provider == null) return EmptyRegistration.Instance;

            lock (providerLock)
            {
                if (IsBuiltInProvider(provider))
                    return EmptyRegistration.Instance;

                providerRegistrationCounts.TryGetValue(provider, out int registrationCount);
                providerRegistrationCounts[provider] = registrationCount + 1;
                if (registrationCount == 0)
                {
                    RegisterProviderInternal(provider);
                }
                providerSnapshotDirty = true;
            }

            return new ResolverRegistration(() => ReleaseProviderRegistration(provider));
        }

        public static void UnregisterProvider(IAudioClipProvider provider)
        {
            if (provider == null) return;
            AudioRuntimeThreadGuard.EnsureMainThread(nameof(AudioClipResolver) + ".UnregisterProvider");

            lock (providerLock)
            {
                providers.Remove(provider);
                providerRegistrationCounts.Remove(provider);
                providerSnapshotDirty = true;
            }
        }

        public static void ClearCustomProviders()
        {
            AudioRuntimeThreadGuard.EnsureMainThread(nameof(AudioClipResolver) + ".ClearCustomProviders");
            lock (providerLock)
            {
                providers.Clear();
                providerRegistrationCounts.Clear();
                RegisterProviderInternal(referenceLoaderProvider);
                RegisterProviderInternal(locationKindLoaderProvider);
                RegisterProviderInternal(builtInExternalProvider);
                providerSnapshotDirty = true;
            }
        }

        public static void GetProviders(List<IAudioClipProvider> results)
        {
            if (results == null) return;
            AudioRuntimeThreadGuard.EnsureMainThread(nameof(AudioClipResolver) + ".GetProviders");

            lock (providerLock)
            {
                results.Clear();
                results.AddRange(providers);
            }
        }

        public static ExternalAudioClipCacheStats GetExternalCacheStats()
        {
            AudioRuntimeThreadGuard.EnsureMainThread(nameof(AudioClipResolver) + ".GetExternalCacheStats");
            return ExternalAudioClipHandle.GetCacheStats();
        }

        public static void GetExternalCacheEntries(List<ExternalAudioClipCacheEntryInfo> results)
        {
            AudioRuntimeThreadGuard.EnsureMainThread(nameof(AudioClipResolver) + ".GetExternalCacheEntries");
            if (results == null) return;
            ExternalAudioClipHandle.FillCacheEntries(results);
        }

        internal static string GetDiagnosticLocation(string location)
        {
            if (string.IsNullOrWhiteSpace(location))
                return string.Empty;

            if (Uri.TryCreate(location, UriKind.Absolute, out Uri uri) && !string.IsNullOrEmpty(uri.Host))
            {
                string authority = uri.Scheme + "://" + uri.IdnHost;
                if (!uri.IsDefaultPort)
                    authority += ":" + uri.Port;
                return authority + "/<redacted>";
            }

            int separatorIndex = Math.Max(location.LastIndexOf('/'), location.LastIndexOf('\\'));
            string leafName = separatorIndex >= 0 && separatorIndex < location.Length - 1
                ? location.Substring(separatorIndex + 1)
                : string.Empty;
            return string.IsNullOrEmpty(leafName)
                ? "<redacted>"
                : "<redacted>/" + leafName;
        }

        private static AudioClipReferenceLoader GetRegisteredLoader(AudioClipReference reference)
        {
            if (reference == null) return null;

            lock (runtimeReferenceLoaderLock)
            {
                if (!runtimeReferenceLoaders.TryGetValue(reference.GetInstanceID(), out List<LoaderRegistration> registrations) ||
                    registrations.Count == 0)
                {
                    return null;
                }

                return registrations[registrations.Count - 1].Loader;
            }
        }

        private static AudioClipReferenceLoader GetRegisteredLocationKindLoader(AudioLocationKind locationKind)
        {
            lock (runtimeReferenceLoaderLock)
            {
                if (!runtimeLocationKindLoaders.TryGetValue(locationKind, out List<LoaderRegistration> registrations) ||
                    registrations.Count == 0)
                {
                    return null;
                }

                return registrations[registrations.Count - 1].Loader;
            }
        }

        private static long GetNextLoaderRegistrationId()
        {
            long registrationId = Interlocked.Increment(ref nextLoaderRegistrationId);
            return registrationId != 0
                ? registrationId
                : Interlocked.Increment(ref nextLoaderRegistrationId);
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

        private static void UnregisterReferenceLoader(int referenceId, long expectedRegistrationId)
        {
            AudioRuntimeThreadGuard.EnsureMainThread(nameof(AudioClipResolver) + ".DisposeReferenceRegistration");
            lock (runtimeReferenceLoaderLock)
            {
                if (!runtimeReferenceLoaders.TryGetValue(referenceId, out List<LoaderRegistration> registrations))
                    return;

                for (int i = registrations.Count - 1; i >= 0; i--)
                {
                    if (registrations[i].Id != expectedRegistrationId)
                        continue;

                    registrations.RemoveAt(i);
                    if (registrations.Count == 0)
                        runtimeReferenceLoaders.Remove(referenceId);
                    return;
                }
            }
        }

        private static void UnregisterLocationKindLoader(
            AudioLocationKind locationKind,
            long expectedRegistrationId)
        {
            AudioRuntimeThreadGuard.EnsureMainThread(nameof(AudioClipResolver) + ".DisposeLocationRegistration");
            lock (runtimeReferenceLoaderLock)
            {
                if (!runtimeLocationKindLoaders.TryGetValue(locationKind, out List<LoaderRegistration> registrations))
                    return;

                for (int i = registrations.Count - 1; i >= 0; i--)
                {
                    if (registrations[i].Id != expectedRegistrationId)
                        continue;

                    registrations.RemoveAt(i);
                    if (registrations.Count == 0)
                        runtimeLocationKindLoaders.Remove(locationKind);
                    return;
                }
            }
        }

        private static bool IsBuiltInProvider(IAudioClipProvider provider)
        {
            return ReferenceEquals(provider, referenceLoaderProvider)
                || ReferenceEquals(provider, locationKindLoaderProvider)
                || ReferenceEquals(provider, builtInExternalProvider);
        }

        private static void ReleaseProviderRegistration(IAudioClipProvider provider)
        {
            AudioRuntimeThreadGuard.EnsureMainThread(nameof(AudioClipResolver) + ".DisposeProviderRegistration");
            lock (providerLock)
            {
                if (!providerRegistrationCounts.TryGetValue(provider, out int registrationCount))
                    return;

                if (registrationCount > 1)
                {
                    providerRegistrationCounts[provider] = registrationCount - 1;
                    return;
                }

                providerRegistrationCounts.Remove(provider);
                providers.Remove(provider);
                providerSnapshotDirty = true;
            }
        }

        private sealed class ResolverRegistration : IDisposable
        {
            private Action unregister;

            public ResolverRegistration(Action unregisterAction)
            {
                unregister = unregisterAction;
            }

            public void Dispose()
            {
                AudioRuntimeThreadGuard.EnsureMainThread(nameof(AudioClipResolver) + ".Registration.Dispose");
                Action action = unregister;
                if (action == null) return;
                unregister = null;
                action();
            }
        }

        private sealed class EmptyRegistration : IDisposable
        {
            public static readonly EmptyRegistration Instance = new EmptyRegistration();
            public void Dispose() { }
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

                return await ExternalAudioClipHandle.LoadAsync(reference, cancellationToken);
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
            AudioRuntimeThreadGuard.EnsureMainThread(nameof(EmbeddedAudioClipHandle) + ".Retain");
            if (clip == null || refCount <= 0) return;
            refCount++;
        }

        public void Release()
        {
            AudioRuntimeThreadGuard.EnsureMainThread(nameof(EmbeddedAudioClipHandle) + ".Release");
            if (refCount <= 0) return;
            refCount--;
            if (refCount == 0) clip = null;
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
            AudioRuntimeThreadGuard.EnsureMainThread(nameof(ManagedAudioClipHandle) + ".Retain");
            if (clip == null || disposed != 0 || refCount <= 0) return;
            refCount++;
        }

        public void Release()
        {
            AudioRuntimeThreadGuard.EnsureMainThread(nameof(ManagedAudioClipHandle) + ".Release");
            if (disposed != 0 || refCount <= 0) return;
            refCount--;
            if (refCount > 0) return;

            disposed = 1;

            try
            {
                releaseAction?.Invoke();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
            finally
            {
                releaseAction = null;
                clip = null;
            }
        }

        public void Dispose() => Release();
    }

    internal readonly struct AudioClipCacheKey : IEquatable<AudioClipCacheKey>
    {
        public readonly AudioLocationKind LocationKind;
        public readonly string Location;
        public readonly int Version;

        public AudioClipCacheKey(AudioClipReference reference)
        {
            LocationKind = reference != null ? reference.LocationKind : default;
            Location = reference != null ? reference.Location ?? string.Empty : string.Empty;
            Version = reference != null ? reference.Version : 0;
        }

        public bool Equals(AudioClipCacheKey other)
        {
            return LocationKind == other.LocationKind &&
                   Version == other.Version &&
                   string.Equals(Location, other.Location, StringComparison.Ordinal);
        }

        public override bool Equals(object obj) => obj is AudioClipCacheKey other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = (int)LocationKind;
                hash = (hash * 397) ^ Version;
                hash = (hash * 397) ^ (Location != null ? StringComparer.Ordinal.GetHashCode(Location) : 0);
                return hash;
            }
        }
    }

    internal sealed class ExternalAudioClipHandle : IAudioClipHandle
    {
        private sealed class CacheEntry
        {
            public readonly AudioClipCacheKey Key;
            public readonly string Location;
            public AudioClip Clip;
            public string Error = string.Empty;
            public bool IsDone;
            public bool IsSuccess;
            public int RefCount;
            public UniTask LoadTask;
            public bool LoadStarted;
            public bool RemoveWhenUnused;
            public UnityWebRequest Request;

            // Memory budget & eviction tracking
            public int AccessCount;
            public float LastAccessTime;
            public long EstimatedMemoryBytes;

            public CacheEntry(AudioClipCacheKey key, string location)
            {
                Key = key;
                Location = location;
                LastAccessTime = Time.realtimeSinceStartup;
            }
        }

        private static readonly object cacheLock = new object();
        private static readonly Dictionary<AudioClipCacheKey, CacheEntry> cache = new Dictionary<AudioClipCacheKey, CacheEntry>();
        private static int totalLoadRequests;
        private static int cacheHitCount;
        private static int cacheMissCount;
        private static int totalFailureCount;

        private CacheEntry entry;
        private int localRefCount;

        private ExternalAudioClipHandle(CacheEntry entry)
        {
            this.entry = entry;
            localRefCount = 1;
        }

        public bool IsDone => entry != null && entry.IsDone;
        public bool IsSuccess => entry != null && entry.IsSuccess;
        public AudioClip Clip => entry?.Clip;
        public string Error => entry?.Error ?? "Audio clip handle released.";
        public int RefCount => localRefCount;

        public static async UniTask<IAudioClipHandle> LoadAsync(AudioClipReference reference, CancellationToken cancellationToken)
        {
            AudioRuntimeThreadGuard.EnsureMainThread(nameof(ExternalAudioClipHandle) + ".LoadAsync");
            if (reference == null || !reference.TryResolveLocation(out string location, out _))
                return null;
            if (string.IsNullOrWhiteSpace(location))
                return null;

            AudioClipCacheKey key = new AudioClipCacheKey(reference);
            CacheEntry entry = AcquireEntry(key, location);
            var handle = new ExternalAudioClipHandle(entry);

            try
            {
                await entry.LoadTask.AttachExternalCancellation(cancellationToken);
                await UniTask.SwitchToMainThread();
                return handle;
            }
            catch
            {
                await UniTask.SwitchToMainThread();
                AudioClipHandleRelease.Safe(handle);
                throw;
            }
        }

        public void Retain()
        {
            AudioRuntimeThreadGuard.EnsureMainThread(nameof(ExternalAudioClipHandle) + ".Retain");
            if (localRefCount <= 0 || entry == null) return;

            lock (cacheLock)
            {
                if (localRefCount <= 0 || entry == null) return;
                localRefCount++;
                entry.RefCount++;
            }
        }

        public void Release()
        {
            AudioRuntimeThreadGuard.EnsureMainThread(nameof(ExternalAudioClipHandle) + ".Release");
            if (localRefCount <= 0 || entry == null) return;

            lock (cacheLock)
            {
                if (localRefCount <= 0 || entry == null) return;

                localRefCount--;
                entry.RefCount = Mathf.Max(0, entry.RefCount - 1);

                if (entry.RefCount == 0)
                {
                    bool retainUnused = AudioManager.ExternalClipMemoryBudgetBytes > 0;
                    if (!retainUnused)
                    {
                        entry.RemoveWhenUnused = true;
                    }

                    // Failed, explicitly cleared, or non-retained entries leave the cache with their last lease.
                    if (entry.IsDone && (!entry.IsSuccess || entry.RemoveWhenUnused))
                    {
                        DestroyAndRemoveEntry(entry);
                    }
                    else if (!entry.IsDone && entry.RemoveWhenUnused)
                    {
                        entry.Request?.Abort();
                    }
                    else
                    {
                        entry.LastAccessTime = Time.realtimeSinceStartup;
                    }
                }

                if (localRefCount == 0)
                {
                    entry = null;
                }
            }
        }

        public void Dispose() => Release();

        private static CacheEntry AcquireEntry(AudioClipCacheKey key, string location)
        {
            lock (cacheLock)
            {
                totalLoadRequests++;
                if (!cache.TryGetValue(key, out CacheEntry entry))
                {
                    cacheMissCount++;
                    entry = new CacheEntry(key, location);
                    entry.RefCount = 1;
                    entry.LoadStarted = true;
                    entry.LoadTask = LoadEntryAsync(entry).Preserve();
                    cache[key] = entry;
                    return entry;
                }

                cacheHitCount++;
                entry.RefCount++;
                entry.AccessCount++;
                entry.LastAccessTime = Time.realtimeSinceStartup;
                if (!entry.LoadStarted)
                {
                    entry.LoadStarted = true;
                    entry.LoadTask = LoadEntryAsync(entry).Preserve();
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
                    www.timeout = Mathf.Max(1, AudioManager.ExternalClipRequestTimeoutSeconds);
                    lock (cacheLock)
                    {
                        entry.Request = www;
                    }

                    UnityWebRequestAsyncOperation operation = www.SendWebRequest();
                    bool downloadLimitExceeded = false;
                    ulong downloadLimit = AudioManager.ExternalClipMaxDownloadBytes;
                    while (!operation.isDone)
                    {
                        if (downloadLimit > 0 && www.downloadedBytes > downloadLimit)
                        {
                            downloadLimitExceeded = true;
                            www.Abort();
                            break;
                        }

                        await UniTask.Yield(PlayerLoopTiming.Update);
                    }

                    if (!operation.isDone)
                        await operation;

                    if (downloadLimit > 0 && www.downloadedBytes > downloadLimit)
                    {
                        downloadLimitExceeded = true;
                    }

                    if (downloadLimitExceeded)
                    {
                        lock (cacheLock)
                        {
                            entry.Error = $"External audio download exceeds the {downloadLimit}-byte limit.";
                            entry.IsDone = true;
                            entry.IsSuccess = false;
                            entry.Request = null;
                            totalFailureCount++;
                            if (entry.RefCount == 0) DestroyAndRemoveEntry(entry);
                        }
                        return;
                    }

                    if (www.result != UnityWebRequest.Result.Success)
                    {
                        lock (cacheLock)
                        {
                            entry.Error = $"External audio request failed ({www.result}).";
                            entry.IsDone = true;
                            entry.IsSuccess = false;
                            entry.Request = null;
                            totalFailureCount++;
                            if (entry.RefCount == 0) DestroyAndRemoveEntry(entry);
                        }
                        return;
                    }

                    AudioClip clip = DownloadHandlerAudioClip.GetContent(www);
                    lock (cacheLock)
                    {
                        entry.Clip = clip;
                        entry.Request = null;
                        if (entry.Clip == null || entry.Clip.length <= 0f)
                        {
                            entry.Error = "Loaded AudioClip is invalid.";
                            entry.IsDone = true;
                            entry.IsSuccess = false;
                            totalFailureCount++;
                            if (entry.RefCount == 0) DestroyAndRemoveEntry(entry);
                            return;
                        }

                        entry.Clip.name = GetSafeExternalClipName(entry.Location);
                        // Estimate PCM memory: samples * channels * sizeof(float)
                        entry.EstimatedMemoryBytes = (long)entry.Clip.samples * entry.Clip.channels * 4;
                        long decodedLimit = AudioManager.ExternalClipMaxDecodedBytes;
                        if (decodedLimit > 0 && entry.EstimatedMemoryBytes > decodedLimit)
                        {
                            entry.Error = $"Decoded AudioClip exceeds the {decodedLimit}-byte limit.";
                            UnityEngine.Object.Destroy(entry.Clip);
                            entry.Clip = null;
                            entry.IsDone = true;
                            entry.IsSuccess = false;
                            totalFailureCount++;
                            if (entry.RefCount == 0) DestroyAndRemoveEntry(entry);
                            return;
                        }

                        entry.IsSuccess = true;
                        entry.IsDone = true;

                        if (entry.RefCount == 0)
                        {
                            // Clip loaded after its final waiter left. Retain only when policy allows it.
                            if (entry.RemoveWhenUnused)
                            {
                                DestroyAndRemoveEntry(entry);
                            }
                            else
                            {
                                entry.LastAccessTime = Time.realtimeSinceStartup;
                            }
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
                        : $"External audio load failed with {ex.GetType().Name}.";
                    entry.IsDone = true;
                    entry.IsSuccess = false;
                    entry.Request = null;
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

            cache.Remove(entry.Key);
            entry.IsDone = true;
            entry.IsSuccess = false;
        }

        public static void ClearCache()
        {
            AudioRuntimeThreadGuard.EnsureMainThread(nameof(ExternalAudioClipHandle) + ".ClearCache");
            lock (cacheLock)
            {
                evictionCandidates.Clear();
                foreach (var pair in cache)
                {
                    CacheEntry entry = pair.Value;
                    if (entry == null) continue;

                    entry.RemoveWhenUnused = true;
                    if (entry.RefCount == 0 && entry.IsDone)
                    {
                        evictionCandidates.Add((entry, 0f));
                    }
                    else if (entry.RefCount == 0)
                    {
                        entry.Request?.Abort();
                    }
                }

                for (int i = 0; i < evictionCandidates.Count; i++)
                    DestroyAndRemoveEntry(evictionCandidates[i].entry);
                evictionCandidates.Clear();

                if (cache.Count == 0)
                {
                    totalLoadRequests = 0;
                    cacheHitCount = 0;
                    cacheMissCount = 0;
                    totalFailureCount = 0;
                }
            }
        }

        private static string GetSafeExternalClipName(string location)
        {
            const string fallbackName = "ExternalAudioClip";
            const int maxNameLength = 128;
            if (string.IsNullOrEmpty(location)) return fallbackName;

            string pathOnly = location;
            if (Uri.TryCreate(location, UriKind.Absolute, out Uri absoluteUri))
                pathOnly = absoluteUri.AbsolutePath;
            else
            {
                int queryIndex = pathOnly.IndexOfAny(new[] { '?', '#' });
                if (queryIndex >= 0) pathOnly = pathOnly.Substring(0, queryIndex);
            }

            pathOnly = pathOnly.Replace('\\', '/');
            int slashIndex = pathOnly.LastIndexOf('/');
            string fileName = slashIndex >= 0 ? pathOnly.Substring(slashIndex + 1) : pathOnly;
            int extensionIndex = fileName.LastIndexOf('.');
            if (extensionIndex > 0) fileName = fileName.Substring(0, extensionIndex);
            if (string.IsNullOrWhiteSpace(fileName)) return fallbackName;
            return fileName.Length <= maxNameLength ? fileName : fileName.Substring(0, maxNameLength);
        }

        /// <summary>
        /// Returns the total estimated memory (in bytes) of all cached external clips.
        /// </summary>
        public static long GetTotalCachedMemoryBytes()
        {
            AudioRuntimeThreadGuard.EnsureMainThread(nameof(ExternalAudioClipHandle) + ".GetTotalCachedMemoryBytes");
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
        /// <param name="memoryBudgetBytes">Unused residency budget. Zero disables unused cache residency.</param>
        public static int EvictExpiredEntries(float maxIdleSeconds = 30f, long memoryBudgetBytes = 0)
        {
            AudioRuntimeThreadGuard.EnsureMainThread(nameof(ExternalAudioClipHandle) + ".EvictExpiredEntries");
            lock (cacheLock)
            {
                if (cache.Count == 0) return 0;

                float now = Time.realtimeSinceStartup;
                bool cacheDisabled = memoryBudgetBytes <= 0;
                bool overBudget = !cacheDisabled && GetTotalCachedMemoryBytesUnsafe() > memoryBudgetBytes;

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

                    if (cacheDisabled || overBudget || idleTime >= effectiveTTL)
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
            AudioRuntimeThreadGuard.EnsureMainThread(nameof(ExternalAudioClipHandle) + ".GetCacheStats");
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
            AudioRuntimeThreadGuard.EnsureMainThread(nameof(ExternalAudioClipHandle) + ".FillCacheEntries");

            lock (cacheLock)
            {
                results.Clear();
                foreach (var pair in cache)
                {
                    CacheEntry entry = pair.Value;
                    if (entry == null) continue;

                    results.Add(new ExternalAudioClipCacheEntryInfo(
                        AudioClipResolver.GetDiagnosticLocation(entry.Location),
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

            // Signed URLs commonly append query/fragment data. Inspect only the path portion and
            // avoid allocating a temporary Uri or extension string on the load path.
            int extensionEnd = path.Length;
            bool isUriLike = path.IndexOf("://", StringComparison.Ordinal) >= 0 ||
                             path.StartsWith("jar:", StringComparison.OrdinalIgnoreCase);
            if (isUriLike)
            {
                int queryIndex = path.IndexOf('?');
                if (queryIndex >= 0) extensionEnd = queryIndex;
                int fragmentIndex = path.IndexOf('#');
                if (fragmentIndex >= 0 && fragmentIndex < extensionEnd) extensionEnd = fragmentIndex;
            }
            if (extensionEnd <= 0) return AudioType.UNKNOWN;

            int dotIndex = path.LastIndexOf('.', extensionEnd - 1, extensionEnd);
            if (dotIndex < 0 || dotIndex >= extensionEnd - 1) return AudioType.UNKNOWN;

            if (MatchExtension(path, dotIndex, extensionEnd, "mp3")) return AudioType.MPEG;
            if (MatchExtension(path, dotIndex, extensionEnd, "wav")) return AudioType.WAV;
            if (MatchExtension(path, dotIndex, extensionEnd, "ogg")) return AudioType.OGGVORBIS;
            if (MatchExtension(path, dotIndex, extensionEnd, "aiff") || MatchExtension(path, dotIndex, extensionEnd, "aif")) return AudioType.AIFF;
#if !UNITY_WEBGL
            if (MatchExtension(path, dotIndex, extensionEnd, "m4a") || MatchExtension(path, dotIndex, extensionEnd, "mp4") || MatchExtension(path, dotIndex, extensionEnd, "aac")) return AudioType.ACC;
#endif
            if (MatchExtension(path, dotIndex, extensionEnd, "webm")) return AudioType.OGGVORBIS; // WebM audio uses Vorbis codec
            if (MatchExtension(path, dotIndex, extensionEnd, "flac")) return AudioType.UNKNOWN; // Unity does not natively support FLAC via UnityWebRequest

            return AudioType.UNKNOWN;
        }

        private static bool MatchExtension(string path, int dotIndex, int extensionEnd, string ext)
        {
            int extLen = extensionEnd - dotIndex - 1;
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
                float selectedStartTime = SelectStartTime(this.embeddedClip);
                activeEvent.AddEventSource(this.embeddedClip, null, null, selectedStartTime, AudioClipResolver.CreateEmbedded(this.embeddedClip));
            }
            else if (effectiveMode == AudioFileSourceMode.ExternalReference && this.externalReference != null)
            {
                AudioEventPreparation preparation = activeEvent.BeginAsyncPreparation();
                if (preparation != null)
                    LoadClipAsync(preparation).Forget();
            }
            else
            {
                Debug.LogWarningFormat("No file or path in node {0}, Event: {1}", this.name, activeEvent.rootEvent.name);
            }
        }

        private async UniTaskVoid LoadClipAsync(AudioEventPreparation preparation)
        {
            IAudioClipHandle handle = null;
            bool succeeded = false;
            try
            {
                handle = await AudioClipResolver.LoadExternalAsync(
                    this.externalReference,
                    preparation.CancellationToken);

                if (handle == null)
                {
                    string errorMessage = this.externalReference != null &&
                                          this.externalReference.LocationKind == AudioLocationKind.AssetAddress
                        ? $"No runtime loader registered for AudioClipReference '{this.externalReference.name}' using AssetAddress mode."
                        : $"Error loading audio reference for node '{this.name}'.";
                    Debug.LogError(errorMessage);
                    return;
                }

                if (!handle.IsSuccess || handle.Clip == null || handle.Clip.length <= 0f)
                {
                    Debug.LogError(
                        $"Audio reference '{this.externalReference.name}' ({this.externalReference.LocationKind}) failed to load.");
                    AudioClipHandleRelease.Safe(handle);
                    handle = null;
                    return;
                }

                float selectedStartTime = SelectStartTime(handle.Clip);
                bool sourceAccepted = preparation.TryAddSource(
                    handle.Clip,
                    null,
                    null,
                    selectedStartTime,
                    handle);
                handle = null;
                if (!sourceAccepted)
                {
                    return;
                }

                succeeded = true;
            }
            catch (OperationCanceledException)
            {
                // This is expected if the event is stopped while loading
            }
            catch (Exception e)
            {
                string referenceName = this.externalReference != null ? this.externalReference.name : "<missing>";
                Debug.LogError(
                    $"Audio reference '{referenceName}' failed with {e.GetType().Name}. Location details are omitted from logs.");
            }
            finally
            {
                try
                {
                    AudioClipHandleRelease.Safe(handle);
                }
                finally
                {
                    preparation.Complete(succeeded);
                }
            }
        }

        /// <summary>
        /// If the min and max start time are not the same, then generate a random value between min and max start time.
        /// </summary>
        /// <returns></returns>
        public void CalculateStartTime(AudioClip clip)
        {
            SelectStartTime(clip);
        }

        private float SelectStartTime(AudioClip clip)
        {
            if (clip == null)
            {
                this.startTime = 0;
                return 0f;
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
            return this.startTime;
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

        internal bool TryGetExternalReference(out AudioClipReference reference)
        {
            reference = GetEffectiveSourceMode() == AudioFileSourceMode.ExternalReference
                ? externalReference
                : null;
            return reference != null;
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
