using System;
using UnityEngine;

namespace CycloneGames.GameplayFramework.Runtime
{
    /// <summary>
    /// Participant controller facade. LocalPlayer, possession, and view target are independent
    /// relationships; remote PlayerControllers do not create local camera state.
    /// </summary>
    public class PlayerController : Controller
    {
        [SerializeField] private bool bAutoManageActiveCameraTarget = true;

        private LocalPlayer localPlayer;
        private SpectatorPawn spectatorPawn;
        private CameraManager cameraManager;
        private CameraContext cameraContext;
        private Actor viewTarget;
        private bool hasExplicitViewTarget;
        private bool runtimeComponentsInitialized;

        public override bool IsLocalController
        {
            get
            {
                AssertActorOwnerThread();
                return localPlayer != null;
            }
        }

        public LocalPlayer LocalPlayer
        {
            get
            {
                AssertActorOwnerThread();
                return localPlayer;
            }
        }

        public bool RuntimeComponentsInitialized
        {
            get
            {
                AssertActorOwnerThread();
                return runtimeComponentsInitialized;
            }
            private set => runtimeComponentsInitialized = value;
        }

        public bool AutoManageActiveCameraTargetEnabled
        {
            get
            {
                AssertActorOwnerThread();
                return bAutoManageActiveCameraTarget;
            }
        }

        public SpectatorPawn GetSpectatorPawn()
        {
            AssertActorOwnerThread();
            return spectatorPawn;
        }

        public CameraManager GetCameraManager()
        {
            AssertActorOwnerThread();
            return cameraManager;
        }

        public virtual void InitializePlayer(
            World targetWorld,
            PlayerState playerState,
            LocalPlayer owningLocalPlayer,
            CameraManager localCameraManager = null,
            SpectatorPawn initialSpectatorPawn = null)
        {
            AssertActorOwnerThread();
            if (RuntimeComponentsInitialized)
            {
                throw new InvalidOperationException("PlayerController runtime components are already initialized.");
            }

            base.Initialize(targetWorld, playerState ?? throw new ArgumentNullException(nameof(playerState)));

            if (localCameraManager != null && owningLocalPlayer == null)
            {
                throw new InvalidOperationException("Only a local PlayerController can own a CameraManager.");
            }

            if (localCameraManager != null && !ReferenceEquals(localCameraManager.World, targetWorld))
            {
                throw new InvalidOperationException("CameraManager must belong to the same World.");
            }

            if (initialSpectatorPawn != null && !ReferenceEquals(initialSpectatorPawn.World, targetWorld))
            {
                throw new InvalidOperationException("SpectatorPawn must belong to the same World.");
            }

            localPlayer = owningLocalPlayer;
            spectatorPawn = initialSpectatorPawn;
            cameraManager = localCameraManager;

            if (cameraManager != null)
            {
                EnsureCameraContextCreated();
                cameraManager.SetOwner(this);
                cameraManager.InitializeFor(this);
            }

            RuntimeComponentsInitialized = true;
            RefreshActiveCameraTarget();
        }

        #region Camera context
        public CameraContext GetCameraContext()
        {
            AssertActorOwnerThread();
            EnsureCameraContextCreated();
            return cameraContext;
        }

        protected virtual IViewTargetPolicy CreateDefaultViewTargetPolicy()
        {
            return new DefaultGameplayViewTargetPolicy();
        }

        protected virtual CameraMode CreateDefaultCameraMode()
        {
            return new ViewTargetCameraMode();
        }

        protected virtual int GetCameraModeStackCapacity() => 8;

        private void EnsureCameraContextCreated()
        {
            AssertActorOwnerThread();
            if (cameraContext != null)
            {
                return;
            }

            cameraContext = new CameraContext(this, GetCameraModeStackCapacity());
            cameraContext.SetViewTargetPolicy(CreateDefaultViewTargetPolicy());
            cameraContext.SetBaseCameraMode(CreateDefaultCameraMode());
            cameraContext.SetResolvedViewTarget(GetAutoManagedViewTarget());
            viewTarget = cameraContext.CurrentViewTarget;
        }

