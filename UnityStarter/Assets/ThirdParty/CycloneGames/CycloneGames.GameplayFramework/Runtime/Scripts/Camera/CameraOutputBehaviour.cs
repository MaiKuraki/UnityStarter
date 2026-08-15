using System;
using System.Threading;
using UnityEngine;

namespace CycloneGames.GameplayFramework.Runtime
{
    /// <summary>
    /// Unity authoring bridge for an ICameraOutput. Resource discovery is stateless; backend
    /// mutation begins only after CameraManager supplies an already leased resource snapshot.
    /// </summary>
    public abstract class CameraOutputBehaviour : MonoBehaviour, ICameraOutput
    {
        private enum OutputState : byte
        {
            Idle,
            Activating,
            Active,
            Faulted,
            Deactivating,
        }

        private CameraManager owner;
        private CameraOutputResourceSet activeResources;
        private int ownerThreadId;
        private OutputState state;

        /// <summary>
        /// Captures the Unity lifecycle owner thread. Overrides must call base.Awake().
        /// </summary>
        protected virtual void Awake()
        {
            BindOutputOwnerThread();
        }

        /// <summary>
        /// Revalidates lifecycle ownership when the component is enabled. Overrides must call
        /// base.OnEnable().
        /// </summary>
        protected virtual void OnEnable()
        {
            BindOutputOwnerThread();
        }

        public string DisplayName
        {
            get
            {
                AssertOutputOwnerThread();
                return GetType().Name;
            }
        }

        /// <summary>
        /// True when the backend is active or its exact state is unknown after a lifecycle
        /// failure. A faulted output remains owned until a later Deactivate call succeeds.
        /// </summary>
        public bool IsActive
        {
            get
            {
                AssertOutputOwnerThread();
                return state != OutputState.Idle;
            }
        }

        public CameraManager Owner
        {
            get
            {
                AssertOutputOwnerThread();
                return owner;
            }
        }

        public UnityEngine.Object OutputObject
        {
            get
            {
                AssertOutputOwnerThread();
                return OnGetOutputObject();
            }
        }

        public bool TryGetResourceSet(
            out CameraOutputResourceSet resources,
            out string error)
        {
            AssertOutputOwnerThread();
            resources = default;
            if (this == null)
            {
                error = "The camera output was destroyed.";
                return false;
            }

            if (!OnTryGetResourceSet(out resources, out error))
            {
                resources = default;
                return false;
            }

            if (this == null)
            {
                resources = default;
                error = "The camera output was destroyed during resource discovery.";
                return false;
            }

            if (!resources.TryValidate(out error))
            {
                resources = default;
                return false;
            }

            error = null;
            return true;
        }

        public bool TryActivate(
            CameraManager newOwner,
            in CameraOutputResourceSet resources,
            out string error)
        {
            AssertOutputOwnerThread();
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

            if (!resources.TryValidate(out error))
            {
                return false;
            }

            if (state == OutputState.Active)
            {
                if (ReferenceEquals(owner, newOwner) && activeResources == resources)
                {
                    error = null;
                    return true;
                }

                error = "Camera output is already bound to another owner or resource snapshot.";
                return false;
            }

            if (state != OutputState.Idle)
            {
                error = "Camera output activation is blocked until its faulted lifecycle is released.";
                return false;
            }

            owner = newOwner;
            activeResources = resources;
            state = OutputState.Activating;
            try
            {
                if (!OnActivate(newOwner, in resources, out error))
                {
                    state = OutputState.Faulted;
                    return false;
                }

                if (this == null)
                {
                    error = "The camera output was destroyed during activation.";
                    state = OutputState.Faulted;
                    return false;
                }

                if (!activeResources.TryValidate(out error))
                {
                    state = OutputState.Faulted;
                    return false;
                }

                state = OutputState.Active;
                error = null;
                return true;
            }
            catch
            {
                state = OutputState.Faulted;
                throw;
            }
        }

        public void ApplyPose(in CameraPose pose)
        {
            AssertOutputOwnerThread();
            if (state != OutputState.Active)
            {
                throw new InvalidOperationException(
                    $"Camera output '{name}' must be active before a pose can be applied.");
            }

            OnApplyPose(in pose);
        }

        public void Deactivate(CameraManager expectedOwner)
        {
            AssertOutputOwnerThread();
            if (state == OutputState.Idle)
            {
                return;
            }

            if (state == OutputState.Activating || state == OutputState.Deactivating)
            {
                throw new InvalidOperationException(
                    "Camera output deactivation cannot run during another lifecycle transition.");
            }

            if (!ReferenceEquals(expectedOwner, null) &&
                !ReferenceEquals(owner, expectedOwner))
            {
                return;
            }

            state = OutputState.Deactivating;
            try
            {
                OnDeactivate();
            }
            catch
            {
                state = OutputState.Faulted;
                throw;
            }

            activeResources = default;
            owner = null;
            state = OutputState.Idle;
        }

        /// <summary>
        /// Resolves an immutable ownership snapshot without changing lifecycle or backend state.
        /// </summary>
        protected abstract bool OnTryGetResourceSet(
            out CameraOutputResourceSet resources,
            out string error);

        protected abstract UnityEngine.Object OnGetOutputObject();

        /// <summary>
        /// Activates the backend with the exact snapshot already leased by CameraManager.
        /// Implementations must not substitute or discover different resources here.
        /// </summary>
        protected virtual bool OnActivate(
            CameraManager newOwner,
            in CameraOutputResourceSet resources,
            out string error)
        {
            error = null;
            return true;
        }

        protected abstract void OnApplyPose(in CameraPose pose);

        /// <summary>
        /// Restores all backend state changed by OnActivate. It must support retry after an
        /// exception because ownership remains quarantined until this callback completes.
        /// </summary>
        protected virtual void OnDeactivate()
        {
        }

        protected void ThrowIfLifecycleBound()
        {
            AssertOutputOwnerThread();
            if (state != OutputState.Idle)
            {
                throw new InvalidOperationException(
                    "Camera output references cannot change while the output is active or faulted.");
            }
        }

        /// <summary>
        /// Releases World ownership when Unity destroys this component. Derived outputs that
        /// override this Unity message must call base.OnDestroy after their local cleanup.
        /// </summary>
        protected virtual void OnDestroy()
        {
            AssertOutputOwnerThread();
            if (state == OutputState.Activating || state == OutputState.Deactivating)
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

        protected void AssertOutputOwnerThread()
        {
            int expectedThreadId = ownerThreadId;
            if (expectedThreadId == 0)
            {
                throw new InvalidOperationException(
                    "Camera output lifecycle ownership has not been initialized.");
            }

            if (Thread.CurrentThread.ManagedThreadId != expectedThreadId)
            {
                throw new InvalidOperationException(
                    "Camera output live state must be accessed on its Unity lifecycle owner thread.");
            }
        }

        private void BindOutputOwnerThread()
        {
            int currentThreadId = Thread.CurrentThread.ManagedThreadId;
            if (ownerThreadId != 0 && ownerThreadId != currentThreadId)
            {
                throw new InvalidOperationException(
                    "Camera output lifecycle ownership cannot move between threads.");
            }

            ownerThreadId = currentThreadId;
        }
    }
}
