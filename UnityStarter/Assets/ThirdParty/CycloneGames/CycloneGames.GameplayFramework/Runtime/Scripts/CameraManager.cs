using CycloneGames.Logger;
using Unity.Cinemachine;
using UnityEngine;

namespace CycloneGames.GameplayFramework.Runtime
{
    public class CameraManager : Actor
    {
        private const string DEBUG_FLAG = "<color=cyan>[CameraManager]</color>";

        [SerializeField] protected float DefaultFOV = 60.0f;

        public CinemachineCamera ActiveVirtualCamera { get; private set; }

        private PlayerController PCOwner;
        public bool IsInitialized { get; private set; }
        private float lockedFOV;
        public float GetLockedFOV() => lockedFOV;
        private Transform PendingViewTargetTF;

        // Cached to avoid FindObjectsByType GC allocation on every initialization
        private static CinemachineCamera[] s_cameraBuffer;

        public virtual void SetActiveVirtualCamera(CinemachineCamera newActiveCamera)
        {
            ActiveVirtualCamera = newActiveCamera;
        }

        public virtual void SetFOV(float NewFOV)
        {
            lockedFOV = NewFOV;
            if (ActiveVirtualCamera != null)
            {
                ActiveVirtualCamera.Lens.FieldOfView = lockedFOV;
            }
        }

        public virtual void SetViewTarget(Transform NewTargetTF)
        {
            PendingViewTargetTF = NewTargetTF;
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
                            SetViewTarget(PlayerController?.transform);
                        }
                    }
                }
            }
            IsInitialized = true;
        }

        protected override void Awake()
        {
            base.Awake();
            CLogger.LogInfo($"{DEBUG_FLAG} CameraManager requires a CinemachineBrain on the active Camera.");
        }
    }
}