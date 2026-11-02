// Copyright (c) CycloneGames
// Licensed under the MIT License.

using UnityEngine;

namespace CycloneGames.Audio.Runtime
{
    /// <summary>
    /// Distance-based virtualisation for looping 3D events.
    /// </summary>
    /// <remarks>
    /// A virtualised loop keeps its logical identity and playback position but returns its
    /// <see cref="AudioSource"/> to the pool, so a large number of distant ambient emitters cannot
    /// exhaust the voice budget. Only single-source loops are virtualised; layered/music events stay
    /// audible because resuming them out of phase would be detectable.
    /// </remarks>
    public partial class ActiveEvent
    {
        private const byte VirtualStateAudible = 0;
        private const byte VirtualStateFadingOut = 1;
        private const byte VirtualStateVirtualized = 2;
        private const byte VirtualStateFadingIn = 3;

        private struct VirtualTrack
        {
            public AudioClip Clip;
            public IAudioClipHandle Handle;
            public float Playhead;
        }

        private VirtualTrack[] virtualTracks;
        private byte virtualizationState;
        private float virtualFade = 1f;
        private float virtualFadeStep;
        private float virtualBaseVolume = 1f;
        private float lastAudibleTime;
        private float virtualElapsed;
        private bool spatialDistanceValid;

        /// <summary>
        /// Computes the listener distance for virtualisation and advances the state machine. Runs on
        /// every update tick, including while the loop holds no source.
        /// </summary>
        private void TickVirtualization(float dt)
        {
            if (rootEvent == null || rootEvent.Output == null || !rootEvent.Output.loop) return;
            if (!is3D) return;
            if (sourceCount != 1 && virtualizationState != VirtualStateVirtualized) return;

            float sqrDistance = (lastEmitterPos - AudioManager.GetReferenceListenerPosition()).sqrMagnitude;
            cachedSqrDistance = sqrDistance;
            spatialDistanceValid = true;

            UpdateVirtualization(dt, sqrDistance);
        }

        /// <summary>Whether the loop currently holds no <see cref="AudioSource"/>.</summary>
        internal bool IsVirtualized => virtualizationState == VirtualStateVirtualized;

        /// <summary>Diagnostic state code: 0 audible, 1 fading out, 2 virtualized, 3 fading in.</summary>
        internal byte VirtualizationStateCode => virtualizationState;

        /// <summary>Diagnostic: whether a clip is retained for the next resume.</summary>
        internal bool HasRetainedVirtualClip => virtualTracks != null && virtualTracks[0].Clip != null;

        /// <summary>Diagnostic: playhead computed for the most recent resume.</summary>
        internal float LastResumePlayhead { get; private set; }

        /// <summary>
        /// Set while the loop budget keeps this loop silent even though it is inside the audible
        /// radius. Cleared by the next budget pass that ranks it among the allowed loops.
        /// </summary>
        internal bool BudgetHold { get; private set; }

        internal void MarkBudgetHold()
        {
            BudgetHold = true;
        }

        internal void ClearBudgetHold()
        {
            BudgetHold = false;
        }

        /// <summary>Loop that participates in the audible budget, whether audible or already silent.</summary>
        internal bool IsLoopBudgetCandidate
        {
            get
            {
                if (rootEvent == null || rootEvent.Output == null || !rootEvent.Output.loop) return false;
                if (!is3D || status != EventStatus.Played) return false;
                if (!spatialDistanceValid) return false;
                return sourceCount == 1 || virtualizationState == VirtualStateVirtualized;
            }
        }

        /// <summary>Whether this event is a single-source 3D loop that could be virtualised.</summary>
        internal bool IsVirtualizableLoop
        {
            get
            {
                if (rootEvent == null || rootEvent.Output == null || !rootEvent.Output.loop) return false;
                if (!is3D || sourceCount != 1 || status != EventStatus.Played) return false;
                if (!spatialDistanceValid) return false;
                return virtualizationState == VirtualStateAudible;
            }
        }

        /// <summary>Listener distance recorded on the last spatial tick.</summary>
        internal float VirtualizationSqrDistance => cachedSqrDistance;

