// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Threading;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("CycloneGames.Audio.Editor")]

namespace CycloneGames.Audio.Runtime
{
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

    /// <summary>
    /// Runtime event of an AudioEvent that is currently playing
    /// </summary>
    [Serializable]
    public class ActiveEvent
    {
        internal int generation;
        internal int handleSlot = -1;

        private static Camera mainCamera;
        private static int mainCameraCacheFrame = -1;

        private const int MaxSourcesPerEvent = 8;
        private const int MaxParametersPerEvent = 8;

        // ---- Hot path fields (accessed every frame in Update) ----
        // Grouped at top for better cache line utilization
        public EventStatus status = EventStatus.Initialized;
        private int sourceCount;
        private float elapsedTime;
        private bool isPaused;
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
        internal double scheduledDspTime = -1;
        private CancellationTokenSource cancellationTokenSource;
        private bool callbackActivated;

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
        public EventSource GetSource(int index) => index >= 0 && index < sourceCount ? sourcesArray[index] : default;

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
            generation++;
            rootEvent = eventToPlay;
            name = eventToPlay.name;
            emitterTransform = emitter;
            if (emitter != null) lastEmitterPos = emitter.position;

            if (cancellationTokenSource != null)
            {
                cancellationTokenSource.Cancel();
                cancellationTokenSource.Dispose();
                cancellationTokenSource = null;
            }
            // CTS is created lazily in GetCancellationToken() only when needed (async loads).

            InitializeParameters();
        }

        public void Play()
        {
            timeStarted = Time.time;
            rootEvent.SetActiveEventProperties(this);

            if (sourceCount == 0 && !hasSnapshotTransition && !isAsync)
            {
                status = EventStatus.Error;
                AudioManager.AddPreviousEvent(this);
                return;
            }

            SetStartingSourceProperties();
            status = EventStatus.Played;
            AudioManager.RegisterActiveEvent(this);
            AudioManager.AddPreviousEvent(this);

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            AudioManager.TrackEventPlayed();
#endif
        }

