using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using UnityEngine.SceneManagement;

namespace CycloneGames.AssetManagement.Runtime
{
    /// <summary>
    /// Lightweight runtime registry for scene-handle diagnostics.
    /// This is intentionally provider-agnostic and contains no gameplay assumptions.
    /// </summary>
    public static class SceneTracker
    {
        public struct SceneInfo
        {
            public int Id;
            public string PackageName;
            public string ProviderType;
            public string SceneLocation;
            public string ScenePath;
            public string RuntimeSceneName;
            public string Bucket;
            public LoadSceneMode LoadMode;
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
            public string Error;
        }

        private sealed class SceneEntry
        {
            public int Id;
            public string PackageName;
            public string ProviderType;
            public string SceneLocation;
            public string Bucket;
            public LoadSceneMode LoadMode;
            public DateTime RegistrationTimeUtc;
            public DateTime? UnloadRequestedTimeUtc;
            public ISceneHandle Handle;
        }

        public static bool Enabled { get; set; } = true;

        private static readonly ConcurrentDictionary<int, SceneEntry> _trackedScenes =
            new ConcurrentDictionary<int, SceneEntry>();

        [ThreadStatic]
        private static List<SceneInfo> _threadLocalScenes;

        public static void Register(
            int id,
            string packageName,
            string providerType,
            string sceneLocation,
            string bucket,
            LoadSceneMode loadMode,
            ISceneHandle handle)
        {
            if (!Enabled || handle == null) return;

            _trackedScenes[id] = new SceneEntry
            {
                Id = id,
                PackageName = packageName,
                ProviderType = providerType,
                SceneLocation = sceneLocation,
                Bucket = bucket,
                LoadMode = loadMode,
                RegistrationTimeUtc = DateTime.UtcNow,
                Handle = handle
            };
        }

        public static void MarkUnloadRequested(int id)
        {
            if (!Enabled) return;
            if (_trackedScenes.TryGetValue(id, out var entry) && !entry.UnloadRequestedTimeUtc.HasValue)
            {
                entry.UnloadRequestedTimeUtc = DateTime.UtcNow;
            }
        }

        public static void Unregister(int id)
        {
            if (!Enabled) return;
            _trackedScenes.TryRemove(id, out _);
        }

        public static int GetTrackedSceneCount()
        {
            return Enabled ? _trackedScenes.Count : 0;
        }

        public static List<SceneInfo> GetTrackedScenes()
        {
            var list = _threadLocalScenes ?? (_threadLocalScenes = new List<SceneInfo>(16));
            list.Clear();

            if (!Enabled) return list;

            foreach (var kvp in _trackedScenes)
            {
                var entry = kvp.Value;
                var handle = entry.Handle;
                if (handle == null) continue;

                var info = new SceneInfo
                {
                    Id = entry.Id,
                    PackageName = entry.PackageName,
                    ProviderType = entry.ProviderType,
                    SceneLocation = entry.SceneLocation,
                    Bucket = entry.Bucket,
                    LoadMode = entry.LoadMode,
                    RegistrationTimeUtc = entry.RegistrationTimeUtc,
                    UnloadRequestedTimeUtc = entry.UnloadRequestedTimeUtc
                };

                try
                {
                    info.ScenePath = handle.ScenePath;
                    info.ActivationMode = handle.ActivationMode;
                    info.ActivationState = handle.ActivationState;
                    info.SupportsManualActivation = handle.SupportsManualActivation;
                    info.IsDone = handle.IsDone;
                    info.Progress = handle.Progress;
                    info.RefCount = handle.RefCount;
                    info.Error = handle.Error;

                    Scene runtimeScene = handle.Scene;
                    info.RuntimeSceneName = runtimeScene.name;
                    info.RuntimeSceneLoaded = runtimeScene.IsValid() && runtimeScene.isLoaded;
                    info.UnloadRequested = entry.UnloadRequestedTimeUtc.HasValue;
                }
                catch (Exception ex)
                {
                    info.Error = string.IsNullOrEmpty(info.Error) ? ex.Message : info.Error;
                }

                list.Add(info);
            }

            return list;
        }

        public static void Clear()
        {
            _trackedScenes.Clear();
        }
    }
}
