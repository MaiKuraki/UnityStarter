using UnityEngine;
using System.Collections.Generic;

namespace CycloneGames.GameplayFramework.Runtime
{
    /// <summary>
    /// Defines the per-frame evaluation logic for mid-state normalized-time triggers.
    /// Implement this interface and override <see cref="CameraActionStateBehaviour.ResolveProgressStrategy"/>
    /// in a subclass to inject custom trigger timing behaviour without modifying base-package code.
    /// </summary>
    public interface ICameraProgressTriggerStrategy
    {
        /// <summary>Returns true when the strategy decides the action should fire this frame.</summary>
        bool ShouldFire(in CameraActionStateBehaviour.TriggerState state, int currentLoop, float currentNormalizedInLoop, float threshold);

        /// <summary>Advances the state record after the fire decision has been made.</summary>
        void UpdateStateAfterEvaluation(ref CameraActionStateBehaviour.TriggerState state, int currentLoop, float currentNormalizedInLoop, bool fired);
    }

    /// <summary>
    /// Defines the action taken on Animator state exit.
    /// Implement this interface and override <see cref="CameraActionStateBehaviour.ResolveExitActionStrategy"/>
    /// in a subclass to inject custom exit behaviour without modifying base-package code.
    /// </summary>
    public interface ICameraExitActionStrategy
    {
        /// <summary>Executes the exit action against the provided binding.</summary>
        void Execute(CameraActionBinding binding, string stopKey, string playKey, float durationOverride);
    }

    /// <summary>
    /// StateMachineBehaviour that triggers camera action presets when entering or exiting an Animator state.
    ///
    /// Usage:
    ///   1. Select an Animator state in the Animator window.
    ///   2. Click the '+' button under "Add Behaviour" and choose CameraActionStateBehaviour.
    ///   3. Fill in OnEnterActionKey (play on enter) and/or OnExitActionKey (stop on exit).
    ///
    /// The binding is resolved automatically from the Animator's GameObject or its parents.
    ///
    /// Extension points for external packages:
    ///   - Subclass this class and override <see cref="ResolveProgressStrategy"/> to swap in a custom
    ///     <see cref="ICameraProgressTriggerStrategy"/> (e.g. multi-threshold firing).
    ///   - Override <see cref="ResolveExitActionStrategy"/> to replace the built-in exit behaviour.
    ///   - Override OnStateEnter / OnStateUpdate / OnStateExit for full control.
    /// </summary>
    public class CameraActionStateBehaviour : StateMachineBehaviour
    {

        private sealed class OncePerStateProgressStrategy : ICameraProgressTriggerStrategy
        {
            public bool ShouldFire(in TriggerState state, int currentLoop, float currentNormalizedInLoop, float threshold)
            {
                if (state.FiredThisLoop) return false;
                return DidCrossThreshold(state.LastLoop, state.LastNormalizedInLoop, currentLoop, currentNormalizedInLoop, threshold);
            }

            public void UpdateStateAfterEvaluation(ref TriggerState state, int currentLoop, float currentNormalizedInLoop, bool fired)
            {
                if (fired)
                {
                    state.FiredThisLoop = true;
                }
                state.LastLoop = currentLoop;
                state.LastNormalizedInLoop = currentNormalizedInLoop;
                state.Initialized = true;
            }
        }

        private sealed class OncePerLoopProgressStrategy : ICameraProgressTriggerStrategy
        {
            public bool ShouldFire(in TriggerState state, int currentLoop, float currentNormalizedInLoop, float threshold)
            {
                bool alreadyFiredThisLoop = state.FiredThisLoop && currentLoop == state.LastLoop;
                if (alreadyFiredThisLoop) return false;
                return DidCrossThreshold(state.LastLoop, state.LastNormalizedInLoop, currentLoop, currentNormalizedInLoop, threshold);
            }

            public void UpdateStateAfterEvaluation(ref TriggerState state, int currentLoop, float currentNormalizedInLoop, bool fired)
            {
                if (currentLoop > state.LastLoop)
                {
                    state.FiredThisLoop = false;
                }

                if (fired)
                {
                    state.FiredThisLoop = true;
                }

                state.LastLoop = currentLoop;
                state.LastNormalizedInLoop = currentNormalizedInLoop;
                state.Initialized = true;
            }
        }

        private sealed class NoExitActionStrategy : ICameraExitActionStrategy
        {
            public void Execute(CameraActionBinding binding, string stopKey, string playKey, float durationOverride)
            {
            }
        }

        private sealed class StopExitActionStrategy : ICameraExitActionStrategy
        {
            public void Execute(CameraActionBinding binding, string stopKey, string playKey, float durationOverride)
            {
                if (!string.IsNullOrEmpty(stopKey))
                    binding.StopAction(stopKey);
            }
        }

