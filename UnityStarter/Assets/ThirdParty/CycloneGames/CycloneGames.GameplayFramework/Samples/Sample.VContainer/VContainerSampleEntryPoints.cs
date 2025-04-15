using CycloneGames.Factory;
using VContainer;
using VContainer.Unity;

namespace CycloneGames.GameplayFramework.Sample.VContainer
{
    public class VContainerSampleEntryPoints : IStartable
    {
        [Inject] private IGameMode gameMode;
        [Inject] private IFactory objectSpawner;
        [Inject] private IWorldSettings worldSettings;

        public void Start()
        {
            ((GameMode)gameMode).Initialize(objectSpawner, worldSettings);
            gameMode.LaunchGameMode();
        }
    }
}