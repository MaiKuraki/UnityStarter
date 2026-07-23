// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using UnityEngine;
using UnityEngine.Audio;
using System.Buffers;
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
        /// <summary>Fully manual; the game calls PauseAll / ResumeAll explicitly.</summary>
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

    public readonly struct AudioRuntimeStats
    {
        public readonly int ActiveEvents;
        public readonly int SourcePoolSize;
        public readonly int PoolInitialSize;
        public readonly int PoolCurrentSize;
        public readonly int PoolMaxSize;
        public readonly int PoolInUse;
        public readonly int PoolAvailable;
        public readonly float PoolUsageRatio;
        public readonly int PoolPeakUsage;
        public readonly int PoolExpansions;
        public readonly int VoiceSteals;
        public readonly int RepeatTriggerRejections;
        public readonly int AudibilityCulls;
        public readonly int LoadedBanks;
        public readonly int RegisteredEvents;
        public readonly int RegisteredParameters;
        public readonly int RegisteredStateGroups;
        public readonly int RegisteredStateMixProfiles;
        public readonly int DuckingRuleCount;
        public readonly int ActiveDuckingRuleCount;
        public readonly int ScopedParameterOverrides;
        public readonly int PendingRemovals;
        [Obsolete("Worker command dispatch was removed; this value is always zero.")]
        public readonly int QueuedCommands;
        public readonly int ExternalCacheEntries;
        public readonly int ExternalLoadingCount;
        public readonly int ExternalLoadedCount;
        public readonly int ExternalFailedCount;
        public readonly int ExternalTotalRefCount;
        public readonly int ExternalTotalLoadRequests;
        public readonly int ExternalCacheHits;
        public readonly int ExternalCacheMisses;
        public readonly int ExternalTotalFailures;
        public readonly long TotalEventsPlayed;
        public readonly int PeakActiveEvents;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        public readonly long TotalMemoryUsage;
#endif

        public AudioRuntimeStats(
            int activeEvents,
            int sourcePoolSize,
            int poolInitialSize,
            int poolCurrentSize,
            int poolMaxSize,
            int poolInUse,
            int poolAvailable,
            float poolUsageRatio,
            int poolPeakUsage,
            int poolExpansions,
            int voiceSteals,
            int repeatTriggerRejections,
            int audibilityCulls,
            int loadedBanks,
            int registeredEvents,
            int registeredParameters,
            int registeredStateGroups,
            int registeredStateMixProfiles,
            int duckingRuleCount,
            int activeDuckingRuleCount,
            int scopedParameterOverrides,
            int pendingRemovals,
            int queuedCommands,
            ExternalAudioClipCacheStats externalCache,
            long totalEventsPlayed,
            int peakActiveEvents
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            , long totalMemoryUsage
#endif
        )
        {
            ActiveEvents = activeEvents;
            SourcePoolSize = sourcePoolSize;
            PoolInitialSize = poolInitialSize;
            PoolCurrentSize = poolCurrentSize;
            PoolMaxSize = poolMaxSize;
            PoolInUse = poolInUse;
            PoolAvailable = poolAvailable;
            PoolUsageRatio = poolUsageRatio;
            PoolPeakUsage = poolPeakUsage;
            PoolExpansions = poolExpansions;
            VoiceSteals = voiceSteals;
            RepeatTriggerRejections = repeatTriggerRejections;
            AudibilityCulls = audibilityCulls;
            LoadedBanks = loadedBanks;
            RegisteredEvents = registeredEvents;
            RegisteredParameters = registeredParameters;
            RegisteredStateGroups = registeredStateGroups;
            RegisteredStateMixProfiles = registeredStateMixProfiles;
            DuckingRuleCount = duckingRuleCount;
            ActiveDuckingRuleCount = activeDuckingRuleCount;
            ScopedParameterOverrides = scopedParameterOverrides;
            PendingRemovals = pendingRemovals;
#pragma warning disable CS0618
            QueuedCommands = 0;
#pragma warning restore CS0618
            ExternalCacheEntries = externalCache.EntryCount;
            ExternalLoadingCount = externalCache.LoadingCount;
            ExternalLoadedCount = externalCache.LoadedCount;
            ExternalFailedCount = externalCache.FailedCount;
            ExternalTotalRefCount = externalCache.TotalRefCount;
            ExternalTotalLoadRequests = externalCache.TotalLoadRequests;
            ExternalCacheHits = externalCache.CacheHitCount;
            ExternalCacheMisses = externalCache.CacheMissCount;
            ExternalTotalFailures = externalCache.TotalFailureCount;
            TotalEventsPlayed = totalEventsPlayed;
            PeakActiveEvents = peakActiveEvents;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            TotalMemoryUsage = totalMemoryUsage;
#endif
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

    internal sealed class PooledAudioClipReferenceSet : IDisposable
    {
        private const int InitialCapacity = 32;

        private AudioClipReference[] references;
        private int[] ids;
        private int[] buckets;
        private int[] next;
        private int count;

        public int Count => count;
        public AudioClipReference this[int index] => references[index];

        public PooledAudioClipReferenceSet()
        {
            references = ArrayPool<AudioClipReference>.Shared.Rent(InitialCapacity);
            ids = ArrayPool<int>.Shared.Rent(InitialCapacity);
            buckets = ArrayPool<int>.Shared.Rent(InitialCapacity);
            next = ArrayPool<int>.Shared.Rent(InitialCapacity);
            Array.Fill(buckets, -1);
        }

        public bool Add(AudioClipReference reference)
        {
            if (reference == null || string.IsNullOrWhiteSpace(reference.Location)) return false;

            int id = reference.GetInstanceID();
            int bucketIndex = id & (buckets.Length - 1);
            for (int entry = buckets[bucketIndex]; entry >= 0; entry = next[entry])
            {
                if (ids[entry] == id) return false;
            }

            EnsureCapacity(count + 1);

            bucketIndex = id & (buckets.Length - 1);
            ids[count] = id;
            references[count] = reference;
            next[count] = buckets[bucketIndex];
            buckets[bucketIndex] = count;
            count++;
            return true;
        }

        public void Dispose()
        {
            if (references != null)
            {
                ArrayPool<AudioClipReference>.Shared.Return(references, true);
                references = null;
            }

            if (ids != null)
            {
                ArrayPool<int>.Shared.Return(ids);
                ids = null;
            }

            if (buckets != null)
            {
                ArrayPool<int>.Shared.Return(buckets);
                buckets = null;
            }

            if (next != null)
            {
                ArrayPool<int>.Shared.Return(next);
                next = null;
            }

            count = 0;
        }

        private void EnsureCapacity(int requiredCount)
        {
            bool needsEntryResize = requiredCount > references.Length;
            bool needsBucketResize = requiredCount >= buckets.Length * 3 / 4;
            if (!needsEntryResize && !needsBucketResize) return;

            if (needsEntryResize)
            {
                int newEntryCapacity = references.Length * 2;
                while (newEntryCapacity < requiredCount)
                    newEntryCapacity *= 2;

                AudioClipReference[] newReferences = ArrayPool<AudioClipReference>.Shared.Rent(newEntryCapacity);
                int[] newIds = ArrayPool<int>.Shared.Rent(newEntryCapacity);
                int[] newNext = ArrayPool<int>.Shared.Rent(newEntryCapacity);

                Array.Copy(references, newReferences, count);
                Array.Copy(ids, newIds, count);
                Array.Copy(next, newNext, count);

                ArrayPool<AudioClipReference>.Shared.Return(references, true);
                ArrayPool<int>.Shared.Return(ids);
                ArrayPool<int>.Shared.Return(next);

                references = newReferences;
                ids = newIds;
                next = newNext;
            }

            if (needsBucketResize)
            {
                int newBucketCapacity = buckets.Length * 2;
                while (requiredCount >= newBucketCapacity * 3 / 4)
                    newBucketCapacity *= 2;

                int[] newBuckets = ArrayPool<int>.Shared.Rent(newBucketCapacity);
                Array.Fill(newBuckets, -1);

                for (int i = 0; i < count; i++)
                {
                    int bucketIndex = ids[i] & (newBuckets.Length - 1);
                    next[i] = newBuckets[bucketIndex];
                    newBuckets[bucketIndex] = i;
                }

                ArrayPool<int>.Shared.Return(buckets);
                buckets = newBuckets;
            }
        }
    }

    internal readonly struct PreparedAudioEventData
    {
        public readonly int EventId;
        public readonly bool Ready;
        public readonly AudioOutput Output;
        public readonly int InstanceLimit;
        public readonly int Group;
        public readonly AudioEventCategory Category;
        public readonly AudioEventVoicePolicy VoicePolicy;

        public PreparedAudioEventData(AudioEvent audioEvent, bool ready)
        {
            EventId = audioEvent != null ? audioEvent.GetInstanceID() : 0;
            Ready = ready;
            Output = audioEvent != null ? audioEvent.Output : null;
            InstanceLimit = audioEvent != null ? audioEvent.InstanceLimit : 0;
            Group = audioEvent != null ? audioEvent.Group : 0;
            Category = audioEvent != null ? audioEvent.Category : AudioEventCategory.GameplaySFX;
            VoicePolicy = audioEvent != null ? audioEvent.GetResolvedVoicePolicy() : default;
        }
    }

    internal readonly struct BankNameContribution<T> where T : UnityEngine.Object
    {
        public readonly int OwnerBankId;
        public readonly string Name;
        public readonly T Value;
        public readonly bool CanOverride;

        public BankNameContribution(int ownerBankId, string name, T value, bool canOverride)
        {
            OwnerBankId = ownerBankId;
            Name = name;
            Value = value;
            CanOverride = canOverride;
        }
    }

    internal sealed class AudioBankRegistration
    {
        public readonly AudioBank Bank;
        public readonly int BankId;
        public readonly bool OverwriteExisting;
        public readonly List<BankNameContribution<AudioEvent>> EventContributions = new List<BankNameContribution<AudioEvent>>();
        public readonly List<BankNameContribution<AudioParameter>> ParameterContributions = new List<BankNameContribution<AudioParameter>>();
        public readonly List<BankNameContribution<AudioStateGroup>> StateGroupContributions = new List<BankNameContribution<AudioStateGroup>>();
        public readonly List<AudioEvent> OwnedEvents = new List<AudioEvent>();
        public readonly List<AudioParameter> OwnedParameters = new List<AudioParameter>();
        public readonly List<AudioSwitch> OwnedSwitches = new List<AudioSwitch>();
        public readonly List<AudioStateGroup> OwnedStateGroups = new List<AudioStateGroup>();
        public readonly List<AudioStateMixProfile> OwnedProfiles = new List<AudioStateMixProfile>();

        public AudioBankRegistration(AudioBank bank, bool overwriteExisting)
        {
            Bank = bank;
            BankId = bank.GetInstanceID();
            OverwriteExisting = overwriteExisting;
        }
    }

    /// <summary>
    /// Unity main-thread audio event playback manager.
    /// </summary>
    public class AudioManager : MonoBehaviour, IAudioService, IAudioLifecyclePauseControl, IAudioBankClipLeaseProvider
    {
        public static AudioManager Instance { get; private set; }

        private static bool AllowCreateInstance = true;
        private static AudioListener cachedAudioListener;
        private static Camera cachedMainCamera;
        private static int cachedMainCameraFrame = -1;
        private static bool isTearingDown;
        private static int managerLifecycleGeneration;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticState()
        {
            AudioRuntimeThreadGuard.CaptureCurrentThread();
            AdvanceManagerLifecycleGeneration();
            isTearingDown = true;
            try
            {
                AllowCreateInstance = true;
                isInitialized = false;
                Application.quitting -= HandleQuitting;
                Instance = null;
                cachedAudioListener = null;
                cachedMainCamera = null;
                cachedMainCameraFrame = -1;
                var banksToNotify = new List<AudioBank>(loadedBanks.Count);
                foreach (var pair in loadedBanks)
                {
                    if (pair.Value != null)
                        banksToNotify.Add(pair.Value);
                }
                if (ActiveEvents != null)
                {
                    for (int i = 0; i < ActiveEvents.Count; i++)
                    {
                        ActiveEvent activeEvent = ActiveEvents[i];
                        if (activeEvent == null) continue;
                        for (int sourceIndex = 0; sourceIndex < activeEvent.SourceCount; sourceIndex++)
                        {
                            EventSource eventSource = activeEvent.GetSource(sourceIndex);
                            if (eventSource.IsValid) ResetAudioSource(eventSource.source);
                        }
                        activeEvent.ResetForPool();
                    }
                    ActiveEvents.Clear();
                }
                for (int i = 0; i < pendingEventRecycles.Count; i++)
                {
                    ActiveEvent pendingEvent = pendingEventRecycles[i].Event;
                    if (pendingEvent != null) pendingEvent.ResetForPool();
                }
                pendingEventRecycles.Clear();
                pendingEventRecycleSet.Clear();
                for (int i = 0; i < sourcePool.Count; i++)
                    ResetAudioSource(sourcePool[i]);
                ReleaseAllPreloadedBankClipLeases();
                activeEventPool.Clear();
                availableSources.Clear();
                sourcePool.Clear();
                activeEventHandleTable.Clear();
                freeActiveEventHandleSlots.Clear();
                activeInstanceCounts.Clear();
                playingEventNameCounts.Clear();
                activeEventIndices.Clear();
                preparedAudioEvents.Clear();
                eventNameMap.Clear();
                parameterNameMap.Clear();
                stateGroupNameMap.Clear();
                registeredStateMixProfiles.Clear();
                bankRegistrations.Clear();
                eventsBeingUnloaded.Clear();
                bankOperationsInProgress.Clear();
                groupsBeingReplaced.Clear();
                preloadBankMutationVersions.Clear();
                eventBindings.Clear();
                parameterBindings.Clear();
                stateGroupBindings.Clear();
                eventOwnerCounts.Clear();
                parameterOwnerCounts.Clear();
                switchOwnerCounts.Clear();
                stateGroupOwnerCounts.Clear();
                profileOwnerCounts.Clear();
                loadedBanks.Clear();
                loadedBanksCacheDirty = true;
                cachedLoadedBanksList.Clear();
                cachedLoadedBanksReadOnly = null;
                pendingRemovals.Clear();
                categoryCountsCacheFrame = -1;
                cachedStealVictimFrame = -1;
                cachedStealVictim = null;
                cachedStealVictimScore = float.MaxValue;
                AudioClipResolver.ClearExternalCache();
                AudioClipResolver.ClearReferenceLoaders();
                AudioClipResolver.ClearCustomProviders();
                AudioPoolConfig.ClearCache();
                AudioVoicePolicyProfile.ClearCache();
                AudioPlatformProfile.ClearCache();
                AudioDuckingProfile.ClearCache();
                recentTriggerTimes.Clear();
                reusableExpiredTriggerKeys.Clear();
                reusableCategorySourceCounts.Clear();
                reusableCategoryBudgetLoads.Clear();
                lastRepeatTriggerCleanupTime = 0f;
                totalRepeatTriggerRejections = 0;
                totalAudibilityCulls = 0;
                activePoolConfig = null;
                activePlatformProfile = null;
                activeDuckingProfile = null;
                activePlatformSettings = AudioPlatformProfile.GetFallbackSettings();
                reusableGlobalParameters.Clear();
                scopedParameterOverrides.Clear();
                scopedParameterOwners.Clear();
                reusableScopedParameterRemovalKeys.Clear();
                reusableDeadParameterScopeIds.Clear();
                lastScopedParameterOwnerCleanupFrame = -1;
                activeDuckingProfile = null;
                activeDuckingRuleCount = 0;
                staticMainMixer = null;
                currentPoolSize = 0;
                initialPoolSize = 0;
                maxPoolSize = 0;
                lastPoolExpansionTime = 0f;
                lastHighUsageTime = 0f;
                lastShrinkTime = 0f;
                peakPoolUsage = 0;
                totalExpansions = 0;
                totalSteals = 0;
                globalVolume = 1f;
                globalPaused = false;
                systemPauseReasons = SystemPauseReason.None;
                activeFocusMode = AudioFocusMode.All;
                debugMode = false;
                Array.Clear(previousEventsBuffer, 0, previousEventsBuffer.Length);
                previousEventsWriteIndex = 0;
                previousEventsCount = 0;
                cachedPreviousEventsVersion = -1;
                CurrentLanguage = 0;
                Languages = null;
                ExternalClipMemoryBudgetBytes = 0;
                ExternalClipMaxDownloadBytes = DefaultExternalClipMaxDownloadBytes;
                ExternalClipMaxDecodedBytes = DefaultExternalClipMaxDecodedBytes;
                ExternalClipRequestTimeoutSeconds = DefaultExternalClipRequestTimeoutSeconds;
                ExternalClipIdleTTL = DefaultExternalClipIdleTtlSeconds;
                BankClipLeaseMaxDecodedBytes = DefaultBankClipLeaseMaxDecodedBytes;
                ActiveBankClipLeaseMemoryBudgetBytes = DefaultActiveBankClipLeaseMemoryBudgetBytes;
                activeBankClipLeaseMemoryBytes = 0L;
                lastEvictionCheckTime = 0f;
                ClearActiveEventCategoryCounts();
                globalParametersCacheDirty = true;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
                activeClipRefCount.Clear();
                clipMemoryCache.Clear();
                Array.Clear(reusableTrackMemoryClips, 0, reusableTrackMemoryClips.Length);
                TotalMemoryUsage = 0;
                totalEventsPlayed = 0;
                peakActiveEvents = 0;
#endif

                for (int i = 0; i < banksToNotify.Count; i++)
                    NotifyBankUnloaded(banksToNotify[i]);
                OnBankUnloaded = null;
            }
            finally
            {
                isTearingDown = false;
            }
        }

        private static void AdvanceManagerLifecycleGeneration()
        {
            unchecked
            {
                managerLifecycleGeneration++;
                if (managerLifecycleGeneration == 0)
                    managerLifecycleGeneration++;
            }
        }

        public static List<ActiveEvent> ActiveEvents { get; private set; }

        private static readonly Stack<ActiveEvent> activeEventPool = new Stack<ActiveEvent>(64);
        private const int MaxRetainedActiveEvents = 64;
        private static readonly Queue<AudioSource> availableSources = new Queue<AudioSource>();
        private static readonly Stack<int> freeActiveEventHandleSlots = new Stack<int>(64);
        private static readonly List<ActiveEvent> activeEventHandleTable = new List<ActiveEvent>(64);
        private static int nextActiveEventHandleGeneration;
        public static IReadOnlyCollection<AudioSource> AvailableSources => availableSources;

        // O(1) instance counting: keyed by AudioEvent.GetInstanceID()
        private static readonly Dictionary<int, int> activeInstanceCounts = new Dictionary<int, int>(128);

        // O(1) event-name playing check
        private static readonly Dictionary<string, int> playingEventNameCounts = new Dictionary<string, int>(128);

        // O(1) ActiveEvent -> index mapping for swap-remove
        private static readonly Dictionary<ActiveEvent, int> activeEventIndices = new Dictionary<ActiveEvent, int>(128);

        // Pending removals (replaces async void DelayRemoveActiveEvent)
        private struct PendingRemoval
        {
            public ActiveEvent Event;
            public int Generation;
            public float RemoveTime;
        }
        private static readonly List<PendingRemoval> pendingRemovals = new List<PendingRemoval>(32);
        private struct PendingEventRecycle
        {
            public ActiveEvent Event;
            public int Generation;
        }
        private static readonly List<PendingEventRecycle> pendingEventRecycles = new List<PendingEventRecycle>(32);
        private static readonly HashSet<ActiveEvent> pendingEventRecycleSet = new HashSet<ActiveEvent>();

        // Per-frame cached category counts (avoids recomputing on multiple steals/frame)
        private static int categoryCountsCacheFrame = -1;

        // Voice stealing victim cache avoids O(N) scan for every steal in the same frame.
        private static int cachedStealVictimFrame = -1;
        private static ActiveEvent cachedStealVictim;
        private static float cachedStealVictimScore = float.MaxValue;
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
        [Tooltip("Optional explicit AudioDuckingProfile. If assigned, it takes precedence over SetConfig and FindConfig.")]
        [SerializeField] private AudioDuckingProfile duckingProfileOverride;

        [Header("Mixing")]
        [SerializeField] private AudioMixer mainMixer;
        private static AudioMixer staticMainMixer;

        private static bool isInitialized;
        private static readonly List<AudioSource> sourcePool = new List<AudioSource>();
        public static IReadOnlyList<AudioSource> SourcePool => sourcePool;

        // Smart pool management
        private static AudioPoolConfig activePoolConfig;
        private static AudioPlatformProfile activePlatformProfile;
        private static AudioDuckingProfile activeDuckingProfile;
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
        private const int AudioEventCategoryCount = 5;
        private static readonly int[] activeEventCategoryCounts = new int[AudioEventCategoryCount];
        private static readonly Dictionary<RepeatTriggerKey, float> recentTriggerTimes = new Dictionary<RepeatTriggerKey, float>(256);
        private static readonly List<RepeatTriggerKey> reusableExpiredTriggerKeys = new List<RepeatTriggerKey>(64);
        private static float lastRepeatTriggerCleanupTime;
        private struct DuckingRuntimeState
        {
            public bool Initialized;
            public float CurrentValue;
            public bool Active;
        }
        private static DuckingRuntimeState[] duckingRuntimeStates = new DuckingRuntimeState[8];
        private static int activeDuckingRuleCount;


#if UNITY_EDITOR || DEVELOPMENT_BUILD
        // Memory tracking - use struct-based approach for better cache coherence
        private static readonly Dictionary<AudioClip, int> activeClipRefCount = new Dictionary<AudioClip, int>();
        private static readonly Dictionary<AudioClip, long> clipMemoryCache = new Dictionary<AudioClip, long>();
        private const int TrackMemoryClipScratchCapacity = 16;
        private static readonly AudioClip[] reusableTrackMemoryClips = new AudioClip[TrackMemoryClipScratchCapacity];
#endif

        private static readonly Dictionary<int, PreparedAudioEventData> preparedAudioEvents = new Dictionary<int, PreparedAudioEventData>(256);

        // O(1) lookup and removal for event names
        private static readonly ConcurrentDictionary<string, AudioEvent> eventNameMap = new ConcurrentDictionary<string, AudioEvent>();
        private static readonly ConcurrentDictionary<string, AudioParameter> parameterNameMap = new ConcurrentDictionary<string, AudioParameter>();
        private static readonly ConcurrentDictionary<string, AudioStateGroup> stateGroupNameMap = new ConcurrentDictionary<string, AudioStateGroup>();
        private static readonly List<AudioStateMixProfile> registeredStateMixProfiles = new List<AudioStateMixProfile>(32);

        private static readonly Dictionary<int, AudioBankRegistration> bankRegistrations = new Dictionary<int, AudioBankRegistration>(16);
        private static readonly Dictionary<string, List<BankNameContribution<AudioEvent>>> eventBindings = new Dictionary<string, List<BankNameContribution<AudioEvent>>>(StringComparer.Ordinal);
        private static readonly Dictionary<string, List<BankNameContribution<AudioParameter>>> parameterBindings = new Dictionary<string, List<BankNameContribution<AudioParameter>>>(StringComparer.Ordinal);
        private static readonly Dictionary<string, List<BankNameContribution<AudioStateGroup>>> stateGroupBindings = new Dictionary<string, List<BankNameContribution<AudioStateGroup>>>(StringComparer.Ordinal);
        private static readonly Dictionary<int, int> eventOwnerCounts = new Dictionary<int, int>(256);
        private static readonly Dictionary<int, int> parameterOwnerCounts = new Dictionary<int, int>(128);
        private static readonly Dictionary<int, int> switchOwnerCounts = new Dictionary<int, int>(64);
        private static readonly Dictionary<int, int> stateGroupOwnerCounts = new Dictionary<int, int>(64);
        private static readonly Dictionary<int, int> profileOwnerCounts = new Dictionary<int, int>(64);
        private static readonly HashSet<int> eventsBeingUnloaded = new HashSet<int>();
        private static readonly HashSet<int> bankOperationsInProgress = new HashSet<int>();
        private static readonly HashSet<int> groupsBeingReplaced = new HashSet<int>();
        private static readonly Dictionary<int, IAudioBankClipLease> preloadedBankClipLeases = new Dictionary<int, IAudioBankClipLease>(16);
        private static readonly Dictionary<int, PreloadBankRequest> preloadBankRequests = new Dictionary<int, PreloadBankRequest>(16);
        private static readonly Dictionary<int, int> preloadBankMutationVersions = new Dictionary<int, int>(16);
        private static int nextPreloadRequestId;

        private sealed class PreloadBankRequest
        {
            public readonly int RequestId;
            public readonly int LifecycleGeneration;
            public readonly CancellationTokenSource Cancellation;

            public PreloadBankRequest(int requestId, int lifecycleGeneration, CancellationTokenSource cancellation)
            {
                RequestId = requestId;
                LifecycleGeneration = lifecycleGeneration;
                Cancellation = cancellation;
            }
        }

        // O(1) bank tracking with instance ID as key for fast unload
        private static readonly ConcurrentDictionary<int, AudioBank> loadedBanks = new ConcurrentDictionary<int, AudioBank>();

        // Reusable collections to avoid allocations during bank loading
        private static readonly List<AudioParameter> reusableGlobalParameters = new List<AudioParameter>();
        private static bool globalParametersCacheDirty = true;
        private static readonly Dictionary<AudioParameterScopeKey, AudioScopedParameterValue> scopedParameterOverrides = new Dictionary<AudioParameterScopeKey, AudioScopedParameterValue>(128);
        private static readonly Dictionary<int, WeakReference<GameObject>> scopedParameterOwners = new Dictionary<int, WeakReference<GameObject>>(32);
        private static readonly List<AudioParameterScopeKey> reusableScopedParameterRemovalKeys = new List<AudioParameterScopeKey>(32);
        private static readonly HashSet<int> reusableDeadParameterScopeIds = new HashSet<int>();
        private static int lastScopedParameterOwnerCleanupFrame = -1;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        public static long TotalMemoryUsage { get; private set; }
        public static IReadOnlyDictionary<AudioClip, int> ActiveClipRefCount => activeClipRefCount;
        public static IReadOnlyDictionary<AudioClip, long> ClipMemoryCache => clipMemoryCache;
#endif

        private static float globalVolume = 1f;
        private static bool globalPaused;
        [Flags]
        private enum SystemPauseReason
        {
            None = 0,
            ApplicationPause = 1,
            FocusLoss = 2
        }
        private static SystemPauseReason systemPauseReasons;
        internal static bool IsPlaybackPaused => globalPaused || systemPauseReasons != SystemPauseReason.None;
        internal static AudioPauseReason CurrentPauseReasons
        {
            get
            {
                AudioPauseReason reasons = globalPaused ? AudioPauseReason.Global : AudioPauseReason.None;
                if ((systemPauseReasons & SystemPauseReason.ApplicationPause) != 0)
                    reasons |= AudioPauseReason.ApplicationPause;
                if ((systemPauseReasons & SystemPauseReason.FocusLoss) != 0)
                    reasons |= AudioPauseReason.FocusLoss;
                return reasons;
            }
        }
        private static AudioFocusMode activeFocusMode = AudioFocusMode.All;
        private static bool debugMode;
        public static bool DebugMode => debugMode;
        private const int MaxPreviousEvents = 300;

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

        public static AudioRuntimeStats GetRuntimeStats()
        {
            AudioRuntimeThreadGuard.EnsureMainThread(nameof(GetRuntimeStats));
            ExternalAudioClipCacheStats externalCache = AudioClipResolver.GetExternalCacheStats();
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            long totalPlayed = Interlocked.Read(ref totalEventsPlayed);
            int peakEvents = peakActiveEvents;
            long totalMemory = TotalMemoryUsage;
#else
            const long totalPlayed = 0;
            const int peakEvents = 0;
#endif

            return new AudioRuntimeStats(
                ActiveEvents != null ? ActiveEvents.Count : 0,
                sourcePool.Count,
                initialPoolSize,
                currentPoolSize,
                maxPoolSize,
                currentPoolSize - availableSources.Count,
                availableSources.Count,
                currentPoolSize > 0 ? (float)(currentPoolSize - availableSources.Count) / currentPoolSize : 0f,
                peakPoolUsage,
                totalExpansions,
                totalSteals,
                totalRepeatTriggerRejections,
                totalAudibilityCulls,
                loadedBanks.Count,
                eventNameMap.Count,
                parameterNameMap.Count,
                stateGroupNameMap.Count,
                registeredStateMixProfiles.Count,
                activeDuckingProfile != null ? activeDuckingProfile.RuleCount : 0,
                activeDuckingRuleCount,
                scopedParameterOverrides.Count,
                pendingRemovals.Count,
                0,
                externalCache,
                totalPlayed,
                peakEvents
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                , totalMemory
#endif
            );
        }

        public static void GetCategoryVoiceStats(List<AudioCategoryVoiceStats> results)
        {
            AudioRuntimeThreadGuard.EnsureMainThread(nameof(GetCategoryVoiceStats));
            if (results == null) return;

            EnsureCategoryCountsCached();
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
            AudioRuntimeThreadGuard.EnsureMainThread(nameof(GetPreviousEvents));
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
        void IAudioService.SetParameterValue(AudioParameter parameter, float value) => SetParameterValue(parameter, value);
        bool IAudioService.SetParameterValue(string parameterName, float value) => SetParameterValue(parameterName, value);
        void IAudioService.SetParameterValue(AudioParameter parameter, GameObject emitterObject, float value) => SetParameterValue(parameter, emitterObject, value);
        bool IAudioService.SetParameterValue(string parameterName, GameObject emitterObject, float value) => SetParameterValue(parameterName, emitterObject, value);
        float IAudioService.GetParameterValue(AudioParameter parameter) => GetParameterValue(parameter);
        float IAudioService.GetParameterValue(AudioParameter parameter, GameObject emitterObject) => GetParameterValue(parameter, emitterObject);
        bool IAudioService.TryGetParameterValue(string parameterName, out float value) => TryGetParameterValue(parameterName, out value);
        AudioParameter IAudioService.GetParameterByName(string parameterName) => GetParameterByName(parameterName);
        bool IAudioService.ClearParameterValue(AudioParameter parameter, GameObject emitterObject) => ClearParameterValue(parameter, emitterObject);
        bool IAudioService.ClearParameterValue(string parameterName, GameObject emitterObject) => ClearParameterValue(parameterName, emitterObject);
        void IAudioService.SetState(AudioStateGroup stateGroup, int stateValue) => SetState(stateGroup, stateValue);
        bool IAudioService.SetState(string stateGroupName, string stateName) => SetState(stateGroupName, stateName);
        void IAudioService.ExecuteActionEvent(AudioActionEvent actionEvent, GameObject emitterObject) => ExecuteActionEvent(actionEvent, emitterObject);
        void IAudioService.ExecuteActionEvent(AudioActionEvent actionEvent, Vector3 position) => ExecuteActionEvent(actionEvent, position);
        AudioStateGroup IAudioService.GetStateGroupByName(string stateGroupName) => GetStateGroupByName(stateGroupName);
        bool IAudioService.TryGetState(string stateGroupName, out string stateName) => TryGetState(stateGroupName, out stateName);
        void IAudioService.LoadBank(AudioBank bank, bool overwriteExisting) => LoadBank(bank, overwriteExisting);
        void IAudioService.UnloadBank(AudioBank bank) => UnloadBank(bank);
        UniTask<int> IAudioService.PreloadBankClipsAsync(AudioBank bank, CancellationToken cancellationToken) => PreloadBankClipsAsync(bank, cancellationToken);
        void IAudioLifecyclePauseControl.ResumeLifecyclePausedEvents() => ResumeLifecyclePausedEvents();
        UniTask<IAudioBankClipLease> IAudioBankClipLeaseProvider.AcquireBankClipLeaseAsync(
            AudioBank bank,
            CancellationToken cancellationToken) => AcquireBankClipLeaseAsync(bank, cancellationToken);

        public void SetMixerVolume(string parameterName, float volume)
        {
            AudioRuntimeThreadGuard.EnsureMainThread(nameof(SetMixerVolume));
            if (string.IsNullOrEmpty(parameterName) || !IsFinite(volume)) return;
            if (mainMixer != null)
                mainMixer.SetFloat(parameterName, volume);
            else
                Debug.LogWarning("AudioManager: Main Mixer not assigned.");
        }

        public static bool SetMixerParameter(string parameterName, float value)
        {
            AudioRuntimeThreadGuard.EnsureMainThread(nameof(SetMixerParameter));
            if (string.IsNullOrEmpty(parameterName) || !IsFinite(value)) return false;

            AudioMixer mixer = staticMainMixer;
            return mixer != null && mixer.SetFloat(parameterName, value);
        }

        public float GetMixerVolume(string parameterName)
        {
            AudioRuntimeThreadGuard.EnsureMainThread(nameof(GetMixerVolume));
            if (mainMixer != null && mainMixer.GetFloat(parameterName, out float value))
                return value;
            return 0f;
        }

        public static void SetGlobalVolume(float volume)
        {
            AudioRuntimeThreadGuard.EnsureMainThread(nameof(SetGlobalVolume));
            if (!IsFinite(volume)) return;
            globalVolume = Mathf.Clamp01(volume);
            AudioListener.volume = globalVolume;
        }

        public static float GetGlobalVolume()
        {
            AudioRuntimeThreadGuard.EnsureMainThread(nameof(GetGlobalVolume));
            return globalVolume;
        }

        public static void SetParameterValue(AudioParameter parameter, float value)
        {
            AudioRuntimeThreadGuard.EnsureMainThread(nameof(SetParameterValue));
            if (parameter == null) throw new ArgumentNullException(nameof(parameter));
            if (!IsFinite(value)) return;

            parameter.SetValue(value);
        }

        public static bool SetParameterValue(string parameterName, float value)
        {
            AudioRuntimeThreadGuard.EnsureMainThread(nameof(SetParameterValue));
            if (!IsFinite(value)) return false;
            AudioParameter parameter = GetParameterByName(parameterName);
            if (parameter == null) return false;

            parameter.SetValue(value);
            return true;
        }

        public static void SetParameterValue(AudioParameter parameter, GameObject emitterObject, float value)
        {
            AudioRuntimeThreadGuard.EnsureMainThread(nameof(SetParameterValue));
            if (parameter == null) throw new ArgumentNullException(nameof(parameter));
            if (!IsFinite(value)) return;
            if (emitterObject == null)
            {
                parameter.SetValue(value);
                return;
            }

            int parameterId = parameter.GetInstanceID();
            int scopeId = emitterObject.GetInstanceID();
            if (!scopedParameterOwners.TryGetValue(scopeId, out WeakReference<GameObject> ownerReference)
                || !ownerReference.TryGetTarget(out GameObject currentOwner)
                || currentOwner != emitterObject)
            {
                RemoveScopedParameterOverridesForScope(scopeId);
                scopedParameterOwners[scopeId] = new WeakReference<GameObject>(emitterObject);
            }

            var key = new AudioParameterScopeKey(parameterId, scopeId);
            if (!scopedParameterOverrides.TryGetValue(key, out AudioScopedParameterValue scopedValue))
            {
                scopedValue = new AudioScopedParameterValue();
                scopedValue.Initialize(parameter, GetParameterValue(parameter, scopeId));
                scopedParameterOverrides.Add(key, scopedValue);
            }

            scopedValue.SetTarget(value);
        }

        public static bool SetParameterValue(string parameterName, GameObject emitterObject, float value)
        {
            AudioRuntimeThreadGuard.EnsureMainThread(nameof(SetParameterValue));
            if (!IsFinite(value)) return false;
            AudioParameter parameter = GetParameterByName(parameterName);
            if (parameter == null) return false;

            SetParameterValue(parameter, emitterObject, value);
            return true;
        }

        public static float GetParameterValue(AudioParameter parameter)
        {
            AudioRuntimeThreadGuard.EnsureMainThread(nameof(GetParameterValue));
            if (parameter == null) throw new ArgumentNullException(nameof(parameter));

            return parameter.EvaluateCurrentValue();
        }

        public static bool TryGetParameterValue(string parameterName, out float value)
        {
            AudioRuntimeThreadGuard.EnsureMainThread(nameof(TryGetParameterValue));
            AudioParameter parameter = GetParameterByName(parameterName);
            if (parameter == null)
            {
                value = 0f;
                return false;
            }

            value = parameter.EvaluateCurrentValue();
            return true;
        }

        public static float GetParameterValue(AudioParameter parameter, GameObject emitterObject)
        {
            AudioRuntimeThreadGuard.EnsureMainThread(nameof(GetParameterValue));
            if (parameter == null) throw new ArgumentNullException(nameof(parameter));

            return emitterObject != null
                ? GetParameterValue(parameter, emitterObject.GetInstanceID())
                : parameter.EvaluateCurrentValue();
        }

        internal static float GetParameterValue(AudioParameter parameter, int scopeId)
        {
            if (parameter == null) return 0f;
            if (scopeId != 0)
            {
                var key = new AudioParameterScopeKey(parameter.GetInstanceID(), scopeId);
                if (scopedParameterOverrides.TryGetValue(key, out AudioScopedParameterValue scopedValue))
                    return scopedValue.CurrentValue;
            }

            return parameter.EvaluateCurrentValue();
        }

        public static bool ClearParameterValue(AudioParameter parameter, GameObject emitterObject)
        {
            AudioRuntimeThreadGuard.EnsureMainThread(nameof(ClearParameterValue));
            if (parameter == null) throw new ArgumentNullException(nameof(parameter));
            if (emitterObject == null) return false;

            int scopeId = emitterObject.GetInstanceID();
            bool removed = scopedParameterOverrides.Remove(new AudioParameterScopeKey(parameter.GetInstanceID(), scopeId));
            if (removed)
            {
                RemoveScopedParameterOwnerIfUnused(scopeId);
            }
            return removed;
        }

        public static bool ClearParameterValue(string parameterName, GameObject emitterObject)
        {
            AudioRuntimeThreadGuard.EnsureMainThread(nameof(ClearParameterValue));
            AudioParameter parameter = GetParameterByName(parameterName);
            return parameter != null && ClearParameterValue(parameter, emitterObject);
        }

        /// <summary>
        /// Removes every emitter-scoped parameter override owned by <paramref name="emitterObject"/>.
        /// Call this during explicit emitter teardown when immediate cleanup is required.
        /// Destroyed emitters are also removed automatically by the runtime maintenance pass.
        /// </summary>
        /// <returns>The number of removed overrides.</returns>
        public static int ClearScopedParameterValues(GameObject emitterObject)
        {
            AudioRuntimeThreadGuard.EnsureMainThread(nameof(ClearScopedParameterValues));
            if (emitterObject == null) return 0;

            int scopeId = emitterObject.GetInstanceID();
            int removedCount = RemoveScopedParameterOverridesForScope(scopeId);
            scopedParameterOwners.Remove(scopeId);
            return removedCount;
        }

        public static void ClearAllScopedParameterValues()
        {
            AudioRuntimeThreadGuard.EnsureMainThread(nameof(ClearAllScopedParameterValues));
            scopedParameterOverrides.Clear();
            scopedParameterOwners.Clear();
        }

        internal static void ValidateScopedParameterOwner(GameObject emitterObject)
        {
            if (emitterObject == null) return;

            int scopeId = emitterObject.GetInstanceID();
            if (!scopedParameterOwners.TryGetValue(scopeId, out WeakReference<GameObject> ownerReference))
                return;
            if (ownerReference.TryGetTarget(out GameObject currentOwner) && currentOwner == emitterObject)
                return;

            RemoveScopedParameterOverridesForScope(scopeId);
            scopedParameterOwners.Remove(scopeId);
        }

        public static void SetState(AudioStateGroup stateGroup, int stateValue)
        {
            if (stateGroup == null) throw new ArgumentNullException(nameof(stateGroup));

            AudioRuntimeThreadGuard.EnsureMainThread(nameof(SetState));

            int previousValue = stateGroup.CurrentValue;
            stateGroup.SetValue(stateValue);
            if (stateGroup.CurrentValue != previousValue)
            {
                ApplyStateMixProfiles(stateGroup);
            }
        }

        public static bool SetState(string stateGroupName, string stateName)
        {
            if (string.IsNullOrEmpty(stateGroupName)) return false;
            if (string.IsNullOrEmpty(stateName)) return false;

            AudioRuntimeThreadGuard.EnsureMainThread(nameof(SetState));

            AudioStateGroup stateGroup = GetStateGroupByName(stateGroupName);
            if (stateGroup == null) return false;

            int previousValue = stateGroup.CurrentValue;
            bool changed = stateGroup.SetValue(stateName);
            if (changed && stateGroup.CurrentValue != previousValue)
            {
                ApplyStateMixProfiles(stateGroup);
            }

            return changed;
        }

        public static AudioStateGroup GetStateGroupByName(string stateGroupName)
        {
            AudioRuntimeThreadGuard.EnsureMainThread(nameof(GetStateGroupByName));
            if (string.IsNullOrEmpty(stateGroupName)) return null;
            stateGroupNameMap.TryGetValue(stateGroupName, out AudioStateGroup result);
            return result;
        }

        public static bool TryGetState(string stateGroupName, out string stateName)
        {
            AudioRuntimeThreadGuard.EnsureMainThread(nameof(TryGetState));
            AudioStateGroup stateGroup = GetStateGroupByName(stateGroupName);
            if (stateGroup == null)
            {
                stateName = string.Empty;
                return false;
            }

            stateName = stateGroup.CurrentStateName;
            return true;
        }

        public static void ExecuteActionEvent(AudioActionEvent actionEvent, GameObject emitterObject = null)
        {
            if (actionEvent == null) throw new ArgumentNullException(nameof(actionEvent));

            AudioRuntimeThreadGuard.EnsureMainThread(nameof(ExecuteActionEvent));

            actionEvent.Execute(emitterObject);
        }

        public static void ExecuteActionEvent(AudioActionEvent actionEvent, Vector3 position)
        {
            if (actionEvent == null) throw new ArgumentNullException(nameof(actionEvent));

            AudioRuntimeThreadGuard.EnsureMainThread(nameof(ExecuteActionEvent));
            if (!IsFinite(position)) return;

            actionEvent.Execute(position);
        }

        /// <summary>
        /// Get or set which Unity lifecycle events automatically pause/resume audio at runtime.
        /// Set to <see cref="AudioFocusMode.None"/> if your game manages audio pause/resume manually.
        /// </summary>
        public static AudioFocusMode FocusMode
        {
            get
            {
                AudioRuntimeThreadGuard.EnsureMainThread(nameof(FocusMode));
                return activeFocusMode;
            }
            set
            {
                AudioRuntimeThreadGuard.EnsureMainThread(nameof(FocusMode));
                activeFocusMode = value;
            }
        }

        public static void PauseAll()
        {
            AudioRuntimeThreadGuard.EnsureMainThread(nameof(PauseAll));
            if (!ValidateManager()) return;
            globalPaused = true;
            int count = ActiveEvents.Count;
            for (int i = 0; i < count; i++)
                ActiveEvents[i]?.SetPauseReason(AudioPauseReason.Global, true);
        }

        public static void ResumeAll()
        {
            AudioRuntimeThreadGuard.EnsureMainThread(nameof(ResumeAll));
            if (!ValidateManager()) return;
            globalPaused = false;
            int count = ActiveEvents.Count;
            for (int i = 0; i < count; i++)
            {
                ActiveEvent activeEvent = ActiveEvents[i];
                if (activeEvent == null) continue;
                activeEvent.SetPauseReason(AudioPauseReason.Global, false);
            }
        }

        /// <summary>
        /// Resumes events held after an automatic lifecycle pause whose matching auto-resume
        /// option was disabled. Manual per-event and PauseAll reasons remain unchanged.
        /// </summary>
        public static void ResumeLifecyclePausedEvents()
        {
            AudioRuntimeThreadGuard.EnsureMainThread(nameof(ResumeLifecyclePausedEvents));
            if (!ValidateManager()) return;

            int count = ActiveEvents.Count;
            for (int i = 0; i < count; i++)
                ActiveEvents[i]?.SetPauseReason(AudioPauseReason.LifecycleHold, false);
        }

        public static bool IsEventPlaying(string eventName)
        {
            AudioRuntimeThreadGuard.EnsureMainThread(nameof(IsEventPlaying));
            if (string.IsNullOrEmpty(eventName)) return false;
            if (ActiveEvents == null) return false;

            for (int i = 0; i < ActiveEvents.Count; i++)
            {
                ActiveEvent activeEvent = ActiveEvents[i];
                if (activeEvent != null &&
                    activeEvent.status == EventStatus.Played &&
                    string.Equals(activeEvent.name, eventName, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// DI-friendly: register an externally-created AudioManager as the singleton.
        /// Call before any PlayEvent if using dependency injection.
        /// </summary>
        public static void SetInstance(AudioManager manager)
        {
            AudioRuntimeThreadGuard.EnsureMainThread(nameof(SetInstance));
            if (manager == null || isTearingDown) return;
            Instance = manager;
            if (!isInitialized) manager.Initialize();
        }

        internal static void ReleaseInstance(AudioManager manager)
        {
            AudioRuntimeThreadGuard.EnsureMainThread(nameof(ReleaseInstance));
            if (ReferenceEquals(manager, null)) return;
            if (ReferenceEquals(Instance, manager)) Cleanup();
        }

        /// <summary>
        /// Start playing an AudioEvent on the Unity main thread.
        /// </summary>
        public static ActiveEvent PlayEvent(AudioEvent eventToPlay, GameObject emitterObject)
        {
            AudioRuntimeThreadGuard.EnsureMainThread(nameof(PlayEvent));

            Transform emitterTransform = emitterObject != null ? emitterObject.transform : null;
            if (!ValidateManager() || !ValidateEvent(eventToPlay, emitterTransform, null, false, out RepeatTriggerKey triggerKey)) return null;

            ActiveEvent tempEvent = GetActiveEventFromPool(eventToPlay, emitterTransform);
            tempEvent.Play();
            if (IsFailedPlaybackStatus(tempEvent))
            {
                RecycleInactiveEvent(tempEvent);
                return null;
            }

            if (!IsFailedPlaybackStatus(tempEvent))
                CommitRepeatTrigger(triggerKey);

            return tempEvent;
        }

        public static ActiveEvent PlayEvent(AudioEvent eventToPlay, Vector3 position)
        {
            AudioRuntimeThreadGuard.EnsureMainThread(nameof(PlayEvent));

            if (!IsFinite(position)) return null;
            if (!ValidateManager() || !ValidateEvent(eventToPlay, null, position, false, out RepeatTriggerKey triggerKey)) return null;

            ActiveEvent tempEvent = GetActiveEventFromPool(eventToPlay, null);
            tempEvent.SetEmitterPosition(position);
            tempEvent.Play();
            if (IsFailedPlaybackStatus(tempEvent))
            {
                RecycleInactiveEvent(tempEvent);
                return null;
            }

            if (!IsFailedPlaybackStatus(tempEvent))
                CommitRepeatTrigger(triggerKey);

            return tempEvent;
        }

        public static ActiveEvent PlayEvent(string eventName, GameObject emitterObject)
        {
            AudioRuntimeThreadGuard.EnsureMainThread(nameof(PlayEvent));

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
            AudioRuntimeThreadGuard.EnsureMainThread(nameof(PlayEvent));

            if (!IsFinite(position)) return null;
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
            AudioRuntimeThreadGuard.EnsureMainThread(nameof(PlayEventScheduled));

            if (!IsFinite(dspTime))
            {
                Debug.LogWarning("AudioManager: Scheduled DSP time must be finite.");
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
            AudioRuntimeThreadGuard.EnsureMainThread(nameof(PlayEventScheduled));

            if (!IsFinite(dspTime))
            {
                Debug.LogWarning("AudioManager: Scheduled DSP time must be finite.");
                return null;
            }
            Transform emitterTransform = emitterObject != null ? emitterObject.transform : null;
            if (!ValidateManager() || !ValidateEvent(eventToPlay, emitterTransform, null, true, out RepeatTriggerKey triggerKey)) return null;

            double currentDspTime = AudioSettings.dspTime;
            if (dspTime < currentDspTime) dspTime = currentDspTime + 0.01;

            ActiveEvent tempEvent = GetActiveEventFromPool(eventToPlay, emitterTransform);
            tempEvent.scheduledDspTime = dspTime;
            tempEvent.Play();
            if (IsFailedPlaybackStatus(tempEvent))
            {
                RecycleInactiveEvent(tempEvent);
                return null;
            }

            if (!IsFailedPlaybackStatus(tempEvent))
                CommitRepeatTrigger(triggerKey);

            return tempEvent;
        }

        private static bool IsFailedPlaybackStatus(ActiveEvent activeEvent)
        {
            return activeEvent == null ||
                   (activeEvent.status != EventStatus.Preparing && activeEvent.status != EventStatus.Played);
        }

        private static ActiveEvent GetActiveEventFromPool(AudioEvent eventToPlay, Transform emitter)
        {
            ActiveEvent activeEvent = activeEventPool.Count > 0 ? activeEventPool.Pop() : new ActiveEvent();
            activeEvent.InitializeForManager(eventToPlay, emitter);
            RegisterActiveEventHandle(activeEvent);
            return activeEvent;
        }

        private static void RecycleInactiveEvent(ActiveEvent activeEvent)
        {
            if (activeEvent == null) return;
            if (activeEvent.handleSlot < 0 && !activeEventIndices.ContainsKey(activeEvent)) return;

            UnregisterActiveEventHandle(activeEvent);
            activeEvent.DetachSourcesToPool();
            RetainRecycledEvent(activeEvent);
        }

        public static void StopAll(AudioEvent eventsToStop)
        {
            AudioRuntimeThreadGuard.EnsureMainThread(nameof(StopAll));

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
            AudioRuntimeThreadGuard.EnsureMainThread(nameof(StopAll));

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
            AudioRuntimeThreadGuard.EnsureMainThread(nameof(StopAll));

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
            AudioRuntimeThreadGuard.EnsureMainThread(nameof(RemoveActiveEvent));
            if (isTearingDown || !ValidateManager()) return;
            if (stoppedEvent == null || !activeEventIndices.ContainsKey(stoppedEvent)) return;

            if (stoppedEvent.status != EventStatus.Stopped)
                stoppedEvent.StopImmediate();
            else
                DeactivateActiveEvent(stoppedEvent);
        }

        private static void RemoveActiveEventInternal(ActiveEvent stoppedEvent)
        {
            if (stoppedEvent == null) return;
            if (DeactivateActiveEventInternal(stoppedEvent, queueForRecycle: false))
            {
                RecycleEventNow(stoppedEvent);
                return;
            }

            RecycleEventNow(stoppedEvent);
        }

        internal static bool DeactivateActiveEvent(ActiveEvent stoppedEvent)
        {
            AudioRuntimeThreadGuard.EnsureMainThread(nameof(DeactivateActiveEvent));
            return DeactivateActiveEventInternal(stoppedEvent, queueForRecycle: true);
        }

        private static bool DeactivateActiveEventInternal(ActiveEvent stoppedEvent, bool queueForRecycle)
        {
            if (stoppedEvent == null || ActiveEvents == null) return false;

            if (!activeEventIndices.TryGetValue(stoppedEvent, out int idx)) return false;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            // Track memory BEFORE clearing clips, so TrackMemory can read source.clip
            if (stoppedEvent.memoryTracked)
            {
                TrackMemory(stoppedEvent, false);
                stoppedEvent.memoryTracked = false;
            }
#endif

            // Decrement O(1) tracking counters
            if (stoppedEvent.rootEvent != null)
            {
                int eventId = stoppedEvent.rootEvent.GetInstanceID();
                if (activeInstanceCounts.TryGetValue(eventId, out int cnt))
                {
                    if (cnt <= 1) activeInstanceCounts.Remove(eventId);
                    else activeInstanceCounts[eventId] = cnt - 1;
                }

                DecrementActiveEventCategory(stoppedEvent.rootEvent.Category);
            }
            if (!string.IsNullOrEmpty(stoppedEvent.name))
            {
                if (playingEventNameCounts.TryGetValue(stoppedEvent.name, out int nc))
                {
                    if (nc <= 1) playingEventNameCounts.Remove(stoppedEvent.name);
                    else playingEventNameCounts[stoppedEvent.name] = nc - 1;
                }
            }

            stoppedEvent.DetachSourcesToPool();

            // O(1) swap-remove from ActiveEvents + index tracking
            int last = ActiveEvents.Count - 1;
            if (idx < last)
            {
                var swapped = ActiveEvents[last];
                ActiveEvents[idx] = swapped;
                activeEventIndices[swapped] = idx;
            }
            ActiveEvents.RemoveAt(last);
            activeEventIndices.Remove(stoppedEvent);

            RemovePendingRemovalsForEvent(stoppedEvent, stoppedEvent.generation);
            UnregisterActiveEventHandle(stoppedEvent);
            if (queueForRecycle && pendingEventRecycleSet.Add(stoppedEvent))
            {
                pendingEventRecycles.Add(new PendingEventRecycle
                {
                    Event = stoppedEvent,
                    Generation = stoppedEvent.generation
                });
            }
            InvalidateVoiceSelectionCaches();
            return true;
        }

        private static void RecycleEventNow(ActiveEvent activeEvent)
        {
            if (activeEvent == null || activeEventIndices.ContainsKey(activeEvent)) return;
            if (activeEvent.handleSlot >= 0) return;

            pendingEventRecycleSet.Remove(activeEvent);
            for (int i = pendingEventRecycles.Count - 1; i >= 0; i--)
            {
                if (!ReferenceEquals(pendingEventRecycles[i].Event, activeEvent)) continue;
                int last = pendingEventRecycles.Count - 1;
                if (i < last) pendingEventRecycles[i] = pendingEventRecycles[last];
                pendingEventRecycles.RemoveAt(last);
            }

            RetainRecycledEvent(activeEvent);
        }

        private static void ProcessPendingEventRecycles()
        {
            for (int i = pendingEventRecycles.Count - 1; i >= 0; i--)
            {
                PendingEventRecycle pending = pendingEventRecycles[i];
                ActiveEvent activeEvent = pending.Event;
                pendingEventRecycles.RemoveAt(i);
                pendingEventRecycleSet.Remove(activeEvent);
                if (activeEvent == null || activeEvent.generation != pending.Generation) continue;
                if (activeEventIndices.ContainsKey(activeEvent) || activeEvent.handleSlot >= 0) continue;

                RetainRecycledEvent(activeEvent);
            }
        }

        private static void RetainRecycledEvent(ActiveEvent activeEvent)
        {
            if (activeEvent == null) return;

            activeEvent.ResetForPool();
            activeEvent.isInPool = true;
            if (activeEventPool.Count < MaxRetainedActiveEvents)
                activeEventPool.Push(activeEvent);
        }

        /// <summary>
        /// Registers a newly-played event into ActiveEvents and all O(1) tracking structures.
        /// Called from ActiveEvent.Play() instead of directly modifying ActiveEvents.
        /// </summary>
        internal static void RegisterActiveEvent(ActiveEvent activeEvent)
        {
            int idx = ActiveEvents.Count;
            ActiveEvents.Add(activeEvent);
            activeEventIndices[activeEvent] = idx;

            if (activeEvent.rootEvent != null)
            {
                int eventId = activeEvent.rootEvent.GetInstanceID();
                activeInstanceCounts.TryGetValue(eventId, out int cnt);
                activeInstanceCounts[eventId] = cnt + 1;
                IncrementActiveEventCategory(activeEvent.rootEvent.Category);
            }
            if (!string.IsNullOrEmpty(activeEvent.name))
            {
                playingEventNameCounts.TryGetValue(activeEvent.name, out int nc);
                playingEventNameCounts[activeEvent.name] = nc + 1;
            }

            InvalidateVoiceSelectionCaches();
        }

        private static void InvalidateVoiceSelectionCaches()
        {
            categoryCountsCacheFrame = -1;
            cachedStealVictimFrame = -1;
            cachedStealVictim = null;
            cachedStealVictimScore = float.MaxValue;
        }

        internal static void NotifyActiveEventSourcesChanged(ActiveEvent activeEvent)
        {
            if (activeEvent != null && activeEventIndices.ContainsKey(activeEvent))
                InvalidateVoiceSelectionCaches();
        }

        public static void AddPreviousEvent(ActiveEvent newEvent)
        {
            AudioRuntimeThreadGuard.EnsureMainThread(nameof(AddPreviousEvent));
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

        private static void IncrementActiveEventCategory(AudioEventCategory category)
        {
            int index = GetCategoryIndex(category);
            activeEventCategoryCounts[index]++;
        }

        private static void DecrementActiveEventCategory(AudioEventCategory category)
        {
            int index = GetCategoryIndex(category);
            int count = activeEventCategoryCounts[index];
            activeEventCategoryCounts[index] = count > 0 ? count - 1 : 0;
        }

        private static void ClearActiveEventCategoryCounts()
        {
            for (int i = 0; i < activeEventCategoryCounts.Length; i++)
            {
                activeEventCategoryCounts[i] = 0;
            }
        }

        private static int GetCategoryIndex(AudioEventCategory category)
        {
            int index = (int)category;
            return (uint)index < AudioEventCategoryCount ? index : (int)AudioEventCategory.GameplaySFX;
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

            unchecked
            {
                nextActiveEventHandleGeneration++;
            }
            if (nextActiveEventHandleGeneration == 0)
                nextActiveEventHandleGeneration = 1;

            activeEvent.generation = nextActiveEventHandleGeneration;
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
            AudioRuntimeThreadGuard.EnsureMainThread(nameof(UpdateLanguages));
            CultureInfo[] cultures = CultureInfo.GetCultures(CultureTypes.AllCultures);
            Languages = new string[cultures.Length];
            for (int i = 0; i < cultures.Length; i++)
                Languages[i] = cultures[i].Name;
        }

        public static void SetDebugMode(bool toggle)
        {
            AudioRuntimeThreadGuard.EnsureMainThread(nameof(SetDebugMode));
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
            AudioRuntimeThreadGuard.EnsureMainThread(nameof(GetPoolStatistics));
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
            AudioRuntimeThreadGuard.EnsureMainThread(nameof(ResetPoolStatistics));
            Interlocked.Exchange(ref totalEventsPlayed, 0);
            Interlocked.Exchange(ref peakActiveEvents, 0);
            totalRepeatTriggerRejections = 0;
            totalAudibilityCulls = 0;
        }
#endif

        /// <summary>
        /// Schedules an active event for deferred removal after the specified delay.
        /// Processed in Update() with no async allocation or CTS dependency.
        /// </summary>
        public static void DelayRemoveActiveEvent(ActiveEvent eventToRemove, float delay = 1)
        {
            AudioRuntimeThreadGuard.EnsureMainThread(nameof(DelayRemoveActiveEvent));
            if (eventToRemove == null) return;
            if (isTearingDown || !ValidateManager()) return;
            if (!activeEventIndices.ContainsKey(eventToRemove)) return;
            if (float.IsNaN(delay) || float.IsInfinity(delay))
            {
                Debug.LogWarning("AudioManager: Deferred removal delay must be finite. Removing on the next update.");
                delay = 0f;
            }
            int generation = eventToRemove.generation;

            for (int i = 0; i < pendingRemovals.Count; i++)
            {
                PendingRemoval removal = pendingRemovals[i];
                if (ReferenceEquals(removal.Event, eventToRemove) && removal.Generation == generation)
                    return;
            }

            pendingRemovals.Add(new PendingRemoval
            {
                Event = eventToRemove,
                Generation = generation,
                RemoveTime = Time.unscaledTime + Mathf.Max(0f, delay)
            });
        }

        private static void RemovePendingRemovalsForEvent(ActiveEvent eventToRemove, int generation)
        {
            for (int i = pendingRemovals.Count - 1; i >= 0; i--)
            {
                PendingRemoval removal = pendingRemovals[i];
                if (!ReferenceEquals(removal.Event, eventToRemove) || removal.Generation != generation)
                    continue;

                int last = pendingRemovals.Count - 1;
                if (i < last) pendingRemovals[i] = pendingRemovals[last];
                pendingRemovals.RemoveAt(last);
            }
        }

        #endregion

        #region Unity Lifecycle

        private void Awake()
        {
            AudioRuntimeThreadGuard.CaptureCurrentThread();

            if (isTearingDown)
            {
                if (Application.isPlaying)
                    Destroy(gameObject);
                else
                    DestroyImmediate(gameObject);
                return;
            }

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
            ReleaseInstance(this);
        }

        private void OnApplicationQuit()
        {
            ReleaseInstance(this);
        }

        private void OnApplicationPause(bool pauseStatus)
        {
            if (pauseStatus)
            {
                if ((activeFocusMode & AudioFocusMode.PauseOnAppPause) != 0)
                    SystemPause(SystemPauseReason.ApplicationPause);
            }
            else
            {
                SystemResume(
                    SystemPauseReason.ApplicationPause,
                    (activeFocusMode & AudioFocusMode.ResumeOnAppResume) != 0);
            }
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            if (!hasFocus)
            {
                if ((activeFocusMode & AudioFocusMode.PauseOnFocusLost) != 0)
                    SystemPause(SystemPauseReason.FocusLoss);
            }
            else
            {
                SystemResume(
                    SystemPauseReason.FocusLoss,
                    (activeFocusMode & AudioFocusMode.ResumeOnFocusGain) != 0);
            }
        }

        /// <summary>
        /// System-initiated pause (focus/app lifecycle). Tracked separately from manual PauseAll()
        /// so that auto-resume can work even after globalPaused was set.
        /// </summary>
        private static void SystemPause(SystemPauseReason reason)
        {
            if (!ValidateManager()) return;
            systemPauseReasons |= reason;
            AudioPauseReason eventReason = reason == SystemPauseReason.ApplicationPause
                ? AudioPauseReason.ApplicationPause
                : AudioPauseReason.FocusLoss;
            int count = ActiveEvents.Count;
            for (int i = 0; i < count; i++)
                ActiveEvents[i]?.SetPauseReason(eventReason, true);
        }

        /// <summary>
        /// System-initiated resume. Only resumes if the pause was system-initiated,
        /// not if the game manually called PauseAll().
        /// </summary>
        private static void SystemResume(SystemPauseReason reason, bool autoResume)
        {
            if ((systemPauseReasons & reason) == 0) return;
            systemPauseReasons &= ~reason;
            AudioPauseReason eventReason = reason == SystemPauseReason.ApplicationPause
                ? AudioPauseReason.ApplicationPause
                : AudioPauseReason.FocusLoss;
            int count = ActiveEvents != null ? ActiveEvents.Count : 0;
            for (int i = 0; i < count; i++)
            {
                ActiveEvent activeEvent = ActiveEvents[i];
                if (activeEvent == null) continue;
                if (!autoResume)
                    activeEvent.SetPauseReason(AudioPauseReason.LifecycleHold, true);
                activeEvent.SetPauseReason(eventReason, false);
            }
        }

        private static void Cleanup()
        {
            if (isTearingDown) return;
            AudioManager instanceBeingCleaned = Instance;
            AdvanceManagerLifecycleGeneration();
            isTearingDown = true;
            try
            {
                Application.quitting -= HandleQuitting;

                var banksToNotify = new List<AudioBank>(loadedBanks.Count);
                foreach (var pair in loadedBanks)
                {
                    if (pair.Value != null)
                        banksToNotify.Add(pair.Value);
                }

                if (ActiveEvents != null)
                {
                    while (ActiveEvents.Count > 0)
                    {
                        ActiveEvent activeEvent = ActiveEvents[ActiveEvents.Count - 1];
                        if (activeEvent == null)
                        {
                            ActiveEvents.RemoveAt(ActiveEvents.Count - 1);
                            continue;
                        }

                        activeEvent.StopImmediate();
                        if (activeEventIndices.ContainsKey(activeEvent))
                            RemoveActiveEventInternal(activeEvent);
                        else
                            RecycleEventNow(activeEvent);
                    }
                }
                ProcessPendingEventRecycles();

                activeInstanceCounts.Clear();
                playingEventNameCounts.Clear();
                activeEventIndices.Clear();
                ClearActiveEventCategoryCounts();
                pendingRemovals.Clear();
                pendingEventRecycles.Clear();
                pendingEventRecycleSet.Clear();
                categoryCountsCacheFrame = -1;
                cachedStealVictimFrame = -1;
                cachedStealVictim = null;

                eventNameMap.Clear();
                parameterNameMap.Clear();
                stateGroupNameMap.Clear();
                preparedAudioEvents.Clear();
                bankRegistrations.Clear();
                eventBindings.Clear();
                parameterBindings.Clear();
                stateGroupBindings.Clear();
                eventOwnerCounts.Clear();
                parameterOwnerCounts.Clear();
                switchOwnerCounts.Clear();
                stateGroupOwnerCounts.Clear();
                profileOwnerCounts.Clear();
                loadedBanks.Clear();
                loadedBanksCacheDirty = true;
                cachedLoadedBanksList.Clear();
                cachedLoadedBanksReadOnly = null;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                activeClipRefCount.Clear();
                clipMemoryCache.Clear();
                TotalMemoryUsage = 0;
#endif
                activeEventPool.Clear();
                availableSources.Clear();
                sourcePool.Clear();
                activeEventHandleTable.Clear();
                freeActiveEventHandleSlots.Clear();
                globalVolume = 1f;
                globalPaused = false;
                systemPauseReasons = SystemPauseReason.None;
                activeFocusMode = AudioFocusMode.All;
                activePlatformProfile = null;
                activeDuckingProfile = null;
                activePoolConfig = null;
                activePlatformSettings = AudioPlatformProfile.GetFallbackSettings();
                ResetDuckingRuntimeState();
                reusableGlobalParameters.Clear();
                scopedParameterOverrides.Clear();
                scopedParameterOwners.Clear();
                reusableScopedParameterRemovalKeys.Clear();
                reusableDeadParameterScopeIds.Clear();
                lastScopedParameterOwnerCleanupFrame = -1;
                globalParametersCacheDirty = true;
                recentTriggerTimes.Clear();
                reusableExpiredTriggerKeys.Clear();
                lastRepeatTriggerCleanupTime = 0f;
                totalRepeatTriggerRejections = 0;
                totalAudibilityCulls = 0;
                AudioClipResolver.ClearExternalCache();
                ReleaseAllPreloadedBankClipLeases();
                activeBankClipLeaseMemoryBytes = 0L;
                registeredStateMixProfiles.Clear();
                eventsBeingUnloaded.Clear();
                bankOperationsInProgress.Clear();
                groupsBeingReplaced.Clear();
                preloadBankMutationVersions.Clear();
                for (int i = 0; i < banksToNotify.Count; i++)
                    NotifyBankUnloaded(banksToNotify[i]);
                staticMainMixer = null;
                currentPoolSize = 0;
                maxPoolSize = 0;
            }
            finally
            {
                isInitialized = false;
                if (ReferenceEquals(Instance, instanceBeingCleaned))
                    Instance = null;
                isTearingDown = false;
            }
        }

        private void Update()
        {
            ProcessPendingEventRecycles();

            // Process pending delayed removals (replaces async void DelayRemoveActiveEvent)
            float now = Time.unscaledTime;
            for (int i = pendingRemovals.Count - 1; i >= 0; i--)
            {
                PendingRemoval removal = pendingRemovals[i];
                if (now >= removal.RemoveTime)
                {
                    var evt = removal.Event;
                    // Swap-remove from pending list
                    int lastPending = pendingRemovals.Count - 1;
                    if (i < lastPending) pendingRemovals[i] = pendingRemovals[lastPending];
                    pendingRemovals.RemoveAt(lastPending);

                    if (evt != null && evt.generation == removal.Generation)
                        evt.StopImmediate();
                }
            }

            float deltaTime = Time.deltaTime;
            UpdateGlobalParameters(deltaTime);
            UpdateDucking(deltaTime);
            CleanupRepeatTriggerCache(Time.unscaledTime);

            // Invalidate per-frame category count cache
            categoryCountsCacheFrame = -1;

              // Update active events with distance-tiered LOD
            int eventCount = ActiveEvents.Count;
            var lodSettings = activePlatformSettings.updateLOD;
            bool lodEnabled = lodSettings.enabled;
            int recalcInterval = Mathf.Max(lodSettings.recalcFrameInterval, 1);
            bool recalcLOD = lodEnabled && (Time.frameCount % recalcInterval) == 0;
            Vector3 listenerPos = default;
            bool hasListener = false;
            if (recalcLOD && cachedAudioListener != null && cachedAudioListener.gameObject != null)
            {
                listenerPos = cachedAudioListener.transform.position;
                hasListener = true;
            }

            for (int i = eventCount - 1; i >= 0; i--)
            {
                if (i >= ActiveEvents.Count) continue;
                var tempEvent = ActiveEvents[i];
                if (tempEvent == null || tempEvent.status == EventStatus.Stopped) continue;
                if (tempEvent.SourceCount == 0 && !tempEvent.hasSnapshotTransition) continue;

                // Recalculate LOD interval based on distance to listener
                if (recalcLOD && tempEvent.is3D && hasListener)
                {
                    float sqrDist = (tempEvent.LastEmitterPosition - listenerPos).sqrMagnitude;
                    tempEvent.updateInterval = lodSettings.GetUpdateInterval(sqrDist);
                }

                tempEvent.UpdateInternal();
            }

            // Track peak usage
            int currentUsage = PoolStats.InUse;
            if (currentUsage > peakPoolUsage) peakPoolUsage = currentUsage;

            // Smart pool shrinking
            TryShrinkPool();

            // Periodic external clip cache eviction
            TryEvictExternalClips();
        }

        #endregion

        #region Initialization

        private static void CreateInstance()
        {
            if (Instance != null || !AllowCreateInstance || isTearingDown) return;
            new GameObject("AudioManager").AddComponent<AudioManager>();
        }

        private void Initialize()
        {
            CurrentLanguage = 0;
            staticMainMixer = mainMixer;
            ActiveEvents = new List<ActiveEvent>(64);
            ClearActiveEventCategoryCounts();
            ResetDuckingRuntimeState();

            ApplyResolvedConfigs();
            InitializeSmartPool();
            RefreshLoadedBankRuntimeState();
            UpdateDucking(0f);

            ValidateAudioListener();
            Application.quitting -= HandleQuitting;
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
            activeDuckingProfile = ResolveDuckingProfile();

            if (activePoolConfig != null)
                AudioPoolConfig.SetConfig(activePoolConfig);

            if (resolvedVoicePolicyProfile != null)
                AudioVoicePolicyProfile.SetConfig(resolvedVoicePolicyProfile);

            if (activePlatformProfile != null)
                AudioPlatformProfile.SetConfig(activePlatformProfile);

            if (activeDuckingProfile != null)
                AudioDuckingProfile.SetConfig(activeDuckingProfile);

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

        private AudioDuckingProfile ResolveDuckingProfile()
        {
            if (duckingProfileOverride != null)
                return duckingProfileOverride;

            return AudioDuckingProfile.FindConfig();
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
            AudioRuntimeThreadGuard.EnsureMainThread(nameof(ReloadPoolConfig));
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
            AudioRuntimeThreadGuard.EnsureMainThread(nameof(ReloadVoicePolicyProfile));
            if (Instance == null)
                return;

            AudioVoicePolicyProfile resolvedProfile = Instance.ResolveVoicePolicyProfile();
            AudioVoicePolicyProfile.SetConfig(resolvedProfile);
        }

        public static void ReloadPlatformProfile()
        {
            AudioRuntimeThreadGuard.EnsureMainThread(nameof(ReloadPlatformProfile));
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

        public static void ReloadDuckingProfile()
        {
            AudioRuntimeThreadGuard.EnsureMainThread(nameof(ReloadDuckingProfile));
            if (Instance == null)
            {
                activeDuckingProfile = AudioDuckingProfile.FindConfig();
                ResetDuckingRuntimeState();
                return;
            }

            activeDuckingProfile = Instance.ResolveDuckingProfile();
            if (activeDuckingProfile != null)
                AudioDuckingProfile.SetConfig(activeDuckingProfile);

            ResetDuckingRuntimeState();
            UpdateDucking(0f);
        }

        public static void ReloadRuntimeProfiles()
        {
            AudioRuntimeThreadGuard.EnsureMainThread(nameof(ReloadRuntimeProfiles));
            ReloadPoolConfig();
            ReloadVoicePolicyProfile();
            ReloadPlatformProfile();
            ReloadDuckingProfile();
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
                {
                    sourcePool.Remove(sourceToRemove);
                    currentPoolSize = sourcePool.Count;
                    continue;
                }

                sourcePool.Remove(sourceToRemove);
                Destroy(sourceToRemove.gameObject);
                currentPoolSize = sourcePool.Count;
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

        /// <summary>
        /// Ensures category counts are computed at most once per frame.
        /// Multiple voice-steal calls in the same frame reuse cached results.
        /// </summary>
        private static void EnsureCategoryCountsCached()
        {
            int frame = Time.frameCount;
            if (categoryCountsCacheFrame == frame) return;
            categoryCountsCacheFrame = frame;
            FillActiveSourceCountsByCategory(reusableCategorySourceCounts, reusableCategoryBudgetLoads);
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

        internal static Vector3 GetReferenceListenerPosition()
        {
            if (cachedAudioListener != null && cachedAudioListener.gameObject != null)
                return cachedAudioListener.transform.position;

            Camera mainCamera = GetMainCamera();
            if (mainCamera != null)
                return mainCamera.transform.position;

            return Vector3.zero;
        }

        /// <summary>Whether occlusion raycasts are enabled for the active platform profile.</summary>
        public static bool IsOcclusionEnabled
        {
            get
            {
                AudioRuntimeThreadGuard.EnsureMainThread(nameof(IsOcclusionEnabled));
                return activePlatformSettings.occlusion.enabled;
            }
        }

        /// <summary>Active occlusion settings for the current platform profile.</summary>
        internal static AudioPlatformProfile.OcclusionSettings ActiveOcclusionSettings => activePlatformSettings.occlusion;

        private static float GetVoiceProtectionScore(
            ActiveEvent evt,
            Dictionary<AudioEventCategory, int> categoryCounts,
            Dictionary<AudioEventCategory, float> weightedLoads)
        {
            if (evt == null || evt.rootEvent == null)
                return float.MaxValue;

            AudioEvent rootEvent = evt.rootEvent;
            TryGetPreparedAudioEventData(rootEvent, out PreparedAudioEventData preparedData);
            AudioEventCategory category = preparedData.EventId != 0 ? preparedData.Category : rootEvent.Category;
            AudioEventVoicePolicy policy = preparedData.EventId != 0 ? preparedData.VoicePolicy : rootEvent.GetResolvedVoicePolicy();
            AudioOutput output = preparedData.EventId != 0 ? preparedData.Output : rootEvent.Output;

            if (!policy.AllowVoiceSteal)
                return float.MaxValue;

            float score = GetCategoryProtectionWeight(category);
            score += rootEvent.Priority * 1.5f;
            score *= policy.StealResistance;

            if (output != null && output.loop)
                score += 20f;

            if (evt.IsPaused)
                score += 15f;

            if (policy.ProtectScheduledPlayback && evt.IsScheduledPlaybackPending)
                score += 40f;

            float age = Mathf.Max(0f, Time.time - evt.timeStarted);
            score -= Mathf.Min(age, 20f) * 1.5f;

            EventSource primarySource = evt.GetSource(0);
            if (policy.AllowDistanceBasedSteal && primarySource.IsValid)
            {
                AudioSource source = primarySource.source;
                float maxDistance = Mathf.Max(source.maxDistance, 0.0001f);
                float sqrDist = (source.transform.position - GetReferenceListenerPosition()).sqrMagnitude;
                float normalizedDistance = Mathf.Clamp01(Mathf.Sqrt(sqrDist) / maxDistance);
                score += (1f - normalizedDistance) * 35f;
                score += Mathf.Clamp01(source.volume) * 15f;
            }

            if (categoryCounts != null && categoryCounts.TryGetValue(category, out int activeSourceCount))
            {
                int budget = GetVoiceBudgetForCategory(category);
                if (activeSourceCount > budget)
                    score -= 25f;
            }

            if (weightedLoads != null && weightedLoads.TryGetValue(category, out float weightedLoad))
            {
                float weightedBudget = GetVoiceBudgetForCategory(category) * 1.15f;
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

            TryGetPreparedAudioEventData(requestingEvent, out PreparedAudioEventData preparedData);
            AudioEventCategory category = preparedData.EventId != 0 ? preparedData.Category : requestingEvent.Category;
            AudioEventVoicePolicy policy = preparedData.EventId != 0 ? preparedData.VoicePolicy : requestingEvent.GetResolvedVoicePolicy();

            float score = GetCategoryProtectionWeight(category);
            score += requestingEvent.Priority * 1.6f;
            score *= policy.StealResistance;

            if (categoryCounts != null && categoryCounts.TryGetValue(category, out int activeSourceCount))
            {
                int budget = GetVoiceBudgetForCategory(category);
                if (activeSourceCount < budget)
                    score += 20f;
                else
                    score -= Mathf.Min(activeSourceCount - budget, 4) * 8f;
            }

            if (weightedLoads != null && weightedLoads.TryGetValue(category, out float weightedLoad))
            {
                float weightedBudget = GetVoiceBudgetForCategory(category) * 1.15f;
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
        private static AudioSource TryStealSource(AudioEvent requestingEvent, ActiveEvent requestingActiveEvent)
        {
            if (ActiveEvents == null || ActiveEvents.Count == 0) return null;

            EnsureCategoryCountsCached();

            // Cache victim scan per frame so multiple steal attempts reuse the same result.
            int frame = Time.frameCount;
            if (cachedStealVictimFrame != frame)
            {
                cachedStealVictimFrame = frame;
                cachedStealVictim = null;
                cachedStealVictimScore = float.MaxValue;

                for (int i = 0; i < ActiveEvents.Count; i++)
                {
                    var evt = ActiveEvents[i];
                    if (evt == null || evt.status != EventStatus.Played) continue;
                    if (ReferenceEquals(evt, requestingActiveEvent)) continue;
                    if (evt.rootEvent == null) continue;
                    if (!HasStealableSource(evt)) continue;

                    float protectionScore = GetVoiceProtectionScore(evt, reusableCategorySourceCounts, reusableCategoryBudgetLoads);
                    if (protectionScore < cachedStealVictimScore)
                    {
                        cachedStealVictimScore = protectionScore;
                        cachedStealVictim = evt;
                    }
                }
            }

            ActiveEvent victim = cachedStealVictim;
            float lowestProtectionScore = cachedStealVictimScore;
            float requesterScore = GetRequesterProtectionScore(requestingEvent, reusableCategorySourceCounts, reusableCategoryBudgetLoads);

            if (victim == null || ReferenceEquals(victim, requestingActiveEvent) || victim.status != EventStatus.Played)
                return null;

            if (requestingEvent != null)
            {
                bool victimOverBudget = reusableCategorySourceCounts.TryGetValue(victim.rootEvent.Category, out int victimCategoryCount) &&
                    victimCategoryCount > GetVoiceBudgetForCategory(victim.rootEvent.Category);

                float requiredMargin = victimOverBudget ? -5f : 10f;
                if (requesterScore < lowestProtectionScore + requiredMargin)
                    return null;
            }

            if (victim != null)
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                string victimName = victim.name;
                AudioEventCategory victimCategory = victim.rootEvent != null ? victim.rootEvent.Category : AudioEventCategory.GameplaySFX;
#endif
                victim.StopImmediate();
                RemoveActiveEvent(victim);
                totalSteals++;
                // Invalidate victim cache since victim was consumed
                cachedStealVictimFrame = -1;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
                Debug.LogWarning($"AudioManager: Voice stolen from '{victimName}' (category:{victimCategory}, protection:{lowestProtectionScore:F1}, total steals: {totalSteals})");
#endif
                return availableSources.Count > 0 ? availableSources.Dequeue() : null;
            }

            return null;
        }

        private static bool HasStealableSource(ActiveEvent activeEvent)
        {
            if (activeEvent == null) return false;

            for (int i = 0; i < activeEvent.SourceCount; i++)
            {
                if (activeEvent.GetSource(i).IsValid)
                    return true;
            }

            return false;
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
            for (int i = 0; i < sourcePool.Count; i++)
            {
                AudioSource source = sourcePool[i];
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
            if (audioEvent == null) return 0;
            activeInstanceCounts.TryGetValue(audioEvent.GetInstanceID(), out int count);
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
            return GetUnusedSource(requestingEvent, null);
        }

        internal static AudioSource GetUnusedSource(AudioEvent requestingEvent, ActiveEvent requestingActiveEvent)
        {
            AudioRuntimeThreadGuard.EnsureMainThread(nameof(GetUnusedSource));
            int maxAttempts = availableSources.Count;
            int attempts = 0;

            while (availableSources.Count > 0 && attempts < maxAttempts)
            {
                AudioSource tempSource = availableSources.Dequeue();
                if (tempSource != null && tempSource.gameObject != null)
                    return tempSource;

                sourcePool.Remove(tempSource);
                currentPoolSize = sourcePool.Count;
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
            var stolenSource = TryStealSource(requestingEvent, requestingActiveEvent);
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

        internal static void ReturnSourceToPool(AudioSource source)
        {
            if (source == null || source.gameObject == null)
            {
                sourcePool.Remove(source);
                currentPoolSize = sourcePool.Count;
                return;
            }

            ResetAudioSource(source);
            availableSources.Enqueue(source);
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private static long CalculateAudioClipMemoryUsage(AudioClip clip)
        {
            if (clip == null) return 0;

            // Use Unity Profiler API for accurate runtime memory measurement.
            // This accounts for the actual load type and compression format:
            //   - Decompress On Load: full PCM in memory (samples x channels x bytesPerSample)
            //   - Compressed In Memory: compressed data only (much smaller than PCM)
            //   - Streaming: only a small I/O buffer (a few KB)
            // Fallback to PCM estimate (samples x channels x 2 bytes) if Profiler returns 0.
            long profilerSize = UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(clip);
            if (profilerSize > 0) return profilerSize;

            // Fallback: assume 16-bit PCM (worst-case estimate for Decompress On Load)
            return clip.samples * clip.channels * 2L;
        }

        private static void TrackMemory(ActiveEvent activeEvent, bool isAdding)
        {
            if (activeEvent == null) return;

            int uniqueClipCount = 0;
            int sourceCount = activeEvent.SourceCount;
            for (int i = 0; i < sourceCount; i++)
            {
                var es = activeEvent.GetSource(i);
                if (!es.IsValid || es.source.clip == null) continue;

                AudioClip clip = es.source.clip;
                bool exists = false;
                for (int j = 0; j < uniqueClipCount; j++)
                {
                    if (reusableTrackMemoryClips[j] == clip)
                    {
                        exists = true;
                        break;
                    }
                }

                if (!exists && uniqueClipCount < TrackMemoryClipScratchCapacity)
                {
                    reusableTrackMemoryClips[uniqueClipCount] = clip;
                    uniqueClipCount++;
                }
            }

            int direction = isAdding ? 1 : -1;
            for (int i = 0; i < uniqueClipCount; i++)
            {
                AudioClip clip = reusableTrackMemoryClips[i];
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

                reusableTrackMemoryClips[i] = null;
            }
        }
#endif

        internal static void TrackActiveEventMemory(ActiveEvent activeEvent)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (activeEvent == null || activeEvent.memoryTracked) return;
            TrackMemory(activeEvent, true);
            activeEvent.memoryTracked = true;
#endif
        }

        public static bool ValidateManager()
        {
            AudioRuntimeThreadGuard.EnsureMainThread(nameof(ValidateManager));
            if (isTearingDown) return false;
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
            int eventId = eventToPlay.GetInstanceID();
            if (eventsBeingUnloaded.Contains(eventId)) return false;
            if (!TryGetPreparedAudioEventData(eventToPlay, out PreparedAudioEventData preparedData) || !preparedData.Ready) return false;
            if (preparedData.InstanceLimit > 0 && CountActiveInstances(eventToPlay) >= preparedData.InstanceLimit) return false;
            if (!PassesAudibilityCulling(eventToPlay, preparedData.Output, emitterTransform, explicitPosition, isScheduledPlayback)) return false;
            if (!PassesRepeatTriggerThrottle(eventToPlay, preparedData.Category, emitterTransform, explicitPosition, isScheduledPlayback, out triggerKey)) return false;
            if (preparedData.Group == 0) return true;

            int group = preparedData.Group;
            if (!groupsBeingReplaced.Add(group)) return false;

            bool requiredBankOwnership = eventOwnerCounts.ContainsKey(eventId);
            try
            {
                StopGroupInstances(group);

                // StopImmediate invokes completion callbacks synchronously. A callback may unload
                // the candidate event or start another instance, so admission must be revalidated
                // while same-group re-entry remains blocked.
                triggerKey = default;
                if (isTearingDown || eventToPlay == null || eventsBeingUnloaded.Contains(eventId)) return false;
                if (requiredBankOwnership && !eventOwnerCounts.ContainsKey(eventId)) return false;
                if (!TryGetPreparedAudioEventData(eventToPlay, out preparedData) || !preparedData.Ready) return false;
                if (preparedData.Group != group) return false;
                if (preparedData.InstanceLimit > 0 && CountActiveInstances(eventToPlay) >= preparedData.InstanceLimit) return false;
                if (!PassesAudibilityCulling(eventToPlay, preparedData.Output, emitterTransform, explicitPosition, isScheduledPlayback)) return false;
                return PassesRepeatTriggerThrottle(
                    eventToPlay,
                    preparedData.Category,
                    emitterTransform,
                    explicitPosition,
                    isScheduledPlayback,
                    out triggerKey);
            }
            finally
            {
                groupsBeingReplaced.Remove(group);
            }
        }

        private static bool TryGetPreparedAudioEventData(AudioEvent eventToPlay, out PreparedAudioEventData preparedData)
        {
            preparedData = default;
            if (eventToPlay == null) return false;

            int eventId = eventToPlay.GetInstanceID();
            if (preparedAudioEvents.TryGetValue(eventId, out preparedData))
                return true;

            preparedData = PrepareAudioEventData(eventToPlay);
            preparedAudioEvents[eventId] = preparedData;
            return true;
        }

        private static PreparedAudioEventData PrepareAudioEventData(AudioEvent eventToPlay)
        {
            bool ready = eventToPlay != null && eventToPlay.ValidateAudioFiles();
            return new PreparedAudioEventData(eventToPlay, ready);
        }

        private static bool PassesRepeatTriggerThrottle(
            AudioEvent eventToPlay,
            AudioEventCategory eventCategory,
            Transform emitterTransform,
            Vector3? explicitPosition,
            bool isScheduledPlayback,
            out RepeatTriggerKey triggerKey)
        {
            triggerKey = default;
            float repeatWindow = activePlatformSettings.GetRepeatTriggerWindow(eventCategory);
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
            // Use GetEnumerator() manually to avoid struct-boxing allocation on Dictionary<K,V>
            var enumerator = recentTriggerTimes.GetEnumerator();
            while (enumerator.MoveNext())
            {
                var entry = enumerator.Current;
                if (entry.Value <= expiryThreshold)
                    reusableExpiredTriggerKeys.Add(entry.Key);
            }
            enumerator.Dispose();

            for (int i = 0; i < reusableExpiredTriggerKeys.Count; i++)
                recentTriggerTimes.Remove(reusableExpiredTriggerKeys[i]);
        }

        private static bool PassesAudibilityCulling(
            AudioEvent eventToPlay,
            AudioOutput output,
            Transform emitterTransform,
            Vector3? explicitPosition,
            bool isScheduledPlayback)
        {
            if (!activePlatformSettings.enableAudibilityCulling) return true;
            if (isScheduledPlayback && !activePlatformSettings.cullScheduledPlayback) return true;

            if (output == null) return true;
            if (output.loop && !activePlatformSettings.cullLoopingEvents) return true;

            if (output.EffectiveSpatialBlend <= 0.01f)
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
            float maxDistance = Mathf.Max(output.EffectiveMaxDistance, 0.01f);
            float paddedDistance = maxDistance + activePlatformSettings.distanceCullPadding;

            // Use squared distance for the common far-cull early-out (avoids sqrt)
            float sqrDistance = (listenerPosition - playbackPosition).sqrMagnitude;
            float sqrPaddedDistance = paddedDistance * paddedDistance;
            if (sqrDistance > sqrPaddedDistance)
            {
                totalAudibilityCulls++;
                return false;
            }

            // Only compute actual distance for fine-grained audibility estimation
            float distance = Mathf.Sqrt(sqrDistance);
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

            AnimationCurve curve = output.EffectiveAttenuationCurve;
            if (curve != null && curve.length > 0)
                return Mathf.Clamp01(curve.Evaluate(normalizedDistance));

            return 1f - normalizedDistance;
        }

        #endregion

        #region Bank Management

        private const int MaxBankEntriesPerCategory = 4096;

        /// <summary>
        /// Load AudioBank and register events for string-based lookup. Must be called on the Unity main thread.
        /// </summary>
        public static void LoadBank(AudioBank bank, bool overwriteExisting = false)
        {
            if (bank == null)
            {
                Debug.LogWarning("AudioManager: Attempted to load null AudioBank.");
                return;
            }

            AudioRuntimeThreadGuard.EnsureMainThread(nameof(LoadBank));

            if (isTearingDown || !ValidateManager()) return;

            if (!IsBankWithinRegistrationBudget(bank, out string budgetError))
            {
                Debug.LogError($"AudioManager: Cannot load bank '{bank.name}'. {budgetError}");
                return;
            }

            int bankId = bank.GetInstanceID();
            if (bankOperationsInProgress.Contains(bankId))
            {
                Debug.LogWarning($"AudioManager: Bank '{bank.name}' is already being modified.");
                return;
            }
            if (bankRegistrations.TryGetValue(bankId, out AudioBankRegistration existingRegistration))
            {
                if (existingRegistration.OverwriteExisting != overwriteExisting)
                {
                    Debug.LogWarning(
                        $"AudioManager: Bank '{bank.name}' is already loaded with a different overwrite policy. " +
                        "Unload it before changing the policy.");
                }
                return;
            }

            AudioBankRegistration registration = CreateBankRegistration(bank, overwriteExisting);
            bankRegistrations.Add(bankId, registration);
            loadedBanks[bankId] = bank;

            RegisterOwnedObjects(registration);
            AddNameContributions(registration.EventContributions, eventBindings, eventNameMap);
            AddNameContributions(registration.ParameterContributions, parameterBindings, parameterNameMap);
            AddNameContributions(registration.StateGroupContributions, stateGroupBindings, stateGroupNameMap);

            loadedBanksCacheDirty = true;
            globalParametersCacheDirty = true;
            ApplyAllStateMixProfiles();

            if (registration.OwnedEvents.Count == 0 &&
                registration.OwnedParameters.Count == 0 &&
                registration.OwnedSwitches.Count == 0 &&
                registration.OwnedStateGroups.Count == 0 &&
                registration.OwnedProfiles.Count == 0)
            {
                Debug.LogWarning($"AudioManager: AudioBank '{bank.name}' contains no runtime audio objects.");
            }
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            else
            {
                Debug.Log(
                    $"AudioManager: Loaded bank '{bank.name}' with {registration.OwnedEvents.Count} events, " +
                    $"{registration.OwnedParameters.Count} parameters, {registration.OwnedStateGroups.Count} state groups, " +
                    $"and {registration.OwnedProfiles.Count} state mix profiles.");
            }
#endif
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }

        private static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
        }

        private static bool IsBankWithinRegistrationBudget(AudioBank bank, out string error)
        {
            error = string.Empty;
            if ((bank.AudioEvents?.Count ?? 0) > MaxBankEntriesPerCategory)
                error = $"Event count exceeds {MaxBankEntriesPerCategory}.";
            else if ((bank.Parameters?.Count ?? 0) > MaxBankEntriesPerCategory)
                error = $"Parameter count exceeds {MaxBankEntriesPerCategory}.";
            else if ((bank.Switches?.Count ?? 0) > MaxBankEntriesPerCategory)
                error = $"Switch count exceeds {MaxBankEntriesPerCategory}.";
            else if ((bank.StateGroups?.Count ?? 0) > MaxBankEntriesPerCategory)
                error = $"State group count exceeds {MaxBankEntriesPerCategory}.";
            else if ((bank.StateMixProfiles?.Count ?? 0) > MaxBankEntriesPerCategory)
                error = $"State mix profile count exceeds {MaxBankEntriesPerCategory}.";

            return string.IsNullOrEmpty(error);
        }

        /// <summary>
        /// Callback invoked after runtime registrations and active playback owned by a bank are removed.
        /// Embedded clips remain serialized dependencies of the bank graph. External asset owners may
        /// use this notification as a coordination signal, but should prefer explicit clip leases.
        /// </summary>
        public static event Action<AudioBank> OnBankUnloaded;

        /// <summary>
        /// Unload an AudioBank: stops active events attributed to this bank, clears their AudioSource
        /// clip references, removes runtime name mappings, and fires <see cref="OnBankUnloaded"/>.
        /// </summary>
        public static void UnloadBank(AudioBank bank)
        {
            if (bank == null) return;

            AudioRuntimeThreadGuard.EnsureMainThread(nameof(UnloadBank));

            if (isTearingDown || !ValidateManager()) return;

            int bankId = bank.GetInstanceID();
            ReleasePreloadedBankClipLease(bankId);
            if (!bankRegistrations.TryGetValue(bankId, out AudioBankRegistration registration))
                return;

            if (!bankOperationsInProgress.Add(bankId)) return;

            var orphanedEvents = new List<AudioEvent>(registration.OwnedEvents.Count);
            try
            {
                bankRegistrations.Remove(bankId);
                loadedBanks.TryRemove(bankId, out _);

                RemoveNameContributions(registration.EventContributions, eventBindings, eventNameMap);
                RemoveNameContributions(registration.ParameterContributions, parameterBindings, parameterNameMap);
                RemoveNameContributions(registration.StateGroupContributions, stateGroupBindings, stateGroupNameMap);

                UnregisterOwnedObjects(registration, orphanedEvents);
                for (int i = 0; i < orphanedEvents.Count; i++)
                {
                    AudioEvent orphanedEvent = orphanedEvents[i];
                    if (orphanedEvent != null) eventsBeingUnloaded.Add(orphanedEvent.GetInstanceID());
                }

                if (orphanedEvents.Count > 0 && ActiveEvents != null && ActiveEvents.Count > 0)
                {
                    for (int i = ActiveEvents.Count - 1; i >= 0; i--)
                    {
                        if (i >= ActiveEvents.Count) continue;
                        ActiveEvent activeEvent = ActiveEvents[i];
                        if (activeEvent?.rootEvent == null || !orphanedEvents.Contains(activeEvent.rootEvent))
                            continue;

                        activeEvent.StopImmediate();
                    }
                }

                loadedBanksCacheDirty = true;
                globalParametersCacheDirty = true;
                ApplyAllStateMixProfiles();

#if UNITY_EDITOR || DEVELOPMENT_BUILD
                Debug.Log($"AudioManager: Unloaded bank '{bank.name}'.");
#endif

                NotifyBankUnloaded(registration.Bank);
            }
            finally
            {
                for (int i = 0; i < orphanedEvents.Count; i++)
                {
                    AudioEvent orphanedEvent = orphanedEvents[i];
                    if (orphanedEvent != null) eventsBeingUnloaded.Remove(orphanedEvent.GetInstanceID());
                }
                bankOperationsInProgress.Remove(bankId);
            }
        }

        private static AudioBankRegistration CreateBankRegistration(AudioBank bank, bool overwriteExisting)
        {
            var registration = new AudioBankRegistration(bank, overwriteExisting);
            var uniqueIds = new HashSet<int>();

            var eventNames = new Dictionary<string, AudioEvent>(StringComparer.Ordinal);
            List<AudioEvent> events = bank.AudioEvents;
            if (events != null)
            {
                for (int i = 0; i < events.Count; i++)
                {
                    AudioEvent audioEvent = events[i];
                    if (audioEvent == null) continue;
                    AddUniqueObject(registration.OwnedEvents, uniqueIds, audioEvent);
                    if (!string.IsNullOrEmpty(audioEvent.name) && !eventNames.ContainsKey(audioEvent.name))
                        eventNames.Add(audioEvent.name, audioEvent);
                }
            }

            foreach (var pair in eventNames)
            {
                registration.EventContributions.Add(
                    new BankNameContribution<AudioEvent>(registration.BankId, pair.Key, pair.Value, overwriteExisting));
            }

            uniqueIds.Clear();
            var parameterNames = new Dictionary<string, AudioParameter>(StringComparer.Ordinal);
            IReadOnlyList<AudioParameter> parameters = bank.Parameters;
            if (parameters != null)
            {
                for (int i = 0; i < parameters.Count; i++)
                {
                    AudioParameter parameter = parameters[i];
                    if (parameter == null) continue;
                    AddUniqueObject(registration.OwnedParameters, uniqueIds, parameter);
                    if (string.IsNullOrEmpty(parameter.name)) continue;
                    if (overwriteExisting || !parameterNames.ContainsKey(parameter.name))
                        parameterNames[parameter.name] = parameter;
                }
            }

            foreach (var pair in parameterNames)
            {
                registration.ParameterContributions.Add(
                    new BankNameContribution<AudioParameter>(registration.BankId, pair.Key, pair.Value, overwriteExisting));
            }

            uniqueIds.Clear();
            IReadOnlyList<AudioSwitch> switches = bank.Switches;
            if (switches != null)
            {
                for (int i = 0; i < switches.Count; i++)
                    AddUniqueObject(registration.OwnedSwitches, uniqueIds, switches[i]);
            }

            uniqueIds.Clear();
            var stateGroupNames = new Dictionary<string, AudioStateGroup>(StringComparer.Ordinal);
            IReadOnlyList<AudioStateGroup> stateGroups = bank.StateGroups;
            if (stateGroups != null)
            {
                for (int i = 0; i < stateGroups.Count; i++)
                {
                    AudioStateGroup stateGroup = stateGroups[i];
                    if (stateGroup == null) continue;
                    AddUniqueObject(registration.OwnedStateGroups, uniqueIds, stateGroup);
                    if (string.IsNullOrEmpty(stateGroup.name)) continue;
                    if (overwriteExisting || !stateGroupNames.ContainsKey(stateGroup.name))
                        stateGroupNames[stateGroup.name] = stateGroup;
                }
            }

            foreach (var pair in stateGroupNames)
            {
                registration.StateGroupContributions.Add(
                    new BankNameContribution<AudioStateGroup>(registration.BankId, pair.Key, pair.Value, overwriteExisting));
            }

            uniqueIds.Clear();
            IReadOnlyList<AudioStateMixProfile> profiles = bank.StateMixProfiles;
            if (profiles != null)
            {
                for (int i = 0; i < profiles.Count; i++)
                    AddUniqueObject(registration.OwnedProfiles, uniqueIds, profiles[i]);
            }

            return registration;
        }

        private static void AddUniqueObject<T>(List<T> destination, HashSet<int> uniqueIds, T value)
            where T : UnityEngine.Object
        {
            if (value != null && uniqueIds.Add(value.GetInstanceID()))
                destination.Add(value);
        }

        private static void RegisterOwnedObjects(AudioBankRegistration registration)
        {
            for (int i = 0; i < registration.OwnedEvents.Count; i++)
            {
                AudioEvent audioEvent = registration.OwnedEvents[i];
                if (IncrementOwnerCount(eventOwnerCounts, audioEvent.GetInstanceID()))
                    preparedAudioEvents[audioEvent.GetInstanceID()] = PrepareAudioEventData(audioEvent);
            }

            for (int i = 0; i < registration.OwnedParameters.Count; i++)
            {
                AudioParameter parameter = registration.OwnedParameters[i];
                if (IncrementOwnerCount(parameterOwnerCounts, parameter.GetInstanceID()))
                    parameter.ResetParameter();
            }

            for (int i = 0; i < registration.OwnedSwitches.Count; i++)
            {
                AudioSwitch audioSwitch = registration.OwnedSwitches[i];
                if (IncrementOwnerCount(switchOwnerCounts, audioSwitch.GetInstanceID()))
                    audioSwitch.ResetSwitch();
            }

            for (int i = 0; i < registration.OwnedStateGroups.Count; i++)
            {
                AudioStateGroup stateGroup = registration.OwnedStateGroups[i];
                if (IncrementOwnerCount(stateGroupOwnerCounts, stateGroup.GetInstanceID()))
                    stateGroup.ResetStateGroup();
            }

            for (int i = 0; i < registration.OwnedProfiles.Count; i++)
            {
                AudioStateMixProfile profile = registration.OwnedProfiles[i];
                if (IncrementOwnerCount(profileOwnerCounts, profile.GetInstanceID()))
                    registeredStateMixProfiles.Add(profile);
            }
        }

        private static void UnregisterOwnedObjects(
            AudioBankRegistration registration,
            List<AudioEvent> orphanedEvents)
        {
            for (int i = 0; i < registration.OwnedEvents.Count; i++)
            {
                AudioEvent audioEvent = registration.OwnedEvents[i];
                if (!DecrementOwnerCount(eventOwnerCounts, audioEvent.GetInstanceID())) continue;
                preparedAudioEvents.Remove(audioEvent.GetInstanceID());
                orphanedEvents.Add(audioEvent);
            }

            for (int i = 0; i < registration.OwnedParameters.Count; i++)
            {
                AudioParameter parameter = registration.OwnedParameters[i];
                if (!DecrementOwnerCount(parameterOwnerCounts, parameter.GetInstanceID())) continue;
                RemoveScopedParameterOverrides(parameter.GetInstanceID());
                parameter.ResetParameter();
            }

            for (int i = 0; i < registration.OwnedSwitches.Count; i++)
            {
                AudioSwitch audioSwitch = registration.OwnedSwitches[i];
                if (DecrementOwnerCount(switchOwnerCounts, audioSwitch.GetInstanceID()))
                    audioSwitch.ResetSwitch();
            }

            for (int i = 0; i < registration.OwnedStateGroups.Count; i++)
            {
                AudioStateGroup stateGroup = registration.OwnedStateGroups[i];
                if (DecrementOwnerCount(stateGroupOwnerCounts, stateGroup.GetInstanceID()))
                    stateGroup.ResetStateGroup();
            }

            for (int i = 0; i < registration.OwnedProfiles.Count; i++)
            {
                AudioStateMixProfile profile = registration.OwnedProfiles[i];
                if (DecrementOwnerCount(profileOwnerCounts, profile.GetInstanceID()))
                    registeredStateMixProfiles.Remove(profile);
            }
        }

        private static bool IncrementOwnerCount(Dictionary<int, int> ownerCounts, int instanceId)
        {
            if (ownerCounts.TryGetValue(instanceId, out int count))
            {
                ownerCounts[instanceId] = count + 1;
                return false;
            }

            ownerCounts.Add(instanceId, 1);
            return true;
        }

        private static bool DecrementOwnerCount(Dictionary<int, int> ownerCounts, int instanceId)
        {
            if (!ownerCounts.TryGetValue(instanceId, out int count))
                return false;

            if (count > 1)
            {
                ownerCounts[instanceId] = count - 1;
                return false;
            }

            ownerCounts.Remove(instanceId);
            return true;
        }

        private static void AddNameContributions<T>(
            List<BankNameContribution<T>> contributions,
            Dictionary<string, List<BankNameContribution<T>>> bindings,
            ConcurrentDictionary<string, T> effectiveMap)
            where T : UnityEngine.Object
        {
            for (int i = 0; i < contributions.Count; i++)
            {
                BankNameContribution<T> contribution = contributions[i];
                if (!bindings.TryGetValue(contribution.Name, out List<BankNameContribution<T>> list))
                {
                    list = new List<BankNameContribution<T>>(2);
                    bindings.Add(contribution.Name, list);
                }

                list.Add(contribution);
                RecomputeNameBinding(contribution.Name, list, effectiveMap);
            }
        }

        private static void RemoveNameContributions<T>(
            List<BankNameContribution<T>> contributions,
            Dictionary<string, List<BankNameContribution<T>>> bindings,
            ConcurrentDictionary<string, T> effectiveMap)
            where T : UnityEngine.Object
        {
            for (int i = 0; i < contributions.Count; i++)
            {
                BankNameContribution<T> contribution = contributions[i];
                if (!bindings.TryGetValue(contribution.Name, out List<BankNameContribution<T>> list))
                    continue;

                for (int entryIndex = list.Count - 1; entryIndex >= 0; entryIndex--)
                {
                    if (list[entryIndex].OwnerBankId == contribution.OwnerBankId)
                        list.RemoveAt(entryIndex);
                }

                if (list.Count == 0)
                {
                    bindings.Remove(contribution.Name);
                    effectiveMap.TryRemove(contribution.Name, out _);
                }
                else
                {
                    RecomputeNameBinding(contribution.Name, list, effectiveMap);
                }
            }
        }

        private static void RecomputeNameBinding<T>(
            string name,
            List<BankNameContribution<T>> contributions,
            ConcurrentDictionary<string, T> effectiveMap)
            where T : UnityEngine.Object
        {
            T effectiveValue = null;
            bool hasValue = false;
            for (int i = 0; i < contributions.Count; i++)
            {
                BankNameContribution<T> contribution = contributions[i];
                if (contribution.Value == null) continue;

                if (!hasValue)
                {
                    effectiveValue = contribution.Value;
                    hasValue = true;
                }
                else if (contribution.CanOverride)
                {
                    effectiveValue = contribution.Value;
                }
            }

            if (hasValue)
                effectiveMap[name] = effectiveValue;
            else
                effectiveMap.TryRemove(name, out _);
        }

        private static void NotifyBankUnloaded(AudioBank bank)
        {
            if (bank == null) return;

            Action<AudioBank> callbacks = OnBankUnloaded;
            if (callbacks == null) return;

            Delegate[] invocationList = callbacks.GetInvocationList();
            for (int i = 0; i < invocationList.Length; i++)
            {
                try
                {
                    ((Action<AudioBank>)invocationList[i])(bank);
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception);
                }
            }
        }

        private static void ApplyStateMixProfiles(AudioStateGroup changedGroup)
        {
            for (int i = 0; i < registeredStateMixProfiles.Count; i++)
            {
                AudioStateMixProfile profile = registeredStateMixProfiles[i];
                if (profile != null)
                {
                    profile.Apply(changedGroup);
                }
            }
        }

        private static void ApplyAllStateMixProfiles()
        {
            ApplyStateMixProfiles(null);
        }

        private static void RemoveScopedParameterOverrides(int parameterId)
        {
            reusableScopedParameterRemovalKeys.Clear();

            var enumerator = scopedParameterOverrides.GetEnumerator();
            while (enumerator.MoveNext())
            {
                AudioParameterScopeKey key = enumerator.Current.Key;
                if (key.ParameterId == parameterId)
                    reusableScopedParameterRemovalKeys.Add(key);
            }
            enumerator.Dispose();

            for (int i = 0; i < reusableScopedParameterRemovalKeys.Count; i++)
            {
                scopedParameterOverrides.Remove(reusableScopedParameterRemovalKeys[i]);
            }

            reusableScopedParameterRemovalKeys.Clear();
        }

        private static int RemoveScopedParameterOverridesForScope(int scopeId)
        {
            reusableScopedParameterRemovalKeys.Clear();

            var enumerator = scopedParameterOverrides.GetEnumerator();
            while (enumerator.MoveNext())
            {
                AudioParameterScopeKey key = enumerator.Current.Key;
                if (key.ScopeId == scopeId)
                    reusableScopedParameterRemovalKeys.Add(key);
            }
            enumerator.Dispose();

            int removedCount = reusableScopedParameterRemovalKeys.Count;
            for (int i = 0; i < removedCount; i++)
            {
                scopedParameterOverrides.Remove(reusableScopedParameterRemovalKeys[i]);
            }

            reusableScopedParameterRemovalKeys.Clear();
            return removedCount;
        }

        private static void RemoveScopedParameterOwnerIfUnused(int scopeId)
        {
            var enumerator = scopedParameterOverrides.GetEnumerator();
            while (enumerator.MoveNext())
            {
                if (enumerator.Current.Key.ScopeId == scopeId)
                {
                    enumerator.Dispose();
                    return;
                }
            }
            enumerator.Dispose();
            scopedParameterOwners.Remove(scopeId);
        }

        public static AudioEvent GetEventByName(string eventName)
        {
            AudioRuntimeThreadGuard.EnsureMainThread(nameof(GetEventByName));
            if (string.IsNullOrEmpty(eventName)) return null;
            eventNameMap.TryGetValue(eventName, out AudioEvent result);
            return result;
        }

        public static AudioParameter GetParameterByName(string parameterName)
        {
            AudioRuntimeThreadGuard.EnsureMainThread(nameof(GetParameterByName));
            if (string.IsNullOrEmpty(parameterName)) return null;
            parameterNameMap.TryGetValue(parameterName, out AudioParameter result);
            return result;
        }

        public static void ClearEventNameMap()
        {
            AudioRuntimeThreadGuard.EnsureMainThread(nameof(ClearEventNameMap));

            int count = eventNameMap.Count;
            eventNameMap.Clear();
            eventBindings.Clear();
            if (count > 0)
                Debug.Log($"AudioManager: Cleared {count} events from name map.");
        }

        public static int GetRegisteredEventCount()
        {
            AudioRuntimeThreadGuard.EnsureMainThread(nameof(GetRegisteredEventCount));
            return eventNameMap.Count;
        }

        public static Dictionary<string, List<AudioEvent>> ValidateBankForDuplicateNames(AudioBank bank)
        {
            AudioRuntimeThreadGuard.EnsureMainThread(nameof(ValidateBankForDuplicateNames));
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

            var nameGroupsEnum = nameGroups.GetEnumerator();
            while (nameGroupsEnum.MoveNext())
            {
                if (nameGroupsEnum.Current.Value.Count > 1)
                    duplicates[nameGroupsEnum.Current.Key] = nameGroupsEnum.Current.Value;
            }
            nameGroupsEnum.Dispose();

            return duplicates;
        }

        internal static int CountDuplicateEventNames(AudioBank bank, Dictionary<string, int> reusableNameCounts)
        {
            if (reusableNameCounts == null)
            {
                throw new ArgumentNullException(nameof(reusableNameCounts));
            }

            reusableNameCounts.Clear();
            if (bank?.AudioEvents == null)
            {
                return 0;
            }

            int duplicateNameCount = 0;
            int eventCount = bank.AudioEvents.Count;
            for (int i = 0; i < eventCount; i++)
            {
                AudioEvent audioEvent = bank.AudioEvents[i];
                if (audioEvent == null || string.IsNullOrEmpty(audioEvent.name))
                {
                    continue;
                }

                if (!reusableNameCounts.TryGetValue(audioEvent.name, out int count))
                {
                    reusableNameCounts.Add(audioEvent.name, 1);
                    continue;
                }

                count++;
                reusableNameCounts[audioEvent.name] = count;
                if (count == 2)
                {
                    duplicateNameCount++;
                }
            }

            return duplicateNameCount;
        }

        // Cached bank list for GetLoadedBanks() - avoids per-call allocation
        private static readonly List<AudioBank> cachedLoadedBanksList = new List<AudioBank>(16);
        private static bool loadedBanksCacheDirty = true;
        private static System.Collections.ObjectModel.ReadOnlyCollection<AudioBank> cachedLoadedBanksReadOnly;

        public static IReadOnlyCollection<AudioBank> GetLoadedBanks()
        {
            AudioRuntimeThreadGuard.EnsureMainThread(nameof(GetLoadedBanks));
            if (loadedBanksCacheDirty)
            {
                cachedLoadedBanksList.Clear();
                var enumerator = loadedBanks.GetEnumerator();
                while (enumerator.MoveNext())
                    cachedLoadedBanksList.Add(enumerator.Current.Value);
                enumerator.Dispose();
                cachedLoadedBanksReadOnly = cachedLoadedBanksList.AsReadOnly();
                loadedBanksCacheDirty = false;
            }
            return cachedLoadedBanksReadOnly;
        }

        public static int GetLoadedBankCount()
        {
            AudioRuntimeThreadGuard.EnsureMainThread(nameof(GetLoadedBankCount));
            return loadedBanks.Count;
        }

        private static void RefreshLoadedBankRuntimeState()
        {
            var visitedIds = new HashSet<int>();
            foreach (var pair in bankRegistrations)
            {
                AudioBankRegistration registration = pair.Value;
                for (int i = 0; i < registration.OwnedParameters.Count; i++)
                {
                    AudioParameter parameter = registration.OwnedParameters[i];
                    if (parameter != null && visitedIds.Add(parameter.GetInstanceID()))
                        parameter.ResetParameter();
                }
            }

            visitedIds.Clear();
            foreach (var pair in bankRegistrations)
            {
                AudioBankRegistration registration = pair.Value;
                for (int i = 0; i < registration.OwnedSwitches.Count; i++)
                {
                    AudioSwitch audioSwitch = registration.OwnedSwitches[i];
                    if (audioSwitch != null && visitedIds.Add(audioSwitch.GetInstanceID()))
                        audioSwitch.ResetSwitch();
                }
            }

            visitedIds.Clear();
            foreach (var pair in bankRegistrations)
            {
                AudioBankRegistration registration = pair.Value;
                for (int i = 0; i < registration.OwnedStateGroups.Count; i++)
                {
                    AudioStateGroup stateGroup = registration.OwnedStateGroups[i];
                    if (stateGroup != null && visitedIds.Add(stateGroup.GetInstanceID()))
                        stateGroup.ResetStateGroup();
                }
            }

            ApplyAllStateMixProfiles();
        }

        private static void UpdateGlobalParameters(float deltaTime)
        {
            CleanupDestroyedScopedParameterOwners();

            if (globalParametersCacheDirty)
            {
                RebuildGlobalParametersCache();
            }

            for (int i = 0; i < reusableGlobalParameters.Count; i++)
            {
                reusableGlobalParameters[i].UpdateInterpolation(deltaTime);
            }

            var scopedEnumerator = scopedParameterOverrides.GetEnumerator();
            while (scopedEnumerator.MoveNext())
            {
                scopedEnumerator.Current.Value.Update(deltaTime);
            }
            scopedEnumerator.Dispose();
        }

        private static void CleanupDestroyedScopedParameterOwners()
        {
            int frame = Time.frameCount;
            if (lastScopedParameterOwnerCleanupFrame >= 0
                && frame - lastScopedParameterOwnerCleanupFrame < 120)
            {
                return;
            }

            lastScopedParameterOwnerCleanupFrame = frame;
            reusableDeadParameterScopeIds.Clear();

            var ownerEnumerator = scopedParameterOwners.GetEnumerator();
            while (ownerEnumerator.MoveNext())
            {
                KeyValuePair<int, WeakReference<GameObject>> owner = ownerEnumerator.Current;
                if (!owner.Value.TryGetTarget(out GameObject emitterObject) || emitterObject == null)
                {
                    reusableDeadParameterScopeIds.Add(owner.Key);
                }
            }
            ownerEnumerator.Dispose();

            if (reusableDeadParameterScopeIds.Count == 0)
            {
                return;
            }

            reusableScopedParameterRemovalKeys.Clear();
            var overrideEnumerator = scopedParameterOverrides.GetEnumerator();
            while (overrideEnumerator.MoveNext())
            {
                AudioParameterScopeKey key = overrideEnumerator.Current.Key;
                if (reusableDeadParameterScopeIds.Contains(key.ScopeId))
                {
                    reusableScopedParameterRemovalKeys.Add(key);
                }
            }
            overrideEnumerator.Dispose();

            for (int i = 0; i < reusableScopedParameterRemovalKeys.Count; i++)
            {
                scopedParameterOverrides.Remove(reusableScopedParameterRemovalKeys[i]);
            }

            foreach (int scopeId in reusableDeadParameterScopeIds)
            {
                scopedParameterOwners.Remove(scopeId);
            }

            reusableScopedParameterRemovalKeys.Clear();
            reusableDeadParameterScopeIds.Clear();
        }

        private static void UpdateDucking(float deltaTime)
        {
            AudioDuckingProfile profile = activeDuckingProfile;
            AudioMixer mixer = staticMainMixer;
            if (profile == null || mixer == null)
            {
                activeDuckingRuleCount = 0;
                return;
            }

            int ruleCount = profile.RuleCount;
            EnsureDuckingRuntimeCapacity(ruleCount);

            int activeRuleCount = 0;
            for (int i = 0; i < ruleCount; i++)
            {
                AudioDuckingRule rule = profile.GetRule(i);
                ref DuckingRuntimeState state = ref duckingRuntimeStates[i];
                if (!rule.enabled || string.IsNullOrEmpty(rule.targetMixerParameter))
                {
                    state.Active = false;
                    continue;
                }

                int categoryIndex = GetCategoryIndex(rule.triggerCategory);
                bool active = activeEventCategoryCounts[categoryIndex] >= Mathf.Max(1, rule.minActiveEvents);
                float targetValue = active ? rule.duckedValueDb : rule.normalValueDb;
                float transitionTime = active ? rule.attackTime : rule.releaseTime;
                if (active) activeRuleCount++;

                if (!state.Initialized)
                {
                    state.Initialized = true;
                    state.CurrentValue = targetValue;
                    state.Active = active;
                    mixer.SetFloat(rule.targetMixerParameter, targetValue);
                    continue;
                }

                state.Active = active;
                float currentValue = state.CurrentValue;
                if (Mathf.Approximately(currentValue, targetValue)) continue;

                float nextValue;
                if (transitionTime <= 0f || deltaTime <= 0f)
                {
                    nextValue = targetValue;
                }
                else
                {
                    float speed = Mathf.Abs(targetValue - currentValue) / transitionTime;
                    nextValue = Mathf.MoveTowards(currentValue, targetValue, speed * deltaTime);
                }

                state.CurrentValue = nextValue;
                mixer.SetFloat(rule.targetMixerParameter, nextValue);
            }

            activeDuckingRuleCount = activeRuleCount;
        }

        private static void EnsureDuckingRuntimeCapacity(int ruleCount)
        {
            if (ruleCount <= duckingRuntimeStates.Length) return;

            int capacity = duckingRuntimeStates.Length > 0 ? duckingRuntimeStates.Length : 8;
            while (capacity < ruleCount)
            {
                capacity *= 2;
            }

            Array.Resize(ref duckingRuntimeStates, capacity);
        }

        private static void ResetDuckingRuntimeState()
        {
            for (int i = 0; i < duckingRuntimeStates.Length; i++)
            {
                duckingRuntimeStates[i] = default;
            }

            activeDuckingRuleCount = 0;
        }

        private static void RebuildGlobalParametersCache()
        {
            reusableGlobalParameters.Clear();
            var visitedIds = new HashSet<int>();
            foreach (var pair in bankRegistrations)
            {
                List<AudioParameter> parameters = pair.Value.OwnedParameters;
                for (int i = 0; i < parameters.Count; i++)
                {
                    AudioParameter parameter = parameters[i];
                    if (parameter != null && visitedIds.Add(parameter.GetInstanceID()))
                    {
                        reusableGlobalParameters.Add(parameter);
                    }
                }
            }

            globalParametersCacheDirty = false;
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

            AudioLowPassFilter lowPassFilter = source.GetComponent<AudioLowPassFilter>();
            if (lowPassFilter != null)
            {
                lowPassFilter.cutoffFrequency = 22000f;
                lowPassFilter.lowpassResonanceQ = 1f;
                lowPassFilter.enabled = false;
            }
        }

        #endregion

        #region Clip Preload & Memory Budget

        private const int MaxExternalClipReferencesPerBank = 1024;
        private const ulong DefaultExternalClipMaxDownloadBytes = 64UL * 1024UL * 1024UL;
        private const long DefaultExternalClipMaxDecodedBytes = 256L * 1024L * 1024L;
        private const long DefaultBankClipLeaseMaxDecodedBytes = 512L * 1024L * 1024L;
        private const long DefaultActiveBankClipLeaseMemoryBudgetBytes = 1024L * 1024L * 1024L;
        private const int DefaultExternalClipRequestTimeoutSeconds = 30;
        private const float DefaultExternalClipIdleTtlSeconds = 30f;
        private static long externalClipMemoryBudgetBytes;
        private static ulong externalClipMaxDownloadBytes = DefaultExternalClipMaxDownloadBytes;
        private static long externalClipMaxDecodedBytes = DefaultExternalClipMaxDecodedBytes;
        private static int externalClipRequestTimeoutSeconds = DefaultExternalClipRequestTimeoutSeconds;
        private static float externalClipIdleTtl = DefaultExternalClipIdleTtlSeconds;
        private static long bankClipLeaseMaxDecodedBytes = DefaultBankClipLeaseMaxDecodedBytes;
        private static long activeBankClipLeaseMemoryBudgetBytes = DefaultActiveBankClipLeaseMemoryBudgetBytes;
        private static long activeBankClipLeaseMemoryBytes;

        /// <summary>
        /// Memory budget in bytes for retaining unused externally-loaded clips.
        /// A value of 0 disables unused cache residency; active leases are never evicted.
        /// </summary>
        public static long ExternalClipMemoryBudgetBytes
        {
            get
            {
                AudioRuntimeThreadGuard.EnsureMainThread(nameof(ExternalClipMemoryBudgetBytes));
                return externalClipMemoryBudgetBytes;
            }
            set
            {
                AudioRuntimeThreadGuard.EnsureMainThread(nameof(ExternalClipMemoryBudgetBytes));
                externalClipMemoryBudgetBytes = Math.Max(0L, value);
            }
        }

        /// <summary>Maximum encoded bytes accepted by the built-in external loader per clip.</summary>
        public static ulong ExternalClipMaxDownloadBytes
        {
            get
            {
                AudioRuntimeThreadGuard.EnsureMainThread(nameof(ExternalClipMaxDownloadBytes));
                return externalClipMaxDownloadBytes;
            }
            set
            {
                AudioRuntimeThreadGuard.EnsureMainThread(nameof(ExternalClipMaxDownloadBytes));
                externalClipMaxDownloadBytes = value;
            }
        }

        /// <summary>Maximum estimated decoded PCM bytes accepted per externally loaded clip.</summary>
        public static long ExternalClipMaxDecodedBytes
        {
            get
            {
                AudioRuntimeThreadGuard.EnsureMainThread(nameof(ExternalClipMaxDecodedBytes));
                return externalClipMaxDecodedBytes;
            }
            set
            {
                AudioRuntimeThreadGuard.EnsureMainThread(nameof(ExternalClipMaxDecodedBytes));
                externalClipMaxDecodedBytes = Math.Max(0L, value);
            }
        }

        /// <summary>Timeout in seconds for built-in external clip requests.</summary>
        public static int ExternalClipRequestTimeoutSeconds
        {
            get
            {
                AudioRuntimeThreadGuard.EnsureMainThread(nameof(ExternalClipRequestTimeoutSeconds));
                return externalClipRequestTimeoutSeconds;
            }
            set
            {
                AudioRuntimeThreadGuard.EnsureMainThread(nameof(ExternalClipRequestTimeoutSeconds));
                externalClipRequestTimeoutSeconds = Math.Max(1, value);
            }
        }

        /// <summary>
        /// Idle TTL (seconds) for unused external clips before eviction. Default 30s.
        /// </summary>
        public static float ExternalClipIdleTTL
        {
            get
            {
                AudioRuntimeThreadGuard.EnsureMainThread(nameof(ExternalClipIdleTTL));
                return externalClipIdleTtl;
            }
            set
            {
                AudioRuntimeThreadGuard.EnsureMainThread(nameof(ExternalClipIdleTTL));
                externalClipIdleTtl = float.IsNaN(value) || float.IsInfinity(value) ? 0f : Mathf.Max(0f, value);
            }
        }

        /// <summary>Maximum estimated decoded bytes retained by one bank clip lease.</summary>
        public static long BankClipLeaseMaxDecodedBytes
        {
            get
            {
                AudioRuntimeThreadGuard.EnsureMainThread(nameof(BankClipLeaseMaxDecodedBytes));
                return bankClipLeaseMaxDecodedBytes;
            }
            set
            {
                AudioRuntimeThreadGuard.EnsureMainThread(nameof(BankClipLeaseMaxDecodedBytes));
                bankClipLeaseMaxDecodedBytes = Math.Max(0L, value);
            }
        }

        /// <summary>Maximum estimated decoded bytes reserved across all active bank clip leases.</summary>
        public static long ActiveBankClipLeaseMemoryBudgetBytes
        {
            get
            {
                AudioRuntimeThreadGuard.EnsureMainThread(nameof(ActiveBankClipLeaseMemoryBudgetBytes));
                return activeBankClipLeaseMemoryBudgetBytes;
            }
            set
            {
                AudioRuntimeThreadGuard.EnsureMainThread(nameof(ActiveBankClipLeaseMemoryBudgetBytes));
                activeBankClipLeaseMemoryBudgetBytes = Math.Max(0L, value);
            }
        }

        public static long ActiveBankClipLeaseMemoryBytes
        {
            get
            {
                AudioRuntimeThreadGuard.EnsureMainThread(nameof(ActiveBankClipLeaseMemoryBytes));
                return activeBankClipLeaseMemoryBytes;
            }
        }

        private static float lastEvictionCheckTime;

        /// <summary>
        /// Preloads and retains all AudioClipReference-based external clips from a bank's
        /// events. The manager-owned residency lease remains active until the bank is
        /// unloaded, <see cref="ReleasePreloadedBankClips"/> is called, or the manager shuts down.
        /// </summary>
        /// <param name="bank">The bank whose events' external clips should be preloaded.</param>
        /// <param name="cancellationToken">Token to cancel the preload operation.</param>
        /// <returns>Number of clips successfully preloaded.</returns>
        public static async UniTask<int> PreloadBankClipsAsync(AudioBank bank, CancellationToken cancellationToken = default)
        {
            AudioRuntimeThreadGuard.EnsureMainThread(nameof(PreloadBankClipsAsync));
            if (bank == null || isTearingDown) return 0;

            int bankId = bank.GetInstanceID();
            CancellationTokenSource linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            CancellationToken linkedToken = linkedCancellation.Token;
            var request = new PreloadBankRequest(
                GetNextPreloadRequestId(),
                managerLifecycleGeneration,
                linkedCancellation);
            preloadBankMutationVersions[bankId] = request.RequestId;
            preloadBankRequests.TryGetValue(bankId, out PreloadBankRequest previousRequest);
            if (previousRequest != null)
                preloadBankRequests.Remove(bankId);

            IAudioBankClipLease lease = null;
            try
            {
                // Cancellation callbacks are synchronous and may re-enter preload/release/unload.
                // Publish this request only if no later mutation won during that callback.
                if (previousRequest != null)
                    SafeCancelCancellationSource(previousRequest.Cancellation);
                if (!IsCurrentPreloadMutation(bankId, request))
                    return 0;

                linkedToken.ThrowIfCancellationRequested();
                preloadBankRequests[bankId] = request;
                if (preloadedBankClipLeases.TryGetValue(bankId, out IAudioBankClipLease previousLease))
                {
                    // Release the previous reservation before acquiring its replacement. Keeping
                    // both would double-count shared clips and can reject a same-bank refresh at
                    // an otherwise sufficient aggregate budget.
                    preloadedBankClipLeases.Remove(bankId);
                    SafeDisposeBankClipLease(previousLease);
                    linkedToken.ThrowIfCancellationRequested();
                    if (!IsCurrentPreloadMutation(bankId, request))
                        return 0;
                }

                lease = await AcquireBankClipLeaseAsync(bank, linkedToken);
                AudioRuntimeThreadGuard.EnsureMainThread(nameof(PreloadBankClipsAsync) + " continuation");
                linkedToken.ThrowIfCancellationRequested();

                if (!IsCurrentPreloadMutation(bankId, request) ||
                    !preloadBankRequests.TryGetValue(bankId, out PreloadBankRequest currentRequest) ||
                    !ReferenceEquals(currentRequest, request))
                {
                    return 0;
                }

                preloadBankRequests.Remove(bankId);
                int loadedCount = lease != null ? lease.LoadedCount : 0;
                preloadedBankClipLeases[bankId] = lease;
                lease = null;
                return loadedCount;
            }
            finally
            {
                if (preloadBankRequests.TryGetValue(bankId, out PreloadBankRequest currentRequest) &&
                    ReferenceEquals(currentRequest, request))
                {
                    preloadBankRequests.Remove(bankId);
                }

                SafeDisposeBankClipLease(lease);
                SafeDisposeCancellationSource(linkedCancellation);
                RemoveCompletedPreloadMutation(bankId, request.RequestId);
            }
        }

        /// <summary>Releases the manager-owned residency lease created by <see cref="PreloadBankClipsAsync"/>.</summary>
        public static bool ReleasePreloadedBankClips(AudioBank bank)
        {
            AudioRuntimeThreadGuard.EnsureMainThread(nameof(ReleasePreloadedBankClips));
            return bank != null && ReleasePreloadedBankClipLease(bank.GetInstanceID());
        }

        /// <summary>
        /// Loads and retains all external clips referenced by a bank. The returned lease owns
        /// every successful clip handle and must be disposed on the Unity main thread.
        /// </summary>
        public static async UniTask<IAudioBankClipLease> AcquireBankClipLeaseAsync(
            AudioBank bank,
            CancellationToken cancellationToken = default)
        {
            AudioRuntimeThreadGuard.EnsureMainThread(nameof(AcquireBankClipLeaseAsync));
            int lifecycleGeneration = managerLifecycleGeneration;
            if (isTearingDown)
                throw new InvalidOperationException("Audio clip leases cannot be acquired while AudioManager is shutting down.");
            if (bank == null || bank.AudioEvents == null)
                return new AudioBankClipLease(bank, 0, Array.Empty<IAudioClipHandle>(), 0, 0L, lifecycleGeneration);

            using var references = new PooledAudioClipReferenceSet();

            int eventCount = bank.AudioEvents.Count;
            for (int i = 0; i < eventCount; i++)
            {
                var audioEvent = bank.AudioEvents[i];
                if (audioEvent == null) continue;

                if (!CollectExternalClipReferences(audioEvent, references))
                {
                    throw new InvalidOperationException(
                        $"AudioBank '{bank.name}' exceeds the {MaxExternalClipReferencesPerBank}-clip preload limit.");
                }
            }

            if (references.Count == 0)
                return new AudioBankClipLease(bank, 0, Array.Empty<IAudioClipHandle>(), 0, 0L, lifecycleGeneration);

            var retainedHandles = new List<IAudioClipHandle>(references.Count);
            int failedCount = 0;
            long reservedDecodedBytes = 0L;
            IAudioClipHandle pendingHandle = null;

            try
            {
                for (int i = 0; i < references.Count; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    pendingHandle = null;
                    try
                    {
                        pendingHandle = await AudioClipResolver.LoadExternalAsync(references[i], cancellationToken);
                        AudioRuntimeThreadGuard.EnsureMainThread(nameof(AcquireBankClipLeaseAsync) + " continuation");
                        cancellationToken.ThrowIfCancellationRequested();
                        if (isTearingDown || lifecycleGeneration != managerLifecycleGeneration)
                        {
                            AudioClipHandleRelease.Safe(pendingHandle);
                            pendingHandle = null;
                            throw new OperationCanceledException(
                                "AudioManager lifecycle changed while acquiring a bank clip lease.",
                                cancellationToken);
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        AudioClipHandleRelease.Safe(pendingHandle);
                        pendingHandle = null;
                        throw;
                    }
                    catch (Exception exception)
                    {
                        AudioClipHandleRelease.Safe(pendingHandle);
                        pendingHandle = null;
                        failedCount++;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                        Debug.LogException(exception);
#endif
                        continue;
                    }

                    if (pendingHandle != null && pendingHandle.IsSuccess && pendingHandle.Clip != null)
                    {
                        long decodedBytes = EstimateDecodedClipBytes(pendingHandle.Clip);
                        long perLeaseLimit = bankClipLeaseMaxDecodedBytes;
                        bool exceedsPerLeaseLimit = perLeaseLimit > 0 &&
                            (decodedBytes > perLeaseLimit - Math.Min(reservedDecodedBytes, perLeaseLimit));
                        bool exceedsCounterCapacity = decodedBytes > long.MaxValue - reservedDecodedBytes;
                        if (exceedsPerLeaseLimit || exceedsCounterCapacity ||
                            !TryReserveActiveBankClipLeaseBytes(decodedBytes))
                        {
                            AudioClipHandleRelease.Safe(pendingHandle);
                            pendingHandle = null;
                            failedCount++;
                            continue;
                        }

                        reservedDecodedBytes += decodedBytes;
                        retainedHandles.Add(pendingHandle);
                        pendingHandle = null;
                    }
                    else
                    {
                        AudioClipHandleRelease.Safe(pendingHandle);
                        pendingHandle = null;
                        failedCount++;
                    }
                }
            }
            catch
            {
                AudioClipHandleRelease.Safe(pendingHandle);
                for (int i = 0; i < retainedHandles.Count; i++)
                    AudioClipHandleRelease.Safe(retainedHandles[i]);
                ReleaseActiveBankClipLeaseBytes(reservedDecodedBytes, lifecycleGeneration);
                throw;
            }

            try
            {
                return new AudioBankClipLease(
                    bank,
                    references.Count,
                    retainedHandles.ToArray(),
                    failedCount,
                    reservedDecodedBytes,
                    lifecycleGeneration);
            }
            catch
            {
                for (int i = 0; i < retainedHandles.Count; i++)
                    AudioClipHandleRelease.Safe(retainedHandles[i]);
                ReleaseActiveBankClipLeaseBytes(reservedDecodedBytes, lifecycleGeneration);
                throw;
            }
        }

        private static long EstimateDecodedClipBytes(AudioClip clip)
        {
            if (clip == null) return 0L;
            long samples = Math.Max(0, clip.samples);
            long channels = Math.Max(0, clip.channels);
            if (samples == 0L || channels == 0L) return 0L;
            if (samples > long.MaxValue / channels / 4L) return long.MaxValue;
            return samples * channels * 4L;
        }

        private static bool TryReserveActiveBankClipLeaseBytes(long decodedBytes)
        {
            decodedBytes = Math.Max(0L, decodedBytes);
            long budget = activeBankClipLeaseMemoryBudgetBytes;
            if (decodedBytes > long.MaxValue - activeBankClipLeaseMemoryBytes)
                return false;
            if (budget > 0 && decodedBytes > budget - Math.Min(activeBankClipLeaseMemoryBytes, budget))
                return false;

            activeBankClipLeaseMemoryBytes += decodedBytes;
            return true;
        }

        internal static void ReleaseActiveBankClipLeaseBytes(long decodedBytes, int lifecycleGeneration)
        {
            AudioRuntimeThreadGuard.EnsureMainThread(nameof(ReleaseActiveBankClipLeaseBytes));
            if (decodedBytes <= 0L || lifecycleGeneration != managerLifecycleGeneration) return;
            activeBankClipLeaseMemoryBytes = Math.Max(0L, activeBankClipLeaseMemoryBytes - decodedBytes);
        }

        private static bool ReleasePreloadedBankClipLease(int bankId)
        {
            int mutationVersion = GetNextPreloadRequestId();
            preloadBankMutationVersions[bankId] = mutationVersion;
            try
            {
                bool changed = CancelPreloadBankRequest(bankId);
                if (!IsCurrentPreloadMutationVersion(bankId, mutationVersion))
                    return changed;
                if (!preloadedBankClipLeases.TryGetValue(bankId, out IAudioBankClipLease lease))
                    return changed;

                preloadedBankClipLeases.Remove(bankId);
                SafeDisposeBankClipLease(lease);
                return true;
            }
            finally
            {
                RemoveCompletedPreloadMutation(bankId, mutationVersion);
            }
        }

        private static void ReleaseAllPreloadedBankClipLeases()
        {
            if (preloadBankRequests.Count > 0)
            {
                var requests = new List<PreloadBankRequest>(preloadBankRequests.Values);
                preloadBankRequests.Clear();
                for (int i = 0; i < requests.Count; i++)
                    SafeCancelCancellationSource(requests[i].Cancellation);
            }

            if (preloadedBankClipLeases.Count == 0) return;

            var leases = new List<IAudioBankClipLease>(preloadedBankClipLeases.Values);
            preloadedBankClipLeases.Clear();
            for (int i = 0; i < leases.Count; i++)
                SafeDisposeBankClipLease(leases[i]);
        }

        private static int GetNextPreloadRequestId()
        {
            unchecked
            {
                nextPreloadRequestId++;
                if (nextPreloadRequestId == 0)
                    nextPreloadRequestId++;
                return nextPreloadRequestId;
            }
        }

        private static bool IsCurrentPreloadMutation(int bankId, PreloadBankRequest request)
        {
            return request != null &&
                   !isTearingDown &&
                   request.LifecycleGeneration == managerLifecycleGeneration &&
                   IsCurrentPreloadMutationVersion(bankId, request.RequestId);
        }

        private static bool IsCurrentPreloadMutationVersion(int bankId, int expectedMutationVersion)
        {
            return preloadBankMutationVersions.TryGetValue(bankId, out int mutationVersion) &&
                   mutationVersion == expectedMutationVersion;
        }

        private static void RemoveCompletedPreloadMutation(int bankId, int expectedMutationVersion)
        {
            if (preloadBankRequests.ContainsKey(bankId) || preloadedBankClipLeases.ContainsKey(bankId))
                return;
            if (preloadBankMutationVersions.TryGetValue(bankId, out int mutationVersion) &&
                mutationVersion == expectedMutationVersion)
            {
                preloadBankMutationVersions.Remove(bankId);
            }
        }

        private static bool CancelPreloadBankRequest(int bankId)
        {
            if (!preloadBankRequests.TryGetValue(bankId, out PreloadBankRequest request))
                return false;

            preloadBankRequests.Remove(bankId);
            SafeCancelCancellationSource(request.Cancellation);
            return true;
        }

        private static void SafeCancelCancellationSource(CancellationTokenSource cancellation)
        {
            if (cancellation == null) return;
            try
            {
                cancellation.Cancel();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        }

        private static void SafeDisposeCancellationSource(CancellationTokenSource cancellation)
        {
            if (cancellation == null) return;
            try
            {
                cancellation.Dispose();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        }

        private static void SafeDisposeBankClipLease(IAudioBankClipLease lease)
        {
            if (lease == null) return;
            try
            {
                lease.Dispose();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        }

        private static bool CollectExternalClipReferences(AudioEvent audioEvent, PooledAudioClipReferenceSet references)
        {
            if (audioEvent == null) return true;

            var nodes = audioEvent.Nodes;
            if (nodes == null) return true;

            for (int i = 0; i < nodes.Count; i++)
            {
                var node = nodes[i];
                if (node is AudioFile audioFile &&
                    audioFile.TryGetExternalReference(out AudioClipReference audioFileReference))
                {
                    references.Add(audioFileReference);
                }
                else if (node is AudioVoiceFile voiceFile &&
                         voiceFile.TryGetExternalReference(out AudioClipReference voiceFileReference))
                {
                    references.Add(voiceFileReference);
                }
                else if (node is AudioBlendFile blendFile &&
                         blendFile.TryGetExternalReference(out AudioClipReference blendFileReference))
                {
                    references.Add(blendFileReference);
                }

                if (references.Count > MaxExternalClipReferencesPerBank)
                    return false;
            }

            return true;
        }

        /// <summary>
        /// Called periodically to evict expired external clips that exceed the memory budget.
        /// Integrated into the Update loop.
        /// </summary>
        internal static void TryEvictExternalClips()
        {
            float now = Time.realtimeSinceStartup;
            if (now - lastEvictionCheckTime < 5f) return; // Check every 5 seconds max
            lastEvictionCheckTime = now;

            int evicted = ExternalAudioClipHandle.EvictExpiredEntries(ExternalClipIdleTTL, ExternalClipMemoryBudgetBytes);

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (evicted > 0)
                Debug.Log($"AudioManager: Evicted {evicted} expired external clips from cache.");
#endif
        }

        /// <summary>
        /// Returns the total estimated memory currently used by cached external clips.
        /// </summary>
        public static long GetExternalClipCacheMemoryBytes()
        {
            AudioRuntimeThreadGuard.EnsureMainThread(nameof(GetExternalClipCacheMemoryBytes));
            return ExternalAudioClipHandle.GetTotalCachedMemoryBytes();
        }

        #endregion

        #region Editor

        public static void ApplyActiveSolos()
        {
            AudioRuntimeThreadGuard.EnsureMainThread(nameof(ApplyActiveSolos));
            if (!ValidateManager()) return;

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
            AudioRuntimeThreadGuard.EnsureMainThread(nameof(ClearActiveSolos));
            if (!ValidateManager()) return;
            int count = ActiveEvents.Count;
            for (int i = 0; i < count; i++) ActiveEvents[i].ClearSolo();
        }

        #endregion
    }
}