        private sealed class PlayExitActionStrategy : ICameraExitActionStrategy
        {
            public void Execute(CameraActionBinding binding, string stopKey, string playKey, float durationOverride)
            {
                if (!string.IsNullOrEmpty(playKey))
                    binding.PlayAction(playKey, durationOverride);
            }
        }

        private static readonly ICameraProgressTriggerStrategy OncePerStateStrategy = new OncePerStateProgressStrategy();
        private static readonly ICameraProgressTriggerStrategy OncePerLoopStrategy = new OncePerLoopProgressStrategy();
        private static readonly ICameraExitActionStrategy ExitNoneStrategy = new NoExitActionStrategy();
        private static readonly ICameraExitActionStrategy ExitStopStrategy = new StopExitActionStrategy();
        private static readonly ICameraExitActionStrategy ExitPlayStrategy = new PlayExitActionStrategy();

        public enum ExitActionMode
        {
            None,
            StopActionKey,
            PlayActionKey
        }

        private readonly struct StateKey : System.IEquatable<StateKey>
        {
            public readonly int AnimatorId;
            public readonly int Layer;

            public StateKey(int animatorId, int layer)
            {
                AnimatorId = animatorId;
                Layer = layer;
            }

            public bool Equals(StateKey other)
            {
                return AnimatorId == other.AnimatorId && Layer == other.Layer;
            }

            public override bool Equals(object obj)
            {
                return obj is StateKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                // Deterministic 32-bit hash combine, stable across platforms.
                unchecked
                {
                    uint hash = (uint)AnimatorId;
                    hash ^= (uint)Layer + 0x9e3779b9u + (hash << 6) + (hash >> 2);
                    return (int)hash;
                }
            }
        }

        /// <summary>
        /// Per-Animator per-layer frame tracking state used by <see cref="ICameraProgressTriggerStrategy"/>.
        /// Public so external strategy implementations can read and mutate it through the interface contract.
        /// </summary>
        public struct TriggerState
        {
            public bool Initialized;
            public int LastLoop;
            public float LastNormalizedInLoop;
            public bool FiredThisLoop;
        }

        [Tooltip("Action key to play when entering this state. Leave empty to skip.")]
        [SerializeField] private string onEnterActionKey;

        [Tooltip("If false, OnEnter action will not fire while Animator is transitioning into this state.")]
        [SerializeField] private bool allowEnterTriggerInTransition = true;

        [Header("Exit Behavior")]
        [Tooltip("Defines what happens on OnStateExit.")]
        [SerializeField] private ExitActionMode onExitMode = ExitActionMode.StopActionKey;

        [Tooltip("Action key used when ExitActionMode is StopActionKey.")]
        [SerializeField] private string onExitActionKey;

        [Tooltip("Action key used when ExitActionMode is PlayActionKey.")]
        [SerializeField] private string onExitPlayActionKey;

        [Tooltip("Optional action key to play when normalizedTime crosses TriggerNormalizedTime while this state is running.")]
        [SerializeField] private string onProgressActionKey;

        [Tooltip("Normalized time threshold in [0,1]. Crossing this value during OnStateUpdate triggers OnProgressActionKey.")]
        [SerializeField, Range(0f, 1f)] private float triggerNormalizedTime = 0.5f;

        [Tooltip("If true, OnProgressActionKey can trigger once per loop. If false, it triggers only once for the whole state lifetime.")]
        [SerializeField] private bool triggerEveryLoop = true;

        [Tooltip("If false, progress-threshold triggers are suppressed while the Animator is in transition on this layer.")]
        [SerializeField] private bool allowProgressTriggerInTransition = true;

        [Tooltip("Duration override in seconds applied to both enter/exit actions. Non-positive = use entry default.")]
        [SerializeField] private float durationOverride = -1f;

        // Cache is keyed by the Animator reference so recycled GameObjects (object pools)
        // get a fresh lookup instead of reusing a stale binding from the previous owner.
        private Animator ownerAnimator;
        private CameraActionBinding cachedBinding;
        private readonly Dictionary<StateKey, TriggerState> triggerStates = new Dictionary<StateKey, TriggerState>(4);

