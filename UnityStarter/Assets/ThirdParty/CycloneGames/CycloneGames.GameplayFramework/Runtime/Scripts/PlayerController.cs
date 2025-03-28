using CycloneGames.Logger;
using Cysharp.Threading.Tasks;

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
            spectatorPawn = objectSpawner?.SpawnObject(worldSettings?.SpectatorPawnClass) as SpectatorPawn;
            if (spectatorPawn == null)
            {
                CLogger.LogError("Spawn Spectator Failed, please check your spawn pipeline");
                return null;
            }
            return spectatorPawn;
        }

        void SpawnCameraManager()
        {
            cameraManager = objectSpawner?.SpawnObject(worldSettings?.CameraManagerClass) as CameraManager;
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

        protected override void Awake()
        {
            base.Awake();

            InitializePlayerController().Forget();
        }

        private async UniTask InitializePlayerController()
        {
            await UniTask.WaitUntil(() => base.IsInitialized);
            InitPlayerState();
            SpawnCameraManager();
            SpawnSpectatorPawn();
        }
    }
}