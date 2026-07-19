using UnityEngine;

namespace CycloneGames.GameplayFramework.Runtime
{
    /// <summary>
    /// StateMachineBehaviour that triggers camera action presets when entering or exiting an Animator state.
    ///
    /// Usage:
    ///   1. Select an Animator state in the Animator window.
    ///   2. Click the '+' button under "Add Behaviour" and choose CameraActionStateBehaviour.
    ///   3. Fill in OnEnterActionKey (play on enter) and/or OnExitActionKey (stop on exit).
    ///
    /// The binding is resolved automatically from the Animator's GameObject or its parents.
    /// Progress state is tracked for a fixed number of concurrent Animator/layer pairs. When that
    /// capacity is exhausted, enter and exit actions continue while additional progress triggers
    /// are skipped until a tracked state exits.
    /// </summary>
    public class CameraActionStateBehaviour : StateMachineBehaviour
    {
        private const int MaxTrackedStateCount = 8;

        public enum ExitActionMode
        {
            None,
            StopActionKey,
            PlayActionKey
        }

        private struct TriggerState
        {
            public int LastLoop;
            public float LastNormalizedInLoop;
            public bool FiredThisLoop;
        }

        private struct TrackedState
        {
            public Animator Animator;
            public int LayerIndex;
            public TriggerState Progress;
            public bool IsOccupied;
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
        private readonly TrackedState[] trackedStates = new TrackedState[MaxTrackedStateCount];
        private bool capacityWarningLogged;

