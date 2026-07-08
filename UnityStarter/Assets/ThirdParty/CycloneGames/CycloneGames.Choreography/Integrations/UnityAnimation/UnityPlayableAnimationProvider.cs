using System;
using System.Collections.Generic;
using CycloneGames.Choreography.Core;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace CycloneGames.Choreography.UnityAnimation
{
    /// <summary>
    /// Default Animator/PlayableGraph-backed animation provider. It samples AnimationClip resources from
    /// Choreography local time so animation, events, audio, and VFX can share the same timeline authority.
    /// </summary>
    public sealed class UnityPlayableAnimationProvider : IAnimationProvider, IDisposable
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

        private struct ActiveClip
        {
            public AnimationClip Clip;
            public AnimationClipPlayable Playable;
            public int InputIndex;
        }

        private readonly Animator _animator;
        private readonly IUnityChoreographyResourceResolver _resolver;
        private readonly IChoreographyDiagnostics _diagnostics;
        private readonly bool _evaluateImmediately;
        private readonly Dictionary<VoiceKey, ActiveClip> _voices;
        private readonly Stack<int> _freeInputs;
        private PlayableGraph _graph;
        private AnimationMixerPlayable _mixer;
        private int _nextInput;
        private bool _dirty;
        private bool _warnedMissingResource;
        private bool _warnedUnsupportedKind;

        public UnityPlayableAnimationProvider(
            Animator animator,
            IUnityChoreographyResourceResolver resolver,
            IChoreographyDiagnostics diagnostics = null,
            bool evaluateImmediately = true,
            int initialCapacity = 4)
        {
            _animator = animator != null ? animator : throw new ArgumentNullException(nameof(animator));
            _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
            _diagnostics = diagnostics ?? NullChoreographyDiagnostics.Instance;
            _evaluateImmediately = evaluateImmediately;
            int capacity = initialCapacity > 0 ? initialCapacity : 4;
            _voices = new Dictionary<VoiceKey, ActiveClip>(capacity);
            _freeInputs = new Stack<int>(capacity);
        }

        public void BeginClip(in ChoreographyPlaybackSample sample)
        {
            ChoreographyClip clip = sample.Clip;
            ChoreographyResourceReference reference = clip.Resource;
            if (reference.Kind != ChoreographyResourceKind.Animation && reference.Kind != ChoreographyResourceKind.Generic)
            {
                WarnUnsupportedKind(clip.Id, reference.Kind);
                return;
            }

            if (!_resolver.TryGetAsset(in reference, out AnimationClip animationClip) || animationClip == null)
            {
                WarnMissingResource(clip.Id, reference.Address);
                return;
            }

            EnsureGraph();
            VoiceKey key = new VoiceKey(sample.InstanceId, sample.PlaybackChannel, sample.ClipChannel, clip.Id);
            RemoveVoice(key);

            int inputIndex = AllocateInput();
            AnimationClipPlayable playable = AnimationClipPlayable.Create(_graph, animationClip);
            playable.SetApplyFootIK(false);
            playable.SetApplyPlayableIK(false);
            playable.SetSpeed(0d);
            playable.SetTime(ClampLocalTime(sample.LocalTime, animationClip));

            EnsureInputCapacity(inputIndex + 1);
            _graph.Connect(playable, 0, _mixer, inputIndex);
            _mixer.SetInputWeight(inputIndex, Clamp01(sample.Weight));
            _voices[key] = new ActiveClip
            {
                Clip = animationClip,
                Playable = playable,
                InputIndex = inputIndex
            };
            MarkDirty();
        }

        public void UpdateClip(in ChoreographyPlaybackSample sample)
        {
            VoiceKey key = new VoiceKey(sample.InstanceId, sample.PlaybackChannel, sample.ClipChannel, sample.Clip.Id);
            if (!_voices.TryGetValue(key, out ActiveClip active))
            {
                return;
            }

            if (active.Playable.IsValid())
            {
                active.Playable.SetTime(ClampLocalTime(sample.LocalTime, active.Clip));
            }

            if (_mixer.IsValid())
            {
                _mixer.SetInputWeight(active.InputIndex, Clamp01(sample.Weight));
            }
            MarkDirty();
        }

        public void EndClip(in ChoreographyClipStop stop)
        {
            RemoveVoice(new VoiceKey(stop.InstanceId, stop.PlaybackChannel, stop.ClipChannel, stop.ClipId));
            MarkDirty();
        }

        public void Evaluate()
        {
            if (!_dirty || !_graph.IsValid())
            {
                return;
            }

            _graph.Evaluate(0f);
            _dirty = false;
        }

        public void StopAll()
        {
            foreach (KeyValuePair<VoiceKey, ActiveClip> pair in _voices)
            {
                DestroyPlayable(pair.Value);
            }

            _voices.Clear();
            _freeInputs.Clear();
            _nextInput = 0;
            if (_mixer.IsValid())
            {
                _mixer.SetInputCount(0);
            }
            MarkDirty();
        }

        public void Dispose()
        {
            StopAll();
            if (_graph.IsValid())
            {
                _graph.Destroy();
            }
        }

        private void EnsureGraph()
        {
            if (_graph.IsValid())
            {
                return;
            }

            _graph = PlayableGraph.Create("Choreography Unity Animation");
            _graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            _mixer = AnimationMixerPlayable.Create(_graph, 0, true);
            AnimationPlayableOutput output = AnimationPlayableOutput.Create(_graph, "Choreography", _animator);
            output.SetSourcePlayable(_mixer);
            _graph.Play();
        }

        private int AllocateInput()
        {
            return _freeInputs.Count > 0 ? _freeInputs.Pop() : _nextInput++;
        }

        private void EnsureInputCapacity(int count)
        {
            if (_mixer.GetInputCount() < count)
            {
                _mixer.SetInputCount(count);
            }
        }

        private void RemoveVoice(VoiceKey key)
        {
            if (!_voices.TryGetValue(key, out ActiveClip active))
            {
                return;
            }

            _voices.Remove(key);
            DestroyPlayable(active);
            _freeInputs.Push(active.InputIndex);
        }

        private void DestroyPlayable(ActiveClip active)
        {
            if (_mixer.IsValid() && active.InputIndex >= 0 && active.InputIndex < _mixer.GetInputCount())
            {
                _mixer.SetInputWeight(active.InputIndex, 0f);
                _graph.Disconnect(_mixer, active.InputIndex);
            }

            if (active.Playable.IsValid())
            {
                active.Playable.Destroy();
            }
        }

        private void MarkDirty()
        {
            _dirty = true;
            if (_evaluateImmediately)
            {
                Evaluate();
            }
        }

        private void WarnMissingResource(string clipId, string address)
        {
            if (!_warnedMissingResource && _diagnostics.IsEnabled(ChoreographyLogLevel.Warning))
            {
                _warnedMissingResource = true;
                _diagnostics.Log(ChoreographyLogLevel.Warning, "Choreography.UnityAnimation",
                    "Animation clip '" + clipId + "' skipped: resource '" + address + "' is not loaded. Further animation resource warnings are suppressed.");
            }
        }

        private void WarnUnsupportedKind(string clipId, ChoreographyResourceKind kind)
        {
            if (!_warnedUnsupportedKind && _diagnostics.IsEnabled(ChoreographyLogLevel.Warning))
            {
                _warnedUnsupportedKind = true;
                _diagnostics.Log(ChoreographyLogLevel.Warning, "Choreography.UnityAnimation",
                    "Animation clip '" + clipId + "' skipped: UnityPlayableAnimationProvider only supports Animation resources, but received '" + kind + "'. Further animation kind warnings are suppressed.");
            }
        }

        private static double ClampLocalTime(double localTime, AnimationClip clip)
        {
            if (double.IsNaN(localTime) || double.IsInfinity(localTime) || localTime < 0d)
            {
                return 0d;
            }

            if (clip == null || clip.length <= 0f)
            {
                return localTime;
            }

            double maxTime = clip.length;
            return localTime > maxTime ? maxTime : localTime;
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
