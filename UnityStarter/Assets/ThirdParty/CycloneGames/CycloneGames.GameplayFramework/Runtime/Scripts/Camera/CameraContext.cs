using System.Collections.Generic;

namespace CycloneGames.GameplayFramework.Runtime
{
    public sealed class CameraContext
    {
        private readonly List<CameraMode> cameraModes = new List<CameraMode>(4);

        public PlayerController Owner { get; }
        public IViewTargetPolicy ViewTargetPolicy { get; private set; }
        public Actor CurrentViewTarget { get; private set; }
        public Actor ManualViewTargetOverride { get; private set; }
        public CameraMode BaseCameraMode { get; private set; }

        public int CameraModeCount => cameraModes.Count;

        public CameraContext(PlayerController owner)
        {
            Owner = owner;
        }

        public void SetViewTargetPolicy(IViewTargetPolicy policy)
        {
            ViewTargetPolicy = policy;
        }

        public Actor ResolveViewTarget(Actor suggestedTarget)
        {
            CurrentViewTarget = ViewTargetPolicy != null
                ? ViewTargetPolicy.ResolveViewTarget(this, suggestedTarget)
                : suggestedTarget;
            return CurrentViewTarget;
        }

        public void SetResolvedViewTarget(Actor target)
        {
            CurrentViewTarget = target;
        }

        public void SetManualViewTargetOverride(Actor target)
        {
            ManualViewTargetOverride = target;
            CurrentViewTarget = target;
        }

        public void ClearManualViewTargetOverride()
        {
            ManualViewTargetOverride = null;
        }

        public void SetBaseCameraMode(CameraMode cameraMode)
        {
            if (ReferenceEquals(BaseCameraMode, cameraMode)) return;

            BaseCameraMode?.OnDeactivate(this);
            BaseCameraMode = cameraMode;
            BaseCameraMode?.OnActivate(this);
        }

        public void PushCameraMode(CameraMode cameraMode)
        {
            if (cameraMode == null) return;

            cameraModes.Add(cameraMode);
            cameraMode.OnActivate(this);
        }

        public bool RemoveCameraMode(CameraMode cameraMode)
        {
            if (cameraMode == null) return false;

            for (int i = cameraModes.Count - 1; i >= 0; i--)
            {
                if (!ReferenceEquals(cameraModes[i], cameraMode)) continue;

                cameraModes[i].OnDeactivate(this);
                cameraModes.RemoveAt(i);
                return true;
            }

            return false;
        }

        public CameraMode GetCameraModeAt(int index)
        {
            return cameraModes[index];
        }

        public CameraMode GetPrimaryCameraMode()
        {
            return cameraModes.Count > 0 ? cameraModes[cameraModes.Count - 1] : BaseCameraMode;
        }
    }
}