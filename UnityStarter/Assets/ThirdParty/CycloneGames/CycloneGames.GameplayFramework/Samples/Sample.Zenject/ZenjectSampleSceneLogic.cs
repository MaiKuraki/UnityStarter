using UnityEngine;
using Zenject;

namespace CycloneGames.GameplayFramework.Sample.Zenject
{
    public class ZenjectSampleSceneLogic : MonoBehaviour
    {
        [Inject] IGameMode gameMode;

        void Start()
        {
            gameMode.LaunchGameMode();            
        }
    }
}