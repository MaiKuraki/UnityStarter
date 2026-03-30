using CycloneGames.Logger;
using Cysharp.Threading.Tasks;
using System.Threading;
using UnityEngine;

namespace CycloneGames.GameplayFramework.Runtime
{
    public class PlayerController : Controller
    {
        public UniTask InitializationTask { get; private set; }

        private SpectatorPawn spectatorPawn;
        public SpectatorPawn GetSpectatorPawn() => spectatorPawn;
        private CameraManager cameraManager;
        public CameraManager GetCameraManager() => cameraManager;

        private Actor viewTarget;

        #region Spectator
        public SpectatorPawn SpawnSpectatorPawn()
        {
            spectatorPawn = objectSpawner?.Create(worldSettings?.SpectatorPawnClass) as SpectatorPawn;
            if (spectatorPawn == null)
            {
                CLogger.LogError("[PlayerController] Spawn SpectatorPawn Failed, check spawn pipeline");
                return null;
            }
            return spectatorPawn;
        }
        #endregion

        #region Camera
        void SpawnCameraManager()
        {
            if (worldSettings?.CameraManagerClass == null) return;

            cameraManager = objectSpawner?.Create(worldSettings.CameraManagerClass) as CameraManager;
            if (cameraManager == null)
            {
                CLogger.LogError("[PlayerController] Spawn CameraManager Failed, check spawn pipeline");
                return;
            }

            cameraManager.SetOwner(this);
            cameraManager.InitializeFor(this);
        }

        /// <summary>
        /// Sets the view target for this player controller's camera system.
        /// </summary>
        public virtual void SetViewTarget(Actor NewViewTarget)
        {
            viewTarget = NewViewTarget;
            if (cameraManager != null && NewViewTarget != null)
            {
                cameraManager.SetViewTarget(NewViewTarget.transform);
            }
        }

        /// <summary>
        /// Sets the view target with a blend time for smooth camera transition.
        /// </summary>
        public virtual void SetViewTargetWithBlend(Actor NewViewTarget, float BlendTime = 0f)
        {
            SetViewTarget(NewViewTarget);
        }

        public override Actor GetViewTarget()
        {
            if (viewTarget != null) return viewTarget;
            return base.GetViewTarget();
        }

        public virtual void AutoManageActiveCameraTarget(Actor SuggestedTarget)
        {
            if (cameraManager != null && SuggestedTarget != null)
            {
                cameraManager.SetViewTarget(SuggestedTarget.transform);
            }
        }
        #endregion

        #region Initialization
        private CancellationTokenSource initCts;

        protected override void Awake()
        {
            base.Awake();
            initCts = new CancellationTokenSource();
            InitializationTask = InitializePlayerController(initCts.Token);
        }

        private async UniTask InitializePlayerController(CancellationToken token)
        {
            await UniTask.WaitUntil(() => base.IsInitialized, cancellationToken: token);
            if (token.IsCancellationRequested) return;
            InitPlayerState();
            if (token.IsCancellationRequested) return;
            SpawnCameraManager();
            if (token.IsCancellationRequested) return;
            SpawnSpectatorPawn();
        }

        protected override void OnPossess(Pawn InPawn)
        {
            base.OnPossess(InPawn);
            AutoManageActiveCameraTarget(InPawn);
        }

        protected override void OnDestroy()
        {
            if (initCts != null)
            {
                initCts.Cancel();
                initCts.Dispose();
                initCts = null;
            }
            base.OnDestroy();
        }
        #endregion
    }
}
