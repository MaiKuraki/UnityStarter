using UnityEngine;
using VContainer;
using CycloneGames.Factory;

namespace CycloneGames.GameplayFramework.Sample.VContainer
{
    public class VContainerSampleGameMode : GameMode
    {
        
        /*
            NOTE: you can not use like this 

            ------------------------------------------------------------------------------------------------------
            [Inject]
            public override void Initialize(in IUnityObjectSpawner objectSpawner, in IWorldSettings worldSettings)
            ------------------------------------------------------------------------------------------------------

            the key word 'in' will cause DI framework resolve failed, you should use 'in' in the method injection.
        */

        //  NOTE: In VContainer, we use the 'Inject' attribute to inject the dependencies, not call base.Initialize or Initialize out of the class.
        [Inject]
        public override void Initialize(IUnityObjectSpawner objectSpawner, IWorldSettings worldSettings)
        {
            base.Initialize(objectSpawner, worldSettings);
        }
    }
}