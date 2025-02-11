using CycloneGames.Core;
using UnityEngine;
using Zenject;

namespace CycloneGames.GameplayFramework.Example.Zenject
{
    public class ZenjectExampleSceneLogic : MonoBehaviour
    {
        [Inject] IGameMode gameMode;
        [Inject] IObjectSpawner objectSpawner;

        void Start()
        {
            gameMode.LaunchGameMode();            
        }
    }
}