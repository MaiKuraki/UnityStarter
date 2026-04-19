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
    /// Controls which Unity lifecycle events automatically pause and/or resume audio.
    /// Flags can be combined freely. Separate pause and resume flags allow patterns like
    /// "auto-pause on focus loss, but let the game decide when to resume".
    /// </summary>
    [Flags]
    public enum AudioFocusMode
    {
        /// <summary>Fully manual — the game calls PauseAll / ResumeAll explicitly.</summary>
        None = 0,

        /// <summary>Auto-pause when OnApplicationPause(true) fires (mobile background, console Home).</summary>
        PauseOnAppPause = 1 << 0,
        /// <summary>Auto-resume when OnApplicationPause(false) fires (return from background).</summary>
        ResumeOnAppResume = 1 << 1,

        /// <summary>Auto-pause when OnApplicationFocus(false) fires (desktop Alt-Tab, mobile task-switcher).</summary>
        PauseOnFocusLost = 1 << 2,
        /// <summary>Auto-resume when OnApplicationFocus(true) fires (window regains focus).</summary>
        ResumeOnFocusGain = 1 << 3,

        // ---- Convenience presets ----

        /// <summary>Auto-pause on any loss event, but never auto-resume (game decides when to resume).</summary>
        AutoPauseOnly = PauseOnAppPause | PauseOnFocusLost,
        /// <summary>Full automatic pause and resume on both events (default, backward-compatible).</summary>
        All = PauseOnAppPause | ResumeOnAppResume | PauseOnFocusLost | ResumeOnFocusGain
    }

    public readonly struct AudioCategoryVoiceStats
    {
        public readonly AudioEventCategory Category;
        public readonly int Budget;
        public readonly int ActiveSources;
        public readonly float WeightedLoad;

        public AudioCategoryVoiceStats(AudioEventCategory category, int budget, int activeSources, float weightedLoad)
        {
            Category = category;
            Budget = budget;
            ActiveSources = activeSources;
            WeightedLoad = weightedLoad;
        }
    }

    internal readonly struct RepeatTriggerKey : IEquatable<RepeatTriggerKey>
    {
        public readonly int EventId;
        public readonly int ScopeId;

        public RepeatTriggerKey(int eventId, int scopeId)
        {
            EventId = eventId;
            ScopeId = scopeId;
        }

        public bool Equals(RepeatTriggerKey other)
        {
            return EventId == other.EventId && ScopeId == other.ScopeId;
        }

        public override bool Equals(object obj)
        {
            return obj is RepeatTriggerKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return (EventId * 397) ^ ScopeId;
            }
        }
    }

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

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticState()
        {
            AllowCreateInstance = true;
            isInitialized = false;
            Instance = null;
            cachedAudioListener = null;
            cachedMainCamera = null;
            cachedMainCameraFrame = -1;
            mainThreadId = Thread.CurrentThread.ManagedThreadId;
            OnBankUnloaded = null;
            activeEventHandleTable.Clear();
            freeActiveEventHandleSlots.Clear();
            ClearCommandQueue();
            AudioClipResolver.ClearExternalCache();
            AudioClipResolver.ClearReferenceLoaders();
            AudioClipResolver.ClearCustomProviders();
            AudioPoolConfig.ClearCache();
            AudioVoicePolicyProfile.ClearCache();
            AudioPlatformProfile.ClearCache();
            recentTriggerTimes.Clear();
            reusableExpiredTriggerKeys.Clear();
            lastRepeatTriggerCleanupTime = 0f;
            totalRepeatTriggerRejections = 0;
            totalAudibilityCulls = 0;
            activePoolConfig = null;
            activePlatformProfile = null;
            activePlatformSettings = AudioPlatformProfile.GetFallbackSettings();
        }

        public static List<ActiveEvent> ActiveEvents { get; private set; }

        private static readonly Stack<ActiveEvent> activeEventPool = new Stack<ActiveEvent>(64);
        private static readonly Queue<AudioSource> availableSources = new Queue<AudioSource>();
        private static readonly Stack<int> freeActiveEventHandleSlots = new Stack<int>(64);
        private static readonly List<ActiveEvent> activeEventHandleTable = new List<ActiveEvent>(64);
        public static IReadOnlyCollection<AudioSource> AvailableSources => availableSources;
        private enum AudioCommandType
        {
            None = 0,
            PlayEventOnObject = 1,
            PlayEventAtPosition = 2,
            PlayEventByNameOnObject = 3,
            PlayEventByNameAtPosition = 4,
            PlayScheduledByName = 5,
            PlayScheduledEvent = 6,
            PlayEventOnAudioSource = 7,
            StopAllByEvent = 8,
            StopAllByName = 9,
            StopAllByGroup = 10,
            LoadBank = 11,
            UnloadBank = 12,
            ClearEventNameMap = 13
        }

        private readonly struct AudioCommand
        {
            public readonly AudioCommandType Type;
            public readonly AudioEvent AudioEvent;
            public readonly string EventName;
            public readonly GameObject EmitterObject;
            public readonly AudioSource EmitterSource;
            public readonly Vector3 Position;
            public readonly double DspTime;
            public readonly int Group;
            public readonly AudioBank Bank;
            public readonly bool OverwriteExisting;

            public AudioCommand(
                AudioCommandType type,
                AudioEvent audioEvent = null,
                string eventName = null,
                GameObject emitterObject = null,
                AudioSource emitterSource = null,
                Vector3 position = default,
                double dspTime = 0d,
                int group = 0,
                AudioBank bank = null,
                bool overwriteExisting = false)
            {
                Type = type;
                AudioEvent = audioEvent;
                EventName = eventName;
                EmitterObject = emitterObject;
                EmitterSource = emitterSource;
                Position = position;
                DspTime = dspTime;
                Group = group;
                Bank = bank;
                OverwriteExisting = overwriteExisting;
            }
        }

        private const int CommandQueueCapacity = 1024;
        private static readonly object commandQueueLock = new object();
        private static readonly AudioCommand[] commandQueue = new AudioCommand[CommandQueueCapacity];
        private static int commandQueueHead;
        private static int commandQueueTail;
        private static int commandQueueCount;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private static int droppedCommandCount;
#endif
        private static readonly ActiveEvent[] previousEventsBuffer = new ActiveEvent[MaxPreviousEvents];
        private static int previousEventsWriteIndex;
        private static int previousEventsCount;

        public static int CurrentLanguage { get; private set; }
        public static string[] Languages;

        [Header("Focus & Pause")]
        [Tooltip("Controls which Unity lifecycle events automatically pause/resume audio.\nSet to None if your game manages audio pause/resume manually.")]
        [SerializeField] private AudioFocusMode focusMode = AudioFocusMode.All;

        [Header("Pool Configuration")]
        [Tooltip("Set to 0 to use auto-detected pool size based on device. Set > 0 to override with fixed size.")]
        [SerializeField] private int customPoolSize = 0;
        [Tooltip("Optional explicit AudioPoolConfig. If assigned, it takes precedence over SetConfig and FindConfig.")]
        [SerializeField] private AudioPoolConfig poolConfigOverride;

        [Header("Policy Profiles")]
        [Tooltip("Optional explicit AudioVoicePolicyProfile. If assigned, it takes precedence over SetConfig and FindConfig.")]
        [SerializeField] private AudioVoicePolicyProfile voicePolicyProfileOverride;
        [Tooltip("Optional explicit AudioPlatformProfile. If assigned, it takes precedence over SetConfig and FindConfig.")]
        [SerializeField] private AudioPlatformProfile platformProfileOverride;

        [Header("Mixing")]
        [SerializeField] private AudioMixer mainMixer;
        private static AudioMixer staticMainMixer;

        private static bool isInitialized;
        private static readonly List<AudioSource> sourcePool = new List<AudioSource>();
        public static IReadOnlyList<AudioSource> SourcePool => sourcePool;

        // Smart pool management
        private static AudioPoolConfig activePoolConfig;
        private static AudioPlatformProfile activePlatformProfile;
        private static AudioPlatformProfile.PlatformRuntimeSettings activePlatformSettings;
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
        private static int totalRepeatTriggerRejections;
        private static int totalAudibilityCulls;
        private static readonly Dictionary<AudioEventCategory, int> reusableCategorySourceCounts = new Dictionary<AudioEventCategory, int>(5);
        private static readonly Dictionary<AudioEventCategory, float> reusableCategoryBudgetLoads = new Dictionary<AudioEventCategory, float>(5);
        private static readonly Dictionary<RepeatTriggerKey, float> recentTriggerTimes = new Dictionary<RepeatTriggerKey, float>(256);
        private static readonly List<RepeatTriggerKey> reusableExpiredTriggerKeys = new List<RepeatTriggerKey>(64);
        private static float lastRepeatTriggerCleanupTime;


