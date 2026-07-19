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

        public override bool IsLocalController => localPlayer != null;
        public LocalPlayer LocalPlayer => localPlayer;
        public bool RuntimeComponentsInitialized { get; private set; }
        public bool AutoManageActiveCameraTargetEnabled => bAutoManageActiveCameraTarget;

        public SpectatorPawn GetSpectatorPawn() => spectatorPawn;
        public CameraManager GetCameraManager() => cameraManager;

        public virtual void InitializePlayer(
            World targetWorld,
            PlayerState playerState,
            LocalPlayer owningLocalPlayer,
            CameraManager localCameraManager = null,
            SpectatorPawn initialSpectatorPawn = null)
        {
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
            GetCameraContext().SetViewTargetPolicy(policy ?? CreateDefaultViewTargetPolicy());
            RefreshActiveCameraTarget();
        }

        public virtual void SetBaseCameraMode(CameraMode cameraMode)
        {
            GetCameraContext().SetBaseCameraMode(cameraMode);
            cameraManager?.NotifyCameraStateChanged();
        }

        public virtual bool PushCameraMode(CameraMode cameraMode)
        {
            if (cameraMode == null)
            {
                return false;
            }

            bool pushed = GetCameraContext().PushCameraMode(cameraMode);
            if (pushed)
            {
                cameraManager?.NotifyCameraStateChanged();
            }

            return pushed;
        }

        public virtual bool TryPushCameraMode(CameraMode cameraMode) => PushCameraMode(cameraMode);

        public virtual bool TryPushOrReplaceOldestCameraMode(
            CameraMode cameraMode,
            out CameraMode replacedMode)
        {
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
            SetViewTargetInternal(newViewTarget, isExplicitOverride: true);
        }

        public virtual void SetViewTargetWithBlend(Actor newViewTarget, float blendTime = 0f)
        {
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
            if (cameraContext != null && cameraContext.CurrentViewTarget != null)
            {
                return cameraContext.CurrentViewTarget;
            }

            return viewTarget != null ? viewTarget : base.GetViewTarget();
        }

        public virtual void AutoManageActiveCameraTarget(Actor suggestedTarget)
        {
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
            World currentWorld = World;
            CameraManager ownedCameraManager = cameraManager;
            SpectatorPawn ownedSpectatorPawn = spectatorPawn;
            try
            {
                base.OnWorldUnbound(reason);
            }
            finally
            {
                ClearPlayerRuntimeRelationships();
                if (currentWorld != null &&
                    (currentWorld.LifecycleState == WorldLifecycleState.Initializing ||
                     currentWorld.LifecycleState == WorldLifecycleState.Playing))
                {
                    DestroyAssociatedActor(currentWorld, ownedCameraManager);
                    DestroyAssociatedActor(currentWorld, ownedSpectatorPawn);
                }
            }
        }

        protected override void OnDestroy()
        {
            World currentWorld = World;
            if (currentWorld != null &&
                currentWorld.ContainsPlayerController(this) &&
                currentWorld.GameMode != null)
            {
                currentWorld.GameMode.HandleDestroyingPlayerController(this);
            }

            CameraManager ownedCameraManager = cameraManager;
            SpectatorPawn ownedSpectatorPawn = spectatorPawn;

            base.OnDestroy();
            ClearPlayerRuntimeRelationships();

            if (currentWorld != null)
            {
                if (ownedCameraManager != null && currentWorld.IsActorRegistered(ownedCameraManager))
                {
                    currentWorld.DestroyActor(ownedCameraManager);
                }

                if (ownedSpectatorPawn != null && currentWorld.IsActorRegistered(ownedSpectatorPawn))
                {
                    currentWorld.DestroyActor(ownedSpectatorPawn);
                }
            }
        }

        private void ClearPlayerRuntimeRelationships()
        {
            LocalPlayer ownedLocalPlayer = localPlayer;
            try
            {
                cameraContext?.Clear();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, this);
            }
            finally
            {
                cameraContext = null;
                cameraManager = null;
                spectatorPawn = null;
                viewTarget = null;
                hasExplicitViewTarget = false;
                RuntimeComponentsInitialized = false;
                if (ownedLocalPlayer != null && ReferenceEquals(ownedLocalPlayer.PlayerController, this))
                {
                    ownedLocalPlayer.PlayerController = null;
                }

                localPlayer = null;
            }
        }

        private static void DestroyAssociatedActor(World currentWorld, Actor actor)
        {
            if (actor == null || !currentWorld.IsActorRegistered(actor))
            {
                return;
            }

            try
            {
                currentWorld.DestroyActor(actor);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, actor);
            }
        }
    }
}
