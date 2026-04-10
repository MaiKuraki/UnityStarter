using CycloneGames.Logger;
using Unity.Cinemachine;
using UnityEngine;

namespace CycloneGames.GameplayFramework.Runtime
{
    public class CameraManager : Actor
    {
        private const string DEBUG_FLAG = "<color=cyan>[CameraManager]</color>";

        [SerializeField] protected float DefaultFOV = 60.0f;
        [SerializeField] private float defaultBlendDuration = 0.15f;
        [SerializeField] private CinemachineCamera bootstrapVirtualCamera;
        [SerializeField] private CinemachineBrain bootstrapBrain;

        public CinemachineCamera ActiveVirtualCamera { get; private set; }
        public float DefaultBlendDuration => defaultBlendDuration;
        public bool HasExplicitFovOverride => hasExplicitFovOverride;
        public bool CameraStateDirty => cameraStateDirty;
        public bool HasCurrentPose => hasCurrentPose;
        public CameraPose CurrentPose => currentPose;
        public bool HasPendingBlendDurationOverride => hasPendingBlendDurationOverride;
        public float PendingBlendDurationOverride => pendingBlendDurationOverride;
        public Transform PendingViewTargetTransform => PendingViewTargetTF;
        public Actor LastViewTarget => lastViewTarget;
        public CameraMode LastPrimaryMode => lastPrimaryMode;
        public CameraBlendState BlendState => blendState;
        public CinemachineBrain ActiveBrain => activeBrain;

        private PlayerController PCOwner;
        public PlayerController OwnerController => PCOwner;
        public bool IsInitialized { get; private set; }
        private float lockedFOV;
        public float GetLockedFOV() => lockedFOV;
        private bool hasExplicitFovOverride;
        private Transform PendingViewTargetTF;
        private Actor lastViewTarget;
        private CameraMode lastPrimaryMode;
        private CameraPose currentPose;
        private bool hasCurrentPose;
        private bool cameraStateDirty;
        private CameraBlendState blendState;
        private bool hasPendingBlendDurationOverride;
        private float pendingBlendDurationOverride;

        // Cached to avoid FindObjectsByType GC allocation on every initialization
        private static CinemachineCamera[] s_cameraBuffer;
        private static CinemachineBrain[] s_brainBuffer;

        // Brain reference kept so we can drive ManualUpdate and restore on destroy
        private CinemachineBrain activeBrain;
        private CinemachineBrain.UpdateMethods previousBrainUpdateMethod;
        private bool hasBrainUpdateMethodSnapshot;

        // Fixed-capacity array keeps registration allocation-free after construction.
        private const int MAX_POST_PROCESSORS = 16;
        private readonly ICameraPostProcessor[] postProcessors = new ICameraPostProcessor[MAX_POST_PROCESSORS];
        private int postProcessorCount;

        public virtual void SetActiveVirtualCamera(CinemachineCamera newActiveCamera)
        {
            ActiveVirtualCamera = newActiveCamera;
            if (ActiveVirtualCamera != null)
            {
                ActiveVirtualCamera.Follow = null;
                ActiveVirtualCamera.LookAt = null;
            }
        }

        /// <summary>
        /// Set the preferred CinemachineBrain for this manager.
        /// Useful when CameraManager is instantiated at runtime and cannot serialize scene references.
        /// </summary>
        public virtual void SetBootstrapBrain(CinemachineBrain brain, bool rebindImmediately = true)
        {
            bootstrapBrain = brain;
            if (!rebindImmediately) return;

            // If explicit brain is null, fall back to runtime discovery.
            BindBrain(bootstrapBrain != null ? bootstrapBrain : ResolveCinemachineBrain());
        }

        /// <summary>
        /// Backward-compatible typo alias kept for API discoverability.
        /// </summary>
        public virtual void SetBootStartpBrain(CinemachineBrain brain, bool rebindImmediately = true)
        {
            SetBootstrapBrain(brain, rebindImmediately);
        }

