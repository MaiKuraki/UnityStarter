using System;
using System.Threading;
using CycloneGames.Logging;

namespace CycloneGames.GameplayFramework.Runtime
{
    public sealed class CameraContext
    {
        private static readonly LogChannel Log = GameplayFrameworkLog.Channel;
        private readonly CameraMode[] cameraModes;
        private readonly CameraMode[] pendingRemovalModes;
        private readonly int ownerThreadId;
        private IViewTargetPolicy viewTargetPolicy;
        private Actor currentViewTarget;
        private Actor manualViewTargetOverride;
        private CameraMode baseCameraMode;
        private int cameraModeCount;
        private int pendingRemovalCount;
        private bool isClearing;
        private bool isChangingModes;
        private bool isEvaluating;
        private bool isProcessingPendingRemovals;
        private bool clearRequested;
        private bool hasModeLifecycleFault;
        private CameraMode uncommittedLifecycleMode;

        public PlayerController Owner { get; }
        public IViewTargetPolicy ViewTargetPolicy
        {
            get
            {
                EnsureOwnerThread();
                return viewTargetPolicy;
            }
            private set => viewTargetPolicy = value;
        }

        public Actor CurrentViewTarget
        {
            get
            {
                EnsureOwnerThread();
                return currentViewTarget;
            }
            private set => currentViewTarget = value;
        }

        public Actor ManualViewTargetOverride
        {
            get
            {
                EnsureOwnerThread();
                return manualViewTargetOverride;
            }
            private set => manualViewTargetOverride = value;
        }

        public CameraMode BaseCameraMode
        {
            get
            {
                EnsureOwnerThread();
                return baseCameraMode;
            }
            private set => baseCameraMode = value;
        }

        public int CameraModeCount
        {
            get
            {
                EnsureOwnerThread();
                return cameraModeCount;
            }
        }

        public int MaxCameraModes => cameraModes.Length;
        public bool HasModeLifecycleFault
        {
            get
            {
                EnsureOwnerThread();
                return hasModeLifecycleFault;
            }
        }

        public CameraContext(PlayerController owner, int modeCapacity = 8)
        {
            Owner = owner;
            ownerThreadId = Thread.CurrentThread.ManagedThreadId;
            int capacity = modeCapacity > 0 ? modeCapacity : 1;
            cameraModes = new CameraMode[capacity];
            pendingRemovalModes = new CameraMode[capacity];
        }

        public void SetViewTargetPolicy(IViewTargetPolicy policy)
        {
            EnsureOwnerThread();
            ViewTargetPolicy = policy;
        }

        public Actor ResolveViewTarget(Actor suggestedTarget)
        {
            EnsureOwnerThread();
            CurrentViewTarget = ViewTargetPolicy != null
                ? ViewTargetPolicy.ResolveViewTarget(this, suggestedTarget)
                : suggestedTarget;
            return CurrentViewTarget;
        }

        public void SetResolvedViewTarget(Actor target)
        {
            EnsureOwnerThread();
            CurrentViewTarget = target;
        }

        public void SetManualViewTargetOverride(Actor target)
        {
            EnsureOwnerThread();
            ManualViewTargetOverride = target;
            CurrentViewTarget = target;
        }

        public void ClearManualViewTargetOverride()
        {
            EnsureOwnerThread();
            ManualViewTargetOverride = null;
        }

