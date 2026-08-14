using CycloneGames.Logging;
using UnityEngine;

namespace CycloneGames.GameplayFramework.Runtime
{
    public class CameraManager : Actor
    {
        private static readonly LogChannel Log = GameplayFrameworkLog.Channel;

        [SerializeField] protected float DefaultFOV = 60.0f;
        [SerializeField] private float defaultBlendDuration = 0.15f;
        [SerializeField] private CameraOutputBehaviour cameraOutput;

        public ICameraOutput ActiveOutput { get; private set; }
        public ICameraOutput ConfiguredOutput => IsOutputAlive(configuredOutput)
            ? configuredOutput
            : cameraOutput != null
                ? cameraOutput
                : null;
        public UnityEngine.Object ActiveOutputObject => IsOutputAlive(ActiveOutput)
            ? ActiveOutput.OutputObject
            : null;
        public float DefaultBlendDuration => defaultBlendDuration;
        public bool HasExplicitFovOverride => hasExplicitFovOverride;
        public bool CameraStateDirty => cameraStateDirty;
        public bool HasCurrentPose => hasCurrentPose;
        public CameraPose CurrentPose => currentPose;
        public bool HasPendingBlendDurationOverride => hasPendingBlendDurationOverride;
        public float PendingBlendDurationOverride => pendingBlendDurationOverride;
        public Transform PendingViewTargetTransform => PendingViewTargetTF;
        public Actor LastViewTarget => lastViewTarget;
        public CameraMode LastPrimaryMode => lastPrimaryMode;
        public CameraBlendState BlendState => blendState;

        private PlayerController PCOwner;
        public PlayerController OwnerController => PCOwner;
        public bool IsInitialized { get; private set; }
        private float lockedFOV;
        public float GetLockedFOV() => lockedFOV;
        private bool hasExplicitFovOverride;
        private Transform PendingViewTargetTF;
        private Actor lastViewTarget;
        private CameraMode lastPrimaryMode;
        private CameraPose currentPose;
        private bool hasCurrentPose;
        private bool cameraStateDirty;
        private CameraBlendState blendState;
        private bool hasPendingBlendDurationOverride;
        private float pendingBlendDurationOverride;
        private bool isUpdatingCamera;

        private ICameraOutput configuredOutput;
        private CameraOutputLease activeOutputLease;
        private bool isTransitioningOutput;

        // Fixed-capacity array keeps registration allocation-free after construction.
        private const int MAX_POST_PROCESSORS = 16;
        private readonly ICameraPostProcessor[] postProcessors = new ICameraPostProcessor[MAX_POST_PROCESSORS];
        private int postProcessorCount;

        protected override void Awake()
        {
            base.Awake();
            EnsureActorTickConfiguration();
        }

        /// <summary>
        /// Selects a camera output supplied by authoring, manual composition, or DI. The output
        /// is optional; a CameraManager without one still evaluates and exposes CameraPose state.
        /// </summary>
        public virtual void SetCameraOutput(ICameraOutput output, bool rebindImmediately = true)
        {
            ThrowIfOutputTransitioning();
            if (ReferenceEquals(ConfiguredOutput, output) &&
                (!rebindImmediately || ReferenceEquals(ActiveOutput, output)))
            {
                return;
            }

            World?.AssertOwnerThread();
            ReleaseActiveOutput();
            configuredOutput = output;
            cameraOutput = output as CameraOutputBehaviour;

            if (rebindImmediately && IsInitialized && IsOutputAlive(output))
            {
                TryBindOutput(output);
            }
        }

        /// <summary>
        /// Resolves the configured authoring output and binds it immediately. Returns false when
        /// no output is configured or the backend cannot acquire its resource.
        /// </summary>
        public virtual bool TryResolveAndBindOutput()
        {
            ICameraOutput output = ConfiguredOutput;
            if (!IsOutputAlive(output))
            {
                CameraOutputBehaviour localOutput = GetComponent<CameraOutputBehaviour>();
                if (localOutput == null)
                {
                    localOutput = GetComponentInChildren<CameraOutputBehaviour>(includeInactive: true);
                }

                output = localOutput;
                if (localOutput != null)
                {
                    cameraOutput = localOutput;
                }
            }

            return IsOutputAlive(output) && TryBindOutput(output);
        }

