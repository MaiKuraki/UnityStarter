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
        private enum OutputState : byte
        {
            Idle,
            Preparing,
            Prepared,
            Activating,
            Active,
            Deactivating,
        }

        private readonly UnityEngine.Object[] preparedResources =
            new UnityEngine.Object[CameraOutputLimits.MaximumPreparedResourceCount];

        private CameraManager owner;
        private OutputState state;
        private int preparedResourceCount;

        public virtual string DisplayName => GetType().Name;
        public bool IsActive => state == OutputState.Active || state == OutputState.Deactivating;
        public CameraManager Owner => owner;
        public abstract UnityEngine.Object OutputObject { get; }
        public int PreparedResourceCount => preparedResourceCount;

        public bool TryPrepare(out string error)
        {
            if (this == null)
            {
                error = "The camera output was destroyed.";
                return false;
            }

            if (state == OutputState.Active)
            {
                return ValidatePreparedResources(out error);
            }

            if (state == OutputState.Prepared)
            {
                if (ValidatePreparedResources(out error))
                {
                    return true;
                }

                ReleasePreparedState(invokeDeactivation: false);
                return false;
            }

            if (state != OutputState.Idle)
            {
                error = "Camera output preparation cannot run during another lifecycle transition.";
                return false;
            }

            state = OutputState.Preparing;
            try
            {
                if (!OnTryPrepare(out error))
                {
                    ReleasePreparedState(invokeDeactivation: false);
                    return false;
                }

                if (this == null)
                {
                    error = "The camera output was destroyed during preparation.";
                    ReleasePreparedState(invokeDeactivation: false);
                    return false;
                }

                if (!ValidatePreparedResources(out error))
                {
                    ReleasePreparedState(invokeDeactivation: false);
                    return false;
                }

                state = OutputState.Prepared;
                error = null;
                return true;
            }
            catch
            {
                ReleasePreparedState(invokeDeactivation: false);
                throw;
            }
        }

        public UnityEngine.Object GetPreparedResource(int index)
        {
            if ((uint)index >= preparedResourceCount)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            return preparedResources[index];
        }

        public bool TryActivate(CameraManager newOwner, out string error)
        {
            if (this == null)
            {
                error = "The camera output was destroyed.";
                return false;
            }

            if (newOwner == null)
            {
                error = "CameraManager is required.";
                return false;
            }

            if (state == OutputState.Active)
            {
                if (ReferenceEquals(owner, newOwner))
                {
                    return ValidatePreparedResources(out error);
                }

                error = "Camera output is already owned by another CameraManager.";
                return false;
            }

            if (state != OutputState.Prepared && !TryPrepare(out error))
            {
                return false;
            }

            if (state != OutputState.Prepared)
            {
                error = "Camera output activation cannot run during another lifecycle transition.";
                return false;
            }

            state = OutputState.Activating;
            bool activationSucceeded;
            try
            {
                activationSucceeded = OnActivate(newOwner, out error);
            }
            catch
            {
                ReleasePreparedState(invokeDeactivation: true);
                throw;
            }

            if (!activationSucceeded)
            {
                ReleasePreparedState(invokeDeactivation: true);
                return false;
            }

            if (this == null)
            {
                error = "The camera output was destroyed during activation.";
                ReleasePreparedState(invokeDeactivation: true);
                return false;
            }

            if (!ValidatePreparedResources(out error))
            {
                ReleasePreparedState(invokeDeactivation: true);
                return false;
            }

            owner = newOwner;
            state = OutputState.Active;
            error = null;
            return true;
        }

        public void ApplyPose(in CameraPose pose)
        {
            if (state != OutputState.Active)
            {
                throw new InvalidOperationException(
                    $"Camera output '{name}' must be active before a pose can be applied.");
            }

            OnApplyPose(in pose);
        }

        public void Deactivate(CameraManager expectedOwner)
        {
            if (state == OutputState.Idle || state == OutputState.Deactivating)
            {
                return;
            }

            if (state == OutputState.Preparing || state == OutputState.Activating)
            {
                throw new InvalidOperationException(
                    "Camera output deactivation cannot run during another lifecycle transition.");
            }

            if (state == OutputState.Active &&
                !ReferenceEquals(expectedOwner, null) &&
                !ReferenceEquals(owner, expectedOwner))
            {
                return;
            }

            bool invokeDeactivation = state == OutputState.Active;
            state = OutputState.Deactivating;
            try
            {
                if (invokeDeactivation)
                {
                    OnDeactivate();
                }
            }
            finally
            {
                ReleasePreparedState(invokeDeactivation: false);
            }
        }

        protected abstract bool OnTryPrepare(out string error);

        protected virtual bool OnActivate(CameraManager newOwner, out string error)
        {
            error = null;
            return true;
        }

        protected abstract void OnApplyPose(in CameraPose pose);

        protected virtual void OnDeactivate() { }

        /// <summary>
        /// Clears backend references resolved during preparation. This callback must not restore
        /// externally owned state; <see cref="OnDeactivate"/> owns that responsibility.
        /// </summary>
        protected virtual void OnReleasePreparedResources() { }

        protected bool TryAddPreparedResource(UnityEngine.Object resource, out string error)
        {
            if (state != OutputState.Preparing)
            {
                throw new InvalidOperationException(
                    "Prepared resources can only be registered from OnTryPrepare.");
            }

            if (resource == null)
            {
                error = "Camera output preparation produced a missing or destroyed resource.";
                return false;
            }

            if (preparedResourceCount >= CameraOutputLimits.MaximumPreparedResourceCount)
            {
                error = $"Camera output preparation exceeds the {CameraOutputLimits.MaximumPreparedResourceCount}-resource limit.";
                return false;
            }

            int resourceId = resource.GetInstanceID();
            for (int i = 0; i < preparedResourceCount; i++)
            {
                UnityEngine.Object existing = preparedResources[i];
                if (existing != null && existing.GetInstanceID() == resourceId)
                {
                    error = "Camera output preparation produced a duplicate ownership resource.";
                    return false;
                }
            }

            preparedResources[preparedResourceCount++] = resource;
            error = null;
            return true;
        }

        protected void ThrowIfPreparedOrActive()
        {
            if (state != OutputState.Idle)
            {
                throw new InvalidOperationException(
                    "Camera output references cannot change while resources are prepared or active.");
            }
        }

        private bool ValidatePreparedResources(out string error)
        {
            if (preparedResourceCount <= 0 ||
                preparedResourceCount > CameraOutputLimits.MaximumPreparedResourceCount)
            {
                error = "Camera output preparation must provide at least one ownership resource.";
                return false;
            }

            for (int i = 0; i < preparedResourceCount; i++)
            {
                UnityEngine.Object resource = preparedResources[i];
                if (resource == null)
                {
                    error = "A prepared camera output resource was destroyed.";
                    return false;
                }

                int resourceId = resource.GetInstanceID();
                for (int j = 0; j < i; j++)
                {
                    UnityEngine.Object previous = preparedResources[j];
                    if (previous != null && previous.GetInstanceID() == resourceId)
                    {
                        error = "Camera output preparation produced a duplicate ownership resource.";
                        return false;
                    }
                }
            }

            error = null;
            return true;
        }

        private void ReleasePreparedState(bool invokeDeactivation)
        {
            try
            {
                if (invokeDeactivation)
                {
                    OnDeactivate();
                }
            }
            finally
            {
                try
                {
                    OnReleasePreparedResources();
                }
                finally
                {
                    for (int i = 0; i < preparedResourceCount; i++)
                    {
                        preparedResources[i] = null;
                    }

                    preparedResourceCount = 0;
                    owner = null;
                    state = OutputState.Idle;
                }
            }
        }

        private void OnDestroy()
        {
            if (state == OutputState.Preparing ||
                state == OutputState.Activating ||
                state == OutputState.Deactivating)
            {
                return;
            }

            CameraManager currentOwner = owner;
            if (!ReferenceEquals(currentOwner, null))
            {
                currentOwner.HandleCameraOutputDestroyed(this);
                return;
            }

            Deactivate(null);
        }
    }
}