        public override void OnStateEnter(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
        {
            if (!string.IsNullOrEmpty(onProgressActionKey))
            {
                TryStartTracking(animator, layerIndex, stateInfo, out _);
            }

            if (string.IsNullOrEmpty(onEnterActionKey)) return;
            if (!allowEnterTriggerInTransition && animator.IsInTransition(layerIndex)) return;
            CameraActionBinding binding = GetOrResolveBinding(animator);
            binding?.PlayAction(onEnterActionKey, durationOverride);
        }

        public override void OnStateUpdate(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
        {
            if (string.IsNullOrEmpty(onProgressActionKey)) return;

            int trackedIndex = FindTrackedState(animator, layerIndex);
            if (trackedIndex < 0 && !TryStartTracking(animator, layerIndex, stateInfo, out trackedIndex))
            {
                return;
            }

            TriggerState state = trackedStates[trackedIndex].Progress;
            int currentLoop;
            float currentNormalizedInLoop;
            DecomposeNormalizedTime(stateInfo.normalizedTime, out currentLoop, out currentNormalizedInLoop);

            if (!allowProgressTriggerInTransition && animator.IsInTransition(layerIndex))
            {
                state.LastLoop = currentLoop;
                state.LastNormalizedInLoop = currentNormalizedInLoop;
                trackedStates[trackedIndex].Progress = state;
                return;
            }

            bool shouldFire = ShouldFireProgress(state, currentLoop, currentNormalizedInLoop);
            if (shouldFire)
            {
                CameraActionBinding binding = GetOrResolveBinding(animator);
                binding?.PlayAction(onProgressActionKey, durationOverride);
            }

            UpdateProgressState(ref state, currentLoop, currentNormalizedInLoop, shouldFire);
            trackedStates[trackedIndex].Progress = state;
        }

        public override void OnStateExit(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
        {
            ReleaseTrackedState(animator, layerIndex);
            ExecuteExitAction(animator);
            ClearBindingCacheIfUnused(animator);
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

        private bool TryStartTracking(Animator animator, int layerIndex, AnimatorStateInfo stateInfo, out int trackedIndex)
        {
            trackedIndex = FindTrackedState(animator, layerIndex);
            if (trackedIndex < 0)
            {
                trackedIndex = FindAvailableTrackedState();
            }

            if (trackedIndex < 0)
            {
                ReportTrackingCapacityReached();
                return false;
            }

            trackedStates[trackedIndex] = new TrackedState
            {
                Animator = animator,
                LayerIndex = layerIndex,
                Progress = BuildInitialTriggerState(stateInfo),
                IsOccupied = true
            };
            return true;
        }

        private int FindTrackedState(Animator animator, int layerIndex)
        {
            for (int i = 0; i < trackedStates.Length; i++)
            {
                TrackedState trackedState = trackedStates[i];
                if (trackedState.IsOccupied && trackedState.Animator == animator && trackedState.LayerIndex == layerIndex)
                {
                    return i;
                }
            }

            return -1;
        }

        private int FindAvailableTrackedState()
        {
            for (int i = 0; i < trackedStates.Length; i++)
            {
                if (!trackedStates[i].IsOccupied)
                {
                    return i;
                }

                if (trackedStates[i].Animator == null)
                {
                    trackedStates[i] = default;
                    return i;
                }
            }

            return -1;
        }

        private void ReleaseTrackedState(Animator animator, int layerIndex)
        {
            int trackedIndex = FindTrackedState(animator, layerIndex);
            if (trackedIndex < 0) return;

            trackedStates[trackedIndex] = default;
            capacityWarningLogged = false;
        }

        private bool HasTrackedState(Animator animator)
        {
            for (int i = 0; i < trackedStates.Length; i++)
            {
                if (trackedStates[i].IsOccupied && trackedStates[i].Animator == animator)
                {
                    return true;
                }
            }

            return false;
        }

        private void ReportTrackingCapacityReached()
        {
            if (capacityWarningLogged) return;

            capacityWarningLogged = true;
            Debug.LogWarning(
                "CameraActionStateBehaviour reached its fixed capacity of 8 concurrent Animator/layer pairs. " +
                "Enter and exit actions continue, but progress triggers for additional pairs are skipped until a slot is released.",
                this);
        }

        private void ClearBindingCacheIfUnused(Animator animator)
        {
            if (ownerAnimator != animator || HasTrackedState(animator)) return;

            ownerAnimator = null;
            cachedBinding = null;
        }

        private static TriggerState BuildInitialTriggerState(AnimatorStateInfo stateInfo)
        {
            int loop;
            float normalizedInLoop;
            DecomposeNormalizedTime(stateInfo.normalizedTime, out loop, out normalizedInLoop);
            return new TriggerState
            {
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

        private bool ShouldFireProgress(TriggerState state, int currentLoop, float currentNormalizedInLoop)
        {
            if (triggerEveryLoop)
            {
                bool alreadyFiredThisLoop = state.FiredThisLoop && currentLoop == state.LastLoop;
                if (alreadyFiredThisLoop) return false;
            }
            else if (state.FiredThisLoop)
            {
                return false;
            }

            return DidCrossThreshold(
                state.LastLoop,
                state.LastNormalizedInLoop,
                currentLoop,
                currentNormalizedInLoop,
                triggerNormalizedTime);
        }

        private void UpdateProgressState(
            ref TriggerState state,
            int currentLoop,
            float currentNormalizedInLoop,
            bool fired)
        {
            if (triggerEveryLoop && currentLoop > state.LastLoop)
            {
                state.FiredThisLoop = false;
            }

            if (fired)
            {
                state.FiredThisLoop = true;
            }

            state.LastLoop = currentLoop;
            state.LastNormalizedInLoop = currentNormalizedInLoop;
        }

        private void ExecuteExitAction(Animator animator)
        {
            if (onExitMode == ExitActionMode.None) return;

            CameraActionBinding binding = GetOrResolveBinding(animator);
            if (binding == null) return;

            switch (onExitMode)
            {
                case ExitActionMode.StopActionKey:
                    if (!string.IsNullOrEmpty(onExitActionKey))
                    {
                        binding.StopAction(onExitActionKey);
                    }
                    break;

                case ExitActionMode.PlayActionKey:
                    if (!string.IsNullOrEmpty(onExitPlayActionKey))
                    {
                        binding.PlayAction(onExitPlayActionKey, durationOverride);
                    }
                    break;
            }
        }
    }
}