        public virtual void SetFOV(float NewFOV)
        {
            if (float.IsNaN(NewFOV) || float.IsInfinity(NewFOV) || NewFOV <= 0f || NewFOV >= 180f)
            {
                throw new System.ArgumentOutOfRangeException(nameof(NewFOV));
            }

            lockedFOV = NewFOV;
            hasExplicitFovOverride = true;
            NotifyCameraStateChanged();
        }

        public virtual void ClearFOVOverride()
        {
            hasExplicitFovOverride = false;
            lockedFOV = DefaultFOV;
            NotifyCameraStateChanged();
        }

        /// <summary>
        /// Set the default FOV used when no explicit FOV override is active.
        /// Typically called by <see cref="CameraProfile.ApplyTo"/>.
        /// </summary>
        public virtual void SetDefaultFOV(float fov)
        {
            if (float.IsNaN(fov) || float.IsInfinity(fov) || fov <= 0f || fov >= 180f)
            {
                throw new System.ArgumentOutOfRangeException(nameof(fov));
            }

            DefaultFOV = fov;
            if (!hasExplicitFovOverride)
            {
                lockedFOV = DefaultFOV;
                NotifyCameraStateChanged();
            }
        }

        /// <summary>
        /// Set the fallback blend duration used when the active CameraMode does not specify one.
        /// Typically called by <see cref="CameraProfile.ApplyTo"/>.
        /// </summary>
        public virtual void SetDefaultBlendDuration(float duration)
        {
            if (float.IsNaN(duration) || float.IsInfinity(duration) || duration < 0f)
            {
                throw new System.ArgumentOutOfRangeException(nameof(duration));
            }

            defaultBlendDuration = duration;
        }

        /// <summary>
        /// Sets a one-shot blend duration override that is consumed on the next camera state transition.
        /// </summary>
        public virtual void SetNextBlendDuration(float duration)
        {
            if (float.IsNaN(duration) || float.IsInfinity(duration) || duration < 0f)
            {
                throw new System.ArgumentOutOfRangeException(nameof(duration));
            }

            pendingBlendDurationOverride = Mathf.Max(0f, duration);
            hasPendingBlendDurationOverride = true;
        }

        public virtual void SetViewTarget(Transform NewTargetTF)
        {
            PendingViewTargetTF = NewTargetTF;
            NotifyCameraStateChanged();
        }

        public virtual void NotifyCameraStateChanged()
        {
            cameraStateDirty = true;
        }

        public virtual void InitializeFor(PlayerController PlayerController)
        {
            if (PlayerController == null)
            {
                throw new System.ArgumentNullException(nameof(PlayerController));
            }

            if (!PlayerController.IsLocalController)
            {
                throw new System.InvalidOperationException("CameraManager requires a local PlayerController.");
            }

            if (!ReferenceEquals(PlayerController.World, World))
            {
                throw new System.InvalidOperationException("CameraManager and PlayerController must belong to the same World.");
            }

            if (IsInitialized)
            {
                throw new System.InvalidOperationException("CameraManager is already initialized.");
            }

            World expectedWorld = World;
            PCOwner = PlayerController;
            lockedFOV = DefaultFOV;
            hasExplicitFovOverride = false;

            if (!IsOutputAlive(ActiveOutput))
            {
                ICameraOutput output = ConfiguredOutput;
                if (!IsOutputAlive(output))
                {
                    CameraOutputBehaviour localOutput = GetComponent<CameraOutputBehaviour>();
                    if (localOutput == null)
                    {
                        localOutput = GetComponentInChildren<CameraOutputBehaviour>(includeInactive: true);
                    }

                    output = localOutput;
                    if (localOutput != null)
                    {
                        cameraOutput = localOutput;
                    }
                }

                if (IsOutputAlive(output))
                {
                    TryBindOutput(output);
                }
            }

            if (!ReferenceEquals(World, expectedWorld) ||
                expectedWorld.LifecycleState == WorldLifecycleState.Stopping ||
                expectedWorld.LifecycleState == WorldLifecycleState.Stopped ||
                expectedWorld.LifecycleState == WorldLifecycleState.Disposed)
            {
                ResetRuntimeState();
                throw new System.InvalidOperationException(
                    "CameraManager initialization was interrupted by World teardown.");
            }

            var currentViewTarget = PlayerController != null ? PlayerController.GetViewTarget() : null;
            PendingViewTargetTF = currentViewTarget != null ? currentViewTarget.transform : PlayerController?.transform;
            NotifyCameraStateChanged();
            IsInitialized = true;
            EnsureActorTickConfiguration();
            SetActorTickEnabled(true);
        }