#if UNITY_EDITOR || DEVELOPMENT_BUILD
        // Memory tracking - use struct-based approach for better cache coherence
        private static readonly Dictionary<AudioClip, int> activeClipRefCount = new Dictionary<AudioClip, int>();
        private static readonly Dictionary<AudioClip, long> clipMemoryCache = new Dictionary<AudioClip, long>();
        private static readonly HashSet<AudioClip> reusableClipSet = new HashSet<AudioClip>();
#endif

        // O(1) lookup and removal for event names
        private static readonly ConcurrentDictionary<string, AudioEvent> eventNameMap = new ConcurrentDictionary<string, AudioEvent>();

        // O(1) bank tracking with instance ID as key for fast unload
        private static readonly ConcurrentDictionary<int, AudioBank> loadedBanks = new ConcurrentDictionary<int, AudioBank>();

        // Reusable collections to avoid allocations during bank loading
        private static readonly Dictionary<string, List<AudioEvent>> reusableDuplicateCheck = new Dictionary<string, List<AudioEvent>>();
        private static readonly HashSet<string> reusableBankRegisteredNames = new HashSet<string>();
        private static readonly HashSet<AudioParameter> reusableGlobalParameters = new HashSet<AudioParameter>();

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        public static long TotalMemoryUsage { get; private set; }
        public static IReadOnlyDictionary<AudioClip, int> ActiveClipRefCount => activeClipRefCount;
        public static IReadOnlyDictionary<AudioClip, long> ClipMemoryCache => clipMemoryCache;
