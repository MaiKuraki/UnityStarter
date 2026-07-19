using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine.SceneManagement;

namespace CycloneGames.AssetManagement.Runtime
{
    /// <summary>
    /// Lightweight runtime registry for scene-handle diagnostics.
    /// This is intentionally provider-agnostic and contains no gameplay assumptions.
    /// </summary>
    public static class SceneTracker
    {
        public const int DEFAULT_CAPACITY = 4_096;
        public const int MAX_CAPACITY = 16_384;

        public struct SceneInfo
        {
            public long Id;
            public string PackageName;
            public string ProviderType;
            public string SceneLocation;
            public string ScenePath;
            public string RuntimeSceneName;
            public string Bucket;
            public LoadSceneMode LoadMode;
            public LocalPhysicsMode LocalPhysicsMode;
            public SceneActivationMode ActivationMode;
            public SceneActivationState ActivationState;
            public bool SupportsManualActivation;
            public bool RuntimeSceneLoaded;
            public bool IsDone;
            public bool UnloadRequested;
            public float Progress;
            public int RefCount;
            public DateTime RegistrationTimeUtc;
            public DateTime? UnloadRequestedTimeUtc;
            internal long RegistrationTimestamp;
            internal long UnloadRequestedTimestamp;
            public string Error;
        }

        private sealed class SceneEntry
        {
            public long Id;
            public string PackageName;
            public string ProviderType;
            public string SceneLocation;
            public string Bucket;
            public LoadSceneMode LoadMode;
            public LocalPhysicsMode LocalPhysicsMode;
            public DateTime RegistrationTimeUtc;
            public DateTime? UnloadRequestedTimeUtc;
            public long RegistrationTimestamp;
            public long UnloadRequestedTimestamp;
            public string LifecycleError;
            public WeakReference<ISceneHandle> Handle;
        }

        private static volatile bool _enabled = true;
        private static volatile bool _observationIncomplete;
        private static int _capacity = DEFAULT_CAPACITY;
        private static long _droppedRegistrationCount;
        private static Func<long> _monotonicTimestampProvider = Stopwatch.GetTimestamp;

        internal static Func<long> MonotonicTimestampProvider
        {
            get => _monotonicTimestampProvider;
            set => _monotonicTimestampProvider = value ?? throw new ArgumentNullException(nameof(value));
        }

        public static bool Enabled
        {
            get => _enabled;
            set
            {
                AssetRuntimeGuard.EnsureMainThread();
                _enabled = value;
                if (!value)
                {
                    _observationIncomplete = true;
                    _trackedScenes.Clear();
                }
            }
        }

        /// <summary>
        /// True when a scene handle may exist outside the current registry because tracking was disabled, cleared,
        /// or reset while a previous Play Mode owner survived.
        /// </summary>
        internal static bool ObservationIncomplete => _observationIncomplete;

        /// <summary>The maximum number of live scene diagnostics retained by the registry.</summary>
        public static int Capacity => _capacity;

        /// <summary>
        /// Number of scene registrations rejected because the diagnostics registry reached capacity.
        /// The counter is cumulative until subsystem reset.
        /// </summary>
        public static long DroppedRegistrationCount => _droppedRegistrationCount;

        private static readonly Dictionary<long, SceneEntry> _trackedScenes =
            new Dictionary<long, SceneEntry>();

        private static readonly List<long> StaleSceneIds = new List<long>(4);

        /// <summary>
        /// Configures the diagnostics bound while tracking is disabled. This setting does not change provider
        /// scene ownership or loading capacity.
        /// </summary>
        public static void ConfigureCapacity(int capacity)
        {
            AssetRuntimeGuard.EnsureMainThread();
            if (capacity <= 0 || capacity > MAX_CAPACITY)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(capacity),
                    $"Scene tracking capacity must be between 1 and {MAX_CAPACITY}.");
            }

            if (_enabled)
            {
                throw new InvalidOperationException(
                    "Scene tracking capacity cannot be changed while tracking is enabled.");
            }

