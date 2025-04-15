using UnityEngine;
using CycloneGames.Factory;

namespace CycloneGames.GameplayFramework.Sample.PureUnity
{
    public class UnitySampleBoot : MonoBehaviour
    {
        private IFactory objectSpawner;

        void Start()
        {
            // Init Services First
            UnitySampleGameInstance.Instance.InitializeWorld();
            objectSpawner = new UnitySampleObjectSpawner();

            // This WorldSettings' ScriptableObject is Located at /Samples/Sample.PureUnity/UnitySampleWorldSettings.asset
            // Maybe you should implement your own AssetLoader
            WorldSettings exampleWorldSettings = Resources.Load<WorldSettings>("UnitySampleWorldSettings");
            IGameMode exampleGameMode = ((IFactory<MonoBehaviour, MonoBehaviour>)objectSpawner).Create(exampleWorldSettings.GameModeClass) as IGameMode;
            ((GameMode)exampleGameMode).Initialize(objectSpawner, exampleWorldSettings);
            // Set the GameMode for the World
            UnitySampleGameInstance.Instance.World.SetGameMode((GameMode)exampleGameMode);
            exampleGameMode.LaunchGameMode();
        }
    }
}
