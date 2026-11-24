using System;
using System.Diagnostics;
using CycloneGames.AnimationRuntime.Core;
using CycloneGames.Logging;
using UnityEngine;

namespace CycloneGames.AnimationRuntime
{
    /// <summary>Unity phase in which the graph is committed. Must match the actor's movement model.</summary>
    public enum AnimationCommitPhase : byte
    {
        /// <summary>Commits in Update at execution order -100.</summary>
        Update = 0,

        /// <summary>Commits in LateUpdate at execution order -100.</summary>
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

    /// <summary>Receives Unity's IK pass after the pose has been evaluated. Registered once per binding.</summary>
    public interface IAnimationIKPass
    {
        void OnAnimatorIK(int layerIndex);
    }

    /// <summary>Phases recorded by the development ordering probe.</summary>
    [Flags]
    public enum AnimationRuntimePhase : byte
    {
        Update = 1 << 0,
        LateUpdate = 1 << 1,
        FixedUpdate = 1 << 2,
        Commit = 1 << 3,
        AnimatorMove = 1 << 4,
        AnimatorIK = 1 << 5
    }

    /// <summary>Binds <see cref="AnimationRuntimeGraph"/> to a scene Animator and produces commit ticks.</summary>
    /// <remarks>In External mode the host advances clips through BeginPlayback/UpdatePlayback.</remarks>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-100)]
    [AddComponentMenu("CycloneGames/Animation/Animation Runtime")]
    public sealed class AnimationRuntimeBehaviour : MonoBehaviour
    {
        [Tooltip("Animator sampled by the runtime graph. Falls back to a child Animator when empty. Backend root motion requires the Animator on this same GameObject.")]
        [SerializeField] private Animator _animator;

        [Tooltip("Layer stack for this character archetype. Empty falls back to one full-body layer with four slots.")]
        [SerializeField] private AnimationLayerProfile _profile;

        [Tooltip("External: the host supplies absolute clip time through UpdatePlayback. TickDriven: this component advances clips from its own clock.")]
        [SerializeField] private AnimationTimePolicy _timePolicy = AnimationTimePolicy.External;

        [Tooltip("Phase in which the graph is committed. FixedUpdate is required when a movement component consumes root motion per physics step. LateUpdate commits after OnAnimatorIK, so an IK pass sees the previous pose.")]
        [SerializeField] private AnimationCommitPhase _commitPhase = AnimationCommitPhase.LateUpdate;

        [Tooltip("Who applies root motion. External keeps an existing movement component in charge.")]
        [SerializeField] private RootMotionOwnership _rootMotionOwnership = RootMotionOwnership.External;

        [Tooltip("Axes forwarded when the runtime owns root motion.")]
        [SerializeField] private RootMotionAxis _rootMotionAxis = RootMotionAxis.Horizontal;

        [Tooltip("Multiplier applied to forwarded root motion.")]
        [Min(0f)]
        [SerializeField] private float _rootMotionScale = 1f;

        [Tooltip("Initialize on Awake. Disable to call Initialize from a composition root.")]
        [SerializeField] private bool _initializeOnAwake = true;

        [Tooltip("Forward runtime warnings and errors to the Console.")]
        [SerializeField] private bool _logDiagnostics = true;

        [Tooltip("Dispatch AnimationClip events. Disable when a timeline (Choreography) owns event dispatch.")]
        [SerializeField] private bool _dispatchClipEvents = true;

        [Tooltip("Commit rate in Hz. 0 commits every frame/step. Lower rates skip commits but pass the full accumulated delta to the next one, so fades and tick-driven clocks stay correct.")]
        [Min(0f)]
        [SerializeField] private float _updateRateHz = 0f;

        [Tooltip("Skip commits while a Renderer on this GameObject is culled. Graph state is preserved; no playable is destroyed.")]
        [SerializeField] private bool _pauseWhenInvisible = false;

        private AnimationRuntimeGraph _graph;
        private ILogWriter _logWriter;
        private long _stepIndex = -1;
        private double _lastCommitClock = -1d;
        private bool _culled;
        private bool _rootMotionActive;
        private bool _disabledByMissingAnimator;
        private bool _warnedFrameModel;
        private bool _warnedRootMotionSink;
        private int _phaseMask;
        private int _phaseFrame = -1;
        private IAnimationIKPass _ikPass;
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

        /// <summary>True when an IK pass is registered. Unity calls OnAnimatorIK before LateUpdate.</summary>
        public bool HasIKPass => _ikPass != null;

        /// <summary>Registers the receiver for Unity's IK pass. Pass null to detach.</summary>
        public void SetIKPass(IAnimationIKPass pass)
        {
            _ikPass = pass;
        }

