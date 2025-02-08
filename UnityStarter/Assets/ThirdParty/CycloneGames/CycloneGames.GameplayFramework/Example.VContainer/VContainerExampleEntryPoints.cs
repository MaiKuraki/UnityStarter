using VContainer;
using VContainer.Unity;

namespace CycloneGames.GameplayFramework.VContainer
{
    public class VContainerExampleEntryPoints : IStartable
    {
        [Inject] private IGameMode gameMode;

        public void Start()
        {
            gameMode.LaunchGameMode();
        }
    }
}