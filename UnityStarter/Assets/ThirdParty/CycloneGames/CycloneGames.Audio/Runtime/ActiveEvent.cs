// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Threading;
using UnityEngine;
using UnityEngine.Audio;
#if UNITY_EDITOR
using UnityEditor;
#endif

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("CycloneGames.Audio.Editor")]
[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("CycloneGames.Audio.Tests.Editor")]

namespace CycloneGames.Audio.Runtime
{
    [Flags]
    internal enum AudioPauseReason
    {
        None = 0,
        Manual = 1,
        Global = 2,
        ApplicationPause = 4,
        FocusLoss = 8,
        LifecycleHold = 16
    }

    /// <summary>
    /// Lightweight handle for safe ActiveEvent reference without holding strong reference
    /// </summary>
    public struct AudioHandle
    {
        private int slot;
        private int generation;

        public AudioHandle(ActiveEvent activeEvent)
        {
            this.slot = activeEvent != null ? activeEvent.handleSlot : -1;
            this.generation = activeEvent != null ? activeEvent.generation : 0;
        }

        private ActiveEvent Resolve()
        {
            AudioRuntimeThreadGuard.EnsureMainThread(nameof(AudioHandle));
            return AudioManager.TryGetActiveEventByHandle(slot, generation, out ActiveEvent activeEvent)
                ? activeEvent
                : null;
        }

        public bool IsValid => Resolve() != null;

        public void Stop()
        {
            ActiveEvent activeEvent = Resolve();
            if (activeEvent != null) activeEvent.Stop();
        }

        public void StopImmediate()
        {
            ActiveEvent activeEvent = Resolve();
            if (activeEvent != null) activeEvent.StopImmediate();
        }

        public void SetVolume(float volume)
        {
            ActiveEvent activeEvent = Resolve();
            if (activeEvent != null) activeEvent.SetVolume(volume);
        }

        public float EstimatedRemainingTime
        {
            get
            {
                ActiveEvent activeEvent = Resolve();
                return activeEvent != null ? activeEvent.EstimatedRemainingTime : 0f;
            }
        }

        public bool IsPlaying => IsValid;
    }

    /// <summary>
    /// Zero-allocation struct for audio source data. Use IsValid to check validity.
    /// </summary>
    public struct EventSource
    {
        public AudioSource source;
        public AudioParameter parameter;
        public AnimationCurve responseCurve;
        public float startTime;
        public IAudioClipHandle clipHandle;

        public bool IsValid => source != null;
    }

    internal sealed class AudioEventCancellationContext
    {
        private CancellationTokenSource source = new CancellationTokenSource();
        private int ownerCount = 1;
        private bool tokenEscapedWithoutOwner;

        internal CancellationToken Token
        {
            get
            {
                CancellationTokenSource current = source;
                return current != null ? current.Token : new CancellationToken(true);
            }
        }

        internal void Retain()
        {
            if (source == null || ownerCount <= 0)
                throw new ObjectDisposedException(nameof(AudioEventCancellationContext));
            ownerCount++;
        }

        internal void MarkTokenEscapedWithoutOwner()
        {
            tokenEscapedWithoutOwner = true;
        }