        /// <summary>Bit mask of phases seen in the current rendered frame. Development builds only.</summary>
        public int PhaseOrderMask => _phaseMask;

        // Conditional removes every call in non-development builds. frameCount changes once per
        // rendered frame, so a FixedUpdate earlier in the same frame is not cleared by Update.
        [Conditional("DEBUG")]
        private void RecordPhase(AnimationRuntimePhase phase)
        {
            int frame = Time.frameCount;
            if (_phaseFrame != frame)
            {
                _phaseFrame = frame;
                _phaseMask = 0;
            }

            _phaseMask |= (int)phase;
        }

        /// <summary>Commit cadence in Hz; 0 commits every frame or fixed step.</summary>
        public float UpdateRateHz
        {
            get => _updateRateHz;
            set => _updateRateHz = AnimationClockMath.IsFinite(value) && value > 0f ? value : 0f;
        }

        /// <summary>Whether commits are skipped while a Renderer on this GameObject is culled.</summary>
        public bool PauseWhenInvisible
        {
            get => _pauseWhenInvisible;
            set => _pauseWhenInvisible = value;
        }

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

            Animator animator = _animator;
            if (animator == null)
            {
#if DEBUG
                Animator[] animators = GetComponentsInChildren<Animator>(true);
                if (animators.Length > 1)
                {
                    AnimationRuntimeLog.Channel.Warning(
                        "[AnimationRuntime] Multiple Animators found under '" + name + "'; using '"
                        + animators[0].name + "'. Assign the Animator field explicitly to avoid driving the wrong rig.");
                }

                animator = animators.Length > 0 ? animators[0] : null;
#else
                animator = GetComponentInChildren<Animator>(true);
#endif
            }

            if (animator == null)
            {
                AnimationRuntimeLog.Channel.Error(
                    "[AnimationRuntime] No Animator assigned and none found in children; the component is disabled.");
                _disabledByMissingAnimator = true;
                enabled = false;
                return;
            }

            _animator = animator;

#if DEBUG
            ValidateFrameModel(animator);
#endif

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
            _lastCommitClock = -1d;
            _culled = false;
        }

        /// <summary>Pauses commits while a Renderer on this GameObject is culled.</summary>
        private void OnBecameInvisible()
        {
            _culled = true;
        }

        private void OnBecameVisible()
        {
            _culled = false;
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
            RecordPhase(AnimationRuntimePhase.Update);
            if (_commitPhase == AnimationCommitPhase.Update)
            {
                TickFrame(false);
            }
        }

        private void LateUpdate()
        {
            RecordPhase(AnimationRuntimePhase.LateUpdate);
            if (_commitPhase == AnimationCommitPhase.LateUpdate)
            {
                TickFrame(false);
            }
        }

        private void FixedUpdate()
        {
            RecordPhase(AnimationRuntimePhase.FixedUpdate);
            if (_commitPhase == AnimationCommitPhase.FixedUpdate)
            {
                TickFrame(true);
            }
        }

        /// <summary>Commits once per physics step in FixedUpdate mode; rate LOD applies only to frame phases.</summary>
        private void TickFrame(bool fixedPhase)
        {
            if (fixedPhase)
            {
                CommitNow();
                return;
            }

            if (_pauseWhenInvisible && _culled)
            {
                return;
            }

            double clock = Time.timeAsDouble;
            if (_updateRateHz > 0f && clock - _lastCommitClock < 1d / _updateRateHz)
            {
                return;
            }

            CommitNow();
        }

        /// <summary>Commits with an internally produced absolute tick.</summary>
        [ContextMenu("Commit Now")]
        public void CommitNow()
        {
            if (_graph == null || !_graph.IsInitialized)
            {
                return;
            }

            RecordPhase(AnimationRuntimePhase.Commit);

            // Negative step indices are invalid; wrap before the signed limit.
            _stepIndex = _stepIndex == long.MaxValue ? 0L : _stepIndex + 1L;
            bool fixedPhase = _commitPhase == AnimationCommitPhase.FixedUpdate;

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

            double clock = fixedPhase ? Time.fixedTimeAsDouble : Time.timeAsDouble;
            _lastCommitClock = clock;

            _graph.Commit(new AnimationTick(clock, _stepIndex, source));
        }

        /// <summary>Commits with an externally produced tick.</summary>
        public void Commit(in AnimationTick tick)
        {
            if (_graph == null || !_graph.IsInitialized)
            {
                return;
            }

            RecordPhase(AnimationRuntimePhase.Commit);
            _stepIndex = tick.StepIndex;
            _lastCommitClock = tick.ClipTime;
            _graph.Commit(tick);
        }