        public void SetBaseCameraMode(CameraMode cameraMode)
        {
            EnsureOwnerThread();
            if (isClearing || isChangingModes || isEvaluating || hasModeLifecycleFault) return;
            if (ReferenceEquals(BaseCameraMode, cameraMode)) return;
            if (cameraMode != null && ContainsCameraMode(cameraMode)) return;

            isChangingModes = true;
            CameraMode previousMode = BaseCameraMode;
            try
            {
                if (!TryDeactivate(previousMode))
                {
                    // Deactivation may have failed before or after external cleanup. Retain the
                    // existing reference and never activate it a second time in an unknown state.
                    hasModeLifecycleFault = true;
                    return;
                }

                BaseCameraMode = cameraMode;
                if (!TryActivate(cameraMode))
                {
                    if (!TryDeactivate(cameraMode))
                    {
                        hasModeLifecycleFault = true;
                        return;
                    }

                    // Publish the cleanup owner before invoking external rollback code. If the
                    // callback runs out of memory after committing side effects, the context still
                    // retains the only handle that can later deactivate the previous mode.
                    BaseCameraMode = previousMode;
                    if (!TryActivate(previousMode))
                    {
                        if (!TryDeactivate(previousMode))
                        {
                            hasModeLifecycleFault = true;
                            return;
                        }

                        BaseCameraMode = null;
                    }
                }
            }
            finally
            {
                CompleteModeChange();
            }
        }

