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
        private static readonly Dictionary<int, AudioClipReferenceLoader> runtimeReferenceLoaders = new Dictionary<int, AudioClipReferenceLoader>();
        private static readonly object runtimeReferenceLoaderLock = new object();

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

            AudioClipReferenceLoader runtimeLoader = GetRegisteredLoader(reference);
            if (runtimeLoader != null)
            {
                IAudioClipHandle managedHandle = await runtimeLoader(reference, cancellationToken);
                if (managedHandle != null)
                    return managedHandle;
            }

            if (reference.LocationKind == AudioLocationKind.AssetAddress)
            {
                return null;
            }

            string resolvedLocation = reference.ResolveLocation();
            return await ExternalAudioClipHandle.LoadAsync(resolvedLocation, cancellationToken);
        }

        public static async UniTask<IAudioClipHandle> LoadLegacyPathAsync(string legacyPath, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(legacyPath))
                return null;

            return await ExternalAudioClipHandle.LoadAsync(legacyPath, cancellationToken);
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

        public static void UnregisterReferenceLoader(AudioClipReference reference)
        {
            if (reference == null) return;

            lock (runtimeReferenceLoaderLock)
            {
                runtimeReferenceLoaders.Remove(reference.GetInstanceID());
            }
        }

        public static void ClearReferenceLoaders()
        {
            lock (runtimeReferenceLoaderLock)
            {
                runtimeReferenceLoaders.Clear();
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
        public int RefCount => refCount;

        public void Retain()
        {
            if (clip == null) return;
            refCount++;
        }

        public void Release()
        {
            if (refCount > 0) refCount--;
        }

        public void Dispose() => Release();
    }

    internal sealed class ManagedAudioClipHandle : IAudioClipHandle
    {
        private AudioClip clip;
        private Action releaseAction;
        private int refCount;

        public ManagedAudioClipHandle(AudioClip clip, Action releaseAction)
        {
            this.clip = clip;
            this.releaseAction = releaseAction;
            refCount = 1;
        }

        public bool IsDone => true;
        public bool IsSuccess => clip != null;
        public AudioClip Clip => clip;
        public string Error => clip == null ? "Managed AudioClip is null." : string.Empty;
        public int RefCount => refCount;

        public void Retain()
        {
            if (clip == null) return;
            refCount++;
        }

        public void Release()
        {
            if (refCount <= 0) return;

            refCount--;
            if (refCount > 0) return;

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

            public CacheEntry(string location)
            {
                Location = location;
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

                if (entry.RefCount == 0 && entry.IsDone)
                {
                    DestroyAndRemoveEntry(entry);
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
                            if (entry.RefCount == 0) cache.Remove(entry.Location);
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

                        if (entry.RefCount == 0)
                        {
                            DestroyAndRemoveEntry(entry);
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
                    if (entry.RefCount == 0) cache.Remove(entry.Location);
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
            string lower = path.ToLowerInvariant();
            if (lower.EndsWith(".mp3")) return AudioType.MPEG;
            if (lower.EndsWith(".wav")) return AudioType.WAV;
            if (lower.EndsWith(".ogg")) return AudioType.OGGVORBIS;
            if (lower.EndsWith(".aiff") || lower.EndsWith(".aif")) return AudioType.AIFF;
            return AudioType.UNKNOWN;
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

        [SerializeField, FormerlySerializedAs("filePath"), HideInInspector]
        private string legacyFilePath = "";

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
            else if (effectiveMode == AudioFileSourceMode.ExternalReference && (this.externalReference != null || !string.IsNullOrEmpty(this.legacyFilePath)))
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
                IAudioClipHandle handle = this.externalReference != null
                    ? await AudioClipResolver.LoadExternalAsync(this.externalReference, activeEvent.GetCancellationToken())
                    : await AudioClipResolver.LoadLegacyPathAsync(this.legacyFilePath, activeEvent.GetCancellationToken());

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
            if (externalReference != null)
                return externalReference.ResolveLocation();

            return legacyFilePath;
        }

        private AudioFileSourceMode GetEffectiveSourceMode()
        {
            if (sourceMode == AudioFileSourceMode.ExternalReference)
                return AudioFileSourceMode.ExternalReference;

            if (embeddedClip != null)
                return AudioFileSourceMode.EmbeddedClip;

            if (externalReference != null || !string.IsNullOrEmpty(legacyFilePath))
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
                    EditorGUILayout.LabelField("Resolved Path", this.externalReference.ResolveLocation(), EditorStyles.wordWrappedLabel);
                }
                else if (!string.IsNullOrEmpty(this.legacyFilePath))
                {
                    EditorGUILayout.HelpBox("Legacy filePath data is still present on this node. Assign a new AudioClipReference asset to complete migration.", MessageType.Warning);
                    EditorGUILayout.LabelField("Legacy Path", this.legacyFilePath, EditorStyles.wordWrappedLabel);
                }
            }
            this.volumeOffset = EditorGUILayout.Slider("Volume Offset", this.volumeOffset, -1, 1);
            this.pitchOffset = EditorGUILayout.Slider("Pitch Offset", this.pitchOffset, -1, 1);
            EditorGUILayout.MinMaxSlider("Start Time", ref this.minStartTime, ref this.maxStartTime, 0, 1);
        }

#endif
    }
}