        internal void Cancel()
        {
            CancellationTokenSource current = source;
            if (current == null || current.IsCancellationRequested) return;

            try
            {
                current.Cancel();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        }

        internal void Release()
        {
            if (ownerCount <= 0) return;
            ownerCount--;
            if (ownerCount > 0) return;

            CancellationTokenSource current = source;
            source = null;
            if (current == null || tokenEscapedWithoutOwner) return;

            try
            {
                current.Dispose();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        }
    }

    /// <summary>
    /// Owns one asynchronous preparation operation for an <see cref="ActiveEvent"/>.
    /// Create the scope with <see cref="ActiveEvent.BeginAsyncPreparation"/>, then complete it
    /// exactly once after its sources have been added. Except for reading
    /// <see cref="CancellationToken"/>, members must be used on the Unity main thread.
    /// </summary>
    /// <remarks>
    /// <see cref="TryAddSource"/> always takes ownership of a non-null clip handle, including
    /// when the event no longer accepts the result. Call <see cref="Complete()"/> on success.
    /// Disposing an incomplete scope completes it as failed so an abandoned operation cannot
    /// leave the event permanently preparing.
    /// </remarks>
    public sealed class AudioEventPreparation : IDisposable
    {
        private ActiveEvent activeEvent;
        private AudioEventCancellationContext cancellationContext;
        private readonly int generation;
        private readonly CancellationToken cancellationToken;
        private bool completed;

        internal AudioEventPreparation(
            ActiveEvent activeEvent,
            int generation,
            AudioEventCancellationContext cancellationContext)
        {
            cancellationContext.Retain();
            this.activeEvent = activeEvent;
            this.cancellationContext = cancellationContext;
            this.generation = generation;
            this.cancellationToken = cancellationContext.Token;
        }

        /// <summary>
        /// Gets the token that is cancelled when the owning event stops or is recycled.
        /// The token may be read and observed from any thread.
        /// </summary>
        public CancellationToken CancellationToken => cancellationToken;

        /// <summary>
        /// Atomically adds a generation-bound source and transfers ownership of
        /// <paramref name="clipHandle"/> to the event. If the source is rejected, the handle is
        /// released before this method returns.
        /// </summary>
        /// <returns><see langword="true"/> when the source was accepted by the event.</returns>
        public bool TryAddSource(
            AudioClip clip,
            AudioParameter parameter = null,
            AnimationCurve responseCurve = null,
            float startTime = 0f,
            IAudioClipHandle clipHandle = null)
        {
            AudioRuntimeThreadGuard.EnsureMainThread(nameof(AudioEventPreparation) + ".TryAddSource");
            if (completed || activeEvent == null)
            {
                AudioClipHandleRelease.Safe(clipHandle);
                return false;
            }

            return activeEvent.TryAddAsyncEventSource(
                generation,
                clip,
                parameter,
                responseCurve,
                startTime,
                clipHandle);
        }

        /// <summary>Completes this preparation successfully. Repeated calls have no effect.</summary>
        public void Complete()
        {
            Complete(true);
        }

        /// <summary>
        /// Completes this preparation. Passing <see langword="false"/> fails and stops the event
        /// if it is still the generation owned by this scope. Repeated calls have no effect.
        /// </summary>
        public void Complete(bool succeeded)
        {
            if (completed) return;

            AudioRuntimeThreadGuard.EnsureMainThread(nameof(AudioEventPreparation) + ".Complete");
            completed = true;

            ActiveEvent owner = activeEvent;
            activeEvent = null;
            AudioEventCancellationContext context = cancellationContext;
            cancellationContext = null;
            try
            {
                owner?.CompleteAsyncLoad(generation, succeeded);
            }
            finally
            {
                context?.Release();
            }
        }

        /// <summary>
        /// Fails an incomplete preparation. Call <see cref="Complete()"/> before disposal after
        /// successfully transferring all source handles.
        /// </summary>
        public void Dispose()
        {
            Complete(false);
        }
    }

    /// <summary>
    /// Runtime event of an AudioEvent that is currently playing
    /// </summary>
    [Serializable]
    public class ActiveEvent
    {
        internal int generation;
        internal int handleSlot = -1;
        internal bool managerOwned;
        internal bool isInPool;

        private static Camera mainCamera;
        private static int mainCameraCacheFrame = -1;

        private const int MaxSourcesPerEvent = 8;
        private const int MaxParametersPerEvent = 8;
        private const int MaxProcessedGraphNodes = 1024;
        private const int MaxGraphStackDepth = 128;
        private const int MaxSnapshotTransitionsPerEvent = 8;

        // ---- Hot path fields (accessed every frame in Update) ----
        // Grouped at top for better cache line utilization
        public EventStatus status = EventStatus.Initialized;
        private int sourceCount;
        private float elapsedTime;
        private AudioPauseReason pauseReasons;
        private bool hasPlayed;
        private bool hasTimeAdvanced;
        private float eventVolume;
        private float targetVolume = 1f;
        private float eventPitch = 1f;
        private float targetFadeTime;
        private float currentFadeTime;
        private float fadeOriginVolume;
        private bool fadeStopQueued;

        // Distance-tiered update LOD: skip non-critical work for distant/simple events
        internal int updateSkipCounter;
        internal int updateInterval = 1;   // 1 = every frame, 2 = every 2nd frame, etc.
        internal bool is3D;
        internal bool hasActiveParameters;

        // 3D spatial state: occlusion, distance LP, spread curve, cone
        private float occlusionFactor;       // 0 = unoccluded, 1 = fully occluded
        private float distanceLPCutoff = 22000f;
        private float coneVolumeScale = 1f;
        private float spatialVolumeScale = 1f;
        private float currentSpread;
        private float cachedSqrDistance;     // listener distance cached on LOD tick
        private AudioLowPassFilter[] cachedLPFilters = new AudioLowPassFilter[MaxSourcesPerEvent];
        private bool lpFiltersCached;

        // ---- Warm path fields (accessed during play/stop) ----
        public string name = "";
        public AudioEvent rootEvent { get; private set; }
        private EventSource[] sourcesArray = new EventSource[MaxSourcesPerEvent];
        private ActiveParameter[] activeParameters;
        private int activeParameterCount;
        public float timeStarted;
        internal Transform emitterTransform;
        private Vector3 lastEmitterPos;
        internal Vector3 LastEmitterPosition => lastEmitterPos;
        internal float initialDelay;
        internal bool isAsync;
        internal bool hasSnapshotTransition;
        private float snapshotTransitionLifetime;
        internal double scheduledDspTime = -1;
        internal bool IsScheduledPlaybackPending => scheduledDspTime > AudioSettings.dspTime;
        private AudioEventCancellationContext cancellationContext;
        private bool callbackActivated;
        private int pendingAsyncLoadCount;
        private bool graphPreparationOpen;
        private bool playbackFinalized;
        internal bool memoryTracked;
        private bool playbackClockStarted;
        private readonly AudioMixerSnapshot[] snapshotTransitions = new AudioMixerSnapshot[MaxSnapshotTransitionsPerEvent];
        private readonly float[] snapshotTransitionTimes = new float[MaxSnapshotTransitionsPerEvent];
        private int snapshotTransitionCount;
        private bool snapshotTransitionsApplied;
        private readonly AudioNode[] graphNodeStack = new AudioNode[MaxGraphStackDepth];
        private int graphNodeStackCount;
        private int processedGraphNodeCount;

        // ---- Cold path fields (rarely accessed) ----
        private Transform gazeReference;
        internal string text = "";
        private bool useGaze;
        /// <summary>
        /// Minimum time in seconds after PlayAllSources before we consider the event
        /// complete. Prevents false completion when AudioSource.isPlaying hasn't
        /// yet returned true on the frame immediately after Play().
        /// </summary>
        private const float MinPlayGracePeriod = 0.05f;
        private float playStartRealtime;

        public int SourceCount => sourceCount;
        public EventSource GetSource(int index)
        {
            AudioRuntimeThreadGuard.EnsureMainThread(nameof(ActiveEvent) + ".GetSource");
            return index >= 0 && index < sourceCount ? sourcesArray[index] : default;
        }

        internal ReadOnlySpan<EventSource> SourcesSpan => new ReadOnlySpan<EventSource>(sourcesArray, 0, sourceCount);

        public float EstimatedRemainingTime { get; private set; }
        public bool Muted { get; private set; }
        public bool Soloed { get; private set; }
        public AudioHandle Handle => new AudioHandle(this);

        public delegate void EventCompleted();
        public event EventCompleted CompletionCallback;

        public ActiveEvent()
        {
            sourcesArray = new EventSource[MaxSourcesPerEvent];
            activeParameters = new ActiveParameter[MaxParametersPerEvent];
            for (int i = 0; i < MaxParametersPerEvent; i++)
                activeParameters[i] = new ActiveParameter();
        }

        public void Initialize(AudioEvent eventToPlay, Transform emitter)
        {
            AudioRuntimeThreadGuard.EnsureMainThread(nameof(ActiveEvent) + ".Initialize");
            if (managerOwned || isInPool)
            {
                throw new InvalidOperationException("Manager-owned ActiveEvent instances cannot be initialized directly.");
            }
            InitializeCore(eventToPlay, emitter);
        }

        internal void InitializeForManager(AudioEvent eventToPlay, Transform emitter)
        {
            managerOwned = true;
            isInPool = false;
            InitializeCore(eventToPlay, emitter);
        }

        private void InitializeCore(AudioEvent eventToPlay, Transform emitter)
        {
            if (status != EventStatus.Initialized || rootEvent != null || handleSlot >= 0)
            {
                throw new InvalidOperationException("An ActiveEvent cannot be initialized while it is managed or playing.");
            }
            if (eventToPlay == null) throw new ArgumentNullException(nameof(eventToPlay));

            CancelAndReleaseCancellationContext();

            unchecked
            {
                generation++;
            }
            if (generation == 0)
            {
                generation = 1;
            }
            rootEvent = eventToPlay;
            name = eventToPlay.name;
            emitterTransform = emitter;
            if (emitter != null)
            {
                lastEmitterPos = emitter.position;
                AudioManager.ValidateScopedParameterOwner(emitter.gameObject);
            }

            // The cancellation context is created lazily only when async work needs it.

            InitializeParameters();
        }

        public void Play()
        {
            AudioRuntimeThreadGuard.EnsureMainThread(nameof(ActiveEvent) + ".Play");
            if (status != EventStatus.Initialized || rootEvent == null)
            {
                throw new InvalidOperationException("An ActiveEvent can only be played once after initialization.");
            }
            timeStarted = Time.time;
            status = EventStatus.Preparing;
            pauseReasons = AudioManager.CurrentPauseReasons;
            graphPreparationOpen = true;
            try
            {
                rootEvent.SetActiveEventProperties(this);
            }
            catch (Exception exception)
            {
                status = EventStatus.Error;
                Debug.LogException(exception);
            }
            finally
            {
                graphPreparationOpen = false;
            }

            if (status != EventStatus.Preparing)
            {
                return;
            }

            if (sourceCount == 0 && !hasSnapshotTransition && pendingAsyncLoadCount == 0 && !isAsync)
            {
                status = EventStatus.Error;
                return;
            }

            AudioManager.RegisterActiveEvent(this);
            AudioManager.AddPreviousEvent(this);
            TryFinalizePreparation();
            if (status != EventStatus.Played && status != EventStatus.Preparing)
                return;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            AudioManager.TrackEventPlayed();
#endif
        }

        public void Update()
        {
            AudioRuntimeThreadGuard.EnsureMainThread(nameof(ActiveEvent) + ".Update");
            UpdateInternal();
        }

        internal void UpdateInternal()
        {
            if (rootEvent == null)
            {
                StopImmediate();
                return;
            }

            if (IsPaused || status == EventStatus.Preparing) return;

            float dt = Time.deltaTime;
            elapsedTime += dt;

            if (!hasPlayed && elapsedTime >= initialDelay)
            {
                hasPlayed = true;
                PlayAllSources();
            }

            if (hasPlayed && !playbackClockStarted && !IsScheduledPlaybackPending)
            {
                playbackClockStarted = true;
                ApplySnapshotTransitions();
            }

            // ---- Critical path: always runs every frame ----

            if (playbackClockStarted && currentFadeTime < targetFadeTime)
            {
                UpdateFade(dt);
                if (status == EventStatus.Stopped) return;
            }

            // Track audio playback progress for completion detection
            if (hasPlayed && !hasTimeAdvanced && sourceCount > 0)
            {
                for (int i = 0; i < sourceCount; i++)
                {
                    ref var source = ref sourcesArray[i];
                    if (source.IsValid && source.source.time > 0f)
                    {
                        hasTimeAdvanced = true;
                        break;
                    }
                }
            }

            if (playbackClockStarted && sourceCount > 0 && !rootEvent.Output.loop)
            {
                UpdateRemainingTime();
                if (status == EventStatus.Stopped) return;
                bool pastGracePeriod = scheduledDspTime > 0
                    ? AudioSettings.dspTime >= scheduledDspTime + MinPlayGracePeriod
                    : (Time.realtimeSinceStartup - playStartRealtime) >= MinPlayGracePeriod;
                if (hasPlayed && pastGracePeriod && !IsAnySourcePlaying())
                {
                    StopImmediate();
                    return;
                }
            }

            // ---- LOD path: position, parameters, gaze can run at reduced frequency ----
            // updateInterval=1 means every frame; >1 means skip non-critical work
            if (updateInterval > 1)
            {
                updateSkipCounter++;
                if (updateSkipCounter < updateInterval)
                    return;
                updateSkipCounter = 0;
            }

            if (is3D && emitterTransform != null)
            {
                Vector3 newPos = emitterTransform.position;
                Vector3 delta = newPos - lastEmitterPos;
                if (delta.sqrMagnitude > 0.000001f)
                {
                    SetAllSourcePositions(newPos);
                    lastEmitterPos = newPos;
                }
            }

            if (useGaze) UpdateGaze();

            if (hasActiveParameters)
            {
                // Scale deltaTime by interval to keep interpolation speed correct
                float scaledDt = dt * updateInterval;
                UpdateParameters(scaledDt);
            }

            if (is3D)
            {
                UpdateSpatial3D(dt);
            }
            else if (hasActiveParameters)
            {
                ApplyParameters();
            }
        }

        public void Pause()
        {
            AudioRuntimeThreadGuard.EnsureMainThread(nameof(ActiveEvent) + ".Pause");
            SetPauseReason(AudioPauseReason.Manual, true);
        }

        public void Resume()
        {
            AudioRuntimeThreadGuard.EnsureMainThread(nameof(ActiveEvent) + ".Resume");
            SetPauseReason(AudioPauseReason.Manual, false);
        }

        internal void SetPauseReason(AudioPauseReason reason, bool paused)
        {
            bool wasPaused = IsPaused;
            if (paused)
                pauseReasons |= reason;
            else
                pauseReasons &= ~reason;

            bool nowPaused = IsPaused;
            if (wasPaused == nowPaused || (status != EventStatus.Played && status != EventStatus.Preparing))
                return;

            if (nowPaused)
            {
                if (!hasPlayed) return;
                for (int i = 0; i < sourceCount; i++)
                {
                    ref var eventSource = ref sourcesArray[i];
                    if (eventSource.IsValid) eventSource.source.Pause();
                }
                return;
            }

            if (!hasPlayed)
            {
                if (status == EventStatus.Played && elapsedTime >= initialDelay)
                {
                    hasPlayed = true;
                    PlayAllSources();
                }
                return;
            }

            for (int i = 0; i < sourceCount; i++)
            {
                ref var eventSource = ref sourcesArray[i];
                if (eventSource.IsValid) eventSource.source.UnPause();
            }
        }

        public bool IsPaused => pauseReasons != AudioPauseReason.None;

        public void Stop()
        {
            AudioRuntimeThreadGuard.EnsureMainThread(nameof(ActiveEvent) + ".Stop");
            if (fadeStopQueued) return;
            if (status != EventStatus.Played || rootEvent == null || rootEvent.FadeOut <= 0)
            {
                StopImmediate();
            }
            else
            {
                targetVolume = 0;
                fadeOriginVolume = eventVolume;
                targetFadeTime = rootEvent.FadeOut;
                currentFadeTime = 0;
                fadeStopQueued = true;
            }
        }

        public void StopImmediate()
        {
            AudioRuntimeThreadGuard.EnsureMainThread(nameof(ActiveEvent) + ".StopImmediate");
            if (status == EventStatus.Stopped) return;

            status = EventStatus.Stopped;
            bool ownershipDetached = AudioManager.DeactivateActiveEvent(this);
            if (!ownershipDetached)
            {
                StopAllSources();
            }

            CancelAndReleaseCancellationContext();

            EventCompleted callbacks = CompletionCallback;
            if (callbacks != null && !callbackActivated)
            {
                callbackActivated = true;
                Delegate[] invocationList = callbacks.GetInvocationList();
                for (int i = 0; i < invocationList.Length; i++)
                {
                    try
                    {
                        ((EventCompleted)invocationList[i])();
                    }
                    catch (Exception exception)
                    {
                        Debug.LogException(exception);
                    }
                }
            }

            AudioManager.RemoveActiveEvent(this);
        }

        public void SetLocalParameter(AudioParameter localParameter, float newValue)
        {
            AudioRuntimeThreadGuard.EnsureMainThread(nameof(ActiveEvent) + ".SetLocalParameter");
            for (int i = 0; i < activeParameterCount; i++)
            {
                var param = activeParameters[i];
                if (param != null && param.rootParameter.parameter == localParameter)
                {
                    param.CurrentValue = newValue;
                    ApplyParameters();
                    return;
                }
            }
        }

        /// <summary>
        /// Begins a generation-bound asynchronous preparation operation for a custom
        /// <see cref="AudioNode"/>. Returns <see langword="null"/> when this event is not currently
        /// accepting preparation work.
        /// </summary>
        public AudioEventPreparation BeginAsyncPreparation()
        {
            AudioRuntimeThreadGuard.EnsureMainThread(nameof(ActiveEvent) + ".BeginAsyncPreparation");
            int preparationGeneration = BeginAsyncLoad();
            if (preparationGeneration == 0)
                return null;

            try
            {
                AudioEventCancellationContext context = GetOrCreateCancellationContext(preparationGeneration);
                if (context == null)
                {
                    CompleteAsyncLoad(preparationGeneration, false);
                    return null;
                }

                return new AudioEventPreparation(this, preparationGeneration, context);
            }
            catch
            {
                CompleteAsyncLoad(preparationGeneration, false);
                throw;
            }
        }

        internal int BeginAsyncLoad()
        {
            if (rootEvent == null || status != EventStatus.Preparing)
                return 0;

            pendingAsyncLoadCount++;
            return generation;
        }

        internal bool TryEnterGraphNode(AudioNode node)
        {
            if (node == null || status == EventStatus.Error || status == EventStatus.Stopped)
                return false;

            if (processedGraphNodeCount >= MaxProcessedGraphNodes || graphNodeStackCount >= MaxGraphStackDepth)
            {
                status = EventStatus.Error;
                Debug.LogError($"AudioManager: Graph processing budget exceeded for event '{name}'.");
                return false;
            }

            for (int i = 0; i < graphNodeStackCount; i++)
            {
                if (graphNodeStack[i] != node) continue;
                status = EventStatus.Error;
                Debug.LogError($"AudioManager: Runtime graph cycle detected in event '{name}' at node '{node.name}'.");
                return false;
            }

            graphNodeStack[graphNodeStackCount++] = node;
            processedGraphNodeCount++;
            return true;
        }

        internal void ExitGraphNode(AudioNode node)
        {
            if (graphNodeStackCount <= 0) return;

            int lastIndex = graphNodeStackCount - 1;
            graphNodeStack[lastIndex] = null;
            graphNodeStackCount = lastIndex;
        }

        private AudioEventCancellationContext GetOrCreateCancellationContext(int expectedGeneration)
        {
            if (!CanAcceptAsyncResult(expectedGeneration))
                return null;

            if (cancellationContext == null)
                cancellationContext = new AudioEventCancellationContext();
            return cancellationContext;
        }

        [Obsolete("Use BeginAsyncPreparation() and AudioEventPreparation.CancellationToken from an AudioNode implementation.")]
        public CancellationToken GetCancellationToken()
        {
            AudioRuntimeThreadGuard.EnsureMainThread(nameof(ActiveEvent) + ".GetCancellationToken");
            AudioEventCancellationContext context = GetOrCreateCancellationContext(generation);
            if (context == null)
                return new CancellationToken(true);

            // The legacy API has no matching release boundary. Avoid disposing its source after
            // Stop so an already-returned token remains safe to observe or register against.
            context.MarkTokenEscapedWithoutOwner();
            return context.Token;
        }

        [Obsolete("Use AudioEventPreparation.Complete() or Dispose() for the scope returned by BeginAsyncPreparation().")]
        public void OnAsyncLoadCompleted()
        {
            AudioRuntimeThreadGuard.EnsureMainThread(nameof(ActiveEvent) + ".OnAsyncLoadCompleted");
            isAsync = false;
            TryFinalizePreparation();
        }

        internal bool TryAddAsyncEventSource(
            int expectedGeneration,
            AudioClip clip,
            AudioParameter parameter,
            AnimationCurve responseCurve,
            float startTime,
            IAudioClipHandle clipHandle)
        {
            if (!CanAcceptAsyncResult(expectedGeneration))
            {
                AudioClipHandleRelease.Safe(clipHandle);
                return false;
            }

            return AddEventSource(clip, parameter, responseCurve, startTime, clipHandle);
        }

        internal void CompleteAsyncLoad(int expectedGeneration, bool succeeded)
        {
            if (expectedGeneration != generation || rootEvent == null)
                return;

            if (pendingAsyncLoadCount > 0)
                pendingAsyncLoadCount--;

            if (!succeeded && status != EventStatus.Stopped && status != EventStatus.Error)
            {
                status = EventStatus.Error;
                StopImmediate();
                return;
            }

            TryFinalizePreparation();
        }

        private bool CanAcceptAsyncResult(int expectedGeneration)
        {
            return expectedGeneration != 0 &&
                   expectedGeneration == generation &&
                   rootEvent != null &&
                   status != EventStatus.Stopped &&
                   status != EventStatus.Error;
        }

        private void TryFinalizePreparation()
        {
            if (graphPreparationOpen || pendingAsyncLoadCount > 0 || isAsync || playbackFinalized)
                return;
            if (status == EventStatus.Stopped || status == EventStatus.Error || rootEvent == null)
                return;
            if (sourceCount == 0 && !hasSnapshotTransition)
            {
                status = EventStatus.Error;
                StopImmediate();
                return;
            }

            playbackFinalized = true;
            try
            {
                rootEvent.Output?.ApplySourceProperties(this);
                SetStartingSourceProperties();
                status = EventStatus.Played;
                AudioManager.TrackActiveEventMemory(this);
            }
            catch (Exception exception)
            {
                status = EventStatus.Error;
                Debug.LogException(exception);
                StopImmediate();
            }
        }

        internal void RegisterSnapshotTransition(AudioMixerSnapshot snapshot, float transitionTime)
        {
            if (snapshot == null) return;
            hasSnapshotTransition = true;
            if (float.IsNaN(transitionTime) || float.IsInfinity(transitionTime))
                transitionTime = 0f;
            transitionTime = Mathf.Max(0f, transitionTime);
            snapshotTransitionLifetime = Mathf.Max(snapshotTransitionLifetime, transitionTime);
            if (snapshotTransitionCount >= MaxSnapshotTransitionsPerEvent)
            {
                status = EventStatus.Error;
                Debug.LogError($"AudioManager: Event '{name}' exceeds the {MaxSnapshotTransitionsPerEvent}-snapshot transition limit.");
                return;
            }

            snapshotTransitions[snapshotTransitionCount] = snapshot;
            snapshotTransitionTimes[snapshotTransitionCount] = transitionTime;
            snapshotTransitionCount++;
        }

        public bool AddEventSource(AudioClip clip, AudioParameter parameter = null, AnimationCurve responseCurve = null, float startTime = 0, IAudioClipHandle clipHandle = null)
        {
            AudioRuntimeThreadGuard.EnsureMainThread(nameof(ActiveEvent) + ".AddEventSource");
            if (rootEvent == null || status != EventStatus.Preparing)
            {
                AudioClipHandleRelease.Safe(clipHandle);
                Debug.LogWarning($"AudioManager: Event '{name}' is not accepting source additions.");
                return false;
            }

            if (sourceCount >= MaxSourcesPerEvent)
            {
                AudioClipHandleRelease.Safe(clipHandle);
                Debug.LogWarning($"AudioManager: Max sources ({MaxSourcesPerEvent}) reached for event {name}");
                StopImmediate();
                return false;
            }

            int expectedGeneration = generation;
            AudioEvent expectedRootEvent = rootEvent;
            AudioSource source = AudioManager.GetUnusedSource(rootEvent, this);
            if (source == null)
            {
                Debug.LogWarning($"AudioManager: Can't find unused audio source for event {name}!");
                AudioClipHandleRelease.Safe(clipHandle);
                StopImmediate();
                return false;
            }

            if (generation != expectedGeneration ||
                status != EventStatus.Preparing ||
                rootEvent != expectedRootEvent ||
                sourceCount >= MaxSourcesPerEvent)
            {
                AudioManager.ReturnSourceToPool(source);
                AudioClipHandleRelease.Safe(clipHandle);
                return false;
            }

            source.loop = rootEvent.Output.loop;
            source.clip = clip;
            source.transform.position = lastEmitterPos;

            sourcesArray[sourceCount] = new EventSource
            {
                source = source,
                parameter = parameter,
                responseCurve = responseCurve,
                startTime = startTime,
                clipHandle = clipHandle
            };
            sourceCount++;
            AudioManager.NotifyActiveEventSourcesChanged(this);

            return true;
        }

        public void SetVolume(float newVolume)
        {
            AudioRuntimeThreadGuard.EnsureMainThread(nameof(ActiveEvent) + ".SetVolume");
            if (float.IsNaN(newVolume) || float.IsInfinity(newVolume))
            {
                Debug.LogWarning("AudioManager: Event volume must be finite.");
                return;
            }
            targetVolume = Mathf.Max(0f, newVolume);
            if (currentFadeTime >= targetFadeTime)
            {
                eventVolume = targetVolume;
                ApplyParameters();
            }
        }

        public void ModulateVolume(float volumeDelta) => SetVolume(targetVolume + volumeDelta);

        public void SetPitch(float newPitch)
        {
            AudioRuntimeThreadGuard.EnsureMainThread(nameof(ActiveEvent) + ".SetPitch");
            if (float.IsNaN(newPitch) || float.IsInfinity(newPitch) || newPitch <= 0)
            {
                string eventName = rootEvent != null ? rootEvent.name : name;
                Debug.LogWarning($"Invalid pitch set in event {eventName}");
                return;
            }
            eventPitch = newPitch;
            ApplyParameters();
        }

        public void ModulatePitch(float pitchDelta) => SetPitch(eventPitch + pitchDelta);

        public void SetEmitterPosition(Vector3 newPos)
        {
            AudioRuntimeThreadGuard.EnsureMainThread(nameof(ActiveEvent) + ".SetEmitterPosition");
            if (!IsFinite(newPos))
            {
                Debug.LogWarning("AudioManager: Emitter position must be finite.");
                return;
            }
            SetAllSourcePositions(newPos);
            lastEmitterPos = newPos;
        }

        #region Source Property Settings

        private void SetStartingSourceProperties()
        {
            if (rootEvent.FadeIn > 0)
            {
                SetAllSourceVolumes(0);
                eventVolume = 0;
                fadeOriginVolume = 0;
                currentFadeTime = 0;
                targetFadeTime = rootEvent.FadeIn;
            }
            else
            {
                SetAllSourceVolumes(targetVolume);
                eventVolume = targetVolume;
            }

            if (emitterTransform != null)
            {
                lastEmitterPos = emitterTransform.position;
            }
            SetAllSourcePositions(lastEmitterPos);

            SetAllSourcePitches(eventPitch);

            // Cache fast-path flags to avoid per-frame checks
            is3D = rootEvent.Output != null && rootEvent.Output.EffectiveSpatialBlend > 0.01f;
            hasActiveParameters = activeParameterCount > 0;

            useGaze = HasGazeProperty();
            if (useGaze)
            {
                UpdateMainCameraCache();
                if (mainCamera != null)
                {
                    gazeReference = mainCamera.transform;
                    UpdateGaze();
                }
            }

            ApplyParameters();

            if (initialDelay == 0 && !IsPaused)
            {
                hasPlayed = true;
                PlayAllSources();
            }
        }

        private void SetAllSourceVolumes(float newVolume)
        {
            for (int i = 0; i < sourceCount; i++)
            {
                ref var es = ref sourcesArray[i];
                if (es.IsValid) es.source.volume = newVolume;
            }
        }

        private void SetAllSourcePitches(float newPitch)
        {
            for (int i = 0; i < sourceCount; i++)
            {
                ref var es = ref sourcesArray[i];
                if (es.IsValid) es.source.pitch = newPitch;
            }
        }

        private void PlayAllSources()
        {
            playStartRealtime = Time.realtimeSinceStartup;
            bool useScheduled = scheduledDspTime > AudioSettings.dspTime;
            playbackClockStarted = !useScheduled;
            if (!useScheduled)
            {
                scheduledDspTime = -1;
                ApplySnapshotTransitions();
            }
            for (int i = 0; i < sourceCount; i++)
            {
                ref var es = ref sourcesArray[i];
                if (!es.IsValid) continue;

                if (useScheduled)
                {
                    if (es.startTime > 0f)
                        es.source.time = Mathf.Min(es.startTime, Mathf.Max(0f, es.source.clip.length - 0.001f));
                    es.source.PlayScheduled(scheduledDspTime);
                }
                else
                {
                    if (es.startTime > 0f)
                        es.source.time = Mathf.Min(es.startTime, Mathf.Max(0f, es.source.clip.length - 0.001f));
                    es.source.Play();
                }
            }
        }

        private void StopAllSources()
        {
            for (int i = 0; i < sourceCount; i++)
            {
                ref var es = ref sourcesArray[i];
                if (es.IsValid)
                {
                    es.source.Stop();
                    es.source.SetScheduledEndTime(double.MaxValue);
                }
            }
        }

        private void ApplySnapshotTransitions()
        {
            if (snapshotTransitionsApplied) return;
            snapshotTransitionsApplied = true;

            for (int i = 0; i < snapshotTransitionCount; i++)
            {
                AudioMixerSnapshot snapshot = snapshotTransitions[i];
                if (snapshot == null) continue;
                try
                {
                    snapshot.TransitionTo(snapshotTransitionTimes[i]);
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception);
                }
            }

            if (sourceCount == 0 && hasSnapshotTransition)
                AudioManager.DelayRemoveActiveEvent(this, snapshotTransitionLifetime);
        }

        internal void DetachSourcesToPool()
        {
            for (int i = 0; i < sourceCount; i++)
            {
                ref EventSource eventSource = ref sourcesArray[i];
                AudioSource source = eventSource.source;
                eventSource.source = null;
                if (source != null)
                {
                    AudioManager.ReturnSourceToPool(source);
                }
            }
        }

        public void SetAllSourcePositions(Vector3 position)
        {
            AudioRuntimeThreadGuard.EnsureMainThread(nameof(ActiveEvent) + ".SetAllSourcePositions");
            if (!IsFinite(position)) return;
            for (int i = 0; i < sourceCount; i++)
            {
                ref var es = ref sourcesArray[i];
                if (es.IsValid && es.source.transform != null)
                {
                    es.source.transform.position = position;
                }
            }
        }

        private bool IsAnySourcePlaying()
        {
            for (int i = sourceCount - 1; i >= 0; i--)
            {
                ref var es = ref sourcesArray[i];
                if (es.IsValid && es.source.isPlaying) return true;
            }
            return false;
        }

        #endregion

        #region Private Functions

        /// <summary>
        /// Runs on the LOD tick. Evaluates distance low-pass, spread curve, cone attenuation,
        /// and occlusion raycast, then applies results to each AudioSource.
        /// Zero allocation; GetComponent avoided via per-event LPF cache.
        /// </summary>
        private void UpdateSpatial3D(float dt)
        {
            if (sourceCount == 0 || rootEvent == null) return;
            AudioOutput output = rootEvent.Output;
            if (output == null || output.EffectiveSpatialBlend <= 0f) return;

            // Compute listener distance
            Vector3 listenerPos = AudioManager.GetReferenceListenerPosition();
            Vector3 emitterPos = lastEmitterPos;
            float sqrDist = (emitterPos - listenerPos).sqrMagnitude;
            cachedSqrDistance = sqrDist;

            float dist = Mathf.Sqrt(sqrDist);
            float minDistance = output.EffectiveMinDistance;
            float maxDistance = output.EffectiveMaxDistance;
            float range = maxDistance - minDistance;
            float normDist = (range > 0.001f)
                ? Mathf.Clamp01((dist - minDistance) / range)
                : (dist >= maxDistance ? 1f : 0f);

            // ---- Distance Low-Pass (air absorption) ----
            float targetDistLP = 22000f;
            AnimationCurve distanceLowPassCurve = output.EffectiveDistanceLowPassCurve;
            if (output.EffectiveUseDistanceLowPass && distanceLowPassCurve != null)
                targetDistLP = distanceLowPassCurve.Evaluate(normDist);
            distanceLPCutoff = targetDistLP;

            // ---- Spread Curve ----
            AnimationCurve spreadCurve = output.EffectiveSpreadCurve;
            if (output.EffectiveUseSpreadCurve && spreadCurve != null)
            {
                float spreadDeg = spreadCurve.Evaluate(normDist) * 360f;
                if (Mathf.Abs(spreadDeg - currentSpread) > 0.5f)
                {
                    currentSpread = spreadDeg;
                    for (int i = 0; i < sourceCount; i++)
                    {
                        ref var es = ref sourcesArray[i];
                        if (es.IsValid) es.source.spread = spreadDeg;
                    }
                }
            }

            // ---- Cone Attenuation ----
            float newConeScale = 1f;
            if (output.EffectiveUseConeAttenuation && emitterTransform != null)
            {
                Vector3 toListener = listenerPos - emitterPos;
                float toListenerSqr = toListener.sqrMagnitude;
                if (toListenerSqr > 0.0001f)
                {
                    // Normalize without Normalize() to avoid allocation check overhead
                    float invLen = 1f / Mathf.Sqrt(toListenerSqr);
                    toListener.x *= invLen; toListener.y *= invLen; toListener.z *= invLen;
                    float dot = Vector3.Dot(emitterTransform.forward, toListener);
                    float angleDeg = Mathf.Acos(Mathf.Clamp(dot, -1f, 1f)) * Mathf.Rad2Deg;
                    float halfInner = output.EffectiveConeInnerAngle * 0.5f;
                    float halfOuter = output.EffectiveConeOuterAngle * 0.5f;
                    if (angleDeg <= halfInner)
                        newConeScale = 1f;
                    else if (angleDeg >= halfOuter)
                        newConeScale = output.EffectiveConeOuterVolume;
                    else
                        newConeScale = halfOuter > halfInner
                            ? Mathf.Lerp(1f, output.EffectiveConeOuterVolume, (angleDeg - halfInner) / (halfOuter - halfInner))
                            : output.EffectiveConeOuterVolume;
                }
            }
            coneVolumeScale = newConeScale;

            // ---- Occlusion Raycast ----
            float occlusionCutoff = 22000f;
            float occlusionVolumeScale = 1f;
            var occlSettings = AudioManager.ActiveOcclusionSettings;
            if (occlSettings.enabled && sqrDist <= occlSettings.maxOcclusionDistance * occlSettings.maxOcclusionDistance)
            {
                bool isOccluded = Physics.Linecast(emitterPos, listenerPos, occlSettings.occlusionLayers);
                float targetFactor = isOccluded ? 1f : 0f;
                // Scale lerp speed by LOD interval so it doesn't animate slower at higher LOD
                occlusionFactor = Mathf.MoveTowards(occlusionFactor, targetFactor,
                    dt * updateInterval * occlSettings.interpolationSpeed);
                occlusionCutoff = Mathf.Lerp(22000f, occlSettings.occludedCutoffHz, occlusionFactor);
                occlusionVolumeScale = Mathf.Lerp(1f, occlSettings.occludedVolumeScale, occlusionFactor);
            }
            else if (!occlSettings.enabled && occlusionFactor > 0f)
            {
                // Smoothly clear when occlusion is toggled off at runtime
                occlusionFactor = Mathf.MoveTowards(occlusionFactor, 0f, dt * 5f);
                occlusionCutoff = Mathf.Lerp(22000f, occlSettings.occludedCutoffHz, occlusionFactor);
                occlusionVolumeScale = Mathf.Lerp(1f, occlSettings.occludedVolumeScale, occlusionFactor);
            }

            // ---- Apply to AudioSources ----
            float finalLPCutoff = Mathf.Min(distanceLPCutoff, occlusionCutoff);
            bool needsLPF = output.EffectiveUseDistanceLowPass || occlusionFactor > 0.001f;
            float combinedVolumeScale = coneVolumeScale * occlusionVolumeScale;
            spatialVolumeScale = output.EffectiveUseConeAttenuation || occlSettings.enabled
                ? combinedVolumeScale
                : 1f;

            // Cache LPF components once per active event lifetime (avoids repeated GetComponent)
            if (!lpFiltersCached && needsLPF)
            {
                for (int i = 0; i < sourceCount; i++)
                {
                    ref var es = ref sourcesArray[i];
                    if (es.IsValid) cachedLPFilters[i] = es.source.GetComponent<AudioLowPassFilter>();
                }
                lpFiltersCached = true;
            }

            for (int i = 0; i < sourceCount; i++)
            {
                ref var es = ref sourcesArray[i];
                if (!es.IsValid) continue;

                if (lpFiltersCached)
                {
                    AudioLowPassFilter lpf = cachedLPFilters[i];
                    if (lpf != null)
                    {
                        lpf.enabled = needsLPF;
                        lpf.cutoffFrequency = needsLPF ? finalLPCutoff : 22000f;
                    }
                }

            }

            ApplyParameters();
        }

        private void InitializeParameters()
        {
            var eventParams = rootEvent.Parameters;
            int count = eventParams?.Count ?? 0;
            activeParameterCount = 0;

            if (count == 0) return;

            int parameterScopeId = emitterTransform != null ? emitterTransform.gameObject.GetInstanceID() : 0;
            for (int i = 0; i < count && i < MaxParametersPerEvent; i++)
            {
                var p = eventParams[i];
                if (p != null)
                {
                    activeParameters[activeParameterCount].ReInitialize(p, parameterScopeId);
                    activeParameterCount++;
                }
            }
        }

        private void UpdateParameters(float dt)
        {
            for (int i = 0; i < activeParameterCount; i++)
            {
                var ap = activeParameters[i];
                if (ap?.rootParameter?.parameter == null) continue;
                ap.Update(dt);
            }
        }

        private void ApplyParameters()
        {
            float tempVolume = eventVolume;
            float tempPitch = eventPitch;
            float spatialBlend = -1f;
            float panStereo = float.NaN;
            float reverbZoneMix = float.NaN;
            float dopplerLevel = float.NaN;

            for (int i = 0; i < activeParameterCount; i++)
            {
                var param = activeParameters[i];
                if (param?.rootParameter == null) continue;

                switch (param.rootParameter.paramType)
                {
                    case ParameterType.Volume:
                        tempVolume *= param.CurrentResult;
                        break;
                    case ParameterType.Pitch:
                        tempPitch *= param.CurrentResult;
                        break;
                    case ParameterType.SpatialBlend:
                        spatialBlend = param.CurrentResult;
                        break;
                    case ParameterType.PanStereo:
                        panStereo = param.CurrentResult;
                        break;
                    case ParameterType.ReverbZoneMix:
                        reverbZoneMix = param.CurrentResult;
                        break;
                    case ParameterType.DopplerLevel:
                        dopplerLevel = param.CurrentResult;
                        break;
                }
            }

            for (int i = 0; i < sourceCount; i++)
            {
                ref var es = ref sourcesArray[i];
                if (!es.IsValid) continue;

                es.source.pitch = tempPitch;
                if (es.parameter != null && es.responseCurve != null)
                {
                    float volumeScale = es.responseCurve.Evaluate(es.parameter.EvaluateCurrentValue());
                    es.source.volume = tempVolume * volumeScale * spatialVolumeScale;
                }
                else
                {
                    es.source.volume = tempVolume * spatialVolumeScale;
                }

                if (spatialBlend >= 0f) es.source.spatialBlend = spatialBlend;
                if (!float.IsNaN(panStereo)) es.source.panStereo = panStereo;
                if (!float.IsNaN(reverbZoneMix)) es.source.reverbZoneMix = reverbZoneMix;
                if (!float.IsNaN(dopplerLevel)) es.source.dopplerLevel = dopplerLevel;
            }
        }

        private void UpdateFade(float dt)
        {
            if (targetFadeTime <= 0.0001f)
            {
                eventVolume = targetVolume;
                ApplyParameters();
                if (fadeStopQueued) StopImmediate();
                return;
            }

            currentFadeTime += dt;
            float t = currentFadeTime / targetFadeTime;

            if (t >= 1f)
            {
                eventVolume = targetVolume;
                currentFadeTime = targetFadeTime;
                if (fadeStopQueued)
                {
                    StopImmediate();
                    return;
                }
            }
            else
            {
                eventVolume = fadeOriginVolume + (targetVolume - fadeOriginVolume) * t;
            }

            ApplyParameters();
        }

        [Obsolete("Active events are reset by AudioManager. Stop the event instead of resetting it directly.")]
        public void Reset()
        {
            AudioRuntimeThreadGuard.EnsureMainThread(nameof(ActiveEvent) + ".Reset");
            if (managerOwned || isInPool || rootEvent != null || handleSlot >= 0)
            {
                throw new InvalidOperationException(
                    "A managed ActiveEvent cannot be reset directly. Call StopImmediate instead.");
            }

            ResetForPool();
        }

        internal void ResetForPool()
        {
            name = "";
            rootEvent = null;

            // Clear sources without reallocating
            for (int i = 0; i < sourceCount; i++)
            {
                IAudioClipHandle clipHandle = sourcesArray[i].clipHandle;
                sourcesArray[i] = default;
                if (clipHandle == null) continue;

                AudioClipHandleRelease.Safe(clipHandle);
            }
            sourceCount = 0;

            // Clear parameters without reallocating; instances are reused via ReInitialize.
            activeParameterCount = 0;

            status = EventStatus.Initialized;
            timeStarted = 0;
            gazeReference = null;
            emitterTransform = null;
            lastEmitterPos = Vector3.zero;
            text = "";
            initialDelay = 0;
            eventVolume = 0;
            targetVolume = 1;
            eventPitch = 1;
            targetFadeTime = 0;
            currentFadeTime = 0;
            elapsedTime = 0;
            fadeOriginVolume = 0;
            fadeStopQueued = false;
            hasPlayed = false;
            isAsync = false;
            pauseReasons = AudioPauseReason.None;
            updateSkipCounter = 0;
            updateInterval = 1;
            is3D = false;
            hasActiveParameters = false;
            occlusionFactor = 0f;
            distanceLPCutoff = 22000f;
            coneVolumeScale = 1f;
            spatialVolumeScale = 1f;
            currentSpread = 0f;
            cachedSqrDistance = 0f;
            lpFiltersCached = false;
            System.Array.Clear(cachedLPFilters, 0, MaxSourcesPerEvent);
            useGaze = false;
            callbackActivated = false;
            hasTimeAdvanced = false;
            hasSnapshotTransition = false;
            snapshotTransitionLifetime = 0f;
            scheduledDspTime = -1;
            handleSlot = -1;
            playStartRealtime = 0;
            pendingAsyncLoadCount = 0;
            graphPreparationOpen = false;
            playbackFinalized = false;
            memoryTracked = false;
            playbackClockStarted = false;
            for (int i = 0; i < snapshotTransitionCount; i++)
            {
                snapshotTransitions[i] = null;
                snapshotTransitionTimes[i] = 0f;
            }
            snapshotTransitionCount = 0;
            snapshotTransitionsApplied = false;
            for (int i = 0; i < graphNodeStackCount; i++)
                graphNodeStack[i] = null;
            graphNodeStackCount = 0;
            processedGraphNodeCount = 0;

            CancelAndReleaseCancellationContext();

            EstimatedRemainingTime = 0;
            Muted = false;
            Soloed = false;
            CompletionCallback = null;
        }

        private void CancelAndReleaseCancellationContext()
        {
            AudioEventCancellationContext context = cancellationContext;
            cancellationContext = null;
            if (context == null) return;

            context.Cancel();
            context.Release();
        }

        private static bool IsFinite(Vector3 value)
        {
            return !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
                   !float.IsNaN(value.y) && !float.IsInfinity(value.y) &&
                   !float.IsNaN(value.z) && !float.IsInfinity(value.z);
        }

        private void UpdateRemainingTime()
        {
            if (rootEvent == null)
            {
                StopImmediate();
                return;
            }

            if (rootEvent.Output.loop)
            {
                EstimatedRemainingTime = 500;
                return;
            }

            float maxRemainingTime = 0f;
            bool hasValidSource = false;
            for (int i = 0; i < sourceCount; i++)
            {
                ref var eventSource = ref sourcesArray[i];
                AudioSource source = eventSource.source;
                AudioClip clip = source != null ? source.clip : null;
                if (clip == null || clip.length <= 0f) continue;

                hasValidSource = true;
                float pitch = Mathf.Abs(source.pitch);
                float remainingTime = pitch > 0.001f
                    ? Mathf.Max(0f, clip.length - source.time) / pitch
                    : clip.length;
                if (remainingTime > maxRemainingTime)
                    maxRemainingTime = remainingTime;
            }

            if (!hasValidSource)
            {
                StopImmediate();
                return;
            }

            bool scheduledStartPending = IsScheduledPlaybackPending;
            float scheduledDelay = scheduledStartPending
                ? (float)(scheduledDspTime - AudioSettings.dspTime)
                : 0f;
            EstimatedRemainingTime = maxRemainingTime + scheduledDelay;
            if (scheduledStartPending)
            {
                return;
            }

            if (!fadeStopQueued && EstimatedRemainingTime <= rootEvent.FadeOut)
            {
                Stop();
            }
        }

        private bool HasGazeProperty()
        {
            var eventParams = rootEvent.Parameters;
            int count = eventParams?.Count ?? 0;

            for (int i = 0; i < count; i++)
            {
                var p = eventParams[i]?.parameter;
                if (p == null)
                {
                    Debug.LogWarning($"Audio event '{rootEvent.name}' has a null parameter!");
                    continue;
                }
                if (p.UseGaze) return true;
            }
            return false;
        }

        private void UpdateGaze()
        {
            if (sourceCount == 0) return;

            ref var mainSource = ref sourcesArray[0];
            if (!mainSource.IsValid) return;

            if (gazeReference == null)
            {
                UpdateMainCameraCache();
                if (mainCamera != null)
                {
                    gazeReference = mainCamera.transform;
                }
                else
                {
                    useGaze = false;
                    return;
                }
            }

            Vector3 posDelta = gazeReference.position - mainSource.source.transform.position;
            float gazeAngle = Mathf.Abs(180 - Vector3.Angle(gazeReference.forward, posDelta));

            for (int i = 0; i < activeParameterCount; i++)
            {
                var param = activeParameters[i];
                if (param.rootParameter.parameter.UseGaze)
                {
                    param.CurrentValue = gazeAngle;
                }
            }
        }

        private static void UpdateMainCameraCache()
        {
            int currentFrame = Time.frameCount;
            if (mainCamera == null || mainCamera.gameObject == null || mainCameraCacheFrame != currentFrame)
            {
                mainCamera = Camera.main;
                mainCameraCacheFrame = currentFrame;
            }
        }

        #endregion

        #region Editor

        public void ToggleGaze(bool toggle, AudioParameter gazeParameter)
        {
            AudioRuntimeThreadGuard.EnsureMainThread(nameof(ActiveEvent) + ".ToggleGaze");
            useGaze = toggle;
            if (!toggle)
            {
                for (int i = 0; i < activeParameterCount; i++)
                {
                    var param = activeParameters[i];
                    if (param.rootParameter.parameter == gazeParameter)
                    {
                        param.Reset();
                    }
                }
            }
        }

        public void ToggleMute() => SetMute(!Muted);

        public void SetMute(bool toggle)
        {
            AudioRuntimeThreadGuard.EnsureMainThread(nameof(ActiveEvent) + ".SetMute");
            Muted = toggle;
            for (int i = 0; i < sourceCount; i++)
            {
                ref var es = ref sourcesArray[i];
                if (es.IsValid) es.source.mute = toggle;
            }
        }

        public void ToggleSolo()
        {
            AudioRuntimeThreadGuard.EnsureMainThread(nameof(ActiveEvent) + ".ToggleSolo");
            Soloed = !Soloed;
            AudioManager.ApplyActiveSolos();
        }

        public void SetSolo(bool toggle)
        {
            AudioRuntimeThreadGuard.EnsureMainThread(nameof(ActiveEvent) + ".SetSolo");
            Soloed = toggle;
            AudioManager.ApplyActiveSolos();
        }

        public void ApplySolo() => SetMute(!Soloed);
        public void ClearSolo()
        {
            AudioRuntimeThreadGuard.EnsureMainThread(nameof(ActiveEvent) + ".ClearSolo");
            Soloed = false;
            SetMute(Muted);
        }

#if UNITY_EDITOR

        public void DisplayProperties()
        {
            if (sourceCount == 0 || !sourcesArray[0].IsValid) return;

            if (emitterTransform != null)
            {
                EditorGUILayout.LabelField("Emitter:" + emitterTransform.gameObject.name);
            }
            else
            {
                EditorGUILayout.LabelField("Emitter:" + sourcesArray[0].source.gameObject.name);
            }
        }

        protected void DrawWindow(int id)
        {
            GUI.DragWindow();
            DisplayProperties();
        }

        public virtual void DrawNode(int id, Rect window)
        {
            GUI.Window(id, window, DrawWindow, name);
        }

#endif

        #endregion
    }

    public enum EventStatus
    {
        Initialized = 0,
        Played = 1,
        Stopped = 2,
        Error = 3,
        Preparing = 4
    }
}
