using System;
using CycloneGames.AnimationRuntime.Core;
using CycloneGames.Logging;
using UnityEngine;

namespace CycloneGames.AnimationRuntime
{
    /// <summary>Unity phase in which the graph is committed. Must match the actor's movement model.</summary>
    public enum AnimationCommitPhase : byte
    {
        /// <summary>
        /// Commits in Update. Unity runs this component at order -100 inside the phase, so other Update scripts
        /// that keep the default order (0) run after it. Use it only when the pose does not need to see this
        /// frame's gameplay writes, or when the host drives <c>Commit(tick)</c> from a later order.
        /// </summary>
        Update = 0,

        /// <summary>
        /// Commits in LateUpdate at order -100: after every Update script, before LateUpdate cameras at order 0.
        /// Changing the attribute moves the pose relative to LateUpdate consumers.
        /// </summary>
        LateUpdate = 1,

        /// <summary>Inside the physics step. Required when a movement component consumes root motion per fixed step.</summary>
        FixedUpdate = 2
    }

    /// <summary>Who reads <c>Animator.deltaPosition</c> and moves the character.</summary>
    public enum RootMotionOwnership : byte
    {
        /// <summary>A movement component owns root motion. The runtime never implements OnAnimatorMove.</summary>
        External = 0,

        /// <summary>The runtime owns root motion and forwards filtered deltas through <see cref="AnimationRuntimeBehaviour.RootMotionApplied"/>.</summary>
        Backend = 1,

        /// <summary>Root motion is ignored.</summary>
        Disabled = 2
    }

    /// <summary>
    /// Scene binding for <see cref="AnimationRuntimeGraph"/>. Coordinates lifecycle, the commit phase and root
    /// motion ownership only; it produces ticks and contains no animation logic.
    ///
    /// In <see cref="AnimationTimePolicy.External"/> the internal tick is diagnostic: clips advance only when the
    /// host calls <c>BeginClip</c>/<c>UpdateClip</c>. In <see cref="AnimationTimePolicy.TickDriven"/> the
    /// component's clock is authoritative and clips advance on their own.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-100)]
    [AddComponentMenu("CycloneGames/Animation/Animation Runtime")]
    public sealed class AnimationRuntimeBehaviour : MonoBehaviour
    {
        [Header("Binding")]
        [Tooltip("Animator sampled by the runtime graph. Falls back to a child Animator when empty. Backend root motion requires the Animator on this same GameObject.")]
        [SerializeField] private Animator _animator;

        [Tooltip("Layer stack for this character archetype. Empty falls back to one full-body layer with four slots.")]
        [SerializeField] private AnimationLayerProfile _profile;

        [Header("Time")]
        [Tooltip("External: the host supplies absolute clip time through UpdateClip. TickDriven: this component advances clips from its own clock.")]
        [SerializeField] private AnimationTimePolicy _timePolicy = AnimationTimePolicy.External;

        [Header("Frame Model")]
        [Tooltip("Phase in which the graph is committed. FixedUpdate is required when a movement component consumes root motion per physics step.")]
        [SerializeField] private AnimationCommitPhase _commitPhase = AnimationCommitPhase.LateUpdate;

        [Tooltip("Who applies root motion. External keeps an existing movement component in charge.")]
        [SerializeField] private RootMotionOwnership _rootMotionOwnership = RootMotionOwnership.External;

        [Tooltip("Axes forwarded when the runtime owns root motion.")]
        [SerializeField] private RootMotionAxis _rootMotionAxis = RootMotionAxis.Horizontal;

        [Tooltip("Multiplier applied to forwarded root motion.")]
        [Min(0f)]
        [SerializeField] private float _rootMotionScale = 1f;

        [Header("Lifecycle")]
        [Tooltip("Initialize on Awake. Disable to call Initialize from a composition root.")]
        [SerializeField] private bool _initializeOnAwake = true;

        [Tooltip("Forward runtime warnings and errors to the Console.")]
        [SerializeField] private bool _logDiagnostics = true;

        [Tooltip("Dispatch AnimationClip events. Disable when a timeline (Choreography) owns event dispatch.")]
        [SerializeField] private bool _dispatchClipEvents = true;

        private AnimationRuntimeGraph _graph;
        private ILogWriter _logWriter;
        private long _stepIndex = -1;
        private bool _rootMotionActive;
        private bool _disabledByMissingAnimator;
        private IAnimationClipEventSink _clipEventSink;