        /// <summary>
        /// Re-resolve a CinemachineBrain using current discovery rules and bind it immediately.
        /// Returns false when no brain could be resolved.
        /// </summary>
        public virtual bool TryResolveAndBindBrain()
        {
            CinemachineBrain resolved = ResolveCinemachineBrain();
            if (resolved == null) return false;

            BindBrain(resolved);
            return true;
        }

        public virtual void SetFOV(float NewFOV)
        {
            lockedFOV = NewFOV;
            hasExplicitFovOverride = true;
            if (ActiveVirtualCamera != null)
            {
                ActiveVirtualCamera.Lens.FieldOfView = lockedFOV;
            }
        }

        public virtual void ClearFOVOverride()
        {
            hasExplicitFovOverride = false;
            lockedFOV = DefaultFOV;
            NotifyCameraStateChanged();
        }

        /// <summary>
        /// Set the default FOV used when no explicit FOV override is active.
        /// Typically called by <see cref="CameraProfile.ApplyTo"/>.
        /// </summary>
        public virtual void SetDefaultFOV(float fov)
        {
            DefaultFOV = fov;
            if (!hasExplicitFovOverride)
            {
                lockedFOV = DefaultFOV;
                NotifyCameraStateChanged();
            }
        }

        /// <summary>
        /// Set the fallback blend duration used when the active CameraMode does not specify one.
        /// Typically called by <see cref="CameraProfile.ApplyTo"/>.
        /// </summary>
        public virtual void SetDefaultBlendDuration(float duration)
        {
            defaultBlendDuration = duration;
        }

        /// <summary>
        /// Sets a one-shot blend duration override that is consumed on the next camera state transition.
        /// </summary>
        public virtual void SetNextBlendDuration(float duration)
        {
            pendingBlendDurationOverride = Mathf.Max(0f, duration);
            hasPendingBlendDurationOverride = true;
        }

        public virtual void SetViewTarget(Transform NewTargetTF)
        {
            PendingViewTargetTF = NewTargetTF;
            NotifyCameraStateChanged();
        }

        public virtual void NotifyCameraStateChanged()
        {
            cameraStateDirty = true;
        }

        public virtual void InitializeFor(PlayerController PlayerController)
        {
            PCOwner = PlayerController;
            lockedFOV = DefaultFOV;
            hasExplicitFovOverride = false;

            BindBrain(ResolveCinemachineBrain());

            if (ActiveVirtualCamera == null)
            {
                if (bootstrapVirtualCamera != null)
                {
                    SetActiveVirtualCamera(bootstrapVirtualCamera);
                }
                else
                {
                    // Reuse static buffer to avoid repeated GC from FindObjectsByType.
                    s_cameraBuffer = FindObjectsByType<CinemachineCamera>(FindObjectsSortMode.None);
                    if (s_cameraBuffer != null && s_cameraBuffer.Length > 0)
                    {
                        SetActiveVirtualCamera(s_cameraBuffer[0]);
                    }
                }
            }

            var currentViewTarget = PlayerController != null ? PlayerController.GetViewTarget() : null;
            PendingViewTargetTF = currentViewTarget != null ? currentViewTarget.transform : PlayerController?.transform;
            NotifyCameraStateChanged();
            IsInitialized = true;
        }

        private void BindBrain(CinemachineBrain newBrain)
        {
            if (ReferenceEquals(activeBrain, newBrain))
            {
                if (activeBrain != null)
                {
                    activeBrain.UpdateMethod = CinemachineBrain.UpdateMethods.ManualUpdate;
                }
                return;
            }

            RestoreActiveBrainUpdateMethod();

            activeBrain = newBrain;
            if (activeBrain == null)
            {
                return;
            }

            // Take ownership of update timing so our pose write and Brain read
            // happen in a deterministic order within the same LateUpdate.
            previousBrainUpdateMethod = activeBrain.UpdateMethod;
            hasBrainUpdateMethodSnapshot = true;
            activeBrain.UpdateMethod = CinemachineBrain.UpdateMethods.ManualUpdate;
        }