        /// <summary>
        /// Advances the virtualisation state machine. Called from the spatial LOD tick with the
        /// distance already computed for attenuation and occlusion.
        /// </summary>
        internal void UpdateVirtualization(float dt, float sqrDistance)
        {
            AudioVirtualizationSettings settings = AudioManager.LoopVirtualization;
            if (!settings.Enabled || dt <= 0f) return;
            if (rootEvent == null || rootEvent.Output == null || !rootEvent.Output.loop) return;
            if (!is3D) return;
            if (sourceCount != 1 && virtualizationState != VirtualStateVirtualized) return;

            switch (virtualizationState)
            {
                case VirtualStateAudible:
                    if (sqrDistance <= settings.AudibleRadiusSqr)
                    {
                        lastAudibleTime = elapsedTime;
                        return;
                    }

                    if (elapsedTime - lastAudibleTime < settings.MinimumDwellSeconds) return;
                    BeginFadeOut(settings.FadeSeconds);
                    break;

                case VirtualStateFadingOut:
                    virtualFade -= virtualFadeStep * dt;
                    if (virtualFade > 0f)
                    {
                        ApplyVirtualFade();
                        return;
                    }

                    virtualFade = 0f;
                    DetachForVirtualization();
                    virtualizationState = VirtualStateVirtualized;
                    virtualElapsed = 0f;
                    AudioManager.NotifyLoopVirtualized();
                    break;

                case VirtualStateVirtualized:
                    // Accumulate with the simulation clock so the playhead does not depend on when a
                    // frame happens to run (tooling, tests and paused rendering all differ).
                    virtualElapsed += dt;
                    if (BudgetHold) return;
                    if (sqrDistance > settings.ResumeRadiusSqr) return;
                    if (!AudioManager.TryReserveLoopResume()) return;
                    if (!TryReattachForVirtualization()) return;
                    virtualFade = 0f;
                    virtualFadeStep = 1f / Mathf.Max(0.001f, settings.FadeSeconds);
                    virtualizationState = VirtualStateFadingIn;
                    ApplyVirtualFade();
                    AudioManager.NotifyLoopResumed();
                    break;

                case VirtualStateFadingIn:
                    virtualFade += virtualFadeStep * dt;
                    if (virtualFade < 1f)
                    {
                        ApplyVirtualFade();
                        return;
                    }

                    virtualFade = 1f;
                    lastAudibleTime = elapsedTime;
                    virtualizationState = VirtualStateAudible;
                    break;
            }
        }

        /// <summary>Starts a fade-out so a budget pass can reclaim the voice smoothly.</summary>
        internal bool TryBeginVirtualization(float fadeSeconds)
        {
            if (!IsVirtualizableLoop) return false;
            BudgetHold = true;
            BeginFadeOut(fadeSeconds);
            return true;
        }

        private void BeginFadeOut(float fadeSeconds)
        {
            virtualBaseVolume = sourceCount > 0 && sourcesArray[0].source != null
                ? sourcesArray[0].source.volume
                : 1f;
            virtualFade = 1f;
            virtualFadeStep = 1f / Mathf.Max(0.001f, fadeSeconds);
            virtualizationState = VirtualStateFadingOut;
        }

        private void DetachForVirtualization()
        {
            if (virtualTracks == null) virtualTracks = new VirtualTrack[1];

            ref EventSource slot = ref sourcesArray[0];
            virtualTracks[0].Clip = slot.source != null ? slot.source.clip : null;
            virtualTracks[0].Handle = slot.clipHandle;
            virtualTracks[0].Playhead = slot.source != null ? slot.source.time : 0f;

            AudioSource source = slot.source;
            slot.source = null;
            slot.clipHandle = null;
            slot.parameter = null;
            slot.responseCurve = null;
            sourceCount = 0;

            if (source != null) AudioManager.ReturnSourceToPool(source);
        }

        private bool TryReattachForVirtualization()
        {
            if (virtualTracks == null) return false;

            AudioClip clip = virtualTracks[0].Clip;
            if (clip == null) return false;

            AudioSource source = AudioManager.GetUnusedSource(rootEvent, this);
            if (source == null) return false;

            float length = clip.length;
            float playhead = length > 0.01f ? Mathf.Repeat(virtualTracks[0].Playhead + virtualElapsed, length) : 0f;

            source.clip = clip;
            source.loop = true;
            if (length > 0.01f)
            {
                source.time = Mathf.Clamp(playhead, 0f, length - 0.01f);
            }

            LastResumePlayhead = playhead;

            source.volume = 0f;
            source.Play();

            ref EventSource slot = ref sourcesArray[0];
            slot.source = source;
            slot.clipHandle = virtualTracks[0].Handle;
            slot.startTime = 0f;
            sourceCount = 1;
            ApplyParameters();
            return true;
        }

        private void ApplyVirtualFade()
        {
            if (sourceCount == 0) return;

            AudioSource source = sourcesArray[0].source;
            if (source == null) return;

            source.volume = Mathf.Clamp01(virtualBaseVolume * virtualFade);
        }

        /// <summary>Releases retained clips and returns to the audible state.</summary>
        private void ResetVirtualization()
        {
            if (virtualTracks != null)
            {
                for (int i = 0; i < virtualTracks.Length; i++)
                {
                    if (virtualTracks[i].Handle != null)
                    {
                        AudioClipHandleRelease.Safe(virtualTracks[i].Handle);
                        virtualTracks[i].Handle = null;
                    }

                    virtualTracks[i].Clip = null;
                    virtualTracks[i].Playhead = 0f;
                }
            }

            virtualizationState = VirtualStateAudible;
            virtualFade = 1f;
            virtualFadeStep = 0f;
            virtualBaseVolume = 1f;
            lastAudibleTime = 0f;
            virtualElapsed = 0f;
            BudgetHold = false;
            LastResumePlayhead = 0f;
        }
    }
}
