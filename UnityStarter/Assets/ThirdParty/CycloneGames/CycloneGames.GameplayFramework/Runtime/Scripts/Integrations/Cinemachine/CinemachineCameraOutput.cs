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
        [SerializeField] private bool allowSceneDiscovery = true;

        private CinemachineCamera activeVirtualCamera;
        private CinemachineBrain activeBrain;
        private CinemachineBrain.UpdateMethods previousUpdateMethod;
        private Transform previousFollowTarget;
        private Transform previousLookAtTarget;

        public CinemachineCamera ActiveVirtualCamera => activeVirtualCamera;
        public CinemachineBrain ActiveBrain => activeBrain;
        public override UnityEngine.Object OutputObject =>
            activeVirtualCamera != null ? activeVirtualCamera : bootstrapVirtualCamera;

        public void SetVirtualCamera(CinemachineCamera virtualCamera)
        {
            ThrowIfActive();
            bootstrapVirtualCamera = virtualCamera;
        }

        public void SetBrain(CinemachineBrain brain)
        {
            ThrowIfActive();
            bootstrapBrain = brain;
        }

        protected override bool OnTryPrepare(out UnityEngine.Object ownershipResource, out string error)
        {
            activeVirtualCamera = ResolveVirtualCamera(out error);
            if (activeVirtualCamera == null)
            {
                ownershipResource = null;
                return false;
            }

            activeBrain = ResolveBrain(activeVirtualCamera, out error);
            if (activeBrain == null)
            {
                activeVirtualCamera = null;
                ownershipResource = null;
                return false;
            }

            ownershipResource = activeBrain;
            error = null;
            return true;
        }

        protected override bool OnActivate(CameraManager newOwner, out string error)
        {
            if (activeVirtualCamera == null || activeBrain == null)
            {
                error = "Cinemachine output resources are no longer available.";
                return false;
            }

            previousUpdateMethod = activeBrain.UpdateMethod;
            previousFollowTarget = activeVirtualCamera.Follow;
            previousLookAtTarget = activeVirtualCamera.LookAt;
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
            if (activeVirtualCamera != null)
            {
                activeVirtualCamera.Follow = previousFollowTarget;
                activeVirtualCamera.LookAt = previousLookAtTarget;
            }

            if (activeBrain != null)
            {
                activeBrain.UpdateMethod = previousUpdateMethod;
            }

            previousFollowTarget = null;
            previousLookAtTarget = null;
            activeVirtualCamera = null;
            activeBrain = null;
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
            if (cameras.Length != 1)
            {
                error = cameras.Length == 0
                    ? "No CinemachineCamera was found."
                    : "Multiple CinemachineCamera components were found; assign one explicitly.";
                return null;
            }

            error = null;
            return cameras[0];
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
            if (brains.Length == 1)
            {
                error = null;
                return brains[0];
            }

            if (brains.Length == 0)
            {
                error = "No CinemachineBrain was found.";
                return null;
            }

            CinemachineBrain activeCandidate = null;
            for (int i = 0; i < brains.Length; i++)
            {
                CinemachineBrain candidate = brains[i];
                if (candidate == null || !candidate.isActiveAndEnabled || !candidate.gameObject.activeInHierarchy)
                {
                    continue;
                }

                if (activeCandidate != null)
                {
                    error = "Multiple active CinemachineBrain components were found; assign one explicitly.";
                    return null;
                }

                activeCandidate = candidate;
            }

            if (activeCandidate == null)
            {
                error = "Multiple CinemachineBrain components were found; assign one explicitly.";
                return null;
            }

            error = null;
            return activeCandidate;
        }

        private void ThrowIfActive()
        {
            if (IsActive)
            {
                throw new InvalidOperationException(
                    "Cinemachine output references cannot change while the output is active.");
            }
        }
    }
}
