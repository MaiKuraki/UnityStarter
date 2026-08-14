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

        public Camera ActiveCamera
        {
            get
            {
                AssertOutputOwnerThread();
                return activeCamera;
            }
        }
        protected override UnityEngine.Object OnGetOutputObject()
        {
            return activeCamera != null ? activeCamera : targetCamera;
        }

        public void SetTargetCamera(Camera camera)
        {
            ThrowIfLifecycleBound();
            targetCamera = camera;
        }

        protected override bool OnTryGetResourceSet(
            out CameraOutputResourceSet resources,
            out string error)
        {
            Camera resolvedCamera = targetCamera != null
                ? targetCamera
                : GetComponent<Camera>();
            if (resolvedCamera == null)
            {
                resolvedCamera = GetComponentInChildren<Camera>(includeInactive: true);
            }

            if (resolvedCamera == null)
            {
                resources = default;
                error = "UnityCameraOutput requires an explicitly assigned Camera or a Camera on its hierarchy.";
                return false;
            }

            resources = new CameraOutputResourceSet(resolvedCamera);
            error = null;
            return true;
        }

        protected override bool OnActivate(
            CameraManager newOwner,
            in CameraOutputResourceSet resources,
            out string error)
        {
            if (resources.Count != 1 || !(resources.GetResource(0) is Camera leasedCamera))
            {
                error = "UnityCameraOutput requires one leased Unity Camera resource.";
                return false;
            }

            activeCamera = leasedCamera;
            error = null;
            return true;
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

        protected override void OnDeactivate()
        {
            activeCamera = null;
        }
    }
}
