using CycloneGames.Logger;
using Cysharp.Threading.Tasks;
using System.Threading;

namespace CycloneGames.GameplayFramework
{
    public class PlayerController : Controller
    {
        public UniTask InitializationTask { get; private set; }
        
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
            if (worldSettings?.CameraManagerClass == null)
            {
                //  This is an expected case, CameraManager is optional.
                return;
            }
            
            cameraManager = objectSpawner?.Create(worldSettings.CameraManagerClass) as CameraManager;
            if (cameraManager == null)
            {
                CLogger.LogError("Spawn CameraManager Failed, a CameraManagerClass was provided in WorldSettings but it could not be spawned. Check your spawn pipeline.");
                return;
            }

            cameraManager.SetOwner(this);
            cameraManager.InitializeFor(this);
        }

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
