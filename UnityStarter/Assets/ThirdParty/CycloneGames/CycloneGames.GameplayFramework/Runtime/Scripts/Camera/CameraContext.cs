using System;
using CycloneGames.Logger;

namespace CycloneGames.GameplayFramework.Runtime
{
    public sealed class CameraContext
    {
        private const string DEBUG_FLAG = "<color=cyan>[CameraContext]</color>";
        private readonly CameraMode[] cameraModes;
        private int cameraModeCount;

        public PlayerController Owner { get; }
        public IViewTargetPolicy ViewTargetPolicy { get; private set; }
        public Actor CurrentViewTarget { get; private set; }
        public Actor ManualViewTargetOverride { get; private set; }
        public CameraMode BaseCameraMode { get; private set; }

        public int CameraModeCount => cameraModeCount;
        public int MaxCameraModes => cameraModes.Length;

        public CameraContext(PlayerController owner, int modeCapacity = 8)
        {
            Owner = owner;
            int capacity = modeCapacity > 0 ? modeCapacity : 1;
            cameraModes = new CameraMode[capacity];
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
            TryPushCameraMode(cameraMode);
        }

        /// <summary>
        /// Try to push a camera mode onto the stack.
        /// Returns false if mode is null or stack capacity is full.
        /// </summary>
        public bool TryPushCameraMode(CameraMode cameraMode)
        {
            if (cameraMode == null) return false;

            if (cameraModeCount >= cameraModes.Length)
            {
                CLogger.LogWarning($"{DEBUG_FLAG} CameraMode stack full ({cameraModes.Length}). Drop {cameraMode.GetType().Name}.");
                return false;
            }

            cameraModes[cameraModeCount++] = cameraMode;
            cameraMode.OnActivate(this);
            return true;
        }

        /// <summary>
        /// Try to push a camera mode. If the stack is full, replace the oldest stacked mode.
        /// Returns false only when mode is null.
        /// </summary>
        public bool TryPushOrReplaceOldest(CameraMode cameraMode)
        {
            if (cameraMode == null) return false;

            if (cameraModeCount < cameraModes.Length)
            {
                cameraModes[cameraModeCount++] = cameraMode;
                cameraMode.OnActivate(this);
                return true;
            }

            if (cameraModeCount <= 0)
            {
                CLogger.LogWarning($"{DEBUG_FLAG} Invalid full-stack state. Unable to push {cameraMode.GetType().Name}.");
                return false;
            }

            CameraMode oldest = cameraModes[0];
            oldest?.OnDeactivate(this);

            int moveCount = cameraModeCount - 1;
            if (moveCount > 0)
            {
                Array.Copy(cameraModes, 1, cameraModes, 0, moveCount);
            }

            cameraModes[cameraModeCount - 1] = cameraMode;
            cameraMode.OnActivate(this);
            return true;
        }

        public bool RemoveCameraMode(CameraMode cameraMode)
        {
            if (cameraMode == null) return false;

            for (int i = cameraModeCount - 1; i >= 0; i--)
            {
                if (!ReferenceEquals(cameraModes[i], cameraMode)) continue;

                cameraModes[i].OnDeactivate(this);

                int moveCount = cameraModeCount - i - 1;
                if (moveCount > 0)
                {
                    Array.Copy(cameraModes, i + 1, cameraModes, i, moveCount);
                }

                cameraModeCount--;
                cameraModes[cameraModeCount] = null;
                return true;
            }

            return false;
        }

        public CameraMode GetCameraModeAt(int index)
        {
            if (index < 0 || index >= cameraModeCount)
            {
                throw new IndexOutOfRangeException($"Camera mode index out of range: {index}, count={cameraModeCount}");
            }

            return cameraModes[index];
        }

        public CameraMode GetPrimaryCameraMode()
        {
            return cameraModeCount > 0 ? cameraModes[cameraModeCount - 1] : BaseCameraMode;
        }
    }
}