        private void RestoreActiveBrainUpdateMethod()
        {
            if (activeBrain != null && hasBrainUpdateMethodSnapshot)
            {
                activeBrain.UpdateMethod = previousBrainUpdateMethod;
            }

            hasBrainUpdateMethodSnapshot = false;
        }

        private CinemachineBrain ResolveCinemachineBrain()
        {
            if (bootstrapBrain != null)
            {
                return bootstrapBrain;
            }

            if (ActiveVirtualCamera != null)
            {
                CinemachineBrain vcamBrain = ActiveVirtualCamera.GetComponentInParent<CinemachineBrain>();
                if (vcamBrain != null)
                {
                    return vcamBrain;
                }
            }

            s_brainBuffer = FindObjectsByType<CinemachineBrain>(FindObjectsSortMode.None);
            if (s_brainBuffer == null || s_brainBuffer.Length <= 0)
            {
                CLogger.LogWarning($"{DEBUG_FLAG} No CinemachineBrain found. Camera output will not be driven.");
                return null;
            }

            CinemachineBrain chosen = null;
            for (int i = 0; i < s_brainBuffer.Length; i++)
            {
                CinemachineBrain brain = s_brainBuffer[i];
                if (brain == null) continue;
                if (!brain.isActiveAndEnabled) continue;
                if (!brain.gameObject.activeInHierarchy) continue;

                chosen = brain;
                break;
            }

            if (chosen == null)
            {
                chosen = s_brainBuffer[0];
            }

            if (s_brainBuffer.Length > 1)
            {
                CLogger.LogWarning($"{DEBUG_FLAG} Multiple CinemachineBrain instances detected ({s_brainBuffer.Length}). " +
                                   $"Using '{chosen.name}'. Assign Bootstrap Brain for deterministic binding.");
            }

            return chosen;
        }

        public virtual void UpdateCamera(float deltaTime)
        {
            if (!IsInitialized) return;

            CameraPose desiredPose = EvaluateDesiredPose(deltaTime);
            CameraContext context = PCOwner != null ? PCOwner.GetCameraContext() : null;
            Actor currentViewTarget = context != null ? context.CurrentViewTarget : null;
            CameraMode primaryMode = context != null ? context.GetPrimaryCameraMode() : null;

            if (!hasCurrentPose)
            {
                ApplyCameraPose(desiredPose);
                lastViewTarget = currentViewTarget;
                lastPrimaryMode = primaryMode;
                cameraStateDirty = false;
                return;
            }

            if (cameraStateDirty || !ReferenceEquals(lastViewTarget, currentViewTarget) || !ReferenceEquals(lastPrimaryMode, primaryMode))
            {
                float blendDuration;
                if (hasPendingBlendDurationOverride)
                {
                    blendDuration = pendingBlendDurationOverride;
                    hasPendingBlendDurationOverride = false;
                }
                else
                {
                    blendDuration = primaryMode != null ? primaryMode.BlendDuration : defaultBlendDuration;
                }

                blendState.Start(currentPose, blendDuration);
                lastViewTarget = currentViewTarget;
                lastPrimaryMode = primaryMode;
                cameraStateDirty = false;
            }

            CameraPose outputPose = blendState.Evaluate(desiredPose, deltaTime);
            ApplyCameraPose(outputPose);
        }