        public virtual void SetViewTargetPolicy(IViewTargetPolicy policy)
        {
            AssertActorOwnerThread();
            GetCameraContext().SetViewTargetPolicy(policy ?? CreateDefaultViewTargetPolicy());
            RefreshActiveCameraTarget();
        }

        public virtual void SetBaseCameraMode(CameraMode cameraMode)
        {
            AssertActorOwnerThread();
            GetCameraContext().SetBaseCameraMode(cameraMode);
            cameraManager?.NotifyCameraStateChanged();
        }

        public virtual bool TryPushCameraMode(CameraMode cameraMode)
        {
            AssertActorOwnerThread();
            if (cameraMode == null)
            {
                return false;
            }

            bool pushed = GetCameraContext().TryPushCameraMode(cameraMode);
            if (pushed)
            {
                cameraManager?.NotifyCameraStateChanged();
            }

            return pushed;
        }

        public virtual bool TryPushOrReplaceOldestCameraMode(
            CameraMode cameraMode,
            out CameraMode replacedMode)
        {
            AssertActorOwnerThread();
            if (cameraMode == null)
            {
                replacedMode = null;
                return false;
            }

            bool applied = GetCameraContext().TryPushOrReplaceOldest(cameraMode, out replacedMode);
            if (applied)
            {
                cameraManager?.NotifyCameraStateChanged();
            }

            return applied;
        }

        public virtual bool RemoveCameraMode(CameraMode cameraMode)
        {
            AssertActorOwnerThread();
            if (cameraMode == null || cameraContext == null)
            {
                return false;
            }

            bool removed = cameraContext.RemoveCameraMode(cameraMode);
            if (removed)
            {
                cameraManager?.NotifyCameraStateChanged();
            }

            return removed;
        }

        public virtual void SetAutoManageActiveCameraTarget(bool enabled)
        {
            AssertActorOwnerThread();
            if (bAutoManageActiveCameraTarget == enabled)
            {
                return;
            }

            bAutoManageActiveCameraTarget = enabled;
            if (enabled)
            {
                hasExplicitViewTarget = false;
                AutoManageActiveCameraTarget(GetAutoManagedViewTarget());
            }
        }

        public virtual void ClearViewTargetOverride(bool restoreAutoManagedTarget = true)
        {
            AssertActorOwnerThread();
            hasExplicitViewTarget = false;
            cameraContext?.ClearManualViewTargetOverride();
            if (restoreAutoManagedTarget)
            {
                RefreshActiveCameraTarget();
            }
        }

        protected virtual Actor GetAutoManagedViewTarget()
        {
            Pawn pawn = GetPawn();
            if (pawn != null) return pawn;
            if (spectatorPawn != null) return spectatorPawn;
            return this;
        }

        protected virtual void RefreshActiveCameraTarget()
        {
            if (!IsLocalController && cameraContext == null)
            {
                return;
            }

            EnsureCameraContextCreated();
            if (hasExplicitViewTarget && viewTarget == null)
            {
                hasExplicitViewTarget = false;
                cameraContext.ClearManualViewTargetOverride();
            }

            if (hasExplicitViewTarget)
            {
                ApplyViewTargetToCameraManager(viewTarget);
                return;
            }

            if (bAutoManageActiveCameraTarget)
            {
                Actor resolvedTarget = cameraContext.ResolveViewTarget(GetAutoManagedViewTarget());
                ApplyViewTargetToCameraManager(resolvedTarget);
                return;
            }

            ApplyViewTargetToCameraManager(viewTarget);
        }

        protected virtual void ApplyViewTargetToCameraManager(Actor target)
        {
            EnsureCameraContextCreated();
            viewTarget = target;
            cameraContext.SetResolvedViewTarget(target);
            cameraManager?.SetViewTarget(target != null ? target.transform : null);
        }

