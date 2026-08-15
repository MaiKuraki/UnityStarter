using System;
using Unity.Cinemachine;
using UnityEngine;

namespace CycloneGames.GameplayFramework.Runtime.Integrations.Cinemachine
{
    /// <summary>Applies CameraManager output to a CinemachineCamera and its CinemachineBrain.</summary>
    [DisallowMultipleComponent]
    public sealed class CinemachineCameraOutput : CameraOutputBehaviour
    {
        [SerializeField] private CinemachineCamera bootstrapVirtualCamera;
        [SerializeField] private CinemachineBrain bootstrapBrain;
        [SerializeField] private bool allowSceneDiscovery;

        private CinemachineCamera activeVirtualCamera;
        private CinemachineBrain activeBrain;
        private CinemachineBrain.UpdateMethods previousUpdateMethod;
        private Transform previousFollowTarget;
        private Transform previousLookAtTarget;
        private bool stateCaptured;
        private bool followRestorePending;
        private bool lookAtRestorePending;
        private bool brainUpdateRestorePending;

        public CinemachineCamera ActiveVirtualCamera
        {
            get
            {
                AssertOutputOwnerThread();
                return activeVirtualCamera;
            }
        }

        public CinemachineBrain ActiveBrain
        {
            get
            {
                AssertOutputOwnerThread();
                return activeBrain;
            }
        }

        protected override UnityEngine.Object OnGetOutputObject()
        {
            return activeVirtualCamera != null ? activeVirtualCamera : bootstrapVirtualCamera;
        }

        public void SetVirtualCamera(CinemachineCamera virtualCamera)
        {
            ThrowIfLifecycleBound();
            bootstrapVirtualCamera = virtualCamera;
        }

        public void SetBrain(CinemachineBrain brain)
        {
            ThrowIfLifecycleBound();
            bootstrapBrain = brain;
        }

        protected override bool OnTryGetResourceSet(
            out CameraOutputResourceSet resources,
            out string error)
        {
            CinemachineCamera resolvedVirtualCamera = ResolveVirtualCamera(out error);
            if (resolvedVirtualCamera == null)
            {
                resources = default;
                return false;
            }

            CinemachineBrain resolvedBrain = ResolveBrain(resolvedVirtualCamera, out error);
            if (resolvedBrain == null)
            {
                resources = default;
                return false;
            }

            resources = new CameraOutputResourceSet(resolvedBrain, resolvedVirtualCamera);
            error = null;
            return true;
        }

        protected override bool OnActivate(
            CameraManager newOwner,
            in CameraOutputResourceSet resources,
            out string error)
        {
            if (resources.Count != 2 ||
                !(resources.GetResource(0) is CinemachineBrain leasedBrain) ||
                !(resources.GetResource(1) is CinemachineCamera leasedVirtualCamera))
            {
                error = "CinemachineCameraOutput requires one leased Brain and one leased Cinemachine Camera.";
                return false;
            }

            activeBrain = leasedBrain;
            activeVirtualCamera = leasedVirtualCamera;
            previousUpdateMethod = activeBrain.UpdateMethod;
            previousFollowTarget = activeVirtualCamera.Follow;
            previousLookAtTarget = activeVirtualCamera.LookAt;
            stateCaptured = true;
            followRestorePending = true;
            lookAtRestorePending = true;
            brainUpdateRestorePending = true;
            activeBrain.UpdateMethod = CinemachineBrain.UpdateMethods.ManualUpdate;
            activeVirtualCamera.Follow = null;
            activeVirtualCamera.LookAt = null;
            error = null;
            return true;
        }

        protected override void OnApplyPose(in CameraPose pose)
        {
            if (activeVirtualCamera == null || activeBrain == null)
            {
                throw new InvalidOperationException("The active Cinemachine output was destroyed.");
            }

            activeVirtualCamera.transform.SetPositionAndRotation(pose.Position, pose.Rotation);
            activeVirtualCamera.Lens.FieldOfView = pose.Fov;
            activeBrain.ManualUpdate();
        }

