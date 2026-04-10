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

        public virtual void SetActiveVirtualCamera(CinemachineCamera newActiveCamera)
        {
            ActiveVirtualCamera = newActiveCamera;
            if (ActiveVirtualCamera != null)
            {
                ActiveVirtualCamera.Follow = null;
                ActiveVirtualCamera.LookAt = null;
            }
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

            if (ActiveVirtualCamera == null)
            {
                var mainCam = Camera.main;
                if (mainCam != null)
                {
                    var brain = mainCam.GetComponent<CinemachineBrain>();
                    if (brain != null)
                    {
                        // Reuse static buffer to avoid GC from FindObjectsByType
                        s_cameraBuffer = FindObjectsByType<CinemachineCamera>(FindObjectsSortMode.None);
                        if (s_cameraBuffer != null && s_cameraBuffer.Length > 0)
                        {
                            SetActiveVirtualCamera(s_cameraBuffer[0]);
                        }
                    }
                }
            }

            var currentViewTarget = PlayerController != null ? PlayerController.GetViewTarget() : null;
            PendingViewTargetTF = currentViewTarget != null ? currentViewTarget.transform : PlayerController?.transform;
            NotifyCameraStateChanged();
            IsInitialized = true;
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
                ActiveVirtualCamera.Follow = null;
                ActiveVirtualCamera.LookAt = null;
                ActiveVirtualCamera.transform.SetPositionAndRotation(pose.Position, pose.Rotation);
                ActiveVirtualCamera.Lens.FieldOfView = pose.Fov;
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