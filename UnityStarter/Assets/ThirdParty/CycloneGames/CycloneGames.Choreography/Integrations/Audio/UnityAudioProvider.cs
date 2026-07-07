using System;
using System.Collections.Generic;
using CycloneGames.Choreography.Core;
using UnityEngine;

namespace CycloneGames.Choreography.Audio
{
    /// <summary>
    /// Reference <see cref="IAudioProvider"/> built on Unity's built-in <see cref="AudioSource"/>/<see cref="AudioClip"/>.
    /// It is intended for demos, small projects, or direct AudioClip playback; event-based audio middleware should
    /// use a dedicated provider instead.
    ///
    /// Behavior:
    /// - Clips are resolved through <see cref="IUnityChoreographyResourceResolver"/> (typically preloaded). A missing
    ///   clip or resolver produces a throttled warning and the voice is skipped; it never throws.
    /// - One-shot clips (no duration and no loop) play via <see cref="AudioSource.PlayOneShot"/> on a pooled source
    ///   and are reclaimed by <see cref="ReclaimFinishedOneShots"/>.
    /// - Duration/looping clips reserve a pooled source, tracked by (instance, playback channel, clip channel, clip) so per-tick weight
    ///   changes map to volume and <see cref="EndClip"/> stops and recycles the source.
    /// - The resolved <see cref="ChoreographyPlaybackSample.Weight"/> is applied directly as volume, so Blend
    ///   normalizes across the channel, Additive stacks, and Priority/Override mute non-dominant voices (weight 0).
    /// </summary>
    public sealed class UnityAudioProvider : IAudioProvider
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

            public override bool Equals(object obj) => obj is VoiceKey other && Equals(other);

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

        private readonly IUnityChoreographyResourceResolver _resolver;
        private readonly IChoreographyDiagnostics _diagnostics;
        private readonly AudioSourcePool _pool;
        private readonly Dictionary<VoiceKey, AudioSource> _voices = new Dictionary<VoiceKey, AudioSource>();
        private readonly List<AudioSource> _oneShots = new List<AudioSource>(8);
        private bool _warnedMissingResource;
        private bool _warnedUnsupportedKind;

        public UnityAudioProvider(
            Transform poolRoot,
            IUnityChoreographyResourceResolver resolver,
            IChoreographyDiagnostics diagnostics = null,
            int initialPoolSize = 8)
        {
            if (poolRoot == null)
            {
                throw new ArgumentNullException(nameof(poolRoot));
            }
            _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
            _diagnostics = diagnostics ?? NullChoreographyDiagnostics.Instance;
            _pool = new AudioSourcePool(poolRoot, initialPoolSize);
        }

        public void BeginClip(in ChoreographyPlaybackSample sample)
        {
            ReclaimFinishedOneShots();

            ChoreographyClip clip = sample.Clip;
            ChoreographyResourceReference reference = clip.Resource;
            if (reference.Kind != ChoreographyResourceKind.AudioClip && reference.Kind != ChoreographyResourceKind.Generic)
            {
                WarnUnsupportedKind(clip.Id, reference.Kind);
                return;
            }

            if (!_resolver.TryGetAsset(in reference, out AudioClip audioClip) || audioClip == null)
            {
                WarnMissing(clip.Id, reference.Address);
                return;
            }

            float volume = Clamp01(sample.Weight);
            bool oneShot = !clip.HasDuration && !clip.Loop;

            AudioSource source = _pool.Rent();
            source.volume = volume;

            if (oneShot)
            {
                source.loop = false;
                source.clip = null;
                source.PlayOneShot(audioClip, volume);
                _oneShots.Add(source);
                return;
            }

            source.clip = audioClip;
            source.loop = clip.Loop;
            source.time = Mathf.Min((float)sample.LocalTime, audioClip.length > 0f ? audioClip.length - 0.001f : 0f);
            source.Play();
            _voices[new VoiceKey(sample.InstanceId, sample.PlaybackChannel, sample.ClipChannel, clip.Id)] = source;
        }

        public void UpdateClip(in ChoreographyPlaybackSample sample)
        {
            if (_voices.TryGetValue(new VoiceKey(sample.InstanceId, sample.PlaybackChannel, sample.ClipChannel, sample.Clip.Id), out AudioSource source) && source != null)
            {
                source.volume = Clamp01(sample.Weight);
            }
        }

        public void EndClip(in ChoreographyClipStop stop)
        {
            VoiceKey key = new VoiceKey(stop.InstanceId, stop.PlaybackChannel, stop.ClipChannel, stop.ClipId);
            if (_voices.TryGetValue(key, out AudioSource source))
            {
                _voices.Remove(key);
                _pool.Return(source);
            }
        }

        /// <summary>Recycles one-shot sources whose clip has finished. Call from the owner update loop.</summary>
        public void ReclaimFinishedOneShots()
        {
            for (int i = _oneShots.Count - 1; i >= 0; i--)
            {
                AudioSource source = _oneShots[i];
                if (source == null || !source.isPlaying)
                {
                    _oneShots.RemoveAt(i);
                    _pool.Return(source);
                }
            }
        }

        /// <summary>Stops and recycles every tracked voice. Call on teardown to avoid leaking active sources.</summary>
        public void StopAll()
        {
            foreach (KeyValuePair<VoiceKey, AudioSource> pair in _voices)
            {
                _pool.Return(pair.Value);
            }
            _voices.Clear();

            for (int i = _oneShots.Count - 1; i >= 0; i--)
            {
                _pool.Return(_oneShots[i]);
            }
            _oneShots.Clear();
        }

        private void WarnMissing(string clipId, string address)
        {
            if (!_warnedMissingResource && _diagnostics.IsEnabled(ChoreographyLogLevel.Warning))
            {
                _warnedMissingResource = true;
                _diagnostics.Log(ChoreographyLogLevel.Warning, "Choreography.Audio",
                    "Audio clip '" + clipId + "' skipped: resource '" + address + "' is not loaded (preload it first). Further audio resource warnings are suppressed.");
            }
        }

        private void WarnUnsupportedKind(string clipId, ChoreographyResourceKind kind)
        {
            if (!_warnedUnsupportedKind && _diagnostics.IsEnabled(ChoreographyLogLevel.Warning))
            {
                _warnedUnsupportedKind = true;
                _diagnostics.Log(ChoreographyLogLevel.Warning, "Choreography.Audio",
                    "Audio clip '" + clipId + "' skipped: UnityAudioProvider only supports AudioClip resources, but received '" + kind + "'. Further audio kind warnings are suppressed.");
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
