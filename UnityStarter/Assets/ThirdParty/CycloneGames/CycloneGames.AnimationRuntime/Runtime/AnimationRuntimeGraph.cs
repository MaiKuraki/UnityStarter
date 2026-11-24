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
    /// <summary>Turns clip requests into a skeleton pose through one <see cref="UnityEngine.Playables.PlayableGraph"/>.</summary>
    /// <remarks>
    /// Topology is fixed at construction: each layer has a fixed playback capacity and one reserved
    /// external source input. Input counts never change. Native writes are epsilon-gated and commits
    /// are idempotent per <see cref="AnimationTick.StepIndex"/>.
    /// </remarks>
    public sealed class AnimationRuntimeGraph : IDisposable
    {
        private const byte PlaybackInUse = 1 << 0;
        private const byte PlaybackConnected = 1 << 1;
        private const byte PlaybackPendingRelease = 1 << 2;
        private const byte PlaybackReleaseAfterFade = 1 << 3;
        private const byte PlaybackTransitionActive = 1 << 4;
        private const byte ExitArmed = 1 << 0;
        private const byte ExitFired = 1 << 1;
        private const byte ExitEveryCrossing = 1 << 2;
        private const byte TransitionFadeBit = 1 << 0;
        private const byte TransitionExitBit = 1 << 1;

        private static readonly ProfilerMarker CommitMarker = new ProfilerMarker("AnimationRuntime.Commit");

        private struct Playback
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
            public byte Flags;
            public byte ClipChannel;
        }

        /// <summary>Folding parameters for one slot, kept outside <see cref="Playback"/> to preserve its 64-byte layout.</summary>
        private struct PlaybackClock
        {
            public double StartLocalTime;

            /// <summary>Tick the fold is anchored to; NaN until the first commit that sees the playback.</summary>
            public double StartTick;

            public float Speed;
            public byte Loop;
        }

        /// <summary>Cold per-slot ramp state, kept out of <see cref="Playback"/> so the pose struct stays 64 B.</summary>
        private struct PlaybackTransition
        {
            public float FromWeight;
            public float TargetWeight;
            public float Duration;
            public double StartTransitionTime;
            public double FadeAbsoluteStart;
            public byte FadeFired;
            public byte Easing;
            public byte ExitFlags;

            /// <summary>Set by <c>UpdatePlayback</c> and <c>SeekPlayback</c>; cleared by the commit that consumes it.</summary>
            public byte Seeked;

            public float ExitNormalized;
            public float ExitLastNormalized;
            public double LastClipTime;

            /// <summary>Unfolded clock at the previous commit; stays zero under the external policy.</summary>
            public double LastUnwrapped;

            public AnimationClipEvent[] ClipEvents;
        }

        /// <summary>The unfolded clock readings of two consecutive commits.</summary>
        private readonly struct SweepWindow
        {
            public readonly bool ClockDriven;
            public readonly double From;
            public readonly double To;

            public SweepWindow(bool clockDriven, double from, double to)
            {
                ClockDriven = clockDriven;
                From = from;
                To = to;
            }
        }

        private struct ClipEventDispatch
        {
            public AnimationPlaybackHandle Handle;
            public AnimationClipEvent Event;

            public ClipEventDispatch(AnimationPlaybackHandle handle, in AnimationClipEvent clipEvent)
            {
                Handle = handle;
                Event = clipEvent;
            }
        }

        /// <summary>
        /// One slot's transition deliveries for the current commit. A playback can complete a fade and cross
        /// its exit point in the same commit, so both reasons are held as bits in one queue entry. Indexing
        /// the queue by (layer, slot) makes overflow structurally impossible: there is exactly one entry per
        /// possible playback and at most two reasons per entry.
        /// </summary>
        private struct TransitionEvent
        {
            public AnimationPlaybackHandle Handle;
            public byte Reasons;
        }

        private struct Layer
        {
            public AnimationMixerPlayable Mixer;
            public Playable ExternalSource;
            public AvatarMask Mask;
            public Playback[] Playbacks;
            public PlaybackClock[] Clocks;
            public PlaybackTransition[] Transitions;
            public uint[] Generations;
            public Playable[] CachedPlayables;
            public AnimationClip[] CachedClips;
            public float Weight;
            public float AppliedWeight;
            public float ExternalWeight;
            public float AppliedExternalWeight;
            public float LayerFadeFrom;
            public float LayerFadeTarget;
            public float LayerFadeDuration;
            public double LayerFadeStart;
            public bool LayerFadeActive;
            public byte LayerFadeEasing;
            public byte Capacity;
            public byte ActivePlaybacks;

            /// <summary>Bit per in-use playback slot; capacity is at most 8.</summary>
            public byte ActiveMask;
            public bool Additive;
            public bool ApplyFootIK;
            public bool ApplyPlayableIK;
            public bool HasExternalSource;
            public bool ExternalConnected;
        }

        private Animator _animator;
        private readonly IAnimationDiagnostics _diagnostics;
        private readonly AnimationTimePolicy _timePolicy;
        private readonly Layer[] _layers;
        private readonly int _layerCount;
        private readonly AnimationLayerDescriptor[] _descriptors;

        private PlayableGraph _graph;
        private AnimationLayerMixerPlayable _topMixer;
        private readonly long[] _droppedClipEventsByLayer;
        private bool _initialized;
        private bool _dirty = true;
        private int _graphId;
        private long _lastStepIndex = long.MinValue;
        private double _lastTickTime;
        private double _lastTransitionTime = double.NaN;
        private long _droppedPlaybacks;
        private long _droppedClipEvents;
        private long _recreatedPlayables;
        private long _reusedPlayables;
        private long _commits;
        private long _evaluations;
        private long _nativeWrites;
        private int _activePlaybacks;
        private int _peakPlaybacks;
        private bool _warnedRigMismatch;
        private bool _warnedLayerClamp;
        private bool _warnedClipMismatch;
        private bool _warnedWeightConflict;
        private bool _warnedClipEventOverflow;
        private bool _hasPendingRelease;
        private bool _dispatchClipEvents;
        private IAnimationClipEventSink _clipEventSink;
        private readonly ConditionalWeakTable<AnimationClip, AnimationClipEvent[]> _clipEventCache =
            new ConditionalWeakTable<AnimationClip, AnimationClipEvent[]>();

        /// <summary>Marker dispatch queue; allocated on first use and shrunk after a drain.</summary>
        private ClipEventDispatch[] _pendingClipEvents = Array.Empty<ClipEventDispatch>();
        private int _pendingClipEventCount;
        private bool _dispatchingEvents;
        private bool _warnedEventSinkError;
        private bool _warnedReentrantCommit;

        /// <summary>Indexed by layer * MaxLayerCapacity + slot; never overflows, never allocates on the hot path.</summary>
        private readonly TransitionEvent[] _pendingTransitions;
        private bool _hasPendingTransitions;

        /// <summary>
        /// Raised after a commit, once per completed fade. Handlers run on the main thread after the graph has
        /// evaluated; they must not call <see cref="Commit"/> re-entrantly.
        /// </summary>
        public event Action<AnimationPlaybackHandle, AnimationTransitionReason> TransitionCompleted;

        public AnimationRuntimeGraph(
            Animator animator,
            IReadOnlyList<AnimationLayerDescriptor> layers,
            IAnimationDiagnostics diagnostics = null,
            AnimationTimePolicy timePolicy = AnimationTimePolicy.External,
            bool dispatchClipEvents = true)
        {
            if (animator == null)
            {
                throw new ArgumentNullException(nameof(animator));
            }

            _animator = animator;
            _diagnostics = diagnostics ?? NullAnimationDiagnostics.Instance;
            _timePolicy = timePolicy == AnimationTimePolicy.TickDriven
                ? AnimationTimePolicy.TickDriven
                : AnimationTimePolicy.External;
            _dispatchClipEvents = dispatchClipEvents;

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
            _droppedClipEventsByLayer = new long[_layerCount];
            _pendingTransitions = new TransitionEvent[
                AnimationRuntimeLimits.MaxLayers * AnimationRuntimeLimits.MaxLayerCapacity];
            Initialize();
        }

        /// <summary>False before <see cref="Initialize"/> or after <see cref="Dispose"/>.</summary>
        public bool IsInitialized => _initialized && _graph.IsValid();

        public int LayerCount => _layerCount;

        /// <summary>How this graph maps ticks onto clip time. Fixed at construction.</summary>
        public AnimationTimePolicy TimePolicy => _timePolicy;

        /// <summary>The Animator this graph writes to. Changes only through <see cref="TryRebind"/>.</summary>
        public Animator TargetAnimator => _animator;

        /// <summary>Instance id of the current graph. Bumped on every rebuild.</summary>
        public int GraphId => _graphId;

        public int PlayableCount => _graph.IsValid() ? _graph.GetPlayableCount() : 0;

        /// <summary>
        /// Clip markers discarded because one commit exceeded <see cref="AnimationRuntimeLimits.MaxPendingClipEventsPerCommit"/>.
        /// </summary>
        public long DroppedClipEvents => _droppedClipEvents;

        /// <summary>
        /// The underlying graph. Exposed so a host can create an auxiliary node (a locomotion blend) and hand it
        /// to <see cref="SetLayerSource"/>, which requires both nodes to live in the same graph. Treat it as
        /// read-only: connecting or destroying nodes outside this API breaks the fixed-topology contract.
        /// </summary>
        public PlayableGraph PlayableGraph => _graph;

        /// <summary>Size of the internal playback struct. Guarded by a test so layout regressions fail CI.</summary>
        internal static int PlaybackStructSize => Unsafe.SizeOf<Playback>();

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

            try
            {
                BuildGraph();
            }
            catch
            {
                // A half-built graph must not keep the Animator claimed forever.
                AnimationRuntimeRegistry.ReleaseAnimator(_animator, this);
                throw;
            }
        }

        private void BuildGraph()
        {
            if (_dispatchClipEvents && _animator.fireEvents)
            {
                Warn("Animator.fireEvents is enabled and clip-event dispatch is on: every AnimationClip event will "
                    + "fire twice (native Animator + AnimationRuntime). Disable Animator.fireEvents or turn off "
                    + "DispatchClipEvents.");
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
                int capacity = descriptor.Capacity;
                var layer = new Layer
                {
                    Capacity = descriptor.Capacity,
                    Playbacks = new Playback[capacity],
                    Clocks = new PlaybackClock[capacity],
                    Transitions = new PlaybackTransition[capacity],
                    Generations = new uint[capacity],
                    CachedPlayables = new Playable[capacity],
                    CachedClips = new AnimationClip[capacity],
                    Mixer = AnimationMixerPlayable.Create(_graph, capacity + 1),
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

            BumpGraphId();

            _lastStepIndex = long.MinValue;
            _lastTickTime = 0d;
            _lastTransitionTime = double.NaN;
            _dirty = true;
            _warnedRigMismatch = false;
            _warnedLayerClamp = false;
            _warnedClipMismatch = false;
            _initialized = true;
            AnimationRuntimeRegistry.OnGraphCreated();
        }

        /// <summary>Ends every playback, disconnects external sources and destroys the graph.</summary>
        public void Dispose()
        {
            if (!_initialized)
            {
                return;
            }

            TearDownGraph();
            AnimationRuntimeRegistry.ReleaseAnimator(_animator, this);
            AnimationRuntimeRegistry.OnGraphDisposed();
        }

        /// <summary>
        /// Ends every playback, destroys the graph and rebuilds it against another Animator. On failure the graph
        /// keeps driving the previous Animator, so a pooled actor can never end up with the component and the
        /// graph pointing at two different targets.
        /// </summary>
        public bool TryRebind(Animator animator)
        {
            if (animator == null)
            {
                throw new ArgumentNullException(nameof(animator));
            }

            if (!_initialized)
            {
                _animator = animator;
                Initialize();
                return _initialized;
            }

            if (ReferenceEquals(animator, _animator))
            {
                return true;
            }

            // Claim before tearing anything down: a rejected rebind must leave the live graph untouched.
            if (!AnimationRuntimeRegistry.TryClaimAnimator(animator, this))
            {
                return false;
            }

            Animator previous = _animator;
            TearDownGraph();
            AnimationRuntimeRegistry.ReleaseAnimator(previous, this);

            _animator = animator;
            try
            {
                BuildGraph();
                return true;
            }
            catch
            {
                AnimationRuntimeRegistry.ReleaseAnimator(animator, this);
                throw;
            }
        }

        private void TearDownGraph()
        {
            StopAllInternal(true);

            // A rebuild must not deliver events queued against the previous graph id.
            _pendingClipEventCount = 0;
            _hasPendingTransitions = false;
            Array.Clear(_pendingTransitions, 0, _pendingTransitions.Length);
            if (_pendingClipEvents.Length > AnimationRuntimeLimits.SteadyClipEventQueueCapacity)
            {
                _pendingClipEvents = Array.Empty<ClipEventDispatch>();
            }

            if (_graph.IsValid())
            {
                _graph.Destroy();
            }

            _graph = default;
            _topMixer = default;
            _initialized = false;
            _activePlaybacks = 0;
            _dirty = true;
            BumpGraphId();
        }

        private void BumpGraphId()
        {
            _graphId++;
            if (_graphId == 0)
            {
                _graphId = 1;
            }
        }

        // ------------------------------------------------------------------ playback API

        /// <summary>Starts or replaces a playback. Native work happens at <see cref="Commit"/>.</summary>
        public AnimationPlaybackHandle BeginPlayback(in AnimationClipRequest request)
        {
            return BeginPlaybackCore(request, default, false);
        }

        /// <summary>
        /// Starts or replaces a playback and ramps its weight from the current value to
        /// <see cref="AnimationFade.TargetWeight"/>. When a fade is supplied, the request's weight is ignored.
        /// </summary>
        public AnimationPlaybackHandle BeginPlayback(in AnimationClipRequest request, in AnimationFade fade)
        {
            return BeginPlaybackCore(request, fade, true);
        }

        private AnimationPlaybackHandle BeginPlaybackCore(in AnimationClipRequest request, in AnimationFade fade, bool hasFade)
        {
            if (!_initialized)
            {
                return AnimationPlaybackHandle.Invalid;
            }

            if (request.Clip == null)
            {
                Warn("BeginPlayback rejected: clip is null.");
                return AnimationPlaybackHandle.Invalid;
            }

#if DEBUG
            ValidateClipForAnimator(request.Clip);
#endif

            int layerIndex = ResolveLayer(request.Layer);
            ref Layer layer = ref _layers[layerIndex];

            if (layer.HasExternalSource)
            {
                Warn("BeginPlayback rejected: layer is owned by an external source.");
                return AnimationPlaybackHandle.Invalid;
            }

            int slot = FindPlayback(ref layer, request.InstanceId, request.PlaybackChannel, request.ClipChannel);
            bool isNew = slot < 0;
            if (isNew)
            {
                slot = FindFreeSlot(ref layer);
            }

            if (slot < 0)
            {
                _droppedPlaybacks++;
                Warn("BeginPlayback dropped: layer playback capacity exhausted.");
                return AnimationPlaybackHandle.Invalid;
            }

            ref Playback playback = ref layer.Playbacks[slot];
            uint generation = layer.Generations[slot];

            if (isNew)
            {
                generation = AnimationPlaybackGeneration.Next(generation, out bool wrapped);
                layer.Generations[slot] = generation;
                if (wrapped)
                {
                    // A generation wrap invalidates every outstanding handle.
                    BumpGraphId();
                }

                playback.Flags |= PlaybackInUse;
                layer.ActiveMask |= (byte)(1 << slot);
                layer.ActivePlaybacks++;
                _activePlaybacks++;
                AnimationRuntimeRegistry.OnPlaybackCountChanged(1);
                if (_activePlaybacks > _peakPlaybacks)
                {
                    _peakPlaybacks = _activePlaybacks;
                }
            }

            CancelTransition(ref layer, ref playback, slot);

            playback.InstanceId = request.InstanceId;
            playback.PlaybackChannel = request.PlaybackChannel;
            playback.ClipChannel = (byte)request.ClipChannel;
            playback.Clip = request.Clip;
            playback.Length = request.Clip.length;
            playback.Time = ClampTime(request.LocalTime, playback.Length);
            playback.Weight = hasFade ? (isNew ? 0f : playback.Weight) : request.Weight;
            playback.Flags &= unchecked((byte)~PlaybackPendingRelease);

            ref PlaybackClock clock = ref layer.Clocks[slot];
            clock.StartLocalTime = playback.Time;
            clock.StartTick = double.NaN;
            clock.Speed = request.Speed;
            clock.Loop = (byte)request.Loop;
            layer.Transitions[slot].LastClipTime = -1f;
            layer.Transitions[slot].ExitLastNormalized = 0f;

            if (request.HoldLastFrame && playback.Length > AnimationRuntimeLimits.MinClipLength)
            {
                playback.Time = playback.Length;
                clock.StartLocalTime = playback.Time;
                clock.StartTick = double.NaN;
                clock.Speed = 0f;
            }

            if (hasFade)
            {
                StartTransition(ref layer, ref playback, layerIndex, slot, fade, false);
            }

            _dirty = true;
            return new AnimationPlaybackHandle(_graphId, layerIndex, slot, generation);
        }

        /// <summary>
        /// Updates an existing playback. In <see cref="AnimationTimePolicy.External"/> the request carries the new
        /// absolute time; in <see cref="AnimationTimePolicy.TickDriven"/> time comes from ticks and this method
        /// only applies weight/speed/loop. <c>Clip</c> is never changed here; use <see cref="BeginPlayback"/>.
        /// </summary>
        public void UpdatePlayback(in AnimationClipRequest request)
        {
            if (!_initialized)
            {
                return;
            }

            int layerIndex = ResolveLayer(request.Layer);
            ref Layer layer = ref _layers[layerIndex];
            int slot = FindPlayback(ref layer, request.InstanceId, request.PlaybackChannel, request.ClipChannel);
            if (slot < 0)
            {
                return;
            }

            ref Playback playback = ref layer.Playbacks[slot];
            if ((playback.Flags & PlaybackInUse) == 0)
            {
                return;
            }

#if DEBUG
            if (request.Clip != null && !ReferenceEquals(request.Clip, playback.Clip) && !_warnedClipMismatch)
            {
                _warnedClipMismatch = true;
                Warn("UpdatePlayback cannot change the clip of a live playback; the request's Clip was ignored. "
                    + "Call BeginPlayback for the same key to replace the clip.");
            }
#endif

            ref PlaybackClock clock = ref layer.Clocks[slot];
            bool changed = false;

            if (_timePolicy == AnimationTimePolicy.External)
            {
                double time = ClampTime(request.LocalTime, playback.Length);
                if (playback.Time != time)
                {
                    playback.Time = time;
                    layer.Transitions[slot].Seeked = 1;
                    changed = true;
                }
            }
            else
            {
                if (clock.Speed != request.Speed)
                {
                    clock.Speed = request.Speed;
                    changed = true;
                }

                if (clock.Loop != (byte)request.Loop)
                {
                    clock.Loop = (byte)request.Loop;
                    changed = true;
                }
            }

            if (playback.Weight != request.Weight)
            {
                if ((playback.Flags & PlaybackTransitionActive) != 0)
                {
                    if (!_warnedWeightConflict)
                    {
                        _warnedWeightConflict = true;
                        Warn("UpdatePlayback weight ignored while a fade is active; the fade owns the weight "
                            + "until it completes. This warning is reported once per graph.");
                    }
                }
                else
                {
                    playback.Weight = request.Weight;
                    changed = true;
                }
            }

            if (changed)
            {
                _dirty = true;
            }
        }

        /// <summary>Moves a live playback to an explicit local time.</summary>
        public bool SeekPlayback(in AnimationPlaybackKey key, double localTime)
        {
            if (!_initialized)
            {
                return false;
            }

            int layerIndex = ResolveLayer(key.Layer);
            ref Layer layer = ref _layers[layerIndex];
            int slot = FindPlayback(ref layer, key.InstanceId, key.PlaybackChannel, key.ClipChannel);
            if (slot < 0 || (layer.Playbacks[slot].Flags & PlaybackInUse) == 0)
            {
                return false;
            }

            ref Playback playback = ref layer.Playbacks[slot];
            ref PlaybackClock clock = ref layer.Clocks[slot];
            playback.Time = ClampTime(localTime, playback.Length);
            layer.Transitions[slot].Seeked = 1;
            clock.StartLocalTime = playback.Time;
            clock.StartTick = double.NaN;
            _dirty = true;
            return true;
        }

        /// <summary>
        /// Ramps a live playback from its current weight to <see cref="AnimationFade.TargetWeight"/>. While the ramp
        /// is active, <see cref="UpdatePlayback"/> cannot change the weight.
        /// </summary>
        public bool FadePlayback(in AnimationPlaybackKey key, in AnimationFade fade)
        {
            if (!_initialized)
            {
                return false;
            }

            int layerIndex = ResolveLayer(key.Layer);
            ref Layer layer = ref _layers[layerIndex];
            int slot = FindPlayback(ref layer, key.InstanceId, key.PlaybackChannel, key.ClipChannel);
            if (slot < 0 || (layer.Playbacks[slot].Flags & PlaybackInUse) == 0)
            {
                return false;
            }

            ref Playback playback = ref layer.Playbacks[slot];
            StartTransition(ref layer, ref playback, layerIndex, slot, fade, false);
            _dirty = true;
            return true;
        }

        /// <summary>
        /// Enables or disables clip-local event dispatch. Turn it off when a timeline (Choreography) owns events,
        /// otherwise both layers would dispatch the same marker.
        /// </summary>
        public bool DispatchClipEvents
        {
            get => _dispatchClipEvents;
            set => _dispatchClipEvents = value;
        }

        /// <summary>Registers the clip-event receiver. One sink per graph; pass null to detach.</summary>
        public void SetClipEventSink(IAnimationClipEventSink sink)
        {
            _clipEventSink = sink;
        }

        /// <summary>Arms a normalized exit point for a live playback. Fires once at the next crossing.</summary>
        public bool SetExitTime(in AnimationPlaybackKey key, in AnimationExitTime exit)
        {
            if (!_initialized)
            {
                return false;
            }

            int layerIndex = ResolveLayer(key.Layer);
            ref Layer layer = ref _layers[layerIndex];
            int slot = FindPlayback(ref layer, key.InstanceId, key.PlaybackChannel, key.ClipChannel);
            if (slot < 0 || (layer.Playbacks[slot].Flags & PlaybackInUse) == 0)
            {
                return false;
            }

            ref Playback playback = ref layer.Playbacks[slot];
            ref PlaybackTransition transition = ref layer.Transitions[slot];
            transition.ExitNormalized = exit.NormalizedTime;
            transition.ExitFlags = (byte)(ExitArmed
                | (exit.Mode == AnimationExitMode.EveryCrossing ? ExitEveryCrossing : 0));
            transition.ExitLastNormalized = NormalizeTime(playback.Time, playback.Length);
            _dirty = true;
            return true;
        }

        /// <summary>Disarms a previously configured exit point.</summary>
        public void ClearExitTime(in AnimationPlaybackKey key)
        {
            if (!_initialized)
            {
                return;
            }

            int layerIndex = ResolveLayer(key.Layer);
            ref Layer layer = ref _layers[layerIndex];
            int slot = FindPlayback(ref layer, key.InstanceId, key.PlaybackChannel, key.ClipChannel);
            if (slot >= 0)
            {
                layer.Transitions[slot].ExitFlags = 0;
                _dirty = true;
            }
        }

        /// <summary>Requests a playback to stop. The slot is released at the next commit.</summary>
        public void EndPlayback(in AnimationPlaybackKey key)
        {
            if (!_initialized)
            {
                return;
            }

            int layerIndex = ResolveLayer(key.Layer);
            ref Layer layer = ref _layers[layerIndex];
            int slot = FindPlayback(ref layer, key.InstanceId, key.PlaybackChannel, key.ClipChannel);
            if (slot < 0)
            {
                return;
            }

            ref Playback playback = ref layer.Playbacks[slot];
            if ((playback.Flags & PlaybackInUse) == 0)
            {
                return;
            }

            CancelTransition(ref layer, ref playback, slot);
            playback.Flags |= PlaybackPendingRelease;
            playback.Weight = 0f;
            _hasPendingRelease = true;
            _dirty = true;
        }

        /// <summary>
        /// Fades a playback out and releases its slot after the ramp completes. A zero duration behaves like
        /// <see cref="EndPlayback(in AnimationPlaybackKey)"/>.
        /// </summary>
        public void EndPlayback(in AnimationPlaybackKey key, in AnimationFade fade)
        {
            if (!_initialized)
            {
                return;
            }

            if (fade.Duration <= 0f)
            {
                EndPlayback(key);
                return;
            }

            int layerIndex = ResolveLayer(key.Layer);
            ref Layer layer = ref _layers[layerIndex];
            int slot = FindPlayback(ref layer, key.InstanceId, key.PlaybackChannel, key.ClipChannel);
            if (slot < 0)
            {
                return;
            }

            ref Playback playback = ref layer.Playbacks[slot];
            if ((playback.Flags & PlaybackInUse) == 0 || (playback.Flags & PlaybackPendingRelease) != 0)
            {
                return;
            }

            StartTransition(ref layer, ref playback, layerIndex, slot, fade, true);
            _dirty = true;
        }

        /// <summary>Releases every playback and external source. The graph stays alive and reusable.</summary>
        public void StopAll()
        {
            if (!_initialized)
            {
                return;
            }

            StopAllInternal(false);
            _dirty = true;
        }

        private void StopAllInternal(bool destroyPlaybacks)
        {
            for (int i = 0; i < _layerCount; i++)
            {
                ref Layer layer = ref _layers[i];

                for (int slot = 0; slot < layer.Capacity; slot++)
                {
                    ref Playback playback = ref layer.Playbacks[slot];
                    ReleasePlayback(ref layer, ref playback, slot, destroyPlaybacks);
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
                layer.ActivePlaybacks = 0;

                if (layer.Mixer.IsValid())
                {
                    layer.Mixer.SetInputWeight(layer.Capacity, 0f);
                }
            }

            _activePlaybacks = 0;
            _hasPendingRelease = false;
        }

        // ------------------------------------------------------------------ commit

        /// <summary>Applies pending changes and evaluates the graph when dirty.</summary>
        /// <remarks>Idempotent per <see cref="AnimationTick.StepIndex"/>. External-source layers always evaluate.</remarks>
        public void Commit(in AnimationTick tick)
        {
            if (!_initialized)
            {
                return;
            }

            if (_dispatchingEvents)
            {
                if (!_warnedReentrantCommit)
                {
                    _warnedReentrantCommit = true;
                    Warn("Commit was called from an event handler and was ignored. Event handlers must not re-enter Commit.");
                }

                return;
            }

            if (tick.IsValid && tick.StepIndex == _lastStepIndex)
            {
                return;
            }

            if (tick.IsValid)
            {
                _lastStepIndex = tick.StepIndex;
                _lastTickTime = tick.ClipTime;
            }

            _commits++;
            double transitionTime = tick.TransitionTime;
            bool transitionClockMoved = transitionTime != _lastTransitionTime;

#if DEBUG
            if ((_commits % LivenessCheckInterval) == 0)
            {
                CheckClipLiveness();
            }
#endif

            if (!_dirty && !HasExternalSource() && !ClockDrivenPlaybacksPending()
                && (!transitionClockMoved || !HasActiveFades()) && !_hasPendingRelease)
            {
                return;
            }

            CommitMarker.Begin();
            try
            {
                for (int i = 0; i < _layerCount; i++)
                {
                    ApplyLayer(i, tick.ClipTime, tick.IsBackward, tick.TransitionTime);
                }

                _graph.Evaluate(0f);
                _dirty = false;
                _lastTransitionTime = transitionTime;
                _evaluations++;
                if (_hasPendingRelease)
                {
                    _hasPendingRelease = HasPendingRelease();
                }
            }
            finally
            {
                CommitMarker.End();
            }

            _dispatchingEvents = true;
            try
            {
                if (tick.SuppressEvents)
                {
                    DiscardPendingEvents();
                }
                else
                {
                    DrainClipEvents();
                    DrainTransitionEvents();
                }
            }
            finally
            {
                _dispatchingEvents = false;
            }
        }

        /// <summary>Drops queued events without invoking any sink.</summary>
        private void DiscardPendingEvents()
        {
            _pendingClipEventCount = 0;
            _hasPendingTransitions = false;
            Array.Clear(_pendingTransitions, 0, _pendingTransitions.Length);
            if (_pendingClipEvents.Length > AnimationRuntimeLimits.SteadyClipEventQueueCapacity)
            {
                Array.Resize(ref _pendingClipEvents, AnimationRuntimeLimits.SteadyClipEventQueueCapacity);
            }
        }

        private bool HasExternalSource()
        {
            for (int i = 0; i < _layerCount; i++)
            {
                if (_layers[i].HasExternalSource)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>True when a playback or layer ramp can still change at a different transition time.</summary>
        private bool HasActiveFades()
        {
            for (int i = 0; i < _layerCount; i++)
            {
                ref Layer layer = ref _layers[i];
                if (layer.LayerFadeActive)
                {
                    return true;
                }

                byte remaining = layer.ActiveMask;
                while (remaining != 0)
                {
                    int slot = LowestSetBit(remaining);
                    remaining &= (byte)(remaining - 1);
                    if ((layer.Playbacks[slot].Flags & PlaybackTransitionActive) != 0)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private bool HasPendingRelease()
        {
            for (int i = 0; i < _layerCount; i++)
            {
                ref Layer layer = ref _layers[i];
                byte remaining = layer.ActiveMask;
                while (remaining != 0)
                {
                    int slot = LowestSetBit(remaining);
                    remaining &= (byte)(remaining - 1);
                    if ((layer.Playbacks[slot].Flags & PlaybackPendingRelease) != 0)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// A tick-driven playback advances its own clock, so consecutive commits are real changes even when no
        /// request was issued. External-policy graphs only reach this check when nothing is dirty.
        /// </summary>
        private bool ClockDrivenPlaybacksPending()
        {
            if (_timePolicy != AnimationTimePolicy.TickDriven)
            {
                return false;
            }

            for (int i = 0; i < _layerCount; i++)
            {
                if (_layers[i].ActivePlaybacks > 0)
                {
                    return true;
                }
            }

            return false;
        }

        private void ApplyLayer(int layerIndex, double tickTime, bool backward, double transitionTime)
        {
            ref Layer layer = ref _layers[layerIndex];

            // Snapshot the mask: a pending release removes its own bit during this loop.
            byte remaining = layer.ActiveMask;
            while (remaining != 0)
            {
                int slot = LowestSetBit(remaining);
                remaining &= (byte)(remaining - 1);

                ref Playback playback = ref layer.Playbacks[slot];

                if ((playback.Flags & PlaybackPendingRelease) != 0)
                {
                    ReleasePlayback(ref layer, ref playback, slot, false);
                    continue;
                }

                if ((playback.Flags & PlaybackInUse) == 0)
                {
                    continue;
                }

                EnsurePlayable(ref layer, ref playback, slot);

                if ((playback.Flags & PlaybackConnected) == 0)
                {
                    continue;
                }

                double unwrapped = 0d;
                bool clockDriven = _timePolicy == AnimationTimePolicy.TickDriven;

                if (clockDriven)
                {
                    ref PlaybackClock clock = ref layer.Clocks[slot];

                    // Anchor on the first commit that sees this playback, and re-anchor on a host
                    // rewind so the pose stays where it is. Both make (tickTime - StartTick) zero.
                    if (double.IsNaN(clock.StartTick) || backward)
                    {
                        clock.StartTick = tickTime;
                        if (backward)
                        {
                            clock.StartLocalTime = playback.Time;
                        }
                    }

                    playback.Time = EvaluateClock(ref clock, playback.Length, tickTime, out unwrapped);
                }

                // Consumed once per commit by both event channels below.
                ref PlaybackTransition transition = ref layer.Transitions[slot];
                bool seeked = transition.Seeked != 0;
                transition.Seeked = 0;

                SweepWindow window = BuildWindow(clockDriven, ref transition, unwrapped, playback.Length, seeked, backward);

                DispatchClipEventsForPlayback(ref layer, ref playback, layerIndex, slot, seeked, in window);
                CheckExitTime(ref layer, ref playback, layerIndex, slot, seeked, in window);

                transition.LastUnwrapped = unwrapped;

                if ((playback.Flags & PlaybackTransitionActive) != 0)
                {
                    AdvanceTransition(ref layer, ref playback, layerIndex, slot, transitionTime);
                }

                // Playback has no applied-time field, so its 64-byte layout stays intact.
                playback.Playable.SetTime(playback.Time);
                _nativeWrites++;

                layer.Transitions[slot].LastClipTime = playback.Time;

                if (Math.Abs(playback.Weight - playback.AppliedWeight) > AnimationRuntimeLimits.WeightEpsilon)
                {
                    layer.Mixer.SetInputWeight(slot, playback.Weight);
                    playback.AppliedWeight = playback.Weight;
                    _nativeWrites++;
                }
            }

            if (layer.LayerFadeActive)
            {
                AdvanceLayerFade(ref layer, transitionTime);
            }

            if (layer.HasExternalSource)
            {
                // Drop the binding if the host destroyed its blend node without clearing the layer.
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

        /// <summary>Folds the absolute tick clock into clip-local seconds.</summary>
        private static double EvaluateClock(ref PlaybackClock clock, float length, double tickTime, out double unwrapped)
        {
            unwrapped = 0d;

            if (length <= AnimationRuntimeLimits.MinClipLength)
            {
                return 0d;
            }

            if (!AnimationClockMath.TryUnwrapped(
                    clock.StartLocalTime, clock.StartTick, tickTime, clock.Speed, out double computed))
            {
                // Two finite inputs can still overflow when multiplied. Hold the last finite anchor.
                unwrapped = AnimationClockMath.ClampFinite(clock.StartLocalTime, length);
                return unwrapped;
            }

            unwrapped = computed;
            return FoldTime(computed, length, clock.Loop);
        }

        /// <summary>Builds the sweep window from the unfolded clock and the previous commit's reading.</summary>
        private static SweepWindow BuildWindow(
            bool clockDriven, ref PlaybackTransition transition, double unwrapped, float length, bool seeked,
            bool rewind)
        {
            if (!clockDriven || seeked || rewind || transition.LastClipTime < 0d
                || length <= AnimationRuntimeLimits.MinClipLength
                || !AnimationClockMath.IsFinite(unwrapped)
                || !AnimationClockMath.IsFinite(transition.LastUnwrapped))
            {
                return new SweepWindow(false, 0d, 0d);
            }

            return new SweepWindow(true, transition.LastUnwrapped, unwrapped);
        }

        private void EnsurePlayable(ref Layer layer, ref Playback playback, int slot)
        {
            if (playback.Playable.IsValid() && ReferenceEquals(playback.BoundClip, playback.Clip))
            {
                if ((playback.Flags & PlaybackConnected) == 0)
                {
                    Connect(ref layer, ref playback, slot);
                }

                return;
            }

            if ((playback.Flags & PlaybackConnected) != 0)
            {
                layer.Mixer.DisconnectInput(slot);
                playback.Flags &= unchecked((byte)~PlaybackConnected);
            }

            // Reuse the native node when switching back to the clip used last commit.
            if (playback.Clip != null
                && ReferenceEquals(layer.CachedClips[slot], playback.Clip)
                && layer.CachedPlayables[slot].IsValid())
            {
                Playable previous = playback.Playable;
                AnimationClip previousClip = playback.BoundClip;

                playback.Playable = layer.CachedPlayables[slot];
                playback.BoundClip = layer.CachedClips[slot];
                playback.AppliedWeight = 0f;

                layer.CachedPlayables[slot] = previous;
                layer.CachedClips[slot] = previousClip;

                Connect(ref layer, ref playback, slot);
                _reusedPlayables++;
                return;
            }

            if (layer.CachedPlayables[slot].IsValid())
            {
                if (_graph.IsValid())
                {
                    _graph.DestroyPlayable(layer.CachedPlayables[slot]);
                }

                layer.CachedPlayables[slot] = default;
                layer.CachedClips[slot] = null;
            }

            layer.CachedPlayables[slot] = playback.Playable;
            layer.CachedClips[slot] = playback.BoundClip;
            playback.Playable = default;
            playback.BoundClip = null;

            if (playback.Clip == null)
            {
                return;
            }

            AnimationClipPlayable clipPlayable = AnimationClipPlayable.Create(_graph, playback.Clip);
            clipPlayable.SetSpeed(0d);
            clipPlayable.SetApplyFootIK(layer.ApplyFootIK);
            clipPlayable.SetApplyPlayableIK(layer.ApplyPlayableIK);

            playback.Playable = clipPlayable;
            playback.BoundClip = playback.Clip;
            playback.AppliedWeight = 0f;
            _recreatedPlayables++;
            Connect(ref layer, ref playback, slot);
        }

        private void Connect(ref Layer layer, ref Playback playback, int slot)
        {
            layer.Mixer.ConnectInput(slot, playback.Playable, 0);
            layer.Mixer.SetInputWeight(slot, 0f);
            playback.AppliedWeight = 0f;
            playback.Flags |= PlaybackConnected;
            _nativeWrites += 2;
        }

        private void ReleasePlayback(ref Layer layer, ref Playback playback, int slot, bool destroy)
        {
            if ((playback.Flags & PlaybackConnected) != 0)
            {
                if (layer.Mixer.IsValid())
                {
                    layer.Mixer.SetInputWeight(slot, 0f);
                    layer.Mixer.DisconnectInput(slot);
                }

                playback.Flags &= unchecked((byte)~PlaybackConnected);
            }

            if (destroy)
            {
                if (playback.Playable.IsValid() && _graph.IsValid())
                {
                    _graph.DestroyPlayable(playback.Playable);
                }

                playback.Playable = default;
                playback.BoundClip = null;

                if (layer.CachedPlayables[slot].IsValid() && _graph.IsValid())
                {
                    _graph.DestroyPlayable(layer.CachedPlayables[slot]);
                }

                layer.CachedPlayables[slot] = default;
                layer.CachedClips[slot] = null;
            }

            bool wasInUse = (playback.Flags & PlaybackInUse) != 0;
            playback.Flags &= unchecked((byte)~(PlaybackInUse | PlaybackPendingRelease | PlaybackTransitionActive | PlaybackReleaseAfterFade));
            layer.Transitions[slot] = default;
            playback.InstanceId = 0;
            playback.PlaybackChannel = 0;
            playback.ClipChannel = 0;
            playback.Clip = null;
            playback.Weight = 0f;
            playback.AppliedWeight = 0f;
            playback.Time = 0d;

            // Keep the bit mask authoritative even when this slot was already free.
            layer.ActiveMask &= unchecked((byte)~(1 << slot));

            if (wasInUse)
            {
                if (layer.ActivePlaybacks > 0)
                {
                    layer.ActivePlaybacks--;
                }

                if (_activePlaybacks > 0)
                {
                    _activePlaybacks--;
                }

                AnimationRuntimeRegistry.OnPlaybackCountChanged(-1);
            }
        }

        // ------------------------------------------------------------------ transitions

        private void StartTransition(
            ref Layer layer,
            ref Playback playback,
            int layerIndex,
            int slot,
            in AnimationFade fade,
            bool releaseAfterFade)
        {
            bool wasActive = (playback.Flags & PlaybackTransitionActive) != 0;

            if (fade.IsInstant)
            {
                playback.Flags &= unchecked((byte)~(PlaybackTransitionActive | PlaybackReleaseAfterFade));
                playback.Weight = fade.TargetWeight;

                // Reset the ramp only; LastClipTime, the exit point and ClipEvents are preserved.
                ref PlaybackTransition instant = ref layer.Transitions[slot];
                instant.FromWeight = 0f;
                instant.TargetWeight = 0f;
                instant.Duration = 0f;
                instant.FadeAbsoluteStart = 0d;
                instant.FadeFired = 0;
                instant.Easing = 0;

                if (releaseAfterFade)
                {
                    playback.Flags |= PlaybackPendingRelease;
                    _hasPendingRelease = true;
                }

                return;
            }

            ref PlaybackTransition transition = ref layer.Transitions[slot];
            transition.FromWeight = playback.Weight;
            transition.TargetWeight = fade.TargetWeight;
            transition.Duration = fade.Duration;
            transition.FadeAbsoluteStart = fade.AbsoluteStartTime;
            transition.StartTransitionTime = fade.AbsoluteStartTime;
            transition.FadeFired = 0;
            transition.Easing = (byte)fade.Easing;

            playback.Flags |= PlaybackTransitionActive;
            if (releaseAfterFade)
            {
                playback.Flags |= PlaybackReleaseAfterFade;
            }
            else
            {
                playback.Flags &= unchecked((byte)~PlaybackReleaseAfterFade);
            }
        }

        /// <summary>Computes ramp weight from the absolute transition clock.</summary>
        private void AdvanceTransition(ref Layer layer, ref Playback playback, int layerIndex, int slot,
            double transitionTime)
        {
            ref PlaybackTransition transition = ref layer.Transitions[slot];
            double elapsed = transitionTime - transition.StartTransitionTime;
            float progress = transition.Duration <= 0f ? 1f : (float)(elapsed / transition.Duration);
            if (progress < 0f)
            {
                progress = 0f;
            }
            else if (progress > 1f)
            {
                progress = 1f;
            }

            float eased = AnimationFade.Evaluate((AnimationFadeEasing)transition.Easing, progress);
            playback.Weight = transition.FromWeight + ((transition.TargetWeight - transition.FromWeight) * eased);

            if (progress < 1f)
            {
                transition.FadeFired = 0;
                return;
            }

            playback.Weight = transition.TargetWeight;
            if (transition.FadeFired == 0)
            {
                transition.FadeFired = 1;
                QueueTransitionEvent(layerIndex, slot, AnimationTransitionReason.FadeCompleted);

                bool release = (playback.Flags & PlaybackReleaseAfterFade) != 0;
                playback.Flags &= unchecked((byte)~PlaybackReleaseAfterFade);
                if (release)
                {
                    playback.Flags |= PlaybackPendingRelease;
                    _hasPendingRelease = true;
                }
            }

            // Keep PlaybackTransitionActive so a rewound clock recomputes the weight;
            // FadeFired guards only the completion event and release.
        }

        private void CancelTransition(ref Layer layer, ref Playback playback, int slot)
        {
            playback.Flags &= unchecked((byte)~(PlaybackTransitionActive | PlaybackReleaseAfterFade));
            layer.Transitions[slot] = default;
        }

        private void QueueTransitionEvent(int layerIndex, int slot, AnimationTransitionReason reason)
        {
            if (TransitionCompleted == null)
            {
                return;
            }

            // One entry per possible playback, indexed by (layer, slot). Both reasons fit in one byte, so a
            // playback that completes a fade and crosses its exit point in the same commit is never dropped.
            int index = layerIndex * AnimationRuntimeLimits.MaxLayerCapacity + slot;
            ref TransitionEvent entry = ref _pendingTransitions[index];
            entry.Handle = new AnimationPlaybackHandle(
                _graphId, layerIndex, slot, _layers[layerIndex].Generations[slot]);
            entry.Reasons |= reason == AnimationTransitionReason.ExitTime
                ? TransitionExitBit
                : TransitionFadeBit;
            _hasPendingTransitions = true;
        }

        private void DrainTransitionEvents()
        {
            if (!_hasPendingTransitions)
            {
                return;
            }

            _hasPendingTransitions = false;
            Action<AnimationPlaybackHandle, AnimationTransitionReason> handler = TransitionCompleted;

            for (int i = 0; i < _pendingTransitions.Length; i++)
            {
                byte reasons = _pendingTransitions[i].Reasons;
                if (reasons == 0)
                {
                    continue;
                }

                // Clear before invoking: a handler may reuse this slot during dispatch.
                _pendingTransitions[i].Reasons = 0;
                if (handler == null)
                {
                    continue;
                }

                AnimationPlaybackHandle handle = _pendingTransitions[i].Handle;

                // Documented order: clip events, then exit time, then fade completion.
                if ((reasons & TransitionExitBit) != 0)
                {
                    try
                    {
                        handler(handle, AnimationTransitionReason.ExitTime);
                    }
                    catch (Exception exception)
                    {
                        ReportEventSinkException(exception);
                    }
                }

                if ((reasons & TransitionFadeBit) != 0)
                {
                    try
                    {
                        handler(handle, AnimationTransitionReason.FadeCompleted);
                    }
                    catch (Exception exception)
                    {
                        ReportEventSinkException(exception);
                    }
                }
            }
        }

        private void ReportEventSinkException(Exception exception)
        {
            if (_warnedEventSinkError)
            {
                return;
            }

            _warnedEventSinkError = true;
            Error("An event handler threw; the exception was contained and the remaining events were delivered. "
                + exception);
        }

        // ------------------------------------------------------------------ clip events / exit time

        private static float NormalizeTime(double time, float length)
        {
            if (length <= AnimationRuntimeLimits.MinClipLength)
            {
                return 0f;
            }

            double normalized = time / length;
            return normalized <= 0d ? 0f : (normalized >= 1d ? 1f : (float)normalized);
        }

        private AnimationClipEvent[] GetClipEvents(AnimationClip clip)
        {
            if (_clipEventCache.TryGetValue(clip, out AnimationClipEvent[] cached))
            {
                return cached;
            }

            AnimationEvent[] source = clip.events;
            var result = new AnimationClipEvent[source.Length];
            float length = clip.length;

            for (int i = 0; i < source.Length; i++)
            {
                AnimationEvent sourceEvent = source[i];
                float normalized = length > AnimationRuntimeLimits.MinClipLength
                    ? sourceEvent.time / length
                    : 0f;
                if (normalized < 0f)
                {
                    normalized = 0f;
                }
                else if (normalized > 1f)
                {
                    normalized = 1f;
                }

                result[i] = new AnimationClipEvent(
                    HashEventName(sourceEvent.functionName),
                    normalized,
                    sourceEvent.functionName,
                    sourceEvent.stringParameter,
                    sourceEvent.intParameter,
                    sourceEvent.floatParameter);
            }

            // Clips are usually authored in time order, but the cache must not depend on that.
            for (int i = 1; i < result.Length; i++)
            {
                AnimationClipEvent key = result[i];
                int j = i - 1;
                while (j >= 0 && result[j].NormalizedTime > key.NormalizedTime)
                {
                    result[j + 1] = result[j];
                    j--;
                }

                result[j + 1] = key;
            }

            _clipEventCache.Add(clip, result);
            return result;
        }

        private static int HashEventName(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return 0;
            }

            unchecked
            {
                int hash = 17;
                for (int i = 0; i < name.Length; i++)
                {
                    hash = (hash * 31) + name[i];
                }

                return hash;
            }
        }

        /// <summary>
        /// Reports clip markers crossed since the previous commit. The first commit sweeps [0, current]; a backward
        /// step sweeps the clip tail plus head, unless it came from a host clock write, which sweeps nothing.
        /// </summary>
        private void DispatchClipEventsForPlayback(
            ref Layer layer,
            ref Playback playback,
            int layerIndex,
            int slot,
            bool seeked,
            in SweepWindow window)
        {
            ref PlaybackTransition transition = ref layer.Transitions[slot];

            if (!_dispatchClipEvents || _clipEventSink == null || playback.Clip == null
                || playback.Length <= AnimationRuntimeLimits.MinClipLength)
            {
                return;
            }

            // A silent playback reports nothing: a fade-out or a zero-weight request owns no audible pose.
            float target = (playback.Flags & PlaybackTransitionActive) != 0
                ? transition.TargetWeight
                : playback.Weight;
            if (target <= 0f && playback.Weight <= 0f)
            {
                return;
            }

            AnimationClipEvent[] events = transition.ClipEvents;
            if (events == null)
            {
                events = GetClipEvents(playback.Clip);
                transition.ClipEvents = events;
            }

            if (events.Length == 0)
            {
                return;
            }

            double previousTime = transition.LastClipTime;
            double currentTime = playback.Time;
            bool firstCommit = previousTime < 0d;
            bool backward = !firstCommit && currentTime < previousTime;

            byte loop = layer.Clocks[slot].Loop;
            bool loopMode = loop == (byte)AnimationLoopMode.Loop;
            bool pingPong = loop == (byte)AnimationLoopMode.PingPong;

            if (window.ClockDriven)
            {
                QueueClockSpan(layerIndex, slot, events, playback.Length, window.From, window.To, loop);
                return;
            }

            // External policy or a host clock write: only the folded endpoints are available.
            if (backward && (seeked || (!loopMode && !pingPong)))
            {
                return;
            }

            bool looped = backward && loopMode;
            bool reverse = backward && pingPong;
            float previous = firstCommit ? 0f : NormalizeTime(previousTime, playback.Length);
            float current = NormalizeTime(currentTime, playback.Length);

            if (reverse)
            {
                // Ping-pong walks from previous down to current, so markers are reported high to low.
                for (int i = events.Length - 1; i >= 0; i--)
                {
                    float at = events[i].NormalizedTime;
                    if (at < previous && at >= current)
                    {
                        QueueClipEvent(layerIndex, slot, events[i]);
                    }
                }

                return;
            }

            if (looped)
            {
                // A loop wrap walks the tail first, then the head.
                for (int i = 0; i < events.Length; i++)
                {
                    if (events[i].NormalizedTime > previous)
                    {
                        QueueClipEvent(layerIndex, slot, events[i]);
                    }
                }

                for (int i = 0; i < events.Length; i++)
                {
                    if (events[i].NormalizedTime <= current)
                    {
                        QueueClipEvent(layerIndex, slot, events[i]);
                    }
                }

                return;
            }

            if (firstCommit)
            {
                // A reverse playback has no history to pre-roll, and the clip end folds to zero on a loop.
                bool fromEnd = _timePolicy == AnimationTimePolicy.TickDriven && layer.Clocks[slot].Speed < 0f;
                if (!fromEnd)
                {
                    for (int i = 0; i < events.Length; i++)
                    {
                        if (events[i].NormalizedTime <= current)
                        {
                            QueueClipEvent(layerIndex, slot, events[i]);
                        }
                    }
                }

                return;
            }

            for (int i = 0; i < events.Length; i++)
            {
                float at = events[i].NormalizedTime;
                if (at > previous && at <= current)
                {
                    QueueClipEvent(layerIndex, slot, events[i]);
                }
            }
        }

        /// <summary>
        /// Walks the unfolded clock across one commit, splitting the interval at every clip-length multiple. A step that
        /// covers a whole cycle on a looping mode reports each marker once.
        /// </summary>
        private void QueueClockSpan(
            int layerIndex, int slot, AnimationClipEvent[] events, float length, double from, double to, byte loop)
        {
            double delta = to - from;
            if (delta == 0d || !AnimationClockMath.IsFinite(delta))
            {
                return;
            }

            if (loop != (byte)AnimationLoopMode.Clamp)
            {
                double period = loop == (byte)AnimationLoopMode.Loop ? length : length * 2d;
                if (Math.Abs(delta) >= period)
                {
                    for (int i = 0; i < events.Length; i++)
                    {
                        QueueClipEvent(layerIndex, slot, events[i]);
                    }

                    return;
                }
            }

            int direction = delta > 0d ? 1 : -1;
            double cursor = from;
            int legs = 0;

            while (true)
            {
                // Clamp pins the pose at a bound, so every remaining segment would be empty.
                if (loop == (byte)AnimationLoopMode.Clamp
                    && (direction > 0 ? cursor >= length : cursor <= 0d))
                {
                    return;
                }

                double boundary = direction > 0
                    ? (Math.Floor(cursor / length) + 1d) * length
                    : (Math.Ceiling(cursor / length) - 1d) * length;

                bool last = direction > 0 ? boundary >= to : boundary <= to;
                double end = last ? to : boundary;

                // A boundary that does not advance the cursor would spin: 3 * 1.6 rounds past 4.8 / 1.6.
                if (direction > 0 ? end <= cursor : end >= cursor)
                {
                    end = cursor + (direction * length);
                    last = direction > 0 ? end >= to : end <= to;
                    if (last)
                    {
                        end = to;
                    }
                }

                if (TryLegBounds(length, cursor, end, direction, loop, out float start, out float finish))
                {
                    if (finish > start)
                    {
                        for (int i = 0; i < events.Length; i++)
                        {
                            float at = events[i].NormalizedTime;
                            if (at > start && at <= finish)
                            {
                                QueueClipEvent(layerIndex, slot, events[i]);
                            }
                        }
                    }
                    else
                    {
                        for (int i = events.Length - 1; i >= 0; i--)
                        {
                            float at = events[i].NormalizedTime;
                            if (at < start && at >= finish)
                            {
                                QueueClipEvent(layerIndex, slot, events[i]);
                            }
                        }
                    }
                }

                if (last)
                {
                    return;
                }

                // Bound the walk; normal commits use at most three legs.
                if (++legs >= AnimationRuntimeLimits.MaxSweepLegs)
                {
                    return;
                }

                cursor = end;
            }
        }

        /// <summary>The same walk as <see cref="QueueClockSpan"/>, answering whether one position was crossed.</summary>
        private static bool SpanCrosses(float length, double from, double to, byte loop, float position)
        {
            double delta = to - from;
            if (delta == 0d || !AnimationClockMath.IsFinite(delta))
            {
                return false;
            }

            if (loop != (byte)AnimationLoopMode.Clamp)
            {
                double period = loop == (byte)AnimationLoopMode.Loop ? length : length * 2d;
                if (Math.Abs(delta) >= period)
                {
                    return true;
                }
            }

            int direction = delta > 0d ? 1 : -1;
            double cursor = from;
            int legs = 0;

            while (true)
            {
                // Clamp pins the pose at a bound, so every remaining segment would be empty.
                if (loop == (byte)AnimationLoopMode.Clamp
                    && (direction > 0 ? cursor >= length : cursor <= 0d))
                {
                    return false;
                }

                double boundary = direction > 0
                    ? (Math.Floor(cursor / length) + 1d) * length
                    : (Math.Ceiling(cursor / length) - 1d) * length;

                bool last = direction > 0 ? boundary >= to : boundary <= to;
                double end = last ? to : boundary;

                // A boundary that does not advance the cursor would spin: 3 * 1.6 rounds past 4.8 / 1.6.
                if (direction > 0 ? end <= cursor : end >= cursor)
                {
                    end = cursor + (direction * length);
                    last = direction > 0 ? end >= to : end <= to;
                    if (last)
                    {
                        end = to;
                    }
                }

                if (TryLegBounds(length, cursor, end, direction, loop, out float start, out float finish))
                {
                    bool inside = finish > start
                        ? position > start && position <= finish
                        : position < start && position >= finish;

                    if (inside)
                    {
                        return true;
                    }
                }

                if (last)
                {
                    return false;
                }

                if (++legs >= AnimationRuntimeLimits.MaxSweepLegs)
                {
                    return false;
                }

                cursor = end;
            }
        }

        /// <summary>Normalized endpoints of one leg, or false when the leg does not move the pose.</summary>
        private static bool TryLegBounds(
            float length, double a, double b, int direction, byte loop, out float start, out float finish)
        {
            start = 0f;
            finish = 0f;

            double low = Math.Min(a, b);
            double high = Math.Max(a, b);
            if (high <= low)
            {
                return false;
            }

            start = (float)(SideValue(direction > 0 ? low : high, direction > 0 ? 0d : length, length, loop) / length);
            finish = (float)(SideValue(direction > 0 ? high : low, direction > 0 ? length : 0d, length, loop) / length);
            return start != finish;
        }

        /// <summary>Folded clock, with the side a Loop wrap is approached from made explicit.</summary>
        private static double SideValue(double u, double atWrap, float length, byte loop)
        {
            if (loop != (byte)AnimationLoopMode.Loop)
            {
                return FoldTime(u, length, loop);
            }

            double turns = u / length;
            if (Math.Abs(turns - Math.Round(turns)) <= 1e-9d)
            {
                return atWrap;
            }

            return u - (Math.Floor(turns) * length);
        }

        private static double FoldTime(double u, float length, byte loop)
        {
            switch ((AnimationLoopMode)loop)
            {
                case AnimationLoopMode.Loop:
                {
                    double m = u % length;
                    return m < 0d ? m + length : m;
                }

                case AnimationLoopMode.PingPong:
                {
                    double span = length * 2d;
                    double m = u % span;
                    if (m < 0d)
                    {
                        m += span;
                    }

                    return m > length ? span - m : m;
                }

                default:
                    return u <= 0d ? 0d : (u > length ? length : u);
            }
        }

        private void CheckExitTime(
            ref Layer layer, ref Playback playback, int layerIndex, int slot, bool seeked, in SweepWindow window)
        {
            ref PlaybackTransition transition = ref layer.Transitions[slot];
            if ((transition.ExitFlags & ExitArmed) == 0)
            {
                return;
            }

            float target = (playback.Flags & PlaybackTransitionActive) != 0
                ? transition.TargetWeight
                : playback.Weight;
            if (target <= 0f && playback.Weight <= 0f)
            {
                return;
            }

            bool everyLoop = (transition.ExitFlags & ExitEveryCrossing) != 0;
            if (!everyLoop && (transition.ExitFlags & ExitFired) != 0)
            {
                return;
            }

            float current = NormalizeTime(playback.Time, playback.Length);
            float previous = transition.ExitLastNormalized;
            transition.ExitLastNormalized = current;

            byte loop = layer.Clocks[slot].Loop;
            bool backward = current < previous;
            bool looped = !seeked && backward && loop == (byte)AnimationLoopMode.Loop;
            bool reverse = !seeked && backward && loop == (byte)AnimationLoopMode.PingPong;

            if (backward && !looped && !reverse)
            {
                // A backward jump re-arms a once-only exit if the pose is now before the target.
                if (current < transition.ExitNormalized)
                {
                    transition.ExitFlags &= unchecked((byte)~ExitFired);
                }

                return;
            }

            bool crossed;
            if (reverse)
            {
                crossed = transition.ExitNormalized < previous && transition.ExitNormalized >= current;
            }
            else if (looped)
            {
                crossed = transition.ExitNormalized > previous || transition.ExitNormalized <= current;
            }
            else
            {
                crossed = transition.ExitNormalized <= 0f
                    ? previous <= 0f
                    : previous < transition.ExitNormalized && current >= transition.ExitNormalized;
            }

            if (!crossed && window.ClockDriven)
            {
                crossed = SpanCrosses(playback.Length, window.From, window.To, loop, transition.ExitNormalized);
            }

            if (!crossed)
            {
                return;
            }

            if (!everyLoop)
            {
                transition.ExitFlags |= ExitFired;
            }

            QueueTransitionEvent(layerIndex, slot, AnimationTransitionReason.ExitTime);
        }

        private void QueueClipEvent(int layerIndex, int slot, in AnimationClipEvent clipEvent)
        {
            if (_pendingClipEventCount >= AnimationRuntimeLimits.MaxPendingClipEventsPerCommit)
            {
                _droppedClipEvents++;
                if (layerIndex >= 0 && layerIndex < _droppedClipEventsByLayer.Length)
                {
                    _droppedClipEventsByLayer[layerIndex]++;
                }
                if (!_warnedClipEventOverflow)
                {
                    _warnedClipEventOverflow = true;
                    Warn("Clip-event dispatch budget exceeded; further markers in this commit are dropped. "
                        + "See AnimationRuntimeGraph.DroppedClipEvents. This warning is reported once per graph.");
                }

                return;
            }

            if (_pendingClipEventCount == _pendingClipEvents.Length)
            {
                int next = _pendingClipEvents.Length == 0
                    ? AnimationRuntimeLimits.InitialClipEventQueueCapacity
                    : _pendingClipEvents.Length * 2;
                if (next > AnimationRuntimeLimits.MaxPendingClipEventsPerCommit)
                {
                    next = AnimationRuntimeLimits.MaxPendingClipEventsPerCommit;
                }

                Array.Resize(ref _pendingClipEvents, next);
            }

            _pendingClipEvents[_pendingClipEventCount++] = new ClipEventDispatch(
                new AnimationPlaybackHandle(_graphId, layerIndex, slot, _layers[layerIndex].Generations[slot]),
                clipEvent);
        }

        private void DrainClipEvents()
        {
            if (_pendingClipEventCount == 0)
            {
                return;
            }

            int count = _pendingClipEventCount;
            _pendingClipEventCount = 0;
            IAnimationClipEventSink sink = _clipEventSink;
            if (sink != null)
            {
                for (int i = 0; i < count; i++)
                {
                    ClipEventDispatch dispatch = _pendingClipEvents[i];
                    try
                    {
                        sink.OnClipEvent(dispatch.Handle, dispatch.Event);
                    }
                    catch (Exception exception)
                    {
                        ReportEventSinkException(exception);
                    }
                }
            }

            // Shrink the queue after a drain.
            if (_pendingClipEvents.Length > AnimationRuntimeLimits.SteadyClipEventQueueCapacity)
            {
                Array.Resize(ref _pendingClipEvents, AnimationRuntimeLimits.SteadyClipEventQueueCapacity);
            }
        }

        // ------------------------------------------------------------------ layer API

        public void SetLayerWeight(int layerIndex, float weight)
        {
            if (layerIndex < 0 || layerIndex >= _layerCount)
            {
                return;
            }

            float clamped = weight <= 0f ? 0f : (weight >= 1f ? 1f : weight);
            ref Layer layer = ref _layers[layerIndex];
            layer.LayerFadeActive = false;
            if (layer.Weight != clamped)
            {
                layer.Weight = clamped;
                _dirty = true;
            }
        }

        /// <summary>Ramps a layer weight using the absolute <see cref="AnimationTick.TransitionTime"/>.</summary>
        public void SetLayerWeight(int layerIndex, float weight, in AnimationFade fade)
        {
            if (layerIndex < 0 || layerIndex >= _layerCount)
            {
                return;
            }

            float clamped = weight <= 0f ? 0f : (weight >= 1f ? 1f : weight);
            ref Layer layer = ref _layers[layerIndex];
            if (fade.IsInstant)
            {
                layer.LayerFadeActive = false;
                layer.Weight = clamped;
                _dirty = true;
                return;
            }

            layer.LayerFadeFrom = layer.Weight;
            layer.LayerFadeTarget = clamped;
            layer.LayerFadeDuration = fade.Duration;
            layer.LayerFadeEasing = (byte)fade.Easing;
            layer.LayerFadeStart = fade.AbsoluteStartTime;
            layer.LayerFadeActive = true;
            _dirty = true;
        }

        private void AdvanceLayerFade(ref Layer layer, double transitionTime)
        {
            float progress = layer.LayerFadeDuration <= 0f
                ? 1f
                : (float)((transitionTime - layer.LayerFadeStart) / layer.LayerFadeDuration);
            if (progress < 0f)
            {
                progress = 0f;
            }
            else if (progress > 1f)
            {
                progress = 1f;
            }

            float eased = AnimationFade.Evaluate((AnimationFadeEasing)layer.LayerFadeEasing, progress);
            layer.Weight = layer.LayerFadeFrom + ((layer.LayerFadeTarget - layer.LayerFadeFrom) * eased);
            if (progress >= 1f)
            {
                layer.LayerFadeActive = false;
            }
        }

        /// <summary>Changes the weight of the host-owned source slot without reconnecting it.</summary>
        public bool SetLayerSourceWeight(int layerIndex, float weight)
        {
            if (!_initialized || layerIndex < 0 || layerIndex >= _layerCount)
            {
                return false;
            }

            ref Layer layer = ref _layers[layerIndex];
            if (!layer.HasExternalSource)
            {
                return false;
            }

            float clamped = weight <= 0f ? 0f : (weight >= 1f ? 1f : weight);
            if (layer.ExternalWeight != clamped)
            {
                layer.ExternalWeight = clamped;
                _dirty = true;
            }

            return true;
        }

        /// <summary>Creates an Animation C# Job playable in this graph.</summary>
        /// <remarks>Attach it with <see cref="SetLayerSource"/>; the host owns the job data and node lifetime.</remarks>
        public AnimationScriptPlayable CreateJobPlayable<TJob>(in TJob job, int inputCount = 1)
            where TJob : struct, IAnimationJob
        {
            return _initialized ? AnimationScriptPlayable.Create(_graph, job, inputCount) : default;
        }

        /// <summary>Clears transition anchors, exit cursors, event windows and queued events for a replay.</summary>
        /// <remarks>Playbacks, playables and current poses are preserved.</remarks>
        public void ResetToTick(in AnimationTick tick)
        {
            if (!_initialized)
            {
                return;
            }

            for (int layerIndex = 0; layerIndex < _layerCount; layerIndex++)
            {
                ref Layer layer = ref _layers[layerIndex];
                byte remaining = layer.ActiveMask;
                while (remaining != 0)
                {
                    int slot = LowestSetBit(remaining);
                    remaining &= (byte)(remaining - 1);

                    ref PlaybackTransition transition = ref layer.Transitions[slot];
                    ref PlaybackClock clock = ref layer.Clocks[slot];
                    clock.StartLocalTime = layer.Playbacks[slot].Time;
                    clock.StartTick = tick.ClipTime;
                    transition.LastClipTime = -1d;
                    transition.LastUnwrapped = tick.ClipTime;
                    transition.Seeked = 0;
                    transition.StartTransitionTime = transition.FadeAbsoluteStart;
                    transition.FadeFired = 0;
                    transition.ExitFlags &= unchecked((byte)~ExitFired);
                    transition.ExitLastNormalized = NormalizeTime(
                        layer.Playbacks[slot].Time, layer.Playbacks[slot].Length);
                }

                layer.LayerFadeActive = false;
            }

            DiscardPendingEvents();
            _lastStepIndex = long.MinValue;
            _dirty = true;
        }

        /// <summary>FNV-1a hash of live layer and playback state; allocation-free.</summary>
        public ulong ComputePoseHash()
        {
            ulong hash = 14695981039346656037UL;
            for (int layerIndex = 0; layerIndex < _layerCount; layerIndex++)
            {
                ref Layer layer = ref _layers[layerIndex];
                hash = MixHash(hash, BitConverter.DoubleToInt64Bits(layer.Weight));
                hash = MixHash(hash, BitConverter.DoubleToInt64Bits(layer.ExternalWeight));

                byte remaining = layer.ActiveMask;
                while (remaining != 0)
                {
                    int slot = LowestSetBit(remaining);
                    remaining &= (byte)(remaining - 1);

                    ref Playback playback = ref layer.Playbacks[slot];
                    hash = MixHash(hash, playback.InstanceId);
                    hash = MixHash(hash, playback.PlaybackChannel);
                    hash = MixHash(hash, playback.ClipChannel);
                    hash = MixHash(hash, BitConverter.DoubleToInt64Bits(playback.Time));
                    hash = MixHash(hash, BitConverter.DoubleToInt64Bits(playback.Weight));
                    hash = MixHash(hash, BitConverter.DoubleToInt64Bits(playback.Length));
                    hash = MixHash(hash, playback.Flags);
                }
            }

            return hash;
        }

        private static ulong MixHash(ulong hash, long value)
        {
            return (hash ^ (ulong)value) * 1099511628211UL;
        }

        /// <summary>Markers dropped by the per-commit event budget on one layer.</summary>
        public long GetDroppedClipEvents(int layerIndex)
        {
            return layerIndex >= 0 && layerIndex < _droppedClipEventsByLayer.Length
                ? _droppedClipEventsByLayer[layerIndex]
                : 0L;
        }

        public float GetLayerWeight(int layerIndex)
        {
            return layerIndex >= 0 && layerIndex < _layerCount ? _layers[layerIndex].Weight : 0f;
        }

        /// <summary>
        /// Connects an external Playable (a locomotion blend node) as the layer's only source. Releases the
        /// layer's playbacks. This is a topology change: call it on a mode switch, never per frame.
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
                ref Playback playback = ref layer.Playbacks[slot];
                ReleasePlayback(ref layer, ref playback, slot, false);
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
            _dirty = true;
            return true;
        }

        /// <summary>Detaches the external source and returns the layer to the playback pool.</summary>
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
            _dirty = true;
        }

        // ------------------------------------------------------------------ inspection

        public AnimationRuntimeStats GetStats()
        {
            return new AnimationRuntimeStats(
                _activePlaybacks,
                _peakPlaybacks,
                _layerCount,
                PlayableCount,
                _droppedPlaybacks,
                _recreatedPlayables,
                _reusedPlayables,
                _commits,
                _evaluations,
                _nativeWrites,
                _timePolicy);
        }

        /// <summary>Zero-allocation snapshot copy for the inspector.</summary>
        public int CopyPlaybackSnapshots(int layerIndex, AnimationPlaybackSnapshot[] buffer)
        {
            if (buffer == null || layerIndex < 0 || layerIndex >= _layerCount)
            {
                return 0;
            }

            ref Layer layer = ref _layers[layerIndex];
            int written = 0;
            byte remaining = layer.ActiveMask;
            while (remaining != 0 && written < buffer.Length)
            {
                int slot = LowestSetBit(remaining);
                remaining &= (byte)(remaining - 1);

                ref Playback playback = ref layer.Playbacks[slot];
                buffer[written] = new AnimationPlaybackSnapshot(
                    playback.InstanceId,
                    playback.PlaybackChannel,
                    playback.ClipChannel,
                    playback.Time,
                    playback.Weight,
                    playback.Length,
                    (playback.Flags & PlaybackConnected) != 0);
                written++;
            }

            return written;
        }

        public int GetLayerActivePlaybacks(int layerIndex)
        {
            return layerIndex >= 0 && layerIndex < _layerCount ? _layers[layerIndex].ActivePlaybacks : 0;
        }

        /// <summary>Fixed playback capacity of a layer.</summary>
        public int GetLayerCapacity(int layerIndex)
        {
            return layerIndex >= 0 && layerIndex < _layerCount ? _layers[layerIndex].Capacity : 0;
        }

        /// <summary>
        /// Whether a handle still addresses a live playback. Use this before acting on a handle that may have been
        /// invalidated by slot reuse or a graph rebuild; <see cref="AnimationPlaybackHandle.IsValid"/> only checks
        /// that the handle was constructed, not that the playback survives.
        /// </summary>
        public bool IsPlaybackAlive(in AnimationPlaybackHandle handle)
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

            ref Playback playback = ref layer.Playbacks[handle.Slot];
            return AnimationPlaybackLiveness.IsAlive(
                handle,
                _graphId,
                layer.Generations[handle.Slot],
                (playback.Flags & PlaybackInUse) != 0);
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

                if (descriptor.Additive && descriptor.Mask == null)
                {
                    Warn("Layer " + i + " is additive with a full-body mask. Additive full-body poses need an explicit reference pose; "
                        + "a mask is usually what you want.");
                }
            }
        }

#if DEBUG
        /// <summary>Reports a humanoid/generic rig mismatch once per graph in development builds.</summary>
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
            if (_diagnostics.IsEnabled(AnimationDiagnosticLevel.Warning))
            {
                Warn("Clip '" + clip.name + "' (humanoid=" + clip.isHumanMotion
                    + ") does not match the Animator (humanoid=" + _animator.isHuman
                    + "); the resulting pose will be wrong. This warning is reported once per graph.");
            }
        }
#endif

#if DEBUG
        /// <summary>Commits between liveness scans. 30 commits is roughly half a second at 60 Hz.</summary>
        private const int LivenessCheckInterval = 30;

        /// <summary>Development guard: terminates playbacks whose clip was destroyed while sampling.</summary>
        private void CheckClipLiveness()
        {
            for (int layerIndex = 0; layerIndex < _layerCount; layerIndex++)
            {
                ref Layer layer = ref _layers[layerIndex];
                for (int slot = 0; slot < layer.Capacity; slot++)
                {
                    ref Playback playback = ref layer.Playbacks[slot];

                    if ((playback.Flags & PlaybackInUse) == 0)
                    {
                        // Drop cached playables bound to a destroyed clip.
                        if (playback.Playable.IsValid() && playback.BoundClip == null)
                        {
                            if (_graph.IsValid())
                            {
                                _graph.DestroyPlayable(playback.Playable);
                            }

                            playback.Playable = default;
                            playback.BoundClip = null;
                        }

                        if (layer.CachedPlayables[slot].IsValid() && layer.CachedClips[slot] == null)
                        {
                            if (_graph.IsValid())
                            {
                                _graph.DestroyPlayable(layer.CachedPlayables[slot]);
                            }

                            layer.CachedPlayables[slot] = default;
                        }

                        continue;
                    }

                    // Unity's == operator reports true for a destroyed object.
                    if (playback.Clip != null)
                    {
                        continue;
                    }

                    if (_diagnostics.IsEnabled(AnimationDiagnosticLevel.Error))
                    {
                        Error(
                            "Playback clip was destroyed while playing (instance " + playback.InstanceId
                            + ", channel " + playback.PlaybackChannel
                            + ", clip channel " + playback.ClipChannel
                            + ", layer " + layerIndex
                            + "). The owner released the clip before stopping playback; the playback is terminated.");
                    }

                    playback.Flags |= PlaybackPendingRelease;
                    playback.Weight = 0f;
                    _dirty = true;
                }
            }
        }
#endif

        private int ResolveLayer(int requested)
        {
            if (requested >= 0 && requested < _layerCount)
            {
                return requested;
            }

#if DEBUG
            if (!_warnedLayerClamp && _diagnostics.IsEnabled(AnimationDiagnosticLevel.Warning))
            {
                _warnedLayerClamp = true;
                int clamped = requested < 0 ? 0 : _layerCount - 1;
                Warn("Layer index " + requested + " is outside [0," + (_layerCount - 1)
                    + "]; clamped to " + clamped + ". This warning is reported once per graph.");
            }
#endif

            return requested < 0 ? 0 : _layerCount - 1;
        }

        private static int FindPlayback(ref Layer layer, int instanceId, int playbackChannel, int clipChannel)
        {
            // Full layers use a forward scan; sparse layers use the mask.
            if (layer.ActivePlaybacks >= layer.Capacity)
            {
                for (int slot = 0; slot < layer.Capacity; slot++)
                {
                    ref Playback dense = ref layer.Playbacks[slot];
                    if (dense.InstanceId == instanceId
                        && dense.PlaybackChannel == playbackChannel
                        && dense.ClipChannel == clipChannel)
                    {
                        return slot;
                    }
                }

                return -1;
            }

            byte remaining = layer.ActiveMask;
            while (remaining != 0)
            {
                int slot = LowestSetBit(remaining);
                remaining &= (byte)(remaining - 1);

                ref Playback playback = ref layer.Playbacks[slot];
                if (playback.InstanceId == instanceId
                    && playback.PlaybackChannel == playbackChannel
                    && playback.ClipChannel == clipChannel)
                {
                    return slot;
                }
            }

            return -1;
        }

        private static int FindFreeSlot(ref Layer layer)
        {
            // Only slots below the configured capacity may be handed out.
            byte slotMask = (byte)((1 << layer.Capacity) - 1);
            byte free = (byte)(~layer.ActiveMask & slotMask);
            return free == 0 ? -1 : LowestSetBit(free);
        }

        /// <summary>Index of the lowest set bit in a non-zero byte.</summary>
        private static int LowestSetBit(byte mask)
        {
            // Widen once to int so the three probe steps contain no repeated byte casts.
            int bits = mask;
            int index = 0;
            if ((bits & 0x0F) == 0)
            {
                bits >>= 4;
                index += 4;
            }

            if ((bits & 0x03) == 0)
            {
                bits >>= 2;
                index += 2;
            }

            if ((bits & 0x01) == 0)
            {
                index += 1;
            }

            return index;
        }

        private static double ClampTime(double time, float length)
        {
            if (double.IsNaN(time) || time < 0d)
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
