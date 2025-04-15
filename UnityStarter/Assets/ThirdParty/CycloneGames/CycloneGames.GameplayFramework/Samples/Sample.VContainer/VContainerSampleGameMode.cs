using UnityEngine;
using VContainer;
using CycloneGames.Factory;

namespace CycloneGames.GameplayFramework.Sample.VContainer
{
    public class VContainerSampleGameMode : GameMode
    {
        //  NOTE: In VContainer, we use the 'Inject' attribute to inject the dependencies, not use base.Initialize
        public override void Initialize(in IFactory objectSpawner, in IWorldSettings worldSettings)
        {
            base.Initialize(objectSpawner, worldSettings);
        }
    }
}