        private bool TryBindOutput(ICameraOutput output)
        {
            World expectedWorld = World;
            if (expectedWorld == null)
            {
                return false;
            }

            expectedWorld.AssertOwnerThread();
            ThrowIfOutputTransitioning();
            isTransitioningOutput = true;
            try
            {
                if (ReferenceEquals(ActiveOutput, output))
                {
                    CameraOutputLease existingLease = activeOutputLease;
                    try
                    {
                        if (IsOutputAlive(output) &&
                            output.TryPrepare(out _) &&
                            ValidatePreparedResourcesAgainstLease(
                                output,
                                in activeOutputLease,
                                out _) &&
                            IsTransitionWorldValid(expectedWorld))
                        {
                            return true;
                        }
                    }
                    catch
                    {
                        ReleaseRepreparedActiveOutput(output, expectedWorld, in existingLease);
                        throw;
                    }

                    ReleaseRepreparedActiveOutput(output, expectedWorld, in existingLease);
                }

                if (!IsOutputAlive(output) || !IsTransitionWorldValid(expectedWorld))
                {
                    return false;
                }

                bool prepared;
                string error;
                try
                {
                    prepared = output.TryPrepare(out error);
                }
                catch
                {
                    ReleasePreparedOutput(output);
                    throw;
                }

                if (!prepared ||
                    !ValidatePreparedResources(output, out error))
                {
                    Log.Error(
                        error,
                        static (message, builder) =>
                        {
                            builder.Append("Camera output preparation failed: ");
                            builder.Append(message);
                        });
                    ReleasePreparedOutput(output);
                    return false;
                }

                if (!IsTransitionWorldValid(expectedWorld))
                {
                    ReleasePreparedOutput(output);
                    return false;
                }

                ReleaseActiveOutputCore();
                if (!IsTransitionWorldValid(expectedWorld))
                {
                    ReleasePreparedOutput(output);
                    return false;
                }

                if (!expectedWorld.TryAcquireCameraOutput(
                        this,
                        output,
                        out CameraOutputLease newLease,
                        out error))
                {
                    Log.Error(
                        error,
                        static (message, builder) =>
                        {
                            builder.Append("Camera output ownership acquisition failed: ");
                            builder.Append(message);
                        });
                    ReleasePreparedOutput(output);
                    return false;
                }

                try
                {
                    if (!output.TryActivate(this, out error))
                    {
                        Log.Error(
                            error,
                            static (message, builder) =>
                            {
                                builder.Append("Camera output activation failed: ");
                                builder.Append(message);
                            });
                        ReleaseFailedActivation(output, expectedWorld, newLease);
                        return false;
                    }

                    if (!IsOutputAlive(output) ||
                        !output.TryPrepare(out error) ||
                        !ValidatePreparedResourcesAgainstLease(
                            output,
                            in newLease,
                            out error) ||
                        !IsTransitionWorldValid(expectedWorld))
                    {
                        ReleaseFailedActivation(output, expectedWorld, newLease);
                        Log.Error(
                            error ?? "Camera output resources were destroyed during activation.");
                        return false;
                    }
                }
                catch
                {
                    ReleaseFailedActivation(output, expectedWorld, newLease);
                    throw;
                }

                ActiveOutput = output;
                activeOutputLease = newLease;
                return true;
            }
            finally
            {
                isTransitioningOutput = false;
            }
        }

        private void ReleaseActiveOutput()
        {
            ThrowIfOutputTransitioning();
            isTransitioningOutput = true;
            try
            {
                ReleaseActiveOutputCore();
            }
            finally
            {
                isTransitioningOutput = false;
            }
        }

