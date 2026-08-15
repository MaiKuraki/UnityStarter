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

        public ICameraOutput ActiveOutput
        {
            get
            {
                AssertActorOwnerThread();
                return activeOutput;
            }
            private set => activeOutput = value;
        }

        public ICameraOutput ConfiguredOutput
        {
            get
            {
                AssertActorOwnerThread();
                return IsOutputAlive(configuredOutput)
                    ? configuredOutput
                    : cameraOutput != null
                        ? cameraOutput
                        : null;
            }
        }

        public UnityEngine.Object ActiveOutputObject
        {
            get
            {
                AssertActorOwnerThread();
                ICameraOutput output = activeOutput;
                return IsOutputAlive(output) ? output.OutputObject : null;
            }
        }

        public float DefaultBlendDuration
        {
            get
            {
                AssertActorOwnerThread();
                return defaultBlendDuration;
            }
        }

        public bool HasExplicitFovOverride
        {
            get
            {
                AssertActorOwnerThread();
                return hasExplicitFovOverride;
            }
        }

        public bool CameraStateDirty
        {
            get
            {
                AssertActorOwnerThread();
                return cameraStateDirty;
            }
        }

        public bool HasCurrentPose
        {
            get
            {
                AssertActorOwnerThread();
                return hasCurrentPose;
            }
        }

        public CameraPose CurrentPose
        {
            get
            {
                AssertActorOwnerThread();
                return currentPose;
            }
        }

        public bool HasPendingBlendDurationOverride
        {
            get
            {
                AssertActorOwnerThread();
                return hasPendingBlendDurationOverride;
            }
        }

        public float PendingBlendDurationOverride
        {
            get
            {
                AssertActorOwnerThread();
                return pendingBlendDurationOverride;
            }
        }
        /// <summary>
        /// True when a lease-arbiter exception left camera-output ownership untrusted. Output
        /// binding remains fail-closed until this manager is unbound from its World and reset.
        /// </summary>
        public bool HasOutputLeaseFault
        {
            get
            {
                AssertActorOwnerThread();
                return hasOutputLeaseFault;
            }
        }

        public Transform PendingViewTargetTransform
        {
            get
            {
                AssertActorOwnerThread();
                return PendingViewTargetTF;
            }
        }

        public Actor LastViewTarget
        {
            get
            {
                AssertActorOwnerThread();
                return lastViewTarget;
            }
        }

        public CameraMode LastPrimaryMode
        {
            get
            {
                AssertActorOwnerThread();
                return lastPrimaryMode;
            }
        }

        public CameraBlendState BlendState
        {
            get
            {
                AssertActorOwnerThread();
                return blendState;
            }
        }

        private PlayerController PCOwner;
        public PlayerController OwnerController
        {
            get
            {
                AssertActorOwnerThread();
                return PCOwner;
            }
        }

        public bool IsInitialized
        {
            get
            {
                AssertActorOwnerThread();
                return isInitialized;
            }
            private set => isInitialized = value;
        }
        private float lockedFOV;
        public float GetLockedFOV()
        {
            AssertActorOwnerThread();
            return lockedFOV;
        }
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
        private bool invalidPoseEncounteredThisEvaluation;
        private bool invalidPoseReported;

        private ICameraOutput configuredOutput;
        private ICameraOutput activeOutput;
        private CameraOutputLease activeOutputLease;
        private CameraOutputResourceSet activeOutputResources;
        private bool isTransitioningOutput;
        private bool hasOutputLeaseFault;
        private bool isInitialized;

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
            AssertActorOwnerThread();
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
            AssertActorOwnerThread();
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
            AssertActorOwnerThread();
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
            AssertActorOwnerThread();
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
            AssertActorOwnerThread();
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
            AssertActorOwnerThread();
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
            AssertActorOwnerThread();
            if (float.IsNaN(duration) || float.IsInfinity(duration) || duration < 0f)
            {
                throw new System.ArgumentOutOfRangeException(nameof(duration));
            }

            pendingBlendDurationOverride = Mathf.Max(0f, duration);
            hasPendingBlendDurationOverride = true;
        }

        public virtual void SetViewTarget(Transform NewTargetTF)
        {
            AssertActorOwnerThread();
            PendingViewTargetTF = NewTargetTF;
            NotifyCameraStateChanged();
        }

        public virtual void NotifyCameraStateChanged()
        {
            AssertActorOwnerThread();
            cameraStateDirty = true;
        }

        public virtual void InitializeFor(PlayerController PlayerController)
        {
            AssertActorOwnerThread();
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
            if (expectedWorld == null || hasOutputLeaseFault)
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
                    if (IsOutputAlive(output) &&
                        ValidateResourceSetAgainstLease(
                            in activeOutputResources,
                            in activeOutputLease,
                            out _) &&
                        IsTransitionWorldValid(expectedWorld))
                    {
                        return true;
                    }

                    if (!ReleaseActiveOutputCore())
                    {
                        return false;
                    }
                }

                if (!IsOutputAlive(output) || !IsTransitionWorldValid(expectedWorld))
                {
                    return false;
                }

                CameraOutputResourceSet resources;
                string error;
                bool discovered = output.TryGetResourceSet(out resources, out error);

                if (!discovered || !resources.TryValidate(out error))
                {
                    Log.Error(
                        error,
                        static (message, builder) =>
                        {
                            builder.Append("Camera output resource discovery failed: ");
                            builder.Append(message);
                        });
                    return false;
                }

                if (!IsTransitionWorldValid(expectedWorld))
                {
                    return false;
                }

                if (!ReleaseActiveOutputCore())
                {
                    return false;
                }

                if (!IsTransitionWorldValid(expectedWorld))
                {
                    return false;
                }

                CameraOutputLease newLease = default;
                bool acquired;
                try
                {
                    acquired = expectedWorld.TryAcquireCameraOutput(
                        this,
                        output,
                        in resources,
                        out newLease,
                        out error);
                }
                catch (System.Exception exception) when (!(exception is System.OutOfMemoryException))
                {
                    hasOutputLeaseFault = true;
                    TryReleaseCameraOutputLease(expectedWorld, output, in newLease);
                    Log.Error(
                        exception,
                        $"Camera output '{GetOutputDisplayName(output)}' lease acquisition failed with an exception.");
                    throw;
                }

                if (!acquired)
                {
                    if (newLease.IsValid)
                    {
                        hasOutputLeaseFault = true;
                        TryReleaseCameraOutputLease(expectedWorld, output, in newLease);
                    }

                    Log.Error(
                        error,
                        static (message, builder) =>
                        {
                            builder.Append("Camera output ownership acquisition failed: ");
                            builder.Append(message);
                        });
                    return false;
                }

                bool activated;
                try
                {
                    activated = output.TryActivate(this, in resources, out error);
                }
                catch
                {
                    ReleaseFailedActivation(
                        output,
                        expectedWorld,
                        newLease,
                        in resources);
                    throw;
                }

                if (!activated)
                {
                    ReleaseFailedActivation(
                        output,
                        expectedWorld,
                        newLease,
                        in resources);
                    Log.Error(
                        error,
                        static (message, builder) =>
                        {
                            builder.Append("Camera output activation failed: ");
                            builder.Append(message);
                        });
                    return false;
                }

                bool activationRemainsValid;
                try
                {
                    activationRemainsValid = IsOutputAlive(output) &&
                                             resources.TryValidate(out error) &&
                                             ValidateResourceSetAgainstLease(
                                                 in resources,
                                                 in newLease,
                                                 out error) &&
                                             IsTransitionWorldValid(expectedWorld);
                }
                catch
                {
                    ReleaseFailedActivation(
                        output,
                        expectedWorld,
                        newLease,
                        in resources);
                    throw;
                }

                if (!activationRemainsValid)
                {
                    ReleaseFailedActivation(
                        output,
                        expectedWorld,
                        newLease,
                        in resources);
                    Log.Error(
                        error ?? "Camera output resources were destroyed during activation.");
                    return false;
                }

                ActiveOutput = output;
                activeOutputLease = newLease;
                activeOutputResources = resources;
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

        private bool ReleaseActiveOutputCore()
        {
            ICameraOutput output = ActiveOutput;
            CameraOutputLease lease = activeOutputLease;
            CameraOutputResourceSet resources = activeOutputResources;
            World owningWorld = World;
            ActiveOutput = null;
            if (output == null)
            {
                if (!lease.IsValid)
                {
                    activeOutputResources = default;
                }
                return !lease.IsValid;
            }

            if (lease.IsValid &&
                owningWorld != null &&
                owningWorld.LifecycleState == WorldLifecycleState.Stopping &&
                !owningWorld.TryBeginCameraOutputTerminalReleaseAttempt(
                    this,
                    output,
                    in lease))
            {
                // Another cleanup path already consumed this lease's callback slot for the
                // current terminal pass, or ownership no longer matches. Keep the local token
                // quarantined and let the arbiter decide whether a later pass may retry it.
                hasOutputLeaseFault = true;
                activeOutputLease = lease;
                activeOutputResources = resources;
                return false;
            }

            if (!TryDeactivateOutput(output))
            {
                // The backend may still own or mutate its resources. Keep the arbiter token
                // until World terminal cleanup so no second CameraManager can claim them.
                activeOutputLease = lease;
                activeOutputResources = resources;
                return false;
            }

            if (!TryReleaseCameraOutputLease(owningWorld, output, in lease))
            {
                activeOutputLease = lease;
                activeOutputResources = resources;
                return false;
            }

            activeOutputLease = default;
            activeOutputResources = default;
            return true;
        }

        private void ReleaseFailedActivation(
            ICameraOutput output,
            World owningWorld,
            CameraOutputLease lease,
            in CameraOutputResourceSet resources)
        {
            // Quarantine ownership before invoking any backend cleanup. Catastrophic failures
            // may escape, but this manager must remain unable to bind a second resource domain.
            ActiveOutput = null;
            activeOutputLease = lease;
            activeOutputResources = resources;
            if (!TryDeactivateOutput(output))
            {
                return;
            }

            if (!TryReleaseCameraOutputLease(owningWorld, output, in lease))
            {
                activeOutputLease = lease;
                activeOutputResources = resources;
                return;
            }

            activeOutputLease = default;
            activeOutputResources = default;
        }

        private bool TryReleaseCameraOutputLease(
            World owningWorld,
            ICameraOutput output,
            in CameraOutputLease lease)
        {
            if (!lease.IsValid)
            {
                return true;
            }

            if (owningWorld == null)
            {
                hasOutputLeaseFault = true;
                Log.Error("Camera output lease could not be released because its World is unavailable.");
                return false;
            }

            try
            {
                owningWorld.ReleaseCameraOutput(this, output, lease);
                return true;
            }
            catch (System.OutOfMemoryException)
            {
                hasOutputLeaseFault = true;
                throw;
            }
            catch (System.Exception exception) when (!(exception is System.OutOfMemoryException))
            {
                hasOutputLeaseFault = true;
                Log.Error(
                    exception,
                    $"Camera output '{GetOutputDisplayName(output)}' lease release failed.");
                return false;
            }
        }

        private bool TryDeactivateOutput(ICameraOutput output)
        {
            try
            {
                output?.Deactivate(this);
                return true;
            }
            catch (System.OutOfMemoryException)
            {
                hasOutputLeaseFault = true;
                throw;
            }
            catch (System.Exception exception) when (!(exception is System.OutOfMemoryException))
            {
                hasOutputLeaseFault = true;
                Log.Error(exception, $"Camera output '{GetOutputDisplayName(output)}' failed to deactivate.");
                return false;
            }
        }

        public virtual void UpdateCamera(float deltaTime)
        {
            AssertActorOwnerThread();
            if (float.IsNaN(deltaTime) || float.IsInfinity(deltaTime) || deltaTime < 0f)
            {
                throw new System.ArgumentOutOfRangeException(nameof(deltaTime));
            }

            if (!IsInitialized || isUpdatingCamera) return;

            isUpdatingCamera = true;
            invalidPoseEncounteredThisEvaluation = false;
            try
            {
                CameraPose fallbackPose = GetLastKnownGoodOrFallbackPose(
                    hasExplicitFovOverride ? lockedFOV : DefaultFOV);
                CameraPose desiredPose;
                try
                {
                    desiredPose = EvaluateDesiredPose(deltaTime);
                }
                catch (System.Exception exception) when (!(exception is System.OutOfMemoryException))
                {
                    Log.Error(exception, "Camera pose evaluation failed; the last valid pose was retained.");
                    ReportInvalidPose();
                    desiredPose = fallbackPose;
                }

                if (!TryUseValidPose(desiredPose, fallbackPose, out desiredPose))
                {
                    desiredPose = fallbackPose;
                }

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

                    if (float.IsNaN(blendDuration) ||
                        float.IsInfinity(blendDuration) ||
                        blendDuration < 0f)
                    {
                        blendDuration = IsValidBlendDuration(defaultBlendDuration)
                            ? defaultBlendDuration
                            : 0f;
                    }

                    blendState.Start(currentPose, blendDuration);
                    lastViewTarget = currentViewTarget;
                    lastPrimaryMode = primaryMode;
                    cameraStateDirty = false;
                }

                CameraPose outputPose;
                try
                {
                    outputPose = blendState.Evaluate(desiredPose, deltaTime);
                }
                catch (System.Exception exception) when (!(exception is System.OutOfMemoryException))
                {
                    Log.Error(exception, "Camera blend evaluation failed; the last valid pose was retained.");
                    ReportInvalidPose();
                    outputPose = currentPose;
                }

                TryUseValidPose(outputPose, currentPose, out outputPose);
                ApplyCameraPose(outputPose);
            }
            finally
            {
                if (!invalidPoseEncounteredThisEvaluation)
                {
                    invalidPoseReported = false;
                }

                isUpdatingCamera = false;
            }
        }

        protected virtual CameraPose EvaluateDesiredPose(float deltaTime)
        {
            float fallbackFov = hasExplicitFovOverride ? lockedFOV : DefaultFOV;
            if (!IsValidFov(fallbackFov))
            {
                fallbackFov = 60f;
            }

            CameraPose fallbackPose = GetLastKnownGoodOrFallbackPose(fallbackFov);
            CameraContext context = PCOwner != null ? PCOwner.GetCameraContext() : null;
            bool ownsEvaluationScope = context != null && context.TryBeginEvaluation();

            try
            {
                CameraPose desiredPose;
                if (context != null && context.CurrentViewTarget != null)
                {
                    try
                    {
                        context.CurrentViewTarget.CalcCamera(
                            deltaTime,
                            out desiredPose,
                            fallbackFov);
                    }
                    catch (System.Exception exception) when (!(exception is System.OutOfMemoryException))
                    {
                        Log.Error(exception, "View-target camera evaluation failed.");
                        ReportInvalidPose();
                        return fallbackPose;
                    }
                }
                else if (PendingViewTargetTF != null)
                {
                    if (!CameraPose.TryCreate(
                            PendingViewTargetTF.position,
                            PendingViewTargetTF.rotation,
                            fallbackFov,
                            out desiredPose))
                    {
                        ReportInvalidPose();
                        return fallbackPose;
                    }
                }
                else
                {
                    if (!CameraPose.TryCreate(
                            transform.position,
                            transform.rotation,
                            fallbackFov,
                            out desiredPose))
                    {
                        ReportInvalidPose();
                        return fallbackPose;
                    }
                }

                if (!TryUseValidPose(desiredPose, fallbackPose, out desiredPose))
                {
                    return fallbackPose;
                }

                if (context != null && ownsEvaluationScope)
                {
                    CameraMode baseMode = context.BaseCameraMode;
                    if (baseMode != null)
                    {
                        if (!TryEvaluateCameraMode(
                                baseMode,
                                context,
                                desiredPose,
                                fallbackPose,
                                deltaTime,
                                out desiredPose))
                        {
                            return fallbackPose;
                        }
                    }

                    int modeCount = context.CameraModeCount;
                    for (int i = 0; i < modeCount; i++)
                    {
                        CameraMode mode = context.GetCameraModeAt(i);
                        if (mode == null) continue;

                        if (!TryEvaluateCameraMode(
                                mode,
                                context,
                                desiredPose,
                                fallbackPose,
                                deltaTime,
                                out desiredPose))
                        {
                            return fallbackPose;
                        }
                    }
                }

                for (int i = 0; i < postProcessorCount; i++)
                {
                    ICameraPostProcessor proc = postProcessors[i];
                    if (proc == null)
                    {
                        continue;
                    }

                    try
                    {
                        desiredPose = proc.Process(desiredPose, context, deltaTime);
                    }
                    catch (System.Exception exception) when (!(exception is System.OutOfMemoryException))
                    {
                        Log.Error(exception, "Camera post-processor evaluation failed.");
                        ReportInvalidPose();
                        return fallbackPose;
                    }

                    if (!TryUseValidPose(desiredPose, fallbackPose, out desiredPose))
                    {
                        return fallbackPose;
                    }
                }

                if (hasExplicitFovOverride)
                {
                    desiredPose = desiredPose.WithFov(lockedFOV);
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
            if (!pose.IsValid)
            {
                ReportInvalidPose();
                return;
            }

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
            catch (System.Exception exception) when (!(exception is System.OutOfMemoryException))
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
            AssertActorOwnerThread();
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
            AssertActorOwnerThread();
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
                TryDeactivateOutput(output);
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

        private static bool ValidateResourceSetAgainstLease(
            in CameraOutputResourceSet resources,
            in CameraOutputLease lease,
            out string error)
        {
            if (!lease.IsValid)
            {
                error = "Camera output has no active ownership lease.";
                return false;
            }

            if (!resources.TryValidate(out error))
            {
                return false;
            }

            if (resources.Count != lease.ResourceCount)
            {
                error = "Camera output changed its ownership-resource count after lease acquisition.";
                return false;
            }

            for (int index = 0; index < resources.Count; index++)
            {
                if (resources.GetResourceId(index) != lease.GetResourceId(index))
                {
                    error =
                        $"Camera output resource snapshot {index} does not match its ownership lease.";
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
            catch (System.Exception exception) when (!(exception is System.OutOfMemoryException))
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
            invalidPoseEncounteredThisEvaluation = false;
            invalidPoseReported = false;
            hasOutputLeaseFault = false;
            ActiveOutput = null;
            activeOutputLease = default;
            activeOutputResources = default;
        }

        private void EnsureActorTickConfiguration()
        {
            if (TickPhase != ActorTickPhase.LateUpdate || IsTickEnabledAtStart)
            {
                ConfigureActorTick(ActorTickPhase.LateUpdate, startWithTickEnabled: false);
            }
        }

        private CameraPose GetLastKnownGoodOrFallbackPose(float fallbackFov = 60f)
        {
            if (hasCurrentPose && currentPose.IsValid)
            {
                return currentPose;
            }

            float safeFov = IsValidFov(fallbackFov)
                ? fallbackFov
                : IsValidFov(DefaultFOV)
                    ? DefaultFOV
                    : 60f;
            if (CameraPose.TryCreate(
                    transform.position,
                    transform.rotation,
                    safeFov,
                    out CameraPose transformPose))
            {
                return transformPose;
            }

            return new CameraPose(Vector3.zero, Quaternion.identity, safeFov);
        }

        private bool TryEvaluateCameraMode(
            CameraMode mode,
            CameraContext context,
            in CameraPose inputPose,
            in CameraPose fallbackPose,
            float deltaTime,
            out CameraPose evaluatedPose)
        {
            try
            {
                mode.Tick(context, deltaTime);
                evaluatedPose = mode.Evaluate(context, inputPose, deltaTime);
            }
            catch (System.Exception exception) when (!(exception is System.OutOfMemoryException))
            {
                Log.Error(
                    exception,
                    $"CameraMode '{mode.GetType().Name}' evaluation failed.");
                ReportInvalidPose();
                evaluatedPose = fallbackPose;
                return false;
            }

            return TryUseValidPose(evaluatedPose, fallbackPose, out evaluatedPose);
        }

        private bool TryUseValidPose(
            in CameraPose candidate,
            in CameraPose fallbackPose,
            out CameraPose acceptedPose)
        {
            if (candidate.IsValid)
            {
                acceptedPose = candidate;
                return true;
            }

            ReportInvalidPose();
            acceptedPose = fallbackPose;
            return false;
        }

        private void ReportInvalidPose()
        {
            invalidPoseEncounteredThisEvaluation = true;
            if (invalidPoseReported)
            {
                return;
            }

            invalidPoseReported = true;
            Log.Error("Camera pose validation failed; the last valid pose was retained.");
        }

        private static bool IsValidFov(float fov)
        {
            return !float.IsNaN(fov) &&
                   !float.IsInfinity(fov) &&
                   fov > 0f &&
                   fov < 180f;
        }

        private static bool IsValidBlendDuration(float duration)
        {
            return !float.IsNaN(duration) &&
                   !float.IsInfinity(duration) &&
                   duration >= 0f;
        }

    }
}
