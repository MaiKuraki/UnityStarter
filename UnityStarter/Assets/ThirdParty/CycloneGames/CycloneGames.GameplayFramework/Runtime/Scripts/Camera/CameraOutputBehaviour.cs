using System;
using UnityEngine;

namespace CycloneGames.GameplayFramework.Runtime
{
    /// <summary>
    /// Unity authoring bridge for an ICameraOutput. The base class owns activation state so
    /// backend implementations only provide resource resolution and pose application.
    /// </summary>
    public abstract class CameraOutputBehaviour : MonoBehaviour, ICameraOutput
    {
        private CameraManager owner;
        private UnityEngine.Object ownershipResource;
        private bool isPrepared;
        private bool isActive;
        private bool isDeactivating;

        public virtual string DisplayName => GetType().Name;
        public bool IsActive => isActive;
        public CameraManager Owner => owner;
        public abstract UnityEngine.Object OutputObject { get; }

        public bool TryPrepare(out UnityEngine.Object resource, out string error)
        {
            if (isActive)
            {
                resource = ownershipResource;
                error = resource != null ? null : "The active camera output lost its ownership resource.";
                return resource != null;
            }

            if (isPrepared && ownershipResource == null)
            {
                isPrepared = false;
                ownershipResource = null;
            }

            if (!isPrepared)
            {
                if (!OnTryPrepare(out ownershipResource, out error))
                {
                    ownershipResource = null;
                    resource = null;
                    return false;
                }

                if (ownershipResource == null)
                {
                    error = "Camera output preparation did not provide an ownership resource.";
                    resource = null;
                    return false;
                }

                isPrepared = true;
            }

            resource = ownershipResource;
            error = null;
            return true;
        }

        public bool TryActivate(CameraManager newOwner, out string error)
        {
            if (newOwner == null)
            {
                error = "CameraManager is required.";
                return false;
            }

            if (isActive)
            {
                if (ReferenceEquals(owner, newOwner))
                {
                    error = null;
                    return true;
                }

                error = $"Camera output '{name}' is already owned by '{owner?.name}'.";
                return false;
            }

            if (!isPrepared && !TryPrepare(out _, out error))
            {
                return false;
            }

            try
            {
                if (!OnActivate(newOwner, out error))
                {
                    RollBackFailedActivation();
                    return false;
                }
            }
            catch
            {
                RollBackFailedActivation();
                throw;
            }

            owner = newOwner;
            isActive = true;
            error = null;
            return true;
        }

        public void ApplyPose(in CameraPose pose)
        {
            if (!isActive)
            {
                throw new InvalidOperationException(
                    $"Camera output '{name}' must be active before a pose can be applied.");
            }

            OnApplyPose(in pose);
        }

        public void Deactivate(CameraManager expectedOwner)
        {
            if (!isActive || isDeactivating)
            {
                return;
            }

            if (expectedOwner != null && !ReferenceEquals(owner, expectedOwner))
            {
                return;
            }

            isDeactivating = true;
            try
            {
                OnDeactivate();
            }
            finally
            {
                isActive = false;
                isPrepared = false;
                owner = null;
                ownershipResource = null;
                isDeactivating = false;
            }
        }

        protected abstract bool OnTryPrepare(
            out UnityEngine.Object ownershipResource,
            out string error);

        protected virtual bool OnActivate(CameraManager newOwner, out string error)
        {
            error = null;
            return true;
        }

        protected abstract void OnApplyPose(in CameraPose pose);

        protected virtual void OnDeactivate() { }

        private void RollBackFailedActivation()
        {
            try
            {
                OnDeactivate();
            }
            finally
            {
                isPrepared = false;
                ownershipResource = null;
            }
        }

        private void OnDestroy()
        {
            CameraManager currentOwner = owner;
            if (currentOwner != null)
            {
                currentOwner.HandleCameraOutputDestroyed(this);
                return;
            }

            Deactivate(null);
        }
    }
}
