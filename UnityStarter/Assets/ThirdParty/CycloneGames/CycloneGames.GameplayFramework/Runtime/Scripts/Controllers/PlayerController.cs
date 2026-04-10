using CycloneGames.Logger;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace CycloneGames.GameplayFramework.Runtime
{
    public class PlayerController : Controller
    {
        [SerializeField] private bool bAutoManageActiveCameraTarget = true;

        public UniTask InitializationTask { get; private set; } = UniTask.CompletedTask;
        public bool RuntimeComponentsInitialized { get; private set; }

        private SpectatorPawn spectatorPawn;
        public SpectatorPawn GetSpectatorPawn() => spectatorPawn;
        private CameraManager cameraManager;
        public CameraManager GetCameraManager() => cameraManager;
        private CycloneGames.GameplayFramework.Runtime.CameraContext cameraContext;

        private Actor viewTarget;
        private bool hasExplicitViewTarget;

        #region Spectator
        public SpectatorPawn SpawnSpectatorPawn()
        {
            if (spectatorPawn != null) return spectatorPawn;

            spectatorPawn = objectSpawner?.Create(worldSettings?.SpectatorPawnClass) as SpectatorPawn;
            if (spectatorPawn == null)
            {
                CLogger.LogError("[PlayerController] Spawn SpectatorPawn Failed, check spawn pipeline");
                return null;
            }

            RefreshActiveCameraTarget();
            return spectatorPawn;
        }
        #endregion

        #region Camera
        void SpawnCameraManager()
        {
            if (cameraManager != null) return;
            if (worldSettings?.CameraManagerClass == null) return;

            cameraManager = objectSpawner?.Create(worldSettings.CameraManagerClass) as CameraManager;
            if (cameraManager == null)
            {
                CLogger.LogError("[PlayerController] Spawn CameraManager Failed, check spawn pipeline");
                return;
            }

            cameraManager.SetOwner(this);
            cameraManager.InitializeFor(this);
            RefreshActiveCameraTarget();
        }

        public virtual void InitializeRuntimeComponents()
        {
            if (RuntimeComponentsInitialized) return;

            if (!IsInitialized)
            {
                CLogger.LogError("[PlayerController] InitializeRuntimeComponents called before Initialize");
                return;
            }

            if (GetPlayerState() == null)
            {
                InitPlayerState();
            }

            EnsureCameraContextCreated();
            SpawnCameraManager();
            SpawnSpectatorPawn();
            RefreshActiveCameraTarget();
            RuntimeComponentsInitialized = true;
            InitializationTask = UniTask.CompletedTask;
        }

        public CycloneGames.GameplayFramework.Runtime.CameraContext GetCameraContext()
        {
            EnsureCameraContextCreated();
            return cameraContext;
        }

        protected virtual CycloneGames.GameplayFramework.Runtime.IViewTargetPolicy CreateDefaultViewTargetPolicy()
        {
            return new CycloneGames.GameplayFramework.Runtime.DefaultGameplayViewTargetPolicy();
        }

        protected virtual CycloneGames.GameplayFramework.Runtime.CameraMode CreateDefaultCameraMode()
        {
            // Keep framework default neutral; project-specific framing belongs in game layer.
            return new CycloneGames.GameplayFramework.Runtime.ViewTargetCameraMode();
        }

        private void EnsureCameraContextCreated()
        {
            if (cameraContext != null) return;

            cameraContext = new CycloneGames.GameplayFramework.Runtime.CameraContext(this);
            cameraContext.SetViewTargetPolicy(CreateDefaultViewTargetPolicy());
            cameraContext.SetBaseCameraMode(CreateDefaultCameraMode());
            cameraContext.SetResolvedViewTarget(GetAutoManagedViewTarget());
            viewTarget = cameraContext.CurrentViewTarget;
        }

        public bool AutoManageActiveCameraTargetEnabled => bAutoManageActiveCameraTarget;

        public virtual void SetViewTargetPolicy(CycloneGames.GameplayFramework.Runtime.IViewTargetPolicy policy)
        {
            GetCameraContext().SetViewTargetPolicy(policy ?? CreateDefaultViewTargetPolicy());
            RefreshActiveCameraTarget();
        }

        public virtual void SetBaseCameraMode(CycloneGames.GameplayFramework.Runtime.CameraMode cameraMode)
        {
            GetCameraContext().SetBaseCameraMode(cameraMode);
            cameraManager?.NotifyCameraStateChanged();
        }

        public virtual void PushCameraMode(CycloneGames.GameplayFramework.Runtime.CameraMode cameraMode)
        {
            if (cameraMode == null) return;

            GetCameraContext().PushCameraMode(cameraMode);
            cameraManager?.NotifyCameraStateChanged();
        }

        public virtual bool RemoveCameraMode(CycloneGames.GameplayFramework.Runtime.CameraMode cameraMode)
        {
            if (cameraMode == null || cameraContext == null) return false;

            bool removed = cameraContext.RemoveCameraMode(cameraMode);
            if (removed)
            {
                cameraManager?.NotifyCameraStateChanged();
            }
            return removed;
        }

        public virtual void SetAutoManageActiveCameraTarget(bool enabled)
        {
            if (bAutoManageActiveCameraTarget == enabled) return;

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
            if (cameraManager != null)
            {
                // Keep CameraManager fallback transform target in sync with Actor view target.
                // CameraManager primarily consumes CameraContext.CurrentViewTarget, but syncing
                // PendingViewTargetTF keeps internal state/debug view consistent.
                cameraManager.SetViewTarget(target != null ? target.transform : null);
            }
        }

        protected virtual void SetViewTargetInternal(Actor newViewTarget, bool isExplicitOverride)
        {
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

        /// <summary>
        /// Sets the view target for this player controller's camera system.
        /// </summary>
        public virtual void SetViewTarget(Actor NewViewTarget)
        {
            SetViewTargetInternal(NewViewTarget, isExplicitOverride: true);
        }

        /// <summary>
        /// Sets the view target with a blend time for smooth camera transition.
        /// </summary>
        public virtual void SetViewTargetWithBlend(Actor NewViewTarget, float BlendTime = 0f)
        {
            cameraManager?.SetNextBlendDuration(BlendTime);
            SetViewTarget(NewViewTarget);
        }

        public override Actor GetViewTarget()
        {
            if (cameraContext != null && cameraContext.CurrentViewTarget != null) return cameraContext.CurrentViewTarget;
            if (viewTarget != null) return viewTarget;
            return base.GetViewTarget();
        }

        public virtual void AutoManageActiveCameraTarget(Actor SuggestedTarget)
        {
            if (!bAutoManageActiveCameraTarget || hasExplicitViewTarget) return;

            Actor target = GetCameraContext().ResolveViewTarget(SuggestedTarget != null ? SuggestedTarget : GetAutoManagedViewTarget());
            SetViewTargetInternal(target, isExplicitOverride: false);
        }
        #endregion

        #region Initialization

        protected override void OnPossess(Pawn InPawn)
        {
            base.OnPossess(InPawn);
            AutoManageActiveCameraTarget(InPawn);
        }

        protected override void OnUnPossess()
        {
            base.OnUnPossess();
            RefreshActiveCameraTarget();
        }

        protected override void OnDestroy()
        {
            cameraManager = null;
            spectatorPawn = null;
            cameraContext = null;
            viewTarget = null;
            hasExplicitViewTarget = false;
            RuntimeComponentsInitialized = false;
            InitializationTask = UniTask.CompletedTask;
            base.OnDestroy();
        }
        #endregion
    }
}