        private void ReleaseActiveOutputCore()
        {
            ICameraOutput output = ActiveOutput;
            CameraOutputLease lease = activeOutputLease;
            World owningWorld = World;
            ActiveOutput = null;
            activeOutputLease = default;
            if (output == null)
            {
                return;
            }

            try
            {
                ReleasePreparedOutput(output);
            }
            finally
            {
                owningWorld?.ReleaseCameraOutput(this, output, lease);
            }
        }

        private void ReleaseFailedActivation(
            ICameraOutput output,
            World owningWorld,
            CameraOutputLease lease)
        {
            try
            {
                ReleasePreparedOutput(output);
            }
            finally
            {
                owningWorld?.ReleaseCameraOutput(this, output, lease);
            }
        }

        private void ReleaseRepreparedActiveOutput(
            ICameraOutput output,
            World owningWorld,
            in CameraOutputLease lease)
        {
            if (ReferenceEquals(ActiveOutput, output))
            {
                ReleaseActiveOutputCore();
                return;
            }

            try
            {
                ReleasePreparedOutput(output);
            }
            finally
            {
                owningWorld?.ReleaseCameraOutput(this, output, lease);
            }
        }

        private void ReleasePreparedOutput(ICameraOutput output)
        {
            try
            {
                output?.Deactivate(this);
            }
            catch (System.Exception exception)
            {
                Log.Error(exception, $"Camera output '{GetOutputDisplayName(output)}' failed to release prepared state.");
            }
        }

        public virtual void UpdateCamera(float deltaTime)
        {
            if (!IsInitialized || isUpdatingCamera) return;

            isUpdatingCamera = true;
            try
            {
                CameraPose desiredPose = EvaluateDesiredPose(deltaTime);
                CameraContext context = PCOwner != null ? PCOwner.GetCameraContext() : null;
                Actor currentViewTarget = context != null ? context.CurrentViewTarget : null;
                CameraMode primaryMode = context != null ? context.GetPrimaryCameraMode() : null;

                if (!hasCurrentPose)
                {
                    ApplyCameraPose(desiredPose);
                    lastViewTarget = currentViewTarget;
                    lastPrimaryMode = primaryMode;
                    cameraStateDirty = false;
                    return;
                }

                if (cameraStateDirty || !ReferenceEquals(lastViewTarget, currentViewTarget) || !ReferenceEquals(lastPrimaryMode, primaryMode))
                {
                    float blendDuration;
                    if (hasPendingBlendDurationOverride)
                    {
                        blendDuration = pendingBlendDurationOverride;
                        hasPendingBlendDurationOverride = false;
                    }
                    else
                    {
                        blendDuration = primaryMode != null ? primaryMode.BlendDuration : defaultBlendDuration;
                    }

                    blendState.Start(currentPose, blendDuration);
                    lastViewTarget = currentViewTarget;
                    lastPrimaryMode = primaryMode;
                    cameraStateDirty = false;
                }

                CameraPose outputPose = blendState.Evaluate(desiredPose, deltaTime);
                ApplyCameraPose(outputPose);
            }
            finally
            {
                isUpdatingCamera = false;
            }
        }

        protected virtual CameraPose EvaluateDesiredPose(float deltaTime)
        {
            float fallbackFov = hasExplicitFovOverride ? lockedFOV : DefaultFOV;
            CameraContext context = PCOwner != null ? PCOwner.GetCameraContext() : null;
            bool ownsEvaluationScope = context != null && context.TryBeginEvaluation();

            try
            {
                CameraPose desiredPose;
                if (context != null && context.CurrentViewTarget != null)
                {
                    context.CurrentViewTarget.CalcCamera(deltaTime, out desiredPose, fallbackFov);
                }
                else if (PendingViewTargetTF != null)
                {
                    desiredPose = CameraPoseUtility.GetCameraPose(PendingViewTargetTF, fallbackFov);
                }
                else
                {
                    desiredPose = new CameraPose(transform.position, transform.rotation, fallbackFov);
                }

                if (context != null && ownsEvaluationScope)
                {
                    CameraMode baseMode = context.BaseCameraMode;
                    if (baseMode != null)
                    {
                        baseMode.Tick(context, deltaTime);
                        desiredPose = baseMode.Evaluate(context, desiredPose, deltaTime);
                    }

                    int modeCount = context.CameraModeCount;
                    for (int i = 0; i < modeCount; i++)
                    {
                        CameraMode mode = context.GetCameraModeAt(i);
                        if (mode == null) continue;

                        mode.Tick(context, deltaTime);
                        desiredPose = mode.Evaluate(context, desiredPose, deltaTime);
                    }
                }

                // Post-processors run after all CameraModes (e.g. collision avoidance, screen shake)
                for (int i = 0; i < postProcessorCount; i++)
                {
                    ICameraPostProcessor proc = postProcessors[i];
                    if (proc != null)
                        desiredPose = proc.Process(desiredPose, context, deltaTime);
                }

                if (hasExplicitFovOverride)
                {
                    desiredPose.Fov = lockedFOV;
                }
                else
                {
                    lockedFOV = desiredPose.Fov;
                }

                return desiredPose;
            }
            finally
            {
                if (ownsEvaluationScope)
                {
                    context.EndEvaluation();
                }
            }
        }