        protected virtual void SetViewTargetInternal(Actor newViewTarget, bool isExplicitOverride)
        {
            ValidateViewTarget(newViewTarget);

            EnsureCameraContextCreated();
            hasExplicitViewTarget = isExplicitOverride;
            if (isExplicitOverride)
            {
                cameraContext.SetManualViewTargetOverride(newViewTarget);
            }
            else
            {
                cameraContext.ClearManualViewTargetOverride();
            }

            ApplyViewTargetToCameraManager(newViewTarget);
        }

        public virtual void SetViewTarget(Actor newViewTarget)
        {
            AssertActorOwnerThread();
            SetViewTargetInternal(newViewTarget, isExplicitOverride: true);
        }

        public virtual void SetViewTargetWithBlend(Actor newViewTarget, float blendTime = 0f)
        {
            AssertActorOwnerThread();
            if (float.IsNaN(blendTime) || float.IsInfinity(blendTime) || blendTime < 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(blendTime));
            }

            // Validate before publishing the one-shot override so a rejected target cannot
            // leave blend state that would be consumed by a later, unrelated transition.
            ValidateViewTarget(newViewTarget);
            cameraManager?.SetNextBlendDuration(blendTime);
            SetViewTarget(newViewTarget);
        }

        private void ValidateViewTarget(Actor newViewTarget)
        {
            if (newViewTarget != null && World != null && !ReferenceEquals(newViewTarget.World, World))
            {
                throw new InvalidOperationException("View target must belong to the same World.");
            }
        }

        public override Actor GetViewTarget()
        {
            AssertActorOwnerThread();
            if (cameraContext != null && cameraContext.CurrentViewTarget != null)
            {
                return cameraContext.CurrentViewTarget;
            }

            return viewTarget != null ? viewTarget : base.GetViewTarget();
        }

        public virtual void AutoManageActiveCameraTarget(Actor suggestedTarget)
        {
            AssertActorOwnerThread();
            if (!bAutoManageActiveCameraTarget || hasExplicitViewTarget)
            {
                return;
            }

            Actor target = GetCameraContext().ResolveViewTarget(
                suggestedTarget != null ? suggestedTarget : GetAutoManagedViewTarget());
            SetViewTargetInternal(target, isExplicitOverride: false);
        }
        #endregion

        protected override void OnPossess(Pawn newPawn)
        {
            base.OnPossess(newPawn);
            AutoManageActiveCameraTarget(newPawn);
        }

        protected override void OnUnPossess()
        {
            base.OnUnPossess();
            RefreshActiveCameraTarget();
        }

        protected override void OnWorldUnbound(EndPlayReason reason)
        {
            var terminalExceptions = new TerminalExceptionAccumulator();
            World currentWorld = World;
            CameraManager ownedCameraManager = cameraManager;
            SpectatorPawn ownedSpectatorPawn = spectatorPawn;
            try
            {
                base.OnWorldUnbound(reason);
            }
            catch (Exception exception)
            {
                terminalExceptions.HandleAndLog(
                    exception,
                    "PlayerController base Controller cleanup failed while unbinding from its World.");
            }

            ClearPlayerRuntimeRelationships(ref terminalExceptions);
            bool destroyAssociatedActors = false;
            if (currentWorld != null)
            {
                try
                {
                    WorldLifecycleState state = currentWorld.LifecycleState;
                    destroyAssociatedActors =
                        state == WorldLifecycleState.Initializing ||
                        state == WorldLifecycleState.Playing;
                }
                catch (Exception exception)
                {
                    terminalExceptions.HandleAndLog(
                        exception,
                        "PlayerController failed to inspect World state during relationship cleanup.");
                }
            }

            if (destroyAssociatedActors)
            {
                DestroyAssociatedActor(
                    currentWorld,
                    ownedCameraManager,
                    ref terminalExceptions);
                DestroyAssociatedActor(
                    currentWorld,
                    ownedSpectatorPawn,
                    ref terminalExceptions);
            }

            terminalExceptions.ThrowIfCaptured();
        }

