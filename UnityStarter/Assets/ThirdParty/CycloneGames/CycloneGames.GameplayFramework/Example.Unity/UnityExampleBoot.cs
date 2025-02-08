using UnityEngine;
using CycloneGames.Core;

namespace CycloneGames.GameplayFramework
{
    public class UnityExampleBoot : MonoBehaviour
    {
        private IObjectSpawner objectSpawner;

        void Start()
        {
            // Init Services First
            UnityExampleGameInstance.Instance.InitializeWorld();
            objectSpawner = new UnityExampleObjectSpawner();

            // This WorldSettins' ScriptableObject is Located at /NoDIExample/UnityExampleWorldSettings.asset
            // Maybe you should implement your own AssetLoader
            WorldSettings exampleWorldSettings = Resources.Load<WorldSettings>("UnityExampleWorldSettings");

            IGameMode exampleGameMode = objectSpawner.SpawnObject(exampleWorldSettings.GameModeClass) as IGameMode;
            ((GameMode)exampleGameMode).Initialize(objectSpawner, exampleWorldSettings);
            // Set the GameMode for the World
            UnityExampleGameInstance.Instance.World.SetGameMode((GameMode)exampleGameMode);
            exampleGameMode.LaunchGameMode();
        }
    }
}
