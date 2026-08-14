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
        private bool stateCaptured;

        public CinemachineCamera ActiveVirtualCamera => activeVirtualCamera;
        public CinemachineBrain ActiveBrain => activeBrain;
        public override UnityEngine.Object OutputObject =>
            activeVirtualCamera != null ? activeVirtualCamera : bootstrapVirtualCamera;

        public void SetVirtualCamera(CinemachineCamera virtualCamera)
        {
            ThrowIfPreparedOrActive();
            bootstrapVirtualCamera = virtualCamera;
        }

        public void SetBrain(CinemachineBrain brain)
        {
            ThrowIfPreparedOrActive();
            bootstrapBrain = brain;
        }

        protected override bool OnTryPrepare(out string error)
        {
            activeVirtualCamera = ResolveVirtualCamera(out error);
            if (activeVirtualCamera == null)
            {
                return false;
            }

            activeBrain = ResolveBrain(activeVirtualCamera, out error);
            if (activeBrain == null)
            {
                activeVirtualCamera = null;
                return false;
            }

            if (!TryAddPreparedResource(activeBrain, out error))
            {
                return false;
            }

            return TryAddPreparedResource(activeVirtualCamera, out error);
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
            stateCaptured = true;
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
            if (stateCaptured && activeVirtualCamera != null)
            {
                activeVirtualCamera.Follow = previousFollowTarget;
                activeVirtualCamera.LookAt = previousLookAtTarget;
            }

            if (stateCaptured && activeBrain != null)
            {
                activeBrain.UpdateMethod = previousUpdateMethod;
            }
        }

        protected override void OnReleasePreparedResources()
        {
            stateCaptured = false;
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

            if (sceneBrainCount == 1)
            {
                error = null;
                return onlySceneBrain;
            }

            if (sceneBrainCount == 0)
            {
                error = "No CinemachineBrain was found in the output Scene.";
                return null;
            }

            CinemachineBrain activeCandidate = null;
            for (int i = 0; i < brains.Length; i++)
            {
                CinemachineBrain candidate = brains[i];
                if (candidate == null ||
                    candidate.gameObject.scene != gameObject.scene ||
                    !candidate.isActiveAndEnabled ||
                    !candidate.gameObject.activeInHierarchy)
                {
                    continue;
                }

                if (activeCandidate != null)
                {
                    error = "Multiple active CinemachineBrain components were found in the output Scene; assign one explicitly.";
                    return null;
                }

                activeCandidate = candidate;
            }

            if (activeCandidate == null)
            {
                error = "Multiple CinemachineBrain components were found in the output Scene; assign one explicitly.";
                return null;
            }

            error = null;
            return activeCandidate;
        }

    }
}