        protected override void OnDestroy()
        {
            var terminalExceptions = new TerminalExceptionAccumulator();
            World currentWorld = World;
            CameraManager ownedCameraManager = cameraManager;
            SpectatorPawn ownedSpectatorPawn = spectatorPawn;
            if (currentWorld != null)
            {
                try
                {
                    GameMode currentGameMode = currentWorld.GameMode;
                    if (currentWorld.ContainsPlayerController(this) &&
                        currentGameMode != null &&
                        !currentGameMode.HandleDestroyingPlayerController(this))
                    {
                        terminalExceptions.LogFailure(
                            "PlayerController destruction retained participant cleanup ownership for retry.");
                    }
                }
                catch (Exception exception)
                {
                    terminalExceptions.HandleAndLog(
                        exception,
                        "PlayerController participant cleanup failed during destruction; terminal cleanup will continue.");
                }
            }

            try
            {
                base.OnDestroy();
            }
            catch (Exception exception)
            {
                terminalExceptions.HandleAndLog(
                    exception,
                    "PlayerController base Controller cleanup failed during destruction.");
            }

            ClearPlayerRuntimeRelationships(ref terminalExceptions);

            if (currentWorld != null)
            {
                DestroyAssociatedActor(
                    currentWorld,
                    ownedCameraManager,
                    ref terminalExceptions);
                DestroyAssociatedActor(
                    currentWorld,
                    ownedSpectatorPawn,
                    ref terminalExceptions);
            }

            terminalExceptions.ThrowIfCaptured();
        }

        private void ClearPlayerRuntimeRelationships(
            ref TerminalExceptionAccumulator terminalExceptions)
        {
            LocalPlayer ownedLocalPlayer = localPlayer;
            CameraContext ownedCameraContext = cameraContext;
            bool cameraContextReleased = ownedCameraContext == null;
            try
            {
                cameraContextReleased = ownedCameraContext == null || ownedCameraContext.Clear();
                if (!cameraContextReleased)
                {
                    terminalExceptions.LogFailure(
                        "PlayerController retained a faulted camera context so cleanup can be retried.");
                }
            }
            catch (Exception exception)
            {
                terminalExceptions.HandleAndLog(
                    exception,
                    "PlayerController failed to clear its camera context; relationship cleanup will continue.");
            }

            cameraContext = cameraContextReleased ? null : ownedCameraContext;
            cameraManager = null;
            spectatorPawn = null;
            viewTarget = null;
            hasExplicitViewTarget = false;
            RuntimeComponentsInitialized = false;
            if (ownedLocalPlayer != null)
            {
                try
                {
                    if (ReferenceEquals(ownedLocalPlayer.PlayerController, this))
                    {
                        ownedLocalPlayer.PlayerController = null;
                    }
                }
                catch (Exception exception)
                {
                    terminalExceptions.HandleAndLog(
                        exception,
                        "PlayerController failed to clear its LocalPlayer association during terminal cleanup.");
                }
            }

            localPlayer = null;
        }

        internal bool TryReleaseCameraContextForWorldTeardown()
        {
            AssertActorOwnerThread();
            CameraContext ownedCameraContext = cameraContext;
            if (ownedCameraContext == null)
            {
                return true;
            }

            try
            {
                if (!ownedCameraContext.Clear())
                {
                    return false;
                }

                cameraContext = null;
                return true;
            }
            catch (Exception exception)
            {
                var terminalExceptions = new TerminalExceptionAccumulator();
                terminalExceptions.HandleAndLog(
                    exception,
                    "PlayerController camera context cleanup failed; World teardown retained the participant for retry.");
                terminalExceptions.ThrowIfCaptured();
                return false;
            }
        }

        private void DestroyAssociatedActor(
            World currentWorld,
            Actor actor,
            ref TerminalExceptionAccumulator terminalExceptions)
        {
            try
            {
                if (actor == null || !currentWorld.IsActorRegistered(actor))
                {
                    return;
                }

                currentWorld.DestroyActor(actor);
            }
            catch (Exception exception)
            {
                terminalExceptions.HandleAndLog(
                    exception,
                    "PlayerController failed to destroy an associated Actor during terminal cleanup.");
            }
        }
    }
}