#endif

        private static float globalVolume = 1f;
        private static bool globalPaused;
        private static bool systemPaused;
        private static AudioFocusMode activeFocusMode = AudioFocusMode.All;
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
            public static int TotalRepeatTriggerRejections => totalRepeatTriggerRejections;
            public static int TotalAudibilityCulls => totalAudibilityCulls;
            public static string DeviceTier => activePoolConfig?.GetDeviceTierName() ?? AudioPoolConfig.GetDefaultDeviceTierName();
            public static bool HasConfig => activePoolConfig != null;
            public static string PlatformProfile => activePlatformProfile != null ? activePlatformProfile.name : "Fallback";
            public static string PlatformTarget => activePlatformProfile != null ? activePlatformProfile.GetCurrentPlatformLabel() : "Desktop Fallback";
        }

        public static void GetCategoryVoiceStats(List<AudioCategoryVoiceStats> results)
        {
            if (results == null) return;

            FillActiveSourceCountsByCategory(reusableCategorySourceCounts, reusableCategoryBudgetLoads);
            results.Clear();

            AddCategoryVoiceStats(results, AudioEventCategory.CriticalUI);
            AddCategoryVoiceStats(results, AudioEventCategory.GameplaySFX);
            AddCategoryVoiceStats(results, AudioEventCategory.Voice);
            AddCategoryVoiceStats(results, AudioEventCategory.Ambient);
            AddCategoryVoiceStats(results, AudioEventCategory.Music);
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

        event Action<AudioBank> IAudioService.OnBankUnloaded
        {
            add => OnBankUnloaded += value;
            remove => OnBankUnloaded -= value;
        }
        ActiveEvent IAudioService.PlayEvent(AudioEvent eventToPlay, GameObject emitterObject) => PlayEvent(eventToPlay, emitterObject);
        ActiveEvent IAudioService.PlayEvent(AudioEvent eventToPlay, Vector3 position) => PlayEvent(eventToPlay, position);
        ActiveEvent IAudioService.PlayEvent(string eventName, GameObject emitterObject) => PlayEvent(eventName, emitterObject);
        ActiveEvent IAudioService.PlayEvent(string eventName, Vector3 position) => PlayEvent(eventName, position);
        ActiveEvent IAudioService.PlayEventScheduled(AudioEvent eventToPlay, GameObject emitterObject, double dspTime) => PlayEventScheduled(eventToPlay, emitterObject, dspTime);
        ActiveEvent IAudioService.PlayEventScheduled(string eventName, GameObject emitterObject, double dspTime) => PlayEventScheduled(eventName, emitterObject, dspTime);
        void IAudioService.StopAll(AudioEvent eventsToStop) => StopAll(eventsToStop);
        void IAudioService.StopAll(string eventName) => StopAll(eventName);
        void IAudioService.StopAll(int groupNum) => StopAll(groupNum);
        void IAudioService.PauseAll() => PauseAll();
        void IAudioService.ResumeAll() => ResumeAll();
        void IAudioService.PauseEvent(ActiveEvent e) => e?.Pause();
        void IAudioService.ResumeEvent(ActiveEvent e) => e?.Resume();
        bool IAudioService.IsEventPlaying(string eventName) => IsEventPlaying(eventName);
        void IAudioService.SetGlobalVolume(float volume) => SetGlobalVolume(volume);
        float IAudioService.GetGlobalVolume() => GetGlobalVolume();
        void IAudioService.LoadBank(AudioBank bank, bool overwriteExisting) => LoadBank(bank, overwriteExisting);
        void IAudioService.UnloadBank(AudioBank bank) => UnloadBank(bank);

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

        public static void SetGlobalVolume(float volume)
        {
            globalVolume = Mathf.Clamp01(volume);
            AudioListener.volume = globalVolume;
        }

        public static float GetGlobalVolume() => globalVolume;

        /// <summary>
        /// Get or set which Unity lifecycle events automatically pause/resume audio at runtime.
        /// Set to <see cref="AudioFocusMode.None"/> if your game manages audio pause/resume manually.
        /// </summary>
        public static AudioFocusMode FocusMode
        {
            get => activeFocusMode;
            set => activeFocusMode = value;
        }

        public static void PauseAll()
        {
            if (!ValidateManager()) return;
            globalPaused = true;
            int count = ActiveEvents.Count;
            for (int i = 0; i < count; i++)
                ActiveEvents[i]?.Pause();
        }

        public static void ResumeAll()
        {
            if (!ValidateManager()) return;
            globalPaused = false;
            int count = ActiveEvents.Count;
            for (int i = 0; i < count; i++)
                ActiveEvents[i]?.Resume();
        }

        public static bool IsEventPlaying(string eventName)
        {
            if (string.IsNullOrEmpty(eventName) || ActiveEvents == null) return false;
            AudioEvent evt = GetEventByName(eventName);
            if (evt == null) return false;
            int count = ActiveEvents.Count;
            for (int i = 0; i < count; i++)
            {
                var ae = ActiveEvents[i];
                if (ae != null && ae.rootEvent == evt && ae.status == EventStatus.Played)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// DI-friendly: register an externally-created AudioManager as the singleton.
        /// Call before any PlayEvent if using dependency injection.
        /// </summary>
        public static void SetInstance(AudioManager manager)
        {
            if (manager == null) return;
            Instance = manager;
            if (!isInitialized) manager.Initialize();
        }

        /// <summary>
        /// Start playing an AudioEvent. Thread-safe.
        /// </summary>
        public static ActiveEvent PlayEvent(AudioEvent eventToPlay, GameObject emitterObject)
        {
            if (Thread.CurrentThread.ManagedThreadId != mainThreadId)
            {
                TryEnqueueCommand(new AudioCommand(
                    AudioCommandType.PlayEventOnObject,
                    audioEvent: eventToPlay,
                    emitterObject: emitterObject));
                return null;
            }

            Transform emitterTransform = emitterObject != null ? emitterObject.transform : null;
            if (!ValidateManager() || !ValidateEvent(eventToPlay, emitterTransform, null, false, out RepeatTriggerKey triggerKey)) return null;

            ActiveEvent tempEvent = GetActiveEventFromPool(eventToPlay, emitterTransform);
            tempEvent.Play();
            if (tempEvent.status != EventStatus.Error)
                CommitRepeatTrigger(triggerKey);

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (tempEvent.status != EventStatus.Error)
                TrackMemory(tempEvent, true);
#endif

            return tempEvent;
        }

        public static ActiveEvent PlayEvent(AudioEvent eventToPlay, Vector3 position)
        {
            if (Thread.CurrentThread.ManagedThreadId != mainThreadId)
            {
                TryEnqueueCommand(new AudioCommand(
                    AudioCommandType.PlayEventAtPosition,
                    audioEvent: eventToPlay,
                    position: position));
                return null;
            }

            if (!ValidateManager() || !ValidateEvent(eventToPlay, null, position, false, out RepeatTriggerKey triggerKey)) return null;

            ActiveEvent tempEvent = GetActiveEventFromPool(eventToPlay, null);
            tempEvent.Play();
            tempEvent.SetAllSourcePositions(position);
            if (tempEvent.status != EventStatus.Error)
                CommitRepeatTrigger(triggerKey);

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (tempEvent.status != EventStatus.Error)
                TrackMemory(tempEvent, true);
#endif

            return tempEvent;
        }

        public static ActiveEvent PlayEvent(string eventName, GameObject emitterObject)
        {
            if (Thread.CurrentThread.ManagedThreadId != mainThreadId)
            {
                TryEnqueueCommand(new AudioCommand(
                    AudioCommandType.PlayEventByNameOnObject,
                    eventName: eventName,
                    emitterObject: emitterObject));
                return null;
            }

            if (!ValidateManager() || string.IsNullOrEmpty(eventName)) return null;

            AudioEvent eventToPlay = GetEventByName(eventName);
            if (eventToPlay == null)
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                Debug.LogWarning($"AudioManager: Event '{eventName}' not found.");
#endif
                return null;
            }

            return PlayEvent(eventToPlay, emitterObject);
        }

        public static ActiveEvent PlayEvent(string eventName, Vector3 position)
        {
            if (Thread.CurrentThread.ManagedThreadId != mainThreadId)
            {
                TryEnqueueCommand(new AudioCommand(
                    AudioCommandType.PlayEventByNameAtPosition,
                    eventName: eventName,
                    position: position));
                return null;
            }

            if (!ValidateManager() || string.IsNullOrEmpty(eventName)) return null;

            AudioEvent eventToPlay = GetEventByName(eventName);
            if (eventToPlay == null)
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                Debug.LogWarning($"AudioManager: Event '{eventName}' not found.");
#endif
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
                double capturedDspTime = AudioSettings.dspTime;
                double adjustedDspTime = dspTime > capturedDspTime ? dspTime : capturedDspTime + 0.01;
                TryEnqueueCommand(new AudioCommand(
                    AudioCommandType.PlayScheduledByName,
                    eventName: eventName,
                    emitterObject: emitterObject,
                    dspTime: adjustedDspTime));
                return null;
            }

            if (!ValidateManager() || string.IsNullOrEmpty(eventName)) return null;

            AudioEvent eventToPlay = GetEventByName(eventName);
            if (eventToPlay == null)
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                Debug.LogWarning($"AudioManager: Event '{eventName}' not found.");
#endif
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
                TryEnqueueCommand(new AudioCommand(
                    AudioCommandType.PlayScheduledEvent,
                    audioEvent: eventToPlay,
                    emitterObject: emitterObject,
                    dspTime: adjustedDspTime));
                return null;
            }

            Transform emitterTransform = emitterObject != null ? emitterObject.transform : null;
            if (!ValidateManager() || !ValidateEvent(eventToPlay, emitterTransform, null, true, out RepeatTriggerKey triggerKey)) return null;

            double currentDspTime = AudioSettings.dspTime;
            if (dspTime < currentDspTime) dspTime = currentDspTime + 0.01;

            ActiveEvent tempEvent = GetActiveEventFromPool(eventToPlay, emitterTransform);
            tempEvent.scheduledDspTime = dspTime;
            tempEvent.Play();
            if (tempEvent.status != EventStatus.Error)
                CommitRepeatTrigger(triggerKey);

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (tempEvent.status != EventStatus.Error)
                TrackMemory(tempEvent, true);
#endif

            return tempEvent;
        }

        private static ActiveEvent GetActiveEventFromPool(AudioEvent eventToPlay, Transform emitter)
        {
            ActiveEvent activeEvent = activeEventPool.Count > 0 ? activeEventPool.Pop() : new ActiveEvent();
            activeEvent.Initialize(eventToPlay, emitter);
            RegisterActiveEventHandle(activeEvent);
            return activeEvent;
        }

        [Obsolete("Playing on AudioSource is deprecated")]
        public static ActiveEvent PlayEvent(AudioEvent eventToPlay, AudioSource emitter)
        {
            if (Thread.CurrentThread.ManagedThreadId != mainThreadId)
            {
                TryEnqueueCommand(new AudioCommand(
                    AudioCommandType.PlayEventOnAudioSource,
                    audioEvent: eventToPlay,
                    emitterSource: emitter));
                return null;
            }

            Debug.LogWarning("AudioManager: Deprecated - play on AudioSource no longer supported");
            Transform emitterTransform = emitter != null ? emitter.transform : null;
            if (!ValidateManager() || !ValidateEvent(eventToPlay, emitterTransform, null, false, out RepeatTriggerKey triggerKey)) return null;

            ActiveEvent tempEvent = GetActiveEventFromPool(eventToPlay, emitter.transform);
            tempEvent.Play();
            if (tempEvent.status != EventStatus.Error)
                CommitRepeatTrigger(triggerKey);

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (tempEvent.status != EventStatus.Error)
                TrackMemory(tempEvent, true);
#endif

            return tempEvent;
        }

        public static void StopAll(AudioEvent eventsToStop)
        {
            if (Thread.CurrentThread.ManagedThreadId != mainThreadId)
            {
                TryEnqueueCommand(new AudioCommand(
                    AudioCommandType.StopAllByEvent,
                    audioEvent: eventsToStop));
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
                TryEnqueueCommand(new AudioCommand(
                    AudioCommandType.StopAllByName,
                    eventName: eventName));
                return;
            }

            if (!ValidateManager() || string.IsNullOrEmpty(eventName)) return;

            AudioEvent eventToStop = GetEventByName(eventName);
            if (eventToStop == null)
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                Debug.LogWarning($"AudioManager: Event '{eventName}' not found.");
#endif
                return;
            }

            StopAll(eventToStop);
        }

        public static void StopAll(int groupNum)
        {
            if (Thread.CurrentThread.ManagedThreadId != mainThreadId)
            {
                TryEnqueueCommand(new AudioCommand(
                    AudioCommandType.StopAllByGroup,
                    group: groupNum));
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

            // Idempotent: skip if already removed (prevents double pool entry from delayed removal)
            int idx = ActiveEvents.IndexOf(stoppedEvent);
            if (idx < 0) return;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            // Track memory BEFORE clearing clips, so TrackMemory can read source.clip
            TrackMemory(stoppedEvent, false);
#endif

            // Clear AudioSource.clip before returning to pool.
            // This releases the clip reference so external asset management systems
            // (AssetManagement, Addressables, etc.) can safely unload the underlying asset.
            int sourceCount = stoppedEvent.SourceCount;
            for (int i = 0; i < sourceCount; i++)
            {
                var es = stoppedEvent.GetSource(i);
                if (es.IsValid)
                {
                    ResetAudioSource(es.source);
                    es.source.clip = null;
                    availableSources.Enqueue(es.source);
                }
            }

            // O(1) swap-remove
            int last = ActiveEvents.Count - 1;
            if (idx < last)
                ActiveEvents[idx] = ActiveEvents[last];
            ActiveEvents.RemoveAt(last);

            UnregisterActiveEventHandle(stoppedEvent);
            stoppedEvent.Reset();
            activeEventPool.Push(stoppedEvent);
        }

        public static void AddPreviousEvent(ActiveEvent newEvent)
        {
            previousEventsBuffer[previousEventsWriteIndex] = newEvent;
            previousEventsWriteIndex = (previousEventsWriteIndex + 1) % MaxPreviousEvents;
            if (previousEventsCount < MaxPreviousEvents) previousEventsCount++;
        }

        internal static bool TryGetActiveEventByHandle(int slot, int generation, out ActiveEvent activeEvent)
        {
            if ((uint)slot < (uint)activeEventHandleTable.Count)
            {
                activeEvent = activeEventHandleTable[slot];
                if (activeEvent != null &&
                    activeEvent.generation == generation &&
                    activeEvent.status != EventStatus.Stopped &&
                    activeEvent.handleSlot == slot)
                {
                    return true;
                }
            }

            activeEvent = null;
            return false;
        }

        private static void RegisterActiveEventHandle(ActiveEvent activeEvent)
        {
            if (activeEvent == null) return;

            int slot = freeActiveEventHandleSlots.Count > 0
                ? freeActiveEventHandleSlots.Pop()
                : activeEventHandleTable.Count;

            if (slot == activeEventHandleTable.Count)
            {
                activeEventHandleTable.Add(activeEvent);
            }
            else
            {
                activeEventHandleTable[slot] = activeEvent;
            }

            activeEvent.handleSlot = slot;
        }

        private static void UnregisterActiveEventHandle(ActiveEvent activeEvent)
        {
            if (activeEvent == null) return;

            int slot = activeEvent.handleSlot;
            if ((uint)slot >= (uint)activeEventHandleTable.Count) return;
            if (!ReferenceEquals(activeEventHandleTable[slot], activeEvent)) return;

            activeEventHandleTable[slot] = null;
            freeActiveEventHandleSlots.Push(slot);
            activeEvent.handleSlot = -1;
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
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            public long TotalMemoryUsage;
#endif
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
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                TotalMemoryUsage = TotalMemoryUsage,
#endif
                LoadedBanksCount = loadedBanks.Count
            };
        }

        public static void ResetPoolStatistics()
        {
            Interlocked.Exchange(ref totalEventsPlayed, 0);
            Interlocked.Exchange(ref peakActiveEvents, 0);
            totalRepeatTriggerRejections = 0;
            totalAudibilityCulls = 0;
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
            activeFocusMode = focusMode;

            if (!isInitialized) Initialize();
        }

        private void OnDestroy()
        {
            if (Instance == this) Cleanup();
        }

        private void OnApplicationQuit() => Cleanup();

        private void OnApplicationPause(bool pauseStatus)
        {
            if (pauseStatus)
            {
                if ((activeFocusMode & AudioFocusMode.PauseOnAppPause) != 0)
                    SystemPause();
            }
            else
            {
                if ((activeFocusMode & AudioFocusMode.ResumeOnAppResume) != 0)
                    SystemResume();
            }
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            if (!hasFocus)
            {
                if ((activeFocusMode & AudioFocusMode.PauseOnFocusLost) != 0)
                    SystemPause();
            }
            else
            {
                if ((activeFocusMode & AudioFocusMode.ResumeOnFocusGain) != 0)
                    SystemResume();
            }
        }

        /// <summary>
        /// System-initiated pause (focus/app lifecycle). Tracked separately from manual PauseAll()
        /// so that auto-resume can work even after globalPaused was set.
        /// </summary>
        private static void SystemPause()
        {
            if (!ValidateManager()) return;
            systemPaused = true;
            int count = ActiveEvents.Count;
            for (int i = 0; i < count; i++)
                ActiveEvents[i]?.Pause();
        }

        /// <summary>
        /// System-initiated resume. Only resumes if the pause was system-initiated,
        /// not if the game manually called PauseAll().
        /// </summary>
        private static void SystemResume()
        {
            if (!systemPaused) return;
            systemPaused = false;
            if (!globalPaused)
                ResumeAll();
        }

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
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            activeClipRefCount.Clear();
            clipMemoryCache.Clear();
            TotalMemoryUsage = 0;
