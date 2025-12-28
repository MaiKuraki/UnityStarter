// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Buffers;
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
        private ActiveEvent internalEvent;
        private int generation;

        public AudioHandle(ActiveEvent activeEvent)
        {
            this.internalEvent = activeEvent;
            this.generation = activeEvent != null ? activeEvent.generation : 0;
        }

        public bool IsValid => internalEvent != null && internalEvent.generation == generation && internalEvent.status != EventStatus.Stopped;

        public void Stop()
        {
            if (IsValid) internalEvent.Stop();
        }

        public void StopImmediate()
        {
            if (IsValid) internalEvent.StopImmediate();
        }

        public float EstimatedRemainingTime => IsValid ? internalEvent.EstimatedRemainingTime : 0f;
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

        public bool IsValid => source != null;
    }

    /// <summary>
    /// Runtime event of an AudioEvent that is currently playing
    /// </summary>
    [Serializable]
    public class ActiveEvent
    {
        internal int generation;

        private static Camera mainCamera;
        private static int mainCameraCacheFrame = -1;

        private const int MaxSourcesPerEvent = 8;
        private const int MaxParametersPerEvent = 8;

        public string name = "";
        public AudioEvent rootEvent { get; private set; }

        // Zero-allocation source array with fixed capacity
        private EventSource[] sourcesArray = new EventSource[MaxSourcesPerEvent];
        private int sourceCount;

        public int SourceCount => sourceCount;
        public EventSource GetSource(int index) => index >= 0 && index < sourceCount ? sourcesArray[index] : default;

        // Legacy property for backward compatibility - returns a ReadOnlySpan view
        internal ReadOnlySpan<EventSource> SourcesSpan => new ReadOnlySpan<EventSource>(sourcesArray, 0, sourceCount);

        public EventStatus status = EventStatus.Initialized;

        private ActiveParameter[] activeParameters;
        private int activeParameterCount;

        public float timeStarted;
        private Transform gazeReference;
        internal Transform emitterTransform;
        private Vector3 lastEmitterPos;
        internal string text = "";
        internal float initialDelay;
        private float eventVolume;
        private float targetVolume = 1f;
        private float eventPitch = 1f;
        private float targetFadeTime;
        private float currentFadeTime;
        private float elapsedTime;
        private float fadeOriginVolume;
        private bool fadeStopQueued;
        private bool hasPlayed;
        internal bool isAsync;
        private bool useGaze;
        private bool callbackActivated;
        private bool hasTimeAdvanced;
        internal bool hasSnapshotTransition;
        private CancellationTokenSource cancellationTokenSource;
        internal double scheduledDspTime = -1;

        public float EstimatedRemainingTime { get; private set; }
        public bool Muted { get; private set; }
        public bool Soloed { get; private set; }

        public delegate void EventCompleted();
        public event EventCompleted CompletionCallback;

        public ActiveEvent()
        {
            sourcesArray = new EventSource[MaxSourcesPerEvent];
            activeParameters = new ActiveParameter[MaxParametersPerEvent];
        }

        public void Initialize(AudioEvent eventToPlay, Transform emitter)
        {
            generation++;
            rootEvent = eventToPlay;
            name = eventToPlay.name;
            emitterTransform = emitter;
            if (emitter != null) lastEmitterPos = emitter.position;

            cancellationTokenSource?.Dispose();
            cancellationTokenSource = new CancellationTokenSource();

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
            AudioManager.ActiveEvents.Add(this);
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

            float dt = Time.deltaTime;
            elapsedTime += dt;

            if (!hasPlayed && elapsedTime >= initialDelay)
            {
                hasPlayed = true;
                PlayAllSources();
            }

            if (emitterTransform != null)
            {
                Vector3 newPos = emitterTransform.position;
                Vector3 delta = newPos - lastEmitterPos;
                if (delta.sqrMagnitude > 0.000001f)
                {
                    SetAllSourcePositions(newPos);
                    lastEmitterPos = newPos;
                }
            }

            if (hasPlayed && currentFadeTime < targetFadeTime)
            {
                UpdateFade(dt);
            }

            if (!rootEvent.Output.loop)
            {
                UpdateRemainingTime();
                if (hasPlayed && !IsAnySourcePlaying())
                {
                    StopImmediate();
                }
            }

            if (useGaze) UpdateGaze();

            UpdateParameters();
            ApplyParameters();
        }

        public void Stop()
        {
            if (rootEvent.FadeOut <= 0)
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
            return cancellationTokenSource?.Token ?? CancellationToken.None;
        }

        public bool AddEventSource(AudioClip clip, AudioParameter parameter = null, AnimationCurve responseCurve = null, float startTime = 0)
        {
            if (rootEvent == null)
            {
                Debug.LogWarning($"AudioManager: Can't find audio event for {name}!");
                StopImmediate();
                return false;
            }

            if (sourceCount >= MaxSourcesPerEvent)
            {
                Debug.LogWarning($"AudioManager: Max sources ({MaxSourcesPerEvent}) reached for event {name}");
                return false;
            }

            AudioSource source = AudioManager.GetUnusedSource();
            if (source == null)
            {
                Debug.LogWarning($"AudioManager: Can't find unused audio source for event {name}!");
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
                startTime = startTime
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
                    activeParameters[activeParameterCount++] = new ActiveParameter(p);
                }
            }
        }

        private void UpdateParameters()
        {
            for (int i = 0; i < activeParameterCount; i++)
            {
                var ap = activeParameters[i];
                if (ap?.rootParameter?.parameter == null) continue;

                if (ap.rootParameter.CurrentValue != ap.rootParameter.parameter.CurrentValue)
                {
                    ap.rootParameter.SyncParameter();
                }
            }
        }

        private void ApplyParameters()
        {
            float tempVolume = eventVolume;
            float tempPitch = eventPitch;

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
                }
            }

            for (int i = 0; i < sourceCount; i++)
            {
                ref var es = ref sourcesArray[i];
                if (!es.IsValid) continue;

                es.source.pitch = tempPitch;
                if (es.parameter != null && es.responseCurve != null)
                {
                    float volumeScale = es.responseCurve.Evaluate(es.parameter.CurrentValue);
                    es.source.volume = eventVolume * volumeScale;
                }
                else
                {
                    es.source.volume = tempVolume;
                }
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
                sourcesArray[i] = default;
            }
            sourceCount = 0;

            // Clear parameters without reallocating
            for (int i = 0; i < activeParameterCount; i++)
            {
                activeParameters[i] = null;
            }
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
            useGaze = false;
            callbackActivated = false;
            hasTimeAdvanced = false;
            hasSnapshotTransition = false;
            scheduledDspTime = -1;

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

        // Legacy compatibility: provide sources as list for code that expects List<EventSource>
        private System.Collections.Generic.List<EventSource> sourcesList;
        public System.Collections.Generic.List<EventSource> sources
        {
            get
            {
                if (sourcesList == null) sourcesList = new System.Collections.Generic.List<EventSource>(MaxSourcesPerEvent);
                sourcesList.Clear();
                for (int i = 0; i < sourceCount; i++)
                {
                    sourcesList.Add(sourcesArray[i]);
                }
                return sourcesList;
            }
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