        public override void OnStateEnter(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
        {
            StateKey key = BuildStateKey(animator, layerIndex);
            triggerStates[key] = BuildInitialTriggerState(stateInfo);

            if (string.IsNullOrEmpty(onEnterActionKey)) return;
            if (!allowEnterTriggerInTransition && animator.IsInTransition(layerIndex)) return;
            CameraActionBinding binding = GetOrResolveBinding(animator);
            binding?.PlayAction(onEnterActionKey, durationOverride);
        }

        public override void OnStateUpdate(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
        {
            if (string.IsNullOrEmpty(onProgressActionKey)) return;

            StateKey key = BuildStateKey(animator, layerIndex);
            TriggerState state;
            if (!triggerStates.TryGetValue(key, out state) || !state.Initialized)
            {
                state = BuildInitialTriggerState(stateInfo);
            }

            int currentLoop;
            float currentNormalizedInLoop;
            DecomposeNormalizedTime(stateInfo.normalizedTime, out currentLoop, out currentNormalizedInLoop);

            if (!allowProgressTriggerInTransition && animator.IsInTransition(layerIndex))
            {
                state.Initialized = true;
                state.LastLoop = currentLoop;
                state.LastNormalizedInLoop = currentNormalizedInLoop;
                triggerStates[key] = state;
                return;
            }

            ICameraProgressTriggerStrategy strategy = ResolveProgressStrategy();
            bool shouldFire = strategy.ShouldFire(state, currentLoop, currentNormalizedInLoop, triggerNormalizedTime);
            if (shouldFire)
            {
                CameraActionBinding binding = GetOrResolveBinding(animator);
                binding?.PlayAction(onProgressActionKey, durationOverride);
            }

            strategy.UpdateStateAfterEvaluation(ref state, currentLoop, currentNormalizedInLoop, shouldFire);
            triggerStates[key] = state;
        }

        public override void OnStateExit(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
        {
            triggerStates.Remove(BuildStateKey(animator, layerIndex));
            CameraActionBinding binding = GetOrResolveBinding(animator);
            if (binding == null) return;

            ResolveExitActionStrategy().Execute(binding, onExitActionKey, onExitPlayActionKey, durationOverride);
        }

        private CameraActionBinding GetOrResolveBinding(Animator animator)
        {
            if (cachedBinding != null && ownerAnimator == animator) return cachedBinding;
            ownerAnimator = animator;
            cachedBinding = animator.GetComponent<CameraActionBinding>();
            if (cachedBinding == null)
                cachedBinding = animator.GetComponentInParent<CameraActionBinding>();
            return cachedBinding;
        }

        private static StateKey BuildStateKey(Animator animator, int layerIndex)
        {
            return new StateKey(animator.GetInstanceID(), layerIndex);
        }

        private static TriggerState BuildInitialTriggerState(AnimatorStateInfo stateInfo)
        {
            int loop;
            float normalizedInLoop;
            DecomposeNormalizedTime(stateInfo.normalizedTime, out loop, out normalizedInLoop);
            return new TriggerState
            {
                Initialized = true,
                LastLoop = loop,
                LastNormalizedInLoop = normalizedInLoop,
                FiredThisLoop = false
            };
        }

        private static void DecomposeNormalizedTime(float normalizedTime, out int loop, out float normalizedInLoop)
        {
            float floored = Mathf.Floor(normalizedTime);
            loop = (int)floored;
            normalizedInLoop = Mathf.Clamp01(normalizedTime - floored);
        }

        private static bool DidCrossThreshold(int lastLoop, float lastNorm, int currentLoop, float currentNorm, float threshold)
        {
            if (currentLoop < lastLoop)
            {
                return false;
            }

            if (currentLoop == lastLoop)
            {
                return lastNorm < threshold && currentNorm >= threshold;
            }

            return lastNorm < threshold || currentNorm >= threshold;
        }

        /// <summary>
        /// Returns the strategy used to decide when the progress threshold trigger fires.
        /// Override in a subclass to inject a custom <see cref="ICameraProgressTriggerStrategy"/>.
        /// The default implementation selects between once-per-state and once-per-loop based on
        /// the serialized <c>triggerEveryLoop</c> field.
        /// </summary>
        protected virtual ICameraProgressTriggerStrategy ResolveProgressStrategy()
        {
            return triggerEveryLoop ? OncePerLoopStrategy : OncePerStateStrategy;
        }

        /// <summary>
        /// Returns the strategy that executes on <c>OnStateExit</c>.
        /// Override in a subclass to inject a custom <see cref="ICameraExitActionStrategy"/>.
        /// The default implementation dispatches based on the serialized <c>onExitMode</c> enum.
        /// </summary>
        protected virtual ICameraExitActionStrategy ResolveExitActionStrategy()
        {
            switch (onExitMode)
            {
                case ExitActionMode.StopActionKey: return ExitStopStrategy;
                case ExitActionMode.PlayActionKey: return ExitPlayStrategy;
                default: return ExitNoneStrategy;
            }
        }
    }
}