            _capacity = capacity;
        }

        public static void Register(
            long id,
            string packageName,
            string providerType,
            string sceneLocation,
            string bucket,
            LoadSceneMode loadMode,
            ISceneHandle handle)
        {
            Register(
                id,
                packageName,
                providerType,
                sceneLocation,
                bucket,
                new LoadSceneParameters(loadMode),
                handle);
        }

        public static void Register(
            long id,
            string packageName,
            string providerType,
            string sceneLocation,
            string bucket,
            LoadSceneParameters loadParameters,
            ISceneHandle handle)
        {
            AssetRuntimeGuard.EnsureMainThread();
            if (!Enabled)
            {
                if (handle != null) _observationIncomplete = true;
                return;
            }
            if (handle == null) return;

            if (!_trackedScenes.ContainsKey(id) && _trackedScenes.Count >= _capacity)
            {
                PruneCollectedHandles();
            }

            if (!_trackedScenes.ContainsKey(id) && _trackedScenes.Count >= _capacity)
            {
                if (_droppedRegistrationCount < long.MaxValue)
                {
                    _droppedRegistrationCount++;
                }

                _observationIncomplete = true;

                return;
            }

            _trackedScenes[id] = new SceneEntry
            {
                Id = id,
                PackageName = packageName,
                ProviderType = providerType,
                SceneLocation = sceneLocation,
                Bucket = bucket,
                LoadMode = loadParameters.loadSceneMode,
                LocalPhysicsMode = loadParameters.localPhysicsMode,
                RegistrationTimeUtc = DateTime.UtcNow,
                RegistrationTimestamp = _monotonicTimestampProvider(),
                Handle = new WeakReference<ISceneHandle>(handle)
            };
        }

        public static void MarkUnloadRequested(long id)
        {
            AssetRuntimeGuard.EnsureMainThread();
            if (!Enabled) return;
            if (_trackedScenes.TryGetValue(id, out var entry))
            {
                entry.UnloadRequestedTimeUtc = DateTime.UtcNow;
                entry.UnloadRequestedTimestamp = _monotonicTimestampProvider();
                entry.LifecycleError = null;
            }
        }

        internal static void MarkUnloadFailed(long id, string error)
        {
            AssetRuntimeGuard.EnsureMainThread();
            if (!Enabled) return;
            if (_trackedScenes.TryGetValue(id, out var entry))
            {
                entry.UnloadRequestedTimeUtc = null;
                entry.UnloadRequestedTimestamp = 0L;
                entry.LifecycleError = string.IsNullOrEmpty(error)
                    ? "Scene unload failed."
                    : error;
            }
        }

        public static void Unregister(long id)
        {
            AssetRuntimeGuard.EnsureMainThread();
            _trackedScenes.Remove(id);
        }

        public static int GetTrackedSceneCount()
        {
            AssetRuntimeGuard.EnsureMainThread();
            if (!Enabled) return 0;
            PruneCollectedHandles();
            return _trackedScenes.Count;
        }

        /// <summary>
        /// Copies at most <paramref name="maxCount"/> live entries into a caller-owned reusable list and returns
        /// the exact live registry count observed by this capture. The returned count can exceed the number copied.
        /// </summary>
        public static int CopyTrackedScenesTo(List<SceneInfo> destination, int maxCount)
        {
            AssetRuntimeGuard.EnsureMainThread();
            if (destination == null) throw new ArgumentNullException(nameof(destination));
            if (maxCount < 0) throw new ArgumentOutOfRangeException(nameof(maxCount));

            destination.Clear();
            StaleSceneIds.Clear();

            if (!Enabled) return 0;

            int totalCount = 0;

            foreach (var kvp in _trackedScenes)
            {
                var entry = kvp.Value;
                if (entry.Handle == null || !entry.Handle.TryGetTarget(out ISceneHandle handle) || handle == null)
                {
                    StaleSceneIds.Add(kvp.Key);
                    _observationIncomplete = true;
                    continue;
                }

                if (handle is ISceneTrackerHandleState state && state.ShouldRemoveFromSceneTracker)
                {
                    StaleSceneIds.Add(kvp.Key);
                    continue;
                }

                totalCount++;
                if (destination.Count >= maxCount)
                {
                    continue;
                }

                var info = new SceneInfo
                {
                    Id = entry.Id,
                    PackageName = entry.PackageName,
                    ProviderType = entry.ProviderType,
                    SceneLocation = entry.SceneLocation,
                    Bucket = entry.Bucket,
                    LoadMode = entry.LoadMode,
                    LocalPhysicsMode = entry.LocalPhysicsMode,
                    RegistrationTimeUtc = entry.RegistrationTimeUtc,
                    UnloadRequestedTimeUtc = entry.UnloadRequestedTimeUtc,
                    RegistrationTimestamp = entry.RegistrationTimestamp,
                    UnloadRequestedTimestamp = entry.UnloadRequestedTimestamp,
                    UnloadRequested = entry.UnloadRequestedTimeUtc.HasValue,
                    Error = entry.LifecycleError
                };

                try
                {
                    info.ScenePath = handle.ScenePath;
                    info.ActivationMode = handle.ActivationMode;
                    info.ActivationState = handle.ActivationState;
                    info.SupportsManualActivation = handle.SupportsManualActivation;
                    info.IsDone = handle.IsDone;
                    info.Progress = handle.Progress;
                    info.RefCount = handle is IReferenceCounted referenceCounted ? referenceCounted.RefCount : 0;
                    string providerError = handle.Error;
                    if (string.IsNullOrEmpty(info.Error))
                    {
                        info.Error = providerError;
                    }

                    Scene runtimeScene = handle.Scene;
                    info.RuntimeSceneName = runtimeScene.name;
                    info.RuntimeSceneLoaded = runtimeScene.IsValid() && runtimeScene.isLoaded;
                }
                catch (Exception ex) when (AssetRuntimeGuard.IsRecoverableException(ex))
                {
                    info.Error = string.IsNullOrEmpty(info.Error) ? ex.Message : info.Error;
                }

                destination.Add(info);
            }

            for (int i = 0; i < StaleSceneIds.Count; i++)
            {
                _trackedScenes.Remove(StaleSceneIds[i]);
            }

            StaleSceneIds.Clear();

            return totalCount;
        }

        public static void Clear()
        {
            AssetRuntimeGuard.EnsureMainThread();
            if (_enabled) _observationIncomplete = true;
            _trackedScenes.Clear();
        }

        internal static bool Reset()
        {
            bool previousEpochWasIncomplete =
                _trackedScenes.Count > 0 || _observationIncomplete || _droppedRegistrationCount > 0L;
            _trackedScenes.Clear();
            StaleSceneIds.Clear();
            _capacity = DEFAULT_CAPACITY;
            _droppedRegistrationCount = 0L;
            _enabled = true;
            _observationIncomplete = false;
            _monotonicTimestampProvider = Stopwatch.GetTimestamp;
            return previousEpochWasIncomplete;
        }

        internal static void MarkObservationIncomplete()
        {
            _observationIncomplete = true;
        }

        internal static long GetMonotonicTimestamp()
        {
            return _monotonicTimestampProvider();
        }

        internal static double GetAgeSeconds(long startTimestamp, long nowTimestamp)
        {
            if (startTimestamp <= 0L || nowTimestamp <= startTimestamp) return 0d;
            return (double)(nowTimestamp - startTimestamp) / Stopwatch.Frequency;
        }

        private static void PruneCollectedHandles()
        {
            StaleSceneIds.Clear();
            foreach (KeyValuePair<long, SceneEntry> pair in _trackedScenes)
            {
                WeakReference<ISceneHandle> reference = pair.Value.Handle;
                if (reference == null || !reference.TryGetTarget(out ISceneHandle handle) || handle == null)
                {
                    StaleSceneIds.Add(pair.Key);
                    _observationIncomplete = true;
                }
                else if (handle is ISceneTrackerHandleState state && state.ShouldRemoveFromSceneTracker)
                {
                    StaleSceneIds.Add(pair.Key);
                }
            }

            for (int i = 0; i < StaleSceneIds.Count; i++)
            {
                _trackedScenes.Remove(StaleSceneIds[i]);
            }

            StaleSceneIds.Clear();
        }
    }

    internal interface ISceneTrackerHandleState
    {
        bool ShouldRemoveFromSceneTracker { get; }
    }
}
