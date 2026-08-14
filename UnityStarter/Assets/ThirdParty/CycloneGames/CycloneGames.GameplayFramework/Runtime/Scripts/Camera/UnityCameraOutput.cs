using System;
using UnityEngine;

namespace CycloneGames.GameplayFramework.Runtime
{
    /// <summary>Applies CameraManager output directly to a UnityEngine.Camera.</summary>
    [DisallowMultipleComponent]
    public sealed class UnityCameraOutput : CameraOutputBehaviour
    {
        [SerializeField] private Camera targetCamera;
        [SerializeField] private bool applyTransform = true;
        [SerializeField] private bool applyFieldOfView = true;

        private Camera activeCamera;

        public Camera ActiveCamera => activeCamera;
        public override UnityEngine.Object OutputObject => activeCamera != null ? activeCamera : targetCamera;

        public void SetTargetCamera(Camera camera)
        {
            ThrowIfPreparedOrActive();
            targetCamera = camera;
        }

        protected override bool OnTryPrepare(out string error)
        {
            activeCamera = targetCamera != null
                ? targetCamera
                : GetComponent<Camera>();
            if (activeCamera == null)
            {
                activeCamera = GetComponentInChildren<Camera>(includeInactive: true);
            }

            if (activeCamera == null)
            {
                error = "UnityCameraOutput requires an explicitly assigned Camera or a Camera on its hierarchy.";
                return false;
            }

            return TryAddPreparedResource(activeCamera, out error);
        }

        protected override void OnApplyPose(in CameraPose pose)
        {
            if (activeCamera == null)
            {
                throw new InvalidOperationException("The active Unity Camera was destroyed.");
            }

            if (applyTransform)
            {
                activeCamera.transform.SetPositionAndRotation(pose.Position, pose.Rotation);
            }

            if (applyFieldOfView)
            {
                activeCamera.fieldOfView = pose.Fov;
            }
        }

        protected override void OnReleasePreparedResources()
        {
            activeCamera = null;
        }
    }
}
