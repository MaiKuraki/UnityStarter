using CycloneGames.Factory;
using VContainer;
using VContainer.Unity;

namespace CycloneGames.GameplayFramework.Sample.VContainer
{
    public class VContainerSampleEntryPoints : IStartable
    {
        [Inject] private IGameMode gameMode;
        public void Start()
        {
            gameMode.LaunchGameMode();
        }
    }
}