        /// <summary>
        /// Try to push a camera mode onto the stack.
        /// Returns false if the mode is null, already stacked, the context is clearing,
        /// or stack capacity is full.
        /// </summary>
        public bool TryPushCameraMode(CameraMode cameraMode)
        {
            EnsureOwnerThread();
            if (cameraMode == null ||
                isClearing ||
                isChangingModes ||
                isEvaluating ||
                hasModeLifecycleFault ||
                ContainsCameraMode(cameraMode)) return false;

            if (cameraModeCount >= cameraModes.Length)
            {
                Log.Warning(
                    (Capacity: cameraModes.Length, Mode: cameraMode),
                    static (state, builder) =>
                    {
                        builder.Append("CameraMode stack reached capacity ");
                        builder.Append(state.Capacity);
                        builder.Append("; dropped '");
                        builder.Append(state.Mode.GetType().Name);
                        builder.Append("'.");
                    });
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

                if (!TryDeactivate(cameraMode))
                {
                    // Activation may have committed external work before throwing. Retain the
                    // only reference and freeze evaluation until Clear can complete cleanup.
                    hasModeLifecycleFault = true;
                    return false;
                }

                cameraModeCount--;
                cameraModes[cameraModeCount] = null;
                return false;
            }
            finally
            {
                CompleteModeChange();
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
            EnsureOwnerThread();
            replacedMode = null;
            if (cameraMode == null ||
                isClearing ||
                isChangingModes ||
                isEvaluating ||
                hasModeLifecycleFault ||
                ContainsCameraMode(cameraMode)) return false;

            if (cameraModeCount < cameraModes.Length)
            {
                return TryPushCameraMode(cameraMode);
            }

            if (cameraModeCount <= 0)
            {
                Log.Warning(
                    cameraMode,
                    static (mode, builder) =>
                    {
                        builder.Append("CameraMode stack entered an invalid full-stack state; unable to push '");
                        builder.Append(mode.GetType().Name);
                        builder.Append("'.");
                    });
                return false;
            }

            isChangingModes = true;
            try
            {
                CameraMode oldest = cameraModes[0];
                int moveCount = cameraModeCount - 1;
                if (!TryDeactivate(oldest))
                {
                    hasModeLifecycleFault = true;
                    return false;
                }

                uncommittedLifecycleMode = cameraMode;
                bool activated;
                try
                {
                    activated = TryActivate(cameraMode);
                }
                catch (OutOfMemoryException)
                {
                    // The oldest mode is known to be inactive. Remove that stale logical entry,
                    // while retaining the replacement as the only cleanup handle.
                    RemoveModeAtWithoutDeactivation(0);
                    throw;
                }

                if (activated)
                {
                    uncommittedLifecycleMode = null;
                    if (moveCount > 0)
                    {
                        Array.Copy(cameraModes, 1, cameraModes, 0, moveCount);
                    }

                    cameraModes[cameraModeCount - 1] = cameraMode;
                    replacedMode = oldest;
                    return true;
                }

                bool replacementCleanupSucceeded;
                try
                {
                    replacementCleanupSucceeded = TryDeactivate(cameraMode);
                }
                catch (OutOfMemoryException)
                {
                    // The replacement may have partially activated and must remain quarantined.
                    // The oldest mode is already inactive and must not be deactivated twice.
                    RemoveModeAtWithoutDeactivation(0);
                    throw;
                }

                if (!replacementCleanupSucceeded)
                {
                    // The old mode is known to be inactive. Replace it with the faulted new
                    // mode so Clear retains a cleanup handle and no pooled caller can reuse it.
                    if (moveCount > 0)
                    {
                        Array.Copy(cameraModes, 1, cameraModes, 0, moveCount);
                    }

                    cameraModes[cameraModeCount - 1] = cameraMode;
                    uncommittedLifecycleMode = null;
                    hasModeLifecycleFault = true;
                    return false;
                }

                uncommittedLifecycleMode = null;

                if (TryActivate(oldest))
                {
                    return false;
                }

                if (!TryDeactivate(oldest))
                {
                    hasModeLifecycleFault = true;
                    return false;
                }

                RemoveModeAtWithoutDeactivation(0);
                return false;
            }
            finally
            {
                CompleteModeChange();
            }
        }

        /// <summary>
        /// Removes a stacked mode immediately, or records a fixed-capacity removal request when
        /// camera evaluation is in progress. A true result means the request was accepted;
        /// ContainsCameraMode remains true until a deferred request commits at evaluation end.
        /// </summary>
        public bool RemoveCameraMode(CameraMode cameraMode)
        {
            EnsureOwnerThread();
            if (cameraMode == null)
            {
                return false;
            }

            if (isEvaluating)
            {
                return TryQueueModeRemoval(cameraMode);
            }

            if (isClearing ||
                isChangingModes ||
                hasModeLifecycleFault) return false;

            for (int i = cameraModeCount - 1; i >= 0; i--)
            {
                if (!ReferenceEquals(cameraModes[i], cameraMode)) continue;

                isChangingModes = true;
                try
                {
                    if (!TryDeactivate(cameraMode))
                    {
                        hasModeLifecycleFault = true;
                        return false;
                    }

                    int moveCount = cameraModeCount - i - 1;
                    if (moveCount > 0)
                    {
                        Array.Copy(cameraModes, i + 1, cameraModes, i, moveCount);
                    }

                    cameraModeCount--;
                    cameraModes[cameraModeCount] = null;
                    return true;
                }
                finally
                {
                    CompleteModeChange();
                }
            }

            return false;
        }

        /// <summary>
        /// Deactivates stacked modes in reverse order, followed by the base mode.
        /// Modes whose cleanup throws remain retained and freeze evaluation so callers can
        /// explicitly retry Clear without losing the only cleanup handle.
        /// </summary>
        public bool Clear()
        {
            EnsureOwnerThread();
            if (isEvaluating)
            {
                clearRequested = true;
                return false;
            }

            if (isClearing || isChangingModes) return false;

            isClearing = true;
            isChangingModes = true;
            ClearPendingRemovalRequests();
            int count = cameraModeCount;
            CameraMode baseCameraMode = BaseCameraMode;
            bool cleanupFailed = false;

            try
            {
                CameraMode uncommittedMode = uncommittedLifecycleMode;
                if (uncommittedMode != null)
                {
                    if (TryDeactivate(uncommittedMode))
                    {
                        uncommittedLifecycleMode = null;
                    }
                    else
                    {
                        cleanupFailed = true;
                    }
                }

                for (int i = count - 1; i >= 0; i--)
                {
                    CameraMode cameraMode = cameraModes[i];
                    if (TryDeactivate(cameraMode))
                    {
                        cameraModes[i] = null;
                    }
                    else
                    {
                        cleanupFailed = true;
                    }
                }

                CompactRetainedModes(count);
                if (TryDeactivate(baseCameraMode))
                {
                    BaseCameraMode = null;
                }
                else
                {
                    BaseCameraMode = baseCameraMode;
                    cleanupFailed = true;
                }

                hasModeLifecycleFault = cleanupFailed;
            }
            catch (OutOfMemoryException)
            {
                // Successful cleanup completed before the OOM remains committed. Compact the
                // retained handles so fault-state diagnostics never expose null logical entries.
                CompactRetainedModes(count);
                hasModeLifecycleFault = true;
                throw;
            }
            finally
            {
                isChangingModes = false;
                isClearing = false;
                clearRequested = false;
            }

            return !hasModeLifecycleFault;
        }

        internal bool TryBeginEvaluation()
        {
            EnsureOwnerThread();
            if (!isClearing && !isChangingModes && !isEvaluating && pendingRemovalCount > 0)
            {
                ProcessPendingModeRemovals();
            }

            if (isClearing || isChangingModes || isEvaluating || hasModeLifecycleFault)
            {
                return false;
            }

            isEvaluating = true;
            return true;
        }

        internal void EndEvaluation()
        {
            EnsureOwnerThread();
            if (!isEvaluating)
            {
                return;
            }

            isEvaluating = false;
            if (clearRequested)
            {
                ClearPendingRemovalRequests();
                Clear();
                return;
            }

            ProcessPendingModeRemovals();
        }

        public CameraMode GetCameraModeAt(int index)
        {
            EnsureOwnerThread();
            if (index < 0 || index >= cameraModeCount)
            {
                throw new IndexOutOfRangeException($"Camera mode index out of range: {index}, count={cameraModeCount}");
            }

            return cameraModes[index];
        }

        public CameraMode GetPrimaryCameraMode()
        {
            EnsureOwnerThread();
            if (hasModeLifecycleFault)
            {
                return null;
            }

            return cameraModeCount > 0 ? cameraModes[cameraModeCount - 1] : BaseCameraMode;
        }

        public bool ContainsCameraMode(CameraMode cameraMode)
        {
            EnsureOwnerThread();
            if (cameraMode == null)
            {
                return false;
            }

            if (ReferenceEquals(uncommittedLifecycleMode, cameraMode))
            {
                return true;
            }

            if (ReferenceEquals(BaseCameraMode, cameraMode))
            {
                return true;
            }

            for (int i = 0; i < cameraModeCount; i++)
            {
                if (ReferenceEquals(cameraModes[i], cameraMode))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Transfers a stacked mode's deferred-cleanup responsibility into this context's
        /// fixed-capacity queue. The method never invokes a mode lifecycle callback.
        /// </summary>
        internal bool TryAdoptModeCleanup(CameraMode cameraMode)
        {
            EnsureOwnerThread();
            if (cameraMode == null)
            {
                return true;
            }

            if (!ContainsCameraMode(cameraMode))
            {
                return true;
            }

            return TryQueueModeRemoval(cameraMode);
        }

        private void RemoveModeAtWithoutDeactivation(int index)
        {
            int moveCount = cameraModeCount - index - 1;
            if (moveCount > 0)
            {
                Array.Copy(cameraModes, index + 1, cameraModes, index, moveCount);
            }

            cameraModeCount--;
            cameraModes[cameraModeCount] = null;
        }

        private void CompactRetainedModes(int count)
        {
            int survivorCount = 0;
            for (int i = 0; i < count; i++)
            {
                CameraMode survivor = cameraModes[i];
                if (survivor != null)
                {
                    cameraModes[survivorCount++] = survivor;
                }
            }

            for (int i = survivorCount; i < count; i++)
            {
                cameraModes[i] = null;
            }

            cameraModeCount = survivorCount;
        }

        private bool TryQueueModeRemoval(CameraMode cameraMode)
        {
            bool isStacked = false;
            for (int i = 0; i < cameraModeCount; i++)
            {
                if (ReferenceEquals(cameraModes[i], cameraMode))
                {
                    isStacked = true;
                    break;
                }
            }

            if (!isStacked)
            {
                return false;
            }

            for (int i = 0; i < pendingRemovalCount; i++)
            {
                if (ReferenceEquals(pendingRemovalModes[i], cameraMode))
                {
                    return true;
                }
            }

            if (pendingRemovalCount >= pendingRemovalModes.Length)
            {
                return false;
            }

            pendingRemovalModes[pendingRemovalCount++] = cameraMode;
            return true;
        }

        private void ProcessPendingModeRemovals()
        {
            if (isProcessingPendingRemovals ||
                isClearing ||
                isChangingModes ||
                isEvaluating ||
                pendingRemovalCount == 0)
            {
                return;
            }

            isProcessingPendingRemovals = true;
            try
            {
                while (pendingRemovalCount > 0)
                {
                    CameraMode cameraMode = pendingRemovalModes[0];
                    int moveCount = pendingRemovalCount - 1;
                    if (moveCount > 0)
                    {
                        Array.Copy(
                            pendingRemovalModes,
                            1,
                            pendingRemovalModes,
                            0,
                            moveCount);
                    }

                    pendingRemovalCount--;
                    pendingRemovalModes[pendingRemovalCount] = null;
                    RemoveCameraMode(cameraMode);
                    if (hasModeLifecycleFault)
                    {
                        break;
                    }
                }
            }
            finally
            {
                isProcessingPendingRemovals = false;
            }
        }

        private void CompleteModeChange()
        {
            isChangingModes = false;
            if (!isClearing &&
                !isEvaluating &&
                !isProcessingPendingRemovals &&
                pendingRemovalCount > 0)
            {
                ProcessPendingModeRemovals();
            }
        }

        private void ClearPendingRemovalRequests()
        {
            for (int i = 0; i < pendingRemovalCount; i++)
            {
                pendingRemovalModes[i] = null;
            }

            pendingRemovalCount = 0;
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
            catch (OutOfMemoryException)
            {
                hasModeLifecycleFault = true;
                throw;
            }
            catch (Exception exception) when (!(exception is OutOfMemoryException))
            {
                OutOfMemoryException outOfMemory = FindOutOfMemory(exception);
                if (outOfMemory != null)
                {
                    hasModeLifecycleFault = true;
                    throw outOfMemory;
                }

                try
                {
                    LogLifecycleFailure(
                        exception,
                        cameraMode,
                        "activation");
                }
                catch (OutOfMemoryException)
                {
                    hasModeLifecycleFault = true;
                    throw;
                }

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
            catch (OutOfMemoryException)
            {
                hasModeLifecycleFault = true;
                throw;
            }
            catch (Exception exception) when (!(exception is OutOfMemoryException))
            {
                hasModeLifecycleFault = true;
                OutOfMemoryException outOfMemory = FindOutOfMemory(exception);
                if (outOfMemory != null)
                {
                    throw outOfMemory;
                }

                LogLifecycleFailure(
                    exception,
                    cameraMode,
                    "deactivation");
                return false;
            }
        }

        private void LogLifecycleFailure(
            Exception exception,
            CameraMode cameraMode,
            string operation)
        {
            try
            {
                Log.Error(
                    exception,
                    $"CameraMode '{cameraMode.GetType().Name}' {operation} failed.");
            }
            catch (Exception loggingException)
            {
                OutOfMemoryException outOfMemory = FindOutOfMemory(loggingException);
                if (outOfMemory == null)
                {
                    return;
                }

                hasModeLifecycleFault = true;
                throw outOfMemory;
            }
        }

        private static OutOfMemoryException FindOutOfMemory(Exception exception)
        {
            if (exception is OutOfMemoryException outOfMemoryException)
            {
                return outOfMemoryException;
            }

            if (exception is AggregateException aggregateException)
            {
                for (int index = 0; index < aggregateException.InnerExceptions.Count; index++)
                {
                    OutOfMemoryException nested = FindOutOfMemory(
                        aggregateException.InnerExceptions[index]);
                    if (nested != null)
                    {
                        return nested;
                    }
                }
            }

            return null;
        }

        private void EnsureOwnerThread()
        {
            if (Thread.CurrentThread.ManagedThreadId != ownerThreadId)
            {
                throw new InvalidOperationException(
                    "CameraContext live state must be accessed on its composition owner thread.");
            }
        }
    }
}
