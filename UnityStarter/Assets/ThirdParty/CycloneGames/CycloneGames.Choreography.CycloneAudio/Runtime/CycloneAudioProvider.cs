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

        private readonly IAudioService _audioService;
        private readonly GameObject _defaultEmitter;
        private readonly IChoreographyDiagnostics _diagnostics;
        private readonly ICycloneAudioBankState _bankState;
        private readonly Dictionary<VoiceKey, ActiveEvent> _voices = new Dictionary<VoiceKey, ActiveEvent>(16);
        private bool _warnedMissingEvent;
        private bool _warnedMissingBank;
        private bool _warnedUnsupportedKind;

        public CycloneAudioProvider(
            IAudioService audioService,
            GameObject defaultEmitter,
            IChoreographyDiagnostics diagnostics = null,
            ICycloneAudioBankState bankState = null)
        {
            _audioService = audioService ?? throw new ArgumentNullException(nameof(audioService));
            _defaultEmitter = defaultEmitter;
            _diagnostics = diagnostics ?? NullChoreographyDiagnostics.Instance;
            _bankState = bankState;
        }

        public void BeginClip(in ChoreographyPlaybackSample sample)
        {
            ChoreographyClip clip = sample.Clip;
            ChoreographyResourceReference reference = clip.Resource;
            if (reference.Kind != ChoreographyResourceKind.AudioEvent
                && reference.Kind != ChoreographyResourceKind.BackendCue
                && reference.Kind != ChoreographyResourceKind.Generic)
            {
                WarnUnsupportedKind(clip.Id, reference.Kind);
                return;
            }

            string eventName = ResolveEventName(clip);
            if (string.IsNullOrEmpty(eventName))
            {
                WarnMissingEvent(clip.Id, reference.Group, eventName);
                return;
            }

            if (!IsBankReady(reference.Group, clip.Id, eventName))
            {
                return;
            }

            ActiveEvent activeEvent = _audioService.PlayEvent(eventName, _defaultEmitter);
            if (activeEvent == null)
            {
                WarnMissingEvent(clip.Id, reference.Group, eventName);
                return;
            }

            activeEvent.SetVolume(Clamp01(sample.Weight));
            if (clip.HasDuration || clip.Loop)
            {
                _voices[new VoiceKey(sample.InstanceId, sample.PlaybackChannel, sample.ClipChannel, clip.Id)] = activeEvent;
            }
        }

        public void UpdateClip(in ChoreographyPlaybackSample sample)
        {
            VoiceKey key = new VoiceKey(sample.InstanceId, sample.PlaybackChannel, sample.ClipChannel, sample.Clip.Id);
            if (_voices.TryGetValue(key, out ActiveEvent activeEvent) && activeEvent != null)
            {
                activeEvent.SetVolume(Clamp01(sample.Weight));
            }
        }

        public void EndClip(in ChoreographyClipStop stop)
        {
            VoiceKey key = new VoiceKey(stop.InstanceId, stop.PlaybackChannel, stop.ClipChannel, stop.ClipId);
            if (_voices.TryGetValue(key, out ActiveEvent activeEvent))
            {
                _voices.Remove(key);
                activeEvent?.Stop();
            }
        }

        public void StopAll()
        {
            foreach (KeyValuePair<VoiceKey, ActiveEvent> pair in _voices)
            {
                pair.Value?.Stop();
            }

            _voices.Clear();
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
