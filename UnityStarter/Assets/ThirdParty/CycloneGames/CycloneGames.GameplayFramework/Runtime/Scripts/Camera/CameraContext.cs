using System;
using CycloneGames.Logger;

namespace CycloneGames.GameplayFramework.Runtime
{
    public sealed class CameraContext
    {
        private const string DEBUG_FLAG = "<color=cyan>[CameraContext]</color>";
        private readonly CameraMode[] cameraModes;
        private int cameraModeCount;
        private bool isClearing;
        private bool isChangingModes;
        private bool isEvaluating;
        private bool clearRequested;

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
            if (isClearing || isChangingModes || isEvaluating) return;
            if (ReferenceEquals(BaseCameraMode, cameraMode)) return;

            isChangingModes = true;
            CameraMode previousMode = BaseCameraMode;
            try
            {
                if (!TryDeactivate(previousMode))
                {
                    TryActivate(previousMode);
                    return;
                }

                BaseCameraMode = cameraMode;
                if (!TryActivate(cameraMode))
                {
                    BaseCameraMode = previousMode;
                    TryActivate(previousMode);
                }
            }
            finally
            {
                isChangingModes = false;
            }
        }

        public bool PushCameraMode(CameraMode cameraMode)
        {
            return TryPushCameraMode(cameraMode);
        }

        /// <summary>
        /// Try to push a camera mode onto the stack.
        /// Returns false if the mode is null, already stacked, the context is clearing,
        /// or stack capacity is full.
        /// </summary>
        public bool TryPushCameraMode(CameraMode cameraMode)
        {
            if (cameraMode == null || isClearing || isChangingModes || isEvaluating || ContainsCameraMode(cameraMode)) return false;

            if (cameraModeCount >= cameraModes.Length)
            {
                CLogger.LogWarning($"{DEBUG_FLAG} CameraMode stack full ({cameraModes.Length}). Drop {cameraMode.GetType().Name}.");
                return false;
            }

            isChangingModes = true;
            try
            {
                cameraModes[cameraModeCount++] = cameraMode;
                if (TryActivate(cameraMode))
                {
                    return true;
                }

                cameraModeCount--;
                cameraModes[cameraModeCount] = null;
                return false;
            }
            finally
            {
                isChangingModes = false;
            }
        }

        /// <summary>
        /// Try to push a camera mode. If the stack is full, replace the oldest stacked mode.
        /// Returns false if the mode is null, already stacked, or the context is clearing.
        /// </summary>
        public bool TryPushOrReplaceOldest(CameraMode cameraMode)
        {
            return TryPushOrReplaceOldest(cameraMode, out _);
        }

        /// <summary>
        /// Try to push a camera mode. If the stack is full, replace the oldest stacked mode
        /// and return it through <paramref name="replacedMode"/>.
        /// </summary>
        public bool TryPushOrReplaceOldest(CameraMode cameraMode, out CameraMode replacedMode)
        {
            replacedMode = null;
            if (cameraMode == null || isClearing || isChangingModes || isEvaluating || ContainsCameraMode(cameraMode)) return false;

            if (cameraModeCount < cameraModes.Length)
            {
                return TryPushCameraMode(cameraMode);
            }

            if (cameraModeCount <= 0)
            {
                CLogger.LogWarning($"{DEBUG_FLAG} Invalid full-stack state. Unable to push {cameraMode.GetType().Name}.");
                return false;
            }

            isChangingModes = true;
            try
            {
                CameraMode oldest = cameraModes[0];
                int moveCount = cameraModeCount - 1;
                if (moveCount > 0)
                {
                    Array.Copy(cameraModes, 1, cameraModes, 0, moveCount);
                }

                cameraModes[cameraModeCount - 1] = cameraMode;
                if (!TryDeactivate(oldest) || !TryActivate(cameraMode))
                {
                    if (moveCount > 0)
                    {
                        Array.Copy(cameraModes, 0, cameraModes, 1, moveCount);
                    }

                    cameraModes[0] = oldest;
                    TryActivate(oldest);
                    return false;
                }

                replacedMode = oldest;
                return true;
            }
            finally
            {
                isChangingModes = false;
            }
        }

        public bool RemoveCameraMode(CameraMode cameraMode)
        {
            if (cameraMode == null || isClearing || isChangingModes || isEvaluating) return false;

            for (int i = cameraModeCount - 1; i >= 0; i--)
            {
                if (!ReferenceEquals(cameraModes[i], cameraMode)) continue;

                isChangingModes = true;
                try
                {
                    int moveCount = cameraModeCount - i - 1;
                    if (moveCount > 0)
                    {
                        Array.Copy(cameraModes, i + 1, cameraModes, i, moveCount);
                    }

                    cameraModeCount--;
                    cameraModes[cameraModeCount] = null;
                    TryDeactivate(cameraMode);
                    return true;
                }
                finally
                {
                    isChangingModes = false;
                }
            }

            return false;
        }

        /// <summary>
        /// Deactivates stacked modes in reverse order, followed by the base mode.
        /// The context is empty before callbacks run so deactivation observers cannot
        /// access modes that are already being torn down.
        /// </summary>
        public void Clear()
        {
            if (isEvaluating)
            {
                clearRequested = true;
                return;
            }

            if (isClearing || isChangingModes) return;

            isClearing = true;
            isChangingModes = true;
            int count = cameraModeCount;
            CameraMode baseCameraMode = BaseCameraMode;
            cameraModeCount = 0;
            BaseCameraMode = null;

            try
            {
                for (int i = count - 1; i >= 0; i--)
                {
                    CameraMode cameraMode = cameraModes[i];
                    cameraModes[i] = null;
                    TryDeactivate(cameraMode);
                }

                TryDeactivate(baseCameraMode);
            }
            finally
            {
                Array.Clear(cameraModes, 0, count);
                isChangingModes = false;
                isClearing = false;
                clearRequested = false;
            }
        }

        internal bool TryBeginEvaluation()
        {
            if (isClearing || isChangingModes || isEvaluating)
            {
                return false;
            }

            isEvaluating = true;
            return true;
        }

        internal void EndEvaluation()
        {
            if (!isEvaluating)
            {
                return;
            }

            isEvaluating = false;
            if (clearRequested)
            {
                Clear();
            }
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

        private bool ContainsCameraMode(CameraMode cameraMode)
        {
            for (int i = 0; i < cameraModeCount; i++)
            {
                if (ReferenceEquals(cameraModes[i], cameraMode))
                {
                    return true;
                }
            }

            return false;
        }

        private bool TryActivate(CameraMode cameraMode)
        {
            if (cameraMode == null)
            {
                return true;
            }

            try
            {
                cameraMode.OnActivate(this);
                return true;
            }
            catch (Exception exception)
            {
                CLogger.LogError($"{DEBUG_FLAG} CameraMode activation failed: {exception}");
                return false;
            }
        }

        private bool TryDeactivate(CameraMode cameraMode)
        {
            if (cameraMode == null)
            {
                return true;
            }

            try
            {
                cameraMode.OnDeactivate(this);
                return true;
            }
            catch (Exception exception)
            {
                CLogger.LogError($"{DEBUG_FLAG} CameraMode deactivation failed: {exception}");
                return false;
            }
        }
    }
}