        // ------------------------------------------------------------------ playback facade

        public AnimationPlaybackHandle BeginPlayback(in AnimationClipRequest request)
        {
            return _graph != null && _graph.IsInitialized ? _graph.BeginPlayback(request) : AnimationPlaybackHandle.Invalid;
        }

        /// <summary>Starts or replaces a playback and ramps its weight to the fade target.</summary>
        public AnimationPlaybackHandle BeginPlayback(in AnimationClipRequest request, in AnimationFade fade)
        {
            return _graph != null && _graph.IsInitialized ? _graph.BeginPlayback(request, fade) : AnimationPlaybackHandle.Invalid;
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

        public void UpdatePlayback(in AnimationClipRequest request)
        {
            if (_graph != null && _graph.IsInitialized)
            {
                _graph.UpdatePlayback(request);
            }
        }

        public bool SeekPlayback(in AnimationPlaybackKey key, double localTime)
        {
            return _graph != null && _graph.IsInitialized && _graph.SeekPlayback(key, localTime);
        }

        public void EndPlayback(in AnimationPlaybackKey key)
        {
            if (_graph != null && _graph.IsInitialized)
            {
                _graph.EndPlayback(key);
            }
        }

        /// <summary>Fades a playback out and releases it when the ramp completes.</summary>
        public void EndPlayback(in AnimationPlaybackKey key, in AnimationFade fade)
        {
            if (_graph != null && _graph.IsInitialized)
            {
                _graph.EndPlayback(key, fade);
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

        /// <summary>Rebuilds Backend root-motion ownership; requires the Animator on this GameObject.</summary>
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

        /// <summary>Forwards Unity's IK pass to the registered <see cref="IAnimationIKPass"/>.</summary>
        private void OnAnimatorIK(int layerIndex)
        {
            RecordPhase(AnimationRuntimePhase.AnimatorIK);
            _ikPass?.OnAnimatorIK(layerIndex);
        }

        private void OnAnimatorMove()
        {
            RecordPhase(AnimationRuntimePhase.AnimatorMove);
            if (!_rootMotionActive || _animator == null)
            {
                return;
            }

#if DEBUG
            if (RootMotionApplied == null && !_warnedRootMotionSink)
            {
                _warnedRootMotionSink = true;
                AnimationRuntimeLog.Channel.Error(
                    "[AnimationRuntime] Backend root motion is active but no RootMotionApplied handler is "
                    + "subscribed. Implementing OnAnimatorMove disables Unity's automatic root motion; the "
                    + "character will not move until a movement component consumes the forwarded delta.");
            }
#endif

            float scale = _rootMotionScale;
            if (!AnimationClockMath.IsFinite(scale) || scale < 0f)
            {
                scale = 0f;
            }

            Vector3 delta = _animator.deltaPosition * scale;
            Quaternion rotation = _animator.deltaRotation;

            if ((_rootMotionAxis & RootMotionAxis.X) == 0) delta.x = 0f;
            if ((_rootMotionAxis & RootMotionAxis.Y) == 0) delta.y = 0f;
            if ((_rootMotionAxis & RootMotionAxis.Z) == 0) delta.z = 0f;
            if ((_rootMotionAxis & RootMotionAxis.Rotation) == 0) rotation = Quaternion.identity;

            RootMotionApplied?.Invoke(delta, rotation);
        }

#if DEBUG
        /// <summary>Reports a mismatched commit phase and Animator.updateMode once per binding.</summary>
        private void ValidateFrameModel(Animator animator)
        {
            if (_warnedFrameModel || animator == null)
            {
                return;
            }

            bool physicsPhase = _commitPhase == AnimationCommitPhase.FixedUpdate;
            bool physicsUpdate = animator.updateMode == AnimatorUpdateMode.AnimatePhysics;
            bool modeMismatch = physicsPhase != physicsUpdate;
            bool lodMismatch = physicsPhase && (_updateRateHz > 0f || _pauseWhenInvisible);
            if (!modeMismatch && !lodMismatch)
            {
                return;
            }

            _warnedFrameModel = true;
            AnimationRuntimeLog.Channel.Error(
                "[AnimationRuntime] Frame model mismatch: commit phase is " + _commitPhase
                + ", Animator.updateMode is " + animator.updateMode
                + (lodMismatch
                    ? ", and commit-rate LOD / visibility skipping are enabled (invalid in M3; ignored)"
                    : string.Empty)
                + ". Use AnimatePhysics for FixedUpdate commits and Normal otherwise; every physics step "
                + "must produce exactly one commit.");
        }
#endif

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
