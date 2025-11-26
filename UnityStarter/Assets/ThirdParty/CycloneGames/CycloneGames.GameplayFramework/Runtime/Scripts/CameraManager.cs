using CycloneGames.Logger;
using Unity.Cinemachine;
using UnityEngine;

namespace CycloneGames.GameplayFramework.Runtime
{
    public class CameraManager : Actor
    {
        private readonly static string DEBUG_FLAG = "<color=cyan>[Camera Manager]</color>";

        [SerializeField] protected float DefaultFOV = 60.0f;

        /// <summary>
        /// A property to hold the currently active virtual camera.
        /// It can be read publicly but only set by this class or its subclasses.
        /// This is no longer serialized, as it's determined at runtime.
        /// </summary>
        public CinemachineCamera ActiveVirtualCamera { get; private set; }

        private PlayerController PCOwner;
        public bool IsInitialized { get; private set; }
        private float lockedFOV;
        public float GetLockedFOV() => lockedFOV;
        private Transform PendingViewTargetTF;

        /// <summary>
        /// Sets the provided camera as the new active camera.
        /// This is the designated entry point for changing the active camera.
        /// </summary>
        /// <param name="newActiveCamera">The camera to be made active.</param>
        public virtual void SetActiveVirtualCamera(CinemachineCamera newActiveCamera)
        {
            // This is the perfect place for future expansion, e.g., logging or events.
            // CLogger.Log($"New active camera set: {newActiveCamera?.name}");

            ActiveVirtualCamera = newActiveCamera;
        }

        public virtual void SetFOV(float NewFOV)
        {
            lockedFOV = NewFOV;
            // Now, this method operates on whichever camera is currently active.
            if (ActiveVirtualCamera != null)
            {
                ActiveVirtualCamera.Lens.FieldOfView = lockedFOV;
            }
        }

        public virtual void SetViewTarget(Transform NewTargetTF)
        {
            PendingViewTargetTF = NewTargetTF;
            // This also operates on the active camera.
            if (ActiveVirtualCamera != null && PendingViewTargetTF != null)
            {
                ActiveVirtualCamera.Follow = PendingViewTargetTF;
                ActiveVirtualCamera.LookAt = PendingViewTargetTF;
            }
        }

        public virtual void InitializeFor(PlayerController PlayerController)
        {
            PCOwner = PlayerController;
            SetFOV(DefaultFOV);
            SetViewTarget(PlayerController?.transform);
            // If no active virtual camera has been explicitly set, attempt to find one at runtime.
            if (ActiveVirtualCamera == null)
            {
                var brain = Camera.main ? Camera.main.GetComponent<CinemachineBrain>() : null;
                if (brain != null)
                {
                    // Try find any virtual camera in scene; prefer one that already follows the target
                    var candidates = GameObject.FindObjectsByType<CinemachineCamera>(FindObjectsSortMode.None);
                    if (candidates != null && candidates.Length > 0)
                    {
                        // Choose first for determinism; callers can override later via SetActiveVirtualCamera
                        SetActiveVirtualCamera(candidates[0]);
                        // Ensure follow/look target are set
                        SetViewTarget(PlayerController?.transform);
                    }
                }
            }
            IsInitialized = true;
        }

        protected override void Awake()
        {
            base.Awake();
            CLogger.LogInfo($"{DEBUG_FLAG}\nYour working camera for CameraManager must have a 'CinemachineBrain' component, this is just a notice.\nIf your camera not following the PlayerController by default, check your Camera.\n");
        }
    }
}