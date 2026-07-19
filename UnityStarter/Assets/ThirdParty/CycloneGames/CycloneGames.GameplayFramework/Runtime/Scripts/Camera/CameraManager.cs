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
        private bool isUpdatingCamera;

        // Brain ownership is arbitrated by the explicit World scope.
        private CinemachineBrain activeBrain;
        private int activeBrainOwnershipId;
        private CinemachineCamera targetSnapshotCamera;
        private Transform previousFollowTarget;
        private Transform previousLookAtTarget;

        // Fixed-capacity array keeps registration allocation-free after construction.
        private const int MAX_POST_PROCESSORS = 16;
        private readonly ICameraPostProcessor[] postProcessors = new ICameraPostProcessor[MAX_POST_PROCESSORS];
        private int postProcessorCount;

        protected override void Awake()
        {
            base.Awake();
            EnsureActorTickConfiguration();
        }

        public virtual void SetActiveVirtualCamera(CinemachineCamera newActiveCamera)
        {
            RestoreVirtualCameraTargets();
            ActiveVirtualCamera = newActiveCamera;
            if (ActiveVirtualCamera != null)
            {
                targetSnapshotCamera = ActiveVirtualCamera;
                previousFollowTarget = ActiveVirtualCamera.Follow;
                previousLookAtTarget = ActiveVirtualCamera.LookAt;
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
        /// Re-resolve a CinemachineBrain using current discovery rules and bind it immediately.
        /// Returns false when no brain could be resolved.
        /// </summary>
        public virtual bool TryResolveAndBindBrain()
        {
            CinemachineBrain resolved = ResolveCinemachineBrain();
            if (resolved == null) return false;

            return BindBrain(resolved);
        }

        public virtual void SetFOV(float NewFOV)
        {
            if (float.IsNaN(NewFOV) || float.IsInfinity(NewFOV) || NewFOV <= 0f || NewFOV >= 180f)
            {
                throw new System.ArgumentOutOfRangeException(nameof(NewFOV));
            }

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
            if (float.IsNaN(fov) || float.IsInfinity(fov) || fov <= 0f || fov >= 180f)
            {
                throw new System.ArgumentOutOfRangeException(nameof(fov));
            }

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
            if (float.IsNaN(duration) || float.IsInfinity(duration) || duration < 0f)
            {
                throw new System.ArgumentOutOfRangeException(nameof(duration));
            }

            defaultBlendDuration = duration;
        }

        /// <summary>
        /// Sets a one-shot blend duration override that is consumed on the next camera state transition.
        /// </summary>
        public virtual void SetNextBlendDuration(float duration)
        {
            if (float.IsNaN(duration) || float.IsInfinity(duration) || duration < 0f)
            {
                throw new System.ArgumentOutOfRangeException(nameof(duration));
            }

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
            if (PlayerController == null)
            {
                throw new System.ArgumentNullException(nameof(PlayerController));
            }

            if (!PlayerController.IsLocalController)
            {
                throw new System.InvalidOperationException("CameraManager requires a local PlayerController.");
            }

            if (!ReferenceEquals(PlayerController.World, World))
            {
                throw new System.InvalidOperationException("CameraManager and PlayerController must belong to the same World.");
            }

            if (IsInitialized)
            {
                throw new System.InvalidOperationException("CameraManager is already initialized.");
            }

            PCOwner = PlayerController;
            lockedFOV = DefaultFOV;
            hasExplicitFovOverride = false;

            if (ActiveVirtualCamera == null)
            {
                if (bootstrapVirtualCamera != null)
                {
                    SetActiveVirtualCamera(bootstrapVirtualCamera);
                }
                else
                {
                    // Discovery is a one-time cold-path fallback. Production composition should
                    // assign explicit scene references when multiple cameras are present.
                    CinemachineCamera[] cameras = FindObjectsByType<CinemachineCamera>(FindObjectsSortMode.None);
                    if (cameras != null && cameras.Length > 0)
                    {
                        SetActiveVirtualCamera(cameras[0]);
                    }
                }
            }

            // Resolve the Brain after the virtual camera has been selected so a configured
            // camera hierarchy is preferred over global scene discovery.
            BindBrain(ResolveCinemachineBrain());

            var currentViewTarget = PlayerController != null ? PlayerController.GetViewTarget() : null;
            PendingViewTargetTF = currentViewTarget != null ? currentViewTarget.transform : PlayerController?.transform;
            NotifyCameraStateChanged();
            IsInitialized = true;
            EnsureActorTickConfiguration();
            SetActorTickEnabled(true);
        }

        private bool BindBrain(CinemachineBrain newBrain)
        {
            if (ReferenceEquals(activeBrain, newBrain))
            {
                return activeBrain != null;
            }

            if (newBrain == null || World == null)
            {
                return false;
            }

            if (!World.TryAcquireCameraBrain(this, newBrain, out int newOwnershipId, out string error))
            {
                CLogger.LogError($"{DEBUG_FLAG} {error}");
                return false;
            }

            int previousOwnershipId = activeBrainOwnershipId;
            activeBrain = newBrain;
            activeBrainOwnershipId = newOwnershipId;
            if (previousOwnershipId != 0)
            {
                World.ReleaseCameraBrain(this, previousOwnershipId);
            }

            return true;
        }

        private void ReleaseActiveBrain()
        {
            World?.ReleaseCameraBrain(this, activeBrainOwnershipId);
            activeBrainOwnershipId = 0;
            activeBrain = null;
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

            CinemachineBrain[] brains = FindObjectsByType<CinemachineBrain>(FindObjectsSortMode.None);
            if (brains == null || brains.Length <= 0)
            {
                CLogger.LogWarning($"{DEBUG_FLAG} No CinemachineBrain found. Camera output will not be driven.");
                return null;
            }

            CinemachineBrain chosen = null;
            for (int i = 0; i < brains.Length; i++)
            {
                CinemachineBrain brain = brains[i];
                if (brain == null) continue;
                if (!brain.isActiveAndEnabled) continue;
                if (!brain.gameObject.activeInHierarchy) continue;

                chosen = brain;
                break;
            }

            if (chosen == null)
            {
                chosen = brains[0];
            }

            if (brains.Length > 1)
            {
                CLogger.LogWarning($"{DEBUG_FLAG} Multiple CinemachineBrain instances detected ({brains.Length}). " +
                                   $"Using '{chosen.name}'. Assign Bootstrap Brain for deterministic binding.");
            }

            return chosen;
        }

        public virtual void UpdateCamera(float deltaTime)
        {
            if (!IsInitialized || isUpdatingCamera) return;

            isUpdatingCamera = true;
            try
            {
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
            finally
            {
                isUpdatingCamera = false;
            }
        }

        protected virtual CameraPose EvaluateDesiredPose(float deltaTime)
        {
            float fallbackFov = hasExplicitFovOverride ? lockedFOV : DefaultFOV;
            CameraContext context = PCOwner != null ? PCOwner.GetCameraContext() : null;
            bool ownsEvaluationScope = context != null && context.TryBeginEvaluation();

            try
            {
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

                if (context != null && ownsEvaluationScope)
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
            finally
            {
                if (ownsEvaluationScope)
                {
                    context.EndEvaluation();
                }
            }
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

            // Trigger Brain after writing the final virtual-camera pose so the Brain reads the
            // current frame rather than a frame-behind value.
            if (activeBrain != null)
            {
                activeBrain.ManualUpdate();
            }
            else if (!ReferenceEquals(activeBrain, null))
            {
                ReleaseActiveBrain();
            }
        }

        /// <summary>Add a post-processor to the evaluation chain. No-op if already registered.</summary>
        public void RegisterPostProcessor(ICameraPostProcessor processor)
        {
            if (processor == null || isUpdatingCamera) return;

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
            if (processor == null || isUpdatingCamera) return;

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

        protected override void Tick(float deltaSeconds)
        {
            UpdateCamera(deltaSeconds);
        }

        protected override void OnWorldUnbound(EndPlayReason reason)
        {
            ReleaseActiveBrain();
            RestoreVirtualCameraTargets();
            ResetRuntimeState();
            base.OnWorldUnbound(reason);
        }

        protected override void OnDestroy()
        {
            ReleaseActiveBrain();
            RestoreVirtualCameraTargets();
            ResetRuntimeState();
            base.OnDestroy();
        }

        private void ResetRuntimeState()
        {
            SetActorTickEnabled(false);
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
            PCOwner = null;
            ActiveVirtualCamera = null;
            IsInitialized = false;
            lockedFOV = DefaultFOV;
            blendState = default;
            isUpdatingCamera = false;
        }

        private void EnsureActorTickConfiguration()
        {
            if (TickPhase != ActorTickPhase.LateUpdate || IsTickEnabledAtStart)
            {
                ConfigureActorTick(ActorTickPhase.LateUpdate, startWithTickEnabled: false);
            }
        }

        private void RestoreVirtualCameraTargets()
        {
            if (targetSnapshotCamera != null)
            {
                targetSnapshotCamera.Follow = previousFollowTarget;
                targetSnapshotCamera.LookAt = previousLookAtTarget;
            }

            targetSnapshotCamera = null;
            previousFollowTarget = null;
            previousLookAtTarget = null;
        }
    }
}
