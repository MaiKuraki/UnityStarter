using System;
using CycloneGames.AnimationRuntime.Core;
using CycloneGames.Logging;
using UnityEngine;

namespace CycloneGames.AnimationRuntime
{
    /// <summary>Unity phase in which the graph is committed. Must match the actor's movement model.</summary>
    public enum AnimationCommitPhase : byte
    {
        /// <summary>After gameplay Update. Use for visual-only actors and Update-driven controllers.</summary>
        Update = 0,

        /// <summary>After gameplay LateUpdate. Default for visual-only actors.</summary>
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
    /// Scene binding for <see cref="AnimationRuntimeGraph"/>. Coordinates lifecycle and the commit phase only;
    /// produces absolute time ticks and contains no animation logic.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-100)]
    [AddComponentMenu("CycloneGames/Animation/Animation Runtime")]
    public sealed class AnimationRuntimeBehaviour : MonoBehaviour
    {
        [Header("Binding")]
        [Tooltip("Animator sampled by the runtime graph. Falls back to a child Animator when empty.")]
        [SerializeField] private Animator _animator;

        [Tooltip("Layer stack for this character archetype. Empty falls back to one full-body layer with four slots.")]
        [SerializeField] private AnimationLayerProfile _profile;

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

        private AnimationRuntimeGraph _graph;
        private ILogWriter _logWriter;
        private long _stepIndex = -1;

        /// <summary>Raised once per frame when <see cref="RootMotionOwnership.Backend"/> is active.</summary>
        public event Action<Vector3, Quaternion> RootMotionApplied;

        public AnimationRuntimeGraph Graph => _graph;

        public bool IsInitialized => _graph != null && _graph.IsInitialized;

        public Animator TargetAnimator => _animator;

        public AnimationCommitPhase CommitPhase => _commitPhase;

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

            Animator animator = _animator != null ? _animator : GetComponentInChildren<Animator>(true);
            if (animator == null)
            {
                AnimationRuntimeLog.Channel.Error(
                    "[AnimationRuntime] No Animator assigned and none found in children; the component is disabled.");
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

            _graph = new AnimationRuntimeGraph(animator, descriptors, diagnostics);

            if (_rootMotionOwnership == RootMotionOwnership.Backend)
            {
                animator.applyRootMotion = true;
            }

            _stepIndex = -1;
        }

        private void OnDestroy()
        {
            if (_graph != null)
            {
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

            _stepIndex++;
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
                0d,
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

        public AnimationVoiceHandle BeginClip(in AnimationClipRequest request)
        {
            return _graph != null && _graph.IsInitialized ? _graph.BeginClip(request) : AnimationVoiceHandle.Invalid;
        }

        public void UpdateClip(in AnimationClipRequest request)
        {
            if (_graph != null && _graph.IsInitialized)
            {
                _graph.UpdateClip(request);
            }
        }

        public void EndClip(in AnimationVoiceKey key)
        {
            if (_graph != null && _graph.IsInitialized)
            {
                _graph.EndClip(key);
            }
        }

        public void StopAll()
        {
            if (_graph != null && _graph.IsInitialized)
            {
                _graph.StopAll();
            }
        }

        /// <summary>
        /// Rebinds to another Animator, rebuilding the graph. Every voice and handle is invalidated. Use this for
        /// pooled actors that swap models, or after replacing the Animator component.
        /// </summary>
        public void Rebind(Animator animator)
        {
            if (animator == null)
            {
                throw new ArgumentNullException(nameof(animator));
            }

            if (_graph == null)
            {
                _animator = animator;
                Initialize();
                return;
            }

            // Only adopt the new Animator once the swap succeeded; a failed rebind must leave the component
            // pointing at the Animator it is actually driving.
            _graph.Rebind(animator);
            _animator = animator;

            if (_rootMotionOwnership == RootMotionOwnership.Backend)
            {
                animator.applyRootMotion = true;
            }
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

        private void OnAnimatorMove()
        {
            if (_rootMotionOwnership != RootMotionOwnership.Backend || _animator == null)
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
                + " voices=" + stats.ActiveVoices + "/" + stats.PeakVoices
                + " dropped=" + stats.DroppedVoices
                + " recreated=" + stats.RecreatedPlayables
                + " commits=" + stats.Commits
                + " writes=" + stats.NativeWrites);
        }
    }
}
