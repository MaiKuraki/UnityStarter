
using CycloneGames.Core;
using VContainer;

namespace CycloneGames.GameplayFramework.Example.VContainer
{
    public class VContainerExampleGameMode : GameMode
    {
        //  NOTE: In VContainer, we use the 'Inject' attribute to inject the dependencies, not use base.Initialize
        [Inject]
        public override void Initialize(IObjectSpawner objectSpawner, IWorldSettings worldSettings)
        {
            base.Initialize(objectSpawner, worldSettings);
        }
    }
}