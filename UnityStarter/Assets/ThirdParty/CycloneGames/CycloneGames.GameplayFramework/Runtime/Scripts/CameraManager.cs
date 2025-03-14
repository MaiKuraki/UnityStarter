using CycloneGames.Logger;
using Unity.Cinemachine;
using UnityEngine;

namespace CycloneGames.GameplayFramework
{
    public class CameraManager : Actor
    {
        private readonly static string DEBUG_FLAG = "<color=cyan>[Camera Manager]</color>";
        
        [SerializeField] protected CinemachineCamera VirtualCamera;
        [SerializeField] private float DefaultFOV = 60.0f;
        
        private PlayerController PCOwner;
        private float lockedFOV;
        public float GetLockedFOV() => lockedFOV;
        public void SetFOV(float NewFOV)
        {
            lockedFOV = NewFOV;
            VirtualCamera.Lens.FieldOfView = lockedFOV;
        }
        private Actor PendingViewTarget;

        public void SetViewTarget(Actor NewTarget)
        {
            PendingViewTarget = NewTarget;
            VirtualCamera.Follow = PendingViewTarget.transform;
            VirtualCamera.LookAt = PendingViewTarget.transform;
        }

        public virtual void InitializeFor(PlayerController PlayerController)
        {
            SetFOV(DefaultFOV);
            
            PCOwner = PlayerController;
            
            SetViewTarget(PlayerController);
        }

        protected override void Awake()
        {
            base.Awake();

            CLogger.LogInfo($"{DEBUG_FLAG}\nYour working camera for CameraManager must have a 'CinemachineBrain' component, this is just a notice.\nIf your camera not following the PlayerController by default, check your Camera.\n");
        }
    }
}
