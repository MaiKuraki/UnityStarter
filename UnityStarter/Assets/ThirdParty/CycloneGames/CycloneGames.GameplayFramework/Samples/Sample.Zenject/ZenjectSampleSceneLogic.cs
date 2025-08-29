using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using Zenject;

namespace CycloneGames.GameplayFramework.Sample.Zenject
{
    public class ZenjectSampleSceneLogic : MonoBehaviour
    {
        [Inject] IGameMode gameMode;

        void Start()
        {
            LaunchGameModeAsync(this.GetCancellationTokenOnDestroy()).Forget();
        }

        async UniTask LaunchGameModeAsync(CancellationToken cancel)
        {
            await gameMode.LaunchGameModeAsync(cancel);
        }
    }
}