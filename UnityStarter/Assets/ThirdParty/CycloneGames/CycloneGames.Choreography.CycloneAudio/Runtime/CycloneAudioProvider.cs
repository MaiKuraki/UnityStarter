using System;
using System.Collections.Generic;
using CycloneGames.Audio.Runtime;
using CycloneGames.Choreography.Core;
using UnityEngine;

namespace CycloneGames.Choreography.CycloneAudio
{
    /// <summary>
    /// Choreography audio provider for CycloneGames.Audio AudioEvent playback.
    /// It consumes AudioEvent or BackendCue resources; banks remain owned by the host audio setup.
    /// </summary>
    public sealed class CycloneAudioProvider : IAudioProvider
    {
        public const int AbsoluteMaximumActiveHandleCount = 65_536;

        private readonly struct VoiceKey : IEquatable<VoiceKey>
        {
            public readonly int InstanceId;
            public readonly int PlaybackChannel;
            public readonly int ClipChannel;
            public readonly string ClipId;

            public VoiceKey(int instanceId, int playbackChannel, int clipChannel, string clipId)
            {
                InstanceId = instanceId;
                PlaybackChannel = playbackChannel;
                ClipChannel = clipChannel;
                ClipId = clipId;
            }

            public bool Equals(VoiceKey other)
            {
                return InstanceId == other.InstanceId
                    && PlaybackChannel == other.PlaybackChannel
                    && ClipChannel == other.ClipChannel
                    && string.Equals(ClipId, other.ClipId, StringComparison.Ordinal);
            }

            public override bool Equals(object obj)
            {
                return obj is VoiceKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = InstanceId;
                    hash = (hash * 397) ^ PlaybackChannel;
                    hash = (hash * 397) ^ ClipChannel;
                    hash = (hash * 397) ^ (ClipId != null ? ClipId.GetHashCode() : 0);
                    return hash;
                }
            }
        }

        private readonly struct VoiceControl
        {
            private readonly AudioHandle handle;
            private readonly ActiveEvent fallbackEvent;
            private readonly bool usesStableHandle;

            public VoiceControl(ActiveEvent activeEvent)
            {
                AudioHandle candidate = activeEvent != null ? activeEvent.Handle : default;
                usesStableHandle = candidate.IsValid;
                handle = usesStableHandle ? candidate : default;
                fallbackEvent = usesStableHandle ? null : activeEvent;
            }

            public bool IsValid => usesStableHandle
                ? handle.IsValid
                : fallbackEvent != null
                    && fallbackEvent.status != EventStatus.Stopped
                    && fallbackEvent.status != EventStatus.Error;

            public void SetVolume(float volume)
            {
                if (usesStableHandle)
                    handle.SetVolume(volume);
                else
                    fallbackEvent?.SetVolume(volume);
            }

            public void Stop()
            {
                if (usesStableHandle)
                    handle.Stop();
                else
                    fallbackEvent?.Stop();
            }
        }

        private readonly IAudioService _audioService;
        private readonly GameObject _defaultEmitter;
        private readonly IChoreographyDiagnostics _diagnostics;
        private readonly ICycloneAudioBankState _bankState;
        private readonly int _maximumActiveHandleCount;
        private readonly Dictionary<VoiceKey, VoiceControl> _voices = new Dictionary<VoiceKey, VoiceControl>(16);
        private bool _warnedMissingEvent;
        private bool _warnedMissingBank;
        private bool _warnedUnsupportedKind;
        private int _pendingRequestCount;
        private int _peakActiveHandleCount;
        private int _peakPendingRequestCount;
        private long _playbackRequestCount;
        private long _successfulRequestCount;
        private long _failedRequestCount;
        private long _rejectedRequestCount;
        private long _releasedHandleCount;

        /// <summary>Creates a provider with the compatibility default active-handle ceiling.</summary>
        public CycloneAudioProvider(
            IAudioService audioService,
            GameObject defaultEmitter,
            IChoreographyDiagnostics diagnostics = null,
            ICycloneAudioBankState bankState = null)
            : this(audioService, defaultEmitter, diagnostics, bankState, 1_024)
        {
        }