        protected virtual void ApplyCameraPose(CameraPose pose)
        {
            currentPose = pose;
            hasCurrentPose = true;

            transform.SetPositionAndRotation(pose.Position, pose.Rotation);
            ICameraOutput output = ActiveOutput;
            if (output == null)
            {
                return;
            }

            try
            {
                output.ApplyPose(in pose);
            }
            catch (System.Exception exception)
            {
                Log.Error(
                    exception,
                    $"Camera output '{GetOutputDisplayName(output)}' failed while applying a pose and was released.");
                ReleaseActiveOutput();
            }
        }

        /// <summary>Add a post-processor to the evaluation chain. No-op if already registered.</summary>
        public void RegisterPostProcessor(ICameraPostProcessor processor)
        {
            if (processor == null || isUpdatingCamera) return;

            for (int i = 0; i < postProcessorCount; i++)
            {
                if (ReferenceEquals(postProcessors[i], processor))
                {
                    return;
                }
            }

            if (postProcessorCount >= MAX_POST_PROCESSORS)
            {
                Log.Warning(
                    MAX_POST_PROCESSORS,
                    static (capacity, builder) =>
                    {
                        builder.Append("Camera post-processor registry reached capacity ");
                        builder.Append(capacity);
                        builder.Append('.');
                    });
                return;
            }

            postProcessors[postProcessorCount++] = processor;
        }

        /// <summary>Remove a previously registered post-processor.</summary>
        public void UnregisterPostProcessor(ICameraPostProcessor processor)
        {
            if (processor == null || isUpdatingCamera) return;

            for (int i = 0; i < postProcessorCount; i++)
            {
                if (!ReferenceEquals(postProcessors[i], processor)) continue;

                int moveCount = postProcessorCount - i - 1;
                if (moveCount > 0)
                {
                    System.Array.Copy(postProcessors, i + 1, postProcessors, i, moveCount);
                }

                postProcessorCount--;
                postProcessors[postProcessorCount] = null;
                return;
            }
        }

        protected override void Tick(float deltaSeconds)
        {
            UpdateCamera(deltaSeconds);
        }

        protected override void OnWorldUnbound(EndPlayReason reason)
        {
            try
            {
                if (!isTransitioningOutput)
                {
                    ReleaseActiveOutput();
                }
            }
            finally
            {
                ResetRuntimeState();
                base.OnWorldUnbound(reason);
            }
        }

        protected override void OnDestroy()
        {
            try
            {
                if (!isTransitioningOutput)
                {
                    ReleaseActiveOutput();
                }
            }
            finally
            {
                ResetRuntimeState();
                base.OnDestroy();
            }
        }

        internal void HandleCameraOutputDestroyed(ICameraOutput output)
        {
            if (isTransitioningOutput)
            {
                return;
            }

            if (!ReferenceEquals(ActiveOutput, output))
            {
                ReleasePreparedOutput(output);
                return;
            }

            ReleaseActiveOutput();
        }

        private void ThrowIfOutputTransitioning()
        {
            if (isTransitioningOutput)
            {
                throw new System.InvalidOperationException(
                    "Camera output composition cannot change during an output transition callback.");
            }
        }

        private static bool IsOutputAlive(ICameraOutput output)
        {
            return output != null &&
                   (!(output is UnityEngine.Object unityObject) || unityObject != null);
        }

