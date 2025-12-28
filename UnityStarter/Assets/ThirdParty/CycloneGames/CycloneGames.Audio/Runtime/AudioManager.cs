// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using UnityEngine;
using UnityEngine.Audio;
using System.Globalization;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace CycloneGames.Audio.Runtime
{
    /// <summary>
    /// High-performance audio event playback manager with thread-safe operations
    /// </summary>
    public class AudioManager : MonoBehaviour, IAudioService
    {
        public static AudioManager Instance { get; private set; }

        private static bool AllowCreateInstance = true;
        private static int mainThreadId;
        private static AudioListener cachedAudioListener;
        private static Camera cachedMainCamera;
        private static int cachedMainCameraFrame = -1;

        // Static constructor ensures mainThreadId is set when class is first accessed
        static AudioManager()
        {
            mainThreadId = Thread.CurrentThread.ManagedThreadId;
        }

        public static List<ActiveEvent> ActiveEvents { get; private set; }

        private static readonly Stack<ActiveEvent> activeEventPool = new Stack<ActiveEvent>(64);
        private static readonly Queue<AudioSource> availableSources = new Queue<AudioSource>();
        public static IReadOnlyCollection<AudioSource> AvailableSources => availableSources;
        private static readonly ConcurrentQueue<Action> commandQueue = new ConcurrentQueue<Action>();
        private static readonly ActiveEvent[] previousEventsBuffer = new ActiveEvent[MaxPreviousEvents];
        private static int previousEventsWriteIndex;
        private static int previousEventsCount;

        public static int CurrentLanguage { get; private set; }
        public static string[] Languages;

        [Header("Pool Configuration")]
        [Tooltip("Set to 0 to use auto-detected pool size based on device. Set > 0 to override with fixed size.")]
        [SerializeField] private int customPoolSize = 0;
        
        [Header("Mixing")]
        [SerializeField] private AudioMixer mainMixer;
        private static AudioMixer staticMainMixer;

        private static bool isInitialized;
        private static readonly List<AudioSource> sourcePool = new List<AudioSource>();
        public static IReadOnlyList<AudioSource> SourcePool => sourcePool;

        // Smart pool management
        private static AudioPoolConfig activePoolConfig;
        private static int initialPoolSize;
        private static int currentPoolSize;
        private static int maxPoolSize;
        private static float lastPoolExpansionTime;
        private static float lastHighUsageTime;
        private static float lastShrinkTime;

        // Pool statistics
        private static int peakPoolUsage;
        private static int totalExpansions;
        private static int totalSteals;


        // Memory tracking - use struct-based approach for better cache coherence
        private static readonly Dictionary<AudioClip, int> activeClipRefCount = new Dictionary<AudioClip, int>();
        private static readonly Dictionary<AudioClip, long> clipMemoryCache = new Dictionary<AudioClip, long>();
        private static readonly HashSet<AudioClip> reusableClipSet = new HashSet<AudioClip>();

        // O(1) lookup and removal for event names
        private static readonly ConcurrentDictionary<string, AudioEvent> eventNameMap = new ConcurrentDictionary<string, AudioEvent>();

        // O(1) bank tracking with instance ID as key for fast unload
        private static readonly ConcurrentDictionary<int, AudioBank> loadedBanks = new ConcurrentDictionary<int, AudioBank>();

        // Reusable collections to avoid allocations during bank loading
        private static readonly Dictionary<string, List<AudioEvent>> reusableDuplicateCheck = new Dictionary<string, List<AudioEvent>>();
        private static readonly HashSet<string> reusableBankRegisteredNames = new HashSet<string>();

        public static long TotalMemoryUsage { get; private set; }
        public static IReadOnlyDictionary<AudioClip, int> ActiveClipRefCount => activeClipRefCount;
        public static IReadOnlyDictionary<AudioClip, long> ClipMemoryCache => clipMemoryCache;

        private static bool debugMode;
        public static bool DebugMode => debugMode;
        private const int MaxPreviousEvents = 300;

#if UNITY_ANDROID || UNITY_IOS
        private static float lastMobileUpdateTime;
        private const float MobileUpdateInterval = 0.008f; // ~120Hz max to save battery
#endif

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private static long totalEventsPlayed;
        private static int peakActiveEvents;
#endif

        /// <summary>
        /// Runtime pool statistics for monitoring and debugging.
        /// </summary>
        public static class PoolStats
        {
            public static int InitialSize => initialPoolSize;
            public static int CurrentSize => currentPoolSize;
            public static int MaxSize => maxPoolSize;
            public static int InUse => currentPoolSize - availableSources.Count;
            public static int Available => availableSources.Count;
            public static float UsageRatio => currentPoolSize > 0 ? (float)InUse / currentPoolSize : 0f;
            public static int PeakUsage => peakPoolUsage;
            public static int TotalExpansions => totalExpansions;
            public static int TotalSteals => totalSteals;
            public static string DeviceTier => activePoolConfig?.GetDeviceTierName() ?? AudioPoolConfig.GetDefaultDeviceTierName();
            public static bool HasConfig => activePoolConfig != null;
        }

        private static List<ActiveEvent> cachedPreviousEventsList;
        private static int cachedPreviousEventsVersion = -1;

        public static IReadOnlyList<ActiveEvent> GetPreviousEvents()
        {
            int currentVersion = previousEventsWriteIndex;
            if (cachedPreviousEventsList != null && cachedPreviousEventsVersion == currentVersion)
            {
                return cachedPreviousEventsList;
            }

            cachedPreviousEventsList ??= new List<ActiveEvent>(MaxPreviousEvents);
            cachedPreviousEventsList.Clear();

            int startIndex = previousEventsCount < MaxPreviousEvents ? 0 : previousEventsWriteIndex;
            int count = previousEventsCount;
            for (int i = 0; i < count; i++)
            {
                int index = (startIndex + i) % MaxPreviousEvents;
                if (previousEventsBuffer[index] != null)
                {
                    cachedPreviousEventsList.Add(previousEventsBuffer[index]);
                }
            }

            cachedPreviousEventsVersion = currentVersion;
            return cachedPreviousEventsList;
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
                mainMixer.SetFloat(parameterName, volume);
            else
                Debug.LogWarning("AudioManager: Main Mixer not assigned.");
        }

        public float GetMixerVolume(string parameterName)
        {
            if (mainMixer != null && mainMixer.GetFloat(parameterName, out float value))
                return value;
            return 0f;
        }

        /// <summary>
        /// Start playing an AudioEvent. Thread-safe.
        /// </summary>
        public static ActiveEvent PlayEvent(AudioEvent eventToPlay, GameObject emitterObject)
        {
            if (Thread.CurrentThread.ManagedThreadId != mainThreadId)
            {
                commandQueue.Enqueue(() => PlayEvent(eventToPlay, emitterObject));
                return null;
            }

            if (!ValidateManager() || !ValidateEvent(eventToPlay)) return null;

            Transform emitterTransform = emitterObject != null ? emitterObject.transform : null;
            ActiveEvent tempEvent = GetActiveEventFromPool(eventToPlay, emitterTransform);
            tempEvent.Play();

            if (tempEvent.status != EventStatus.Error)
                TrackMemory(tempEvent, true);

            return tempEvent;
        }

        public static ActiveEvent PlayEvent(AudioEvent eventToPlay, Vector3 position)
        {
            if (Thread.CurrentThread.ManagedThreadId != mainThreadId)
            {
                commandQueue.Enqueue(() => PlayEvent(eventToPlay, position));
                return null;
            }

            if (!ValidateManager() || !ValidateEvent(eventToPlay)) return null;

            ActiveEvent tempEvent = GetActiveEventFromPool(eventToPlay, null);
            tempEvent.Play();
            tempEvent.SetAllSourcePositions(position);

            if (tempEvent.status != EventStatus.Error)
                TrackMemory(tempEvent, true);

            return tempEvent;
        }

        public static ActiveEvent PlayEvent(string eventName, GameObject emitterObject)
        {
            if (Thread.CurrentThread.ManagedThreadId != mainThreadId)
            {
                string capturedName = eventName;
                commandQueue.Enqueue(() => PlayEvent(capturedName, emitterObject));
                return null;
            }

            if (!ValidateManager() || string.IsNullOrEmpty(eventName)) return null;

            AudioEvent eventToPlay = GetEventByName(eventName);
            if (eventToPlay == null)
            {
                Debug.LogWarning($"AudioManager: Event '{eventName}' not found.");
                return null;
            }

            return PlayEvent(eventToPlay, emitterObject);
        }

        public static ActiveEvent PlayEvent(string eventName, Vector3 position)
        {
            if (Thread.CurrentThread.ManagedThreadId != mainThreadId)
            {
                string capturedName = eventName;
                commandQueue.Enqueue(() => PlayEvent(capturedName, position));
                return null;
            }

            if (!ValidateManager() || string.IsNullOrEmpty(eventName)) return null;

            AudioEvent eventToPlay = GetEventByName(eventName);
            if (eventToPlay == null)
            {
                Debug.LogWarning($"AudioManager: Event '{eventName}' not found.");
                return null;
            }

            return PlayEvent(eventToPlay, position);
        }

        /// <summary>
        /// Play audio at precise DSP time for rhythm games
        /// </summary>
        public static ActiveEvent PlayEventScheduled(string eventName, GameObject emitterObject, double dspTime)
        {
            if (Thread.CurrentThread.ManagedThreadId != mainThreadId)
            {
                string capturedName = eventName;
                double capturedDspTime = AudioSettings.dspTime;
                double adjustedDspTime = dspTime > capturedDspTime ? dspTime : capturedDspTime + 0.01;
                commandQueue.Enqueue(() => PlayEventScheduled(capturedName, emitterObject, adjustedDspTime));
                return null;
            }

            if (!ValidateManager() || string.IsNullOrEmpty(eventName)) return null;

            AudioEvent eventToPlay = GetEventByName(eventName);
            if (eventToPlay == null)
            {
                Debug.LogWarning($"AudioManager: Event '{eventName}' not found.");
                return null;
            }

            return PlayEventScheduled(eventToPlay, emitterObject, dspTime);
        }

        public static ActiveEvent PlayEventScheduled(AudioEvent eventToPlay, GameObject emitterObject, double dspTime)
        {
            if (Thread.CurrentThread.ManagedThreadId != mainThreadId)
            {
                double capturedDspTime = AudioSettings.dspTime;
                double adjustedDspTime = dspTime > capturedDspTime ? dspTime : capturedDspTime + 0.01;
                commandQueue.Enqueue(() => PlayEventScheduled(eventToPlay, emitterObject, adjustedDspTime));
                return null;
            }

            if (!ValidateManager() || !ValidateEvent(eventToPlay)) return null;

            double currentDspTime = AudioSettings.dspTime;
            if (dspTime < currentDspTime) dspTime = currentDspTime + 0.01;

            Transform emitterTransform = emitterObject != null ? emitterObject.transform : null;
            ActiveEvent tempEvent = GetActiveEventFromPool(eventToPlay, emitterTransform);
            tempEvent.scheduledDspTime = dspTime;
            tempEvent.Play();

            if (tempEvent.status != EventStatus.Error)
                TrackMemory(tempEvent, true);

            return tempEvent;
        }

        private static ActiveEvent GetActiveEventFromPool(AudioEvent eventToPlay, Transform emitter)
        {
            ActiveEvent activeEvent = activeEventPool.Count > 0 ? activeEventPool.Pop() : new ActiveEvent();
            activeEvent.Initialize(eventToPlay, emitter);
            return activeEvent;
        }

        [Obsolete("Playing on AudioSource is deprecated")]
        public static ActiveEvent PlayEvent(AudioEvent eventToPlay, AudioSource emitter)
        {
            if (Thread.CurrentThread.ManagedThreadId != mainThreadId)
            {
                commandQueue.Enqueue(() => PlayEvent(eventToPlay, emitter));
                return null;
            }

            Debug.LogWarning("AudioManager: Deprecated - play on AudioSource no longer supported");
            if (!ValidateManager() || !ValidateEvent(eventToPlay)) return null;

            ActiveEvent tempEvent = GetActiveEventFromPool(eventToPlay, emitter.transform);
            tempEvent.Play();

            if (tempEvent.status != EventStatus.Error)
                TrackMemory(tempEvent, true);

            return tempEvent;
        }

        public static void StopAll(AudioEvent eventsToStop)
        {
            if (Thread.CurrentThread.ManagedThreadId != mainThreadId)
            {
                commandQueue.Enqueue(() => StopAll(eventsToStop));
                return;
            }

            if (!ValidateManager()) return;

            for (int i = ActiveEvents.Count - 1; i >= 0; i--)
            {
                var tempEvent = ActiveEvents[i];
                if (tempEvent.rootEvent == eventsToStop)
                    tempEvent.Stop();
            }
        }

        public static void StopAll(string eventName)
        {
            if (Thread.CurrentThread.ManagedThreadId != mainThreadId)
            {
                string capturedName = eventName;
                commandQueue.Enqueue(() => StopAll(capturedName));
                return;
            }

            if (!ValidateManager() || string.IsNullOrEmpty(eventName)) return;

            AudioEvent eventToStop = GetEventByName(eventName);
            if (eventToStop == null)
            {
                Debug.LogWarning($"AudioManager: Event '{eventName}' not found.");
                return;
            }

            StopAll(eventToStop);
        }

        public static void StopAll(int groupNum)
        {
            if (Thread.CurrentThread.ManagedThreadId != mainThreadId)
            {
                commandQueue.Enqueue(() => StopAll(groupNum));
                return;
            }

            if (!ValidateManager()) return;

            for (int i = ActiveEvents.Count - 1; i >= 0; i--)
            {
                var tempEvent = ActiveEvents[i];
                if (tempEvent.rootEvent.Group == groupNum)
                    tempEvent.Stop();
            }
        }

        public static void RemoveActiveEvent(ActiveEvent stoppedEvent)
        {
            if (!ValidateManager()) return;

            // Return AudioSources to pool
            int sourceCount = stoppedEvent.SourceCount;
            for (int i = 0; i < sourceCount; i++)
            {
                var es = stoppedEvent.GetSource(i);
                if (es.IsValid)
                    availableSources.Enqueue(es.source);
            }

            TrackMemory(stoppedEvent, false);
            ActiveEvents.Remove(stoppedEvent);

            stoppedEvent.Reset();
            activeEventPool.Push(stoppedEvent);
        }

        public static void AddPreviousEvent(ActiveEvent newEvent)
        {
            previousEventsBuffer[previousEventsWriteIndex] = newEvent;
            previousEventsWriteIndex = (previousEventsWriteIndex + 1) % MaxPreviousEvents;
            if (previousEventsCount < MaxPreviousEvents) previousEventsCount++;
        }

        public static void UpdateLanguages()
        {
            CultureInfo[] cultures = CultureInfo.GetCultures(CultureTypes.AllCultures);
            Languages = new string[cultures.Length];
            for (int i = 0; i < cultures.Length; i++)
                Languages[i] = cultures[i].Name;
        }

        public static void SetDebugMode(bool toggle)
        {
            debugMode = toggle;
            ClearSourceText();
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        internal static void TrackEventPlayed()
        {
            Interlocked.Increment(ref totalEventsPlayed);
            int currentActive = ActiveEvents.Count;
            int peak = peakActiveEvents;
            if (currentActive > peak)
                Interlocked.CompareExchange(ref peakActiveEvents, currentActive, peak);
        }

        public struct PoolStatistics
        {
            public int ActiveEventPoolSize;
            public int AvailableSourcesCount;
            public int ActiveEventsCount;
            public int SourcePoolSize;
            public long TotalEventsPlayed;
            public int PeakActiveEvents;
            public long TotalMemoryUsage;
            public int LoadedBanksCount;
        }

        public static PoolStatistics GetPoolStatistics()
        {
            return new PoolStatistics
            {
                ActiveEventPoolSize = activeEventPool.Count,
                AvailableSourcesCount = availableSources.Count,
                ActiveEventsCount = ActiveEvents?.Count ?? 0,
                SourcePoolSize = sourcePool.Count,
                TotalEventsPlayed = Interlocked.Read(ref totalEventsPlayed),
                PeakActiveEvents = peakActiveEvents,
                TotalMemoryUsage = TotalMemoryUsage,
                LoadedBanksCount = loadedBanks.Count
            };
        }

        public static void ResetPoolStatistics()
        {
            Interlocked.Exchange(ref totalEventsPlayed, 0);
            Interlocked.Exchange(ref peakActiveEvents, 0);
        }
#endif

        public static async void DelayRemoveActiveEvent(ActiveEvent eventToRemove, float delay = 1)
        {
            if (!ValidateManager()) return;

            try
            {
                await UniTask.Delay(TimeSpan.FromSeconds(delay), ignoreTimeScale: false, cancellationToken: eventToRemove.GetCancellationToken());
                if (eventToRemove.status != EventStatus.Error)
                    RemoveActiveEvent(eventToRemove);
            }
            catch (OperationCanceledException)
            {
                // Expected when event stopped abruptly
            }
        }

        #endregion

        #region Unity Lifecycle

        private void Awake()
        {
            mainThreadId = Thread.CurrentThread.ManagedThreadId;

            if (Instance != null)
            {
                if (Instance != this) Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);

            if (!isInitialized) Initialize();
        }

        private void OnDestroy()
        {
            if (Instance == this) Cleanup();
        }

        private void OnApplicationQuit() => Cleanup();

        private static void Cleanup()
        {
            if (ActiveEvents != null)
            {
                for (int i = ActiveEvents.Count - 1; i >= 0; i--)
                {
                    if (i >= ActiveEvents.Count) continue;
                    ActiveEvents[i]?.StopImmediate();
                }
                ActiveEvents.Clear();
            }

            eventNameMap.Clear();
            loadedBanks.Clear();
            activeClipRefCount.Clear();
            clipMemoryCache.Clear();
            TotalMemoryUsage = 0;
            activeEventPool.Clear();
            availableSources.Clear();
            while (commandQueue.TryDequeue(out _)) { }
            AllowCreateInstance = false;
        }

        private void Update()
        {
#if UNITY_ANDROID || UNITY_IOS
            // Throttle updates on mobile to save battery
            float currentTime = Time.unscaledTime;
            if (currentTime - lastMobileUpdateTime < MobileUpdateInterval) return;
            lastMobileUpdateTime = currentTime;
#endif

            // Process command queue with frame limit
            int commandProcessed = 0;
            const int maxCommandsPerFrame = 10;
            while (commandProcessed < maxCommandsPerFrame && commandQueue.TryDequeue(out Action command))
            {
                command.Invoke();
                commandProcessed++;
            }

            // Update active events
            int eventCount = ActiveEvents.Count;
            for (int i = eventCount - 1; i >= 0; i--)
            {
                if (i >= ActiveEvents.Count) continue;
                var tempEvent = ActiveEvents[i];
                if (tempEvent == null || tempEvent.status == EventStatus.Stopped) continue;
                if (tempEvent.SourceCount == 0) continue;
                tempEvent.Update();
            }

            // Track peak usage
            int currentUsage = PoolStats.InUse;
            if (currentUsage > peakPoolUsage) peakPoolUsage = currentUsage;

            // Smart pool shrinking
            TryShrinkPool();
        }

        #endregion

        #region Initialization

        private static void CreateInstance()
        {
            if (Instance != null || !AllowCreateInstance) return;
            new GameObject("AudioManager").AddComponent<AudioManager>();
        }

        private void Initialize()
        {
            CurrentLanguage = 0;
            staticMainMixer = mainMixer;
            ActiveEvents = new List<ActiveEvent>(64);
            
            // Initialize smart pool configuration (auto-discovery only)
            activePoolConfig = AudioPoolConfig.FindConfig();
            InitializeSmartPool();
            
            ValidateAudioListener();
            Application.quitting += HandleQuitting;
            isInitialized = true;
            
            string configSource = activePoolConfig != null ? "Auto-discovered" : "Defaults";
            Debug.Log($"AudioManager: Initialized with {currentPoolSize} sources (max: {maxPoolSize}, tier: {PoolStats.DeviceTier}, config: {configSource})");
        }

        private void InitializeSmartPool()
        {
            // Determine pool sizes based on configuration and device
            if (customPoolSize > 0)
            {
                // Custom pool size overrides everything
                initialPoolSize = customPoolSize;
                maxPoolSize = customPoolSize;
            }
            else if (activePoolConfig != null)
            {
                // Use config asset values
                initialPoolSize = activePoolConfig.GetInitialPoolSizeForPlatform();
                maxPoolSize = activePoolConfig.GetMaxPoolSizeForDevice();
            }
            else
            {
                // Use static defaults (no config exists)
                initialPoolSize = AudioPoolConfig.GetDefaultInitialPoolSizeForPlatform();
                maxPoolSize = AudioPoolConfig.GetDefaultMaxPoolSizeForDevice();
            }

            currentPoolSize = 0;
            peakPoolUsage = 0;
            totalExpansions = 0;
            totalSteals = 0;
            lastHighUsageTime = Time.time;

            // Pre-allocate capacity
            if (sourcePool.Capacity < maxPoolSize)
                sourcePool.Capacity = maxPoolSize;

            // Create initial sources
            CreateSources(initialPoolSize);
        }

        /// <summary>
        /// Reload pool configuration from a new AudioPoolConfig.
        /// Call this after hot-updating the config to apply new settings.
        /// Note: This only updates pool size limits, existing sources are preserved.
        /// </summary>
        public static void ReloadPoolConfig()
        {
            // Re-fetch config (will get the one set via SetConfig)
            activePoolConfig = AudioPoolConfig.FindConfig();
            
            // Update pool limits based on new config
            if (activePoolConfig != null)
            {
                int newInitial = activePoolConfig.GetInitialPoolSizeForPlatform();
                int newMax = activePoolConfig.GetMaxPoolSizeForDevice();
                
                // Only update if not using custom override
                if (Instance != null && Instance.customPoolSize <= 0)
                {
                    initialPoolSize = newInitial;
                    maxPoolSize = newMax;
                    
                    // Update capacity if needed
                    if (sourcePool.Capacity < maxPoolSize)
                        sourcePool.Capacity = maxPoolSize;
                    
                    Debug.Log($"AudioManager: Pool config reloaded (initial: {initialPoolSize}, max: {maxPoolSize}, tier: {PoolStats.DeviceTier})");
                }
            }
        }

        private void ValidateAudioListener()
        {
            if (cachedAudioListener != null && cachedAudioListener.gameObject != null) return;

            cachedAudioListener = FindObjectOfType<AudioListener>();
            if (cachedAudioListener != null) return;

            Camera mainCamera = GetMainCamera();
            if (mainCamera != null)
            {
                Debug.Log("No AudioListener found. Creating one on the main camera.");
                cachedAudioListener = mainCamera.gameObject.AddComponent<AudioListener>();
            }
            else
            {
                Debug.LogWarning("No AudioListener or main camera found. Creating AudioListener on AudioManager.");
                cachedAudioListener = gameObject.AddComponent<AudioListener>();
            }
        }

        private static Camera GetMainCamera()
        {
            int currentFrame = Time.frameCount;
            if (cachedMainCamera != null && cachedMainCamera.gameObject != null && cachedMainCameraFrame == currentFrame)
                return cachedMainCamera;

            cachedMainCamera = Camera.main;
            cachedMainCameraFrame = currentFrame;
            return cachedMainCamera;
        }

        private static void HandleQuitting() => AllowCreateInstance = false;

        private void CreateSources(int count)
        {
            int startIndex = currentPoolSize;
            
            for (int i = 0; i < count; i++)
            {
                var sourceGO = new GameObject($"AudioSource{startIndex + i}");
                sourceGO.transform.localScale = new Vector3(0.1f, 0.1f, 0.1f);
                sourceGO.transform.SetParent(transform);
                var tempSource = sourceGO.AddComponent<AudioSource>();
                tempSource.playOnAwake = false;

#if UNITY_WEBGL || UNITY_ANDROID || UNITY_IOS
                tempSource.bypassEffects = false;
                tempSource.bypassListenerEffects = false;
                tempSource.bypassReverbZones = false;
#endif

                sourcePool.Add(tempSource);
                availableSources.Enqueue(tempSource);

#if UNITY_EDITOR
                var newText = sourceGO.AddComponent<TextMesh>();
                newText.characterSize = 0.2f;
#endif
            }
            
            currentPoolSize += count;
        }

        /// <summary>
        /// Attempt to expand the pool when more sources are needed.
        /// Returns true if expansion was successful.
        /// </summary>
        private bool TryExpandPool()
        {
            if (currentPoolSize >= maxPoolSize) return false;

            int increment = activePoolConfig?.ExpansionIncrement ?? AudioPoolConfig.DefaultExpansionIncrement;
            int actualIncrement = Mathf.Min(increment, maxPoolSize - currentPoolSize);

            if (actualIncrement <= 0) return false;

            CreateSources(actualIncrement);
            lastPoolExpansionTime = Time.time;
            totalExpansions++;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Debug.Log($"AudioManager: Pool expanded to {currentPoolSize}/{maxPoolSize} sources (expansion #{totalExpansions})");
#endif
            return true;
        }

        /// <summary>
        /// Attempt to steal a source from the oldest, lowest-priority playing event.
        /// Returns the stolen source or null if no suitable source found.
        /// </summary>
        private static AudioSource TryStealSource()
        {
            if (ActiveEvents == null || ActiveEvents.Count == 0) return null;

            ActiveEvent oldest = null;
            float oldestTime = float.MaxValue;

            for (int i = 0; i < ActiveEvents.Count; i++)
            {
                var evt = ActiveEvents[i];
                if (evt == null || evt.status == EventStatus.Stopped) continue;
                if (evt.rootEvent == null) continue;
                
                // Skip high-priority and looping events
                if (evt.rootEvent.Output != null && evt.rootEvent.Output.loop) continue;
                
                if (evt.timeStarted < oldestTime)
                {
                    oldestTime = evt.timeStarted;
                    oldest = evt;
                }
            }

            if (oldest != null && oldest.SourceCount > 0)
            {
                var source = oldest.GetSource(0);
                if (source.IsValid)
                {
                    oldest.StopImmediate();
                    totalSteals++;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
                    Debug.LogWarning($"AudioManager: Voice stolen from '{oldest.name}' (total steals: {totalSteals})");
#endif
                    return source.source;
                }
            }

            return null;
        }

        /// <summary>
        /// Shrink the pool if it's been idle for a while.
        /// Called during Update to gradually release unused sources.
        /// </summary>
        private void TryShrinkPool()
        {
            if (currentPoolSize <= initialPoolSize) return;

            float currentTime = Time.time;
            float idleTime = currentTime - lastHighUsageTime;
            float usageRatio = PoolStats.UsageRatio;

            // Get thresholds from config or use defaults
            float shrinkUsageThreshold = activePoolConfig?.ShrinkUsageThreshold ?? AudioPoolConfig.DefaultShrinkUsageThreshold;
            float shrinkIdleThreshold = activePoolConfig?.ShrinkIdleThreshold ?? AudioPoolConfig.DefaultShrinkIdleThreshold;
            float shrinkInterval = activePoolConfig?.ShrinkInterval ?? AudioPoolConfig.DefaultShrinkInterval;

            // Update high usage time if pool is busy
            if (usageRatio >= shrinkUsageThreshold)
            {
                lastHighUsageTime = currentTime;
                return;
            }

            // Check if we should shrink
            if (idleTime < shrinkIdleThreshold) return;
            if (currentTime - lastShrinkTime < shrinkInterval) return;

            // Shrink by one
            if (availableSources.Count > 0 && currentPoolSize > initialPoolSize)
            {
                var sourceToRemove = availableSources.Dequeue();
                if (sourceToRemove != null)
                {
                    sourcePool.Remove(sourceToRemove);
                    Destroy(sourceToRemove.gameObject);
                    currentPoolSize--;
                    lastShrinkTime = currentTime;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
                    if (currentPoolSize % 8 == 0) // Log every 8 shrinks
                    {
                        Debug.Log($"AudioManager: Pool shrunk to {currentPoolSize}/{maxPoolSize} sources");
                    }
#endif
                }
            }
        }

        private static void ClearSourceText()
        {
#if UNITY_EDITOR
            foreach (var source in sourcePool)
            {
                if (source != null)
                {
                    var tempText = source.GetComponent<TextMesh>();
                    if (tempText != null) tempText.text = string.Empty;
                }
            }
#endif
        }

        #endregion

        #region Source Management

        private static int CountActiveInstances(AudioEvent audioEvent)
        {
            if (audioEvent == null || ActiveEvents == null) return 0;

            int count = 0;
            int eventCount = ActiveEvents.Count;
            for (int i = eventCount - 1; i >= 0; i--)
            {
                var ae = ActiveEvents[i];
                if (ae != null && ae.rootEvent == audioEvent && ae.status != EventStatus.Stopped)
                    count++;
            }
            return count;
        }

        private static void StopGroupInstances(int groupNum)
        {
            if (ActiveEvents == null) return;

            for (int i = ActiveEvents.Count - 1; i >= 0; i--)
            {
                if (i >= ActiveEvents.Count) continue;
                var tempEvent = ActiveEvents[i];
                if (tempEvent?.rootEvent != null && tempEvent.rootEvent.Group == groupNum)
                    tempEvent.StopImmediate();
            }
        }

        public static AudioSource GetUnusedSource()
        {
            int maxAttempts = availableSources.Count;
            int attempts = 0;

            while (availableSources.Count > 0 && attempts < maxAttempts)
            {
                AudioSource tempSource = availableSources.Dequeue();
                if (tempSource != null && tempSource.gameObject != null)
                    return tempSource;
                attempts++;
            }

            // Fallback: find any non-playing source
            int poolCount = sourcePool.Count;
            for (int i = 0; i < poolCount; i++)
            {
                var source = sourcePool[i];
                if (source != null && source.gameObject != null && !source.isPlaying)
                    return source;
            }

            // Smart pool: try to expand
            if (Instance != null && Instance.TryExpandPool())
            {
                // After expansion, try again
                if (availableSources.Count > 0)
                {
                    return availableSources.Dequeue();
                }
            }

            // Smart pool: try voice stealing as last resort
            var stolenSource = TryStealSource();
            if (stolenSource != null)
            {
                return stolenSource;
            }

            Debug.LogWarning($"AudioManager: Source pool exhausted ({currentPoolSize}/{maxPoolSize}). All sources in use and none can be stolen.");
            return null;
        }

        private static long CalculateAudioClipMemoryUsage(AudioClip clip)
        {
            if (clip == null) return 0;
            return clip.samples * clip.channels * 2L; // 16-bit samples
        }

        private static void TrackMemory(ActiveEvent activeEvent, bool isAdding)
        {
            reusableClipSet.Clear();

            int sourceCount = activeEvent.SourceCount;
            for (int i = 0; i < sourceCount; i++)
            {
                var es = activeEvent.GetSource(i);
                if (es.IsValid && es.source.clip != null)
                    reusableClipSet.Add(es.source.clip);
            }

            int direction = isAdding ? 1 : -1;
            foreach (var clip in reusableClipSet)
            {
                if (!activeClipRefCount.TryGetValue(clip, out int count)) count = 0;
                count += direction;

                if (count > 0)
                    activeClipRefCount[clip] = count;
                else
                    activeClipRefCount.Remove(clip);

                if (isAdding)
                {
                    if (!clipMemoryCache.ContainsKey(clip))
                    {
                        long memory = CalculateAudioClipMemoryUsage(clip);
                        clipMemoryCache[clip] = memory;
                        TotalMemoryUsage += memory;
                    }
                }
                else if (count == 0)
                {
                    if (clipMemoryCache.TryGetValue(clip, out long memory))
                    {
                        TotalMemoryUsage -= memory;
                        clipMemoryCache.Remove(clip);
                    }
                }
            }
        }

        public static bool ValidateManager()
        {
            if (Instance == null)
            {
                if (!AllowCreateInstance) return false;
                Instance = FindObjectOfType<AudioManager>();
                if (Instance == null) CreateInstance();
            }
            return Instance != null;
        }

        private static bool ValidateEvent(AudioEvent eventToPlay)
        {
            if (eventToPlay == null) return false;
            if (!eventToPlay.ValidateAudioFiles()) return false;
            if (eventToPlay.InstanceLimit > 0 && CountActiveInstances(eventToPlay) >= eventToPlay.InstanceLimit) return false;
            if (eventToPlay.Group != 0) StopGroupInstances(eventToPlay.Group);
            return true;
        }

        #endregion

        #region Bank Management

        /// <summary>
        /// Load AudioBank and register events for string-based lookup. Thread-safe.
        /// </summary>
        public static void LoadBank(AudioBank bank, bool overwriteExisting = false)
        {
            if (bank == null)
            {
                Debug.LogWarning("AudioManager: Attempted to load null AudioBank.");
                return;
            }

            if (Thread.CurrentThread.ManagedThreadId != mainThreadId)
            {
                AudioBank capturedBank = bank;
                commandQueue.Enqueue(() => LoadBank(capturedBank, overwriteExisting));
                return;
            }

            if (!ValidateManager()) return;

            var events = bank.AudioEvents;
            if (events == null || events.Count == 0)
            {
                Debug.LogWarning($"AudioManager: AudioBank '{bank.name}' contains no events.");
                return;
            }

            // Detect duplicates within bank
            reusableDuplicateCheck.Clear();
            int eventCount = events.Count;

            for (int i = 0; i < eventCount; i++)
            {
                var audioEvent = events[i];
                if (audioEvent == null || string.IsNullOrEmpty(audioEvent.name)) continue;

                string eventName = audioEvent.name;
                if (!reusableDuplicateCheck.ContainsKey(eventName))
                    reusableDuplicateCheck[eventName] = new List<AudioEvent>();
                reusableDuplicateCheck[eventName].Add(audioEvent);
            }

            foreach (var kvp in reusableDuplicateCheck)
            {
                if (kvp.Value.Count > 1)
                    Debug.LogError($"AudioManager: Bank '{bank.name}' has {kvp.Value.Count} events named '{kvp.Key}'.");
            }

            // Register events
            reusableBankRegisteredNames.Clear();
            int registeredCount = 0, skippedCount = 0;

            for (int i = 0; i < eventCount; i++)
            {
                var audioEvent = events[i];
                if (audioEvent == null || string.IsNullOrEmpty(audioEvent.name)) continue;

                string eventName = audioEvent.name;
                if (reusableBankRegisteredNames.Contains(eventName)) { skippedCount++; continue; }

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

            // Track bank with instance ID for O(1) unload
            loadedBanks[bank.GetInstanceID()] = bank;

            if (registeredCount > 0)
                Debug.Log($"AudioManager: Loaded {registeredCount} events from '{bank.name}'.");
            if (skippedCount > 0)
                Debug.LogWarning($"AudioManager: Skipped {skippedCount} duplicate events from '{bank.name}'.");
        }

        public static void UnloadBank(AudioBank bank)
        {
            if (bank == null) return;

            if (Thread.CurrentThread.ManagedThreadId != mainThreadId)
            {
                AudioBank capturedBank = bank;
                commandQueue.Enqueue(() => UnloadBank(capturedBank));
                return;
            }

            if (!ValidateManager()) return;

            var events = bank.AudioEvents;
            if (events == null || events.Count == 0) return;

            int removedCount = 0;
            int eventCount = events.Count;

            for (int i = 0; i < eventCount; i++)
            {
                var audioEvent = events[i];
                if (audioEvent == null || string.IsNullOrEmpty(audioEvent.name)) continue;

                string eventName = audioEvent.name;
                if (eventNameMap.TryGetValue(eventName, out AudioEvent mappedEvent) && mappedEvent == audioEvent)
                {
                    eventNameMap.TryRemove(eventName, out _);
                    removedCount++;
                }
            }

            loadedBanks.TryRemove(bank.GetInstanceID(), out _);

            if (removedCount > 0)
                Debug.Log($"AudioManager: Unloaded {removedCount} events from '{bank.name}'.");
        }

        public static void UnloadBankAndStopEvents(AudioBank bank)
        {
            if (bank == null) return;

            if (Thread.CurrentThread.ManagedThreadId != mainThreadId)
            {
                AudioBank capturedBank = bank;
                commandQueue.Enqueue(() => UnloadBankAndStopEvents(capturedBank));
                return;
            }

            if (!ValidateManager()) return;

            var events = bank.AudioEvents;
            if (events == null || events.Count == 0) return;

            // Stop all active events from this bank
            if (ActiveEvents != null && ActiveEvents.Count > 0)
            {
                int stoppedCount = 0;
                for (int i = ActiveEvents.Count - 1; i >= 0; i--)
                {
                    if (i >= ActiveEvents.Count) continue;
                    var tempEvent = ActiveEvents[i];
                    if (tempEvent?.rootEvent != null && events.Contains(tempEvent.rootEvent))
                    {
                        tempEvent.StopImmediate();
                        stoppedCount++;
                    }
                }
                if (stoppedCount > 0)
                    Debug.Log($"AudioManager: Stopped {stoppedCount} events from '{bank.name}'.");
            }

            UnloadBank(bank);
        }

        public static AudioEvent GetEventByName(string eventName)
        {
            if (string.IsNullOrEmpty(eventName)) return null;
            eventNameMap.TryGetValue(eventName, out AudioEvent result);
            return result;
        }

        public static void ClearEventNameMap()
        {
            if (Thread.CurrentThread.ManagedThreadId != mainThreadId)
            {
                commandQueue.Enqueue(ClearEventNameMap);
                return;
            }

            int count = eventNameMap.Count;
            eventNameMap.Clear();
            if (count > 0)
                Debug.Log($"AudioManager: Cleared {count} events from name map.");
        }

        public static int GetRegisteredEventCount() => eventNameMap.Count;

        public static Dictionary<string, List<AudioEvent>> ValidateBankForDuplicateNames(AudioBank bank)
        {
            var duplicates = new Dictionary<string, List<AudioEvent>>();
            if (bank?.AudioEvents == null) return duplicates;

            var nameGroups = new Dictionary<string, List<AudioEvent>>();
            int eventCount = bank.AudioEvents.Count;

            for (int i = 0; i < eventCount; i++)
            {
                var audioEvent = bank.AudioEvents[i];
                if (audioEvent == null || string.IsNullOrEmpty(audioEvent.name)) continue;

                string eventName = audioEvent.name;
                if (!nameGroups.ContainsKey(eventName))
                    nameGroups[eventName] = new List<AudioEvent>();
                nameGroups[eventName].Add(audioEvent);
            }

            foreach (var kvp in nameGroups)
            {
                if (kvp.Value.Count > 1)
                    duplicates[kvp.Key] = kvp.Value;
            }

            return duplicates;
        }

        public static IReadOnlyCollection<AudioBank> GetLoadedBanks()
        {
            return new List<AudioBank>(loadedBanks.Values).AsReadOnly();
        }

        public static int GetLoadedBankCount() => loadedBanks.Count;

        #endregion

        #region Editor

        public static void ApplyActiveSolos()
        {
            ValidateManager();

            bool soloActive = false;
            int count = ActiveEvents.Count;
            for (int i = 0; i < count; i++)
            {
                if (ActiveEvents[i].Soloed) { soloActive = true; break; }
            }

            if (soloActive)
            {
                for (int i = 0; i < count; i++) ActiveEvents[i].ApplySolo();
            }
            else
            {
                ClearActiveSolos();
            }
        }

        public static void ClearActiveSolos()
        {
            ValidateManager();
            int count = ActiveEvents.Count;
            for (int i = 0; i < count; i++) ActiveEvents[i].ClearSolo();
        }

        #endregion
    }
}