        /// <summary>Raised once per frame when root motion is actually forwarded by this component.</summary>
        public event Action<Vector3, Quaternion> RootMotionApplied;

        /// <summary>Raised after a commit when a fade completes. Mirrors <see cref="AnimationRuntimeGraph.TransitionCompleted"/>.</summary>
        public event Action<AnimationPlaybackHandle, AnimationTransitionReason> TransitionCompleted;

        public AnimationRuntimeGraph Graph => _graph;

        public bool IsInitialized => _graph != null && _graph.IsInitialized;

        public Animator TargetAnimator => _animator;

        public AnimationCommitPhase CommitPhase => _commitPhase;

        public AnimationTimePolicy TimePolicy => _graph != null ? _graph.TimePolicy : _timePolicy;

        /// <summary>
        /// True only when Backend ownership is configured, the Animator lives on this GameObject, and the graph
        /// initialized. False means <see cref="RootMotionApplied"/> will never fire.
        /// </summary>
        public bool IsRootMotionActive => _rootMotionActive;

        // ------------------------------------------------------------------ lifecycle

        private void Awake()
        {
            if (_initializeOnAwake)
            {
                Initialize();
            }
        }

        /// <summary>Injects an explicit log writer, then builds the graph. Call before gameplay starts.</summary>
        public void Initialize(ILogWriter logWriter)
        {
            _logWriter = logWriter;
            Initialize();
        }

        /// <summary>Builds the graph. Safe to call twice; the second call is a no-op.</summary>
        public void Initialize()
        {
            if (_graph != null && _graph.IsInitialized)
            {
                return;
            }

            if (_graph != null)
            {
                // A previous initialization failed or was disposed; do not leave a half-live graph subscribed.
                _graph.TransitionCompleted -= HandleGraphTransition;
                _graph.Dispose();
                _graph = null;
            }

            Animator animator = _animator != null ? _animator : GetComponentInChildren<Animator>(true);
            if (animator == null)
            {
                AnimationRuntimeLog.Channel.Error(
                    "[AnimationRuntime] No Animator assigned and none found in children; the component is disabled.");
                _disabledByMissingAnimator = true;
                enabled = false;
                return;
            }

            _animator = animator;

            AnimationLayerDescriptor[] descriptors = _profile != null
                ? _profile.ToDescriptors()
                : new[] { AnimationLayerDescriptor.Default };

            LogChannel channel = _logWriter != null
                ? AnimationRuntimeLog.Create(_logWriter)
                : AnimationRuntimeLog.Channel;

            IAnimationDiagnostics diagnostics = _logDiagnostics
                ? (IAnimationDiagnostics)new LoggingAnimationDiagnostics(channel)
                : NullAnimationDiagnostics.Instance;

            _graph = new AnimationRuntimeGraph(animator, descriptors, diagnostics, _timePolicy, _dispatchClipEvents);
            _graph.SetClipEventSink(_clipEventSink);
            _graph.TransitionCompleted += HandleGraphTransition;

            RebuildRootMotionState(animator, true);
            _stepIndex = -1;

            if (_disabledByMissingAnimator)
            {
                _disabledByMissingAnimator = false;
                enabled = true;
            }
        }

        private void OnEnable()
        {
            // A disabled interval leaves the tick clocks behind; re-anchor so re-enabling never jumps the clip.
            if (_graph != null && _graph.IsInitialized)
            {
                _graph.RebaseTickClocks();
            }
        }

        private void OnDestroy()
        {
            if (_graph != null)
            {
                _graph.TransitionCompleted -= HandleGraphTransition;
                _graph.Dispose();
                _graph = null;
            }
        }

        // ------------------------------------------------------------------ commit

        private void Update()
        {
            if (_commitPhase == AnimationCommitPhase.Update)
            {
                CommitNow();
            }
        }

        private void LateUpdate()
        {
            if (_commitPhase == AnimationCommitPhase.LateUpdate)
            {
                CommitNow();
            }
        }

        private void FixedUpdate()
        {
            if (_commitPhase == AnimationCommitPhase.FixedUpdate)
            {
                CommitNow();
            }
        }

