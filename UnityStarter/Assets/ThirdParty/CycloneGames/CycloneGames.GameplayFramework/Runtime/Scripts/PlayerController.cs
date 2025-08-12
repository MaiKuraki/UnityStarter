using CycloneGames.Logger;
using Cysharp.Threading.Tasks;
using System.Threading;

namespace CycloneGames.GameplayFramework
{
    public class PlayerController : Controller
    {
        private SpectatorPawn spectatorPawn;
        public SpectatorPawn GetSpectatorPawn() => spectatorPawn;
        private CameraManager cameraManager;
        public CameraManager GetCameraManager() => cameraManager;
        public SpectatorPawn SpawnSpectatorPawn()
        {
            spectatorPawn = objectSpawner?.Create(worldSettings?.SpectatorPawnClass) as SpectatorPawn;
            if (spectatorPawn == null)
            {
                CLogger.LogError("Spawn Spectator Failed, please check your spawn pipeline");
                return null;
            }
            return spectatorPawn;
        }

        void SpawnCameraManager()
        {
            cameraManager = objectSpawner?.Create(worldSettings?.CameraManagerClass) as CameraManager;
            if (cameraManager == null)
            {
                CLogger.LogError("Spawn CameraManager Failed, please check your spawn pipeline");
                return;
            }

            if (cameraManager)
            {
                cameraManager.SetOwner(this);
                cameraManager.InitializeFor(this);
            }
        }

        private CancellationTokenSource initCts;

        protected override void Awake()
        {
            base.Awake();

            initCts = new CancellationTokenSource();
            InitializePlayerController(initCts.Token).Forget();
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

        protected override void OnDestroy()
        {
            base.OnDestroy();
            if (initCts != null)
            {
                initCts.Cancel();
                initCts.Dispose();
                initCts = null;
            }
        }
    }
}