        public void Update()
        {
            if (rootEvent == null)
            {
                StopImmediate();
                return;
            }

            if (isPaused) return;

            float dt = Time.deltaTime;
            elapsedTime += dt;

            if (!hasPlayed && elapsedTime >= initialDelay)
            {
                hasPlayed = true;
                PlayAllSources();
            }

            // ---- Critical path: always runs every frame ----

            if (hasPlayed && currentFadeTime < targetFadeTime)
            {
                UpdateFade(dt);
            }

            // Track audio playback progress for completion detection
            if (hasPlayed && !hasTimeAdvanced && sourceCount > 0)
            {
                ref var src = ref sourcesArray[0];
                if (src.IsValid && src.source.time > 0f)
                    hasTimeAdvanced = true;
            }

            if (!rootEvent.Output.loop)
            {
                UpdateRemainingTime();
                bool pastGracePeriod = (Time.realtimeSinceStartup - playStartRealtime) >= MinPlayGracePeriod;
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

            if (is3D) UpdateSpatial3D(dt);

            if (useGaze) UpdateGaze();

            if (hasActiveParameters)
            {
                // Scale deltaTime by interval to keep interpolation speed correct
                float scaledDt = dt * updateInterval;
                UpdateParameters(scaledDt);
                ApplyParameters();
            }
        }

        public void Pause()
        {
            if (isPaused || status != EventStatus.Played) return;
            isPaused = true;
            for (int i = 0; i < sourceCount; i++)
            {
                ref var es = ref sourcesArray[i];
                if (es.IsValid) es.source.Pause();
            }
        }

        public void Resume()
        {
            if (!isPaused || status != EventStatus.Played) return;
            isPaused = false;
            for (int i = 0; i < sourceCount; i++)
            {
                ref var es = ref sourcesArray[i];
                if (es.IsValid) es.source.UnPause();
            }
        }

        public bool IsPaused => isPaused;

        public void Stop()
        {
            if (rootEvent == null || rootEvent.FadeOut <= 0)
            {
                StopImmediate();
            }
            else
            {
                targetVolume = 0;
                fadeOriginVolume = sourceCount > 0 && sourcesArray[0].IsValid ? sourcesArray[0].source.volume : 0;
                targetFadeTime = rootEvent.FadeOut;
                currentFadeTime = 0;
                fadeStopQueued = true;
            }
        }

        public void StopImmediate()
        {
            if (status == EventStatus.Stopped) return;

            if (cancellationTokenSource != null)
            {
                cancellationTokenSource.Cancel();
                cancellationTokenSource.Dispose();
                cancellationTokenSource = null;
            }

            if (CompletionCallback != null && !callbackActivated)
            {
                callbackActivated = true;
                CompletionCallback();
            }

            status = EventStatus.Stopped;
            StopAllSources();
            AudioManager.DelayRemoveActiveEvent(this);
        }

        public void SetLocalParameter(AudioParameter localParameter, float newValue)
        {
            for (int i = 0; i < activeParameterCount; i++)
            {
                var param = activeParameters[i];
                if (param != null && param.rootParameter.parameter == localParameter)
                {
                    param.CurrentValue = newValue;
                    return;
                }
            }
        }

        public void OnAsyncLoadCompleted()
        {
            if (status == EventStatus.Stopped)
            {
                StopImmediate();
                return;
            }

            SetStartingSourceProperties();
            if (initialDelay == 0)
            {
                hasPlayed = true;
                PlayAllSources();
            }
        }

        public CancellationToken GetCancellationToken()
        {
            if (cancellationTokenSource == null)
                cancellationTokenSource = new CancellationTokenSource();
            return cancellationTokenSource.Token;
        }

        public bool AddEventSource(AudioClip clip, AudioParameter parameter = null, AnimationCurve responseCurve = null, float startTime = 0, IAudioClipHandle clipHandle = null)
        {
            if (rootEvent == null)
            {
                clipHandle?.Release();
                Debug.LogWarning($"AudioManager: Can't find audio event for {name}!");
                StopImmediate();
                return false;
            }

            if (sourceCount >= MaxSourcesPerEvent)
            {
                clipHandle?.Release();
                Debug.LogWarning($"AudioManager: Max sources ({MaxSourcesPerEvent}) reached for event {name}");
                return false;
            }

            AudioSource source = AudioManager.GetUnusedSource(rootEvent);
            if (source == null)
            {
                Debug.LogWarning($"AudioManager: Can't find unused audio source for event {name}!");
                clipHandle?.Release();
                StopImmediate();
                return false;
            }

            source.loop = rootEvent.Output.loop;
            source.clip = clip;

            sourcesArray[sourceCount] = new EventSource
            {
                source = source,
                parameter = parameter,
                responseCurve = responseCurve,
                startTime = startTime,
                clipHandle = clipHandle
            };
            sourceCount++;

            return true;
        }

        public void SetVolume(float newVolume) => targetVolume = newVolume;
        public void ModulateVolume(float volumeDelta) => targetVolume += volumeDelta;

        public void SetPitch(float newPitch)
        {
            if (newPitch <= 0)
            {
                Debug.LogWarning($"Invalid pitch set in event {rootEvent.name}");
                return;
            }
            eventPitch = newPitch;
        }

        public void ModulatePitch(float pitchDelta) => eventPitch += pitchDelta;

        public void SetEmitterPosition(Vector3 newPos)
        {
            if (sourceCount > 0 && sourcesArray[0].IsValid)
            {
                sourcesArray[0].source.transform.position = newPos;
            }
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

            if (sourceCount == 0) return;

            SetAllSourcePitches(eventPitch);

            // Cache fast-path flags to avoid per-frame checks
            is3D = rootEvent.Output != null && rootEvent.Output.spatialBlend > 0.01f;
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

            if (initialDelay == 0 && !isAsync)
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
            bool useScheduled = scheduledDspTime > 0;
            for (int i = 0; i < sourceCount; i++)
            {
                ref var es = ref sourcesArray[i];
                if (!es.IsValid) continue;

                if (useScheduled)
                {
                    es.source.PlayScheduled(scheduledDspTime + es.startTime);
                }
                else
                {
                    es.source.Play();
                    if (es.startTime > 0) es.source.time = es.startTime;
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

        public void SetAllSourcePositions(Vector3 position)
        {
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
        /// and occlusion raycast — then applies results to each AudioSource.
        /// Zero allocation; GetComponent avoided via per-event LPF cache.
        /// </summary>
        private void UpdateSpatial3D(float dt)
        {
            if (sourceCount == 0 || rootEvent == null) return;
            AudioOutput output = rootEvent.Output;
            if (output == null || output.spatialBlend <= 0f) return;

            // Compute listener distance
            Vector3 listenerPos = AudioManager.GetReferenceListenerPosition();
            Vector3 emitterPos = lastEmitterPos;
            float sqrDist = (emitterPos - listenerPos).sqrMagnitude;
            cachedSqrDistance = sqrDist;

            float dist = Mathf.Sqrt(sqrDist);
            float range = output.MaxDistance - output.MinDistance;
            float normDist = (range > 0.001f)
                ? Mathf.Clamp01((dist - output.MinDistance) / range)
                : (dist >= output.MaxDistance ? 1f : 0f);

            // ---- Distance Low-Pass (air absorption) ----
            float targetDistLP = 22000f;
            if (output.useDistanceLowPass && output.distanceLowPassCurve != null)
                targetDistLP = output.distanceLowPassCurve.Evaluate(normDist);
            distanceLPCutoff = targetDistLP;

            // ---- Spread Curve ----
            if (output.useSpreadCurve && output.spreadCurve != null)
            {
                float spreadDeg = output.spreadCurve.Evaluate(normDist) * 360f;
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
            if (output.useConeAttenuation && emitterTransform != null)
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
                    float halfInner = output.coneInnerAngle * 0.5f;
                    float halfOuter = output.coneOuterAngle * 0.5f;
                    if (angleDeg <= halfInner)
                        newConeScale = 1f;
                    else if (angleDeg >= halfOuter)
                        newConeScale = output.coneOuterVolume;
                    else
                        newConeScale = halfOuter > halfInner
                            ? Mathf.Lerp(1f, output.coneOuterVolume, (angleDeg - halfInner) / (halfOuter - halfInner))
                            : output.coneOuterVolume;
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
            bool needsLPF = output.useDistanceLowPass || occlusionFactor > 0.001f;
            float combinedVolumeScale = coneVolumeScale * occlusionVolumeScale;
            bool hasVolumeModifier = output.useConeAttenuation || occlSettings.enabled;

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

                if (needsLPF)
                {
                    AudioLowPassFilter lpf = cachedLPFilters[i];
                    if (lpf != null) lpf.cutoffFrequency = finalLPCutoff;
                }

                if (hasVolumeModifier)
                    es.source.volume = eventVolume * combinedVolumeScale;
            }
        }

        private void InitializeParameters()
        {
            var eventParams = rootEvent.Parameters;
            int count = eventParams?.Count ?? 0;
            activeParameterCount = 0;

            if (count == 0) return;

            for (int i = 0; i < count && i < MaxParametersPerEvent; i++)
            {
                var p = eventParams[i];
                if (p != null)
                {
                    activeParameters[activeParameterCount].ReInitialize(p);
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
                    es.source.volume = eventVolume * volumeScale;
                }
                else
                {
                    es.source.volume = tempVolume;
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
                SetAllSourceVolumes(eventVolume);
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

            SetAllSourceVolumes(eventVolume);
        }

        public void Reset()
        {
            name = "";
            rootEvent = null;

            // Clear sources without reallocating
            for (int i = 0; i < sourceCount; i++)
            {
                sourcesArray[i].clipHandle?.Release();
                sourcesArray[i] = default;
            }
            sourceCount = 0;

            // Clear parameters without reallocating — instances are reused via ReInitialize
            activeParameterCount = 0;

            status = EventStatus.Initialized;
            timeStarted = 0;
            gazeReference = null;
            emitterTransform = null;
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
            isPaused = false;
            updateSkipCounter = 0;
            updateInterval = 1;
            is3D = false;
            hasActiveParameters = false;
            occlusionFactor = 0f;
            distanceLPCutoff = 22000f;
            coneVolumeScale = 1f;
            currentSpread = 0f;
            cachedSqrDistance = 0f;
            lpFiltersCached = false;
            System.Array.Clear(cachedLPFilters, 0, MaxSourcesPerEvent);
            useGaze = false;
            callbackActivated = false;
            hasTimeAdvanced = false;
            hasSnapshotTransition = false;
            scheduledDspTime = -1;
            handleSlot = -1;
            playStartRealtime = 0;

            if (cancellationTokenSource != null)
            {
                cancellationTokenSource.Cancel();
                cancellationTokenSource.Dispose();
                cancellationTokenSource = null;
            }

            EstimatedRemainingTime = 0;
            Muted = false;
            Soloed = false;
            CompletionCallback = null;
        }

        private void UpdateRemainingTime()
        {
            if (rootEvent == null)
            {
                StopImmediate();
                return;
            }

            if (sourceCount == 0) return;

            ref var mainSource = ref sourcesArray[0];
            if (!mainSource.IsValid || mainSource.source.clip == null)
            {
                StopImmediate();
                return;
            }

            float clipLength = mainSource.source.clip.length;
            if (clipLength <= 0)
            {
                StopImmediate();
                return;
            }

            if (rootEvent.Output.loop)
            {
                EstimatedRemainingTime = 500;
            }
            else
            {
                float currentTime = mainSource.source.time;
                float pitch = mainSource.source.pitch;
                EstimatedRemainingTime = pitch > 0.001f ? (clipLength - currentTime) / pitch : clipLength;
            }

            if (hasTimeAdvanced && mainSource.source.time == 0)
            {
                StopImmediate();
                return;
            }

            if (EstimatedRemainingTime <= rootEvent.FadeOut)
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
            Muted = toggle;
            for (int i = 0; i < sourceCount; i++)
            {
                ref var es = ref sourcesArray[i];
                if (es.IsValid) es.source.mute = toggle;
            }
        }

        public void ToggleSolo()
        {
            Soloed = !Soloed;
            AudioManager.ApplyActiveSolos();
        }

        public void SetSolo(bool toggle)
        {
            Soloed = toggle;
            AudioManager.ApplyActiveSolos();
        }

        public void ApplySolo() => SetMute(!Soloed);
        public void ClearSolo()
        {
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
        Initialized,
        Played,
        Stopped,
        Error
    }
}
