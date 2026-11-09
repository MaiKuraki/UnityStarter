using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using CycloneGames.AnimationRuntime.Core;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace CycloneGames.AnimationRuntime
{
    /// <summary>
    /// Owns one <see cref="UnityEngine.Playables.PlayableGraph"/> and turns absolute-time clip requests into a skeleton pose.
    /// It does not own a clock, does not read <c>Time.*</c>, and never writes gameplay state.
    ///
    /// Topology is fixed at construction: <c>layerCount</c> layers, each with a fixed voice capacity and one
    /// extra input reserved for an external source (a locomotion blend node). No input count changes later.
    /// Native writes are epsilon-gated, and commits are idempotent per <see cref="AnimationTick.StepIndex"/>.
    /// </summary>
    public sealed class AnimationRuntimeGraph : IDisposable
    {
        private const byte VoiceInUse = 1 << 0;
        private const byte VoiceConnected = 1 << 1;
        private const byte VoicePendingRelease = 1 << 2;

        // Profiler markers are free when no profiler is attached, and answer "where did the frame go" without
        // adding temporary instrumentation to shipping code.
        private static readonly ProfilerMarker CommitMarker = new ProfilerMarker("AnimationRuntime.Commit");

        private struct Voice
        {
            public Playable Playable;
            public AnimationClip Clip;
            public AnimationClip BoundClip;
            public int InstanceId;
            public int PlaybackChannel;
            public double Time;
            public float Weight;
            public float AppliedWeight;
            public float Length;
            public ushort Generation;
            public byte Flags;
        }

        private struct Layer
        {
            public AnimationMixerPlayable Mixer;
            public Playable ExternalSource;
            public AvatarMask Mask;
            public Voice[] Voices;
            public float Weight;
            public float AppliedWeight;
            public float ExternalWeight;
            public float AppliedExternalWeight;
            public byte Capacity;
            public byte ActiveVoices;
            public bool Additive;
            public bool ApplyFootIK;
            public bool ApplyPlayableIK;
            public bool HasExternalSource;
            public bool ExternalConnected;
        }

        private Animator _animator;
        private readonly IAnimationDiagnostics _diagnostics;
        private readonly Layer[] _layers;
        private readonly int _layerCount;
        private readonly AnimationLayerDescriptor[] _descriptors;

        private PlayableGraph _graph;
        private AnimationLayerMixerPlayable _topMixer;
        private bool _initialized;
        private int _graphId;
        private long _lastStepIndex = long.MinValue;
        private long _droppedVoices;
        private long _recreatedPlayables;
        private long _commits;
        private long _nativeWrites;
        private int _activeVoices;
        private int _peakVoices;
        private bool _warnedRigMismatch;

        public AnimationRuntimeGraph(
            Animator animator,
            IReadOnlyList<AnimationLayerDescriptor> layers,
            IAnimationDiagnostics diagnostics = null)
        {
            if (animator == null)
            {
                throw new ArgumentNullException(nameof(animator));
            }

            _animator = animator;
            _diagnostics = diagnostics ?? NullAnimationDiagnostics.Instance;

            int count = layers != null ? layers.Count : 0;
            if (count <= 0)
            {
                _descriptors = new[] { AnimationLayerDescriptor.Default };
            }
            else
            {
                if (count > AnimationRuntimeLimits.MaxLayers)
                {
                    count = AnimationRuntimeLimits.MaxLayers;
                }

                _descriptors = new AnimationLayerDescriptor[count];
                for (int i = 0; i < count; i++)
                {
                    _descriptors[i] = layers[i];
                }
            }

            _layerCount = _descriptors.Length;
            _layers = new Layer[_layerCount];
            Initialize();
        }

        /// <summary>False before <see cref="Initialize"/> or after <see cref="Dispose"/>.</summary>
        public bool IsInitialized => _initialized && _graph.IsValid();

        public int LayerCount => _layerCount;

        /// <summary>The Animator this graph writes to. Changes only through <see cref="Rebind"/>.</summary>
        public Animator TargetAnimator => _animator;

        /// <summary>Instance id of the current graph. Bumped on every rebuild.</summary>
        public int GraphId => _graphId;

        public int PlayableCount => _graph.IsValid() ? _graph.GetPlayableCount() : 0;

        /// <summary>
        /// The underlying graph. Exposed so a host can create an auxiliary node (a locomotion blend) and hand it
        /// to <see cref="SetLayerSource"/>, which requires both nodes to live in the same graph. Treat it as
        /// read-only: connecting or destroying nodes outside this API breaks the fixed-topology contract.
        /// </summary>
        public PlayableGraph PlayableGraph => _graph;

        /// <summary>Size of the internal voice struct. Guarded by a test so layout regressions fail CI.</summary>
        public static int VoiceStructSize => Unsafe.SizeOf<Voice>();

        // ------------------------------------------------------------------ lifecycle

        private void Initialize()
        {
            if (!AnimationRuntimeRegistry.TryClaimAnimator(_animator, this))
            {
                throw new InvalidOperationException(
                    "[AnimationRuntime] Another graph already drives Animator '"
                    + (_animator != null ? _animator.name : "<null>")
                    + "'. One Animator supports one animation output; dispose the other graph first.");
            }

            if (_animator.runtimeAnimatorController != null)
            {
                Warn("The Animator already has a RuntimeAnimatorController. A Playable output bypasses it, so that "
                    + "controller will not be evaluated. Move it into a layer or clear the field.");
            }

            ValidateDescriptors();

            _graph = UnityEngine.Playables.PlayableGraph.Create("CycloneGames.AnimationRuntime");
            _graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            _topMixer = AnimationLayerMixerPlayable.Create(_graph, _layerCount);

            for (int i = 0; i < _layerCount; i++)
            {
                AnimationLayerDescriptor descriptor = _descriptors[i];
                var layer = new Layer
                {
                    Capacity = descriptor.Capacity,
                    Voices = new Voice[descriptor.Capacity],
                    Mixer = AnimationMixerPlayable.Create(_graph, descriptor.Capacity + 1),
                    Mask = descriptor.Mask,
                    Weight = descriptor.Weight,
                    AppliedWeight = -1f,
                    ExternalWeight = 1f,
                    AppliedExternalWeight = -1f,
                    Additive = descriptor.Additive,
                    ApplyFootIK = descriptor.ApplyFootIK,
                    ApplyPlayableIK = descriptor.ApplyPlayableIK
                };

                if (descriptor.Mask != null)
                {
                    _topMixer.SetLayerMaskFromAvatarMask((uint)i, descriptor.Mask);
                }

                _topMixer.SetLayerAdditive((uint)i, descriptor.Additive);
                _topMixer.ConnectInput(i, layer.Mixer, 0);
                _topMixer.SetInputWeight(i, 0f);
                _layers[i] = layer;
            }

            AnimationPlayableOutput output = AnimationPlayableOutput.Create(_graph, "AnimationRuntime", _animator);
            output.SetSourcePlayable(_topMixer);
            _graph.Play();

            _graphId++;
            if (_graphId == 0)
            {
                _graphId = 1;
            }

            _lastStepIndex = long.MinValue;
            _initialized = true;
            AnimationRuntimeRegistry.OnGraphCreated();
        }

        /// <summary>Ends every voice, disconnects external sources and destroys the graph.</summary>
        public void Dispose()
        {
            if (!_initialized)
            {
                return;
            }

            StopAllInternal(true);

            if (_graph.IsValid())
            {
                _graph.Destroy();
            }

            _graph = default;
            _topMixer = default;
            _initialized = false;
            AnimationRuntimeRegistry.ReleaseAnimator(_animator, this);
            AnimationRuntimeRegistry.OnGraphDisposed();
            _graphId++;
            if (_graphId == 0)
            {
                _graphId = 1;
            }
        }

        // ------------------------------------------------------------------ voice API

        /// <summary>
        /// Rebinds to another Animator (model swap, pooled actor reuse). Ends every voice, destroys the graph and
        /// rebuilds it against the new Animator. Every outstanding handle is invalidated.
        /// </summary>
        public void Rebind(Animator animator)
        {
            if (animator == null)
            {
                throw new ArgumentNullException(nameof(animator));
            }

            if (ReferenceEquals(animator, _animator) && IsInitialized)
            {
                return;
            }

            Dispose();
            _animator = animator;
            Initialize();
        }

        /// <summary>
        /// Starts or replaces a voice. Native work happens at <see cref="Commit"/>, so this is safe to call
        /// from gameplay code without touching the Playable graph.
        /// </summary>
        public AnimationVoiceHandle BeginClip(in AnimationClipRequest request)
        {
            if (!_initialized)
            {
                return AnimationVoiceHandle.Invalid;
            }

            if (request.Clip == null)
            {
                Warn("BeginClip rejected: clip is null.");
                return AnimationVoiceHandle.Invalid;
            }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            ValidateClipForAnimator(request.Clip);
#endif

            int layerIndex = ResolveLayer(request.Layer);
            ref Layer layer = ref _layers[layerIndex];

            if (layer.HasExternalSource)
            {
                Warn("BeginClip rejected: layer is owned by an external source.");
                return AnimationVoiceHandle.Invalid;
            }

            int slot = FindVoice(ref layer, request.InstanceId, request.PlaybackChannel);
            bool isNew = slot < 0;
            if (isNew)
            {
                slot = FindFreeSlot(ref layer);
            }

            if (slot < 0)
            {
                _droppedVoices++;
                Warn("BeginClip dropped: layer voice capacity exhausted.");
                return AnimationVoiceHandle.Invalid;
            }

            ref Voice voice = ref layer.Voices[slot];

            if (isNew)
            {
                voice.Generation++;
                if (voice.Generation == 0)
                {
                    voice.Generation = 1;
                }

                voice.Flags |= VoiceInUse;
                layer.ActiveVoices++;
                _activeVoices++;
                AnimationRuntimeRegistry.OnVoiceCountChanged(1);
                if (_activeVoices > _peakVoices)
                {
                    _peakVoices = _activeVoices;
                }
            }

            voice.InstanceId = request.InstanceId;
            voice.PlaybackChannel = request.PlaybackChannel;
            voice.Clip = request.Clip;
            voice.Length = request.Clip.length;
            voice.Time = ClampTime(request.LocalTime, voice.Length);
            voice.Weight = request.Weight;
            voice.Flags &= unchecked((byte)~VoicePendingRelease);

            if (request.HoldLastFrame && voice.Length > AnimationRuntimeLimits.MinClipLength)
            {
                voice.Time = voice.Length;
            }

            return new AnimationVoiceHandle(_graphId, layerIndex, slot, voice.Generation);
        }

        /// <summary>Updates time and weight of an existing voice. Unknown requests are ignored.</summary>
        public void UpdateClip(in AnimationClipRequest request)
        {
            if (!_initialized)
            {
                return;
            }

            int layerIndex = ResolveLayer(request.Layer);
            ref Layer layer = ref _layers[layerIndex];
            int slot = FindVoice(ref layer, request.InstanceId, request.PlaybackChannel);
            if (slot < 0)
            {
                return;
            }

            ref Voice voice = ref layer.Voices[slot];
            if ((voice.Flags & VoiceInUse) == 0)
            {
                return;
            }

            voice.Time = ClampTime(request.LocalTime, voice.Length);
            voice.Weight = request.Weight;
        }

        /// <summary>Requests a voice to stop. The slot is released at the next commit.</summary>
        public void EndClip(in AnimationVoiceKey key)
        {
            if (!_initialized)
            {
                return;
            }

            int layerIndex = ResolveLayer(key.Layer);
            ref Layer layer = ref _layers[layerIndex];
            int slot = FindVoice(ref layer, key.InstanceId, key.PlaybackChannel);
            if (slot < 0)
            {
                return;
            }

            ref Voice voice = ref layer.Voices[slot];
            if ((voice.Flags & VoiceInUse) == 0)
            {
                return;
            }

            voice.Flags |= VoicePendingRelease;
            voice.Weight = 0f;
        }

        /// <summary>Releases every voice and external source. The graph stays alive and reusable.</summary>
        public void StopAll()
        {
            if (!_initialized)
            {
                return;
            }

            StopAllInternal(false);
        }

        private void StopAllInternal(bool destroyVoices)
        {
            for (int i = 0; i < _layerCount; i++)
            {
                ref Layer layer = ref _layers[i];

                for (int slot = 0; slot < layer.Capacity; slot++)
                {
                    ref Voice voice = ref layer.Voices[slot];
                    ReleaseVoice(ref layer, ref voice, slot, destroyVoices);
                }

                if (layer.ExternalConnected)
                {
                    layer.Mixer.DisconnectInput(layer.Capacity);
                    layer.ExternalConnected = false;
                }

                layer.HasExternalSource = false;
                layer.ExternalSource = default;
                layer.ExternalWeight = 1f;
                layer.AppliedExternalWeight = -1f;
                layer.ActiveVoices = 0;

                if (layer.Mixer.IsValid())
                {
                    layer.Mixer.SetInputWeight(layer.Capacity, 0f);
                }
            }

            _activeVoices = 0;
        }

        // ------------------------------------------------------------------ commit

        /// <summary>
        /// Applies pending voice changes and evaluates the graph once with a zero delta.
        /// Idempotent per <see cref="AnimationTick.StepIndex"/>.
        /// </summary>
        public void Commit(in AnimationTick tick)
        {
            if (!_initialized)
            {
                return;
            }

            if (tick.IsValid && tick.StepIndex == _lastStepIndex)
            {
                return;
            }

            if (tick.IsValid)
            {
                _lastStepIndex = tick.StepIndex;
            }

            _commits++;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if ((_commits % LivenessCheckInterval) == 0)
            {
                CheckClipLiveness();
            }
#endif

            CommitMarker.Begin();
            try
            {
                for (int i = 0; i < _layerCount; i++)
                {
                    ApplyLayer(i);
                }

                _graph.Evaluate(0f);
            }
            finally
            {
                CommitMarker.End();
            }
        }

        private void ApplyLayer(int layerIndex)
        {
            ref Layer layer = ref _layers[layerIndex];

            for (int slot = 0; slot < layer.Capacity; slot++)
            {
                ref Voice voice = ref layer.Voices[slot];

                if ((voice.Flags & VoicePendingRelease) != 0)
                {
                    ReleaseVoice(ref layer, ref voice, slot, false);
                    continue;
                }

                if ((voice.Flags & VoiceInUse) == 0)
                {
                    continue;
                }

                EnsurePlayable(ref layer, ref voice, slot);

                if ((voice.Flags & VoiceConnected) == 0)
                {
                    continue;
                }

                // Time is always written: a playing voice advances every commit, and dropping the applied-time
                // field is what keeps the voice struct inside one 64-byte cache line.
                voice.Playable.SetTime(voice.Time);
                _nativeWrites++;

                if (Math.Abs(voice.Weight - voice.AppliedWeight) > AnimationRuntimeLimits.WeightEpsilon)
                {
                    layer.Mixer.SetInputWeight(slot, voice.Weight);
                    voice.AppliedWeight = voice.Weight;
                    _nativeWrites++;
                }
            }

            if (layer.HasExternalSource)
            {
                // A host may dispose its blend node without clearing the layer first. The destroyed playable is
                // no longer valid, so drop the binding here instead of writing weights into a dead node.
                if (!layer.ExternalSource.IsValid())
                {
                    layer.HasExternalSource = false;
                    layer.ExternalConnected = false;
                    layer.ExternalSource = default;
                    layer.ExternalWeight = 1f;
                    layer.AppliedExternalWeight = -1f;
                }
                else if (layer.ExternalConnected)
                {
                    if (Math.Abs(layer.ExternalWeight - layer.AppliedExternalWeight) > AnimationRuntimeLimits.WeightEpsilon)
                    {
                        layer.Mixer.SetInputWeight(layer.Capacity, layer.ExternalWeight);
                        layer.AppliedExternalWeight = layer.ExternalWeight;
                        _nativeWrites++;
                    }
                }
            }

            if (Math.Abs(layer.Weight - layer.AppliedWeight) > AnimationRuntimeLimits.WeightEpsilon)
            {
                _topMixer.SetInputWeight(layerIndex, layer.Weight);
                layer.AppliedWeight = layer.Weight;
                _nativeWrites++;
            }
        }

        private void EnsurePlayable(ref Layer layer, ref Voice voice, int slot)
        {
            if (voice.Playable.IsValid() && ReferenceEquals(voice.BoundClip, voice.Clip))
            {
                if ((voice.Flags & VoiceConnected) == 0)
                {
                    Connect(ref layer, ref voice, slot);
                }

                return;
            }

            if (voice.Playable.IsValid())
            {
                if ((voice.Flags & VoiceConnected) != 0)
                {
                    layer.Mixer.DisconnectInput(slot);
                    voice.Flags &= unchecked((byte)~VoiceConnected);
                }

                _graph.DestroyPlayable(voice.Playable);
                voice.Playable = default;
                _recreatedPlayables++;
            }

            if (voice.Clip == null)
            {
                return;
            }

            AnimationClipPlayable clipPlayable = AnimationClipPlayable.Create(_graph, voice.Clip);
            clipPlayable.SetSpeed(0d);
            clipPlayable.SetApplyFootIK(layer.ApplyFootIK);
            clipPlayable.SetApplyPlayableIK(layer.ApplyPlayableIK);

            voice.Playable = clipPlayable;
            voice.BoundClip = voice.Clip;
            voice.AppliedWeight = 0f;
            Connect(ref layer, ref voice, slot);
        }

        private void Connect(ref Layer layer, ref Voice voice, int slot)
        {
            layer.Mixer.ConnectInput(slot, voice.Playable, 0);
            layer.Mixer.SetInputWeight(slot, 0f);
            voice.AppliedWeight = 0f;
            voice.Flags |= VoiceConnected;
            _nativeWrites += 2;
        }

        private void ReleaseVoice(ref Layer layer, ref Voice voice, int slot, bool destroy)
        {
            if ((voice.Flags & VoiceConnected) != 0)
            {
                if (layer.Mixer.IsValid())
                {
                    layer.Mixer.SetInputWeight(slot, 0f);
                    layer.Mixer.DisconnectInput(slot);
                }

                voice.Flags &= unchecked((byte)~VoiceConnected);
            }

            if (destroy && voice.Playable.IsValid() && _graph.IsValid())
            {
                _graph.DestroyPlayable(voice.Playable);
                voice.Playable = default;
                voice.BoundClip = null;
            }

            bool wasInUse = (voice.Flags & VoiceInUse) != 0;
            voice.Flags &= unchecked((byte)~(VoiceInUse | VoicePendingRelease));
            voice.InstanceId = 0;
            voice.PlaybackChannel = 0;
            voice.Clip = null;
            voice.Weight = 0f;
            voice.AppliedWeight = 0f;
            voice.Time = 0d;

            if (wasInUse)
            {
                if (layer.ActiveVoices > 0)
                {
                    layer.ActiveVoices--;
                }

                if (_activeVoices > 0)
                {
                    _activeVoices--;
                }

                AnimationRuntimeRegistry.OnVoiceCountChanged(-1);
            }
        }

        // ------------------------------------------------------------------ layer API

        public void SetLayerWeight(int layerIndex, float weight)
        {
            if (layerIndex < 0 || layerIndex >= _layerCount)
            {
                return;
            }

            _layers[layerIndex].Weight = weight <= 0f ? 0f : (weight >= 1f ? 1f : weight);
        }

        public float GetLayerWeight(int layerIndex)
        {
            return layerIndex >= 0 && layerIndex < _layerCount ? _layers[layerIndex].Weight : 0f;
        }

        /// <summary>
        /// Connects an external Playable (a locomotion blend node) as the layer's only source. Releases the
        /// layer's voices. This is a topology change: call it on a mode switch, never per frame.
        /// </summary>
        public bool SetLayerSource(int layerIndex, Playable source, float weight = 1f)
        {
            if (!_initialized || layerIndex < 0 || layerIndex >= _layerCount)
            {
                return false;
            }

            if (!source.IsValid())
            {
                return false;
            }

            if (!source.GetGraph().Equals(_graph))
            {
                Error("SetLayerSource rejected: the source belongs to a different PlayableGraph.");
                return false;
            }

            ref Layer layer = ref _layers[layerIndex];

            for (int slot = 0; slot < layer.Capacity; slot++)
            {
                ref Voice voice = ref layer.Voices[slot];
                ReleaseVoice(ref layer, ref voice, slot, false);
            }

            if (layer.ExternalConnected)
            {
                layer.Mixer.DisconnectInput(layer.Capacity);
                layer.ExternalConnected = false;
            }

            layer.ExternalSource = source;
            layer.HasExternalSource = true;
            layer.ExternalWeight = weight <= 0f ? 0f : (weight >= 1f ? 1f : weight);
            layer.AppliedExternalWeight = -1f;
            layer.Mixer.ConnectInput(layer.Capacity, source, 0, 0f);
            layer.ExternalConnected = true;
            _nativeWrites++;
            return true;
        }

        /// <summary>Detaches the external source and returns the layer to the voice pool.</summary>
        public void ClearLayerSource(int layerIndex)
        {
            if (!_initialized || layerIndex < 0 || layerIndex >= _layerCount)
            {
                return;
            }

            ref Layer layer = ref _layers[layerIndex];
            if (layer.ExternalConnected)
            {
                layer.Mixer.SetInputWeight(layer.Capacity, 0f);
                layer.Mixer.DisconnectInput(layer.Capacity);
                layer.ExternalConnected = false;
            }

            layer.HasExternalSource = false;
            layer.ExternalSource = default;
            layer.ExternalWeight = 1f;
            layer.AppliedExternalWeight = -1f;
        }

        // ------------------------------------------------------------------ inspection

        public AnimationRuntimeStats GetStats()
        {
            return new AnimationRuntimeStats(
                _activeVoices,
                _peakVoices,
                _layerCount,
                PlayableCount,
                _droppedVoices,
                _recreatedPlayables,
                _commits,
                _nativeWrites);
        }

        /// <summary>Zero-allocation snapshot copy for the inspector.</summary>
        public int CopyVoiceSnapshots(int layerIndex, AnimationVoiceSnapshot[] buffer)
        {
            if (buffer == null || layerIndex < 0 || layerIndex >= _layerCount)
            {
                return 0;
            }

            ref Layer layer = ref _layers[layerIndex];
            int written = 0;
            for (int slot = 0; slot < layer.Capacity && written < buffer.Length; slot++)
            {
                ref Voice voice = ref layer.Voices[slot];
                if ((voice.Flags & VoiceInUse) == 0)
                {
                    continue;
                }

                buffer[written] = new AnimationVoiceSnapshot(
                    voice.InstanceId,
                    voice.PlaybackChannel,
                    voice.Time,
                    voice.Weight,
                    voice.Length,
                    (voice.Flags & VoiceConnected) != 0);
                written++;
            }

            return written;
        }

        public int GetLayerActiveVoices(int layerIndex)
        {
            return layerIndex >= 0 && layerIndex < _layerCount ? _layers[layerIndex].ActiveVoices : 0;
        }

        /// <summary>Fixed voice capacity of a layer.</summary>
        public int GetLayerCapacity(int layerIndex)
        {
            return layerIndex >= 0 && layerIndex < _layerCount ? _layers[layerIndex].Capacity : 0;
        }

        /// <summary>
        /// Whether a handle still addresses a live voice. Use this before acting on a handle that may have been
        /// invalidated by slot reuse or a graph rebuild; <see cref="AnimationVoiceHandle.IsValid"/> only checks
        /// that the handle was constructed, not that the voice survives.
        /// </summary>
        public bool IsVoiceAlive(in AnimationVoiceHandle handle)
        {
            if (!handle.IsValid || handle.GraphId != _graphId || !_initialized)
            {
                return false;
            }

            int layerIndex = handle.Layer;
            if (layerIndex < 0 || layerIndex >= _layerCount)
            {
                return false;
            }

            ref Layer layer = ref _layers[layerIndex];
            if (handle.Slot < 0 || handle.Slot >= layer.Capacity)
            {
                return false;
            }

            ref Voice voice = ref layer.Voices[handle.Slot];
            return (voice.Flags & VoiceInUse) != 0 && voice.Generation == handle.Generation;
        }

        public bool IsLayerExternallySourced(int layerIndex)
        {
            return layerIndex >= 0 && layerIndex < _layerCount && _layers[layerIndex].HasExternalSource;
        }

        // ------------------------------------------------------------------ helpers

        /// <summary>Authoring mistakes that would otherwise show up as "the layer does nothing".</summary>
        private void ValidateDescriptors()
        {
            if (!_diagnostics.IsEnabled(AnimationDiagnosticLevel.Warning))
            {
                return;
            }

            for (int i = 0; i < _descriptors.Length; i++)
            {
                AnimationLayerDescriptor descriptor = _descriptors[i];

                if (descriptor.Weight <= 0f)
                {
                    Warn("Layer " + i + " has weight 0 and will never contribute to the pose.");
                }

                if (descriptor.Additive && descriptor.Mask == null)
                {
                    Warn("Layer " + i + " is additive with a full-body mask. Additive full-body poses need an explicit reference pose; "
                        + "a mask is usually what you want.");
                }
            }
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        /// <summary>
        /// A humanoid clip on a generic rig (or the reverse) produces a wrong pose without throwing. This is the
        /// most common content error in mixed-rig projects, so it is reported once per clip start in dev builds.
        /// </summary>
        private void ValidateClipForAnimator(AnimationClip clip)
        {
            if (_warnedRigMismatch || clip == null || _animator == null)
            {
                return;
            }

            if (clip.isHumanMotion == _animator.isHuman)
            {
                return;
            }

            _warnedRigMismatch = true;
            Warn("Clip '" + clip.name + "' (humanoid=" + clip.isHumanMotion
                + ") does not match the Animator (humanoid=" + _animator.isHuman
                + "); the resulting pose will be wrong. This warning is reported once per graph.");
        }
#endif

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        /// <summary>Commits between liveness scans. 30 commits is roughly half a second at 60 Hz.</summary>
        private const int LivenessCheckInterval = 30;

        /// <summary>
        /// Development-only guard against the one fatal lifetime mistake: an owner released a clip while a voice
        /// was still sampling it. Detects the destroyed object, terminates the voice and names the instance so
        /// the offending caller can be found. Ship builds pay nothing for this.
        /// </summary>
        private void CheckClipLiveness()
        {
            for (int layerIndex = 0; layerIndex < _layerCount; layerIndex++)
            {
                ref Layer layer = ref _layers[layerIndex];
                for (int slot = 0; slot < layer.Capacity; slot++)
                {
                    ref Voice voice = ref layer.Voices[slot];

                    if ((voice.Flags & VoiceInUse) == 0)
                    {
                        // A pooled slot can still hold a playable bound to a clip the asset system destroyed
                        // after the voice ended. Nothing samples it, but it keeps a native node (and the stale
                        // clip reference) alive until teardown, so drop it here instead.
                        if (voice.Playable.IsValid() && voice.BoundClip == null)
                        {
                            if (_graph.IsValid())
                            {
                                _graph.DestroyPlayable(voice.Playable);
                            }

                            voice.Playable = default;
                            voice.BoundClip = null;
                        }

                        continue;
                    }

                    // Unity's overloaded operator reports true for a destroyed object, which is exactly the case
                    // this guard exists to catch. A valid clip never compares equal to null.
                    if (voice.Clip != null)
                    {
                        continue;
                    }

                    Error(
                        "Voice clip was destroyed while playing (instance " + voice.InstanceId
                        + ", channel " + voice.PlaybackChannel
                        + ", layer " + layerIndex
                        + "). The owner released the clip before stopping playback; the voice is terminated.");

                    voice.Flags |= VoicePendingRelease;
                    voice.Weight = 0f;
                }
            }
        }
#endif

        private int ResolveLayer(int requested)
        {
            if (requested < 0)
            {
                return 0;
            }

            return requested >= _layerCount ? _layerCount - 1 : requested;
        }

        private int FindVoice(ref Layer layer, int instanceId, int playbackChannel)
        {
            for (int slot = 0; slot < layer.Capacity; slot++)
            {
                ref Voice voice = ref layer.Voices[slot];
                if ((voice.Flags & VoiceInUse) != 0
                    && voice.InstanceId == instanceId
                    && voice.PlaybackChannel == playbackChannel)
                {
                    return slot;
                }
            }

            return -1;
        }

        private int FindFreeSlot(ref Layer layer)
        {
            for (int slot = 0; slot < layer.Capacity; slot++)
            {
                if ((layer.Voices[slot].Flags & VoiceInUse) == 0)
                {
                    return slot;
                }
            }

            return -1;
        }

        private static double ClampTime(double time, float length)
        {
            if (time < 0d)
            {
                return 0d;
            }

            if (length <= AnimationRuntimeLimits.MinClipLength)
            {
                return 0d;
            }

            return time > length ? length : time;
        }

        private void Warn(string message)
        {
            if (_diagnostics.IsEnabled(AnimationDiagnosticLevel.Warning))
            {
                _diagnostics.Report(AnimationDiagnosticLevel.Warning, "[AnimationRuntime] " + message);
            }
        }

        private void Error(string message)
        {
            if (_diagnostics.IsEnabled(AnimationDiagnosticLevel.Error))
            {
                _diagnostics.Report(AnimationDiagnosticLevel.Error, "[AnimationRuntime] " + message);
            }
        }
    }
}