        private static bool ValidatePreparedResources(ICameraOutput output, out string error)
        {
            int resourceCount;
            try
            {
                resourceCount = output.PreparedResourceCount;
            }
            catch (System.Exception exception)
            {
                error = $"Camera output resource count could not be read: {exception.Message}";
                return false;
            }

            if (resourceCount <= 0 || resourceCount > CameraOutputLimits.MaximumPreparedResourceCount)
            {
                error = $"Camera output must prepare between 1 and {CameraOutputLimits.MaximumPreparedResourceCount} ownership resources.";
                return false;
            }

            for (int i = 0; i < resourceCount; i++)
            {
                try
                {
                    UnityEngine.Object resource = output.GetPreparedResource(i);
                    if (resource == null)
                    {
                        error = $"Camera output resource {i} is missing or destroyed.";
                        return false;
                    }

                    int resourceId = resource.GetInstanceID();
                    for (int j = 0; j < i; j++)
                    {
                        UnityEngine.Object previous = output.GetPreparedResource(j);
                        if (previous != null && previous.GetInstanceID() == resourceId)
                        {
                            error = $"Camera output resource {i} duplicates resource {j}.";
                            return false;
                        }
                    }
                }
                catch (System.Exception exception)
                {
                    error = $"Camera output resource {i} could not be read: {exception.Message}";
                    return false;
                }
            }

            error = null;
            return true;
        }

        private static bool ValidatePreparedResourcesAgainstLease(
            ICameraOutput output,
            in CameraOutputLease lease,
            out string error)
        {
            if (!lease.IsValid)
            {
                error = "Camera output has no active ownership lease.";
                return false;
            }

            int resourceCount;
            try
            {
                resourceCount = output.PreparedResourceCount;
            }
            catch (System.Exception exception)
            {
                error = $"Camera output resource count could not be read: {exception.Message}";
                return false;
            }

            if (resourceCount != lease.ResourceCount)
            {
                error = "Camera output changed its ownership-resource count after lease acquisition.";
                return false;
            }

            for (int index = 0; index < resourceCount; index++)
            {
                try
                {
                    UnityEngine.Object resource = output.GetPreparedResource(index);
                    if (resource == null || resource.GetInstanceID() != lease.GetResourceId(index))
                    {
                        error =
                            $"Camera output changed ownership resource {index} after lease acquisition.";
                        return false;
                    }
                }
                catch (System.Exception exception)
                {
                    error = $"Camera output resource {index} could not be read: {exception.Message}";
                    return false;
                }
            }

            error = null;
            return true;
        }

        private bool IsTransitionWorldValid(World expectedWorld)
        {
            return ReferenceEquals(World, expectedWorld) &&
                   (expectedWorld.LifecycleState == WorldLifecycleState.Initializing ||
                    expectedWorld.LifecycleState == WorldLifecycleState.Playing);
        }

        private static string GetOutputDisplayName(ICameraOutput output)
        {
            if (output == null)
            {
                return "Unknown";
            }

            try
            {
                return output.DisplayName ?? "Unknown";
            }
            catch
            {
                return "Destroyed output";
            }
        }

        private void ResetRuntimeState()
        {
            SetActorTickEnabled(false);
            for (int i = 0; i < postProcessorCount; i++)
            {
                postProcessors[i] = null;
            }
            postProcessorCount = 0;
            lastViewTarget = null;
            lastPrimaryMode = null;
            PendingViewTargetTF = null;
            currentPose = default;
            hasCurrentPose = false;
            cameraStateDirty = false;
            hasExplicitFovOverride = false;
            hasPendingBlendDurationOverride = false;
            pendingBlendDurationOverride = 0f;
            PCOwner = null;
            IsInitialized = false;
            lockedFOV = DefaultFOV;
            blendState = default;
            isUpdatingCamera = false;
            isTransitioningOutput = false;
            ActiveOutput = null;
            activeOutputLease = default;
        }

        private void EnsureActorTickConfiguration()
        {
            if (TickPhase != ActorTickPhase.LateUpdate || IsTickEnabledAtStart)
            {
                ConfigureActorTick(ActorTickPhase.LateUpdate, startWithTickEnabled: false);
            }
        }

    }
}
