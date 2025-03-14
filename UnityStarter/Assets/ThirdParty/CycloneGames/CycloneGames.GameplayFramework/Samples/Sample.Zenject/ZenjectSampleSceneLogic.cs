using CycloneGames.Core;
using UnityEngine;
using Zenject;

namespace CycloneGames.GameplayFramework.Sample.Zenject
{
    public class ZenjectSampleSceneLogic : MonoBehaviour
    {
        [Inject] IGameMode gameMode;
        [Inject] IObjectSpawner objectSpawner;

        void Start()
        {
            gameMode.LaunchGameMode();            
        }
    }
}