        /// <summary>
        /// Commits with an internally produced tick. Hosts that own an external time authority should call
        /// <see cref="Commit(in AnimationTick)"/> instead and leave the phase at its default.
        /// </summary>
        [ContextMenu("Commit Now")]
        public void CommitNow()
        {
            if (_graph == null || !_graph.IsInitialized)
            {
                return;
            }

            // AnimationTick treats a negative step index as invalid, so wrap before the signed limit.
            _stepIndex = _stepIndex == long.MaxValue ? 0L : _stepIndex + 1L;
            AnimationTimeSource source;
            switch (_commitPhase)
            {
                case AnimationCommitPhase.Update:
                    source = AnimationTimeSource.Update;
                    break;
                case AnimationCommitPhase.FixedUpdate:
                    source = AnimationTimeSource.FixedUpdate;
                    break;
                default:
                    source = AnimationTimeSource.LateUpdate;
                    break;
            }

            var tick = new AnimationTick(
                Time.timeAsDouble,
                _stepIndex,
                source,
                false,
                _commitPhase == AnimationCommitPhase.FixedUpdate ? Time.fixedDeltaTime : Time.deltaTime);

            _graph.Commit(tick);
        }

        /// <summary>Commits with an externally produced tick (Choreography step, replay, editor scrub).</summary>
        public void Commit(in AnimationTick tick)
        {
            if (_graph == null || !_graph.IsInitialized)
            {
                return;
            }

            _stepIndex = tick.StepIndex;
            _graph.Commit(tick);
        }

        // ------------------------------------------------------------------ playback facade

        public AnimationPlaybackHandle BeginClip(in AnimationClipRequest request)
        {
            return _graph != null && _graph.IsInitialized ? _graph.BeginClip(request) : AnimationPlaybackHandle.Invalid;
        }

        /// <summary>Starts or replaces a playback and ramps its weight to the fade target.</summary>
        public AnimationPlaybackHandle BeginClip(in AnimationClipRequest request, in AnimationFade fade)
        {
            return _graph != null && _graph.IsInitialized ? _graph.BeginClip(request, fade) : AnimationPlaybackHandle.Invalid;
        }

        /// <summary>Ramps a live playback from its current weight to the fade target.</summary>
        public bool FadePlayback(in AnimationPlaybackKey key, in AnimationFade fade)
        {
            return _graph != null && _graph.IsInitialized && _graph.FadePlayback(key, fade);
        }

        /// <summary>Arms a normalized exit point; the completion arrives through <see cref="TransitionCompleted"/>.</summary>
        public bool SetExitTime(in AnimationPlaybackKey key, in AnimationExitTime exit)
        {
            return _graph != null && _graph.IsInitialized && _graph.SetExitTime(key, exit);
        }

        public void ClearExitTime(in AnimationPlaybackKey key)
        {
            if (_graph != null && _graph.IsInitialized)
            {
                _graph.ClearExitTime(key);
            }
        }

        /// <summary>Registers the clip-event receiver; applied to the current and any later graph.</summary>
        public void SetClipEventSink(IAnimationClipEventSink sink)
        {
            _clipEventSink = sink;
            if (_graph != null)
            {
                _graph.SetClipEventSink(sink);
            }
        }

        public bool DispatchClipEvents
        {
            get => _graph != null ? _graph.DispatchClipEvents : _dispatchClipEvents;
            set
            {
                _dispatchClipEvents = value;
                if (_graph != null)
                {
                    _graph.DispatchClipEvents = value;
                }
            }
        }

        public void UpdateClip(in AnimationClipRequest request)
        {
            if (_graph != null && _graph.IsInitialized)
            {
                _graph.UpdateClip(request);
            }
        }

        public bool SeekClip(in AnimationPlaybackKey key, double localTime)
        {
            return _graph != null && _graph.IsInitialized && _graph.SeekClip(key, localTime);
        }

        public void EndClip(in AnimationPlaybackKey key)
        {
            if (_graph != null && _graph.IsInitialized)
            {
                _graph.EndClip(key);
            }
        }

        /// <summary>Fades a playback out and releases it when the ramp completes.</summary>
        public void EndClip(in AnimationPlaybackKey key, in AnimationFade fade)
        {
            if (_graph != null && _graph.IsInitialized)
            {
                _graph.EndClip(key, fade);
            }
        }

