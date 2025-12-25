// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;
using System.Threading;
#if UNITY_EDITOR
using UnityEditor;
#endif

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("CycloneGames.Audio.Editor")]

namespace CycloneGames.Audio.Runtime
{
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
            if (IsValid)
            {
                internalEvent.Stop();
            }
        }

        public void StopImmediate()
        {
            if (IsValid)
            {
                internalEvent.StopImmediate();
            }
        }
        
        // Expose other necessary methods safely
        public float EstimatedRemainingTime => IsValid ? internalEvent.EstimatedRemainingTime : 0f;
        public bool IsPlaying => IsValid;
    }

    /// <summary>
    /// The runtime event of an Audio Event that is currently playing
    /// </summary>
    [System.Serializable]
    public class ActiveEvent
    {
        // Internal generation count for handle validation
        internal int generation = 0;
        
        // Cached reference to the main camera to avoid repeated calls to Camera.main, which can impact performance.
        private static Camera mainCamera;
        private static int mainCameraCacheFrame = -1;

        /// <summary>
        /// The name of the audio event to be played
        /// </summary>
        public string name = "";
        /// <summary>
        /// The AudioEvent that is being played
        /// </summary>
        public AudioEvent rootEvent { get; private set; }
        /// <summary>
        /// The AudioSource to play the AudioEvent on
        /// </summary>
        public List<EventSource> sources { get; private set; } = new List<EventSource>();
        /// <summary>
        /// The latest status of the event
        /// </summary>
        public EventStatus status = EventStatus.Initialized;
        /// <summary>
        /// The AudioParameters in use by the event
        /// </summary>
        private ActiveParameter[] activeParameters;
        public float timeStarted = 0f;
        /// <summary>
        /// The Transform to use to calculate the user's gaze position
        /// </summary>
        private Transform gazeReference;
        /// <summary>
        /// The transform that the sound should follow in the scene
        /// </summary>
        internal Transform emitterTransform = null;
        private Vector3 lastEmitterPos;
        /// <summary>
        /// The text associated with the event, usually for subtitles
        /// </summary>
        internal string text = "";
        /// <summary>
        /// Time in seconds before the audio file will start playing
        /// </summary>
        internal float initialDelay = 0;
        /// <summary>
        /// The volume of the event before parameters are applied
        /// </summary>
        private float eventVolume = 0;
        /// <summary>
        /// The volume that the event is fading to or settled on after fading
        /// </summary>
        private float targetVolume = 1;
        /// <summary>
        /// The pitch value 
        /// </summary>
        private float eventPitch = 1;
        /// <summary>
        /// The amount of time in seconds that the event will fade in or out
        /// </summary>
        private float targetFadeTime = 0;
        /// <summary>
        /// The amount of time in seconds that the event has been fading in or out
        /// </summary>
        private float currentFadeTime = 0;
        /// <summary>
        /// The amount of time in seconds since the event started
        /// </summary>
        private float elapsedTime = 0;
        /// <summary>
        /// The previous volume the event was at before starting a fade
        /// </summary>
        private float fadeOriginVolume = 0;
        /// <summary>
        /// Whether the event is currently fading out to be stopped
        /// </summary>
        private bool fadeStopQueued = false;
        /// <summary>
        /// Whether the event has played a sound yet
        /// </summary>
        private bool hasPlayed = false;
        /// <summary>
        /// Whether the event is loading asynchronously
        /// </summary>
        internal bool isAsync = false;
        /// <summary>
        /// Whether the event is using the user's gaze for a parameter
        /// </summary>
        private bool useGaze = false;
        private bool callbackActivated = false;
        private bool hasTimeAdvanced = false;
        internal bool hasSnapshotTransition = false;
        private CancellationTokenSource cancellationTokenSource;

        /// <summary>
        /// The DSP time at which the event is scheduled to play. -1 means play immediately.
        /// </summary>
        internal double scheduledDspTime = -1;

        /// <summary>
        /// The time left on the event unless it is stopped or the pitch changes
        /// </summary>
        public float EstimatedRemainingTime { get; private set; }
        /// <summary>
        /// Whether the event has been muted
        /// </summary>
        public bool Muted { get; private set; } = false;
        /// <summary>
        /// Whether the event has been soloed
        /// </summary>
        public bool Soloed { get; private set; } = false;

        /// <summary>
        /// Delegate for event completion callback
        /// </summary>
        public delegate void EventCompleted();

        /// <summary>
        /// Callback when the event stops
        /// </summary>
        public event EventCompleted CompletionCallback;

        /// <summary>
        /// Default Constructor for pooling.
        /// </summary>
        public ActiveEvent()
        {
            this.sources = new List<EventSource>(4); // Pre-allocate for efficiency
        }

        public void Initialize(AudioEvent eventToPlay, Transform emitterTransform)
        {
            this.generation++; // Increment generation on initialization
            this.rootEvent = eventToPlay;
            this.name = eventToPlay.name;
            this.emitterTransform = emitterTransform;
            if (emitterTransform != null)
            {
                this.lastEmitterPos = emitterTransform.position;
            }

            if (this.cancellationTokenSource != null)
            {
                this.cancellationTokenSource.Dispose();
            }
            this.cancellationTokenSource = new CancellationTokenSource();

            InitializeParameters();
        }

        /// <summary>
        /// Internal AudioManager use: starts the audio event
        /// </summary>
        public void Play()
        {
            this.timeStarted = Time.time;
            this.rootEvent.SetActiveEventProperties(this);

            if (this.sources.Count == 0 && !this.hasSnapshotTransition && !this.isAsync)
            {
                this.status = EventStatus.Error;
                AudioManager.AddPreviousEvent(this);
                return;
            }

            SetStartingSourceProperties();

            this.status = EventStatus.Played;

            AudioManager.ActiveEvents.Add(this);
            AudioManager.AddPreviousEvent(this);
            
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            AudioManager.TrackEventPlayed();
#endif
        }

        /// <summary>
        /// Internal AudioManager use: update fade and RTPC values
        /// </summary>
        public void Update()
        {
            if (this.rootEvent == null)
            {
                StopImmediate();
                return;
            }

            float dt = Time.deltaTime;
            this.elapsedTime += dt;

            if (!this.hasPlayed && this.elapsedTime >= this.initialDelay)
            {
                this.hasPlayed = true;
                PlayAllSources();
            }

            if (this.emitterTransform != null)
            {
                Vector3 newPos = this.emitterTransform.position;
                // Use sqrMagnitude comparison to avoid sqrt calculation
                Vector3 delta = newPos - this.lastEmitterPos;
                if (delta.sqrMagnitude > 0.000001f) // 0.001mm threshold
                {
                    SetAllSourcePositions(newPos);
                    this.lastEmitterPos = newPos;
                }
            }

            if (this.hasPlayed && this.currentFadeTime < this.targetFadeTime)
            {
                UpdateFade(dt);
            }

            if (!this.rootEvent.Output.loop)
            {
                UpdateRemainingTime();
                if (this.hasPlayed && !IsAnySourcePlaying())
                {
                    StopImmediate();
                }
            }

            if (this.useGaze)
            {
                UpdateGaze();
            }

            UpdateParameters();
            ApplyParameters();
        }

        /// <summary>
        /// Stops the event using the default fade time if applicable
        /// </summary>
        public void Stop()
        {
            if (this.rootEvent.FadeOut <= 0)
            {
                StopImmediate();
            }
            else
            {
                this.targetVolume = 0;
                this.fadeOriginVolume = this.sources[0].source.volume;
                this.targetFadeTime = this.rootEvent.FadeOut;
                this.currentFadeTime = 0;
                this.fadeStopQueued = true;
            }
        }

        /// <summary>
        /// Stops the event immediately, ignoring the event's fade time
        /// </summary>
        public void StopImmediate()
        {
            // Prevent the event from being stopped multiple times, which can cause issues like returning sources to the pool multiple times.
            if (this.status == EventStatus.Stopped)
            {
                return;
            }

            if (this.cancellationTokenSource != null)
            {
                this.cancellationTokenSource.Cancel();
                this.cancellationTokenSource.Dispose();
                this.cancellationTokenSource = null;
            }

            if (this.CompletionCallback != null && !this.callbackActivated)
            {
                this.callbackActivated = true;
                CompletionCallback();
            }

            this.status = EventStatus.Stopped;

            StopAllSources();

            AudioManager.DelayRemoveActiveEvent(this);
        }

        /// <summary>
        /// Set the value of a parameter on this event independent of the root parameter's value
        /// </summary>
        /// <param name="localParameter">The AudioParameter set on the root AudioEvent</param>
        /// <param name="newValue">The new local value on this event instance (does not set the AudioParameter value)</param>
        public void SetLocalParameter(AudioParameter localParameter, float newValue)
        {
            bool hasParameter = false;
            ActiveParameter tempParameter;
            int paramCount = this.activeParameters.Length;
            for (int i = 0; i < paramCount; i++)
            {
                tempParameter = this.activeParameters[i];
                if (tempParameter.rootParameter.parameter == localParameter)
                {
                    hasParameter = true;
                    tempParameter.CurrentValue = newValue;
                }
            }

            if (!hasParameter)
            {
                //Debug.LogWarningFormat("Audio event {0} does not have parameter {1}", this.rootEvent.name, localParameter.name);
            }
        }

        public void OnAsyncLoadCompleted()
        {
            if (this.status == EventStatus.Stopped)
            {
                // If the event was stopped while loading, just clean up
                StopImmediate();
                return;
            }

            // Now that the clip is loaded, we can apply the final properties and play
            SetStartingSourceProperties();
            if (this.initialDelay == 0)
            {
                this.hasPlayed = true;
                PlayAllSources();
            }
        }

        public CancellationToken GetCancellationToken()
        {
            return this.cancellationTokenSource?.Token ?? CancellationToken.None;
        }

        public bool AddEventSource(AudioClip clip, AudioParameter parameter = null, AnimationCurve responseCurve = null, float startTime = 0)
        {
            if (this.rootEvent == null)
            {
                Debug.LogWarningFormat(this.emitterTransform, "AudioManager: Can't find audio event for {0}!", this.name);
                StopImmediate();
                return false;
            }

            EventSource newSource = new EventSource();
            newSource.source = AudioManager.GetUnusedSource();

            if (newSource.source == null)
            {
                Debug.LogWarningFormat(this.emitterTransform, "AudioManager: Can't find unused audio source for event {0}!", this.name);
                StopImmediate();
                return false;
            }

            newSource.source.loop = this.rootEvent.Output.loop;
            newSource.source.clip = clip;
            newSource.parameter = parameter;
            newSource.responseCurve = responseCurve;
            newSource.startTime = startTime;
            sources.Add(newSource);

            return true;
        }

        /// <summary>
        /// Internal AudioManager use: initializes the volume based on the event's AudioOutput
        /// </summary>
        /// <param name="newVolume">The target volume for the AudioSource (not necessarily the current volume)</param>
        public void SetVolume(float newVolume)
        {
            this.targetVolume = newVolume;
        }

        /// <summary>
        /// Offset the volume by the specified amount
        /// </summary>
        /// <param name="volumeDelta">A number between -1 and 1 for volume changes</param>
        public void ModulateVolume(float volumeDelta)
        {
            this.targetVolume += volumeDelta;
        }

        /// <summary>
        /// Overwrite the pitch with a new value
        /// </summary>
        /// <param name="newPitch">Pitch value between -1 and 3</param>
        public void SetPitch(float newPitch)
        {
            if (newPitch <= 0)
            {
                Debug.LogWarningFormat("Invalid pitch set in event {0}", this.rootEvent.name);
                return;
            }

            this.eventPitch = newPitch;
        }

        /// <summary>
        /// Offset the pitch by the specified amount
        /// </summary>
        /// <param name="pitchDelta">A number between -1 and 3 for pitch changes</param>
        public void ModulatePitch(float pitchDelta)
        {
            this.eventPitch += pitchDelta;
        }

        public void SetEmitterPosition(Vector3 newPos)
        {
            if (this.sources.Count > 0)
            {
                this.sources[0].source.transform.position = newPos;
            }
            else
            {
                Debug.LogWarningFormat(this.emitterTransform, "No audio emitter for active event {0}", this.name);
            }
        }

        #region Source Property Settings

        private void SetStartingSourceProperties()
        {
            if (this.rootEvent.FadeIn > 0)
            {
                SetallSourceVolumes(0);
                this.eventVolume = 0;
                this.fadeOriginVolume = 0;
                this.currentFadeTime = 0;
                this.targetFadeTime = this.rootEvent.FadeIn;
            }
            else
            {
                SetallSourceVolumes(this.targetVolume);
                this.eventVolume = this.targetVolume;
            }

            if (this.sources.Count == 0) return; // Don't apply other properties if async loading is not complete

            SetAllSourcePitches(this.eventPitch);

            this.useGaze = HasGazeProperty();
            if (this.useGaze)
            {
                UpdateMainCameraCache();
                if (mainCamera != null)
                {
                    this.gazeReference = mainCamera.transform;
                    UpdateGaze();
                }
            }

            ApplyParameters();

            if (this.initialDelay == 0 && !this.isAsync)
            {
                this.hasPlayed = true;
                PlayAllSources();
            }
        }

        private void SetallSourceVolumes(float newVolume)
        {
            int count = this.sources.Count;
            EventSource eventSource;
            for (int i = 0; i < count; i++)
            {
                eventSource = this.sources[i];
                if (eventSource != null && eventSource.source != null)
                {
                    eventSource.source.volume = newVolume;
                }
            }
        }

        private void SetAllSourcePitches(float newPitch)
        {
            int count = this.sources.Count;
            EventSource eventSource;
            for (int i = 0; i < count; i++)
            {
                eventSource = this.sources[i];
                if (eventSource != null && eventSource.source != null)
                {
                    eventSource.source.pitch = newPitch;
                }
            }
        }

        private void PlayAllSources()
        {
            int count = this.sources.Count;
            bool useScheduled = this.scheduledDspTime > 0;
            EventSource eventSource;
            
            for (int i = 0; i < count; i++)
            {
                eventSource = this.sources[i];
                if (eventSource == null || eventSource.source == null) continue;
                
                if (useScheduled)
                {
                    double playTime = this.scheduledDspTime + eventSource.startTime;
                    eventSource.source.PlayScheduled(playTime);
                }
                else
                {
                    eventSource.source.Play();
                    // Only set time if not using PlayScheduled, as PlayScheduled handles timing
                    if (eventSource.startTime > 0)
                    {
                        eventSource.source.time = eventSource.startTime;
                    }
                }
            }
        }

        private void StopAllSources()
        {
            int count = this.sources.Count;
            EventSource tempSource;
            for (int i = 0; i < count; i++)
            {
                tempSource = this.sources[i];
                if (tempSource != null && tempSource.source != null)
                {
                    tempSource.source.Stop();
                    // Reset scheduled time to avoid accidental replays or logic errors
                    tempSource.source.SetScheduledEndTime(double.MaxValue); 
                }
            }
        }

        public void SetAllSourcePositions(Vector3 position)
        {
            int count = this.sources.Count;
            EventSource eventSource;
            for (int i = 0; i < count; i++)
            {
                eventSource = this.sources[i];
                if (eventSource != null && eventSource.source != null && eventSource.source.transform != null)
                {
                    eventSource.source.transform.position = position;
                }
            }
        }

        private bool IsAnySourcePlaying()
        {
            // Early exit optimization: check sources in reverse order for better cache locality
            int count = this.sources.Count;
            EventSource eventSource;
            for (int i = count - 1; i >= 0; i--)
            {
                eventSource = this.sources[i];
                if (eventSource != null && eventSource.source != null && eventSource.source.isPlaying)
                {
                    return true;
                }
            }

            return false;
        }

        #endregion

        #region Private Functions

        private void InitializeParameters()
        {
            this.activeParameters = new ActiveParameter[this.rootEvent.Parameters.Count];
            for (int i = 0; i < this.activeParameters.Length; i++)
            {
                if (this.rootEvent.Parameters[i] != null)
                {
                    this.activeParameters[i] = new ActiveParameter(this.rootEvent.Parameters[i]);
                }
            }
        }

        /// <summary>
        /// Internal AudioManager use: Sync changes to the parameters on all ActiveEvents
        /// </summary>
        private void UpdateParameters()
        {
            int paramCount = this.activeParameters.Length;
            AudioEventParameter tempParam;
            ActiveParameter activeParam;
            for (int i = 0; i < paramCount; i++)
            {
                activeParam = this.activeParameters[i];
                if (activeParam == null) continue;
                
                tempParam = activeParam.rootParameter;
                if (tempParam == null || tempParam.parameter == null)
                {
                    continue;
                }

                if (tempParam.CurrentValue != tempParam.parameter.CurrentValue)
                {
                    tempParam.SyncParameter();
                }
            }
        }

        /// <summary>
        /// Internal AudioManager use: applies parameter changes
        /// </summary>
        private void ApplyParameters()
        {
            if (this.activeParameters == null || this.activeParameters.Length == 0)
            {
                // No parameters, apply base values directly
                int sourceCount = this.sources.Count;
                EventSource tempSource;
                for (int i = 0; i < sourceCount; i++)
                {
                    tempSource = this.sources[i];
                    if (tempSource != null && tempSource.source != null)
                    {
                        if (tempSource.parameter != null && tempSource.responseCurve != null)
                        {
                            float volumeScale = tempSource.responseCurve.Evaluate(tempSource.parameter.CurrentValue);
                            tempSource.source.volume = this.eventVolume * volumeScale;
                        }
                        else
                        {
                            tempSource.source.volume = this.eventVolume;
                        }
                        tempSource.source.pitch = this.eventPitch;
                    }
                }
                return;
            }

            float tempVolume = this.eventVolume;
            float tempPitch = this.eventPitch;
            int paramCount = this.activeParameters.Length;
            ActiveParameter tempParameter;
            for (int i = 0; i < paramCount; i++)
            {
                tempParameter = this.activeParameters[i];
                if (tempParameter?.rootParameter == null) continue;
                
                switch (tempParameter.rootParameter.paramType)
                {
                    case ParameterType.Volume:
                        tempVolume *= tempParameter.CurrentResult;
                        break;
                    case ParameterType.Pitch:
                        tempPitch *= tempParameter.CurrentResult;
                        break;
                }
            }

            int sourceCount2 = this.sources.Count;
            EventSource tempSource2;
            for (int i = 0; i < sourceCount2; i++)
            {
                tempSource2 = this.sources[i];
                if (tempSource2 == null || tempSource2.source == null) continue;
                
                tempSource2.source.volume = tempVolume;
                tempSource2.source.pitch = tempPitch;
                if (tempSource2.parameter != null && tempSource2.responseCurve != null)
                {
                    float volumeScale = tempSource2.responseCurve.Evaluate(tempSource2.parameter.CurrentValue);
                    tempSource2.source.volume = this.eventVolume * volumeScale;
                }
            }
        }

        /// <summary>
        /// Internal AudioManager use: update volume on ActiveEvents fading in and out
        /// </summary>
        private void UpdateFade(float dt)
        {
            // Avoid division by zero
            if (this.targetFadeTime <= 0.0001f)
            {
                this.eventVolume = this.targetVolume;
                SetallSourceVolumes(this.eventVolume);
                if (this.fadeStopQueued)
                {
                    StopImmediate();
                }
                return;
            }

            this.currentFadeTime += dt;
            float percentageFaded = this.currentFadeTime / this.targetFadeTime;

            if (percentageFaded >= 1.0f)
            {
                this.eventVolume = this.targetVolume;
                this.currentFadeTime = this.targetFadeTime;

                if (this.fadeStopQueued)
                {
                    StopImmediate();
                    return;
                }
            }
            else
            {
                // Linear interpolation optimized for performance
                float volumeDelta = this.targetVolume - this.fadeOriginVolume;
                this.eventVolume = this.fadeOriginVolume + (volumeDelta * percentageFaded);
            }

            SetallSourceVolumes(this.eventVolume);
        }

        public void Reset()
        {
            // generation is NOT reset here, it increments on Initialize. 
            // Or we can increment here to invalidate old handles immediately.
            // Incrementing in Initialize is safer as it marks the start of a new life.
            
            this.name = "";
            this.rootEvent = null;
            this.sources.Clear(); // Keep capacity
            this.status = EventStatus.Initialized;
            this.activeParameters = null;
            this.timeStarted = 0;
            this.gazeReference = null;
            this.emitterTransform = null;
            this.text = "";
            this.initialDelay = 0;
            this.eventVolume = 0;
            this.targetVolume = 1;
            this.eventPitch = 1;
            this.targetFadeTime = 0;
            this.currentFadeTime = 0;
            this.elapsedTime = 0;
            this.fadeOriginVolume = 0;
            this.fadeStopQueued = false;
            this.hasPlayed = false;
            this.isAsync = false;
            this.useGaze = false;
            this.callbackActivated = false;
            this.hasTimeAdvanced = false;
            this.hasSnapshotTransition = false;
            this.scheduledDspTime = -1;
            
            if (this.cancellationTokenSource != null)
            {
                this.cancellationTokenSource.Cancel();
                this.cancellationTokenSource.Dispose();
                this.cancellationTokenSource = null;
            }

            this.EstimatedRemainingTime = 0;
            this.Muted = false;
            this.Soloed = false;
            this.CompletionCallback = null;
        }

        /// <summary>
        /// Recalculate estimated time the event will be active for
        /// </summary>
        private void UpdateRemainingTime()
        {
            if (this.rootEvent == null)
            {
                Debug.LogWarningFormat(this.emitterTransform, "AudioManager: Can't find audio event for {0}!", this.name);
                StopImmediate();
                return;
            }

            if (this.sources.Count == 0)
            {
                return;
            }

            AudioSource mainSource = this.sources[0].source;
            if (mainSource == null || mainSource.clip == null)
            {
                StopImmediate();
                return;
            }

            // Cache clip length to avoid repeated property access
            float clipLength = mainSource.clip.length;
            if (clipLength <= 0)
            {
                StopImmediate();
                return;
            }

            if (this.rootEvent.Output.loop)
            {
                this.EstimatedRemainingTime = 500;
            }
            else
            {
                float currentTime = mainSource.time;
                float pitch = mainSource.pitch;
                // Avoid division by zero
                if (pitch > 0.001f)
                {
                    this.EstimatedRemainingTime = (clipLength - currentTime) / pitch;
                }
                else
                {
                    this.EstimatedRemainingTime = clipLength;
                }
            }

            if (this.hasTimeAdvanced && mainSource.time == 0)
            {
                StopImmediate();
                return;
            }

            if (this.EstimatedRemainingTime <= this.rootEvent.FadeOut)
            {
                Stop();
            }
        }

        /// <summary>
        /// Check if any of the parameters on the event use gaze for the value
        /// </summary>
        /// <returns>Whether at least one parameter uses gaze</returns>
        private bool HasGazeProperty()
        {
            for (int i = 0; i < this.rootEvent.Parameters.Count; i++)
            {
                AudioParameter tempParam = this.rootEvent.Parameters[i].parameter;

                if (tempParam == null)
                {
                    Debug.LogWarningFormat("Audio event '{0}' has a null parameter!", this.rootEvent.name);
                    continue;
                }

                if (tempParam.UseGaze)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Get the user's head position relative to the audio event, and set the property's value
        /// </summary>
        private void UpdateGaze()
        {
            if (this.sources.Count == 0) return;

            AudioSource mainSource = this.sources[0].source;
            if (mainSource == null) return;

            if (this.gazeReference == null)
            {
                UpdateMainCameraCache();
                if (mainCamera != null)
                {
                    this.gazeReference = mainCamera.transform;
                }
                else
                {
                    // No main camera found, disable gaze updates for this event.
                    this.useGaze = false;
                    return;
                }
            }

            Vector3 posDelta = this.gazeReference.position - mainSource.transform.position;
            float gazeAngle = Mathf.Abs(180 - Vector3.Angle(this.gazeReference.forward, posDelta));

            int paramCount = this.activeParameters.Length;
            ActiveParameter tempParameter;
            for (int i = 0; i < paramCount; i++)
            {
                tempParameter = this.activeParameters[i];
                if (tempParameter.rootParameter.parameter.UseGaze)
                {
                    tempParameter.CurrentValue = gazeAngle;
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

        /// <summary>
        /// Set whether the defined parameter on the event uses the player's gaze angle
        /// </summary>
        /// <param name="toggle"></param>
        /// <param name="gazeParameter"></param>
        public void ToggleGaze(bool toggle, AudioParameter gazeParameter)
        {
            this.useGaze = toggle;

            if (!toggle)
            {
            int paramCount = this.activeParameters.Length;
            ActiveParameter tempParameter;
            for (int i = 0; i < paramCount; i++)
            {
                tempParameter = this.activeParameters[i];
                if (tempParameter.rootParameter.parameter == gazeParameter)
                {
                    tempParameter.Reset();
                }
            }
            }
        }

        /// <summary>
        /// Toggles whether the event is audible
        /// </summary>
        public void ToggleMute()
        {
            SetMute(!this.Muted);
        }

        /// <summary>
        /// Sets whether the event is audible
        /// </summary>
        /// <param name="toggle">Whether sound should be made inaudible</param>
        public void SetMute(bool toggle)
        {
            this.Muted = toggle;
            int count = this.sources.Count;
            EventSource eventSource;
            for (int i = 0; i < count; i++)
            {
                eventSource = this.sources[i];
                if (eventSource != null && eventSource.source != null)
                {
                    eventSource.source.mute = toggle;
                }
            }
        }

        /// <summary>
        /// Toggles whether only this sound (and other soloed sounds) are audible
        /// </summary>
        public void ToggleSolo()
        {
            this.Soloed = !this.Soloed;
            AudioManager.ApplyActiveSolos();
        }

        /// <summary>
        /// Mutes all other non-soloed events
        /// </summary>
        /// <param name="toggle">Whether this event is part of the isolated audible events</param>
        public void SetSolo(bool toggle)
        {
            this.Soloed = toggle;
            AudioManager.ApplyActiveSolos();
        }

        /// <summary>
        /// Internal AudioManager use: applies solo property to AudioSource, ignoring "mute" property
        /// </summary>
        public void ApplySolo()
        {
            SetMute(!this.Soloed);
        }

        /// <summary>
        /// Internal AudioManager use: clears all solo properties, and reverts to mute property
        /// </summary>
        public void ClearSolo()
        {
            this.Soloed = false;
            SetMute(this.Muted);
        }

#if UNITY_EDITOR

        public void DisplayProperties()
        {
            if (this.sources.Count == 0 || this.sources[0] == null || this.sources[0].source == null)
            {
                return;
            }

            if (emitterTransform != null)
            {
                EditorGUILayout.LabelField("Emitter:" + emitterTransform.gameObject.name);
            }
            else
            {
                EditorGUILayout.LabelField("Emitter:" + this.sources[0].source.gameObject.name);
            }
        }

        protected void DrawWindow(int id)
        {
            GUI.DragWindow();
            DisplayProperties();
        }

        public virtual void DrawNode(int id, Rect window)
        {
            GUI.Window(id, window, DrawWindow, this.name);
        }

#endif

        #endregion
    }
    public class EventSource
    {
        public AudioSource source = null;
        public AudioParameter parameter = null;
        public AnimationCurve responseCurve = null;
        public float startTime = 0;
    }

    public enum EventStatus
    {
        Initialized,
        Played,
        Stopped,
        Error
    }
}