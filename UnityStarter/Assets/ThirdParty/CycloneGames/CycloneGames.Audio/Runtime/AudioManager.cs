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
        private static AudioListener cachedAudioListener = null;
        private static Camera cachedMainCamera = null;
        private static int cachedMainCameraFrame = -1;

        /// <summary>
        /// The currently-playing events at runtime
        /// </summary>
        public static List<ActiveEvent> ActiveEvents { get; private set; }

        private static readonly Stack<ActiveEvent> activeEventPool = new Stack<ActiveEvent>();
        private static readonly Queue<AudioSource> availableSources = new Queue<AudioSource>();
        public static IReadOnlyCollection<AudioSource> AvailableSources => availableSources;
        private static readonly ConcurrentQueue<Action> commandQueue = new ConcurrentQueue<Action>();
        private static readonly ActiveEvent[] previousEventsBuffer = new ActiveEvent[MaxPreviousEvents];
        private static int previousEventsWriteIndex = 0;
        private static int previousEventsCount = 0;
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
        private static readonly HashSet<AudioClip> reusableClipSet = new HashSet<AudioClip>();
        private static readonly Dictionary<string, AudioEvent> eventNameMap = new Dictionary<string, AudioEvent>();
        private static readonly object eventNameMapLock = new object();

        // Track loaded banks for memory management and editor display
        private static readonly HashSet<AudioBank> loadedBanks = new HashSet<AudioBank>();
        private static readonly object loadedBanksLock = new object();

        private static readonly Dictionary<string, List<AudioEvent>> reusableDuplicateCheck = new Dictionary<string, List<AudioEvent>>();
        private static readonly HashSet<string> reusableBankRegisteredNames = new HashSet<string>();

        public static long TotalMemoryUsage { get; private set; } = 0;
        public static IReadOnlyDictionary<AudioClip, int> ActiveClipRefCount => activeClipRefCount;
        public static IReadOnlyDictionary<AudioClip, long> ClipMemoryCache => clipMemoryCache;

        private static bool debugMode = false;
        public static bool DebugMode => debugMode;
        private const int MaxPreviousEvents = 300;

        /// <summary>
        /// Get previous events as a read-only list (creates a new list, use sparingly)
        /// </summary>
        public static List<ActiveEvent> PreviousEvents
        {
            get
            {
                var result = new List<ActiveEvent>(previousEventsCount);
                int startIndex = previousEventsCount < MaxPreviousEvents ? 0 : previousEventsWriteIndex;
                int count = previousEventsCount;
                for (int i = 0; i < count; i++)
                {
                    int index = (startIndex + i) % MaxPreviousEvents;
                    if (previousEventsBuffer[index] != null)
                    {
                        result.Add(previousEventsBuffer[index]);
                    }
                }
                return result;
            }
        }



        #region Interface

        ActiveEvent IAudioService.PlayEvent(AudioEvent eventToPlay, GameObject emitterObject) => PlayEvent(eventToPlay, emitterObject);
        ActiveEvent IAudioService.PlayEvent(AudioEvent eventToPlay, Vector3 position) => PlayEvent(eventToPlay, position);
        ActiveEvent IAudioService.PlayEvent(string eventName, GameObject emitterObject) => PlayEvent(eventName, emitterObject);
        ActiveEvent IAudioService.PlayEvent(string eventName, Vector3 position) => PlayEvent(eventName, position);
        ActiveEvent IAudioService.PlayEventScheduled(AudioEvent eventToPlay, GameObject emitterObject, double dspTime) => PlayEventScheduled(eventToPlay, emitterObject, dspTime);
        ActiveEvent IAudioService.PlayEventScheduled(string eventName, GameObject emitterObject, double dspTime) => PlayEventScheduled(eventName, emitterObject, dspTime);
        void IAudioService.StopAll(AudioEvent eventsToStop) => StopAll(eventsToStop);
        void IAudioService.StopAll(string eventName) => StopAll(eventName);
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
        /// Start playing an AudioEvent by name. Thread-safe (returns null if called from background thread).
        /// </summary>
        /// <param name="eventName">The name of the AudioEvent to play</param>
        /// <param name="emitterObject">The GameObject to play the event on</param>
        /// <returns>The reference for the runtime event that can be modified or stopped explicitly</returns>
        public static ActiveEvent PlayEvent(string eventName, GameObject emitterObject)
        {
            if (Thread.CurrentThread.ManagedThreadId != mainThreadId)
            {
                string capturedName = eventName; // Capture for closure
                commandQueue.Enqueue(() => PlayEvent(capturedName, emitterObject));
                return null;
            }

            if (!ValidateManager() || string.IsNullOrEmpty(eventName))
            {
                return null;
            }

            AudioEvent eventToPlay = GetEventByName(eventName);
            if (eventToPlay == null)
            {
                Debug.LogWarning($"AudioManager: Event '{eventName}' not found. Make sure the AudioBank is loaded.");
                return null;
            }

            return PlayEvent(eventToPlay, emitterObject);
        }

        /// <summary>
        /// Start playing an AudioEvent by name at a specific position. Thread-safe (returns null if called from background thread).
        /// </summary>
        /// <param name="eventName">The name of the AudioEvent to play</param>
        /// <param name="position">The position to play the event at</param>
        /// <returns>The reference for the runtime event that can be modified or stopped explicitly</returns>
        public static ActiveEvent PlayEvent(string eventName, Vector3 position)
        {
            if (Thread.CurrentThread.ManagedThreadId != mainThreadId)
            {
                string capturedName = eventName; // Capture for closure
                commandQueue.Enqueue(() => PlayEvent(capturedName, position));
                return null;
            }

            if (!ValidateManager() || string.IsNullOrEmpty(eventName))
            {
                return null;
            }

            AudioEvent eventToPlay = GetEventByName(eventName);
            if (eventToPlay == null)
            {
                Debug.LogWarning($"AudioManager: Event '{eventName}' not found. Make sure the AudioBank is loaded.");
                return null;
            }

            return PlayEvent(eventToPlay, position);
        }

        /// <summary>
        /// Start playing an AudioEvent by name at a specific DSP time (high precision for rhythm games).
        /// </summary>
        /// <param name="eventName">The name of the AudioEvent to play</param>
        /// <param name="emitterObject">The GameObject to play the event on</param>
        /// <param name="dspTime">The AudioSettings.dspTime to play at</param>
        public static ActiveEvent PlayEventScheduled(string eventName, GameObject emitterObject, double dspTime)
        {
            if (Thread.CurrentThread.ManagedThreadId != mainThreadId)
            {
                string capturedName = eventName; // Capture for closure
                double capturedDspTime = AudioSettings.dspTime;
                double adjustedDspTime = dspTime;
                if (dspTime > capturedDspTime)
                {
                    adjustedDspTime = dspTime;
                }
                else
                {
                    adjustedDspTime = capturedDspTime + 0.01;
                }

                commandQueue.Enqueue(() => PlayEventScheduled(capturedName, emitterObject, adjustedDspTime));
                return null;
            }

            if (!ValidateManager() || string.IsNullOrEmpty(eventName))
            {
                return null;
            }

            AudioEvent eventToPlay = GetEventByName(eventName);
            if (eventToPlay == null)
            {
                Debug.LogWarning($"AudioManager: Event '{eventName}' not found. Make sure the AudioBank is loaded.");
                return null;
            }

            return PlayEventScheduled(eventToPlay, emitterObject, dspTime);
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
                // Scheduled playback from background thread: capture current dspTime to adjust delay
                double capturedDspTime = AudioSettings.dspTime;
                double adjustedDspTime = dspTime;
                if (dspTime > capturedDspTime)
                {
                    // If scheduled time is in the future, it's still valid
                    adjustedDspTime = dspTime;
                }
                else
                {
                    // If scheduled time has passed, play immediately
                    adjustedDspTime = capturedDspTime + 0.01; // Small delay to ensure proper scheduling
                }

                commandQueue.Enqueue(() => PlayEventScheduled(eventToPlay, emitterObject, adjustedDspTime));
                return null;
            }

            if (!ValidateManager() || !ValidateEvent(eventToPlay))
            {
                return null;
            }

            // Validate dspTime is not in the past
            double currentDspTime = AudioSettings.dspTime;
            if (dspTime < currentDspTime)
            {
                // If scheduled time has passed, play immediately
                dspTime = currentDspTime + 0.01;
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

            ActiveEvent tempEvent;
            for (int i = ActiveEvents.Count - 1; i >= 0; i--)
            {
                tempEvent = ActiveEvents[i];
                if (tempEvent.rootEvent == eventsToStop)
                {
                    tempEvent.Stop();
                }
            }
        }

        /// <summary>
        /// Stop all active instances of an audio event by name
        /// </summary>
        /// <param name="eventName">The name of the event to stop all instances of</param>
        public static void StopAll(string eventName)
        {
            if (Thread.CurrentThread.ManagedThreadId != mainThreadId)
            {
                string capturedName = eventName; // Capture for closure
                commandQueue.Enqueue(() => StopAll(capturedName));
                return;
            }

            if (!ValidateManager() || string.IsNullOrEmpty(eventName))
            {
                return;
            }

            AudioEvent eventToStop = GetEventByName(eventName);
            if (eventToStop == null)
            {
                Debug.LogWarning($"AudioManager: Event '{eventName}' not found. Make sure the AudioBank is loaded.");
                return;
            }

            StopAll(eventToStop);
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

            ActiveEvent tempEvent;
            for (int i = ActiveEvents.Count - 1; i >= 0; i--)
            {
                tempEvent = ActiveEvents[i];
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
            AudioSource tempSource;
            int sourceCount = sources.Count;
            for (int i = 0; i < sourceCount; i++)
            {
                tempSource = sources[i].source;
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
            previousEventsBuffer[previousEventsWriteIndex] = newEvent;
            previousEventsWriteIndex = (previousEventsWriteIndex + 1) % MaxPreviousEvents;
            if (previousEventsCount < MaxPreviousEvents)
            {
                previousEventsCount++;
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
                await UniTask.Delay(TimeSpan.FromSeconds(delay), ignoreTimeScale: false, cancellationToken: eventToRemove.GetCancellationToken());

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

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Cleanup();
            }
        }

        private void OnApplicationQuit()
        {
            Cleanup();
        }

        /// <summary>
        /// Cleanup resources on shutdown to prevent memory leaks
        /// </summary>
        private static void Cleanup()
        {
            // Stop all active events
            if (ActiveEvents != null)
            {
                ActiveEvent tempEvent;
                int eventCount = ActiveEvents.Count;
                for (int i = eventCount - 1; i >= 0; i--)
                {
                    if (i >= ActiveEvents.Count) continue;
                    tempEvent = ActiveEvents[i];
                    if (tempEvent != null)
                    {
                        tempEvent.StopImmediate();
                    }
                }
                ActiveEvents.Clear();
            }

            // Clear event name map
            lock (eventNameMapLock)
            {
                eventNameMap.Clear();
            }

            // Clear loaded banks tracking
            lock (loadedBanksLock)
            {
                loadedBanks.Clear();
            }

            // Clear memory tracking
            activeClipRefCount.Clear();
            clipMemoryCache.Clear();
            TotalMemoryUsage = 0;

            // Clear pools
            activeEventPool.Clear();
            availableSources.Clear();

            // Clear command queue
            while (commandQueue.TryDequeue(out _)) { }

            AllowCreateInstance = false;
        }

        private void Update()
        {
            // Process command queue with limit to prevent frame time spikes
            int commandProcessed = 0;
            const int maxCommandsPerFrame = 10;
            while (commandProcessed < maxCommandsPerFrame && commandQueue.TryDequeue(out Action command))
            {
                command.Invoke();
                commandProcessed++;
            }

            // Update active events with early exit optimization
            // Reverse iteration allows safe removal during iteration
            int eventCount = ActiveEvents.Count;
            ActiveEvent tempEvent;
            for (int i = eventCount - 1; i >= 0; i--)
            {
                if (i >= ActiveEvents.Count) continue; // Safety check for concurrent modifications

                tempEvent = ActiveEvents[i];
                if (tempEvent == null || tempEvent.status == EventStatus.Stopped)
                {
                    continue;
                }

                if (tempEvent.sources.Count == 0)
                {
                    continue;
                }

                tempEvent.Update();
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
            if (cachedAudioListener != null && cachedAudioListener.gameObject != null)
            {
                return;
            }

            cachedAudioListener = FindObjectOfType<AudioListener>();
            if (cachedAudioListener != null)
            {
                return;
            }

            Camera mainCamera = GetMainCamera();
            if (mainCamera != null)
            {
                Debug.Log("No AudioListener found. Creating one on the main camera.");
                cachedAudioListener = mainCamera.gameObject.AddComponent<AudioListener>();
            }
            else
            {
                Debug.LogWarning("No AudioListener or main camera found in the scene. Creating AudioListener on AudioManager. 3D audio positioning may be incorrect.");
                cachedAudioListener = this.gameObject.AddComponent<AudioListener>();
            }
        }

        private static Camera GetMainCamera()
        {
            int currentFrame = Time.frameCount;
            if (cachedMainCamera != null && cachedMainCamera.gameObject != null && cachedMainCameraFrame == currentFrame)
            {
                return cachedMainCamera;
            }

            cachedMainCamera = Camera.main;
            cachedMainCameraFrame = currentFrame;
            return cachedMainCamera;
        }

        /// <summary>
        /// On shutdown we cannot create an instance
        /// </summary>
        private static void HandleQuitting()
        {
            AllowCreateInstance = false;
        }

        /// <summary>
        /// Create the pool of AudioSources with platform-optimized settings
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
                // WebGL has limited audio capabilities, use smaller pool
                poolCount = 24;
#elif UNITY_ANDROID || UNITY_IOS
                // Mobile platforms: balance between performance and memory
                poolCount = 32;
#else
                // Desktop platforms: can handle larger pools
                poolCount = 80;
#endif
            }

            // Pre-allocate list capacity to avoid resizing
            if (sourcePool.Capacity < poolCount)
            {
                sourcePool.Capacity = poolCount;
            }

            for (int i = 0; i < poolCount; i++)
            {
                GameObject sourceGO = new GameObject($"AudioSource{i}");
                sourceGO.transform.localScale = new Vector3(0.1f, 0.1f, 0.1f);
                sourceGO.transform.SetParent(this.transform);
                AudioSource tempSource = sourceGO.AddComponent<AudioSource>();
                tempSource.playOnAwake = false;

#if UNITY_WEBGL || UNITY_ANDROID || UNITY_IOS
                // Optimize for mobile/WebGL: disable expensive features by default
                tempSource.bypassEffects = false;
                tempSource.bypassListenerEffects = false;
                tempSource.bypassReverbZones = false;
#endif

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
            // Cache TextMesh components to avoid repeated GetComponent calls
            foreach (var source in sourcePool)
            {
                if (source != null)
                {
#if UNITY_EDITOR
                    TextMesh tempText = source.GetComponent<TextMesh>();
                    if (tempText != null)
                    {
                        tempText.text = string.Empty;
                    }
#endif
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
            if (audioEvent == null || ActiveEvents == null)
            {
                return 0;
            }

            int tempCount = 0;
            int count = ActiveEvents.Count;
            ActiveEvent activeEvent;
            // Reverse iteration for better cache locality when checking many events
            for (int i = count - 1; i >= 0; i--)
            {
                activeEvent = ActiveEvents[i];
                if (activeEvent != null && activeEvent.rootEvent == audioEvent && activeEvent.status != EventStatus.Stopped)
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
            if (ActiveEvents == null) return;

            // Reverse iteration allows safe removal during iteration
            int count = ActiveEvents.Count;
            ActiveEvent tempEvent;
            for (int i = count - 1; i >= 0; i--)
            {
                if (i >= ActiveEvents.Count) continue; // Safety check

                tempEvent = ActiveEvents[i];
                if (tempEvent != null && tempEvent.rootEvent != null && tempEvent.rootEvent.Group == groupNum)
                {
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
            int maxAttempts = availableSources.Count;
            int attempts = 0;

            while (availableSources.Count > 0 && attempts < maxAttempts)
            {
                AudioSource tempSource = availableSources.Dequeue();
                if (tempSource != null && tempSource.gameObject != null)
                {
                    return tempSource;
                }
                // If source was destroyed, it's implicitly removed from the pool. Continue to the next.
                attempts++;
            }

            // Pool is empty or only contained destroyed sources.
            // Try to find any available source from the pool directly
            AudioSource source;
            int poolCount = sourcePool.Count;
            for (int i = 0; i < poolCount; i++)
            {
                source = sourcePool[i];
                if (source != null && source.gameObject != null && !source.isPlaying)
                {
                    return source;
                }
            }

            Debug.LogWarning("Audio source pool is empty. Consider increasing pool size.");
            return null;
        }


        private static long CalculateAudioClipMemoryUsage(AudioClip clip)
        {
            if (clip == null) return 0;
            return clip.samples * clip.channels * 2; // 16-bit samples
        }

        private static void TrackMemory(ActiveEvent activeEvent, bool isAdding)
        {
            reusableClipSet.Clear();
            int sourceCount = activeEvent.sources.Count;
            EventSource eventSource;
            for (int i = 0; i < sourceCount; i++)
            {
                eventSource = activeEvent.sources[i];
                if (eventSource != null && eventSource.source != null && eventSource.source.clip != null)
                {
                    reusableClipSet.Add(eventSource.source.clip);
                }
            }

            int direction = isAdding ? 1 : -1;
            foreach (var clip in reusableClipSet)
            {
                // Update reference count with optimized dictionary access
                if (!activeClipRefCount.TryGetValue(clip, out int count))
                {
                    count = 0;
                }
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

        /// <summary>
        /// Load an AudioBank and register all its events by name for fast string-based lookup.
        /// Note: This does NOT preload audio clips into memory. Audio clips are loaded on-demand when events are played.
        /// Thread-safe and can be called from any thread (queued to main thread if needed).
        /// </summary>
        /// <param name="bank">The AudioBank to load</param>
        /// <param name="overwriteExisting">If true, overwrite existing events with the same name. If false, skip duplicates.</param>
        public static void LoadBank(AudioBank bank, bool overwriteExisting = false)
        {
            if (bank == null)
            {
                Debug.LogWarning("AudioManager: Attempted to load null AudioBank.");
                return;
            }

            if (Thread.CurrentThread.ManagedThreadId != mainThreadId)
            {
                AudioBank capturedBank = bank; // Capture for closure
                commandQueue.Enqueue(() => LoadBank(capturedBank, overwriteExisting));
                return;
            }

            if (!ValidateManager())
            {
                return;
            }

            List<AudioEvent> events = bank.AudioEvents;
            if (events == null || events.Count == 0)
            {
                Debug.LogWarning($"AudioManager: AudioBank '{bank.name}' contains no events.");
                return;
            }

            lock (eventNameMapLock)
            {
                // First pass: detect duplicate names within the same bank (using reusable collection)
                reusableDuplicateCheck.Clear();
                AudioEvent audioEvent;
                int eventCount = events.Count;

                for (int i = 0; i < eventCount; i++)
                {
                    audioEvent = events[i];
                    if (audioEvent == null || string.IsNullOrEmpty(audioEvent.name))
                    {
                        continue;
                    }

                    string eventName = audioEvent.name;
                    if (!reusableDuplicateCheck.ContainsKey(eventName))
                    {
                        reusableDuplicateCheck[eventName] = new List<AudioEvent>();
                    }
                    reusableDuplicateCheck[eventName].Add(audioEvent);
                }

                foreach (var kvp in reusableDuplicateCheck)
                {
                    if (kvp.Value.Count > 1)
                    {
                        Debug.LogError($"AudioManager: AudioBank '{bank.name}' contains {kvp.Value.Count} events with the same name '{kvp.Key}'. " +
                            $"Only the first one will be accessible via PlayEvent(string). Please rename the duplicate events.");
                    }
                }

                // Second pass: register events (only first occurrence of each name within the bank)
                reusableBankRegisteredNames.Clear();
                int registeredCount = 0;
                int skippedCount = 0;

                for (int i = 0; i < eventCount; i++)
                {
                    audioEvent = events[i];
                    if (audioEvent == null || string.IsNullOrEmpty(audioEvent.name))
                    {
                        continue;
                    }

                    string eventName = audioEvent.name;

                    if (reusableBankRegisteredNames.Contains(eventName))
                    {
                        skippedCount++;
                        continue;
                    }

                    if (eventNameMap.ContainsKey(eventName))
                    {
                        if (overwriteExisting)
                        {
                            eventNameMap[eventName] = audioEvent;
                            reusableBankRegisteredNames.Add(eventName);
                            registeredCount++;
                        }
                        else
                        {
                            skippedCount++;
                        }
                    }
                    else
                    {
                        eventNameMap[eventName] = audioEvent;
                        reusableBankRegisteredNames.Add(eventName);
                        registeredCount++;
                    }
                }

                lock (loadedBanksLock)
                {
                    loadedBanks.Add(bank);
                }

                if (registeredCount > 0)
                {
                    Debug.Log($"AudioManager: Loaded {registeredCount} events from AudioBank '{bank.name}'.");
                }
                if (skippedCount > 0)
                {
                    Debug.LogWarning($"AudioManager: Skipped {skippedCount} duplicate event names from AudioBank '{bank.name}'. " +
                        $"Some may be duplicates within the bank, others may conflict with already loaded events. Use overwriteExisting=true to overwrite external conflicts.");
                }
            }
        }

        /// <summary>
        /// Unload an AudioBank and remove all its events from the name map.
        /// Note: This does NOT stop currently playing audio from this bank. Use UnloadBankAndStopEvents if you want to stop them.
        /// Thread-safe and can be called from any thread (queued to main thread if needed).
        /// </summary>
        /// <param name="bank">The AudioBank to unload</param>
        public static void UnloadBank(AudioBank bank)
        {
            if (bank == null)
            {
                return;
            }

            if (Thread.CurrentThread.ManagedThreadId != mainThreadId)
            {
                AudioBank capturedBank = bank; // Capture for closure
                commandQueue.Enqueue(() => UnloadBank(capturedBank));
                return;
            }

            if (!ValidateManager())
            {
                return;
            }

            List<AudioEvent> events = bank.AudioEvents;
            if (events == null || events.Count == 0)
            {
                return;
            }

            lock (eventNameMapLock)
            {
                AudioEvent audioEvent;
                int eventCount = events.Count;
                int removedCount = 0;

                for (int i = 0; i < eventCount; i++)
                {
                    audioEvent = events[i];
                    if (audioEvent == null || string.IsNullOrEmpty(audioEvent.name))
                    {
                        continue;
                    }

                    string eventName = audioEvent.name;
                    if (eventNameMap.TryGetValue(eventName, out AudioEvent mappedEvent) && mappedEvent == audioEvent)
                    {
                        eventNameMap.Remove(eventName);
                        removedCount++;
                    }
                }

                // Remove from loaded banks tracking
                lock (loadedBanksLock)
                {
                    loadedBanks.Remove(bank);
                }

                if (removedCount > 0)
                {
                    Debug.Log($"AudioManager: Unloaded {removedCount} events from AudioBank '{bank.name}'. Note: Currently playing audio will continue.");
                }
            }
        }

        /// <summary>
        /// Unload an AudioBank, remove all its events from the name map, and stop all active events from this bank.
        /// Thread-safe and can be called from any thread (queued to main thread if needed).
        /// </summary>
        /// <param name="bank">The AudioBank to unload</param>
        public static void UnloadBankAndStopEvents(AudioBank bank)
        {
            if (bank == null)
            {
                return;
            }

            if (Thread.CurrentThread.ManagedThreadId != mainThreadId)
            {
                AudioBank capturedBank = bank; // Capture for closure
                commandQueue.Enqueue(() => UnloadBankAndStopEvents(capturedBank));
                return;
            }

            if (!ValidateManager())
            {
                return;
            }

            List<AudioEvent> events = bank.AudioEvents;
            if (events == null || events.Count == 0)
            {
                return;
            }

            // First, stop all active events from this bank
            if (ActiveEvents != null && ActiveEvents.Count > 0)
            {
                ActiveEvent tempEvent;
                int activeCount = ActiveEvents.Count;
                int stoppedCount = 0;

                for (int i = activeCount - 1; i >= 0; i--)
                {
                    if (i >= ActiveEvents.Count) continue;
                    tempEvent = ActiveEvents[i];
                    if (tempEvent != null && tempEvent.rootEvent != null && events.Contains(tempEvent.rootEvent))
                    {
                        tempEvent.StopImmediate();
                        stoppedCount++;
                    }
                }

                if (stoppedCount > 0)
                {
                    Debug.Log($"AudioManager: Stopped {stoppedCount} active events from AudioBank '{bank.name}'.");
                }
            }

            // Then unload the bank (this will remove from name map)
            UnloadBank(bank);
        }

        /// <summary>
        /// Get an AudioEvent by name.
        /// </summary>
        /// <param name="eventName">The name of the event to find</param>
        /// <returns>The AudioEvent if found, null otherwise</returns>
        public static AudioEvent GetEventByName(string eventName)
        {
            if (string.IsNullOrEmpty(eventName))
            {
                return null;
            }

            // Fast path: minimize lock time by doing lookup only
            lock (eventNameMapLock)
            {
                if (eventNameMap.TryGetValue(eventName, out AudioEvent result))
                {
                    return result;
                }
            }
            return null;
        }

        /// <summary>
        /// Clear all registered events from the name map. Use with caution.
        /// Thread-safe and can be called from any thread (queued to main thread if needed).
        /// </summary>
        public static void ClearEventNameMap()
        {
            if (Thread.CurrentThread.ManagedThreadId != mainThreadId)
            {
                commandQueue.Enqueue(() => ClearEventNameMap());
                return;
            }

            lock (eventNameMapLock)
            {
                int count = eventNameMap.Count;
                eventNameMap.Clear();
                if (count > 0)
                {
                    Debug.Log($"AudioManager: Cleared {count} events from name map.");
                }
            }
        }

        /// <summary>
        /// Get the number of registered events in the name map.
        /// Thread-safe.
        /// </summary>
        public static int GetRegisteredEventCount()
        {
            lock (eventNameMapLock)
            {
                return eventNameMap.Count;
            }
        }

        /// <summary>
        /// Validate an AudioBank for duplicate event names within the bank.
        /// Returns a dictionary mapping duplicate names to their event lists.
        /// Thread-safe.
        /// </summary>
        /// <param name="bank">The AudioBank to validate</param>
        /// <returns>Dictionary of duplicate names and their events. Empty if no duplicates.</returns>
        public static Dictionary<string, List<AudioEvent>> ValidateBankForDuplicateNames(AudioBank bank)
        {
            Dictionary<string, List<AudioEvent>> duplicates = new Dictionary<string, List<AudioEvent>>();

            if (bank == null || bank.AudioEvents == null)
            {
                return duplicates;
            }

            Dictionary<string, List<AudioEvent>> nameGroups = new Dictionary<string, List<AudioEvent>>();
            AudioEvent audioEvent;
            int eventCount = bank.AudioEvents.Count;

            for (int i = 0; i < eventCount; i++)
            {
                audioEvent = bank.AudioEvents[i];
                if (audioEvent == null || string.IsNullOrEmpty(audioEvent.name))
                {
                    continue;
                }

                string eventName = audioEvent.name;
                if (!nameGroups.ContainsKey(eventName))
                {
                    nameGroups[eventName] = new List<AudioEvent>();
                }
                nameGroups[eventName].Add(audioEvent);
            }

            foreach (var kvp in nameGroups)
            {
                if (kvp.Value.Count > 1)
                {
                    duplicates[kvp.Key] = kvp.Value;
                }
            }

            return duplicates;
        }

        /// <summary>
        /// Get all loaded banks. Thread-safe.
        /// </summary>
        public static IReadOnlyCollection<AudioBank> GetLoadedBanks()
        {
            lock (loadedBanksLock)
            {
                return new List<AudioBank>(loadedBanks).AsReadOnly();
            }
        }

        /// <summary>
        /// Get the number of loaded banks. Thread-safe.
        /// </summary>
        public static int GetLoadedBankCount()
        {
            lock (loadedBanksLock)
            {
                return loadedBanks.Count;
            }
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