        private void HandleGraphTransition(AnimationPlaybackHandle handle, AnimationTransitionReason reason)
        {
            TransitionCompleted?.Invoke(handle, reason);
        }

        public void StopAll()
        {
            if (_graph != null && _graph.IsInitialized)
            {
                _graph.StopAll();
            }
        }

        /// <summary>
        /// Rebinds to another Animator, rebuilding the graph. Every playback and handle is invalidated. Returns false
        /// and leaves the current binding untouched when the target is already driven by another graph.
        /// </summary>
        public bool TryRebind(Animator animator)
        {
            if (animator == null)
            {
                throw new ArgumentNullException(nameof(animator));
            }

            if (_graph == null)
            {
                _animator = animator;
                Initialize();
                return IsInitialized;
            }

            if (!_graph.TryRebind(animator))
            {
                return false;
            }

            _animator = animator;
            RebuildRootMotionState(animator, true);
            return true;
        }

        public void SetLayerWeight(int layerIndex, float weight)
        {
            if (_graph != null && _graph.IsInitialized)
            {
                _graph.SetLayerWeight(layerIndex, weight);
            }
        }

        public void SetLayerSource(int layerIndex, UnityEngine.Playables.Playable source, float weight = 1f)
        {
            if (_graph != null && _graph.IsInitialized)
            {
                _graph.SetLayerSource(layerIndex, source, weight);
            }
        }

        public void ClearLayerSource(int layerIndex)
        {
            if (_graph != null && _graph.IsInitialized)
            {
                _graph.ClearLayerSource(layerIndex);
            }
        }

        // ------------------------------------------------------------------ root motion

        /// <summary>
        /// OnAnimatorMove is delivered to the GameObject that owns the Animator. A child-rig setup therefore
        /// cannot forward root motion from this component, so Backend ownership is rejected instead of silently
        /// doing nothing.
        /// </summary>
        private void RebuildRootMotionState(Animator animator, bool report)
        {
            bool sameObject = animator != null && animator.gameObject == gameObject;
            _rootMotionActive = _rootMotionOwnership == RootMotionOwnership.Backend && sameObject;

            if (_rootMotionOwnership != RootMotionOwnership.Backend || animator == null)
            {
                return;
            }

            if (!sameObject)
            {
                if (report && _logDiagnostics)
                {
                    AnimationRuntimeLog.Channel.Error(
                        "[AnimationRuntime] Backend root motion requires the Animator on the same GameObject as "
                        + "AnimationRuntimeBehaviour. The Animator is on '" + animator.name
                        + "', this component is on '" + name
                        + "'; root motion is disabled. Move the component, or use RootMotionOwnership.External.");
                }

                return;
            }

            animator.applyRootMotion = true;
        }

        private void OnAnimatorMove()
        {
            if (!_rootMotionActive || _animator == null)
            {
                return;
            }

            Vector3 delta = _animator.deltaPosition * _rootMotionScale;
            Quaternion rotation = _animator.deltaRotation;

            if ((_rootMotionAxis & RootMotionAxis.X) == 0) delta.x = 0f;
            if ((_rootMotionAxis & RootMotionAxis.Y) == 0) delta.y = 0f;
            if ((_rootMotionAxis & RootMotionAxis.Z) == 0) delta.z = 0f;
            if ((_rootMotionAxis & RootMotionAxis.Rotation) == 0) rotation = Quaternion.identity;

            RootMotionApplied?.Invoke(delta, rotation);
        }

        // ------------------------------------------------------------------ tooling

        [ContextMenu("Log Stats")]
        private void LogStats()
        {
            if (_graph == null || !_graph.IsInitialized)
            {
                AnimationRuntimeLog.Channel.Warning("[AnimationRuntime] graph is not initialized.");
                return;
            }

            AnimationRuntimeStats stats = _graph.GetStats();
            AnimationRuntimeLog.Channel.Info(
                "[AnimationRuntime] layers=" + stats.LayerCount
                + " playables=" + stats.PlayableCount
                + " playbacks=" + stats.ActivePlaybacks + "/" + stats.PeakPlaybacks
                + " dropped=" + stats.DroppedPlaybacks
                + " recreated=" + stats.RecreatedPlayables
                + " reused=" + stats.ReusedPlayables
                + " commits=" + stats.Commits
                + " evaluations=" + stats.Evaluations
                + " writes=" + stats.NativeWrites);
        }
    }
}