        protected virtual CameraPose EvaluateDesiredPose(float deltaTime)
        {
            float fallbackFov = hasExplicitFovOverride ? lockedFOV : DefaultFOV;
            CameraContext context = PCOwner != null ? PCOwner.GetCameraContext() : null;

            CameraPose desiredPose;
            if (context != null && context.CurrentViewTarget != null)
            {
                context.CurrentViewTarget.CalcCamera(deltaTime, out desiredPose, fallbackFov);
            }
            else if (PendingViewTargetTF != null)
            {
                desiredPose = CameraPoseUtility.GetCameraPose(PendingViewTargetTF, fallbackFov);
            }
            else
            {
                desiredPose = new CameraPose(transform.position, transform.rotation, fallbackFov);
            }

            if (context != null)
            {
                CameraMode baseMode = context.BaseCameraMode;
                if (baseMode != null)
                {
                    baseMode.Tick(context, deltaTime);
                    desiredPose = baseMode.Evaluate(context, desiredPose, deltaTime);
                }

                int modeCount = context.CameraModeCount;
                for (int i = 0; i < modeCount; i++)
                {
                    CameraMode mode = context.GetCameraModeAt(i);
                    if (mode == null) continue;

                    mode.Tick(context, deltaTime);
                    desiredPose = mode.Evaluate(context, desiredPose, deltaTime);
                }
            }

            // Post-processors run after all CameraModes (e.g. collision avoidance, screen shake)
            for (int i = 0; i < postProcessorCount; i++)
            {
                ICameraPostProcessor proc = postProcessors[i];
                if (proc != null)
                    desiredPose = proc.Process(desiredPose, context, deltaTime);
            }

            if (hasExplicitFovOverride)
            {
                desiredPose.Fov = lockedFOV;
            }
            else
            {
                lockedFOV = desiredPose.Fov;
            }

            return desiredPose;
        }

        protected virtual void ApplyCameraPose(CameraPose pose)
        {
            currentPose = pose;
            hasCurrentPose = true;

            transform.SetPositionAndRotation(pose.Position, pose.Rotation);
            if (ActiveVirtualCamera != null)
            {
                ActiveVirtualCamera.transform.SetPositionAndRotation(pose.Position, pose.Rotation);
                ActiveVirtualCamera.Lens.FieldOfView = pose.Fov;
            }

            // Trigger Brain after we have written the final VCam pose, guaranteeing
            // the Brain always reads the pose we just set — no frame-behind artefacts.
            activeBrain?.ManualUpdate();
        }

        /// <summary>Add a post-processor to the evaluation chain. No-op if already registered.</summary>
        public void RegisterPostProcessor(ICameraPostProcessor processor)
        {
            if (processor == null) return;

            for (int i = 0; i < postProcessorCount; i++)
            {
                if (ReferenceEquals(postProcessors[i], processor))
                {
                    return;
                }
            }

            if (postProcessorCount >= MAX_POST_PROCESSORS)
            {
                CLogger.LogWarning($"{DEBUG_FLAG} Max post-processors reached ({MAX_POST_PROCESSORS}).");
                return;
            }

            postProcessors[postProcessorCount++] = processor;
        }

        /// <summary>Remove a previously registered post-processor.</summary>
        public void UnregisterPostProcessor(ICameraPostProcessor processor)
        {
            if (processor == null) return;

            for (int i = 0; i < postProcessorCount; i++)
            {
                if (!ReferenceEquals(postProcessors[i], processor)) continue;

                int moveCount = postProcessorCount - i - 1;
                if (moveCount > 0)
                {
                    System.Array.Copy(postProcessors, i + 1, postProcessors, i, moveCount);
                }

                postProcessorCount--;
                postProcessors[postProcessorCount] = null;
                return;
            }
        }

        protected override void LateUpdate()
        {
            base.LateUpdate();
            UpdateCamera(Time.deltaTime);
        }

        protected override void Awake()
        {
            base.Awake();
            CLogger.LogInfo($"{DEBUG_FLAG} CameraManager requires a CinemachineBrain on the active Camera.");
        }

        protected override void OnDestroy()
        {
            // Restore Brain to autonomous update so it keeps working after this
            // CameraManager is destroyed (e.g. during scene reload).
            RestoreActiveBrainUpdateMethod();
            activeBrain = null;
            for (int i = 0; i < postProcessorCount; i++)
            {
                postProcessors[i] = null;
            }
            postProcessorCount = 0;
            lastViewTarget = null;
            lastPrimaryMode = null;
            PendingViewTargetTF = null;
            hasCurrentPose = false;
            hasExplicitFovOverride = false;
            hasPendingBlendDurationOverride = false;
            pendingBlendDurationOverride = 0f;
            base.OnDestroy();
        }
    }
}