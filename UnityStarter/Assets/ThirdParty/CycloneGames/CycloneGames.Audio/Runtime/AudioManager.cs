// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using UnityEngine;
using UnityEngine.Audio;
using System.Globalization;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace CycloneGames.Audio.Runtime
{
    /// <summary>
    /// The manager that handles the playback of AudioEvents
    /// </summary>
    public class AudioManager : MonoBehaviour, IAudioService
    {
        /// <summary>
        /// Singleton instance of the audio manager
        /// </summary>
        public static AudioManager Instance { get; private set; }
        /// <summary>
        /// Flag for creating instances, set to false during shutdown 
        /// </summary>
        private static bool AllowCreateInstance = true;

        private static int mainThreadId;

        /// <summary>
        /// The currently-playing events at runtime
        /// </summary>
        public static List<ActiveEvent> ActiveEvents { get; private set; }

        // Pool for ActiveEvent objects to avoid GC allocations
        private static readonly Stack<ActiveEvent> activeEventPool = new Stack<ActiveEvent>();
        /// <summary>
        /// The AudioSource components that are not currently playing.
        /// Using a Queue for efficient acquisition and release (O(1) operations).
        /// </summary>
        private static readonly Queue<AudioSource> availableSources = new Queue<AudioSource>();
        public static IReadOnlyCollection<AudioSource> AvailableSources => availableSources;
        private static readonly ConcurrentQueue<Action> commandQueue = new ConcurrentQueue<Action>();

        /// <summary>
        /// List of the previously started events
        /// </summary>
        private static readonly List<ActiveEvent> previousEvents = new List<ActiveEvent>();
        /// <summary>
        /// The language that all voice events should play in
        /// </summary>
        public static int CurrentLanguage { get; private set; }
        /// <summary>
        /// The full list of languages available
        /// </summary>
        public static string[] Languages;
        /// <summary>
        /// The number of AudioSources to create in the pool. Set this in the Inspector.
        /// If set to 0, a platform-specific default will be used (Desktop: 80, Mobile: 32, WebGL: 24).
        /// </summary>
        [SerializeField]
        private int customPoolSize = 0;

        [Header("Mixing")]
        [SerializeField]
        private AudioMixer mainMixer;
        private static AudioMixer staticMainMixer;

        private static bool isInitialized = false;
        /// <summary>
        /// The total list of AudioSources for the manager to use
        /// </summary>
        private static readonly List<AudioSource> sourcePool = new List<AudioSource>();
        public static IReadOnlyList<AudioSource> SourcePool => sourcePool;

        // Memory tracking fields
        private static readonly Dictionary<AudioClip, int> activeClipRefCount = new Dictionary<AudioClip, int>();
        private static readonly Dictionary<AudioClip, long> clipMemoryCache = new Dictionary<AudioClip, long>();

        // A reusable HashSet to avoid GC allocations in TrackMemory when collecting unique clips.
        private static readonly HashSet<AudioClip> reusableClipSet = new HashSet<AudioClip>();

        public static long TotalMemoryUsage { get; private set; } = 0;
        public static IReadOnlyDictionary<AudioClip, int> ActiveClipRefCount => activeClipRefCount;
        public static IReadOnlyDictionary<AudioClip, long> ClipMemoryCache => clipMemoryCache;

        private static bool debugMode = false;
        public static bool DebugMode => debugMode;
        private const int MaxPreviousEvents = 300;
        public static List<ActiveEvent> PreviousEvents => previousEvents;



        #region Interface

        ActiveEvent IAudioService.PlayEvent(AudioEvent eventToPlay, GameObject emitterObject) => PlayEvent(eventToPlay, emitterObject);
        ActiveEvent IAudioService.PlayEvent(AudioEvent eventToPlay, Vector3 position) => PlayEvent(eventToPlay, position);
        ActiveEvent IAudioService.PlayEventScheduled(AudioEvent eventToPlay, GameObject emitterObject, double dspTime) => PlayEventScheduled(eventToPlay, emitterObject, dspTime);
        void IAudioService.StopAll(AudioEvent eventsToStop) => StopAll(eventsToStop);
        void IAudioService.StopAll(int groupNum) => StopAll(groupNum);

        public void SetMixerVolume(string parameterName, float volume)
        {
            if (mainMixer != null)
            {
                mainMixer.SetFloat(parameterName, volume);
            }
            else
            {
                Debug.LogWarning("AudioManager: Main Mixer not assigned.");
            }
        }

        public float GetMixerVolume(string parameterName)
        {
            if (mainMixer != null)
            {
                if (mainMixer.GetFloat(parameterName, out float value))
                {
                    return value;
                }
            }
            return 0f;
        }

        /// <summary>
        /// Start playing an AudioEvent. Thread-safe (returns null if called from background thread).
        /// </summary>
        /// <param name="eventToPlay">The AudioEvent to play</param>
        /// <param name="emitterObject">The GameObject to play the event on</param>
        /// <returns>The reference for the runtime event that can be modified or stopped explicitly</returns>
        public static ActiveEvent PlayEvent(AudioEvent eventToPlay, GameObject emitterObject)
        {
            if (Thread.CurrentThread.ManagedThreadId != mainThreadId)
            {
                commandQueue.Enqueue(() => PlayEvent(eventToPlay, emitterObject));
                return null;
            }

            if (!ValidateManager() || !ValidateEvent(eventToPlay))
            {
                return null;
            }

            Transform emitterTransform = emitterObject != null ? emitterObject.transform : null;
            ActiveEvent tempEvent = GetActiveEventFromPool(eventToPlay, emitterTransform);
            tempEvent.Play();

            if (tempEvent.status != EventStatus.Error)
            {
                TrackMemory(tempEvent, true);
            }

            return tempEvent;
        }

        public static ActiveEvent PlayEvent(AudioEvent eventToPlay, Vector3 position)
        {
            if (Thread.CurrentThread.ManagedThreadId != mainThreadId)
            {
                commandQueue.Enqueue(() => PlayEvent(eventToPlay, position));
                return null;
            }

            if (!ValidateManager() || !ValidateEvent(eventToPlay))
            {
                return null;
            }

            ActiveEvent tempEvent = GetActiveEventFromPool(eventToPlay, null);
            tempEvent.Play();
            tempEvent.SetAllSourcePositions(position);

            if (tempEvent.status != EventStatus.Error)
            {
                TrackMemory(tempEvent, true);
            }

            return tempEvent;
        }

        /// <summary>
        /// Start playing an AudioEvent at a specific DSP time (high precision for rhythm games).
        /// </summary>
        /// <param name="eventToPlay">The AudioEvent to play</param>
        /// <param name="emitterObject">The GameObject to play the event on</param>
        /// <param name="dspTime">The AudioSettings.dspTime to play at</param>
        public static ActiveEvent PlayEventScheduled(AudioEvent eventToPlay, GameObject emitterObject, double dspTime)
        {
            if (Thread.CurrentThread.ManagedThreadId != mainThreadId)
            {
                // Scheduled playback from background thread is tricky because dspTime moves on. 
                // We still queue it, but the user must be aware of the delay.
                commandQueue.Enqueue(() => PlayEventScheduled(eventToPlay, emitterObject, dspTime));
                return null;
            }

            if (!ValidateManager() || !ValidateEvent(eventToPlay))
            {
                return null;
            }

            Transform emitterTransform = emitterObject != null ? emitterObject.transform : null;
            ActiveEvent tempEvent = GetActiveEventFromPool(eventToPlay, emitterTransform);
            tempEvent.scheduledDspTime = dspTime;
            tempEvent.Play();

            if (tempEvent.status != EventStatus.Error)
            {
                TrackMemory(tempEvent, true);
            }

            return tempEvent;
        }

        private static ActiveEvent GetActiveEventFromPool(AudioEvent eventToPlay, Transform emitter)
        {
            ActiveEvent activeEvent;
            if (activeEventPool.Count > 0)
            {
                activeEvent = activeEventPool.Pop();
                activeEvent.Initialize(eventToPlay, emitter);
            }
            else
            {
                activeEvent = new ActiveEvent();
                activeEvent.Initialize(eventToPlay, emitter);
            }
            return activeEvent;
        }

        /// <summary>
        /// Start playing an AudioEvent
        /// </summary>
        /// <param name="eventToPlay">The AudioEvent to play</param>
        /// <param name="emitter">The AudioSource component to play the event on</param>
        /// <returns>The reference for the runtime event that can be modified or stopped explicitly</returns>
        public static ActiveEvent PlayEvent(AudioEvent eventToPlay, AudioSource emitter)
        {
            if (Thread.CurrentThread.ManagedThreadId != mainThreadId)
            {
                commandQueue.Enqueue(() => PlayEvent(eventToPlay, emitter));
                return null;
            }

            Debug.LogWarningFormat("AudioManager: deprecated function called on event {0} - play on an AudioSource no longer supported");
            if (!ValidateManager() || !ValidateEvent(eventToPlay))
            {
                return null;
            }

            ActiveEvent tempEvent = GetActiveEventFromPool(eventToPlay, emitter.transform);
            tempEvent.Play();

            if (tempEvent.status != EventStatus.Error)
            {
                TrackMemory(tempEvent, true);
            }

            return tempEvent;
        }

        /// <summary>
        /// Stop all active instances of an audio event
        /// </summary>
        /// <param name="eventsToStop">The event to stop all instances of</param>
        public static void StopAll(AudioEvent eventsToStop)
        {
            if (Thread.CurrentThread.ManagedThreadId != mainThreadId)
            {
                commandQueue.Enqueue(() => StopAll(eventsToStop));
                return;
            }

            if (!ValidateManager())
            {
                return;
            }

            for (int i = ActiveEvents.Count - 1; i >= 0; i--)
            {
                ActiveEvent tempEvent = ActiveEvents[i];
                if (tempEvent.rootEvent == eventsToStop)
                {
                    tempEvent.Stop();
                }
            }
        }

        /// <summary>
        /// Stop all active instances of a group
        /// </summary>
        /// <param name="groupNum">The group number to stop all instances of</param>
        public static void StopAll(int groupNum)
        {
            if (Thread.CurrentThread.ManagedThreadId != mainThreadId)
            {
                commandQueue.Enqueue(() => StopAll(groupNum));
                return;
            }

            if (!ValidateManager())
            {
                return;
            }

            for (int i = ActiveEvents.Count - 1; i >= 0; i--)
            {
                ActiveEvent tempEvent = ActiveEvents[i];
                if (tempEvent.rootEvent.Group == groupNum)
                {
                    tempEvent.Stop();
                }
            }
        }

        /// <summary>
        /// Clear an ActiveEvent from the list of ActiveEvents
        /// </summary>
        /// <param name="stoppedEvent">The event that is no longer playing to remove from the ActiveEvent list</param>
        public static void RemoveActiveEvent(ActiveEvent stoppedEvent)
        {
            if (!ValidateManager())
            {
                return;
            }

            // Return all associated AudioSources to the available pool.
            List<EventSource> sources = stoppedEvent.sources;
            for (int i = 0; i < sources.Count; i++)
            {
                AudioSource tempSource = sources[i].source;
                // A null check is important here, as the source might have been destroyed.
                if (tempSource != null)
                {
                    // We assume a source is only ever returned once per event stop, so we don't need a .Contains check,
                    // which would be inefficient (O(n)) for a queue.
                    availableSources.Enqueue(tempSource);
                }
            }

            TrackMemory(stoppedEvent, false);
            ActiveEvents.Remove(stoppedEvent);

            // Return ActiveEvent to pool
            stoppedEvent.Reset();
            activeEventPool.Push(stoppedEvent);

            stoppedEvent = null;
        }

        public static void AddPreviousEvent(ActiveEvent newEvent)
        {
            previousEvents.Add(newEvent);

            while (previousEvents.Count > MaxPreviousEvents)
            {
                previousEvents.RemoveAt(0);
            }
        }

        /// <summary>
        /// Get the list of all cultures for compatible languges
        /// </summary>
        public static void UpdateLanguages()
        {
            CultureInfo[] cultures = CultureInfo.GetCultures(CultureTypes.AllCultures);
            Languages = new string[cultures.Length];
            for (int i = 0; i < cultures.Length; i++)
            {
                Languages[i] = cultures[i].Name;
            }
        }

        public static void SetDebugMode(bool toggle)
        {
            debugMode = toggle;
            ClearSourceText();
        }

        public static async void DelayRemoveActiveEvent(ActiveEvent eventToRemove, float delay = 1)
        {
            if (!ValidateManager())
            {
                return;
            }

            try
            {
                // Use UniTask.Delay for a GC-friendly, cancellable delay.
                await UniTask.Delay(TimeSpan.FromSeconds(delay), ignoreTimeScale: false, cancellationToken: eventToRemove.GetCancellationToken());

                // If the task was not cancelled, proceed with removal.
                if (eventToRemove.status != EventStatus.Error)
                {
                    RemoveActiveEvent(eventToRemove);
                }
            }
            catch (OperationCanceledException)
            {
                // This is expected if the event is stopped abruptly. No action needed.
            }
        }

        // ...

        private void Awake()
        {
            mainThreadId = Thread.CurrentThread.ManagedThreadId;

            if (Instance != null)
            {
                // If an instance already exists and it's not this one, destroy this one.
                if (Instance != this)
                {
                    Destroy(gameObject);
                }
                return;
            }

            // This is the first instance.
            Instance = this;
            DontDestroyOnLoad(gameObject);

            if (!isInitialized)
            {
                Initialize();
            }
        }

        private void Update()
        {
            // Process command queue
            while (commandQueue.TryDequeue(out Action command))
            {
                command.Invoke();
            }

            // The Update loop is now significantly leaner, only responsible for updating active events.
            // Delayed removals are handled by UniTask.
            for (int i = ActiveEvents.Count - 1; i >= 0; i--)
            {
                ActiveEvent tempEvent = ActiveEvents[i];
                if (tempEvent != null && tempEvent.sources.Count != 0)
                {
                    tempEvent.Update();
                }
            }
        }

        /// <summary>
        /// Instantiate a new GameObject and add the AudioManager component as a fallback.
        /// </summary>
        private static void CreateInstance()
        {
            if (Instance != null || !AllowCreateInstance)
            {
                return;
            }

            // This will create the object, and its Awake() method will handle all initialization.
            new GameObject("AudioManager").AddComponent<AudioManager>();
        }

        /// <summary>
        /// Initializes the AudioManager instance.
        /// </summary>
        private void Initialize()
        {
            CurrentLanguage = 0;
            staticMainMixer = this.mainMixer;
            ActiveEvents = new List<ActiveEvent>();
            CreateSources();
            ValidateAudioListener();
            Application.quitting += HandleQuitting;
            isInitialized = true;
        }

        /// <summary>
        /// Ensures that there is an AudioListener in the scene.
        /// It prioritizes adding it to the main camera for correct 3D audio positioning.
        /// As a fallback, it adds it to the AudioManager's GameObject.
        /// </summary>
        private void ValidateAudioListener()
        {
            // This check is performed only once at initialization to avoid performance overhead.
            if (FindObjectsOfType<AudioListener>().Length > 0)
            {
                return;
            }

            Camera mainCamera = Camera.main;
            if (mainCamera != null)
            {
                Debug.Log("No AudioListener found. Creating one on the main camera.");
                mainCamera.gameObject.AddComponent<AudioListener>();
            }
            else
            {
                Debug.LogWarning("No AudioListener or main camera found in the scene. Creating AudioListener on AudioManager. 3D audio positioning may be incorrect.");
                this.gameObject.AddComponent<AudioListener>();
            }
        }

        /// <summary>
        /// On shutdown we cannot create an instance
        /// </summary>
        private static void HandleQuitting()
        {
            AllowCreateInstance = false;
        }

        /// <summary>
        /// Create the pool of AudioSources
        /// </summary>
        private void CreateSources()
        {
            int poolCount;
            if (this.customPoolSize > 0)
            {
                poolCount = this.customPoolSize;
            }
            else
            {
#if UNITY_WEBGL
                    poolCount = 32;
#elif UNITY_ANDROID || UNITY_IOS
                    poolCount = 48;
#else
                poolCount = 128; // Desktop default
#endif
            }

            for (int i = 0; i < poolCount; i++)
            {
                GameObject sourceGO = new GameObject("AudioSource" + i);
                sourceGO.transform.localScale = new Vector3(0.1f, 0.1f, 0.1f);
                sourceGO.transform.SetParent(this.transform);
                AudioSource tempSource = sourceGO.AddComponent<AudioSource>();
                tempSource.playOnAwake = false;
                sourcePool.Add(tempSource);
                availableSources.Enqueue(tempSource);
#if UNITY_EDITOR
                TextMesh newText = sourceGO.AddComponent<TextMesh>();
                newText.characterSize = 0.2f;
#endif
            }
        }

        private static void ClearSourceText()
        {
            foreach (var source in availableSources)
            {
                if (source != null)
                {
                    TextMesh tempText = source.GetComponent<TextMesh>();
                    if (tempText != null)
                    {
                        tempText.text = string.Empty;
                    }
                }
            }
        }

        /// <summary>
        /// Get the number of active instances of an AudioEvent
        /// </summary>
        /// <param name="audioEvent">The event to query the number of active instances of</param>
        /// <returns>The number of active instances of the specified AudioEvent</returns>
        private static int CountActiveInstances(AudioEvent audioEvent)
        {
            int tempCount = 0;

            for (int i = 0; i < ActiveEvents.Count; i++)
            {
                if (ActiveEvents[i].rootEvent == audioEvent)
                {
                    tempCount++;
                }
            }

            return tempCount;
        }

        /// <summary>
        /// Call an immediate stop on all active audio events of a particular group
        /// </summary>
        /// <param name="groupNum">The group number to stop</param>
        private static void StopGroupInstances(int groupNum)
        {
            for (int i = 0; i < ActiveEvents.Count; i++)
            {
                ActiveEvent tempEvent = ActiveEvents[i];
                if (tempEvent.rootEvent.Group == groupNum)
                {
                    Debug.LogFormat("Stopping: {0}", tempEvent.name);
                    tempEvent.StopImmediate();
                }
            }
        }

        /// <summary>
        /// Look for an existing AudioSource component that is not currently playing
        /// </summary>
        /// <returns>An AudioSource reference if one exists, otherwise null</returns>
        public static AudioSource GetUnusedSource()
        {
            // Using Dequeue is an O(1) operation, which is highly efficient.
            // We loop to ensure we don't return a source that was destroyed.
            while (availableSources.Count > 0)
            {
                AudioSource tempSource = availableSources.Dequeue();
                if (tempSource != null)
                {
                    return tempSource;
                }
                // If source was destroyed, it's implicitly removed from the pool. Continue to the next.
            }

            // Pool is empty or only contained destroyed sources.
            Debug.LogWarning("Audio source pool is empty. Consider increasing DefaultSourcesCount.");
            return null;
        }

        /// <summary>
        /// Remove any references to AudioSource components that no longer exist.
        /// NOTE: This operation can be expensive and should be used sparingly, not on hot paths.
        /// It's now implicitly handled by the GetUnusedSource method's null check.
        /// </summary>
        private static void ClearNullAudioSources()
        {
            int count = availableSources.Count;
            for (int i = 0; i < count; i++)
            {
                AudioSource tempSource = availableSources.Dequeue();
                if (tempSource != null)
                {
                    availableSources.Enqueue(tempSource);
                }
            }
        }

        private static long CalculateAudioClipMemoryUsage(AudioClip clip)
        {
            if (clip == null) return 0;
            return clip.samples * clip.channels * 2; // 16-bit samples
        }

        private static void TrackMemory(ActiveEvent activeEvent, bool isAdding)
        {
            // Use a reusable HashSet to get unique clips without GC allocation from LINQ.
            reusableClipSet.Clear();
            foreach (var eventSource in activeEvent.sources)
            {
                if (eventSource.source.clip != null)
                {
                    reusableClipSet.Add(eventSource.source.clip);
                }
            }

            foreach (var clip in reusableClipSet)
            {
                int direction = isAdding ? 1 : -1;

                // Update reference count.
                activeClipRefCount.TryGetValue(clip, out int count);
                count += direction;

                if (count > 0)
                {
                    activeClipRefCount[clip] = count;
                }
                else
                {
                    activeClipRefCount.Remove(clip);
                }

                // Update total memory usage.
                if (isAdding)
                {
                    if (!clipMemoryCache.ContainsKey(clip))
                    {
                        long memory = CalculateAudioClipMemoryUsage(clip);
                        clipMemoryCache[clip] = memory;
                        TotalMemoryUsage += memory;
                    }
                }
                else if (count == 0) // Only decrease memory when the last reference is removed.
                {
                    if (clipMemoryCache.TryGetValue(clip, out long memory))
                    {
                        TotalMemoryUsage -= memory;
                        clipMemoryCache.Remove(clip);
                    }
                }
            }
        }

        /// <summary>
        /// Make sure that the AudioManager has all of the required components
        /// </summary>
        /// <returns>Whether there is a valid AudioManager instance</returns>
        public static bool ValidateManager()
        {
            if (Instance == null)
            {
                if (!AllowCreateInstance) { return false; }

                // Try to find an instance that the user might have placed in the scene.
                Instance = FindObjectOfType<AudioManager>();

                // If no instance exists in the scene, create one.
                if (Instance == null)
                {
                    CreateInstance();
                }
            }
            return Instance != null;
        }

        private static bool ValidateEvent(AudioEvent eventToPlay)
        {
            if (eventToPlay == null)
            {
                return false;
            }

            if (!eventToPlay.ValidateAudioFiles())
            {
                return false;
            }

            if (eventToPlay.InstanceLimit > 0 && CountActiveInstances(eventToPlay) >= eventToPlay.InstanceLimit)
            {
                return false;
            }

            if (eventToPlay.Group != 0)
            {
                StopGroupInstances(eventToPlay.Group);
            }

            return true;
        }

        #endregion

        #region Editor

        /// <summary>
        /// Mute all ActiveEvents that are not soloed
        /// </summary>
        public static void ApplyActiveSolos()
        {
            ValidateManager();

            bool soloActive = false;
            for (int i = 0; i < ActiveEvents.Count; i++)
            {
                if (ActiveEvents[i].Soloed)
                {
                    soloActive = true;
                }
            }

            if (soloActive)
            {
                for (int i = 0; i < ActiveEvents.Count; i++)
                {
                    ActiveEvents[i].ApplySolo();
                }
            }
            else
            {
                ClearActiveSolos();
            }
        }

        /// <summary>
        /// Unmute all events
        /// </summary>
        public static void ClearActiveSolos()
        {
            ValidateManager();

            for (int i = 0; i < ActiveEvents.Count; i++)
            {
                ActiveEvents[i].ClearSolo();
            }
        }

        #endregion
    }
}