        protected override void OnDeactivate()
        {
            Exception cleanupFailure = null;
            if (stateCaptured && followRestorePending)
            {
                try
                {
                    if (activeVirtualCamera != null)
                    {
                        activeVirtualCamera.Follow = previousFollowTarget;
                    }
                    followRestorePending = false;
                }
                catch (Exception exception)
                {
                    CaptureCleanupFailure(ref cleanupFailure, exception);
                }
            }

            if (stateCaptured && lookAtRestorePending)
            {
                try
                {
                    if (activeVirtualCamera != null)
                    {
                        activeVirtualCamera.LookAt = previousLookAtTarget;
                    }
                    lookAtRestorePending = false;
                }
                catch (Exception exception)
                {
                    CaptureCleanupFailure(ref cleanupFailure, exception);
                }
            }

            if (stateCaptured && brainUpdateRestorePending)
            {
                try
                {
                    if (activeBrain != null)
                    {
                        activeBrain.UpdateMethod = previousUpdateMethod;
                    }
                    brainUpdateRestorePending = false;
                }
                catch (Exception exception)
                {
                    CaptureCleanupFailure(ref cleanupFailure, exception);
                }
            }

            if (cleanupFailure != null)
            {
                throw cleanupFailure;
            }

            stateCaptured = false;
            previousFollowTarget = null;
            previousLookAtTarget = null;
            activeVirtualCamera = null;
            activeBrain = null;
        }

        private static void CaptureCleanupFailure(
            ref Exception current,
            Exception candidate)
        {
            if (current == null ||
                (!(current is OutOfMemoryException) && candidate is OutOfMemoryException))
            {
                current = candidate;
            }
        }

        private CinemachineCamera ResolveVirtualCamera(out string error)
        {
            CinemachineCamera resolved = bootstrapVirtualCamera;
            if (resolved == null)
            {
                resolved = GetComponent<CinemachineCamera>();
            }

            if (resolved == null)
            {
                resolved = GetComponentInChildren<CinemachineCamera>(includeInactive: true);
            }

            if (resolved != null)
            {
                error = null;
                return resolved;
            }

            if (!allowSceneDiscovery)
            {
                error = "CinemachineCamera is not assigned and scene discovery is disabled.";
                return null;
            }

            CinemachineCamera[] cameras = FindObjectsByType<CinemachineCamera>(FindObjectsSortMode.None);
            int sceneCameraCount = 0;
            for (int i = 0; i < cameras.Length; i++)
            {
                CinemachineCamera candidate = cameras[i];
                if (candidate == null || candidate.gameObject.scene != gameObject.scene)
                {
                    continue;
                }

                resolved = candidate;
                sceneCameraCount++;
            }

            if (sceneCameraCount != 1)
            {
                error = sceneCameraCount == 0
                    ? "No CinemachineCamera was found in the output Scene."
                    : "Multiple CinemachineCamera components were found in the output Scene; assign one explicitly.";
                return null;
            }

            error = null;
            return resolved;
        }

        private CinemachineBrain ResolveBrain(CinemachineCamera virtualCamera, out string error)
        {
            CinemachineBrain resolved = bootstrapBrain;
            if (resolved == null)
            {
                resolved = virtualCamera.GetComponentInParent<CinemachineBrain>();
            }

            if (resolved != null)
            {
                error = null;
                return resolved;
            }

            if (!allowSceneDiscovery)
            {
                error = "CinemachineBrain is not assigned and scene discovery is disabled.";
                return null;
            }

            CinemachineBrain[] brains = FindObjectsByType<CinemachineBrain>(FindObjectsSortMode.None);
            int sceneBrainCount = 0;
            CinemachineBrain onlySceneBrain = null;
            for (int i = 0; i < brains.Length; i++)
            {
                CinemachineBrain candidate = brains[i];
                if (candidate == null || candidate.gameObject.scene != gameObject.scene)
                {
                    continue;
                }

                onlySceneBrain = candidate;
                sceneBrainCount++;
            }

            if (sceneBrainCount != 1)
            {
                error = sceneBrainCount == 0
                    ? "No CinemachineBrain was found in the output Scene."
                    : "Multiple CinemachineBrain components were found in the output Scene; assign one explicitly.";
                return null;
            }

            error = null;
            return onlySceneBrain;
        }

    }
}