        /// <summary>Creates a provider with an explicit active-handle ceiling.</summary>
        public CycloneAudioProvider(
            IAudioService audioService,
            GameObject defaultEmitter,
            IChoreographyDiagnostics diagnostics,
            ICycloneAudioBankState bankState,
            int maximumActiveHandleCount)
        {
            if (maximumActiveHandleCount <= 0 || maximumActiveHandleCount > AbsoluteMaximumActiveHandleCount)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumActiveHandleCount));
            }

            _audioService = audioService ?? throw new ArgumentNullException(nameof(audioService));
            _defaultEmitter = defaultEmitter;
            _diagnostics = diagnostics ?? NullChoreographyDiagnostics.Instance;
            _bankState = bankState;
            _maximumActiveHandleCount = maximumActiveHandleCount;
        }

        public ChoreographyCycloneAudioMemoryStats GetMemoryStats()
        {
            return new ChoreographyCycloneAudioMemoryStats(
                _voices.Count,
                _maximumActiveHandleCount,
                _pendingRequestCount,
                _peakActiveHandleCount,
                _peakPendingRequestCount,
                _playbackRequestCount,
                _successfulRequestCount,
                _failedRequestCount,
                _rejectedRequestCount,
                _releasedHandleCount);
        }

        public void BeginClip(in ChoreographyPlaybackSample sample)
        {
            _playbackRequestCount++;
            ChoreographyClip clip = sample.Clip;
            ChoreographyResourceReference reference = clip.Resource;
            if (reference.Kind != ChoreographyResourceKind.AudioEvent
                && reference.Kind != ChoreographyResourceKind.BackendCue
                && reference.Kind != ChoreographyResourceKind.Generic)
            {
                _failedRequestCount++;
                WarnUnsupportedKind(clip.Id, reference.Kind);
                return;
            }

            string eventName = ResolveEventName(clip);
            if (string.IsNullOrEmpty(eventName))
            {
                _failedRequestCount++;
                WarnMissingEvent(clip.Id, reference.Group, eventName);
                return;
            }

            if (!IsBankReady(reference.Group, clip.Id, eventName))
            {
                _failedRequestCount++;
                return;
            }

            bool trackHandle = clip.HasDuration || clip.Loop;
            VoiceKey key = trackHandle
                ? new VoiceKey(sample.InstanceId, sample.PlaybackChannel, sample.ClipChannel, clip.Id)
                : default;
            bool replacesTrackedHandle = trackHandle && _voices.ContainsKey(key);
            if (trackHandle && !replacesTrackedHandle && _voices.Count >= _maximumActiveHandleCount)
            {
                _rejectedRequestCount++;
                _failedRequestCount++;
                return;
            }

            _pendingRequestCount++;
            if (_pendingRequestCount > _peakPendingRequestCount)
            {
                _peakPendingRequestCount = _pendingRequestCount;
            }
            ActiveEvent activeEvent;
            try
            {
                activeEvent = _audioService.PlayEvent(eventName, _defaultEmitter);
            }
            catch
            {
                _failedRequestCount++;
                throw;
            }
            finally
            {
                _pendingRequestCount--;
            }
            if (activeEvent == null)
            {
                _failedRequestCount++;
                WarnMissingEvent(clip.Id, reference.Group, eventName);
                return;
            }

            _successfulRequestCount++;
            activeEvent.SetVolume(Clamp01(sample.Weight));
            if (trackHandle)
            {
                if (replacesTrackedHandle && _voices.TryGetValue(key, out VoiceControl previous))
                {
                    previous.Stop();
                    _releasedHandleCount++;
                }
                _voices[key] = new VoiceControl(activeEvent);
                if (_voices.Count > _peakActiveHandleCount)
                {
                    _peakActiveHandleCount = _voices.Count;
                }
            }
        }

        public void UpdateClip(in ChoreographyPlaybackSample sample)
        {
            VoiceKey key = new VoiceKey(sample.InstanceId, sample.PlaybackChannel, sample.ClipChannel, sample.Clip.Id);
            if (_voices.TryGetValue(key, out VoiceControl voice))
            {
                if (voice.IsValid)
                {
                    voice.SetVolume(Clamp01(sample.Weight));
                }
                else
                {
                    _voices.Remove(key);
                    _releasedHandleCount++;
                }
            }
        }

        public void EndClip(in ChoreographyClipStop stop)
        {
            VoiceKey key = new VoiceKey(stop.InstanceId, stop.PlaybackChannel, stop.ClipChannel, stop.ClipId);
            if (_voices.TryGetValue(key, out VoiceControl voice))
            {
                _voices.Remove(key);
                voice.Stop();
                _releasedHandleCount++;
            }
        }

        public void StopAll()
        {
            int released = _voices.Count;
            foreach (KeyValuePair<VoiceKey, VoiceControl> pair in _voices)
            {
                pair.Value.Stop();
            }

            _voices.Clear();
            _releasedHandleCount += released;
        }

        private static string ResolveEventName(ChoreographyClip clip)
        {
            ChoreographyResourceReference reference = clip.Resource;
            if (!string.IsNullOrEmpty(reference.Address))
            {
                return reference.Address;
            }

            return clip.Id;
        }

        private bool IsBankReady(string bankId, string clipId, string eventName)
        {
            if (string.IsNullOrEmpty(bankId) || _bankState == null || _bankState.IsBankLoaded(bankId))
            {
                return true;
            }

            WarnMissingBank(clipId, bankId, eventName);
            return false;
        }

        private void WarnMissingBank(string clipId, string bank, string eventName)
        {
            if (!_warnedMissingBank && _diagnostics.IsEnabled(ChoreographyLogLevel.Warning))
            {
                _warnedMissingBank = true;
                _diagnostics.Log(ChoreographyLogLevel.Warning, "Choreography.CycloneAudio",
                    "Audio event '" + eventName + "' for clip '" + clipId + "' skipped because bank '" + bank + "' is not loaded. Preload and load the bank before playback. Further bank warnings are suppressed.");
            }
        }

        private void WarnMissingEvent(string clipId, string bank, string eventName)
        {
            if (!_warnedMissingEvent && _diagnostics.IsEnabled(ChoreographyLogLevel.Warning))
            {
                _warnedMissingEvent = true;
                string bankHint = string.IsNullOrEmpty(bank) ? string.Empty : " Bank '" + bank + "' may not be loaded.";
                _diagnostics.Log(ChoreographyLogLevel.Warning, "Choreography.CycloneAudio",
                    "Audio event '" + eventName + "' for clip '" + clipId + "' could not be played." + bankHint + " Further audio event warnings are suppressed.");
            }
        }

        private void WarnUnsupportedKind(string clipId, ChoreographyResourceKind kind)
        {
            if (!_warnedUnsupportedKind && _diagnostics.IsEnabled(ChoreographyLogLevel.Warning))
            {
                _warnedUnsupportedKind = true;
                _diagnostics.Log(ChoreographyLogLevel.Warning, "Choreography.CycloneAudio",
                    "Audio clip '" + clipId + "' skipped: CycloneAudioProvider only supports AudioEvent or BackendCue resources, but received '" + kind + "'. Further audio kind warnings are suppressed.");
            }
        }

        private static float Clamp01(float value)
        {
            if (value < 0f)
            {
                return 0f;
            }

            return value > 1f ? 1f : value;
        }
    }
}