#endif
            activeEventPool.Clear();
            availableSources.Clear();
            activeEventHandleTable.Clear();
            freeActiveEventHandleSlots.Clear();
            globalVolume = 1f;
            globalPaused = false;
            systemPaused = false;
            activeFocusMode = AudioFocusMode.All;
            activePlatformProfile = null;
            activePlatformSettings = AudioPlatformProfile.GetFallbackSettings();
            recentTriggerTimes.Clear();
            reusableExpiredTriggerKeys.Clear();
            lastRepeatTriggerCleanupTime = 0f;
            totalRepeatTriggerRejections = 0;
            totalAudibilityCulls = 0;
            ClearCommandQueue();
            AudioClipResolver.ClearExternalCache();
            AudioClipResolver.ClearReferenceLoaders();
            AudioClipResolver.ClearCustomProviders();
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
            while (commandProcessed < maxCommandsPerFrame && TryDequeueCommand(out AudioCommand command))
            {
                ExecuteCommand(command);
                commandProcessed++;
            }

              UpdateGlobalParameters(Time.deltaTime);
            CleanupRepeatTriggerCache(Time.unscaledTime);

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

        private static void ExecuteCommand(in AudioCommand command)
        {
            switch (command.Type)
            {
                case AudioCommandType.PlayEventOnObject:
                    PlayEvent(command.AudioEvent, command.EmitterObject);
                    break;
                case AudioCommandType.PlayEventAtPosition:
                    PlayEvent(command.AudioEvent, command.Position);
                    break;
                case AudioCommandType.PlayEventByNameOnObject:
                    PlayEvent(command.EventName, command.EmitterObject);
                    break;
                case AudioCommandType.PlayEventByNameAtPosition:
                    PlayEvent(command.EventName, command.Position);
                    break;
                case AudioCommandType.PlayScheduledByName:
                    PlayEventScheduled(command.EventName, command.EmitterObject, command.DspTime);
                    break;
                case AudioCommandType.PlayScheduledEvent:
                    PlayEventScheduled(command.AudioEvent, command.EmitterObject, command.DspTime);
                    break;
                case AudioCommandType.PlayEventOnAudioSource:
                    ExecuteDeprecatedAudioSourcePlay(command.AudioEvent, command.EmitterSource);
                    break;
                case AudioCommandType.StopAllByEvent:
                    StopAll(command.AudioEvent);
                    break;
                case AudioCommandType.StopAllByName:
                    StopAll(command.EventName);
                    break;
                case AudioCommandType.StopAllByGroup:
                    StopAll(command.Group);
                    break;
                case AudioCommandType.LoadBank:
                    LoadBank(command.Bank, command.OverwriteExisting);
                    break;
                case AudioCommandType.UnloadBank:
                    UnloadBank(command.Bank);
                    break;
                case AudioCommandType.ClearEventNameMap:
                    ClearEventNameMap();
                    break;
            }
        }

        private static bool TryEnqueueCommand(in AudioCommand command)
        {
            lock (commandQueueLock)
            {
                if (commandQueueCount >= CommandQueueCapacity)
                {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                    droppedCommandCount++;
                    if (droppedCommandCount <= 5)
                    {
                        Debug.LogWarning($"AudioManager: Command queue overflow. Dropped command '{command.Type}'.");
                    }
#endif
                    return false;
                }

                commandQueue[commandQueueTail] = command;
                commandQueueTail = (commandQueueTail + 1) % CommandQueueCapacity;
                commandQueueCount++;
                return true;
            }
        }

        private static bool TryDequeueCommand(out AudioCommand command)
        {
            lock (commandQueueLock)
            {
                if (commandQueueCount == 0)
                {
                    command = default;
                    return false;
                }

                command = commandQueue[commandQueueHead];
                commandQueue[commandQueueHead] = default;
                commandQueueHead = (commandQueueHead + 1) % CommandQueueCapacity;
                commandQueueCount--;
                return true;
            }
        }

        private static void ClearCommandQueue()
        {
            lock (commandQueueLock)
            {
                Array.Clear(commandQueue, 0, commandQueue.Length);
                commandQueueHead = 0;
                commandQueueTail = 0;
                commandQueueCount = 0;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                droppedCommandCount = 0;
#endif
            }
        }

        private static void ExecuteDeprecatedAudioSourcePlay(AudioEvent eventToPlay, AudioSource emitterSource)
        {
#pragma warning disable CS0618
            PlayEvent(eventToPlay, emitterSource);
#pragma warning restore CS0618
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

            ApplyResolvedConfigs();
            InitializeSmartPool();
            RefreshLoadedBankRuntimeState();

            ValidateAudioListener();
            Application.quitting += HandleQuitting;
            isInitialized = true;

            string configSource = activePoolConfig != null ? "Auto-discovered" : "Defaults";
            string platformProfileSource = activePlatformProfile != null
                ? $"{activePlatformProfile.name} ({activePlatformProfile.GetCurrentPlatformLabel()})"
                : "Fallback Desktop Settings";
            Debug.Log($"AudioManager: Initialized with {currentPoolSize} sources (max: {maxPoolSize}, tier: {PoolStats.DeviceTier}, config: {configSource}, platform profile: {platformProfileSource})");
        }

        private void ApplyResolvedConfigs()
        {
            activePoolConfig = ResolvePoolConfig();
            AudioVoicePolicyProfile resolvedVoicePolicyProfile = ResolveVoicePolicyProfile();
            activePlatformProfile = ResolvePlatformProfile();

            if (activePoolConfig != null)
                AudioPoolConfig.SetConfig(activePoolConfig);

            if (resolvedVoicePolicyProfile != null)
                AudioVoicePolicyProfile.SetConfig(resolvedVoicePolicyProfile);

            if (activePlatformProfile != null)
                AudioPlatformProfile.SetConfig(activePlatformProfile);

            activePlatformSettings = activePlatformProfile != null
                ? activePlatformProfile.GetSettingsForCurrentPlatform()
                : AudioPlatformProfile.GetFallbackSettings();
            activeFocusMode = activePlatformSettings.overrideFocusMode
                ? activePlatformSettings.focusMode
                : focusMode;
        }

        private AudioPoolConfig ResolvePoolConfig()
        {
            if (poolConfigOverride != null)
                return poolConfigOverride;

            return AudioPoolConfig.FindConfig();
        }

        private AudioVoicePolicyProfile ResolveVoicePolicyProfile()
        {
            if (voicePolicyProfileOverride != null)
                return voicePolicyProfileOverride;

            return AudioVoicePolicyProfile.FindConfig();
        }

        private AudioPlatformProfile ResolvePlatformProfile()
        {
            if (platformProfileOverride != null)
                return platformProfileOverride;

            return AudioPlatformProfile.FindConfig();
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
            totalRepeatTriggerRejections = 0;
            totalAudibilityCulls = 0;
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
            if (Instance == null)
            {
                activePoolConfig = AudioPoolConfig.FindConfig();
                return;
            }

            activePoolConfig = Instance.ResolvePoolConfig();
            if (activePoolConfig != null)
                AudioPoolConfig.SetConfig(activePoolConfig);

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

                    Instance.TrimExcessIdleSources(forceTrimToMax: true);

                    Debug.Log($"AudioManager: Pool config reloaded (initial: {initialPoolSize}, max: {maxPoolSize}, tier: {PoolStats.DeviceTier})");
                }
            }
        }

        public static void ReloadVoicePolicyProfile()
        {
            if (Instance == null)
                return;

            AudioVoicePolicyProfile resolvedProfile = Instance.ResolveVoicePolicyProfile();
            AudioVoicePolicyProfile.SetConfig(resolvedProfile);
        }

        public static void ReloadPlatformProfile()
        {
            if (Instance == null)
            {
                activePlatformProfile = AudioPlatformProfile.FindConfig();
                activePlatformSettings = activePlatformProfile != null
                    ? activePlatformProfile.GetSettingsForCurrentPlatform()
                    : AudioPlatformProfile.GetFallbackSettings();
                return;
            }

            activePlatformProfile = Instance.ResolvePlatformProfile();
            if (activePlatformProfile != null)
                AudioPlatformProfile.SetConfig(activePlatformProfile);

            activePlatformSettings = activePlatformProfile != null
                ? activePlatformProfile.GetSettingsForCurrentPlatform()
                : AudioPlatformProfile.GetFallbackSettings();
            activeFocusMode = activePlatformSettings.overrideFocusMode
                ? activePlatformSettings.focusMode
                : Instance.focusMode;
        }

        public static void ReloadRuntimeProfiles()
        {
            ReloadPoolConfig();
            ReloadVoicePolicyProfile();
            ReloadPlatformProfile();
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
                ResetAudioSource(tempSource);

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

        private void TrimExcessIdleSources(bool forceTrimToMax)
        {
            if (availableSources.Count == 0 || currentPoolSize <= 0) return;

            int targetPoolSize = forceTrimToMax
                ? Mathf.Clamp(maxPoolSize, 0, currentPoolSize)
                : initialPoolSize;

            if (currentPoolSize <= targetPoolSize) return;

            int trimCount = Mathf.Min(currentPoolSize - targetPoolSize, availableSources.Count);
            if (trimCount <= 0) return;

            for (int i = 0; i < trimCount; i++)
            {
                AudioSource sourceToRemove = availableSources.Dequeue();
                if (sourceToRemove == null)
                    continue;

                sourcePool.Remove(sourceToRemove);
                Destroy(sourceToRemove.gameObject);
                currentPoolSize--;
            }

            if (peakPoolUsage > currentPoolSize)
                peakPoolUsage = currentPoolSize;
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

        private static int GetVoiceBudgetForCategory(AudioEventCategory category)
        {
            int poolCap = Mathf.Max(maxPoolSize, 1);
            float multiplier = activePlatformSettings.GetVoiceBudgetMultiplier(category);
            switch (category)
            {
                case AudioEventCategory.CriticalUI:
                    return Mathf.Max(2, Mathf.CeilToInt(poolCap * 0.10f * multiplier));
                case AudioEventCategory.Voice:
                    return Mathf.Max(2, Mathf.CeilToInt(poolCap * 0.20f * multiplier));
                case AudioEventCategory.Ambient:
                    return Mathf.Max(2, Mathf.CeilToInt(poolCap * 0.18f * multiplier));
                case AudioEventCategory.Music:
                    return Mathf.Max(1, Mathf.CeilToInt(poolCap * 0.08f * multiplier));
                default:
                    return Mathf.Max(4, Mathf.CeilToInt(poolCap * 0.44f * multiplier));
            }
        }

        private static void AddCategoryVoiceStats(List<AudioCategoryVoiceStats> results, AudioEventCategory category)
        {
            int activeSources = reusableCategorySourceCounts.TryGetValue(category, out int sourceCount) ? sourceCount : 0;
            float weightedLoad = reusableCategoryBudgetLoads.TryGetValue(category, out float load) ? load : 0f;
            results.Add(new AudioCategoryVoiceStats(category, GetVoiceBudgetForCategory(category), activeSources, weightedLoad));
        }

        private static void FillActiveSourceCountsByCategory(
            Dictionary<AudioEventCategory, int> counts,
            Dictionary<AudioEventCategory, float> weightedLoads)
        {
            if (counts == null || weightedLoads == null) return;

            counts.Clear();
            weightedLoads.Clear();
            counts[AudioEventCategory.CriticalUI] = 0;
            counts[AudioEventCategory.GameplaySFX] = 0;
            counts[AudioEventCategory.Voice] = 0;
            counts[AudioEventCategory.Ambient] = 0;
            counts[AudioEventCategory.Music] = 0;
            weightedLoads[AudioEventCategory.CriticalUI] = 0f;
            weightedLoads[AudioEventCategory.GameplaySFX] = 0f;
            weightedLoads[AudioEventCategory.Voice] = 0f;
            weightedLoads[AudioEventCategory.Ambient] = 0f;
            weightedLoads[AudioEventCategory.Music] = 0f;

            if (ActiveEvents == null) return;

            for (int i = 0; i < ActiveEvents.Count; i++)
            {
                ActiveEvent evt = ActiveEvents[i];
                if (evt == null || evt.status == EventStatus.Stopped || evt.rootEvent == null)
                    continue;

                AudioEventCategory category = evt.rootEvent.Category;
                counts[category] += evt.SourceCount;
                weightedLoads[category] += evt.SourceCount * evt.rootEvent.VoiceBudgetWeight;
            }
        }

        private static float GetCategoryProtectionWeight(AudioEventCategory category)
        {
            switch (category)
            {
                case AudioEventCategory.CriticalUI: return 120f;
                case AudioEventCategory.Music: return 105f;
                case AudioEventCategory.Voice: return 85f;
                case AudioEventCategory.GameplaySFX: return 65f;
                case AudioEventCategory.Ambient: return 45f;
                default: return 60f;
            }
        }

        private static Vector3 GetReferenceListenerPosition()
        {
            if (cachedAudioListener != null && cachedAudioListener.gameObject != null)
                return cachedAudioListener.transform.position;

            Camera mainCamera = GetMainCamera();
            if (mainCamera != null)
                return mainCamera.transform.position;

            return Vector3.zero;
        }

        private static float GetVoiceProtectionScore(
            ActiveEvent evt,
            Dictionary<AudioEventCategory, int> categoryCounts,
            Dictionary<AudioEventCategory, float> weightedLoads)
        {
            if (evt == null || evt.rootEvent == null)
                return float.MaxValue;

            AudioEvent rootEvent = evt.rootEvent;
            if (!rootEvent.AllowVoiceSteal)
                return float.MaxValue;

            float score = GetCategoryProtectionWeight(rootEvent.Category);
            score += rootEvent.Priority * 1.5f;
            score *= rootEvent.StealResistance;

            if (rootEvent.Output != null && rootEvent.Output.loop)
                score += 20f;

            if (evt.IsPaused)
                score += 15f;

            if (rootEvent.ProtectScheduledPlayback && evt.scheduledDspTime > 0)
                score += 40f;

            float age = Mathf.Max(0f, Time.time - evt.timeStarted);
            score -= Mathf.Min(age, 20f) * 1.5f;

            EventSource primarySource = evt.GetSource(0);
            if (rootEvent.AllowDistanceBasedSteal && primarySource.IsValid)
            {
                AudioSource source = primarySource.source;
                float maxDistance = Mathf.Max(source.maxDistance, 0.0001f);
                float distance = Vector3.Distance(source.transform.position, GetReferenceListenerPosition());
                float normalizedDistance = Mathf.Clamp01(distance / maxDistance);
                score += (1f - normalizedDistance) * 35f;
                score += Mathf.Clamp01(source.volume) * 15f;
            }

            if (categoryCounts != null && categoryCounts.TryGetValue(rootEvent.Category, out int activeSourceCount))
            {
                int budget = GetVoiceBudgetForCategory(rootEvent.Category);
                if (activeSourceCount > budget)
                    score -= 25f;
            }

            if (weightedLoads != null && weightedLoads.TryGetValue(rootEvent.Category, out float weightedLoad))
            {
                float weightedBudget = GetVoiceBudgetForCategory(rootEvent.Category) * 1.15f;
                if (weightedLoad > weightedBudget)
                    score -= Mathf.Min((weightedLoad - weightedBudget) * 6f, 35f);
            }

            return score;
        }

        private static float GetRequesterProtectionScore(
            AudioEvent requestingEvent,
            Dictionary<AudioEventCategory, int> categoryCounts,
            Dictionary<AudioEventCategory, float> weightedLoads)
        {
            if (requestingEvent == null)
                return float.MinValue;

            float score = GetCategoryProtectionWeight(requestingEvent.Category);
            score += requestingEvent.Priority * 1.6f;
            score *= requestingEvent.StealResistance;

            if (categoryCounts != null && categoryCounts.TryGetValue(requestingEvent.Category, out int activeSourceCount))
            {
                int budget = GetVoiceBudgetForCategory(requestingEvent.Category);
                if (activeSourceCount < budget)
                    score += 20f;
                else
                    score -= Mathf.Min(activeSourceCount - budget, 4) * 8f;
            }

            if (weightedLoads != null && weightedLoads.TryGetValue(requestingEvent.Category, out float weightedLoad))
            {
                float weightedBudget = GetVoiceBudgetForCategory(requestingEvent.Category) * 1.15f;
                if (weightedLoad < weightedBudget)
                    score += 10f;
                else
                    score -= Mathf.Min((weightedLoad - weightedBudget) * 6f, 30f);
            }

            return score;
        }

        /// <summary>
        /// Attempt to steal a source using category-aware voice budgeting and protection scoring.
        /// Returns the stolen source or null if no suitable source found.
        /// </summary>
        private static AudioSource TryStealSource(AudioEvent requestingEvent)
        {
            if (ActiveEvents == null || ActiveEvents.Count == 0) return null;

            FillActiveSourceCountsByCategory(reusableCategorySourceCounts, reusableCategoryBudgetLoads);

            ActiveEvent victim = null;
            float lowestProtectionScore = float.MaxValue;
            float requesterScore = GetRequesterProtectionScore(requestingEvent, reusableCategorySourceCounts, reusableCategoryBudgetLoads);

            for (int i = 0; i < ActiveEvents.Count; i++)
            {
                var evt = ActiveEvents[i];
                if (evt == null || evt.status == EventStatus.Stopped) continue;
                if (evt.rootEvent == null) continue;

                float protectionScore = GetVoiceProtectionScore(evt, reusableCategorySourceCounts, reusableCategoryBudgetLoads);
                if (protectionScore < lowestProtectionScore)
                {
                    lowestProtectionScore = protectionScore;
                    victim = evt;
                }
            }

            if (victim == null)
                return null;

            if (requestingEvent != null)
            {
                bool victimOverBudget = reusableCategorySourceCounts.TryGetValue(victim.rootEvent.Category, out int victimCategoryCount) &&
                    victimCategoryCount > GetVoiceBudgetForCategory(victim.rootEvent.Category);

                float requiredMargin = victimOverBudget ? -5f : 10f;
                if (requesterScore < lowestProtectionScore + requiredMargin)
                    return null;
            }

            if (victim != null && victim.SourceCount > 0)
            {
                var source = victim.GetSource(0);
                if (source.IsValid)
                {
                    victim.StopImmediate();
                    totalSteals++;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
                    Debug.LogWarning($"AudioManager: Voice stolen from '{victim.name}' (category:{victim.rootEvent.Category}, protection:{lowestProtectionScore:F1}, total steals: {totalSteals})");
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

            int beforeSize = currentPoolSize;
            TrimExcessIdleSources(forceTrimToMax: false);
            if (currentPoolSize < beforeSize)
            {
                lastShrinkTime = currentTime;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
                if (currentPoolSize % 8 == 0 || currentPoolSize == initialPoolSize)
                {
                    Debug.Log($"AudioManager: Pool shrunk to {currentPoolSize}/{maxPoolSize} sources");
                }
#endif
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

        public static AudioSource GetUnusedSource(AudioEvent requestingEvent = null)
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

            // Smart pool: try to expand
            if (Instance != null && Instance.TryExpandPool())
            {
                if (availableSources.Count > 0)
                {
                    return availableSources.Dequeue();
                }
            }

            // Smart pool: try voice stealing as last resort
            var stolenSource = TryStealSource(requestingEvent);
            if (stolenSource != null)
            {
                return stolenSource;
            }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            string requesterName = requestingEvent != null ? requestingEvent.name : "Unknown";
            Debug.LogWarning($"AudioManager: Source pool exhausted ({currentPoolSize}/{maxPoolSize}) for '{requesterName}'. All sources are in use and none met the current stealing policy.");
#endif
            return null;
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private static long CalculateAudioClipMemoryUsage(AudioClip clip)
        {
            if (clip == null) return 0;

            // Use Unity Profiler API for accurate runtime memory measurement.
            // This accounts for the actual load type and compression format:
            //   - Decompress On Load: full PCM in memory (samples × channels × bytesPerSample)
            //   - Compressed In Memory: compressed data only (much smaller than PCM)
            //   - Streaming: only a small I/O buffer (a few KB)
            // Fallback to PCM estimate (samples × channels × 2 bytes) if Profiler returns 0.
            long profilerSize = UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(clip);
            if (profilerSize > 0) return profilerSize;

            // Fallback: assume 16-bit PCM (worst-case estimate for Decompress On Load)
            return clip.samples * clip.channels * 2L;
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
#endif

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

        private static bool ValidateEvent(
            AudioEvent eventToPlay,
            Transform emitterTransform,
            Vector3? explicitPosition,
            bool isScheduledPlayback,
            out RepeatTriggerKey triggerKey)
        {
            triggerKey = default;
            if (eventToPlay == null) return false;
            if (!eventToPlay.ValidateAudioFiles()) return false;
            if (eventToPlay.InstanceLimit > 0 && CountActiveInstances(eventToPlay) >= eventToPlay.InstanceLimit) return false;
            if (!PassesAudibilityCulling(eventToPlay, emitterTransform, explicitPosition, isScheduledPlayback)) return false;
            if (!PassesRepeatTriggerThrottle(eventToPlay, emitterTransform, explicitPosition, isScheduledPlayback, out triggerKey)) return false;
            if (eventToPlay.Group != 0) StopGroupInstances(eventToPlay.Group);
            return true;
        }

        private static bool PassesRepeatTriggerThrottle(
            AudioEvent eventToPlay,
            Transform emitterTransform,
            Vector3? explicitPosition,
            bool isScheduledPlayback,
            out RepeatTriggerKey triggerKey)
        {
            triggerKey = default;
            float repeatWindow = activePlatformSettings.GetRepeatTriggerWindow(eventToPlay.Category);
            if (repeatWindow <= 0f) return true;
            if (isScheduledPlayback && !activePlatformSettings.throttleScheduledPlayback) return true;

            int scopeId = ResolveTriggerScopeId(emitterTransform, explicitPosition);
            triggerKey = new RepeatTriggerKey(eventToPlay.GetInstanceID(), scopeId);

            float currentTime = Time.unscaledTime;
            if (recentTriggerTimes.TryGetValue(triggerKey, out float lastTime) &&
                currentTime - lastTime < repeatWindow)
            {
                totalRepeatTriggerRejections++;
                return false;
            }

            return true;
        }

        private static void CommitRepeatTrigger(RepeatTriggerKey triggerKey)
        {
            if (triggerKey.EventId == 0) return;
            recentTriggerTimes[triggerKey] = Time.unscaledTime;
        }

        private static int ResolveTriggerScopeId(Transform emitterTransform, Vector3? explicitPosition)
        {
            if (!activePlatformSettings.throttlePerEmitter)
                return 0;

            if (emitterTransform != null)
                return emitterTransform.GetInstanceID();

            if (explicitPosition.HasValue)
                return QuantizePositionHash(explicitPosition.Value);

            return 0;
        }

        private static int QuantizePositionHash(Vector3 position)
        {
            int x = Mathf.RoundToInt(position.x * 2f);
            int y = Mathf.RoundToInt(position.y * 2f);
            int z = Mathf.RoundToInt(position.z * 2f);
            unchecked
            {
                int hash = x;
                hash = (hash * 397) ^ y;
                hash = (hash * 397) ^ z;
                return hash;
            }
        }

        private static void CleanupRepeatTriggerCache(float currentTime)
        {
            if (recentTriggerTimes.Count == 0) return;
            if (currentTime - lastRepeatTriggerCleanupTime < 2f && recentTriggerTimes.Count < 256) return;

            lastRepeatTriggerCleanupTime = currentTime;
            float maxWindow = Mathf.Max(activePlatformSettings.GetMaxRepeatTriggerWindow(), 0.05f);
            float expiryThreshold = currentTime - Mathf.Max(maxWindow * 4f, 1f);

            reusableExpiredTriggerKeys.Clear();
            foreach (var entry in recentTriggerTimes)
            {
                if (entry.Value <= expiryThreshold)
                    reusableExpiredTriggerKeys.Add(entry.Key);
            }

            for (int i = 0; i < reusableExpiredTriggerKeys.Count; i++)
                recentTriggerTimes.Remove(reusableExpiredTriggerKeys[i]);
        }

        private static bool PassesAudibilityCulling(
            AudioEvent eventToPlay,
            Transform emitterTransform,
            Vector3? explicitPosition,
            bool isScheduledPlayback)
        {
            if (!activePlatformSettings.enableAudibilityCulling) return true;
            if (isScheduledPlayback && !activePlatformSettings.cullScheduledPlayback) return true;

            AudioOutput output = eventToPlay.Output;
            if (output == null) return true;
            if (output.loop && !activePlatformSettings.cullLoopingEvents) return true;

            if (output.spatialBlend <= 0.01f)
            {
                if (!activePlatformSettings.cull2DEvents) return true;
                if (output.MaxVolume < activePlatformSettings.minEstimatedAudibility)
                {
                    totalAudibilityCulls++;
                    return false;
                }

                return true;
            }

            if (!TryGetPlaybackWorldPosition(emitterTransform, explicitPosition, out Vector3 playbackPosition))
                return true;

            Vector3 listenerPosition = GetReferenceListenerPosition();
            float distance = Vector3.Distance(listenerPosition, playbackPosition);
            float maxDistance = Mathf.Max(output.MaxDistance, 0.01f);
            float paddedDistance = maxDistance + activePlatformSettings.distanceCullPadding;
            if (distance > paddedDistance)
            {
                totalAudibilityCulls++;
                return false;
            }

            float normalizedDistance = Mathf.Clamp01(distance / maxDistance);
            float attenuation = EstimateAudibilityFromOutput(output, normalizedDistance);
            float estimatedVolume = output.MaxVolume * attenuation;
            if (estimatedVolume < activePlatformSettings.minEstimatedAudibility)
            {
                totalAudibilityCulls++;
                return false;
            }

            return true;
        }

        private static bool TryGetPlaybackWorldPosition(Transform emitterTransform, Vector3? explicitPosition, out Vector3 position)
        {
            if (emitterTransform != null)
            {
                position = emitterTransform.position;
                return true;
            }

            if (explicitPosition.HasValue)
            {
                position = explicitPosition.Value;
                return true;
            }

            position = default;
            return false;
        }

        private static float EstimateAudibilityFromOutput(AudioOutput output, float normalizedDistance)
        {
            if (output == null) return 1f;

            AnimationCurve curve = output.attenuationCurve;
            if (curve != null && curve.length > 0)
                return Mathf.Clamp01(curve.Evaluate(normalizedDistance));

            return 1f - normalizedDistance;
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
                TryEnqueueCommand(new AudioCommand(
                    AudioCommandType.LoadBank,
                    bank: bank,
                    overwriteExisting: overwriteExisting));
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
            InitializeBankRuntimeState(bank);

            if (registeredCount > 0)
                Debug.Log($"AudioManager: Loaded {registeredCount} events from '{bank.name}'.");
            if (skippedCount > 0)
                Debug.LogWarning($"AudioManager: Skipped {skippedCount} duplicate events from '{bank.name}'.");
        }

        /// <summary>
        /// Callback invoked after a bank is fully unloaded and all clip references are cleared.
        /// External asset management systems can subscribe to release underlying asset handles.
        /// </summary>
        public static event Action<AudioBank> OnBankUnloaded;

        /// <summary>
        /// Unload an AudioBank: stops all active events from this bank, immediately clears every
        /// AudioSource.clip reference, removes name mappings, and fires <see cref="OnBankUnloaded"/>.
        /// After this method returns, the audio system holds zero references to the bank's clips,
        /// making it safe for external asset managers to release the underlying assets.
        /// </summary>
        public static void UnloadBank(AudioBank bank)
        {
            if (bank == null) return;

            if (Thread.CurrentThread.ManagedThreadId != mainThreadId)
            {
                TryEnqueueCommand(new AudioCommand(
                    AudioCommandType.UnloadBank,
                    bank: bank));
                return;
            }

            if (!ValidateManager()) return;

            var events = bank.AudioEvents;
            if (events == null || events.Count == 0) return;

            // Stop and immediately clean up all active events from this bank.
            // Immediate removal (instead of DelayRemoveActiveEvent) guarantees that all
            // AudioSource.clip references are cleared before this method returns.
            if (ActiveEvents != null && ActiveEvents.Count > 0)
            {
                for (int i = ActiveEvents.Count - 1; i >= 0; i--)
                {
                    if (i >= ActiveEvents.Count) continue;
                    var tempEvent = ActiveEvents[i];
                    if (tempEvent?.rootEvent != null && events.Contains(tempEvent.rootEvent))
                    {
                        tempEvent.StopImmediate();
                        // RemoveActiveEvent is idempotent; safe even if DelayRemoveActiveEvent fires later
                        RemoveActiveEvent(tempEvent);
                    }
                }
            }

            // Remove name mappings
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
            ResetBankRuntimeState(bank);

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (removedCount > 0)
                Debug.Log($"AudioManager: Unloaded {removedCount} events from '{bank.name}'.");
#endif

            // Notify external systems (AssetManagement, Addressables, etc.) that the bank
            // is fully released and its asset handles can be safely disposed.
            OnBankUnloaded?.Invoke(bank);
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
                TryEnqueueCommand(new AudioCommand(AudioCommandType.ClearEventNameMap));
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

        private static void InitializeBankRuntimeState(AudioBank bank)
        {
            if (bank == null) return;

            var parameters = bank.Parameters;
            if (parameters != null)
            {
                for (int i = 0; i < parameters.Count; i++)
                {
                    parameters[i]?.ResetParameter();
                }
            }

            var switches = bank.Switches;
            if (switches != null)
            {
                for (int i = 0; i < switches.Count; i++)
                {
                    switches[i]?.ResetSwitch();
                }
            }
        }

        private static void ResetBankRuntimeState(AudioBank bank)
        {
            InitializeBankRuntimeState(bank);
        }

        private static void RefreshLoadedBankRuntimeState()
        {
            foreach (var bank in loadedBanks.Values)
            {
                InitializeBankRuntimeState(bank);
            }
        }

        private static void UpdateGlobalParameters(float deltaTime)
        {
            reusableGlobalParameters.Clear();

            foreach (var bank in loadedBanks.Values)
            {
                var parameters = bank?.Parameters;
                if (parameters == null) continue;

                for (int i = 0; i < parameters.Count; i++)
                {
                    var parameter = parameters[i];
                    if (parameter != null)
                    {
                        reusableGlobalParameters.Add(parameter);
                    }
                }
            }

            foreach (var parameter in reusableGlobalParameters)
            {
                parameter.UpdateInterpolation(deltaTime);
            }
        }

        private static void ResetAudioSource(AudioSource source)
        {
            if (source == null) return;

            source.Stop();
            source.clip = null;
            source.outputAudioMixerGroup = null;
            source.playOnAwake = false;
            source.loop = false;
            source.mute = false;
            source.volume = 1f;
            source.pitch = 1f;
            source.panStereo = 0f;
            source.spatialBlend = 0f;
            source.spatialize = false;
            source.dopplerLevel = 1f;
            source.reverbZoneMix = 1f;
            source.bypassEffects = false;
            source.bypassListenerEffects = false;
            source.bypassReverbZones = false;
            source.priority = 128;
            source.rolloffMode = AudioRolloffMode.Logarithmic;
            source.minDistance = 1f;
            source.maxDistance = 500f;
            source.spread = 0f;
            source.SetScheduledEndTime(double.MaxValue);
            source.transform.localPosition = Vector3.zero;
            source.transform.localRotation = Quaternion.identity;
        }

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
